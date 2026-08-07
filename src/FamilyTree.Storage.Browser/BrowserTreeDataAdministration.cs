using System.Globalization;
using FamilyTree.Application.Common;
using FamilyTree.Application.Services;
using FamilyTree.Storage.Browser.Records;

namespace FamilyTree.Storage.Browser;

/// <summary>Browser-backed implementation of the whole-tree operations.</summary>
public sealed class BrowserTreeDataAdministration(IndexedDbStore store) : ITreeDataAdministration
{
    public Task LoadSampleFamilyAsync(CancellationToken ct = default) =>
        store.ReplaceAllAsync(SampleFamily.Build(), ct);

    /// <summary>
    /// Maps the application's snapshot onto the flat storage records and writes
    /// them in one transaction.
    /// </summary>
    /// <remarks>
    /// Stepparent links are written as empty because nothing produces them yet
    /// (PR 9 adds the repository). The store clears every object store as part
    /// of the same transaction, so once stepparent links can exist, omitting
    /// them here would make every import delete them. They have to be added to
    /// <see cref="TreeSnapshot"/> and to this call in that same PR.
    /// </remarks>
    public Task ReplaceAllAsync(TreeSnapshot snapshot, CancellationToken ct = default) =>
        store.ReplaceAllAsync(
            new DatasetRecord(
                snapshot.People.Select(PersonRecord.From).ToList(),
                snapshot.BiologicalLinks.Select(BiologicalLinkRecord.From).ToList(),
                snapshot.AdoptiveLinks.Select(AdoptiveLinkRecord.From).ToList(),
                snapshot.Marriages.Select(MarriageRecord.From).ToList(),
                []),
            ct);

    public Task ClearAsync(CancellationToken ct = default) =>
        store.ClearAllAsync(ct);

    public async Task<StorageDurability> EnsureDurableAsync(CancellationToken ct = default)
    {
        var status = await store.RequestPersistenceAsync(ct);
        return new StorageDurability(status.Supported, status.Persisted);
    }
}

/// <summary>
/// Stores the backup timestamps in this browser's localStorage.
/// </summary>
/// <remarks>
/// localStorage rather than an IndexedDB store, because replacing the whole
/// dataset — a sample load, a restore — must not also rewrite when this browser
/// last made a backup. Round-tripped as ISO 8601 ("O") so a value survives a
/// change of machine time zone unchanged and reads back as the same instant a
/// year later regardless of locale.
///
/// Both timestamps are read in one call so the comparison between them is never
/// made across two different moments.
/// </remarks>
public sealed class BrowserBackupJournal(IndexedDbStore store) : IBackupJournal
{
    private const string ExportKey = "familytree.lastExportedAt";
    private const string ChangeKey = "familytree.lastChangedAt";

    public async Task<BackupState> ReadAsync(CancellationToken ct = default) => new(
        Parse(await store.ReadSettingAsync(ExportKey, ct)),
        Parse(await store.ReadSettingAsync(ChangeKey, ct)));

    public Task RecordExportAsync(DateTimeOffset moment, CancellationToken ct = default) =>
        WriteAsync(ExportKey, moment, ct);

    public Task RecordChangeAsync(DateTimeOffset moment, CancellationToken ct = default) =>
        WriteAsync(ChangeKey, moment, ct);

    private Task WriteAsync(string key, DateTimeOffset moment, CancellationToken ct) =>
        store.WriteSettingAsync(key, moment.ToString("O", CultureInfo.InvariantCulture), ct);

    private static DateTimeOffset? Parse(string? raw) => DateTimeOffset.TryParse(
        raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
        ? parsed
        : null;
}
