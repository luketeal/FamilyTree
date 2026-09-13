using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// Base for every test that opens a browser page, closing what it opened when
/// the test finishes.
/// </summary>
/// <remarks>
/// The browser is shared for the whole run by <see cref="StaticSiteFixture"/>,
/// which is right — launching Chromium per test would dominate the run time.
/// Pages and contexts are not: each one is a live renderer process holding the
/// DOM, the WebAssembly runtime and an IndexedDB connection, and nothing closed
/// them. They accumulated until the fixture tore the browser down at the end of
/// the run, which on a 16 GB box measured 147 renderers and roughly 13 GB of
/// resident memory — at which point tests stop failing for their own reasons and
/// start failing because the machine has nothing left.
/// <para>
/// That failure is worth describing, because it does not look like a leak. The
/// suite goes slower and slower and then tests that have nothing to do with the
/// change under test — booting the shell, rendering the icon rail — begin timing
/// out, which reads as a broken environment rather than as the suite eating it.
/// It was diagnosed that way here before it was measured.
/// </para>
/// <para>
/// Tests go through <see cref="NewPageAsync"/> and <see cref="NewContextAsync"/>
/// rather than reaching for <c>fixture.Browser</c>, so cleanup is inherited
/// rather than remembered. xUnit builds a fresh instance of the test class per
/// test and awaits <see cref="DisposeAsync"/> afterwards, so the scope is one
/// test — nothing survives into the next.
/// </para>
/// </remarks>
public abstract class BrowserTest(StaticSiteFixture fixture) : IAsyncLifetime
{
    private readonly List<IBrowserContext> _contexts = [];
    private readonly List<IPage> _pages = [];

    /// <summary>
    /// The published site and the browser behind it.
    /// </summary>
    /// <remarks>
    /// Read through the base rather than through each subclass's own primary
    /// constructor parameter. Capturing it in both places is what CS9107 warns
    /// about, and this project builds warning-clean.
    /// </remarks>
    protected StaticSiteFixture Fixture { get; } = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>A page on its own implicit context, closed when the test ends.</summary>
    protected async Task<IPage> NewPageAsync(BrowserNewPageOptions? options = null)
    {
        var page = await Fixture.Browser.NewPageAsync(options);
        _pages.Add(page);
        return page;
    }

    /// <summary>
    /// A context, closed when the test ends — and with it every page opened from
    /// it, so those need no tracking of their own.
    /// </summary>
    protected async Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions? options = null)
    {
        var context = await Fixture.Browser.NewContextAsync(options);
        _contexts.Add(context);
        return context;
    }

    /// <summary>
    /// Closes everything this test opened.
    /// </summary>
    /// <remarks>
    /// Failures here are swallowed deliberately. Teardown runs after the
    /// assertions, so throwing would replace a real failure — or a pass — with a
    /// cleanup error, and the only thing at stake is memory the process is about
    /// to reclaim anyway. A page the test closed itself, or one whose browser has
    /// already gone, is the ordinary case rather than a problem.
    /// </remarks>
    public async Task DisposeAsync()
    {
        foreach (var context in _contexts)
        {
            try
            {
                await context.CloseAsync();
            }
            catch (PlaywrightException)
            {
                // Already closed, or the browser went with it.
            }
        }

        foreach (var page in _pages)
        {
            try
            {
                await page.CloseAsync();
            }
            catch (PlaywrightException)
            {
                // As above.
            }
        }

        _contexts.Clear();
        _pages.Clear();
    }
}
