using Bunit;
using FamilyTree.Application.Common;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;
using FamilyTree.UI.Shared;

namespace FamilyTree.UI.Tests.Pages;

/// <summary>
/// Form validation, at the component level. The point is that invalid input
/// never reaches the service: a round trip to storage is the wrong way to
/// discover a required field is blank.
/// </summary>
public class PersonFormTests : ShellTestContext
{
    private PersonService.PersonInput? _submitted;
    private int _submitCount;

    private IRenderedComponent<PersonForm> RenderForm(
        PersonDetailDto? person = null, PartialDateText? birthSeed = null) =>
        Render<PersonForm>(p => p
            .Add(f => f.Person, person)
            .Add(f => f.BirthDateSeed, birthSeed)
            .Add(f => f.Submit, input =>
            {
                _submitted = input;
                _submitCount++;
            }));

    [Fact]
    public void RejectsPerson_WhenFirstNameIsEmpty()
    {
        var cut = RenderForm();
        cut.Find("[data-testid=input-last-name]").Input("Lovelace");

        cut.Find("form").Submit();

        Assert.Equal(0, _submitCount);
        Assert.NotEmpty(cut.FindAll("[data-testid=error-first-name]"));
    }

    [Fact]
    public void RejectsPerson_WhenLastNameIsEmpty()
    {
        var cut = RenderForm();
        cut.Find("[data-testid=input-first-name]").Input("Ada");

        cut.Find("form").Submit();

        Assert.Equal(0, _submitCount);
        Assert.NotEmpty(cut.FindAll("[data-testid=error-last-name]"));
    }

    // Whitespace is not a name. Without this, "   " satisfies a naive check and
    // produces a person with a blank display name.
    [Fact]
    public void RejectsPerson_WhenNamesAreOnlyWhitespace()
    {
        var cut = RenderForm();
        cut.Find("[data-testid=input-first-name]").Input("   ");
        cut.Find("[data-testid=input-last-name]").Input("   ");

        cut.Find("form").Submit();

        Assert.Equal(0, _submitCount);
    }

    [Fact]
    public void AcceptsPerson_WhenBothNamesArePresent()
    {
        var cut = RenderForm();
        cut.Find("[data-testid=input-first-name]").Input("Ada");
        cut.Find("[data-testid=input-last-name]").Input("Lovelace");

        cut.Find("form").Submit();

        Assert.Equal(1, _submitCount);
        Assert.Equal("Ada", _submitted!.FirstName);
        Assert.Equal("Lovelace", _submitted!.LastName);
    }

    [Fact]
    public void TrimsNamesBeforeSubmitting()
    {
        var cut = RenderForm();
        cut.Find("[data-testid=input-first-name]").Input("  Ada  ");
        cut.Find("[data-testid=input-last-name]").Input("  Lovelace  ");

        cut.Find("form").Submit();

        Assert.Equal("Ada", _submitted!.FirstName);
        Assert.Equal("Lovelace", _submitted!.LastName);
    }

    // A blank optional field must be null, not "". An empty string would be
    // stored and then rendered as "(née )".
    [Fact]
    public void TreatsBlankOptionalFieldsAsAbsent()
    {
        var cut = RenderForm();
        cut.Find("[data-testid=input-first-name]").Input("Ada");
        cut.Find("[data-testid=input-last-name]").Input("Lovelace");
        cut.Find("[data-testid=input-birth-surname]").Input("   ");

        cut.Find("form").Submit();

        Assert.Null(_submitted!.BirthSurname);
    }

    [Fact]
    public void ClearsAFieldError_AsSoonAsItIsCorrected()
    {
        var cut = RenderForm();
        cut.Find("form").Submit();
        Assert.NotEmpty(cut.FindAll("[data-testid=error-first-name]"));

        cut.Find("[data-testid=input-first-name]").Input("Ada");

        Assert.Empty(cut.FindAll("[data-testid=error-first-name]"));
    }

    [Fact]
    public void ReportsNoUnsavedChanges_BeforeAnythingIsTyped()
    {
        var cut = RenderForm();

        Assert.Empty(cut.FindAll("[data-testid=unsaved-indicator]"));
    }

    [Fact]
    public void ReportsUnsavedChanges_OnceEdited()
    {
        var cut = RenderForm();

        cut.Find("[data-testid=input-first-name]").Input("Ada");

        Assert.NotEmpty(cut.FindAll("[data-testid=unsaved-indicator]"));
    }

    // The seeded values are the baseline, so opening an existing person and
    // changing nothing must not claim there is unsaved work.
    [Fact]
    public void ReportsNoUnsavedChanges_WhenOpeningAnExistingPerson()
    {
        var cut = RenderForm(Existing());

        Assert.Empty(cut.FindAll("[data-testid=unsaved-indicator]"));
    }

