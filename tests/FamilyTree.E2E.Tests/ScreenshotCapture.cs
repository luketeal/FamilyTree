using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// Captures screenshots of the published site as a build artifact. These are
/// deliberately not assertions: they exist because locator assertions verify
/// that elements are present and wired up, not that the page looks right. A
/// broken stylesheet or a collapsed layout passes every assertion in this
/// suite while rendering unusably.
/// </summary>
[Collection(nameof(StaticSiteCollection))]
public class ScreenshotCapture(StaticSiteFixture fixture)
{
    private static string OutputDirectory =>
        Environment.GetEnvironmentVariable("FAMILYTREE_SCREENSHOT_DIR")
        ?? Path.Combine(Path.GetTempPath(), "familytree-screenshots");

    [Theory]
    [InlineData("desktop", 1440, 900)]
    [InlineData("mobile", 390, 844)]
    public async Task CaptureShell(string name, int width, int height)
    {
        Directory.CreateDirectory(OutputDirectory);

        var page = await fixture.Browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });

        await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"shell-{name}.png"),
            FullPage = true,
        });
    }

    // The one appearance check worth asserting: if the design tokens fail to
    // resolve, every component silently falls back to browser defaults.
    [Fact]
    public async Task DesignTokens_ResolveOnTheDeployedStylesheet()
    {
        var page = await fixture.Browser.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        var paper = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--paper').trim()");

        Assert.Equal("#fbfaf7", paper, ignoreCase: true);
    }
}
