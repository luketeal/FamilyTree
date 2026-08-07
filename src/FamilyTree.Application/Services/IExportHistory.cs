namespace FamilyTree.Application.Services;

/// <summary>
/// Remembers when this device last wrote a backup.
/// </summary>
/// <remarks>
/// Deliberately not part of the tree. This is a fact about a browser, not about
/// a family: two devices holding the same tree have genuinely different answers,
/// and a server-backed implementation of the repositories would not own this at
/// all. Keeping it behind its own interface is what stops it being swept into
/// the exported payload, where it would be wrong the moment the file moved.
///
/// It is also why nothing here fails loudly: a browser in private mode can
/// refuse the write, and losing the reminder must never cost the user the
/// export itself.
/// </remarks>
public interface IExportHistory
{
    /// <summary>When this browser last exported, or null if it never has.</summary>
    Task<DateTimeOffset?> GetLastExportAsync(CancellationToken ct = default);

    Task RecordExportAsync(DateTimeOffset moment, CancellationToken ct = default);
}

/// <summary>
/// How overdue a backup is, given what is in the tree.
/// </summary>
/// <remarks>
/// The prompt is a product requirement rather than a nicety: the browser holds
/// the only copy, and a user who has never exported is one cleared cache from
/// losing years of research with no warning that it was possible.
/// </remarks>
public sealed record BackupStatus(DateTimeOffset? LastExport, int DaysSince, bool IsStale, bool HasData)
{
    /// <summary>
    /// A week. Long enough that somebody who exports after each session is never
    /// nagged; short enough that a lost tree costs one sitting's work, not a year's.
    /// </summary>
    public const int StaleAfterDays = 7;

    public static BackupStatus For(DateTimeOffset? lastExport, DateTimeOffset now, bool hasData)
    {
        if (!hasData)
        {
            return new BackupStatus(lastExport, 0, IsStale: false, HasData: false);
        }

        if (lastExport is not { } last)
        {
            return new BackupStatus(null, 0, IsStale: true, HasData: true);
        }

        // Whole days, floored, and never negative: a clock that moved backwards
        // between the export and now must not read as "exported in -3 days".
        var days = (int)Math.Max(0, Math.Floor((now - last).TotalDays));
        return new BackupStatus(last, days, days >= StaleAfterDays, HasData: true);
    }

    /// <summary>"Last exported 3 days ago", "Last exported today", "Never exported".</summary>
    public string Describe() => (HasData, LastExport, DaysSince) switch
    {
        (_, null, _) => "Never exported",
        (_, _, 0) => "Last exported today",
        (_, _, 1) => "Last exported yesterday",
        (_, _, var days) => $"Last exported {days} days ago",
    };
}
