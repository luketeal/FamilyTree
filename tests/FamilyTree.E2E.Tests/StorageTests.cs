using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// IndexedDB round-trips. These cannot be unit tests — the store is a browser
/// API, and the whole point is that data written by one page load is still
/// there on the next.
/// </summary>
[Collection(nameof(StaticSiteCollection))]
public class StorageTests(StaticSiteFixture fixture)
{
    private async Task<IPage> OpenSettingsAsync()
    {
        // A fresh context per test, so one test's data cannot leak into another.
        var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + "settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByTestId("load-sample").WaitForAsync();
        return page;
    }

    [Fact]
    public async Task StartsEmpty()
    {
        var page = await OpenSettingsAsync();

        await Assertions.Expect(page.GetByTestId("tree-stats"))
            .ToHaveTextAsync("0 people · 0 relationships");
    }

    [Fact]
    public async Task LoadingTheSampleFamilyPopulatesTheTree()
    {
        var page = await OpenSettingsAsync();

        await page.GetByTestId("load-sample").ClickAsync();

        // 10 real people plus one phantom, which is deliberately not counted.
        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");
    }

    // The top bar is a sibling component with no knowledge of Settings; this
    // passes only if the change notification actually reaches it.
    [Fact]
    public async Task LoadingTheSampleFamilyUpdatesTheTopBar()
    {
        var page = await OpenSettingsAsync();

        await page.GetByTestId("load-sample").ClickAsync();

        await Assertions.Expect(page.GetByTestId("tree-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");
    }

    // The reason this app can exist without a backend at all.
    [Fact]
    public async Task DataSurvivesAFullReload()
    {
        var page = await OpenSettingsAsync();
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats")).ToHaveTextAsync("10 people · 14 relationships");

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });

        await Assertions.Expect(page.GetByTestId("tree-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");
    }

    [Fact]
    public async Task LoadingTheSampleTwiceDoesNotDuplicateIt()
    {
        var page = await OpenSettingsAsync();

        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats")).ToHaveTextAsync("10 people · 14 relationships");
        await page.GetByTestId("load-sample").ClickAsync();

        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");
    }

    [Fact]
    public async Task ClearingRemovesEverything()
    {
        var page = await OpenSettingsAsync();
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats")).ToHaveTextAsync("10 people · 14 relationships");

        await page.GetByTestId("clear-data").ClickAsync();

        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("0 people · 0 relationships");
    }

    // Asserts that the request happens and a real answer comes back, not that
    // the grant is given: browsers decide that on engagement heuristics — repeat
    // visits, installation, notification permission — which a headless run
    // cannot satisfy, so it reports best-effort here and would report otherwise
    // for a real user. What must not happen is the status never resolving,
    // which is what a broken or unreachable storage API looks like.
    [Fact]
    public async Task ReportsAResolvedStorageDurabilityStatus()
    {
        var page = await OpenSettingsAsync();

        var status = page.GetByTestId("settings-persistence");
        await Assertions.Expect(status).Not.ToHaveTextAsync("Checking…");
        await Assertions.Expect(status).ToContainTextAsync(
            new System.Text.RegularExpressions.Regex("Persistent|Best effort|cannot guarantee"));
    }
}
