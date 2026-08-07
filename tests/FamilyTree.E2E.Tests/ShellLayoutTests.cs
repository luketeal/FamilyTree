using System.Text.Json;
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
    private async Task<IPage> OpenAsync(int width = 1440, int height = 900, string path = "")
    {
        var page = await fixture.Browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        await page.GotoAsync(fixture.BaseUrl + path, new PageGotoOptions
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
    //
    // Checked on every route rather than the home page alone: the person form is
    // a two-column grid, which is exactly the shape that overflows once the
    // viewport is narrower than its columns.
    [Theory]
    [InlineData(390, "")]
    [InlineData(768, "")]
    [InlineData(1440, "")]
    [InlineData(390, "people")]
    [InlineData(1440, "people")]
    [InlineData(390, "people/add")]
    [InlineData(768, "people/add")]
    [InlineData(1440, "people/add")]
    [InlineData(390, "settings")]
    public async Task Shell_DoesNotScroll(int width, string path)
    {
        var page = await OpenAsync(width, 844, path);

        // Find scroll containers by computed overflow rather than a hardcoded
        // selector list, so a container added later cannot escape the check.
        //
        // Horizontal overflow is always a bug in this layout, so it is checked
        // everywhere with no exemption. Vertical overflow is only a bug when the
        // content was meant to fit: a page of 200 people is legitimately taller
        // than the viewport, while the home page's centred card is not.
        //
        // So the exemption is keyed on the *page* declaring itself scrollable,
        // not on the container. Marking .shell__content itself would exempt it
        // permanently and lose the regression this test exists for — the 37px
        // bug was vertical overflow on .shell__content, caused by a page that
        // should have fitted.
        var measured = await page.EvaluateAsync<string>(@"() => {
            const de = document.documentElement;
            const scrollable = [de, ...document.querySelectorAll('*')].filter(el => {
                if (el === de) return true;
                const o = getComputedStyle(el);
                return ['auto', 'scroll'].includes(o.overflowX) || ['auto', 'scroll'].includes(o.overflowY);
            });

            const verticalAllowed = el => el.querySelector('[data-scrolls]') !== null;
            const describe = el => el.tagName.toLowerCase() +
                (el.className && typeof el.className === 'string'
                    ? '.' + el.className.trim().split(/\s+/).join('.')
                    : '');

            // One pass over one set, so the number and the element that explains
            // it can never disagree about which elements were considered.
            let worst = null, worstBy = 0, axis = '';
            for (const el of scrollable) {
                const h = el.scrollWidth - el.clientWidth;
                const v = verticalAllowed(el) ? 0 : el.scrollHeight - el.clientHeight;
                if (h > worstBy) { worstBy = h; worst = el; axis = 'horizontally'; }
                if (v > worstBy) { worstBy = v; worst = el; axis = 'vertically'; }
            }

            return JSON.stringify({
              overflowBy: worstBy,
              offender: worst ? `${describe(worst)} ${axis} by ${Math.round(worstBy)}px` : 'none',
            });
        }");

        using var result = JsonDocument.Parse(measured);
        var overflowBy = result.RootElement.GetProperty("overflowBy").GetDouble();
        var offender = result.RootElement.GetProperty("offender").GetString();

        Assert.True(overflowBy <= 0,
            $"/{path} scrolls by {overflowBy}px at {width}x844. Worst offender: {offender}");
    }

    // iOS Safari zooms the visual viewport when a focused control renders below
    // 16px, and the zoomed page then scrolls sideways — a form that should only
    // move vertically ends up needing horizontal panning between fields.
    //
    // Headless Chromium has no focus-zoom behaviour, so neither a screenshot nor
    // the overflow test above can reproduce the symptom. The computed font size
    // is the only part that can be pinned here, which is exactly why it is
    // pinned: the bug reached a real phone through a fully green suite.
    [Theory]
    [InlineData("people/add")]
    [InlineData("settings")]
    public async Task FormControls_AreLargeEnoughToNotTriggerIosZoom(string path)
    {
        var page = await OpenAsync(390, 844, path);

        var smallest = await page.EvaluateAsync<string>(@"() => {
            const controls = [...document.querySelectorAll('input, select, textarea')]
                .filter(el => el.type !== 'checkbox' && el.type !== 'radio' && !el.disabled);

            let worst = null, worstSize = Infinity;
            for (const el of controls) {
                const size = parseFloat(getComputedStyle(el).fontSize);
                if (size < worstSize) { worstSize = size; worst = el; }
            }

            return JSON.stringify({
                size: worst ? worstSize : 16,
                offender: worst
                    ? `${worst.tagName.toLowerCase()}[data-testid=${worst.dataset.testid ?? worst.id ?? '?'}]`
                    : 'none',
            });
        }");

        using var result = JsonDocument.Parse(smallest);
        var size = result.RootElement.GetProperty("size").GetDouble();
        var offender = result.RootElement.GetProperty("offender").GetString();

        Assert.True(size >= 16,
            $"/{path} has a form control at {size}px, which makes iOS Safari zoom and scroll "
            + $"the page sideways on focus. Smallest: {offender}");
    }

    // Scrolling the app behind a dialog to reach the dialog is disorienting, and
    // on iOS the document becomes scrollable the moment the keyboard shrinks the
    // visual viewport. 380px of height stands in for that: it is shorter than
    // the popover, which is precisely when the gesture used to chain through.
    [Fact]
    public async Task AnOpenOverlayLocksTheScrollBehindIt()
    {
        var page = await OpenAsync(900, 380);
        await page.GetByTestId("add-person-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeVisibleAsync();

        var locked = await page.EvaluateAsync<string>(@"() => JSON.stringify({
            html: getComputedStyle(document.documentElement).overflowY,
            body: getComputedStyle(document.body).overflowY,
            content: getComputedStyle(document.querySelector('.shell__content')).overflowY,
        })");

        using var result = JsonDocument.Parse(locked);
        Assert.Equal("hidden", result.RootElement.GetProperty("html").GetString());
        Assert.Equal("hidden", result.RootElement.GetProperty("body").GetString());
        Assert.Equal("hidden", result.RootElement.GetProperty("content").GetString());
    }

    // The lock has to lift, or closing the dialog leaves the app unscrollable.
    [Fact]
    public async Task ClosingTheOverlayRestoresScrolling()
    {
        var page = await OpenAsync(900, 380);
        await page.GetByTestId("add-person-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("quick-first-name")).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeHiddenAsync();

        var content = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.shell__content')).overflowY");

        Assert.Equal("auto", content);
    }

    // The overlay must be able to scroll itself, or a panel taller than the
    // screen simply cannot be reached once the background is locked.
    [Fact]
    public async Task AnOverlayTallerThanTheScreenScrollsItself()
    {
        var page = await OpenAsync(900, 380);
        await page.GetByTestId("add-person-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeVisibleAsync();

        var overlay = await page.GetByTestId("quick-add").EvaluateAsync<string>(
            @"el => JSON.stringify({
                overflow: getComputedStyle(el).overflowY,
                chaining: getComputedStyle(el).overscrollBehaviorY,
                scrollable: el.scrollHeight > el.clientHeight,
            })");

        using var result = JsonDocument.Parse(overlay);
        Assert.Equal("auto", result.RootElement.GetProperty("overflow").GetString());
        Assert.Equal("contain", result.RootElement.GetProperty("chaining").GetString());
        Assert.True(result.RootElement.GetProperty("scrollable").GetBoolean(),
            "The overlay is not taller than this viewport, so it does not exercise the case.");
    }

    // 900px wide, not 390: below the 768px breakpoint the top-bar action goes to
    // the full form instead of the popover, so a narrow viewport would not open
    // an overlay at all. The short height is what makes the overlay scroll.
    //
    // The CSS-only lock was not enough on a real phone: with the keyboard up,
    // iOS shrinks the visual viewport while position:fixed keeps measuring the
    // layout viewport, so the overlay extended off screen and reaching it meant
    // panning the whole app. The JS pins the body and sizes the overlay to the
    // visual viewport instead.
    //
    // Headless Chromium has no soft keyboard, so the shrink itself cannot be
    // reproduced here. What is assertable is that the interop ran at all — the
    // body is pinned and the overlay carries an explicit pixel height rather
    // than the layout-viewport fallback. A silently broken module would leave
    // both untouched, which is the regression worth catching.
    [Fact]
    public async Task OpeningAnOverlayPinsTheBodyAndSizesToTheVisualViewport()
    {
        var page = await OpenAsync(900, 620);
        await page.GetByTestId("add-person-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeVisibleAsync();

        var state = await page.EvaluateAsync<string>(@"() => {
            const overlay = document.querySelector('[data-overlay]');
            return JSON.stringify({
                bodyPosition: getComputedStyle(document.body).position,
                inlineHeight: overlay.style.height,
                matchesViewport:
                    Math.abs(parseFloat(overlay.style.height) - window.visualViewport.height) < 1,
            });
        }");

        using var result = JsonDocument.Parse(state);
        Assert.Equal("fixed", result.RootElement.GetProperty("bodyPosition").GetString());
        Assert.NotEqual(string.Empty, result.RootElement.GetProperty("inlineHeight").GetString());
        Assert.True(result.RootElement.GetProperty("matchesViewport").GetBoolean(),
            "The overlay is not sized to the visual viewport, so the tracker did not run.");
    }

    // If the pin outlived the dialog the app would be frozen, which is a worse
    // bug than the one being fixed.
    [Fact]
    public async Task ClosingAnOverlayUnpinsTheBody()
    {
        var page = await OpenAsync(900, 620);
        await page.GetByTestId("add-person-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("quick-first-name")).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeHiddenAsync();

        var position = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.body).position");

        Assert.Equal("static", position);
    }

    // Saving navigates, which disposes the popover without another render. The
    // release therefore has to happen on disposal too, or the app is left pinned
    // on the page the user just landed on.
    [Fact]
    public async Task AnOverlayThatClosesByNavigatingStillUnpinsTheBody()
    {
        var page = await OpenAsync(900, 620);
        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Grace");
        await page.GetByTestId("quick-last-name").FillAsync("Hopper");
        await page.GetByTestId("quick-save").ClickAsync();

        await Assertions.Expect(page.GetByTestId("profile-name")).ToHaveTextAsync("Grace Hopper");

        var position = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.body).position");

        Assert.Equal("static", position);
    }

    // The tint has to cover exactly what the user can see. A separate
    // position:fixed backdrop is pinned to the layout viewport while the overlay
    // tracks the visual viewport, and the keyboard pulls those apart — leaving a
    // strip of undimmed page. Painting it on the overlay makes them the same box
    // by construction. Scrolling first, because unscrolled a broken tint looks
    // correct.
    [Fact]
    public async Task TheTintCoversTheOverlayEvenWhenItScrolls()
    {
        var page = await OpenAsync(900, 380);
        await page.GetByTestId("add-person-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeVisibleAsync();

        var measured = await page.EvaluateAsync<string>(@"async () => {
            const overlay = document.querySelector('[data-overlay]');
            overlay.scrollTop = overlay.scrollHeight;
            await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));

            const rect = overlay.getBoundingClientRect();
            return JSON.stringify({
                scrolled: overlay.scrollTop > 0,
                tinted: getComputedStyle(overlay).backgroundColor,
                top: Math.round(rect.top),
                coversToBottom: Math.round(rect.bottom) >= window.innerHeight,
            });
        }");

        using var result = JsonDocument.Parse(measured);
        Assert.True(result.RootElement.GetProperty("scrolled").GetBoolean(),
            "The overlay did not scroll, so this does not exercise the case.");
        Assert.NotEqual("rgba(0, 0, 0, 0)", result.RootElement.GetProperty("tinted").GetString());
        Assert.Equal(0, result.RootElement.GetProperty("top").GetInt32());
        Assert.True(result.RootElement.GetProperty("coversToBottom").GetBoolean(),
            "The tinted box does not reach the bottom of the viewport, so page shows through beneath it.");
    }

    // Clicking the tint still dismisses, now that it is the overlay itself
    // rather than a dedicated backdrop element.
    [Fact]
    public async Task ClickingOutsideThePanelClosesTheOverlay()
    {
        var page = await OpenAsync(1440, 900);
        await page.GetByTestId("add-person-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeVisibleAsync();

        // Bottom-left of the overlay: tinted area, well clear of the panel.
        await page.Mouse.ClickAsync(20, 860);

        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task ClickingInsideThePanelDoesNotCloseTheOverlay()
    {
        var page = await OpenAsync(1440, 900);
        await page.GetByTestId("add-person-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeVisibleAsync();

        await page.GetByTestId("quick-add").Locator(".quickadd__title").ClickAsync();

        await Assertions.Expect(page.GetByTestId("quick-add")).ToBeVisibleAsync();
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
