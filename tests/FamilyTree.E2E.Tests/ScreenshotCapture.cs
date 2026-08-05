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

    // The confirmation only exists in response to a click, so the shell capture
    // above never shows it. It is the last thing a user reads before an
    // irreversible wipe, which makes "is it actually legible" a real question
    // and not one any assertion in this suite answers.
    [Theory]
    [InlineData("desktop", 1440, 900)]
    [InlineData("mobile", 390, 844)]
    public async Task CaptureDestructiveConfirmation(string name, int width, int height)
    {
        Directory.CreateDirectory(OutputDirectory);

        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl + "settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");

        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-confirm")).ToBeVisibleAsync();

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"confirm-{name}.png"),
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
