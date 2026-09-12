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
        // The tree is no longer empty, so the second load has to be confirmed.
        await page.GetByTestId("load-sample").ClickAsync();
        await page.GetByTestId("confirm-destructive").ClickAsync();

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
        await page.GetByTestId("confirm-destructive").ClickAsync();

        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("0 people · 0 relationships");
    }

    // The case the original tests all missed: every one of them started from an
    // empty tree, so nothing exercised the path where there was something to
    // lose. Loading the sample replaces the whole dataset in one transaction, so
    // without a confirmation a misclick is an unrecoverable wipe — there is no
    // export and no undo yet.
    [Fact]
    public async Task LoadingTheSampleOverAnExistingTreeAsksFirst()
    {
        var page = await OpenSettingsAsync();
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats")).ToHaveTextAsync("10 people · 14 relationships");

        await page.GetByTestId("load-sample").ClickAsync();

        await Assertions.Expect(page.GetByTestId("settings-confirm")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("settings-confirm-text"))
            .ToContainTextAsync("10 people and 14 relationships");
    }

    [Fact]
    public async Task CancellingTheConfirmationLeavesTheTreeIntact()
    {
        var page = await OpenSettingsAsync();
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats")).ToHaveTextAsync("10 people · 14 relationships");

        await page.GetByTestId("clear-data").ClickAsync();
        await page.GetByTestId("cancel-destructive").ClickAsync();

        await Assertions.Expect(page.GetByTestId("settings-confirm")).Not.ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");

        // Not merely still on screen — still in the database.
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await Assertions.Expect(page.GetByTestId("tree-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");
    }

    // role="alertdialog" claims the panel behaves like a dialog. bUnit cannot
    // check either half of that: it has no focus model, and FocusAsync is stubbed
    // JS interop there. Both need a real browser.
    [Fact]
    public async Task TheConfirmationTakesFocusWhenItAppears()
    {
        var page = await OpenSettingsAsync();
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats")).ToHaveTextAsync("10 people · 14 relationships");

        await page.GetByTestId("clear-data").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-confirm")).ToBeVisibleAsync();

        var focused = await page.EvaluateAsync<string?>(
            "() => document.activeElement?.getAttribute('data-testid')");

        Assert.Equal("settings-confirm", focused);
    }

    [Fact]
    public async Task EscapeCancelsTheConfirmationAndKeepsTheData()
    {
        var page = await OpenSettingsAsync();
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats")).ToHaveTextAsync("10 people · 14 relationships");

        await page.GetByTestId("clear-data").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-confirm")).ToBeVisibleAsync();
        // Same race as the quick-add Escape tests: the key is handled on the
        // panel, which is not focused until OnAfterRenderAsync has finished its
        // interop round trips.
        await Assertions.Expect(page.GetByTestId("settings-confirm")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(page.GetByTestId("settings-confirm")).Not.ToBeVisibleAsync();

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await Assertions.Expect(page.GetByTestId("tree-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");
    }

    // An empty tree has nothing to lose, so the confirmation would be pure
    // friction. This pins that distinction so it does not drift.
    [Fact]
    public async Task LoadingTheSampleIntoAnEmptyTreeDoesNotAsk()
    {
        var page = await OpenSettingsAsync();

        await page.GetByTestId("load-sample").ClickAsync();

        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");
        await Assertions.Expect(page.GetByTestId("settings-confirm")).Not.ToBeVisibleAsync();
    }

    // A clear wipes every store, and the schema stamp lives in one of them.
    // Losing it would leave later writes going into an unlabelled database.
    [Fact]
    public async Task ClearingKeepsTheSchemaStamp()
    {
        var page = await OpenSettingsAsync();
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats")).ToHaveTextAsync("10 people · 14 relationships");

        await page.GetByTestId("clear-data").ClickAsync();
        await page.GetByTestId("confirm-destructive").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats")).ToHaveTextAsync("0 people · 0 relationships");

        var meta = await page.EvaluateAsync<string>(@"async () => {
            const db = await new Promise(res => {
                const r = indexedDB.open('familytree');
                r.onsuccess = () => res(r.result);
            });
            const rows = await new Promise(res => {
                const req = db.transaction(['meta'], 'readonly').objectStore('meta').getAll();
                req.onsuccess = () => res(req.result);
            });
            return JSON.stringify(rows.map(r => r.schemaVersion));
        }");

        // Written out rather than read from TreeSchema.Version: this project
        // deliberately has no reference to the app, because it tests the
        // published site rather than the code that produced it. The cost is
        // that a schema bump has to be made here too — PR 7 raised this to 2
        // when phantoms entered the export format (ADR-007).
        Assert.Equal("[2]", meta);
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
