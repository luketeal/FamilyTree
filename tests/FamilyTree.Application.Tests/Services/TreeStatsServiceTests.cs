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
    private readonly TreeDataNotifier _notifier = new();

    private TreeStatsService CreateService(params Person[] people)
    {
        _people.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(people);
        _biological.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _adoptive.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _marriages.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        return new TreeStatsService(
            _people.Object, _biological.Object, _adoptive.Object, _marriages.Object, _notifier);
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

    // Three components ask for these counts, and each ask is a full read of all
    // four stores — free against IndexedDB, four HTTP round trips per listener
    // once the seam is swapped.
    [Fact]
    public async Task ReadsTheStoresOnceForRepeatedAsks()
    {
        var service = CreateService(new Person("Ada", "Lovelace", Gender.Female));

        await service.GetAsync();
        await service.GetAsync();
        await service.GetAsync();

        _people.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadsAgainAfterTheTreeChanges()
    {
        var service = CreateService(new Person("Ada", "Lovelace", Gender.Female));
        await service.GetAsync();

        await _notifier.NotifyChangedAsync();
        await service.GetAsync();

        _people.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // The ordering the cache depends on. Subscribers are awaited together, so a
    // cache that cleared itself from an ordinary handler would race the handlers
    // that read it and hand some of them the counts from before the change.
    [Fact]
    public async Task SubscribersSeeTheNewCountsWhenTheyAreNotified()
    {
        var service = CreateService(new Person("Ada", "Lovelace", Gender.Female));
        await service.GetAsync();

        _people.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new Person("Ada", "Lovelace", Gender.Female),
            new Person("Grace", "Hopper", Gender.Female),
        ]);

        int? observed = null;
        _notifier.Changed += async () => observed = (await service.GetAsync()).Value!.People;

        await _notifier.NotifyChangedAsync();

        Assert.Equal(2, observed);
    }

    // Another tab on the same origin writes to the same database without raising
    // this app's notifier, so the cache can be honestly stale. The destructive
    // confirmations on Settings and Import decide from these counts whether
    // there is anything to lose, which is the worst possible thing to get wrong.
    [Fact]
    public async Task ReadFreshIgnoresTheCache()
    {
        var service = CreateService(new Person("Ada", "Lovelace", Gender.Female));
        await service.GetAsync();

        _people.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new Person("Ada", "Lovelace", Gender.Female),
            new Person("Grace", "Hopper", Gender.Female),
        ]);

        var stats = await service.ReadFreshAsync();

        Assert.Equal(2, stats.Value!.People);
    }

    [Fact]
    public async Task ReadFreshRefillsTheCache()
    {
        var service = CreateService(new Person("Ada", "Lovelace", Gender.Female));

        await service.ReadFreshAsync();
        await service.GetAsync();

        _people.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
