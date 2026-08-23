using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// Recording biological relationships in a real browser, against the published
/// site. US-007 to US-013, US-037, US-051.
/// </summary>
/// <remarks>
/// bUnit covers the same markup and cannot cover this: the wizard is an overlay
/// that pins the page behind it, the writes go through IndexedDB, and the
/// property that matters most — a link is still there after a reload — spans
/// both.
/// </remarks>
[Collection(nameof(StaticSiteCollection))]
public class RelationshipTests(StaticSiteFixture fixture)
{
    private async Task<IPage> OpenAsync(string path = "settings", int width = 1440, int height = 900)
    {
        var page = await fixture.Browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        await page.GotoAsync(fixture.BaseUrl + path, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        return page;
    }

    /// <summary>Loads the demonstration family, which already holds the hard cases.</summary>
    private static async Task LoadSampleAsync(IPage page)
    {
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");
    }

    /// <summary>Opens somebody's profile from the people list, by the name shown there.</summary>
    private async Task OpenProfileAsync(IPage page, string displayName)
    {
        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByText(displayName, new PageGetByTextOptions { Exact = false }).First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-name")).ToBeVisibleAsync();
    }

    // ---- Reading what is already there (US-008, US-012, US-051) ----

    [Fact]
    public async Task AProfileListsTheBiologicalParents()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Susan Hartley");

        await Assertions.Expect(page.GetByTestId("parents-list"))
            .ToContainTextAsync("Arthur Whitfield");
        await Assertions.Expect(page.GetByTestId("parents-list"))
            .ToContainTextAsync("Margaret Whitfield");
    }

    [Fact]
    public async Task AProfileListsTheBiologicalChildren()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Susan Hartley");

