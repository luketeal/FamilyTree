using FamilyTree.Domain.Entities;

namespace FamilyTree.Domain.Repositories;

public interface IMarriageRepository
{
    Task<Marriage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Marriage>> GetForPersonAsync(Guid personId, CancellationToken ct = default);
    Task AddAsync(Marriage marriage, CancellationToken ct = default);
    Task UpdateAsync(Marriage marriage, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
