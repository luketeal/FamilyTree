using Bunit;
using FamilyTree.Application.Services;
using FamilyTree.Domain.ValueObjects;
using FamilyTree.UI.Shared;

namespace FamilyTree.UI.Tests.Shared;

/// <summary>
/// The date control's behaviour: what it emits, when it refuses, and what it
/// must not throw away while the user is still typing.
/// </summary>
public class PartialDateInputTests : BunitContext
{
    private const string Year = "[data-testid=date]";
    private const string Month = "[data-testid=date-month]";
    private const string Day = "[data-testid=date-day]";
    private const string Approx = "[data-testid=date-approx]";
    private const string Error = "[data-testid=error-date]";

    private PartialDate? _emitted;
    private int _emitCount;
    private bool _invalid;

    private IRenderedComponent<PartialDateInput> RenderInput(PartialDateText? seed = null) =>
        Render<PartialDateInput>(p => p
            .Add(c => c.TestId, "date")
            .Add(c => c.Seed, seed ?? PartialDateText.Empty)
            .Add(c => c.ValueChanged, date =>
            {
                _emitted = date;
                _emitCount++;
            })
            .Add(c => c.InvalidChanged, invalid => _invalid = invalid));

    [Fact]
    public void EmitsAYearOnlyDate_WhenOnlyTheYearIsEntered()
    {
        var cut = RenderInput();

        cut.Find(Year).Input("1815");

        Assert.Equal(PartialDate.FromYear(1815), _emitted);
        Assert.False(_invalid);
    }

    [Fact]
    public void EmitsAYearAndMonth_WhenAMonthIsChosen()
    {
        var cut = RenderInput();

        cut.Find(Year).Input("1815");
        cut.Find(Month).Change("12");

        Assert.Equal(PartialDate.FromYearMonth(1815, 12), _emitted);
    }

    [Fact]
    public void EmitsAFullDate_WhenADayIsEntered()
    {
        var cut = RenderInput();

        cut.Find(Year).Input("1815");
        cut.Find(Month).Change("12");
        cut.Find(Day).Input("10");

        Assert.Equal(PartialDate.FromYearMonthDay(1815, 12, 10), _emitted);
    }

    [Fact]
    public void EmitsAnApproximateDate_WhenCircaIsTicked()
    {
        var cut = RenderInput();

        cut.Find(Year).Input("1880");
        cut.Find(Approx).Change(true);

        Assert.Equal(PartialDate.FromYear(1880, isApproximate: true), _emitted);
    }

    // A day without a month is not a date, and ExportedDate rejects the pairing
    // on import — so clearing the month has to take the day with it.
    [Fact]
    public void ClearingTheMonthClearsTheDay()
    {
        var cut = RenderInput();
        cut.Find(Year).Input("1815");
        cut.Find(Month).Change("12");
        cut.Find(Day).Input("10");

        cut.Find(Month).Change(string.Empty);

        Assert.Equal(string.Empty, cut.Find(Day).GetAttribute("value"));
        Assert.Equal(PartialDate.FromYear(1815), _emitted);
        Assert.False(_invalid);
    }

    // The cascade above is the normal route; this is the state it exists to
    // prevent, reached by typing the day first.
    [Fact]
    public void ReportsADayEnteredWithoutAMonth()
    {
        var cut = RenderInput();

        cut.Find(Year).Input("1815");
        cut.Find(Day).Input("10");

        Assert.True(_invalid);
        Assert.Null(_emitted);
        Assert.Equal("Enter a month before a day.", cut.Find(Error).TextContent);
    }

    [Fact]
    public void ReportsAnImpossibleDayRatherThanThrowing()
    {
        var cut = RenderInput();

        cut.Find(Year).Input("1815");
        cut.Find(Month).Change("2");
        cut.Find(Day).Input("31");

        Assert.True(_invalid);
        Assert.Null(_emitted);
        Assert.Equal("Day must be between 1 and 28 for February 1815.", cut.Find(Error).TextContent);
    }

    [Fact]
    public void ReportsAnOutOfRangeYearAndEmitsNoDate()
    {
        var cut = RenderInput();

        cut.Find(Year).Input("30000");

        Assert.True(_invalid);
        Assert.Null(_emitted);
        Assert.Equal($"Year must be between 1 and {PersonService.MaxYear}.", cut.Find(Error).TextContent);
    }

