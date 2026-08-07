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

    private IRenderedComponent<PersonForm> RenderForm(PersonDetailDto? person = null) =>
        Render<PersonForm>(p => p
            .Add(f => f.Person, person)
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
