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

    // The sample family is chosen to exercise every relationship type and the
    // awkward cases, so a full reload that reproduces its exact shape proves
    // each record type survives the flat-record mapping — not just people.
    [Fact]
    public async Task EveryRelationshipTypeSurvivesAReload()
    {
        var page = await OpenSettingsAsync();
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats")).ToHaveTextAsync("10 people · 14 relationships");

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var counts = await page.EvaluateAsync<string>(@"async () => {
            const db = await new Promise((res, rej) => {
                const r = indexedDB.open('familytree');
                r.onsuccess = () => res(r.result);
                r.onerror = () => rej(r.error);
            });
            const count = store => new Promise(res => {
                const req = db.transaction([store], 'readonly').objectStore(store).getAll();
                req.onsuccess = () => res(req.result.length);
            });
            return JSON.stringify({
                people: await count('people'),
                bio: await count('biologicalLinks'),
                adoptive: await count('adoptiveLinks'),
                marriages: await count('marriages'),
            });
        }");

        // 11 stored people: 10 real plus the unidentified ancestor.
        Assert.Equal(
            """{"people":11,"bio":9,"adoptive":2,"marriages":3}""",
            counts);
    }

    // PartialDate carries precision and a circa flag, and a year-only date must
    // not come back claiming a month it never had.
    [Fact]
    public async Task PartialDatesKeepTheirPrecisionThroughStorage()
    {
        var page = await OpenSettingsAsync();
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats")).ToHaveTextAsync("10 people · 14 relationships");

        var stored = await page.EvaluateAsync<string>(@"async () => {
            const db = await new Promise(res => {
                const r = indexedDB.open('familytree');
                r.onsuccess = () => res(r.result);
            });
            const people = await new Promise(res => {
                const req = db.transaction(['people'], 'readonly').objectStore('people').getAll();
                req.onsuccess = () => res(req.result);
            });
            const arthur = people.find(p => p.firstName === 'Arthur');
            return JSON.stringify(arthur.birthDate);
        }");

        Assert.Equal("""{"year":1918,"month":null,"day":null,"isApproximate":false}""", stored);
    }
}