    [Fact]
    public void ClearsTheErrorOnceTheYearIsCorrected()
    {
        var cut = RenderInput();
        cut.Find(Year).Input("30000");

        cut.Find(Year).Input("1815");

        Assert.Empty(cut.FindAll(Error));
        Assert.False(_invalid);
    }

    // Correcting a neighbouring field must not throw away a precision judgement.
    // Clearing the year emptied the whole date, and the circa flag was re-seeded
    // from that empty value on the parent's next render.
    [Fact]
    public void KeepsTheApproximateFlag_WhenTheYearIsClearedAndRetyped()
    {
        var cut = RenderInput(PartialDateText.From(PartialDate.FromYear(1815)));
        cut.Find(Approx).Change(true);

        cut.Find(Year).Input(string.Empty);
        // The parent re-renders on every emit, which is what used to re-seed.
        cut.Render();
        cut.Find(Year).Input("1820");

        Assert.Equal(PartialDate.FromYear(1820, isApproximate: true), _emitted);
        Assert.True(cut.Find(Approx).HasAttribute("checked"));
    }

    // The seed is read once. A later render with the same parameters must not
    // overwrite what has been typed since.
    [Fact]
    public void DoesNotReSeed_WhenReRenderedWithTheSameSeed()
    {
        var seed = PartialDateText.From(PartialDate.FromYear(1815));
        var cut = RenderInput(seed);

        cut.Find(Year).Input("1820");
        cut.Render(p => p.Add(c => c.Seed, seed));

        Assert.Equal("1820", cut.Find(Year).GetAttribute("value"));
    }

    // Without this the carried-over date is in the box but not in the model, so
    // saving without touching the field stores a person with no date at all.
    [Fact]
    public void EmitsASeededDate_WithoutTheUserTouchingTheControl()
    {
        RenderInput(PartialDateText.From(PartialDate.FromYearMonth(1906, 12)));

        Assert.Equal(PartialDate.FromYearMonth(1906, 12), _emitted);
    }

    [Fact]
    public void SaysNothing_WhenSeededWithAnEmptyValue()
    {
        RenderInput();

        Assert.Equal(0, _emitCount);
        Assert.False(_invalid);
    }

    // A seeded value is judged exactly like a typed one. Validity used to be
    // computed only while typing, so a year carried over from quick add was
    // never judged: it seeded a valid-looking date, left the form submittable,
    // and saved — while the same year typed into the box was rejected.
    [Fact]
    public void JudgesASeededYearTheSameWayAsATypedOne()
    {
        var cut = RenderInput(PartialDateText.FromYearText("30000"));

        Assert.True(_invalid);
        Assert.Null(_emitted);
        Assert.Equal($"Year must be between 1 and {PersonService.MaxYear}.", cut.Find(Error).TextContent);
    }

    // Raw text, so a value no PartialDate can hold still reaches the box the
    // user has to correct it in, rather than being dropped on the way.
    [Fact]
    public void ShowsASeededYearEvenWhenNoDateCouldHoldIt()
    {
        var cut = RenderInput(PartialDateText.FromYearText("30000"));

        Assert.Equal("30000", cut.Find(Year).GetAttribute("value"));
    }

    [Fact]
    public void ShowsASeededYearThatIsNotANumberAtAll()
    {
        var cut = RenderInput(PartialDateText.FromYearText("abc"));

        Assert.Equal("abc", cut.Find(Year).GetAttribute("value"));
        Assert.True(_invalid);
    }

    [Fact]
    public void SeedsEveryPartOfAStoredDate()
    {
        var cut = RenderInput(
            PartialDateText.From(PartialDate.FromYearMonthDay(1815, 12, 10, isApproximate: true)));

        Assert.Equal("1815", cut.Find(Year).GetAttribute("value"));
        Assert.Equal("10", cut.Find(Day).GetAttribute("value"));
        Assert.True(cut.Find(Approx).HasAttribute("checked"));
        Assert.Equal("12", cut.Find($"{Month} option[selected]").GetAttribute("value"));
    }
}
