using FamilyTree.Application.Services;
using FamilyTree.Domain.ValueObjects;
using FamilyTree.UI.Shared;

namespace FamilyTree.UI.Tests.Shared;

/// <summary>
/// The text-to-date conversion behind PartialDateInput. Kept separate from the
/// component so every precision level and every way of getting it wrong can be
/// stated as a fact about the values, without a renderer in the way.
/// </summary>
public class PartialDateTextTests
{
    private static PartialDateText Text(
        string year, string month = "", string day = "", bool approximate = false) =>
        new(year, month, day, approximate);

    private static PartialDate Parse(PartialDateText text)
    {
        Assert.True(text.TryToDomain(out var date, out var error), error);
        Assert.NotNull(date);
        return date!;
    }

    private static string Reject(PartialDateText text)
    {
        Assert.False(text.TryToDomain(out var date, out var error));
        Assert.Null(date);
        Assert.NotNull(error);
        return error!;
    }

    // An empty control is a perfectly good answer: a genealogy record frequently
    // knows no date at all, and demanding one would invent information.
    [Fact]
    public void AnEmptyControlIsNoDateRatherThanAnError()
    {
        Assert.True(PartialDateText.Empty.TryToDomain(out var date, out var error));
        Assert.Null(date);
        Assert.Null(error);
    }

    // A box holding only spaces is an empty box. It reported a range error
    // instead, because the blank check read the untrimmed text while the parse
    // trimmed — and quick add, which uses IsNullOrWhiteSpace, disagreed with
    // this control about the same keystrokes.
    [Fact]
    public void TreatsAWhitespaceOnlyYearAsAnEmptyControl()
    {
        Assert.True(Text("   ").TryToDomain(out var date, out var error));
        Assert.Null(date);
        Assert.Null(error);
    }

    [Fact]
    public void TreatsAWhitespaceOnlyDayAsNoDayRatherThanAnImpossibleOne()
    {
        Assert.Equal(PartialDate.FromYearMonth(1815, 12), Parse(Text("1815", "12", "  ")));
    }

    [Fact]
    public void ReadsAYearPaddedWithSpaces()
    {
        Assert.Equal(1815, Parse(Text(" 1815 ")).Year);
    }

    [Fact]
    public void AYearAloneIsAYearOnlyDate()
    {
        var date = Parse(Text("1815"));

        Assert.Equal(1815, date.Year);
        Assert.Null(date.Month);
        Assert.Null(date.Day);
    }

    [Fact]
    public void AYearAndMonthKeepTheDayUnknown()
    {
        var date = Parse(Text("1815", "12"));

        Assert.Equal(12, date.Month);
        Assert.Null(date.Day);
    }

    [Fact]
    public void AYearMonthAndDayIsAFullDate()
    {
        var date = Parse(Text("1815", "12", "10"));

        Assert.Equal(new[] { 1815, 12, 10 }, new[] { date.Year, date.Month!.Value, date.Day!.Value });
    }

    [Fact]
    public void TheApproximateFlagIsCarriedOntoTheDate()
    {
        Assert.True(Parse(Text("1815", approximate: true)).IsApproximate);
    }

    // US-045's display criterion, at the precision the flag is set on.
    [Fact]
    public void AnApproximateFullDateStillRendersWithTheCircaPrefix()
    {
        Assert.Equal("c. 10 December 1815", Parse(Text("1815", "12", "10", approximate: true)).ToString());
    }

    [Fact]
    public void RejectsAYearBeyondTheAllowedRange()
    {
        Assert.Equal($"Year must be between 1 and {PersonService.MaxYear}.", Reject(Text("30000")));
    }

    [Fact]
    public void RejectsAYearOfZero()
    {
        Assert.Contains("Year must be between", Reject(Text("0")));
    }

    // "Typed something impossible" is not "left it blank", but int.TryParse
    // returns the same null for both — so the distinction is drawn from the
    // length of the text rather than from the parse.
    [Fact]
    public void RejectsAYearTooLargeToParseRatherThanTreatingItAsBlank()
    {
        Assert.Contains("Year must be between", Reject(Text("99999999999")));
    }

    [Fact]
    public void RejectsAYearThatIsNotANumberAtAll()
    {
        Assert.Contains("Year must be between", Reject(Text("abc")));
    }

    // A month with nothing to anchor it is not a date, and dropping it silently
    // would lose a fact the user actually entered.
    [Fact]
    public void RejectsAMonthWithoutAYear()
    {
        Assert.Equal("Enter a year before a month.", Reject(Text(string.Empty, "3")));
    }

    // ExportedDate.TryToDomain rejects this combination on import, so the control
    // must not be able to produce it either.
    [Fact]
    public void RejectsADayWithoutAMonth()
    {
        Assert.Equal("Enter a month before a day.", Reject(Text("1815", string.Empty, "10")));
    }

    [Fact]
    public void RejectsAMonthOutsideOneToTwelve()
    {
        Assert.Equal("Month must be between 1 and 12.", Reject(Text("1815", "13")));
    }

    // PartialDate.FromYearMonthDay validates eagerly through DateTime.DaysInMonth
    // and throws, and an exception reaching the renderer is a blank screen.
    [Fact]
    public void RejectsTheThirtyFirstOfFebruaryWithAMessageRatherThanThrowing()
    {
        Assert.Equal("Day must be between 1 and 28 for February 1815.", Reject(Text("1815", "2", "31")));
    }

    [Fact]
    public void AcceptsTheTwentyNinthOfFebruaryInALeapYear()
    {
        Assert.Equal(29, Parse(Text("1816", "2", "29")).Day);
    }

    [Fact]
    public void RejectsTheTwentyNinthOfFebruaryOutsideALeapYear()
    {
        Assert.Equal("Day must be between 1 and 28 for February 1815.", Reject(Text("1815", "2", "29")));
    }

    [Fact]
    public void RejectsADayOfZero()
    {
        Assert.Contains("Day must be between", Reject(Text("1815", "3", "0")));
    }

    [Fact]
    public void RoundTripsAStoredDateBackIntoItsBoxes()
    {
        var text = PartialDateText.From(PartialDate.FromYearMonthDay(1815, 12, 10, isApproximate: true));

        Assert.Equal(new PartialDateText("1815", "12", "10", true), text);
    }

    [Fact]
    public void RoundTripsAYearOnlyDateWithoutInventingAMonth()
    {
        Assert.Equal(new PartialDateText("1815", string.Empty, string.Empty, false),
            PartialDateText.From(PartialDate.FromYear(1815)));
    }

    [Fact]
    public void SeedsFromQueryTextWithoutJudgingItYet()
    {
        Assert.Equal("30000", PartialDateText.FromYearText(" 30000 ").Year);
    }

    [Fact]
    public void TreatsAMissingQueryValueAsAnEmptyControl()
    {
        Assert.True(PartialDateText.FromYearText(null).IsBlank);
    }
}
