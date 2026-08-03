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
