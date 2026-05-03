using FamilyTree.Domain.Entities;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Domain.Tests.Entities;

public sealed class AdoptiveParentChildTests
{
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly Guid ChildId = Guid.NewGuid();

    [Fact]
    public void Constructor_SetsParentId()
    {
        var link = new AdoptiveParentChild(ParentId, ChildId);
        Assert.Equal(ParentId, link.ParentId);
    }

    [Fact]
    public void Constructor_SetsChildId()
    {
        var link = new AdoptiveParentChild(ParentId, ChildId);
        Assert.Equal(ChildId, link.ChildId);
    }

    [Fact]
    public void Constructor_LeavesAdoptionDateNull_ByDefault()
    {
        var link = new AdoptiveParentChild(ParentId, ChildId);
        Assert.Null(link.AdoptionDate);
    }

    [Fact]
    public void Constructor_SetsAdoptionDate_WhenProvided()
    {
        var date = PartialDate.FromYear(1995);
        var link = new AdoptiveParentChild(ParentId, ChildId, date);
        Assert.Equal(date, link.AdoptionDate);
    }

    [Fact]
    public void Constructor_AssignsNonEmptyId()
    {
        var link = new AdoptiveParentChild(ParentId, ChildId);
        Assert.NotEqual(Guid.Empty, link.Id);
    }

    [Fact]
    public void Constructor_Throws_WhenParentIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new AdoptiveParentChild(Guid.Empty, ChildId));
    }

    [Fact]
    public void Constructor_Throws_WhenChildIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new AdoptiveParentChild(ParentId, Guid.Empty));
    }

    [Fact]
    public void Constructor_Throws_WhenParentAndChildAreSamePerson()
    {
        Assert.Throws<ArgumentException>(() => new AdoptiveParentChild(ParentId, ParentId));
    }

    [Fact]
    public void UpdateAdoptionDate_SetsNewDate()
    {
        var link = new AdoptiveParentChild(ParentId, ChildId);
        var date = PartialDate.FromYearMonth(2000, 6);
        link.UpdateAdoptionDate(date);
        Assert.Equal(date, link.AdoptionDate);
    }

    [Fact]
    public void UpdateAdoptionDate_ClearsDate_WhenNull()
    {
        var link = new AdoptiveParentChild(ParentId, ChildId, PartialDate.FromYear(2000));
        link.UpdateAdoptionDate(null);
        Assert.Null(link.AdoptionDate);
    }
}
