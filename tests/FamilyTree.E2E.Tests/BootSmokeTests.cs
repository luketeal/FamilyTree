using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

[Collection(nameof(StaticSiteCollection))]
public class BootSmokeTests(StaticSiteFixture fixture)
{
    private async Task<IPage> OpenAsync(string path = "")
    {
        var page = await fixture.Browser.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + path, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        return page;
    }

    // ADR-004 claims the app works offline and that nothing leaves the browser.
    // A single third-party asset — a webfont, an analytics script, a CDN
    // library — breaks both claims silently, so assert on it rather than
    // relying on review to notice one being added.
    [Fact]
    public async Task Application_MakesNoThirdPartyRequests()
    {
        var page = await fixture.Browser.NewPageAsync();
        var external = new List<string>();

        page.Request += (_, request) =>
        {
            if (!request.Url.StartsWith(fixture.BaseUrl.Split("/FamilyTree/")[0], StringComparison.Ordinal))
            {
                external.Add(request.Url);
            }
        };

        await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        Assert.True(
            external.Count == 0,
            $"App requested {external.Count} third-party asset(s): {string.Join(", ", external)}");
    }

    [Fact]
    public async Task SelfHostedFonts_AreActuallyApplied()
    {
        var page = await fixture.Browser.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        // document.fonts only reports faces the browser successfully loaded, so
        // this fails if a woff2 is missing or the @font-face path is wrong —
        // which would otherwise degrade silently to a system font. Family names
        // come back as authored, quotes included, so normalise before comparing.
        var loaded = await page.EvaluateAsync<string[]>(@"async () => {
            await document.fonts.ready;
            return [...document.fonts]
                .filter(f => f.status === 'loaded')
                .map(f => f.family.replace(/^[""']|[""']$/g, ''));
        }");

        Assert.True(loaded.Contains("Inter"), $"Inter not loaded. Loaded faces: [{string.Join(", ", loaded)}]");
        Assert.True(loaded.Contains("Source Serif 4"), $"Source Serif 4 not loaded. Loaded faces: [{string.Join(", ", loaded)}]");
    }

    [Fact]
    public async Task Application_BootsAndRendersTheShell()
    {
        var page = await OpenAsync();

        await Assertions.Expect(page.GetByTestId("app-logo")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("add-person-button")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("nav-tree")]
    [InlineData("nav-people")]
    [InlineData("nav-relate")]
    [InlineData("nav-import")]
    [InlineData("nav-settings")]
    public async Task IconRail_RendersEveryPrimaryDestination(string testId)
    {
        var page = await OpenAsync();

        await Assertions.Expect(page.GetByTestId(testId)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task HomePage_ShowsTheEmptyState()
    {
        var page = await OpenAsync();

        await Assertions.Expect(page.GetByTestId("home-empty-state")).ToBeVisibleAsync();
    }

    // Guards the GitHub Pages project-subpath deployment: links must resolve
    // against <base href="/FamilyTree/"> rather than the server root.
    [Fact]
    public async Task NavigationLinks_ResolveAgainstTheRepositorySubpath()
    {
        var page = await OpenAsync();

        // The resolved href property, not the literal attribute: that is what
        // proves the relative attribute resolved against the base href.
        var resolved = await page.GetByTestId("nav-people").EvaluateAsync<string>("el => el.href");

        Assert.EndsWith("/FamilyTree/people", resolved);
    }

    // Deep links are served 404.html by GitHub Pages; the app must still boot
    // and route client-side rather than showing the host's error page.
    [Fact]
    public async Task DeepLink_FallsBackTo404PageAndStillBootsTheApp()
    {
        var page = await OpenAsync("settings");

        await Assertions.Expect(page.GetByTestId("app-logo")).ToBeVisibleAsync();
    }
}
