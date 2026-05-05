using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;
using FamilyTree.Infrastructure.Persistence.Repositories;

namespace FamilyTree.Infrastructure.Tests;

public class MarriageRepositoryTests
{
    private static async Task<(Person a, Person b)> SeedSpousesAsync(PersonRepository personRepo)
    {
        var a = new Person("Alex", "Stone", Gender.Female);
        var b = new Person("Sam", "Stone", Gender.Male);
        await personRepo.AddAsync(a);
        await personRepo.AddAsync(b);
        return (a, b);
    }

    [Fact]
    public async Task RoundTripsMarriage_WhenAdded()
    {
        using var db = TestDb.Create();
        var personRepo = new PersonRepository(db.Context);
        var repo = new MarriageRepository(db.Context);
        var (a, b) = await SeedSpousesAsync(personRepo);
        var marriage = new Marriage(a.Id, b.Id, PartialDate.FromYear(2000));
        await repo.AddAsync(marriage);

        var fetched = await repo.GetByIdAsync(marriage.Id);

        Assert.NotNull(fetched);
        Assert.Equal(a.Id, fetched!.Spouse1Id);
        Assert.Equal(b.Id, fetched.Spouse2Id);
    }

    [Fact]
    public async Task ReturnsMarriage_WhenGetForPersonCalledForEitherSpouse()
    {
        using var db = TestDb.Create();
        var personRepo = new PersonRepository(db.Context);
        var repo = new MarriageRepository(db.Context);
        var (a, b) = await SeedSpousesAsync(personRepo);
        await repo.AddAsync(new Marriage(a.Id, b.Id, PartialDate.FromYear(2000)));

        Assert.Single(await repo.GetForPersonAsync(a.Id));
        Assert.Single(await repo.GetForPersonAsync(b.Id));
    }

    [Fact]
    public async Task PersistsUpdatedDates_WhenUpdateAsyncCalled()
    {
        using var db = TestDb.Create();
        var personRepo = new PersonRepository(db.Context);
        var repo = new MarriageRepository(db.Context);
        var (a, b) = await SeedSpousesAsync(personRepo);
        var marriage = new Marriage(a.Id, b.Id, PartialDate.FromYear(2000));
        await repo.AddAsync(marriage);

        marriage.UpdateDates(PartialDate.FromYear(2000), "Reno", PartialDate.FromYear(2010), MarriageEndReason.Divorce);
        await repo.UpdateAsync(marriage);

        var refetched = await repo.GetByIdAsync(marriage.Id);
        Assert.Equal("Reno", refetched!.StartPlace);
        Assert.Equal(MarriageEndReason.Divorce, refetched.EndReason);
    }

    [Fact]
    public async Task RemovesMarriage_WhenDeleteAsyncCalled()
    {
        using var db = TestDb.Create();
        var personRepo = new PersonRepository(db.Context);
        var repo = new MarriageRepository(db.Context);
        var (a, b) = await SeedSpousesAsync(personRepo);
        var marriage = new Marriage(a.Id, b.Id, PartialDate.FromYear(2000));
        await repo.AddAsync(marriage);

        await repo.DeleteAsync(marriage.Id);

        Assert.Null(await repo.GetByIdAsync(marriage.Id));
    }
}
