using Bunit;
using FamilyTree.Application.Common;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;
using FamilyTree.UI.Shared;
using Moq;

namespace FamilyTree.UI.Tests.Shared;

public class PersonSearchSelectTests : ShellTestContext
{
    private static Person Someone(string first, string last, int? birthYear = null)
    {
        var person = new Person(first, last, Gender.Unknown);
        if (birthYear is int year)
        {
            person.UpdateDates(PartialDate.FromYear(year), null, null, null);
        }

        return person;
    }

    private void GivenRoster(params Person[] roster) =>
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(roster);

    /// <summary>
    /// Debounce off by default so the filter is synchronous and the assertions
    /// are about matching rather than about timing. The one test that is about
    /// timing sets its own interval.
    /// </summary>
    private IRenderedComponent<PersonSearchSelect> RenderSelect(
        IReadOnlyCollection<Guid>? exclude = null,
        int debounce = 0,
        Action<PersonSummaryDto?>? onSelected = null) =>
        Render<PersonSearchSelect>(p =>
        {
            p.Add(c => c.DebounceMilliseconds, debounce);
            p.Add(c => c.Exclude, exclude ?? []);
            if (onSelected is not null)
            {
                p.Add(c => c.SelectedChanged, onSelected);
            }
        });

    [Fact]
    public void ListsEverybodyBeforeAnythingIsTyped()
    {
        GivenRoster(Someone("Ada", "Lovelace"), Someone("Grace", "Hopper"));

        var cut = RenderSelect();

        Assert.Equal(2, cut.FindAll("[data-testid^='person-search-option-']").Count);
    }

    [Fact]
    public void FiltersByFirstName()
    {
        GivenRoster(Someone("Ada", "Lovelace"), Someone("Grace", "Hopper"));
        var cut = RenderSelect();

        cut.Find("[data-testid='person-search-query']").Input("ada");

        var options = cut.FindAll("[data-testid^='person-search-option-']");
        Assert.Single(options);
        Assert.Contains("Ada Lovelace", options[0].TextContent);
    }

    [Fact]
    public void FiltersByLastName()
    {
        GivenRoster(Someone("Ada", "Lovelace"), Someone("Grace", "Hopper"));
        var cut = RenderSelect();

        cut.Find("[data-testid='person-search-query']").Input("hopp");

        Assert.Single(cut.FindAll("[data-testid^='person-search-option-']"));
    }

    // Somebody researching a family often knows the name a woman was born with
    // rather than the one she is filed under.
    [Fact]
    public void FiltersByBirthSurname()
    {
        var margaret = Someone("Margaret", "Whitfield");
        margaret.UpdateName("Margaret", "Whitfield", "Ellery");
        GivenRoster(margaret, Someone("Grace", "Hopper"));
        var cut = RenderSelect();

        cut.Find("[data-testid='person-search-query']").Input("ellery");

        Assert.Single(cut.FindAll("[data-testid^='person-search-option-']"));
    }

    [Fact]
    public void MatchesRegardlessOfCase()
    {
        GivenRoster(Someone("Ada", "Lovelace"));
        var cut = RenderSelect();

        cut.Find("[data-testid='person-search-query']").Input("LOVELACE");

        Assert.Single(cut.FindAll("[data-testid^='person-search-option-']"));
    }

    [Fact]
    public void SaysSoWhenNothingMatches()
    {
        GivenRoster(Someone("Ada", "Lovelace"));
        var cut = RenderSelect();

        cut.Find("[data-testid='person-search-query']").Input("zzz");

        Assert.Contains("Nobody matching", cut.Find("[data-testid='person-search-empty']").TextContent);
    }

    // The exclusion list keeps the subject and anyone already linked out of the
    // results. The service would refuse them anyway; this stops the list
    // offering choices whose only outcome is an error.
    [Fact]
    public void OmitsExcludedPeople()
    {
        var ada = Someone("Ada", "Lovelace");
        GivenRoster(ada, Someone("Grace", "Hopper"));

        var cut = RenderSelect(exclude: [ada.Id]);

        var options = cut.FindAll("[data-testid^='person-search-option-']");
        Assert.Single(options);
        Assert.Contains("Grace Hopper", options[0].TextContent);
    }

    [Fact]
    public void RaisesTheChosenPerson()
    {
        var ada = Someone("Ada", "Lovelace");
        GivenRoster(ada);
        PersonSummaryDto? chosen = null;
        var cut = RenderSelect(onSelected: p => chosen = p);

        cut.Find($"[data-testid='person-search-option-{ada.Id}']").Click();

        Assert.Equal(ada.Id, chosen?.Id);
    }

