using FamilyTree.Domain.Entities;

namespace FamilyTree.Domain.Repositories;

public interface IBiologicalRelationshipRepository
{
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
}
