using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Persistence.Repositories;

public sealed class AdoptiveRelationshipRepository : IAdoptiveRelationshipRepository
{
    private readonly FamilyTreeDbContext _db;

    public AdoptiveRelationshipRepository(FamilyTreeDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdoptiveParentChild>> GetParentLinksForChildAsync(Guid childId, CancellationToken ct = default)
        => await _db.AdoptiveParentChildLinks
            .Where(l => l.ChildId == childId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AdoptiveParentChild>> GetChildLinksForParentAsync(Guid parentId, CancellationToken ct = default)
        => await _db.AdoptiveParentChildLinks
            .Where(l => l.ParentId == parentId)
            .ToListAsync(ct);

    public Task<AdoptiveParentChild?> GetAsync(Guid parentId, Guid childId, CancellationToken ct = default)
        => _db.AdoptiveParentChildLinks
            .FirstOrDefaultAsync(l => l.ParentId == parentId && l.ChildId == childId, ct);

    public async Task AddAsync(AdoptiveParentChild link, CancellationToken ct = default)
    {
        await _db.AdoptiveParentChildLinks.AddAsync(link, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(AdoptiveParentChild link, CancellationToken ct = default)
    {
        _db.AdoptiveParentChildLinks.Update(link);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var link = await _db.AdoptiveParentChildLinks.FindAsync([id], ct);
        if (link is null) return;
        _db.AdoptiveParentChildLinks.Remove(link);
        await _db.SaveChangesAsync(ct);
    }
}
