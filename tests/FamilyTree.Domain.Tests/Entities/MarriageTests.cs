using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;


namespace FamilyTree.Domain.Tests.Entities;

public sealed class MarriageTests
{
    private static readonly Guid Spouse1Id = Guid.NewGuid();
    private static readonly Guid Spouse2Id = Guid.NewGuid();
    private static readonly PartialDate StartDate = PartialDate.FromYear(2000);

    // ── Constructor ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsSpouse1Id()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        Assert.Equal(Spouse1Id, marriage.Spouse1Id);
    }

    [Fact]
    public void Constructor_SetsSpouse2Id()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        Assert.Equal(Spouse2Id, marriage.Spouse2Id);
    }

    [Fact]
    public void Constructor_SetsStartDate()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        Assert.Equal(StartDate, marriage.StartDate);
    }

    [Fact]
    public void Constructor_LeavesStartPlaceNull_WhenNotProvided()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        Assert.Null(marriage.StartPlace);
    }

    [Fact]
    public void Constructor_SetsStartPlace_WhenProvided()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate, "  Las Vegas, NV  ");
        Assert.Equal("Las Vegas, NV", marriage.StartPlace);
    }

    [Fact]
    public void Constructor_LeavesEndDateAndReasonNull()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        Assert.Null(marriage.EndDate);
        Assert.Null(marriage.EndReason);
    }

    [Fact]
    public void Constructor_AssignsNonEmptyId()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        Assert.NotEqual(Guid.Empty, marriage.Id);
    }

    [Fact]
    public void Constructor_Throws_WhenSpouse1IdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new Marriage(Guid.Empty, Spouse2Id, StartDate));
    }

    [Fact]
    public void Constructor_Throws_WhenSpouse2IdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new Marriage(Spouse1Id, Guid.Empty, StartDate));
    }

    [Fact]
    public void Constructor_Throws_WhenBothSpousesAreTheSamePerson()
    {
        Assert.Throws<ArgumentException>(() => new Marriage(Spouse1Id, Spouse1Id, StartDate));
    }

    [Fact]
    public void Constructor_Throws_WhenStartDateIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new Marriage(Spouse1Id, Spouse2Id, null!));
    }

    // ── IsOngoing ─────────────────────────────────────────────────────────

    [Fact]
    public void IsOngoing_ReturnsTrue_WhenNoEndDateOrReason()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        Assert.True(marriage.IsOngoing);
    }

    [Fact]
    public void IsOngoing_ReturnsFalse_WhenEndDateIsSet()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        marriage.UpdateDates(StartDate, null, PartialDate.FromYear(2010), null);
        Assert.False(marriage.IsOngoing);
    }

    [Fact]
    public void IsOngoing_ReturnsFalse_WhenEndReasonIsSet()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        marriage.UpdateDates(StartDate, null, null, MarriageEndReason.Divorce);
        Assert.False(marriage.IsOngoing);
    }

    // ── UpdateDates ───────────────────────────────────────────────────────

    [Fact]
    public void UpdateDates_SetsNewStartDate()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        var newStart = PartialDate.FromYear(2001);
        marriage.UpdateDates(newStart, null, null, null);
        Assert.Equal(newStart, marriage.StartDate);
    }

    [Fact]
    public void UpdateDates_SetsEndDate()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        var endDate = PartialDate.FromYear(2015);
        marriage.UpdateDates(StartDate, null, endDate, MarriageEndReason.Divorce);
        Assert.Equal(endDate, marriage.EndDate);
    }

    [Fact]
    public void UpdateDates_SetsEndReason()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        marriage.UpdateDates(StartDate, null, PartialDate.FromYear(2015), MarriageEndReason.Annulment);
        Assert.Equal(MarriageEndReason.Annulment, marriage.EndReason);
    }

    [Fact]
    public void UpdateDates_SetsTrimmedStartPlace()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        marriage.UpdateDates(StartDate, "  Paris  ", null, null);
        Assert.Equal("Paris", marriage.StartPlace);
    }

    [Fact]
    public void UpdateDates_Throws_WhenStartDateIsNull()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        Assert.Throws<ArgumentNullException>(() => marriage.UpdateDates(null!, null, null, null));
    }

    // ── Certainty ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultsCertaintyToConfirmed()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        Assert.Equal(RelationshipCertainty.Confirmed, marriage.Certainty);
    }

    [Fact]
    public void Constructor_SetsCertainty_WhenProvided()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate, certainty: RelationshipCertainty.Speculative);
        Assert.Equal(RelationshipCertainty.Speculative, marriage.Certainty);
    }

    [Fact]
    public void UpdateCertainty_ChangesCertainty()
    {
        var marriage = new Marriage(Spouse1Id, Spouse2Id, StartDate);
        marriage.UpdateCertainty(RelationshipCertainty.Likely);
        Assert.Equal(RelationshipCertainty.Likely, marriage.Certainty);
    }
}
