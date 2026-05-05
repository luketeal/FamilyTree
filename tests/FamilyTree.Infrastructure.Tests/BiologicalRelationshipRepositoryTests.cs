using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Infrastructure.Persistence.Repositories;

namespace FamilyTree.Infrastructure.Tests;

public class BiologicalRelationshipRepositoryTests
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
    public async Task ReturnsLinkForChild_WhenLinkAdded()
    {
        using var db = TestDb.Create();
        var personRepo = new PersonRepository(db.Context);
        var repo = new BiologicalRelationshipRepository(db.Context);
        var (parent, child) = await SeedParentAndChildAsync(personRepo);
        await repo.AddAsync(new BiologicalParentChild(parent.Id, child.Id));

        var links = await repo.GetParentLinksForChildAsync(child.Id);

        Assert.Single(links);
        Assert.Equal(parent.Id, links[0].ParentId);
    }

    [Fact]
    public async Task ReturnsLinkForParent_WhenLinkAdded()
    {
        using var db = TestDb.Create();
        var personRepo = new PersonRepository(db.Context);
        var repo = new BiologicalRelationshipRepository(db.Context);
        var (parent, child) = await SeedParentAndChildAsync(personRepo);
        await repo.AddAsync(new BiologicalParentChild(parent.Id, child.Id));

        var links = await repo.GetChildLinksForParentAsync(parent.Id);

        Assert.Single(links);
        Assert.Equal(child.Id, links[0].ChildId);
    }

    [Fact]
    public async Task ReturnsLink_WhenGetCalledWithBothEnds()
    {
        using var db = TestDb.Create();
        var personRepo = new PersonRepository(db.Context);
        var repo = new BiologicalRelationshipRepository(db.Context);
        var (parent, child) = await SeedParentAndChildAsync(personRepo);
        await repo.AddAsync(new BiologicalParentChild(parent.Id, child.Id));

        var link = await repo.GetAsync(parent.Id, child.Id);

        Assert.NotNull(link);
    }

    [Fact]
    public async Task RemovesLink_WhenDeleteAsyncCalled()
    {
        using var db = TestDb.Create();
        var personRepo = new PersonRepository(db.Context);
        var repo = new BiologicalRelationshipRepository(db.Context);
        var (parent, child) = await SeedParentAndChildAsync(personRepo);
        var link = new BiologicalParentChild(parent.Id, child.Id);
        await repo.AddAsync(link);

        await repo.DeleteAsync(link.Id);

        Assert.Null(await repo.GetAsync(parent.Id, child.Id));
    }
}
