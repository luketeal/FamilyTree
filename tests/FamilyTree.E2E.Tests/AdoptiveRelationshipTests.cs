using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// Recording adoptive relationships in a real browser, against the published
/// site. US-014 to US-020, US-039.
/// </summary>
/// <remarks>
/// The sample family is built for this: Priya has a recorded biological mother
/// and two adoptive parents, which is US-039's acceptance criterion as data
/// rather than as a fixture invented here.
/// </remarks>
[Collection(nameof(StaticSiteCollection))]
public class AdoptiveRelationshipTests(StaticSiteFixture fixture) : BrowserTest(fixture)
{
    private async Task<IPage> OpenAsync(string path = "settings", int width = 1440, int height = 900)
    {
        var page = await NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        await page.GotoAsync(Fixture.BaseUrl + path, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        return page;
    }

    private static async Task LoadSampleAsync(IPage page)
    {
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");
    }

    private async Task OpenProfileAsync(IPage page, string displayName)
    {
        await page.GotoAsync(Fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByText(displayName, new PageGetByTextOptions { Exact = false }).First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-name")).ToBeVisibleAsync();
    }

    /// <summary>Walks the wizard from an Add button through to Save.</summary>
    private static async Task AddAdoptiveAsync(
        IPage page, string trigger, string personName, string? adoptionYear = null)
    {
        await page.GetByTestId(trigger).ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeVisibleAsync();

        await page.GetByTestId("relationship-dialog-search-query").FillAsync(personName);
        await page.GetByTestId("relationship-dialog-search-results")
            .GetByText(personName, new LocatorGetByTextOptions { Exact = false })
            .First.ClickAsync();

        await page.GetByTestId("relationship-dialog-next").ClickAsync();

        if (adoptionYear is not null)
        {
            await page.GetByTestId("relationship-dialog-adoption-date").FillAsync(adoptionYear);
        }

        await page.GetByTestId("relationship-dialog-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeHiddenAsync();
    }

    // ---- Reading what is already there (US-015, US-019) ----

    [Fact]
    public async Task AProfileListsTheAdoptiveParents()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Priya Hartley");

        await Assertions.Expect(page.GetByTestId("adoptive-parents-list"))
            .ToContainTextAsync("Susan Hartley");
        await Assertions.Expect(page.GetByTestId("adoptive-parents-list"))
            .ToContainTextAsync("Raymond Hartley");
    }

    [Fact]
    public async Task EachAdoptiveParentIsLabelledAdoptiveWithItsDate()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Priya Hartley");

        var list = page.GetByTestId("adoptive-parents-list");
        await Assertions.Expect(list).ToContainTextAsync("(Adoptive)");
        await Assertions.Expect(list).ToContainTextAsync("1977");
    }

    [Fact]
    public async Task AProfileListsTheAdoptiveChildren()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Susan Hartley");

        await Assertions.Expect(page.GetByTestId("adoptive-children-list"))
            .ToContainTextAsync("Priya Hartley");
    }

    [Fact]
    public async Task SomebodyWithNoAdoptiveLinksIsToldSo()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Thomas Whitfield");

