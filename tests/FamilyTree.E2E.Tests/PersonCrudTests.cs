using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// The first flow where a user puts their own data in. Every assertion here
/// runs against the published site, because the round trip through IndexedDB is
/// the part that matters and bUnit cannot reach it.
/// </summary>
[Collection(nameof(StaticSiteCollection))]
public class PersonCrudTests(StaticSiteFixture fixture)
{
    private async Task<IPage> OpenAsync(string path = "people")
    {
        var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + path, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        return page;
    }

    private static async Task FillPersonAsync(
        IPage page, string first, string last, string? birthYear = null)
    {
        await page.GetByTestId("input-first-name").FillAsync(first);
        await page.GetByTestId("input-last-name").FillAsync(last);
        if (birthYear is not null)
        {
            await page.GetByTestId("input-birth-year").FillAsync(birthYear);
        }
    }

    [Fact]
    public async Task AnEmptyTreeInvitesTheFirstPerson()
    {
        var page = await OpenAsync();

        await Assertions.Expect(page.GetByTestId("people-empty-state")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("add-first-person")).ToBeVisibleAsync();
    }

    // The whole point of the storage layer: a person the user typed is still
    // there after the browser reloads the app from scratch.
    [Fact]
    public async Task APersonSurvivesAFullReload()
    {
        var page = await OpenAsync("people/add");

        await FillPersonAsync(page, "Ada", "Lovelace", "1815");
        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("profile-name")).ToHaveTextAsync("Ada Lovelace");

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });

        await Assertions.Expect(page.GetByTestId("profile-name")).ToHaveTextAsync("Ada Lovelace");
        await Assertions.Expect(page.GetByTestId("profile-birth")).ToContainTextAsync("1815");
    }

    [Fact]
    public async Task AMissingFirstNameIsRejectedInline()
    {
        var page = await OpenAsync("people/add");

        await page.GetByTestId("input-last-name").FillAsync("Lovelace");
        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("error-first-name")).ToBeVisibleAsync();
        // Still on the form: nothing was saved.
        await Assertions.Expect(page.GetByTestId("save-person")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AMissingLastNameIsRejectedInline()
    {
        var page = await OpenAsync("people/add");

        await page.GetByTestId("input-first-name").FillAsync("Ada");
        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("error-last-name")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CorrectingARejectedFieldClearsItsError()
    {
        var page = await OpenAsync("people/add");

        await page.GetByTestId("save-person").ClickAsync();
        await Assertions.Expect(page.GetByTestId("error-first-name")).ToBeVisibleAsync();

        await page.GetByTestId("input-first-name").FillAsync("Ada");

        await Assertions.Expect(page.GetByTestId("error-first-name")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task ASavedPersonAppearsInTheList()
    {
        var page = await OpenAsync("people/add");
        await FillPersonAsync(page, "Ada", "Lovelace");
        await page.GetByTestId("save-person").ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-name")).ToBeVisibleAsync();

        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        await Assertions.Expect(page.GetByTestId("people-list")).ToContainTextAsync("Ada Lovelace");
        await Assertions.Expect(page.GetByTestId("people-count")).ToHaveTextAsync("1 person");
    }

    [Fact]
    public async Task EditingAPersonPersistsTheChange()
    {
        var page = await OpenAsync("people/add");
        await FillPersonAsync(page, "Ada", "Byron");
        await page.GetByTestId("save-person").ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-name")).ToBeVisibleAsync();

        await page.GetByTestId("edit-person").ClickAsync();
        await page.GetByTestId("input-last-name").FillAsync("Lovelace");
        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("profile-name")).ToHaveTextAsync("Ada Lovelace");

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await Assertions.Expect(page.GetByTestId("profile-name")).ToHaveTextAsync("Ada Lovelace");
    }

    // The edit form is seeded from storage rather than starting blank, or an
    // edit would silently blank every field the user did not retype.
    [Fact]
    public async Task TheEditFormIsPrefilledFromTheStoredPerson()
    {
        var page = await OpenAsync("people/add");
        await FillPersonAsync(page, "Ada", "Lovelace", "1815");
        await page.GetByTestId("input-birth-place").FillAsync("London");
        await page.GetByTestId("save-person").ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-name")).ToBeVisibleAsync();

        await page.GetByTestId("edit-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("input-first-name")).ToHaveValueAsync("Ada");
        await Assertions.Expect(page.GetByTestId("input-birth-year")).ToHaveValueAsync("1815");
        await Assertions.Expect(page.GetByTestId("input-birth-place")).ToHaveValueAsync("London");
    }

    [Fact]
    public async Task TypingMarksTheFormUnsaved()
    {
        var page = await OpenAsync("people/add");

        await Assertions.Expect(page.GetByTestId("unsaved-indicator")).ToBeHiddenAsync();

        await page.GetByTestId("input-first-name").FillAsync("Ada");

        await Assertions.Expect(page.GetByTestId("unsaved-indicator")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DeletingAPersonAsksFirstAndThenRemovesThem()
    {
        var page = await OpenAsync("people/add");
        await FillPersonAsync(page, "Ada", "Lovelace");
        await page.GetByTestId("save-person").ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-name")).ToBeVisibleAsync();

        await page.GetByTestId("delete-person").ClickAsync();
        await Assertions.Expect(page.GetByTestId("delete-modal-body")).ToContainTextAsync("Ada Lovelace");

        await page.GetByTestId("confirm-delete").ClickAsync();

        await Assertions.Expect(page.GetByTestId("people-empty-state")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CancellingADeleteKeepsThePerson()
    {
        var page = await OpenAsync("people/add");
        await FillPersonAsync(page, "Ada", "Lovelace");
        await page.GetByTestId("save-person").ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-name")).ToBeVisibleAsync();

        await page.GetByTestId("delete-person").ClickAsync();
        await page.GetByTestId("cancel-delete").ClickAsync();

        await Assertions.Expect(page.GetByTestId("delete-modal")).ToBeHiddenAsync();

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await Assertions.Expect(page.GetByTestId("profile-name")).ToHaveTextAsync("Ada Lovelace");
    }

    // US-053: adding from the top bar must not cost the user their place.
    [Fact]
    public async Task QuickAddCreatesAPersonFromAnywhere()
    {
        var page = await OpenAsync("settings");

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Grace");
        await page.GetByTestId("quick-last-name").FillAsync("Hopper");
        await page.GetByTestId("quick-save").ClickAsync();

        await Assertions.Expect(page.GetByTestId("profile-name")).ToHaveTextAsync("Grace Hopper");
        await Assertions.Expect(page.GetByTestId("tree-stats")).ToHaveTextAsync("1 person · 0 relationships");
    }

    [Fact]
    public async Task QuickAddFocusesTheFirstFieldSoItCanBeTypedStraightAway()
    {
        var page = await OpenAsync();

        await page.GetByTestId("add-person-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeVisibleAsync();

        var focused = await page.EvaluateAsync<string?>(
            "() => document.activeElement?.getAttribute('data-testid')");

        Assert.Equal("quick-first-name", focused);
    }

    [Fact]
    public async Task QuickAddClosesOnEscapeWithoutSaving()
    {
        var page = await OpenAsync();

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Grace");
        // OnKeyDown is bound to the panel, and focus only reaches it after three
        // interop round trips. Pressing Escape before that sends the key to
        // <body>, where nothing handles it — which is why this flaked under
        // full-suite load and passed in isolation.
        await Assertions.Expect(page.GetByTestId("quick-first-name")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByTestId("tree-stats")).ToHaveTextAsync("0 people · 0 relationships");
    }

    // A duplicate is a warning, not a rejection — and it has to survive the
    // navigation to the new person's profile, which is why it is not a
    // self-dismissing toast.
    [Fact]
    public async Task ADuplicateWarnsButStillSaves()
    {
        var page = await OpenAsync("people/add");
        await FillPersonAsync(page, "Ada", "Lovelace", "1815");
        await page.GetByTestId("save-person").ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-name")).ToBeVisibleAsync();

        await page.GotoAsync(fixture.BaseUrl + "people/add", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await FillPersonAsync(page, "Ada", "Lovelace", "1815");
        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("profile-name")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid=toast]").Filter(new()
        {
            HasText = "already exists",
        })).ToBeVisibleAsync();

        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await Assertions.Expect(page.GetByTestId("people-count")).ToHaveTextAsync("2 people");
    }

    [Fact]
    public async Task ADeceasedPersonIsBadgedAndAged()
    {
        var page = await OpenAsync("people/add");

        await FillPersonAsync(page, "Ada", "Lovelace", "1815");
        await page.GetByTestId("input-death-year").FillAsync("1852");
        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("profile-deceased")).ToBeVisibleAsync();
        // Year-only dates cannot give a precise age, so it must not claim one.
        await Assertions.Expect(page.GetByTestId("profile-age")).ToHaveTextAsync("~37 years");
    }

    [Fact]
    public async Task ABirthSurnameRendersAsNee()
    {
        var page = await OpenAsync("people/add");

        await FillPersonAsync(page, "Ada", "Lovelace");
        await page.GetByTestId("input-birth-surname").FillAsync("Byron");
        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("profile-name")).ToHaveTextAsync("Ada Lovelace (née Byron)");
    }

    // Phantoms are placeholders for unidentified ancestors and exist only as
    // tree nodes; the sample family ships one, so the list is where it would
    // wrongly surface.
    [Fact]
    public async Task PhantomsNeverAppearInTheList()
    {
        var page = await OpenAsync("settings");
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats")).ToHaveTextAsync("10 people · 14 relationships");

        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        await Assertions.Expect(page.GetByTestId("people-count")).ToHaveTextAsync("10 people");
        await Assertions.Expect(page.GetByTestId("people-list")).Not.ToContainTextAsync("Unknown");
    }

    // US-053: the popover produces a useful record, not just a name.
    [Fact]
    public async Task QuickAddStoresTheDatesAndGenderItCollected()
    {
        var page = await OpenAsync();

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Grace");
        await page.GetByTestId("quick-last-name").FillAsync("Hopper");
        await page.GetByTestId("quick-birth-year").FillAsync("1906");
        await page.GetByTestId("quick-death-year").FillAsync("1992");
        await page.GetByTestId("quick-gender").SelectOptionAsync("Female");
        await page.GetByTestId("quick-save").ClickAsync();

        await Assertions.Expect(page.GetByTestId("profile-name")).ToHaveTextAsync("Grace Hopper");
        await Assertions.Expect(page.GetByTestId("profile-birth")).ToContainTextAsync("1906");
        await Assertions.Expect(page.GetByTestId("profile-death")).ToContainTextAsync("1992");
        await Assertions.Expect(page.GetByTestId("profile-gender")).ToHaveTextAsync("Female");
    }

    // Switching to the full form must not cost what has already been typed, or
    // the escape hatch is worse than never opening the popover.
    [Fact]
    public async Task OpeningTheFullFormCarriesOverWhatWasTyped()
    {
        var page = await OpenAsync();

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Grace");
        await page.GetByTestId("quick-last-name").FillAsync("Hopper");
        await page.GetByTestId("quick-birth-year").FillAsync("1906");
        await page.GetByTestId("quick-gender").SelectOptionAsync("Female");
        await page.GetByTestId("quick-open-full").ClickAsync();

        await Assertions.Expect(page.GetByTestId("input-first-name")).ToHaveValueAsync("Grace");
        await Assertions.Expect(page.GetByTestId("input-last-name")).ToHaveValueAsync("Hopper");
        await Assertions.Expect(page.GetByTestId("input-birth-year")).ToHaveValueAsync("1906");
        await Assertions.Expect(page.GetByTestId("input-gender")).ToHaveValueAsync("Female");
        // Nothing was saved on the way through.
        await Assertions.Expect(page.GetByTestId("tree-stats")).ToHaveTextAsync("0 people · 0 relationships");
    }

    // The escape hatch must not launder a value past the rule that just caught
    // it. The same year, in the same form, has to get the same answer whether it
    // was typed there or carried over — it did not, because the seeding path had
    // its own looser copy of the range and the control only judged keystrokes.
    [Fact]
    public async Task AYearQuickAddRejectsIsStillRejectedAfterOpeningTheFullForm()
    {
        var page = await OpenAsync();

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Carry");
        await page.GetByTestId("quick-last-name").FillAsync("Over");
        await page.GetByTestId("quick-birth-year").FillAsync("5000");
        await page.GetByTestId("quick-open-full").ClickAsync();

        // Carried across rather than dropped, and judged on arrival.
        await Assertions.Expect(page.GetByTestId("input-birth-year")).ToHaveValueAsync("5000");
        await Assertions.Expect(page.GetByTestId("error-input-birth-year")).ToBeVisibleAsync();

        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("tree-stats")).ToHaveTextAsync("0 people · 0 relationships");
    }

    // No PartialDate can hold a year past 9999, which is exactly why the form is
    // seeded with text rather than a date: the page used to have to drop this
    // one and then apologise for it. Now it lands in the box the user corrects
    // it in, and is judged there like any other.
    [Fact]
    public async Task AYearTooLargeForAnyDateStillReachesTheFullForm()
    {
        var page = await OpenAsync();

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Carry");
        await page.GetByTestId("quick-last-name").FillAsync("Over");
        await page.GetByTestId("quick-birth-year").FillAsync("30000");
        await page.GetByTestId("quick-open-full").ClickAsync();

        await Assertions.Expect(page.GetByTestId("input-birth-year")).ToHaveValueAsync("30000");
        await Assertions.Expect(page.GetByTestId("error-input-birth-year")).ToBeVisibleAsync();
    }

    // The popover is in the top bar, so it is reachable from the full form
    // itself — and navigating from /people/add to /people/add?... keeps the same
    // component instance. A form that only ever seeds on its first render drops
    // everything carried over on that path.
    [Fact]
    public async Task OpeningTheFullFormWhileAlreadyOnItStillCarriesOverWhatWasTyped()
    {
        var page = await OpenAsync("people/add");

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Grace");
        await page.GetByTestId("quick-last-name").FillAsync("Hopper");
        await page.GetByTestId("quick-birth-year").FillAsync("1906");
        await page.GetByTestId("quick-open-full").ClickAsync();

        await Assertions.Expect(page.GetByTestId("input-first-name")).ToHaveValueAsync("Grace");
        await Assertions.Expect(page.GetByTestId("input-last-name")).ToHaveValueAsync("Hopper");
        await Assertions.Expect(page.GetByTestId("input-birth-year")).ToHaveValueAsync("1906");
    }

    // The same path with a value that cannot be stored: an empty box and no
    // error is exactly the silent loss this control exists to prevent.
    [Fact]
    public async Task AnUncarriableYearIsStillReportedWhenTheFullFormIsAlreadyOpen()
    {
        var page = await OpenAsync("people/add");

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Carry");
        await page.GetByTestId("quick-last-name").FillAsync("Over");
        await page.GetByTestId("quick-birth-year").FillAsync("99999999999");
        await page.GetByTestId("quick-open-full").ClickAsync();

        await Assertions.Expect(page.GetByTestId("input-birth-year")).ToHaveValueAsync("99999999999");
        await Assertions.Expect(page.GetByTestId("error-input-birth-year")).ToBeVisibleAsync();
    }

    // Navigating back to a bare people/add must not leave the previous
    // carry-over behind, or the form arrives pre-filled with someone else.
    [Fact]
    public async Task ReturningToABareAddFormDoesNotKeepTheEarlierCarryOver()
    {
        var page = await OpenAsync("people/add?first=Grace&last=Hopper&born=1906");
        await Assertions.Expect(page.GetByTestId("input-first-name")).ToHaveValueAsync("Grace");

        await page.GetByTestId("nav-people").ClickAsync();
        await Assertions.Expect(page.GetByTestId("people-empty-state")).ToBeVisibleAsync();
        await page.GetByTestId("add-first-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("input-first-name")).ToHaveValueAsync(string.Empty);
        await Assertions.Expect(page.GetByTestId("input-birth-year")).ToHaveValueAsync(string.Empty);
    }

    // The same loss one level earlier: a year too large for Int32 never parsed,
    // so the carry-over dropped it before the query string was even built.
    [Fact]
    public async Task AYearTooLargeToParseStillReachesTheFullForm()
    {
        var page = await OpenAsync();

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Carry");
        await page.GetByTestId("quick-last-name").FillAsync("Over");
        await page.GetByTestId("quick-birth-year").FillAsync("99999999999");
        await page.GetByTestId("quick-open-full").ClickAsync();

        await Assertions.Expect(page.GetByTestId("input-birth-year")).ToHaveValueAsync("99999999999");
        await Assertions.Expect(page.GetByTestId("error-input-birth-year")).ToBeVisibleAsync();
    }

    // The query parameter is bound as text, so a value that is not a number at
    // all survives to be shown and judged rather than being silently discarded
    // by the binder before any code runs.
    [Fact]
    public async Task AHandEditedYearThatIsNotANumberIsShownRatherThanDiscarded()
    {
        var page = await OpenAsync("people/add?first=Carry&born=abc");

        await Assertions.Expect(page.GetByTestId("input-birth-year")).ToHaveValueAsync("abc");
        await Assertions.Expect(page.GetByTestId("error-input-birth-year")).ToBeVisibleAsync();
    }

    // US-045: a full date, stored and read back at the precision it was given.
    [Fact]
    public async Task AFullDateIsSavedAndDisplayedAtFullPrecision()
    {
        var page = await OpenAsync("people/add");

        await FillPersonAsync(page, "Ada", "Lovelace", "1815");
        await page.GetByTestId("input-birth-year-month").SelectOptionAsync("12");
        await page.GetByTestId("input-birth-year-day").FillAsync("10");
        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("profile-birth")).ToContainTextAsync("10 December 1815");

        // Through IndexedDB and back, because month and day are new columns on
        // the stored record's shape as far as this flow is concerned.
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await Assertions.Expect(page.GetByTestId("profile-birth")).ToContainTextAsync("10 December 1815");
    }

    // The precision the source actually recorded, and nothing more. A control
    // that quietly filled in a day would be inventing information.
    [Fact]
    public async Task AYearAndMonthIsSavedWithoutInventingADay()
    {
        var page = await OpenAsync("people/add");

        await FillPersonAsync(page, "Ada", "Lovelace", "1815");
        await page.GetByTestId("input-birth-year-month").SelectOptionAsync("12");
        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("profile-birth")).ToContainTextAsync("December 1815");
        await Assertions.Expect(page.GetByTestId("profile-birth")).Not.ToContainTextAsync("1 December");
    }

    // A day without a month is not a date — ExportedDate rejects the pairing on
    // import — so clearing the month has to take the day with it rather than
    // leaving a combination the app cannot store.
    [Fact]
    public async Task ClearingTheMonthClearsTheDayAndSavesTheYearAlone()
    {
        var page = await OpenAsync("people/add");

        await FillPersonAsync(page, "Ada", "Lovelace", "1815");
        await page.GetByTestId("input-birth-year-month").SelectOptionAsync("12");
        await page.GetByTestId("input-birth-year-day").FillAsync("10");

        await page.GetByTestId("input-birth-year-month").SelectOptionAsync(string.Empty);

        await Assertions.Expect(page.GetByTestId("input-birth-year-day")).ToHaveValueAsync(string.Empty);

        await page.GetByTestId("save-person").ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-birth")).ToContainTextAsync("1815");
        await Assertions.Expect(page.GetByTestId("profile-birth")).Not.ToContainTextAsync("December");
    }

    // PartialDate validates the day eagerly through DateTime.DaysInMonth and
    // throws, and an exception reaching the Blazor renderer is a dead page with
    // an error banner rather than a message the user can act on.
    [Fact]
    public async Task AnImpossibleDayIsReportedRatherThanCrashingTheApp()
    {
        var page = await OpenAsync("people/add");

        await FillPersonAsync(page, "Ada", "Lovelace", "1815");
        await page.GetByTestId("input-birth-year-month").SelectOptionAsync("2");
        await page.GetByTestId("input-birth-year-day").FillAsync("31");

        await Assertions.Expect(page.GetByTestId("error-input-birth-year"))
            .ToContainTextAsync("Day must be between 1 and 28");
        await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();

        await page.GetByTestId("save-person").ClickAsync();
        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await Assertions.Expect(page.GetByTestId("people-empty-state")).ToBeVisibleAsync();
    }

    // US-045's display criterion at full precision, and the age's "~" prefix,
    // which is what an approximate date is *for*.
    [Fact]
    public async Task AnApproximateDateIsMarkedOnBothTheDateAndTheAge()
    {
        var page = await OpenAsync("people/add");

        await FillPersonAsync(page, "Ada", "Lovelace", "1815");
        await page.GetByTestId("input-birth-year-approx").CheckAsync();
        await page.GetByTestId("input-death-year").FillAsync("1852");
        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("profile-birth")).ToContainTextAsync("c. 1815");
        await Assertions.Expect(page.GetByTestId("profile-age")).ToHaveTextAsync("~37 years");
    }

    // US-006: a note is research the user typed. Silently dropping its tail at
    // the storage layer would be data loss, so the limit is visible while typing.
    [Fact]
    public async Task NotesShowALiveCharacterCount()
    {
        var page = await OpenAsync("people/add");

        await Assertions.Expect(page.GetByTestId("notes-count")).ToHaveTextAsync("0 / 5000");

        var note = new string('x', 42);
        await page.GetByTestId("input-notes").FillAsync(note);

        await Assertions.Expect(page.GetByTestId("notes-count")).ToHaveTextAsync($"{note.Length} / 5000");
    }

    [Fact]
    public async Task NotesCannotExceedTheLimit()
    {
        var page = await OpenAsync("people/add");

        await page.GetByTestId("input-notes").FillAsync(new string('x', 5200));

        var length = await page.GetByTestId("input-notes").EvaluateAsync<int>("el => el.value.length");
        Assert.Equal(5000, length);
    }

    // On a narrow screen the top-bar action goes to the full form rather than
    // opening a popover. With the keyboard up there is not enough height for the
    // popover to fit, so it could only scroll — a dialog scrolling inside a
    // shrunken viewport over a pinned body. The form is an ordinary page.
    [Fact]
    public async Task OnANarrowScreenAddPersonGoesStraightToTheFullForm()
    {
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + "settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        await page.GetByTestId("add-person-button").ClickAsync();

        await Assertions.Expect(page.GetByTestId("input-first-name")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeHiddenAsync();
    }

    // The popover still earns its place where there is room for it.
    [Fact]
    public async Task OnAWideScreenAddPersonStillOpensThePopover()
    {
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + "settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        await page.GetByTestId("add-person-button").ClickAsync();

        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeVisibleAsync();
    }

    // An out-of-range year must be rejected by the app, not by the browser.
    // Native min/max blocks the form submit before Blazor sees it, so nothing
    // renders and the button reads as dead.
    [Fact]
    public async Task AnOutOfRangeYearIsRejectedWithAnInAppMessage()
    {
        var page = await OpenAsync("people/add");

        await FillPersonAsync(page, "Ada", "Lovelace", "30000");
        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("error-input-birth-year")).ToBeVisibleAsync();

        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await Assertions.Expect(page.GetByTestId("people-empty-state")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CorrectingAnOutOfRangeYearLetsTheSaveThrough()
    {
        var page = await OpenAsync("people/add");

        await FillPersonAsync(page, "Ada", "Lovelace", "30000");
        await page.GetByTestId("save-person").ClickAsync();
        await Assertions.Expect(page.GetByTestId("error-input-birth-year")).ToBeVisibleAsync();

        await page.GetByTestId("input-birth-year").FillAsync("1815");
        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("profile-name")).ToHaveTextAsync("Ada Lovelace");
    }

    [Fact]
    public async Task QuickAddRejectsAnOutOfRangeYearWithAnInAppMessage()
    {
        var page = await OpenAsync();

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Grace");
        await page.GetByTestId("quick-last-name").FillAsync("Hopper");
        await page.GetByTestId("quick-birth-year").FillAsync("30000");
        await page.GetByTestId("quick-save").ClickAsync();

        await Assertions.Expect(page.GetByTestId("quick-add-error")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("tree-stats")).ToHaveTextAsync("0 people · 0 relationships");
    }

    // Correcting a neighbouring field must not throw away the user's precision
    // judgement. Clearing the year emptied the whole PartialDate, and the circa
    // flag was re-seeded from it.
    [Fact]
    public async Task ApproximateSurvivesClearingAndRetypingTheYear()
    {
        var page = await OpenAsync("people/add");
        await FillPersonAsync(page, "Ada", "Lovelace", "1815");

        await page.GetByTestId("input-birth-year-approx").CheckAsync();
        await page.GetByTestId("input-birth-year").FillAsync(string.Empty);
        await page.GetByTestId("input-birth-year").FillAsync("1820");

        await Assertions.Expect(page.GetByTestId("input-birth-year-approx")).ToBeCheckedAsync();

        await page.GetByTestId("save-person").ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-birth")).ToContainTextAsync("c. 1820");
    }

    // The 5,000-character cap has to be a rule, not just a maxlength attribute:
    // anything reaching the service another way — a paste handler, an import, a
    // future API client — bypasses the textarea entirely.
    [Fact]
    public async Task NotesBeyondTheLimitAreRejectedEvenWhenTheAttributeIsBypassed()
    {
        var page = await OpenAsync("people/add");
        await FillPersonAsync(page, "Ada", "Lovelace");

        // Sets the value past maxlength and raises the event Blazor listens for.
        await page.EvaluateAsync(@"() => {
            const el = document.querySelector('[data-testid=input-notes]');
            const setter = Object.getOwnPropertyDescriptor(
                window.HTMLTextAreaElement.prototype, 'value').set;
            setter.call(el, 'x'.repeat(9000));
            el.dispatchEvent(new Event('input', { bubbles: true }));
        }");

        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("form-error")).ToBeVisibleAsync();

        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await Assertions.Expect(page.GetByTestId("people-empty-state")).ToBeVisibleAsync();
    }

    // A year too large for Int32 is "typed something impossible", not "left it
    // blank" — but a nullable int cannot tell those apart. The full form reasons
    // from the raw string and catches it; the popover must do the same or the
    // same failure survives on one of the two paths.
    [Fact]
    public async Task QuickAddRejectsAYearTooLargeToParse()
    {
        var page = await OpenAsync();

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Grace");
        await page.GetByTestId("quick-last-name").FillAsync("Hopper");
        await page.GetByTestId("quick-birth-year").FillAsync("99999999999");
        await page.GetByTestId("quick-save").ClickAsync();

        await Assertions.Expect(page.GetByTestId("quick-add-error")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("tree-stats")).ToHaveTextAsync("0 people · 0 relationships");
    }

    // Only reachable now that the box is a text input: a number input would not
    // have accepted the keystrokes, which sounds like a guard but only meant the
    // rule below had never been exercised from the keyboard.
    [Fact]
    public async Task QuickAddRejectsAYearThatIsNotANumber()
    {
        var page = await OpenAsync();

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Grace");
        await page.GetByTestId("quick-last-name").FillAsync("Hopper");
        await page.GetByTestId("quick-birth-year").FillAsync("abc");
        await page.GetByTestId("quick-save").ClickAsync();

        await Assertions.Expect(page.GetByTestId("quick-add-error")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("tree-stats")).ToHaveTextAsync("0 people · 0 relationships");
    }

    // The other half of the parity: the full form already handles this, and a
    // test here stops the two drifting apart again.
    [Fact]
    public async Task TheFullFormRejectsAYearTooLargeToParse()
    {
        var page = await OpenAsync("people/add");

        await FillPersonAsync(page, "Grace", "Hopper", "99999999999");
        await page.GetByTestId("save-person").ClickAsync();

        await Assertions.Expect(page.GetByTestId("error-input-birth-year")).ToBeVisibleAsync();

        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await Assertions.Expect(page.GetByTestId("people-empty-state")).ToBeVisibleAsync();
    }
}