    // A mis-tap must be one tap to undo, not a state with no way out.
    [Fact]
    public void ClearsTheChoiceWhenTheChosenRowIsClickedAgain()
    {
        var ada = Someone("Ada", "Lovelace");
        GivenRoster(ada);
        PersonSummaryDto? chosen = null;
        var cut = RenderSelect(onSelected: p => chosen = p);

        cut.Find($"[data-testid='person-search-option-{ada.Id}']").Click();
        // The parent owns Selected, so the round trip is simulated here: without
        // it the control would never see its own choice come back and the second
        // click would look like a first one.
        cut.Render(p => p
            .Add(c => c.DebounceMilliseconds, 0)
            .Add(c => c.Exclude, Array.Empty<Guid>())
            .Add(c => c.Selected, chosen)
            .Add(c => c.SelectedChanged, (PersonSummaryDto? person) => chosen = person));
        cut.Find($"[data-testid='person-search-option-{ada.Id}']").Click();

        Assert.Null(chosen);
    }

    // ---- Debounce ----

    // Two assertions on purpose. "Not filtered yet" is only meaningful next to
    // "filtered afterwards": without the second, a control that never filtered
    // at all would pass, and without the first, one with no debounce would.
    [Fact]
    public async Task WaitsForTypingToStopBeforeFiltering()
    {
        GivenRoster(Someone("Ada", "Lovelace"), Someone("Grace", "Hopper"));
        var cut = RenderSelect(debounce: 80);

        cut.Find("[data-testid='person-search-query']").Input("ada");

        Assert.Equal(2, cut.FindAll("[data-testid^='person-search-option-']").Count);

        await Task.Delay(250);
        cut.WaitForAssertion(
            () => Assert.Single(cut.FindAll("[data-testid^='person-search-option-']")));
    }

    // ---- Creating inline ----

    [Fact]
    public void OffersToCreateSomebodyWhoIsNotThere()
    {
        GivenRoster();
        var cut = RenderSelect();

        cut.Find("[data-testid='person-search-create']").Click();

        Assert.NotNull(cut.Find("[data-testid='person-search-create-form']"));
    }

    // Retyping the name that was just typed into the search box is the kind of
    // friction that makes an escape hatch cost more than not having one.
    [Fact]
    public void CarriesTheSearchTextIntoTheCreateForm()
    {
        GivenRoster();
        var cut = RenderSelect();

        cut.Find("[data-testid='person-search-query']").Input("Ada Lovelace");
        cut.Find("[data-testid='person-search-create']").Click();

        Assert.Equal("Ada", cut.Find("[data-testid='person-search-create-first']").GetAttribute("value"));
        Assert.Equal("Lovelace", cut.Find("[data-testid='person-search-create-last']").GetAttribute("value"));
    }

    [Fact]
    public void RefusesToCreateSomebodyWithNoLastName()
    {
        GivenRoster();
        var cut = RenderSelect();

        cut.Find("[data-testid='person-search-create']").Click();
        cut.Find("[data-testid='person-search-create-first']").Input("Ada");
        cut.Find("[data-testid='person-search-create-save']").Click();

        Assert.Contains("required", cut.Find("[data-testid='person-search-error']").TextContent);
        People.Verify(r => r.AddAsync(It.IsAny<Person>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // A number input reports an empty value for anything non-numeric, so the box
    // could not show back the typo it is complaining about. These are text
    // inputs, which means the range has to be judged here.
    [Fact]
    public void RefusesAnOutOfRangeBirthYear()
    {
        GivenRoster();
        var cut = RenderSelect();

        cut.Find("[data-testid='person-search-create']").Click();
        cut.Find("[data-testid='person-search-create-first']").Input("Ada");
        cut.Find("[data-testid='person-search-create-last']").Input("Lovelace");
        cut.Find("[data-testid='person-search-create-born']").Input("99999999999");
        cut.Find("[data-testid='person-search-create-save']").Click();

        Assert.Contains("Year must be between", cut.Find("[data-testid='person-search-error']").TextContent);
        People.Verify(r => r.AddAsync(It.IsAny<Person>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void CreatesAndSelectsTheNewPerson()
    {
        GivenRoster();
        PersonSummaryDto? chosen = null;
        var cut = RenderSelect(onSelected: p => chosen = p);

        cut.Find("[data-testid='person-search-create']").Click();
        cut.Find("[data-testid='person-search-create-first']").Input("Ada");
        cut.Find("[data-testid='person-search-create-last']").Input("Lovelace");
        cut.Find("[data-testid='person-search-create-save']").Click();

        People.Verify(r => r.AddAsync(It.IsAny<Person>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Ada Lovelace", chosen?.DisplayName);
    }

    // The number of controls does not exceed 16px by accident. Every input here
    // is wrapped in .input so the phone rules in app.css reach it — a bare one
    // would be missed, and iOS would zoom the page on focus.
    [Fact]
    public void WrapsEveryTextFieldInTheStyledInputShell()
    {
        GivenRoster();
        var cut = RenderSelect();
        cut.Find("[data-testid='person-search-create']").Click();

        foreach (var input in cut.FindAll("input"))
        {
            Assert.Contains("input", input.ParentElement!.GetAttribute("class"));
        }
    }
}
