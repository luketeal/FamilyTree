using Bunit;
using FamilyTree.UI.Layout;

namespace FamilyTree.UI.Tests.Layout;

public class ShellLayoutTests : ShellTestContext
{
    // Export joins the five destinations in the mid-fidelity mockups, which were
    // drawn before export existed. It is the backup mechanism for an app whose
    // data lives in exactly one browser, and reaching it only through Settings
    // made it something a user had to already know about.
    [Fact]
    public void IconRail_RendersEveryPrimaryDestination()
    {
        var cut = Render<IconRail>();

        var testIds = cut.FindAll("[data-testid]")
            .Select(e => e.GetAttribute("data-testid")!)
            .ToArray();

        Assert.Equal(
            ["nav-tree", "nav-people", "nav-relate", "nav-export", "nav-import", "nav-settings"],
            testIds);
    }

    // GitHub Pages serves this app from a repository subpath, so every internal
    // link must resolve against <base href="/FamilyTree/">. A leading slash
    // bypasses the base href and breaks navigation on the deployed site while
    // working perfectly at the root during local development.
    [Fact]
    public void IconRail_UsesRelativeHrefs_SoTheyResolveAgainstBaseHref()
    {
        var cut = Render<IconRail>();

        var hrefs = cut.FindAll("a[href]")
            .Select(a => a.GetAttribute("href")!)
            .ToArray();

        Assert.NotEmpty(hrefs);
        Assert.DoesNotContain(hrefs, href => href.StartsWith('/'));
    }

    [Fact]
    public void TopBar_RendersLogoAndAddPersonAction()
    {
        var cut = Render<TopBar>();

        Assert.NotNull(cut.Find("[data-testid='app-logo']"));
        Assert.NotNull(cut.Find("[data-testid='add-person-button']"));
    }

    [Fact]
    public void MainLayout_RendersRailTopBarAndBodyContent()
    {
        var cut = Render<MainLayout>(parameters => parameters
            .Add(p => p.Body, builder => builder.AddMarkupContent(0, "<p>body content</p>")));

        Assert.NotNull(cut.Find("[data-testid='nav-tree']"));
        Assert.NotNull(cut.Find("[data-testid='app-logo']"));
        Assert.Contains("body content", cut.Markup);
    }
}
