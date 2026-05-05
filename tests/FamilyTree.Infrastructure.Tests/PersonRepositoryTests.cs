using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Infrastructure.Persistence.Repositories;

namespace FamilyTree.Infrastructure.Tests;

public class PersonRepositoryTests
{
    [Fact]
    public async Task RoundTripsPerson_WhenAddedAndQueriedById()
    {
        using var db = TestDb.Create();
        var repo = new PersonRepository(db.Context);
        var person = new Person("Ada", "Lovelace", Gender.Female);

        await repo.AddAsync(person);
        var fetched = await repo.GetByIdAsync(person.Id);

        Assert.NotNull(fetched);
        Assert.Equal("Ada", fetched!.FirstName);
        Assert.Equal("Lovelace", fetched.LastName);
    }

    [Fact]
    public async Task ReturnsAllPersons_WhenGetAllCalled()
    {
        using var db = TestDb.Create();
        var repo = new PersonRepository(db.Context);
        await repo.AddAsync(new Person("Ada", "Lovelace", Gender.Female));
        await repo.AddAsync(new Person("Alan", "Turing", Gender.Male));

        var all = await repo.GetAllAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task RemovesPerson_WhenDeleteAsyncCalled()
    {
        using var db = TestDb.Create();
        var repo = new PersonRepository(db.Context);
        var person = new Person("Ada", "Lovelace", Gender.Female);
        await repo.AddAsync(person);

        await repo.DeleteAsync(person.Id);

        Assert.Null(await repo.GetByIdAsync(person.Id));
    }

    [Fact]
    public async Task ReturnsTrue_WhenExistsAsyncCalledForExistingPerson()
    {
        using var db = TestDb.Create();
        var repo = new PersonRepository(db.Context);
        var person = new Person("Ada", "Lovelace", Gender.Female);
        await repo.AddAsync(person);

        Assert.True(await repo.ExistsAsync(person.Id));
    }

    [Fact]
    public async Task ReturnsFalse_WhenExistsAsyncCalledForUnknownId()
    {
        using var db = TestDb.Create();
        var repo = new PersonRepository(db.Context);

        Assert.False(await repo.ExistsAsync(Guid.NewGuid()));
    }
}
