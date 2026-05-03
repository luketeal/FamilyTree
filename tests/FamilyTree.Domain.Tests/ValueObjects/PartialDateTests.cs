using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Domain.Tests.ValueObjects;

public sealed class PartialDateTests
{
    // ── FromYear ──────────────────────────────────────────────────────────

    [Fact]
    public void FromYear_SetsYear()
    {
        var date = PartialDate.FromYear(1920);
        Assert.Equal(1920, date.Year);
    }

    [Fact]
    public void FromYear_LeavesMonthNull()
    {
        var date = PartialDate.FromYear(1920);
        Assert.Null(date.Month);
    }

    [Fact]
    public void FromYear_LeavesDayNull()
    {
        var date = PartialDate.FromYear(1920);
        Assert.Null(date.Day);
    }

    [Fact]
    public void FromYear_IsApproximateFalseByDefault()
    {
        var date = PartialDate.FromYear(1920);
        Assert.False(date.IsApproximate);
    }

    [Fact]
    public void FromYear_SetsIsApproximate_WhenTrue()
    {
        var date = PartialDate.FromYear(1920, isApproximate: true);
        Assert.True(date.IsApproximate);
    }

    [Fact]
    public void FromYear_Throws_WhenYearIsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PartialDate.FromYear(0));
    }

    [Fact]
    public void FromYear_Throws_WhenYearIsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PartialDate.FromYear(-1));
    }

    [Fact]
    public void FromYear_Throws_WhenYearExceeds9999()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PartialDate.FromYear(10000));
    }

    [Fact]
    public void FromYear_AcceptsYear1()
    {
        var date = PartialDate.FromYear(1);
        Assert.Equal(1, date.Year);
    }

    [Fact]
    public void FromYear_AcceptsYear9999()
    {
        var date = PartialDate.FromYear(9999);
        Assert.Equal(9999, date.Year);
    }

    // ── FromYearMonth ─────────────────────────────────────────────────────

    [Fact]
    public void FromYearMonth_SetsYearAndMonth()
    {
        var date = PartialDate.FromYearMonth(1920, 3);
        Assert.Equal(1920, date.Year);
        Assert.Equal(3, date.Month);
    }

    [Fact]
    public void FromYearMonth_LeavesDayNull()
    {
        var date = PartialDate.FromYearMonth(1920, 3);
        Assert.Null(date.Day);
    }

    [Fact]
    public void FromYearMonth_Throws_WhenMonthIsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PartialDate.FromYearMonth(1920, 0));
    }

    [Fact]
    public void FromYearMonth_Throws_WhenMonthExceeds12()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PartialDate.FromYearMonth(1920, 13));
    }

    [Fact]
    public void FromYearMonth_AcceptsMonth1()
    {
        var date = PartialDate.FromYearMonth(2000, 1);
        Assert.Equal(1, date.Month);
    }

    [Fact]
    public void FromYearMonth_AcceptsMonth12()
    {
        var date = PartialDate.FromYearMonth(2000, 12);
        Assert.Equal(12, date.Month);
    }

    // ── FromYearMonthDay ──────────────────────────────────────────────────

    [Fact]
    public void FromYearMonthDay_SetsYearMonthAndDay()
    {
        var date = PartialDate.FromYearMonthDay(1920, 3, 15);
        Assert.Equal(1920, date.Year);
        Assert.Equal(3, date.Month);
        Assert.Equal(15, date.Day);
    }

    [Fact]
    public void FromYearMonthDay_Throws_WhenDayIsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PartialDate.FromYearMonthDay(2000, 1, 0));
    }

    [Fact]
    public void FromYearMonthDay_Throws_WhenDayExceedsMonthLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PartialDate.FromYearMonthDay(2000, 2, 30));
    }

    [Fact]
    public void FromYearMonthDay_Throws_WhenDayExceedsFebruaryInLeapYear()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PartialDate.FromYearMonthDay(2000, 2, 30));
    }

    [Fact]
    public void FromYearMonthDay_AcceptsLeapDay_InLeapYear()
    {
        var date = PartialDate.FromYearMonthDay(2000, 2, 29);
        Assert.Equal(29, date.Day);
    }

    [Fact]
    public void FromYearMonthDay_Throws_WhenLeapDayInNonLeapYear()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PartialDate.FromYearMonthDay(1900, 2, 29));
    }

    // ── ToString ──────────────────────────────────────────────────────────

    [Fact]
    public void ToString_ReturnsYear_WhenYearOnly()
    {
        var date = PartialDate.FromYear(1920);
        Assert.Equal("1920", date.ToString());
    }

    [Fact]
    public void ToString_PrefixesWithC_WhenYearOnlyAndApproximate()
    {
        var date = PartialDate.FromYear(1920, isApproximate: true);
        Assert.Equal("c. 1920", date.ToString());
    }

    [Fact]
    public void ToString_ReturnsMonthYear_WhenNoDay()
    {
        var date = PartialDate.FromYearMonth(1920, 3);
        Assert.Equal("March 1920", date.ToString());
    }

    [Fact]
    public void ToString_PrefixesWithC_WhenYearMonthAndApproximate()
    {
        var date = PartialDate.FromYearMonth(1920, 3, isApproximate: true);
        Assert.Equal("c. March 1920", date.ToString());
    }

    [Fact]
    public void ToString_ReturnsDayMonthYear_WhenFullDate()
    {
        var date = PartialDate.FromYearMonthDay(1920, 3, 15);
        Assert.Equal("15 March 1920", date.ToString());
    }

    [Fact]
    public void ToString_PrefixesWithC_WhenFullDateAndApproximate()
    {
        var date = PartialDate.FromYearMonthDay(1920, 3, 15, isApproximate: true);
        Assert.Equal("c. 15 March 1920", date.ToString());
    }

    // ── CompareTo ─────────────────────────────────────────────────────────

    [Fact]
    public void CompareTo_ReturnsZero_ForIdenticalYearOnly()
    {
        var a = PartialDate.FromYear(1920);
        var b = PartialDate.FromYear(1920);
        Assert.Equal(0, a.CompareTo(b));
    }

    [Fact]
    public void CompareTo_ReturnsNegative_WhenYearIsEarlier()
    {
        var earlier = PartialDate.FromYear(1919);
        var later = PartialDate.FromYear(1920);
        Assert.True(earlier.CompareTo(later) < 0);
    }

    [Fact]
    public void CompareTo_ReturnsPositive_WhenYearIsLater()
    {
        var later = PartialDate.FromYear(1921);
        var earlier = PartialDate.FromYear(1920);
        Assert.True(later.CompareTo(earlier) > 0);
    }

    [Fact]
    public void CompareTo_SortsYearOnly_BeforeYearMonthWithSameYear()
    {
        var yearOnly = PartialDate.FromYear(1920);
        var yearMonth = PartialDate.FromYearMonth(1920, 1);
        Assert.True(yearOnly.CompareTo(yearMonth) < 0);
    }

    [Fact]
    public void CompareTo_SortsEarlierMonth_First_WhenYearsEqual()
    {
        var jan = PartialDate.FromYearMonth(1920, 1);
        var jun = PartialDate.FromYearMonth(1920, 6);
        Assert.True(jan.CompareTo(jun) < 0);
    }

    [Fact]
    public void CompareTo_SortsYearMonth_BeforeFullDateWithSameYearAndMonth()
    {
        var yearMonth = PartialDate.FromYearMonth(1920, 3);
        var fullDate = PartialDate.FromYearMonthDay(1920, 3, 1);
        Assert.True(yearMonth.CompareTo(fullDate) < 0);
    }

    [Fact]
    public void CompareTo_SortsEarlierDay_First_WhenYearAndMonthEqual()
    {
        var day1 = PartialDate.FromYearMonthDay(1920, 3, 1);
        var day15 = PartialDate.FromYearMonthDay(1920, 3, 15);
        Assert.True(day1.CompareTo(day15) < 0);
    }

    [Fact]
    public void CompareTo_ReturnsPositive_WhenOtherIsNull()
    {
        var date = PartialDate.FromYear(1920);
        Assert.True(date.CompareTo(null) > 0);
    }

    [Fact]
    public void CompareTo_IgnoresIsApproximate_ForOrdering()
    {
        var exact = PartialDate.FromYear(1920);
        var approx = PartialDate.FromYear(1920, isApproximate: true);
        Assert.Equal(0, exact.CompareTo(approx));
    }

    // ── Equals / GetHashCode ──────────────────────────────────────────────

    [Fact]
    public void Equals_ReturnsTrue_ForIdenticalDates()
    {
        var a = PartialDate.FromYearMonthDay(1920, 3, 15);
        var b = PartialDate.FromYearMonthDay(1920, 3, 15);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equals_ReturnsFalse_WhenIsApproximateDiffers()
    {
        var exact = PartialDate.FromYear(1920);
        var approx = PartialDate.FromYear(1920, isApproximate: true);
        Assert.False(exact.Equals(approx));
    }

    [Fact]
    public void Equals_ReturnsFalse_WhenYearsDiffer()
    {
        var a = PartialDate.FromYear(1920);
        var b = PartialDate.FromYear(1921);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_ReturnsFalse_WhenMonthsDiffer()
    {
        var a = PartialDate.FromYearMonth(1920, 3);
        var b = PartialDate.FromYearMonth(1920, 4);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_ReturnsFalse_WhenOneHasMonthAndOtherDoesNot()
    {
        var yearOnly = PartialDate.FromYear(1920);
        var yearMonth = PartialDate.FromYearMonth(1920, 1);
        Assert.False(yearOnly.Equals(yearMonth));
    }

    [Fact]
    public void Equals_ReturnsFalse_WhenNull()
    {
        var date = PartialDate.FromYear(1920);
        Assert.False(date.Equals(null));
    }

    [Fact]
    public void GetHashCode_IsConsistent_ForEqualDates()
    {
        var a = PartialDate.FromYearMonthDay(1920, 3, 15);
        var b = PartialDate.FromYearMonthDay(1920, 3, 15);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DiffersForDifferentApproximateFlag()
    {
        var exact = PartialDate.FromYear(1920);
        var approx = PartialDate.FromYear(1920, isApproximate: true);
        Assert.NotEqual(exact.GetHashCode(), approx.GetHashCode());
    }
}
