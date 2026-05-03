using FamilyTree.Domain.Entities;

namespace FamilyTree.Domain.Repositories;

public interface IAdoptiveRelationshipRepository
{
    Task<IReadOnlyList<AdoptiveParentChild>> GetParentLinksForChildAsync(Guid childId, CancellationToken ct = default);
    Task<IReadOnlyList<AdoptiveParentChild>> GetChildLinksForParentAsync(Guid parentId, CancellationToken ct = default);
    Task<AdoptiveParentChild?> GetAsync(Guid parentId, Guid childId, CancellationToken ct = default);
    Task AddAsync(AdoptiveParentChild link, CancellationToken ct = default);
    Task UpdateAsync(AdoptiveParentChild link, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
