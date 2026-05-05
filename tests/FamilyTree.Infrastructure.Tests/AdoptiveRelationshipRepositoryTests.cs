using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;
using FamilyTree.Infrastructure.Persistence.Repositories;

namespace FamilyTree.Infrastructure.Tests;

public class AdoptiveRelationshipRepositoryTests
{
    private static async Task<(Person parent, Person child)> SeedParentAndChildAsync(PersonRepository personRepo)
    {
        var parent = new Person("Parent", "Test", Gender.Female);
        var child = new Person("Child", "Test", Gender.Male);
        await personRepo.AddAsync(parent);
        await personRepo.AddAsync(child);
        return (parent, child);
    }

    [Fact]
    public async Task RoundTripsLink_WhenAdded()
    {
        using var db = TestDb.Create();
        var personRepo = new PersonRepository(db.Context);
        var repo = new AdoptiveRelationshipRepository(db.Context);
        var (parent, child) = await SeedParentAndChildAsync(personRepo);
        await repo.AddAsync(new AdoptiveParentChild(parent.Id, child.Id, PartialDate.FromYear(1990)));

        var link = await repo.GetAsync(parent.Id, child.Id);

        Assert.NotNull(link);
        Assert.Equal(1990, link!.AdoptionDate!.Year);
    }

    [Fact]
    public async Task ReturnsLinkForChild_WhenLinkAdded()
    {
        using var db = TestDb.Create();
        var personRepo = new PersonRepository(db.Context);
        var repo = new AdoptiveRelationshipRepository(db.Context);
        var (parent, child) = await SeedParentAndChildAsync(personRepo);
        await repo.AddAsync(new AdoptiveParentChild(parent.Id, child.Id));

        var links = await repo.GetParentLinksForChildAsync(child.Id);

        Assert.Single(links);
    }

    [Fact]
    public async Task ReturnsLinkForParent_WhenLinkAdded()
    {
        using var db = TestDb.Create();
        var personRepo = new PersonRepository(db.Context);
        var repo = new AdoptiveRelationshipRepository(db.Context);
        var (parent, child) = await SeedParentAndChildAsync(personRepo);
        await repo.AddAsync(new AdoptiveParentChild(parent.Id, child.Id));

        var links = await repo.GetChildLinksForParentAsync(parent.Id);

        Assert.Single(links);
    }

    [Fact]
    public async Task PersistsUpdatedAdoptionDate_WhenUpdateAsyncCalled()
    {
        using var db = TestDb.Create();
        var personRepo = new PersonRepository(db.Context);
        var repo = new AdoptiveRelationshipRepository(db.Context);
        var (parent, child) = await SeedParentAndChildAsync(personRepo);
        var link = new AdoptiveParentChild(parent.Id, child.Id, PartialDate.FromYear(1990));
        await repo.AddAsync(link);

        link.UpdateAdoptionDate(PartialDate.FromYearMonth(1992, 6));
        await repo.UpdateAsync(link);

        var refetched = await repo.GetAsync(parent.Id, child.Id);
        Assert.Equal(1992, refetched!.AdoptionDate!.Year);
        Assert.Equal(6, refetched.AdoptionDate.Month);
    }

    [Fact]
    public async Task RemovesLink_WhenDeleteAsyncCalled()
    {
        using var db = TestDb.Create();
        var personRepo = new PersonRepository(db.Context);
        var repo = new AdoptiveRelationshipRepository(db.Context);
        var (parent, child) = await SeedParentAndChildAsync(personRepo);
        var link = new AdoptiveParentChild(parent.Id, child.Id);
        await repo.AddAsync(link);

        await repo.DeleteAsync(link.Id);

        Assert.Null(await repo.GetAsync(parent.Id, child.Id));
    }
}
