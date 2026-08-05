using FamilyTree.Domain.Entities;

namespace FamilyTree.Domain.Repositories;

public interface IMarriageRepository
{
    /// <summary>The whole set, for tree building, counts and export.</summary>
    Task<IReadOnlyList<Marriage>> GetAllAsync(CancellationToken ct = default);

    Task<Marriage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Marriage>> GetForPersonAsync(Guid personId, CancellationToken ct = default);
    Task AddAsync(Marriage marriage, CancellationToken ct = default);
    Task UpdateAsync(Marriage marriage, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
