using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;

namespace FamilyTree.Domain.Tests.Entities;

public sealed class BiologicalParentChildTests
{
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly Guid ChildId = Guid.NewGuid();

    [Fact]
    public void Constructor_SetsParentId()
    {
        var link = new BiologicalParentChild(ParentId, ChildId);
        Assert.Equal(ParentId, link.ParentId);
    }

    [Fact]
    public void Constructor_SetsChildId()
    {
        var link = new BiologicalParentChild(ParentId, ChildId);
        Assert.Equal(ChildId, link.ChildId);
    }

    [Fact]
    public void Constructor_AssignsNonEmptyId()
    {
        var link = new BiologicalParentChild(ParentId, ChildId);
        Assert.NotEqual(Guid.Empty, link.Id);
    }

    [Fact]
    public void Constructor_Throws_WhenParentIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new BiologicalParentChild(Guid.Empty, ChildId));
    }

    [Fact]
    public void Constructor_Throws_WhenChildIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new BiologicalParentChild(ParentId, Guid.Empty));
    }

    [Fact]
    public void Constructor_Throws_WhenParentAndChildAreSamePerson()
    {
        Assert.Throws<ArgumentException>(() => new BiologicalParentChild(ParentId, ParentId));
    }

    // ── Certainty ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultsCertaintyToConfirmed()
    {
        var link = new BiologicalParentChild(ParentId, ChildId);
        Assert.Equal(RelationshipCertainty.Confirmed, link.Certainty);
    }

    [Fact]
    public void Constructor_SetsCertainty_WhenProvided()
    {
        var link = new BiologicalParentChild(ParentId, ChildId, RelationshipCertainty.Speculative);
        Assert.Equal(RelationshipCertainty.Speculative, link.Certainty);
    }

    [Fact]
    public void UpdateCertainty_ChangesCertainty()
    {
        var link = new BiologicalParentChild(ParentId, ChildId);
        link.UpdateCertainty(RelationshipCertainty.Likely);
        Assert.Equal(RelationshipCertainty.Likely, link.Certainty);
    }
}
