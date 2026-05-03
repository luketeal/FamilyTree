using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FamilyTree.Infrastructure.Persistence;

/// <summary>
/// Used by EF Core tooling (dotnet ef migrations add) at design time.
/// Not used at runtime — the Web project registers the real DbContext.
/// </summary>
public sealed class FamilyTreeDbContextFactory : IDesignTimeDbContextFactory<FamilyTreeDbContext>
{
    public FamilyTreeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FamilyTreeDbContext>()
            .UseSqlite("Data Source=familytree-design.db")
            .Options;

        return new FamilyTreeDbContext(options);
    }
}
