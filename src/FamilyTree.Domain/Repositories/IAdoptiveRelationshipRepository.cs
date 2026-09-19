using FamilyTree.Domain.Entities;

namespace FamilyTree.Domain.Repositories;

public interface IAdoptiveRelationshipRepository
{
    /// <summary>The whole set, for tree building, counts and export.</summary>
    Task<IReadOnlyList<AdoptiveParentChild>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<AdoptiveParentChild>> GetParentLinksForChildAsync(Guid childId, CancellationToken ct = default);

    /// <summary>
    /// Parent links for many children at once. Ancestor walks would otherwise
    /// issue one read per node, which is free against IndexedDB and a request
    /// storm against an HTTP-backed implementation of this same interface.
    /// </summary>
    Task<IReadOnlyList<AdoptiveParentChild>> GetParentLinksForChildrenAsync(IReadOnlyCollection<Guid> childIds, CancellationToken ct = default);
    Task<IReadOnlyList<AdoptiveParentChild>> GetChildLinksForParentAsync(Guid parentId, CancellationToken ct = default);
    Task<AdoptiveParentChild?> GetAsync(Guid parentId, Guid childId, CancellationToken ct = default);
    Task AddAsync(AdoptiveParentChild link, CancellationToken ct = default);
    Task UpdateAsync(AdoptiveParentChild link, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Swaps one adoptive parent link for another as a single mutation.
    /// </summary>
    /// <remarks>
    /// Exists for the same reason as its biological counterpart: the interface
    /// could not otherwise express "replace" as one call, and a delete followed
    /// by an add is the half-failing sequence the API-seam rule exists to
    /// prevent. Its failure mode is a child whose adoptive parent quietly
    /// vanished with nothing left to say who used to be there, which this
    /// project treats as a defect rather than an edge case.
    /// </remarks>
    Task ReplaceParentAsync(Guid oldLinkId, AdoptiveParentChild newLink, CancellationToken ct = default);
}
