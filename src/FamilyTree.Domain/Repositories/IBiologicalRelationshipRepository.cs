using FamilyTree.Domain.Entities;

namespace FamilyTree.Domain.Repositories;

public interface IBiologicalRelationshipRepository
{
    Task<IReadOnlyList<BiologicalParentChild>> GetParentLinksForChildAsync(Guid childId, CancellationToken ct = default);
    Task<IReadOnlyList<BiologicalParentChild>> GetChildLinksForParentAsync(Guid parentId, CancellationToken ct = default);
    Task<BiologicalParentChild?> GetAsync(Guid parentId, Guid childId, CancellationToken ct = default);
    Task AddAsync(BiologicalParentChild link, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
