using FamilyTree.Application.Services;

namespace FamilyTree.Storage.Browser;

/// <summary>Browser-backed implementation of the whole-tree operations.</summary>
public sealed class BrowserTreeDataAdministration(IndexedDbStore store) : ITreeDataAdministration
{
    public Task LoadSampleFamilyAsync(CancellationToken ct = default) =>
        store.ReplaceAllAsync(SampleFamily.Build(), ct);

    public Task ClearAsync(CancellationToken ct = default) =>
        store.ClearAllAsync(ct);

    public async Task<StorageDurability> EnsureDurableAsync(CancellationToken ct = default)
    {
        var status = await store.RequestPersistenceAsync(ct);
        return new StorageDurability(status.Supported, status.Persisted);
    }
}
