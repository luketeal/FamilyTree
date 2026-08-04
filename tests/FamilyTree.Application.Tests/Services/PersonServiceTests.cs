using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.Repositories;
using FamilyTree.Domain.ValueObjects;
using Moq;

namespace FamilyTree.Application.Tests.Services;

public class PersonServiceTests
{
    private readonly Mock<IPersonRepository> _people = new();

    private PersonService CreateService()
    {
        _people.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return new PersonService(_people.Object);
    }

    private static PersonService.PersonInput ValidInput(
        string first = "Jane",
        string last = "Smith",
        PartialDate? birth = null,
        PartialDate? death = null) =>
        new(first, last, BirthDate: birth, DeathDate: death, Gender: Gender.Female);

    [Fact]
    public async Task CreatesAPersonFromValidInput()
    {
        var result = await CreateService().CreateAsync(ValidInput());

        Assert.True(result.IsSuccess);
        Assert.Equal("Jane", result.Value!.FirstName);
    }

    [Fact]
    public async Task PersistsTheCreatedPerson()
    {
        await CreateService().CreateAsync(ValidInput());

        _people.Verify(r => r.AddAsync(It.IsAny<Person>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectsPerson_WhenFirstNameIsEmpty(string firstName)
    {
        var result = await CreateService().CreateAsync(ValidInput(first: firstName));

        Assert.False(result.IsSuccess);
        Assert.Equal("First name is required.", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectsPerson_WhenLastNameIsEmpty(string lastName)
    {
        var result = await CreateService().CreateAsync(ValidInput(last: lastName));

        Assert.False(result.IsSuccess);
        Assert.Equal("Last name is required.", result.Error);
    }

    [Fact]
    public async Task DoesNotPersistAnInvalidPerson()
    {
        await CreateService().CreateAsync(ValidInput(first: ""));

        _people.Verify(r => r.AddAsync(It.IsAny<Person>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectsPerson_WhenDeathDateIsBeforeBirthDate()
    {
        var result = await CreateService().CreateAsync(ValidInput(
            birth: PartialDate.FromYear(1950),
            death: PartialDate.FromYear(1940)));

        Assert.False(result.IsSuccess);
        Assert.Equal("Death date cannot be before birth date.", result.Error);
    }

    [Fact]
    public async Task AcceptsPerson_WhenBirthAndDeathAreInTheSameYear()
    {
        var result = await CreateService().CreateAsync(ValidInput(
            birth: PartialDate.FromYear(1950),
            death: PartialDate.FromYear(1950)));

        Assert.True(result.IsSuccess);
    }

    // A duplicate is a warning, not a rejection: two cousins can share a name
    // and a birth year, and blocking that would be wrong about real families.
    [Fact]
    public async Task WarnsButStillSaves_WhenAnIdenticalNameAndBirthYearExists()
    {
        var existing = new Person("Jane", "Smith", Gender.Female);
        existing.UpdateDates(PartialDate.FromYear(1950), null, null, null);
        _people.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([existing]);

        var result = await new PersonService(_people.Object)
            .CreateAsync(ValidInput(birth: PartialDate.FromYear(1950)));

        Assert.True(result.IsSuccess);
        Assert.True(result.IsWarning);
        _people.Verify(r => r.AddAsync(It.IsAny<Person>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoesNotWarn_WhenTheSameNameHasADifferentBirthYear()
    {
        var existing = new Person("Jane", "Smith", Gender.Female);
        existing.UpdateDates(PartialDate.FromYear(1930), null, null, null);
        _people.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([existing]);

        var result = await new PersonService(_people.Object)
            .CreateAsync(ValidInput(birth: PartialDate.FromYear(1950)));

        Assert.True(result.IsSuccess);
        Assert.False(result.IsWarning);
    }

    [Fact]
    public async Task DoesNotTreatAPhantomAsADuplicate()
    {
        _people.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Person.CreatePhantom()]);

        var result = await new PersonService(_people.Object).CreateAsync(ValidInput());

        Assert.False(result.IsWarning);
    }

    [Fact]
    public async Task UpdateReturnsNotFound_ForAnUnknownId()
    {
        _people.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Person?)null);

        var result = await CreateService().UpdateAsync(Guid.NewGuid(), ValidInput());

        Assert.False(result.IsSuccess);
        Assert.Contains("No person with id", result.Error);
    }

    [Fact]
    public async Task DeleteReturnsNotFound_ForAnUnknownId()
    {
        _people.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateService().DeleteAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        _people.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // US-054: phantoms are tree-only placeholders and must never reach a list.
    [Fact]
    public async Task ExcludesPhantomsFromTheList()
    {
        var real = new Person("Jane", "Smith", Gender.Female);
        _people.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([real, Person.CreatePhantom()]);

        var result = await new PersonService(_people.Object).GetAllAsync();

        Assert.Single(result.Value!);
        Assert.Equal(real.Id, result.Value![0].Id);
    }

    [Fact]
    public async Task SortsTheListBySurnameThenFirstName()
    {
        _people.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new Person("Zoe", "Brown", Gender.Female),
            new Person("Adam", "Smith", Gender.Male),
            new Person("Alice", "Brown", Gender.Female),
        ]);

        var result = await new PersonService(_people.Object).GetAllAsync();

        Assert.Equal(
            ["Alice Brown", "Zoe Brown", "Adam Smith"],
            result.Value!.Select(p => p.DisplayName));
    }

    [Fact]
    public async Task CreatesAPhantomThatIsMarkedAsOne()
    {
        Person? captured = null;
        _people.Setup(r => r.AddAsync(It.IsAny<Person>(), It.IsAny<CancellationToken>()))
            .Callback<Person, CancellationToken>((p, _) => captured = p);

        var result = await CreateService().CreatePhantomAsync();

        Assert.True(result.IsSuccess);
        Assert.True(captured!.IsPhantom);
    }
}
