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
/// Stores the last-export timestamp in this browser's localStorage.
/// </summary>
/// <remarks>
/// Round-tripped as a round-trip ISO 8601 string ("O") so the value survives a
/// change of machine time zone unchanged, and reads back as the same instant a
/// year later regardless of locale.
/// </remarks>
public sealed class BrowserExportHistory(IndexedDbStore store) : IExportHistory
{
    private const string Key = "familytree.lastExportedAt";

    public async Task<DateTimeOffset?> GetLastExportAsync(CancellationToken ct = default)
    {
        var raw = await store.ReadSettingAsync(Key, ct);

        return DateTimeOffset.TryParse(
            raw,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    public Task RecordExportAsync(DateTimeOffset moment, CancellationToken ct = default) =>
        store.WriteSettingAsync(
            Key,
            moment.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ct);
}