        await Assertions.Expect(page.GetByTestId("adoptive-parents-empty")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("adoptive-children-empty")).ToBeVisibleAsync();
    }

    // US-039's acceptance criterion end to end, on the one person in the sample
    // who carries both: Anita is Priya's biological mother, the Hartleys adopted
    // her, and both sections are on screen at once.
    [Fact]
    public async Task BiologicalAndAdoptiveParentsAppearTogether()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Priya Hartley");

        await Assertions.Expect(page.GetByTestId("parents-list"))
            .ToContainTextAsync("Anita Chandra");
        await Assertions.Expect(page.GetByTestId("adoptive-parents-list"))
            .ToContainTextAsync("Susan Hartley");
    }

    // ---- Recording one (US-014, US-018) ----

    [Fact]
    public async Task ARecordedAdoptiveParentSurvivesAReload()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");
        await AddAdoptiveAsync(page, "add-adoptive-parent", "Vera Whitfield", "1968");

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });

        await Assertions.Expect(page.GetByTestId("adoptive-parents-list"))
            .ToContainTextAsync("Vera Whitfield");
        await Assertions.Expect(page.GetByTestId("adoptive-parents-list"))
            .ToContainTextAsync("1968");
    }

    // The reciprocal entry US-014 asks for. One record is both directions, so
    // this is checked from the other profile rather than assumed.
    [Fact]
    public async Task TheAdoptiveParentGainsTheChildOnTheirOwnProfile()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");
        await AddAdoptiveAsync(page, "add-adoptive-parent", "Vera Whitfield", "1968");

        await OpenProfileAsync(page, "Vera Whitfield");

        await Assertions.Expect(page.GetByTestId("adoptive-children-list"))
            .ToContainTextAsync("Daniel Whitfield");
    }

    // US-015 asks for the absence to be said rather than left blank. Recorded
    // through the UI with the date left empty, which is the path a user takes
    // when the record simply does not say.
    [Fact]
    public async Task AnAdoptionWithNoDateSaysSo()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");
        await AddAdoptiveAsync(page, "add-adoptive-parent", "Vera Whitfield");

        await Assertions.Expect(page.GetByTestId("adoptive-parents-list"))
            .ToContainTextAsync("Date unknown");
    }

    // The one behavioural difference from biological parenthood, in the browser:
    // Priya already has two adoptive parents and a third is accepted.
    [Fact]
    public async Task AThirdAdoptiveParentIsAccepted()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Priya Hartley");

        await AddAdoptiveAsync(page, "add-adoptive-parent", "Vera Whitfield", "1980");

        await Assertions.Expect(page.GetByTestId("adoptive-parents-list"))
            .ToContainTextAsync("Vera Whitfield");
        await Assertions.Expect(
            page.Locator("[data-testid='adoptive-parents-list'] li")).ToHaveCountAsync(3);
    }

    // Adding an adoptive parent must not disturb the two biological slots — the
    // failure mode US-039 exists to prevent.
    [Fact]
    public async Task AddingAnAdoptiveParentLeavesTheBiologicalSlotsAlone()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Eleanor Hartley");

        await AddAdoptiveAsync(page, "add-adoptive-parent", "Vera Whitfield", "1974");

        await Assertions.Expect(page.Locator("[data-testid='parents-list'] li")).ToHaveCountAsync(2);
        await Assertions.Expect(page.GetByTestId("parents-list")).ToContainTextAsync("Susan Hartley");
    }

    // US-040 through the adoptive path: Arthur's granddaughter cannot become his
    // adoptive mother. Two generations apart so the cycle check is the only
    // thing that can refuse it.
    [Fact]
    public async Task TheWizardRefusesAnAdoptiveParentWhoIsAlreadyADescendant()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Arthur Whitfield");

        await page.GetByTestId("add-adoptive-parent").ClickAsync();
        await page.GetByTestId("relationship-dialog-search-query").FillAsync("Eleanor");
        await page.GetByTestId("relationship-dialog-search-results")
            .GetByText("Eleanor Hartley").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await page.GetByTestId("relationship-dialog-save").ClickAsync();

        await Assertions.Expect(page.GetByTestId("relationship-dialog-error"))
            .ToContainTextAsync("already a descendant of Arthur Whitfield");
    }

    // Already recorded as an adoptive parent, so never offered — the wizard does
    // not present a choice whose only outcome is an error.
    [Fact]
    public async Task TheWizardDoesNotOfferAnAdoptiveParentWhoIsAlreadyRecorded()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Priya Hartley");

        await page.GetByTestId("add-adoptive-parent").ClickAsync();
        await page.GetByTestId("relationship-dialog-search-query").FillAsync("Hartley");

        // Eleanor shares the surname and is not an adoptive parent of Priya, so
        // she proves the list is populated — without which "Susan is absent"
        // would pass on an empty list.
        await Assertions.Expect(page.GetByTestId("relationship-dialog-search-results"))
            .ToContainTextAsync("Eleanor Hartley");
        await Assertions.Expect(page.GetByTestId("relationship-dialog-search-results"))
            .Not.ToContainTextAsync("Susan Hartley");
    }

    // The exclusion is scoped to the kind, which US-039 requires: Anita is
    // Priya's biological mother and that must not stop her being recorded as an
    // adoptive parent too.
    [Fact]
    public async Task TheWizardStillOffersSomebodyLinkedTheOtherWay()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Priya Hartley");

        await page.GetByTestId("add-adoptive-parent").ClickAsync();
        await page.GetByTestId("relationship-dialog-search-query").FillAsync("Anita");

        await Assertions.Expect(page.GetByTestId("relationship-dialog-search-results"))
            .ToContainTextAsync("Anita Chandra");
    }

    // ---- Editing (US-016) ----

    [Fact]
    public async Task EditingTheAdoptionDatePersists()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Priya Hartley");

        await page.Locator("[data-testid^='edit-adoptive-parent-']").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await page.GetByTestId("relationship-dialog-adoption-date").FillAsync("1978");
        await page.GetByTestId("relationship-dialog-save").ClickAsync();

        // The dialog closes only after the write has been awaited, so this is
        // what "the save finished" looks like from outside. Reloading straight
        // off the click races the IndexedDB round trip and reads the old value.
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByTestId("adoptive-parents-list")).ToContainTextAsync("1978");

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });

        await Assertions.Expect(page.GetByTestId("adoptive-parents-list")).ToContainTextAsync("1978");
    }

    // The other half of US-016: the same control changes who the adoptive parent
    // is, and the swap is one mutation rather than a removal followed by an add.
    [Fact]
    public async Task EditingCanSwapTheAdoptiveParent()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Priya Hartley");

        await page.Locator("[data-testid^='edit-adoptive-parent-']").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-search-query").FillAsync("Vera");
        await page.GetByTestId("relationship-dialog-search-results")
            .GetByText("Vera Whitfield").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await page.GetByTestId("relationship-dialog-save").ClickAsync();

        await Assertions.Expect(page.GetByTestId("adoptive-parents-list"))
            .ToContainTextAsync("Vera Whitfield");
        await Assertions.Expect(
            page.Locator("[data-testid='adoptive-parents-list'] li")).ToHaveCountAsync(2);
    }

    // ---- Removing (US-017, US-020) ----

    [Fact]
    public async Task RemovingAnAdoptiveParentAsksFirst()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Priya Hartley");

        await page.Locator("[data-testid^='remove-adoptive-parent-']").First.ClickAsync();

        await Assertions.Expect(page.GetByTestId("remove-link-modal-body"))
            .ToContainTextAsync("the relationship only");
        await Assertions.Expect(
            page.Locator("[data-testid='adoptive-parents-list'] li")).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task RemovingAnAdoptiveParentSeversTheLinkAndKeepsBothPeople()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Priya Hartley");

        await page.Locator("[data-testid^='remove-adoptive-parent-']").First.ClickAsync();
        await page.GetByTestId("confirm-remove-link").ClickAsync();

        await Assertions.Expect(
            page.Locator("[data-testid='adoptive-parents-list'] li")).ToHaveCountAsync(1);

        await page.GotoAsync(Fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await Assertions.Expect(page.GetByTestId("people-list")).ToContainTextAsync("Susan Hartley");
        await Assertions.Expect(page.GetByTestId("people-list")).ToContainTextAsync("Priya Hartley");
    }

    // Removing the adoptive link must leave the biological one standing — they
    // are separate records about separate facts (US-039).
    [Fact]
    public async Task RemovingAnAdoptiveParentLeavesTheBiologicalOne()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Priya Hartley");

        await page.Locator("[data-testid^='remove-adoptive-parent-']").First.ClickAsync();
        await page.GetByTestId("confirm-remove-link").ClickAsync();

        await Assertions.Expect(page.GetByTestId("parents-list"))
            .ToContainTextAsync("Anita Chandra");
    }

    // ---- Export coverage ----

    // Adoptive links already round-trip through export as of PR 5, and this PR
    // is the first that can create one through the UI. Asserted end to end so
    // "already covered" is a fact rather than a reading of the export code.
    [Fact]
    public async Task AnAdoptiveLinkAddedInTheUiIsInTheExport()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");
        await AddAdoptiveAsync(page, "add-adoptive-parent", "Vera Whitfield", "1968");

        await page.GotoAsync(Fixture.BaseUrl + "export", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await page.GetByTestId("export-download").ClickAsync();
        });

        using var stream = await download.CreateReadStreamAsync();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();

        Assert.Contains("adoptiveLinks", json);
        Assert.Contains("1968", json);
    }
}
