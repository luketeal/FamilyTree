using Microsoft.Playwright;

namespace FamilyTree.Web.E2E.Tests;

[Collection(nameof(WebHostCollection))]
public class BootSmokeTests
{
    private readonly WebHostFixture _host;

    public BootSmokeTests(WebHostFixture host) => _host = host;

    [Fact]
    public async Task HomePage_RendersShellWithLeftRailAndTopBar()
    {
        await using var context = await _host.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(_host.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });
        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);

        var requiredSelectors = new[]
        {
            "[data-testid='nav-tree']",
            "[data-testid='nav-people']",
            "[data-testid='nav-relate']",
            "[data-testid='nav-import']",
            "[data-testid='nav-settings']",
            "[data-testid='app-logo']",
            "[data-testid='add-person-button']",
            "[data-testid='home-empty-state']"
        };

        foreach (var selector in requiredSelectors)
        {
            var locator = page.Locator(selector);
            await locator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10_000
            });
            Assert.True(await locator.IsVisibleAsync(), $"expected '{selector}' to be visible");
        }
    }
}
