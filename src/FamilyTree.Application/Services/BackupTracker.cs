namespace FamilyTree.Application.Services;

/// <summary>When this browser last wrote a backup, and when its tree last changed.</summary>
public sealed record BackupState(DateTimeOffset? LastExport, DateTimeOffset? LastChange);

/// <summary>
/// Remembers, for this browser, whether the tree in it has been written to a file.
/// </summary>
/// <remarks>
/// Deliberately not part of the tree. These are facts about a device, not about a
/// family: two browsers holding the same tree have genuinely different answers,
/// and a server-backed implementation of the repositories would not own them at
/// all. Keeping them behind their own interface is what stops them being swept
/// into the exported payload, where they would be wrong the moment the file moved.
///
/// It is also why nothing here fails loudly: a browser in private mode can refuse
/// the write, and losing the reminder must never cost the user the export itself.
/// </remarks>
public interface IBackupJournal
{
    /// <summary>Both timestamps in one read, so nothing compares two different moments.</summary>
    Task<BackupState> ReadAsync(CancellationToken ct = default);

    Task RecordExportAsync(DateTimeOffset moment, CancellationToken ct = default);

    Task RecordChangeAsync(DateTimeOffset moment, CancellationToken ct = default);
}

/// <summary>
/// Whether the tree holds work that is not in any file.
/// </summary>
/// <remarks>
/// The question is "is there anything unsaved", not "how long has it been". An
/// elapsed-time rule gets both cases backwards: it nags somebody who exported and
/// then did nothing, and stays quiet for somebody who exported and then entered
/// fifty people. The elapsed days are still reported, because "last backed up
/// three days ago" is useful context — they are just not what decides.
/// </remarks>
public sealed record BackupStatus(
    DateTimeOffset? LastExport,
    DateTimeOffset? LastChange,
    int DaysSinceExport,
    bool HasUnsavedChanges,
    bool HasData)
{
    public static BackupStatus None { get; } = new(null, null, 0, false, false);

    /// <summary>Nothing to warn about on a tree with nothing in it.</summary>
    public bool ShouldPrompt => HasData && HasUnsavedChanges;

    public static BackupStatus For(BackupState state, DateTimeOffset now, bool hasData)
    {
        // Whole days, floored, and never negative: a clock that moved backwards
        // between the export and now must not read as "exported in -3 days".
        var days = state.LastExport is { } exported
            ? (int)Math.Max(0, Math.Floor((now - exported).TotalDays))
            : 0;

        var unsaved = state.LastExport is not { } lastExport
            // Never exported. Any tree with records in it is unprotected.
            ? true
            // A change with no recorded time cannot be shown to be newer than the
            // export, and treating it as newer would make the banner permanent for
            // anyone whose browser refused the write. Silence is the wrong failure
            // here, but a warning that can never be cleared is worse — it trains
            // people to ignore the one that matters.
            : state.LastChange is { } lastChange && lastChange > lastExport;

        return new BackupStatus(state.LastExport, state.LastChange, days, unsaved, hasData);
    }

    /// <summary>"Last backed up 3 days ago", "Last backed up today", "Never exported".</summary>
    public string Describe() => (LastExport, DaysSinceExport) switch
    {
        (null, _) => "Never exported",
        (_, 0) => "Last exported today",
        (_, 1) => "Last exported yesterday",
        (_, var days) => $"Last exported {days} days ago",
    };
}

/// <summary>
/// Records every change to the tree, so the app can tell whether the current
/// contents have ever been written to a file.
/// </summary>
/// <remarks>
/// The change is recorded from <see cref="TreeDataNotifier"/>'s before-notifying
/// hook rather than from a subscriber. Subscribers are awaited together, so a
/// subscriber that wrote the timestamp would be racing the ones that read it, and
/// the reminder would sometimes redraw from the state that existed before the
/// change it is reacting to.
///
/// This is also the reason the tracker registers in its constructor: something has
/// to bring it into existence before the first mutation, which is why it is
/// injected by a component that renders on every page.
/// </remarks>
public sealed class BackupTracker
{
    private readonly IBackupJournal _journal;
    private readonly TimeProvider _clock;

    public BackupTracker(IBackupJournal journal, TimeProvider clock, TreeDataNotifier notifier)
    {
        _journal = journal;
        _clock = clock;

        notifier.BeforeNotifying(() => _journal.RecordChangeAsync(_clock.GetUtcNow()));
    }

    public async Task<BackupStatus> GetStatusAsync(bool hasData, CancellationToken ct = default) =>
        BackupStatus.For(await _journal.ReadAsync(ct), _clock.GetUtcNow(), hasData);

    /// <summary>
    /// Notes that the tree as it currently stands has been written to a file.
    /// </summary>
    /// <remarks>
    /// Called after the browser has been handed the file, never before. Marking a
    /// tree as backed up on the strength of a download that did not happen is the
    /// one bug in here that would cost somebody their data.
    /// </remarks>
    public Task RecordExportAsync(DateTimeOffset moment, CancellationToken ct = default) =>
        _journal.RecordExportAsync(moment, ct);
}
