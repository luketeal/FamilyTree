namespace FamilyTree.Application.Services;

public sealed record StorageDurability(bool Supported, bool Persistent);

/// <summary>
/// Whole-tree operations that sit outside the per-entity repositories: seeding,
/// clearing, and asking the browser to keep the data.
/// </summary>
/// <remarks>
/// Components depend on this rather than on a storage class, so the UI never
/// names IndexedDB. That is what keeps ADR-004's swap honest — an API-backed
/// implementation replaces this alongside the repositories, and nothing above
/// changes. It also keeps these operations atomic: replacing the tree is one
/// call here rather than a sequence the caller could half-apply.
/// </remarks>
public interface ITreeDataAdministration
{
    /// <summary>Replaces the entire tree with the demonstration family.</summary>
    Task LoadSampleFamilyAsync(CancellationToken ct = default);

    /// <summary>Removes everything.</summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Asks the browser to exempt this data from routine eviction and reports
    /// what it decided. Safe to call more than once.
    /// </summary>
    Task<StorageDurability> EnsureDurableAsync(CancellationToken ct = default);
}
