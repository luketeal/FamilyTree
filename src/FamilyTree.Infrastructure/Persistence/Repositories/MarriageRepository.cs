using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Persistence.Repositories;

public sealed class MarriageRepository : IMarriageRepository
{
    private readonly FamilyTreeDbContext _db;

    public MarriageRepository(FamilyTreeDbContext db) => _db = db;

    public Task<Marriage?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Marriages.FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<IReadOnlyList<Marriage>> GetForPersonAsync(Guid personId, CancellationToken ct = default)
        => await _db.Marriages
            .Where(m => m.Spouse1Id == personId || m.Spouse2Id == personId)
            .ToListAsync(ct);

    public async Task AddAsync(Marriage marriage, CancellationToken ct = default)
    {
        await _db.Marriages.AddAsync(marriage, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Marriage marriage, CancellationToken ct = default)
    {
        _db.Marriages.Update(marriage);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var marriage = await _db.Marriages.FindAsync([id], ct);
        if (marriage is null) return;
        _db.Marriages.Remove(marriage);
        await _db.SaveChangesAsync(ct);
    }
}
