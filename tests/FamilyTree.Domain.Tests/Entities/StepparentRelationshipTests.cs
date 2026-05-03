using FamilyTree.Domain.Entities;

namespace FamilyTree.Domain.Tests.Entities;

public sealed class StepparentRelationshipTests
{
    private static readonly Guid StepparentId = Guid.NewGuid();
    private static readonly Guid StepchildId = Guid.NewGuid();
    private static readonly Guid MarriageId = Guid.NewGuid();

    [Fact]
    public void Constructor_SetsStepparentId()
    {
        var rel = new StepparentRelationship(StepparentId, StepchildId, MarriageId);
        Assert.Equal(StepparentId, rel.StepparentId);
    }

    [Fact]
    public void Constructor_SetsStepchildId()
    {
        var rel = new StepparentRelationship(StepparentId, StepchildId, MarriageId);
        Assert.Equal(StepchildId, rel.StepchildId);
    }

    [Fact]
    public void Constructor_SetsMarriageId()
    {
        var rel = new StepparentRelationship(StepparentId, StepchildId, MarriageId);
        Assert.Equal(MarriageId, rel.MarriageId);
    }

    [Fact]
    public void Constructor_AssignsNonEmptyId()
    {
        var rel = new StepparentRelationship(StepparentId, StepchildId, MarriageId);
        Assert.NotEqual(Guid.Empty, rel.Id);
    }

    [Fact]
    public void Constructor_Throws_WhenStepparentIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new StepparentRelationship(Guid.Empty, StepchildId, MarriageId));
    }

    [Fact]
    public void Constructor_Throws_WhenStepchildIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new StepparentRelationship(StepparentId, Guid.Empty, MarriageId));
    }

    [Fact]
    public void Constructor_Throws_WhenMarriageIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new StepparentRelationship(StepparentId, StepchildId, Guid.Empty));
    }

    [Fact]
    public void Constructor_Throws_WhenStepparentAndStepchildAreSamePerson()
    {
        Assert.Throws<ArgumentException>(() => new StepparentRelationship(StepparentId, StepparentId, MarriageId));
    }
}
