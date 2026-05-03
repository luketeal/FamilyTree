using FamilyTree.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Persistence;

public class FamilyTreeDbContext : DbContext
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<BiologicalParentChild> BiologicalParentChildLinks => Set<BiologicalParentChild>();
    public DbSet<AdoptiveParentChild> AdoptiveParentChildLinks => Set<AdoptiveParentChild>();
    public DbSet<Marriage> Marriages => Set<Marriage>();
    public DbSet<StepparentRelationship> StepparentRelationships => Set<StepparentRelationship>();

    public FamilyTreeDbContext(DbContextOptions<FamilyTreeDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FamilyTreeDbContext).Assembly);
    }
}
