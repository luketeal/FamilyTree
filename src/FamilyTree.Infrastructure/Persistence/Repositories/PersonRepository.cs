using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Persistence.Repositories;

public sealed class PersonRepository : IPersonRepository
{
    private readonly FamilyTreeDbContext _db;

    public PersonRepository(FamilyTreeDbContext db) => _db = db;

    public Task<Person?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.People.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Person>> GetAllAsync(CancellationToken ct = default)
        => await _db.People.AsNoTracking().ToListAsync(ct);

    public async Task AddAsync(Person person, CancellationToken ct = default)
    {
        await _db.People.AddAsync(person, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Person person, CancellationToken ct = default)
    {
        _db.People.Update(person);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var person = await _db.People.FindAsync([id], ct);
        if (person is null) return;
        _db.People.Remove(person);
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
        => _db.People.AnyAsync(p => p.Id == id, ct);
}
