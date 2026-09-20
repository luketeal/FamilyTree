using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// Recording marriages and step relationships in a real browser, against the
/// published site. US-021 to US-027, US-038, US-042, US-044.
/// </summary>
/// <remarks>
/// The sample family is built for this: Arthur was widowed in 1973 and remarried
/// in 1976, and Vera is recorded as Daniel's stepmother. That is US-042's
/// acceptance criterion and US-038's as data rather than as a fixture invented
/// here.
/// </remarks>
[Collection(nameof(StaticSiteCollection))]
public class MarriageTests(StaticSiteFixture fixture) : BrowserTest(fixture)
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
            .ToHaveTextAsync("10 people · 15 relationships");
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

    /// <summary>Walks the wizard from the Add button through to Save.</summary>
    private static async Task AddMarriageAsync(
        IPage page,
        string spouseName,
        string startYear,
        string? place = null,
        string? endReason = null,
        string? endYear = null)
    {
        await page.GetByTestId("add-marriage").ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeVisibleAsync();

        await page.GetByTestId("relationship-dialog-search-query").FillAsync(spouseName);
        await page.GetByTestId("relationship-dialog-search-results")
            .GetByText(spouseName, new LocatorGetByTextOptions { Exact = false })
            .First.ClickAsync();

        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await page.GetByTestId("relationship-dialog-start-date").FillAsync(startYear);

        if (place is not null)
        {
            await page.GetByTestId("relationship-dialog-place").FillAsync(place);
        }

        if (endReason is not null)
        {
            await page.GetByTestId("relationship-dialog-end-reason")
                .SelectOptionAsync(new SelectOptionValue { Value = endReason });
        }

        if (endYear is not null)
        {
            await page.GetByTestId("relationship-dialog-end-date").FillAsync(endYear);
        }

        await page.GetByTestId("relationship-dialog-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeHiddenAsync();
    }

    // ---- Reading what is already there (US-022, US-042) ----

    [Fact]
    public async Task AProfileListsTheMarriagesChronologically()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Arthur Whitfield");

        var rows = page.Locator("[data-testid='marriages-list'] > li");
        await Assertions.Expect(rows).ToHaveCountAsync(2);
        await Assertions.Expect(rows.Nth(0)).ToContainTextAsync("Margaret Whitfield");
        await Assertions.Expect(rows.Nth(1)).ToContainTextAsync("Vera Whitfield");
    }

    // US-022 asks for the start date, the end date or "Ongoing", and the reason.
    [Fact]
    public async Task EachMarriageShowsItsDatesAndHowItEnded()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Arthur Whitfield");

        var list = page.GetByTestId("marriages-list");
        await Assertions.Expect(list).ToContainTextAsync("1946 – 1973 (Widowed)");
        await Assertions.Expect(list).ToContainTextAsync("1976 – Ongoing");
    }

    [Fact]
    public async Task TheCurrentMarriageIsMarked()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Arthur Whitfield");

        await Assertions.Expect(
            page.Locator("[data-testid^='marriage-ongoing-']")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task SomebodyWithNoMarriagesIsToldSo()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Thomas Whitfield");

        await Assertions.Expect(page.GetByTestId("marriages-empty")).ToBeVisibleAsync();
    }

    // ---- Recording one (US-021, US-044) ----

    [Fact]
    public async Task ARecordedMarriageSurvivesAReload()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Thomas Whitfield");
        await AddMarriageAsync(page, "Eleanor Hartley", "1975", "York");

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var list = page.GetByTestId("marriages-list");
        await Assertions.Expect(list).ToContainTextAsync("Eleanor Hartley");
        await Assertions.Expect(list).ToContainTextAsync("1975 – Ongoing");
        await Assertions.Expect(list).ToContainTextAsync("York");
    }

    // US-021: one record serves both profiles. Checked from the other end rather
    // than assumed, since a reciprocal row that had to be written separately is
    // exactly what could fall out of step.
    [Fact]
    public async Task TheMarriageAppearsOnBothProfiles()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Thomas Whitfield");
        await AddMarriageAsync(page, "Eleanor Hartley", "1975");

        await OpenProfileAsync(page, "Eleanor Hartley");

        await Assertions.Expect(page.GetByTestId("marriages-list"))
            .ToContainTextAsync("Thomas Whitfield");
    }

    // US-021's last criterion, in the browser: a second current marriage between
    // the same two people is refused, and the dialog stays open saying why.
    [Fact]
    public async Task ASecondCurrentMarriageToTheSamePersonIsRefused()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Arthur Whitfield");

        await page.GetByTestId("add-marriage").ClickAsync();
        await page.GetByTestId("relationship-dialog-search-query").FillAsync("Vera");
        // Vera is Arthur's current spouse, so the search will not offer her: the
        // exclusion list is scoped to current spouses precisely so the only
        // choices left are ones that can be saved.
        await Assertions.Expect(page.GetByTestId("relationship-dialog-search-results"))
            .Not.ToContainTextAsync("Vera Whitfield");
    }

    // US-042: an overlapping marriage warns and saves. Thomas is unmarried in the
    // sample, so this records two overlapping ones through the UI.
    [Fact]
    public async Task AnOverlappingMarriageWarnsAndSaves()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Thomas Whitfield");

        await AddMarriageAsync(page, "Eleanor Hartley", "1975");
        await AddMarriageAsync(page, "Priya Hartley", "1980");

        await Assertions.Expect(page.GetByTestId("toast-region")).ToContainTextAsync("overlaps");
        await Assertions.Expect(
            page.Locator("[data-testid='marriages-list'] > li")).ToHaveCountAsync(2);
    }

    // ---- Ending one (US-025, US-026, US-027) ----

    [Fact]
    public async Task AMarriageCanBeEndedByDivorce()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Susan Hartley");

        var row = page.Locator("[data-testid^='marriage-']").First;
        await page.Locator("[data-testid^='edit-marriage-']").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await page.GetByTestId("relationship-dialog-end-reason")
            .SelectOptionAsync(new SelectOptionValue { Value = "Divorce" });
        await page.GetByTestId("relationship-dialog-end-date").FillAsync("1995");
        await page.GetByTestId("relationship-dialog-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeHiddenAsync();

        await Assertions.Expect(page.GetByTestId("marriages-list"))
            .ToContainTextAsync("1970 – 1995 (Divorced)");
        await Assertions.Expect(
            page.Locator("[data-testid^='marriage-ongoing-']")).ToHaveCountAsync(0);
        Assert.NotNull(row);
    }

    [Fact]
    public async Task AnEndedMarriageSurvivesAReload()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Susan Hartley");

        await page.Locator("[data-testid^='edit-marriage-']").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await page.GetByTestId("relationship-dialog-end-reason")
            .SelectOptionAsync(new SelectOptionValue { Value = "Annulment" });
        await page.GetByTestId("relationship-dialog-end-date").FillAsync("1971");
        await page.GetByTestId("relationship-dialog-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeHiddenAsync();

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });

        await Assertions.Expect(page.GetByTestId("marriages-list"))
            .ToContainTextAsync("1970 – 1971 (Annulled)");
    }

    // US-026's first criterion, end to end. Raymond died in 2019, so choosing
    // "Widowed" on Susan's marriage offers his death year — and it says whose
    // death it came from, because a date that appeared on its own is a guess the
    // user cannot check.
    [Fact]
    public async Task ChoosingWidowhoodOffersTheEndDateFromTheSpousesDeath()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Susan Hartley");

        await page.Locator("[data-testid^='edit-marriage-']").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await page.GetByTestId("relationship-dialog-end-reason")
            .SelectOptionAsync(new SelectOptionValue { Value = "DeathOfSpouse" });

        var offer = page.GetByTestId("relationship-dialog-use-death-date");
        await Assertions.Expect(offer).ToContainTextAsync("2019");
        await Assertions.Expect(offer).ToContainTextAsync("Raymond Hartley");

        await offer.ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog-end-date"))
            .ToHaveValueAsync("2019");

        await page.GetByTestId("relationship-dialog-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeHiddenAsync();

        await Assertions.Expect(page.GetByTestId("marriages-list"))
            .ToContainTextAsync("1970 – 2019 (Widowed)");
    }

    // ---- Removing one (US-024) ----

    [Fact]
    public async Task RemovingAMarriageKeepsBothPeople()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Susan Hartley");

        await page.Locator("[data-testid^='remove-marriage-']").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("remove-marriage-modal")).ToBeVisibleAsync();
        await page.GetByTestId("confirm-remove-marriage").ClickAsync();

        await Assertions.Expect(page.GetByTestId("marriages-empty")).ToBeVisibleAsync();

        await OpenProfileAsync(page, "Raymond Hartley");
        await Assertions.Expect(page.GetByTestId("profile-name"))
            .ToContainTextAsync("Raymond Hartley");
    }

    // ---- Step relationships (US-038) ----

    [Fact]
    public async Task AProfileListsTheRecordedStepparent()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");

        var list = page.GetByTestId("stepparents-list");
        await Assertions.Expect(list).ToContainTextAsync("Vera Whitfield");
        await Assertions.Expect(list).ToContainTextAsync("married Arthur Whitfield");
    }

    [Fact]
    public async Task TheStepparentSeesTheStepchildOnTheirOwnProfile()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Vera Whitfield");

        await Assertions.Expect(page.GetByTestId("stepchildren-list"))
            .ToContainTextAsync("Daniel Whitfield");
    }

    // US-038's third criterion: a stepparent is not a parent. Vera is Daniel's
    // stepmother and must not appear in either parent section.
    [Fact]
    public async Task AStepparentIsNotListedAmongTheParents()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");

        await Assertions.Expect(page.GetByTestId("parents-list"))
            .Not.ToContainTextAsync("Vera Whitfield");
        await Assertions.Expect(page.GetByTestId("adoptive-parents-empty")).ToBeVisibleAsync();
    }

    // US-038's first criterion, in the browser: the action sits next to a parent's
    // current spouse. Susan and Thomas were adults when Arthur remarried and are
    // deliberately unlabelled in the sample, so Susan's profile is where the offer
    // is waiting to be taken.
    [Fact]
    public async Task AParentsSpouseCanBeLabelledAsAStepparent()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Susan Hartley");

        await Assertions.Expect(page.GetByTestId("stepparents-empty")).ToBeVisibleAsync();
        await page.Locator("[data-testid^='label-stepparent-']").First.ClickAsync();

        await Assertions.Expect(page.GetByTestId("stepparents-list"))
            .ToContainTextAsync("Vera Whitfield");
    }

    [Fact]
    public async Task ALabelledStepparentSurvivesAReload()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Susan Hartley");
        await page.Locator("[data-testid^='label-stepparent-']").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("stepparents-list")).ToBeVisibleAsync();

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });

        await Assertions.Expect(page.GetByTestId("stepparents-list"))
            .ToContainTextAsync("Vera Whitfield");
    }

    // US-038's last criterion, end to end and across the reload that makes it a
    // storage fact rather than a render one: removing the marriage record removes
    // the label from a third person's profile.
    [Fact]
    public async Task RemovingTheMarriageRemovesTheStepparentLabel()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);

        await OpenProfileAsync(page, "Arthur Whitfield");
        await page.Locator("[data-testid^='remove-marriage-']").Last.ClickAsync();
        await Assertions.Expect(page.GetByTestId("remove-marriage-modal")).ToBeVisibleAsync();
        await page.GetByTestId("confirm-remove-marriage").ClickAsync();
        await Assertions.Expect(page.GetByTestId("toast-region"))
            .ToContainTextAsync("stepparent label");

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await OpenProfileAsync(page, "Daniel Whitfield");

        await Assertions.Expect(page.GetByTestId("stepparents-empty")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AStepLabelCanBeRemovedWithoutTouchingTheMarriage()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Daniel Whitfield");

        await page.Locator("[data-testid^='remove-stepparent-']").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("remove-step-modal")).ToBeVisibleAsync();
        await page.GetByTestId("confirm-remove-step").ClickAsync();

        await Assertions.Expect(page.GetByTestId("stepparents-empty")).ToBeVisibleAsync();

        await OpenProfileAsync(page, "Arthur Whitfield");
        await Assertions.Expect(
            page.Locator("[data-testid='marriages-list'] > li")).ToHaveCountAsync(2);
    }

    // ---- Export coverage: the reason the schema version moved ----

    // The gap the plan warned about. A stepparent label is a new persisted shape,
    // so the version, TreeSnapshot and the file format all had to move in this PR
    // — otherwise every import silently discarded the blended families. Asserted
    // through the downloaded file rather than by reading the export code.
    [Fact]
    public async Task AStepparentLabelAddedInTheUiIsInTheExport()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Susan Hartley");
        await page.Locator("[data-testid^='label-stepparent-']").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("stepparents-list")).ToBeVisibleAsync();

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

        Assert.Contains("stepparentLinks", json);
        Assert.Contains("marriageId", json);
        Assert.Contains("\"schemaVersion\": 3", json);
    }

    // A marriage recorded through the UI reaches the file too, dates and all.
    [Fact]
    public async Task AMarriageAddedInTheUiIsInTheExport()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Thomas Whitfield");
        await AddMarriageAsync(page, "Eleanor Hartley", "1975", "York");

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

        Assert.Contains("York", json);
        Assert.Contains("1975", json);
    }

    // ---- The marriage dates across step navigation ----

    // The same drift the adoption date had: the wizard destroys the date controls
    // when it leaves the Details step and rebuilds them on return, so what the
    // field shows and what the save writes can come apart. A value stored without
    // being displayed is the durability failure this project treats as a defect.
    [Fact]
    public async Task TypedMarriageDetailsSurviveSteppingBackAndForward()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await OpenProfileAsync(page, "Thomas Whitfield");

        await page.GetByTestId("add-marriage").ClickAsync();
        await page.GetByTestId("relationship-dialog-search-query").FillAsync("Eleanor");
        await page.GetByTestId("relationship-dialog-search-results")
            .GetByText("Eleanor Hartley").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await page.GetByTestId("relationship-dialog-start-date").FillAsync("1975");
        await page.GetByTestId("relationship-dialog-place").FillAsync("York");

        await page.GetByTestId("relationship-dialog-back").ClickAsync();
        await page.GetByTestId("relationship-dialog-next").ClickAsync();

        await Assertions.Expect(page.GetByTestId("relationship-dialog-start-date"))
            .ToHaveValueAsync("1975");
        await Assertions.Expect(page.GetByTestId("relationship-dialog-place"))
            .ToHaveValueAsync("York");

        await page.GetByTestId("relationship-dialog-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeHiddenAsync();

        await Assertions.Expect(page.GetByTestId("marriages-list")).ToContainTextAsync("1975");
    }
}
