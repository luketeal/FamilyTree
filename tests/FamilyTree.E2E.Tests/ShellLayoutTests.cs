using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// Computed-layout assertions. These cannot be written against bUnit, which
/// renders markup without a CSS engine: every one of these regressions passes
/// a markup-level test while rendering visibly broken in a browser.
/// </summary>
[Collection(nameof(StaticSiteCollection))]
public class ShellLayoutTests(StaticSiteFixture fixture)
{
    private async Task<IPage> OpenAsync(int width = 1440, int height = 900)
    {
        var page = await fixture.Browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        return page;
    }

    // Rail items are NavLink components. CSS isolation does not apply the
    // parent's scope attribute to a child component's root element, so their
    // styles are reached with ::deep — and silently vanish without it, leaving
    // the anchors as display: inline.
    [Fact]
    public async Task RailItems_ReceiveTheirScopedLayoutStyles()
    {
        var page = await OpenAsync();

        var display = await page.GetByTestId("nav-tree")
            .EvaluateAsync<string>("el => getComputedStyle(el).display");
        var direction = await page.GetByTestId("nav-tree")
            .EvaluateAsync<string>("el => getComputedStyle(el).flexDirection");

        Assert.Equal("flex", display);
        Assert.Equal("column", direction);
    }

    [Fact]
    public async Task RailItems_StayWithinTheRailWidth()
    {
        var page = await OpenAsync();

        var overflow = await page.EvaluateAsync<double>(@"() => {
            const rail = document.querySelector('.rail').getBoundingClientRect();
            return [...document.querySelectorAll('.rail__item')]
                .reduce((worst, el) => Math.max(worst, el.getBoundingClientRect().right - rail.right), 0);
        }");

        Assert.True(overflow <= 0, $"Rail items overflow the rail by {overflow}px");
    }

    // FocusOnNavigate makes the heading focusable so screen readers announce
    // the page; the browser would otherwise draw a focus ring around it.
    [Fact]
    public async Task ProgrammaticFocusTarget_DoesNotRenderAFocusRing()
    {
        var page = await OpenAsync();

        var outline = await page.Locator("h1")
            .EvaluateAsync<string>("el => getComputedStyle(el).outlineStyle");

        Assert.Equal("none", outline);
    }

    // The pre-boot loading indicator is the first thing every visitor sees, for
    // the whole multi-megabyte download. Its rules live in the global stylesheet
    // rather than a component, so nothing else covers them: unstyled, the SVG
    // falls back to a 300x150 box with fill: black.
    [Fact]
    public async Task PreBootLoadingIndicator_IsStyled()
    {
        var page = await fixture.Browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        });

        // Never let the runtime load, so the pre-boot markup stays on screen.
        await page.RouteAsync("**/_framework/dotnet*.js", route => route.AbortAsync());
        await page.GotoAsync(fixture.BaseUrl);
        await page.Locator(".loading-progress").WaitForAsync();

        var svgWidth = await page.Locator(".loading-progress")
            .EvaluateAsync<string>("el => getComputedStyle(el).width");
        var circleFill = await page.Locator(".loading-progress circle").First
            .EvaluateAsync<string>("el => getComputedStyle(el).fill");

        Assert.NotEqual("300px", svgWidth);
        Assert.Equal("none", circleFill);
    }

    // Blazor reveals this banner by setting display: block when an unhandled
    // exception reaches the renderer, so it must be hidden by default. It is
    // not part of any component, so no component test covers it.
    [Fact]
    public async Task BlazorErrorBanner_IsHiddenOnAHealthyLoad()
    {
        var page = await OpenAsync();

        await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    // Both axes: an earlier horizontal-only version passed while the content
    // area overflowed vertically by 37px on a phone, because the home page
    // recomputed its own height from desktop padding and ignored the bottom rail.
    [Theory]
    [InlineData(390)]
    [InlineData(768)]
    [InlineData(1440)]
    public async Task Shell_DoesNotScroll(int width)
    {
        var page = await OpenAsync(width, 844);

        // Find scroll containers by computed overflow rather than by a hardcoded
        // selector list, so a container added later cannot escape the check.
        //
        // Containers marked data-scrolls are exempt on the vertical axis only:
        // .shell__content is designed to scroll once a page is longer than the
        // viewport, so asserting it never does would fail on the first long
        // list. Horizontal overflow is always a bug in this layout, so it is
        // checked everywhere with no exemption.
        var overflowBy = await page.EvaluateAsync<double>(@"() => {
            const de = document.documentElement;
            const scrollable = [de, ...document.querySelectorAll('*')].filter(el => {
                if (el === de) return true;
                const o = getComputedStyle(el);
                return ['auto', 'scroll'].includes(o.overflowX) || ['auto', 'scroll'].includes(o.overflowY);
            });
            return Math.max(0, ...scrollable.flatMap(el => [
                el.scrollWidth - el.clientWidth,
                el.hasAttribute('data-scrolls') ? 0 : el.scrollHeight - el.clientHeight,
            ]));
        }");

        // Naming the axis and the offender turns a failure that needs a
        // debugging session into one that points straight at the fix.
        var offender = await page.EvaluateAsync<string>(@"() => {
            const describe = el => el.tagName.toLowerCase() +
                (el.className && typeof el.className === 'string'
                    ? '.' + el.className.trim().split(/\s+/).join('.')
                    : '');

            const de = document.documentElement;
            let worst = null, worstBy = 0, axis = '';
            for (const el of [de, ...document.querySelectorAll('*')]) {
                const h = el.scrollWidth - el.clientWidth;
                const v = el.hasAttribute('data-scrolls') ? 0 : el.scrollHeight - el.clientHeight;
                if (h > worstBy) { worstBy = h; worst = el; axis = 'horizontally'; }
                if (v > worstBy) { worstBy = v; worst = el; axis = 'vertically'; }
            }
            return worst ? `${describe(worst)} ${axis} by ${Math.round(worstBy)}px` : 'none';
        }");

        Assert.True(overflowBy <= 0,
            $"Page scrolls by {overflowBy}px at {width}x844. Worst offender: {offender}");
    }

    [Fact]
    public async Task Rail_BecomesABottomBarOnSmallScreens()
    {
        var page = await OpenAsync(390, 844);

        var direction = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.rail')).flexDirection");

        Assert.Equal("row", direction);
    }
}
