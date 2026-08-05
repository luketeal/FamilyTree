using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.Repositories;
using Moq;

namespace FamilyTree.Application.Tests.Services;

public class TreeStatsServiceTests
{
    private readonly Mock<IPersonRepository> _people = new();
    private readonly Mock<IBiologicalRelationshipRepository> _biological = new();
    private readonly Mock<IAdoptiveRelationshipRepository> _adoptive = new();
    private readonly Mock<IMarriageRepository> _marriages = new();

    private TreeStatsService CreateService(params Person[] people)
    {
        _people.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(people);
        _biological.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _adoptive.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _marriages.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        return new TreeStatsService(
            _people.Object, _biological.Object, _adoptive.Object, _marriages.Object);
    }

    [Fact]
    public async Task ReportsEmpty_WhenNothingIsStored()
    {
        var stats = await CreateService().GetAsync();

        Assert.True(stats.Value!.IsEmpty);
    }

    [Fact]
    public async Task DoesNotCountPhantomsAsPeople()
    {
        var stats = await CreateService(Person.CreatePhantom()).GetAsync();

        Assert.Equal(0, stats.Value!.People);
    }

    // The distinction that keeps a destructive action honest: a tree of nothing
    // but unidentified ancestors displays as empty, but a wipe would still
    // destroy records. Anything deciding whether there is something to lose has
    // to see through the display count.
    [Fact]
    public async Task IsNotEmpty_WhenOnlyPhantomsAreStored()
    {
        var stats = await CreateService(Person.CreatePhantom()).GetAsync();

        Assert.False(stats.Value!.IsEmpty);
    }

    [Fact]
    public async Task CountsStoredRecordsIncludingPhantoms()
    {
        var stats = await CreateService(
            new Person("Ada", "Lovelace", Gender.Female),
            Person.CreatePhantom()).GetAsync();

        Assert.Equal(2, stats.Value!.StoredRecords);
        Assert.Equal(1, stats.Value!.People);
    }
}
