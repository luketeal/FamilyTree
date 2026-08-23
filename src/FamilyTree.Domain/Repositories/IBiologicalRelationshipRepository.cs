using FamilyTree.Domain.Entities;

namespace FamilyTree.Domain.Repositories;

public interface IBiologicalRelationshipRepository
{
    /// <summary>The whole set, for tree building, counts and export.</summary>
    Task<IReadOnlyList<BiologicalParentChild>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<BiologicalParentChild>> GetParentLinksForChildAsync(Guid childId, CancellationToken ct = default);

    /// <summary>
    /// Parent links for many children at once. Ancestor walks would otherwise
    /// issue one read per node, which is free against IndexedDB and a request
    /// storm against an HTTP-backed implementation of this same interface.
    /// </summary>
    Task<IReadOnlyList<BiologicalParentChild>> GetParentLinksForChildrenAsync(IReadOnlyCollection<Guid> childIds, CancellationToken ct = default);
    Task<IReadOnlyList<BiologicalParentChild>> GetChildLinksForParentAsync(Guid parentId, CancellationToken ct = default);
    Task<BiologicalParentChild?> GetAsync(Guid parentId, Guid childId, CancellationToken ct = default);
    Task AddAsync(BiologicalParentChild link, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Swaps one parent link for another as a single operation.
    /// </summary>
    /// <remarks>
    /// Correcting a wrongly recorded parent (US-009) touches two records, and the
    /// API-seam rule is that a mutation spanning several records is one
    /// repository call so it maps to one request later. Expressed as
    /// delete-then-add it is exactly the half-failing sequence that rule exists
    /// to prevent: the failure leaves the child with no parent on that side and
    /// nothing to say which one was lost.
    /// <para>
    /// <paramref name="oldLinkId"/> is the link's own id, matching
    /// <see cref="DeleteAsync"/>, while <see cref="GetAsync"/> is keyed by the
    /// pair — so a caller holding only (parentId, childId) looks the link up
    /// first.
    /// </para>
    /// </remarks>
    Task ReplaceParentAsync(Guid oldLinkId, BiologicalParentChild newLink, CancellationToken ct = default);
}
