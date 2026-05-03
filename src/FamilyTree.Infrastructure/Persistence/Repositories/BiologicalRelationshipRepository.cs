using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Persistence.Repositories;

public sealed class BiologicalRelationshipRepository : IBiologicalRelationshipRepository
{
    private readonly FamilyTreeDbContext _db;

    public BiologicalRelationshipRepository(FamilyTreeDbContext db) => _db = db;

    public async Task<IReadOnlyList<BiologicalParentChild>> GetParentLinksForChildAsync(Guid childId, CancellationToken ct = default)
        => await _db.BiologicalParentChildLinks
            .Where(l => l.ChildId == childId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BiologicalParentChild>> GetChildLinksForParentAsync(Guid parentId, CancellationToken ct = default)
        => await _db.BiologicalParentChildLinks
            .Where(l => l.ParentId == parentId)
            .ToListAsync(ct);

    public Task<BiologicalParentChild?> GetAsync(Guid parentId, Guid childId, CancellationToken ct = default)
        => _db.BiologicalParentChildLinks
            .FirstOrDefaultAsync(l => l.ParentId == parentId && l.ChildId == childId, ct);

    public async Task AddAsync(BiologicalParentChild link, CancellationToken ct = default)
    {
        await _db.BiologicalParentChildLinks.AddAsync(link, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var link = await _db.BiologicalParentChildLinks.FindAsync([id], ct);
        if (link is null) return;
        _db.BiologicalParentChildLinks.Remove(link);
        await _db.SaveChangesAsync(ct);
    }
}
