using FamilyTree.Application.Common;

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

    /// <summary>
    /// Replaces the entire tree with the given records, atomically.
    /// </summary>
    /// <remarks>
    /// Import's write path. It is one call rather than four repository writes
    /// for the reason this interface exists: a sequence can half-apply, and a
    /// half-applied restore leaves relationships pointing at people who were
    /// never written — which looks like corruption rather than a failed import.
    /// Over a future HTTP-backed implementation it is also one request instead
    /// of four that can each fail separately.
    /// </remarks>
    Task ReplaceAllAsync(TreeSnapshot snapshot, CancellationToken ct = default);

    /// <summary>Removes everything.</summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Asks the browser to exempt this data from routine eviction and reports
    /// what it decided. Safe to call more than once.
    /// </summary>
    Task<StorageDurability> EnsureDurableAsync(CancellationToken ct = default);
}
