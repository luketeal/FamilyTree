using FamilyTree.Domain.Entities;

namespace FamilyTree.Domain.Repositories;

public interface IAdoptiveRelationshipRepository
{
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
}