        await Assertions.Expect(page.GetByTestId("children-list"))
            .ToContainTextAsync("Eleanor Hartley");
    }

    // US-037's acceptance criterion, end to end: Thomas shares both parents with
    // Susan and Daniel shares only Arthur.
    [Fact]
    public async Task SiblingsAreLabelledFullOrHalf()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Susan Hartley");

        var siblings = page.GetByTestId("siblings-list");
        await Assertions.Expect(siblings).ToContainTextAsync("Thomas Whitfield");
        await Assertions.Expect(siblings).ToContainTextAsync("(Full)");
        await Assertions.Expect(siblings).ToContainTextAsync("Daniel Whitfield");
        await Assertions.Expect(siblings).ToContainTextAsync("(Half)");
    }

    [Fact]
    public async Task SomebodyWithNoSiblingsIsToldSo()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Anita Chandra");

        await Assertions.Expect(page.GetByTestId("siblings-empty"))
            .ToHaveTextAsync("No known siblings");
    }

    // ---- Recording one (US-007) ----

    /// <summary>Walks the wizard from an Add button through to Save.</summary>
    private static async Task AddRelationshipAsync(IPage page, string trigger, string personName)
    {
        await page.GetByTestId(trigger).ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeVisibleAsync();

        await page.GetByTestId("relationship-dialog-search-query").FillAsync(personName);
        await page.GetByTestId("relationship-dialog-search-results")
            .GetByText(personName, new LocatorGetByTextOptions { Exact = false })
            .First.ClickAsync();

        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await page.GetByTestId("relationship-dialog-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task ARecordedParentAppearsOnTheProfile()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");

        await AddRelationshipAsync(page, "add-biological-parent", "Vera Whitfield");

        await Assertions.Expect(page.GetByTestId("parents-list"))
            .ToContainTextAsync("Vera Whitfield");
    }

    // A link that only lasted until the tab closed would not be a link. This is
    // the assertion the whole storage layer exists for, and the one a component
    // test cannot make.
    [Fact]
    public async Task ARecordedParentSurvivesAReload()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");
        await AddRelationshipAsync(page, "add-biological-parent", "Vera Whitfield");

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });

        await Assertions.Expect(page.GetByTestId("parents-list"))
            .ToContainTextAsync("Vera Whitfield");
    }

    // The reciprocal entry US-007 asks for. One record is both directions, so
    // this is checked from the other profile rather than assumed.
    [Fact]
    public async Task TheParentGainsTheChildOnTheirOwnProfile()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");
        await AddRelationshipAsync(page, "add-biological-parent", "Vera Whitfield");

        await OpenProfileAsync(page, "Vera Whitfield");

        await Assertions.Expect(page.GetByTestId("children-list"))
            .ToContainTextAsync("Daniel Whitfield");
    }

    // Adding a second parent makes Daniel and nobody else share Vera, so the
    // sibling computation has to change with it rather than being a snapshot.
    [Fact]
    public async Task AddingASharedParentCreatesAHalfSibling()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);

        // Stated as the empty state rather than as "the list lacks this name":
        // Eleanor has no siblings at all yet, so a Not.ToContainText against a
        // list that does not exist would pass without proving anything.
        await OpenProfileAsync(page, "Eleanor Hartley");
        await Assertions.Expect(page.GetByTestId("siblings-empty")).ToBeVisibleAsync();

        await OpenProfileAsync(page, "Daniel Whitfield");
        await AddRelationshipAsync(page, "add-biological-parent", "Susan Hartley");

        await OpenProfileAsync(page, "Eleanor Hartley");
        await Assertions.Expect(page.GetByTestId("siblings-list"))
            .ToContainTextAsync("Daniel Whitfield");
    }

    [Fact]
    public async Task TheWizardRefusesADuplicateParentAndStaysOpen()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");

        await page.GetByTestId("add-biological-parent").ClickAsync();
        await page.GetByTestId("relationship-dialog-search-query").FillAsync("Arthur");
        await page.GetByTestId("relationship-dialog-search-results")
            .GetByText("Arthur Whitfield").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await page.GetByTestId("relationship-dialog-save").ClickAsync();

        await Assertions.Expect(page.GetByTestId("relationship-dialog-error"))
            .ToContainTextAsync("already recorded as a biological parent");
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeVisibleAsync();
    }

    // US-040 in a real browser: Arthur's granddaughter cannot become his mother.
    //
    // Deliberately two generations apart. A direct parent is already on the
    // exclusion list and so never offered, and anyone with two recorded parents
    // trips the cap before the ancestor walk runs — either way the test would
    // pass on the wrong error. Arthur has no parents recorded and Eleanor is his
    // granddaughter through Susan, so the cycle check is the only thing that can
    // refuse this.
    [Fact]
    public async Task TheWizardRefusesAParentWhoIsAlreadyADescendant()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Arthur Whitfield");

        await page.GetByTestId("add-biological-parent").ClickAsync();
        await page.GetByTestId("relationship-dialog-search-query").FillAsync("Eleanor");
        await page.GetByTestId("relationship-dialog-search-results")
            .GetByText("Eleanor Hartley").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await page.GetByTestId("relationship-dialog-save").ClickAsync();

        await Assertions.Expect(page.GetByTestId("relationship-dialog-error"))
            .ToContainTextAsync("already a descendant of Arthur Whitfield");
    }

    // ---- Creating somebody inline (US-007) ----

    [Fact]
    public async Task SomebodyNotInTheTreeCanBeCreatedWithoutLeavingTheWizard()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");

        await page.GetByTestId("add-biological-parent").ClickAsync();
        await page.GetByTestId("relationship-dialog-search-query").FillAsync("Norah Bell");
        await page.GetByTestId("relationship-dialog-search-create").ClickAsync();
        await page.GetByTestId("relationship-dialog-search-create-born").FillAsync("1925");
        await page.GetByTestId("relationship-dialog-search-create-save").ClickAsync();

        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await page.GetByTestId("relationship-dialog-save").ClickAsync();

        await Assertions.Expect(page.GetByTestId("parents-list")).ToContainTextAsync("Norah Bell");
    }

    // ---- Removing (US-010, US-013) ----

    [Fact]
    public async Task RemovingAParentAsksFirst()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");

        await page.Locator("[data-testid^='remove-parent-']").First.ClickAsync();

        await Assertions.Expect(page.GetByTestId("remove-link-modal-body"))
            .ToContainTextAsync("the relationship only");
        await Assertions.Expect(page.GetByTestId("parents-list"))
            .ToContainTextAsync("Arthur Whitfield");
    }

    [Fact]
    public async Task RemovingAParentSeversTheLinkAndKeepsBothPeople()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");

        await page.Locator("[data-testid^='remove-parent-']").First.ClickAsync();
        await page.GetByTestId("confirm-remove-link").ClickAsync();

        await Assertions.Expect(page.GetByTestId("parent-unknown-slot")).ToHaveCountAsync(2);

        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await Assertions.Expect(page.GetByTestId("people-list")).ToContainTextAsync("Arthur Whitfield");
        await Assertions.Expect(page.GetByTestId("people-list")).ToContainTextAsync("Daniel Whitfield");
    }

    // ---- Replacing (US-009) ----

    [Fact]
    public async Task ReplacingAParentNamesBothBeforeSaving()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");

        await page.Locator("[data-testid^='replace-parent-']").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-search-query").FillAsync("Vera");
        await page.GetByTestId("relationship-dialog-search-results")
            .GetByText("Vera Whitfield").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-next").ClickAsync();

        var summary = page.GetByTestId("relationship-dialog-summary");
        await Assertions.Expect(summary).ToContainTextAsync("Arthur Whitfield");
        await Assertions.Expect(summary).ToContainTextAsync("Vera Whitfield");
    }

    [Fact]
    public async Task ReplacingAParentSwapsThemOnTheProfile()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");

        await page.Locator("[data-testid^='replace-parent-']").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-search-query").FillAsync("Vera");
        await page.GetByTestId("relationship-dialog-search-results")
            .GetByText("Vera Whitfield").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await page.GetByTestId("relationship-dialog-save").ClickAsync();

        await Assertions.Expect(page.GetByTestId("parents-list")).ToContainTextAsync("Vera Whitfield");
        await Assertions.Expect(page.GetByTestId("parents-list"))
            .Not.ToContainTextAsync("Arthur Whitfield");
    }

    // ---- The unidentified ancestor (US-041, US-054) ----

    [Fact]
    public async Task AnUnidentifiedAncestorShowsAsAnUnknownParent()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Margaret Whitfield");

        await Assertions.Expect(page.GetByTestId("parents-list")).ToContainTextAsync("Unknown");
        // One recorded placeholder plus one genuinely empty slot.
        await Assertions.Expect(page.GetByTestId("parent-unknown-slot")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task SomebodyWithNoParentsShowsTwoEmptySlots()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Anita Chandra");

        await Assertions.Expect(page.GetByTestId("parent-unknown-slot")).ToHaveCountAsync(2);
    }
}