    [Fact]
    public void SeedsEveryFieldFromAnExistingPerson()
    {
        var cut = RenderForm(Existing());

        Assert.Equal("Ada", cut.Find("[data-testid=input-first-name]").GetAttribute("value"));
        Assert.Equal("Lovelace", cut.Find("[data-testid=input-last-name]").GetAttribute("value"));
        Assert.Equal("Byron", cut.Find("[data-testid=input-birth-surname]").GetAttribute("value"));
        Assert.Equal("1815", cut.Find("[data-testid=input-birth-year]").GetAttribute("value"));
    }

    // Typing re-renders the parent, which re-runs OnParametersSet. Re-seeding
    // there would discard the edit on every keystroke.
    [Fact]
    public void DoesNotDiscardEdits_WhenReRenderedWithTheSamePerson()
    {
        var cut = RenderForm(Existing());

        cut.Find("[data-testid=input-first-name]").Input("Augusta");
        cut.Render();

        Assert.Equal("Augusta", cut.Find("[data-testid=input-first-name]").GetAttribute("value"));
    }

    [Fact]
    public void StopsReportingUnsavedChanges_AfterMarkSaved()
    {
        var cut = RenderForm();
        cut.Find("[data-testid=input-first-name]").Input("Ada");
        Assert.NotEmpty(cut.FindAll("[data-testid=unsaved-indicator]"));

        cut.InvokeAsync(() => cut.Instance.MarkSaved());
        cut.Render();

        Assert.Empty(cut.FindAll("[data-testid=unsaved-indicator]"));
    }

    // The carried-over date has to reach the saved record without the user
    // retyping it. It arrives as text, so nothing puts it in the model unless
    // the control emits it on seeding.
    [Fact]
    public void SubmitsACarriedOverBirthDate_WithoutItBeingRetyped()
    {
        var cut = RenderForm(birthSeed: PartialDateText.FromYearText("1906"));
        cut.Find("[data-testid=input-first-name]").Input("Grace");
        cut.Find("[data-testid=input-last-name]").Input("Hopper");

        cut.Find("form").Submit();

        Assert.Equal(PartialDate.FromYear(1906), _submitted!.BirthDate);
    }

    // A year the app will not accept must block the save rather than being
    // dropped from the record on the way through.
    [Fact]
    public void RefusesToSubmit_WhileACarriedOverYearIsOutOfRange()
    {
        var cut = RenderForm(birthSeed: PartialDateText.FromYearText("30000"));
        cut.Find("[data-testid=input-first-name]").Input("Carry");
        cut.Find("[data-testid=input-last-name]").Input("Over");

        cut.Find("form").Submit();

        Assert.Equal(0, _submitCount);
        Assert.NotEmpty(cut.FindAll("[data-testid=error-input-birth-year]"));
    }

    // The seed is raw text precisely so a value no PartialDate can hold still
    // reaches the box the user corrects it in.
    [Fact]
    public void ShowsACarriedOverYearThatCouldNotBeStored()
    {
        var cut = RenderForm(birthSeed: PartialDateText.FromYearText("30000"));

        Assert.Equal("30000", cut.Find("[data-testid=input-birth-year]").GetAttribute("value"));
    }

    [Fact]
    public void CorrectingACarriedOverYearLetsTheSaveThrough()
    {
        var cut = RenderForm(birthSeed: PartialDateText.FromYearText("30000"));
        cut.Find("[data-testid=input-first-name]").Input("Carry");
        cut.Find("[data-testid=input-last-name]").Input("Over");

        cut.Find("[data-testid=input-birth-year]").Input("1906");
        cut.Find("form").Submit();

        Assert.Equal(1, _submitCount);
        Assert.Equal(PartialDate.FromYear(1906), _submitted!.BirthDate);
    }

    [Fact]
    public void SubmitsAFullDate_WhenEveryPartIsEntered()
    {
        var cut = RenderForm();
        cut.Find("[data-testid=input-first-name]").Input("Ada");
        cut.Find("[data-testid=input-last-name]").Input("Lovelace");
        cut.Find("[data-testid=input-birth-year]").Input("1815");
        cut.Find("[data-testid=input-birth-year-month]").Change("12");
        cut.Find("[data-testid=input-birth-year-day]").Input("10");

        cut.Find("form").Submit();

        Assert.Equal(PartialDate.FromYearMonthDay(1815, 12, 10), _submitted!.BirthDate);
    }

    // An existing date now leaves the model, becomes text, and comes back. A
    // lossy leg anywhere in that round trip would quietly change a stored date
    // on any edit that never touched the field.
    [Fact]
    public void PreservesAnExistingFullDate_ThroughAnEditThatDoesNotTouchIt()
    {
        var stored = PartialDate.FromYearMonthDay(1815, 12, 10, isApproximate: true);
        var cut = RenderForm(Existing() with { BirthDate = stored });

        cut.Find("[data-testid=input-birth-place]").Input("London");
        cut.Find("form").Submit();

        Assert.Equal(stored, _submitted!.BirthDate);
    }

    private static PersonDetailDto Existing() => new(
        Guid.NewGuid(),
        "Ada",
        "Lovelace",
        "Byron",
        PartialDate.FromYear(1815),
        "London",
        null,
        null,
        Gender.Female,
        null,
        null,
        false);
}
