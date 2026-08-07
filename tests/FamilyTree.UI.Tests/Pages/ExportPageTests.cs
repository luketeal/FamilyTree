using System.Text.Json;
using Bunit;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.UI.Pages.Export;
using Moq;

namespace FamilyTree.UI.Tests.Pages;

/// <summary>
/// The page that produces the only copy of the tree that outlives this browser.
/// </summary>
public class ExportPageTests : ShellTestContext
{
    private void GivenTreeContains(params Person[] people) =>
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(people);

    private static Person Ada() => new("Ada", "Lovelace", Gender.Female);

    [Fact]
    public void ReportsThatNothingHasEverBeenExported()
    {
        GivenTreeContains(Ada());

        var cut = Render<ExportPage>();

        Assert.Equal("Never exported", cut.Find("[data-testid=export-status]").TextContent.Trim());
    }

    [Fact]
    public void ReportsHowLongAgoTheLastExportWas()
    {
        GivenTreeContains(Ada());
        Backups.LastExport = Clock.Now.AddDays(-3);

        var cut = Render<ExportPage>();

        Assert.Equal("Last exported 3 days ago", cut.Find("[data-testid=export-status]").TextContent.Trim());
    }

    // The prompt is the product requirement, not the count: a user who has never
    // exported has no idea that losing the browser loses the tree.
    [Fact]
    public void WarnsWhenTheTreeHasNeverBeenBackedUp()
    {
        GivenTreeContains(Ada());

        var cut = Render<ExportPage>();

        Assert.Contains("never been exported", cut.Find("[data-testid=export-stale]").TextContent);
    }

    [Fact]
    public void WarnsWhenTheTreeHasChangedSinceTheLastBackup()
    {
        GivenTreeContains(Ada());
        Backups.LastExport = Clock.Now.AddDays(-9);
        Backups.LastChange = Clock.Now.AddDays(-1);

        var cut = Render<ExportPage>();

        Assert.Contains("changed since the last backup, 9 days ago",
            cut.Find("[data-testid=export-stale]").TextContent);
    }

    // Exported once and untouched since is the state this page should be quiet
    // about, however long ago the export was.
    [Fact]
    public void DoesNotWarnWhenNothingHasChangedSinceTheBackup()
    {
        GivenTreeContains(Ada());
        Backups.LastChange = Clock.Now.AddDays(-40);
        Backups.LastExport = Clock.Now.AddDays(-39);

        var cut = Render<ExportPage>();

        Assert.Empty(cut.FindAll("[data-testid=export-stale]"));
    }

    // An empty tree has nothing to warn about, and warning anyway would make the
    // first thing a new user sees an alarm about data they have not entered.
    [Fact]
    public void DoesNotWarnWhenThereIsNothingToLose()
    {
        var cut = Render<ExportPage>();

        Assert.Empty(cut.FindAll("[data-testid=export-stale]"));
    }

    [Fact]
    public void HandsTheBrowserADatedFileName()
    {
        GivenTreeContains(Ada());
        var cut = Render<ExportPage>();

        cut.Find("[data-testid=export-download]").Click();

        Assert.Equal("familytree-2026-08-07.json", Downloads.FileName);
    }

    [Fact]
    public void HandsTheBrowserTheTree()
    {
        GivenTreeContains(Ada());
        var cut = Render<ExportPage>();

        cut.Find("[data-testid=export-download]").Click();

        using var json = JsonDocument.Parse(Downloads.Content!);
        Assert.Equal("Ada", json.RootElement.GetProperty("people")[0].GetProperty("firstName").GetString());
    }

    [Fact]
    public void RemembersThatTheExportHappened()
    {
        GivenTreeContains(Ada());
        var cut = Render<ExportPage>();

        cut.Find("[data-testid=export-download]").Click();

        Assert.Equal(Clock.Now, Backups.LastExport);
    }

    [Fact]
    public void StopsWarningOnceTheTreeHasBeenExported()
    {
        GivenTreeContains(Ada());
        var cut = Render<ExportPage>();

        cut.Find("[data-testid=export-download]").Click();

        Assert.Empty(cut.FindAll("[data-testid=export-stale]"));
        Assert.Equal("Last exported today", cut.Find("[data-testid=export-status]").TextContent.Trim());
    }

    [Fact]
    public void ReportsWhatWasSaved()
    {
        GivenTreeContains(Ada(), new Person("Grace", "Hopper", Gender.Female));
        var cut = Render<ExportPage>();

        cut.Find("[data-testid=export-download]").Click();

        Assert.Contains("2 people", cut.Find("[data-testid=export-summary]").TextContent);
    }

    // An empty file would be a backup of nothing, and the natural thing to do
    // with a backup is save it over the last one.
    [Fact]
    public void RefusesToExportAnEmptyTree()
    {
        var cut = Render<ExportPage>();

        cut.Find("[data-testid=export-download]").Click();

        Assert.Contains("nothing to export", cut.Find("[data-testid=export-error]").TextContent);
        Assert.Equal(0, Downloads.Count);
    }

    // A subtraction the user cannot see is one they will discover by noticing
    // the counts disagree, long after they could do anything about it.
    [Fact]
    public void SaysWhenUnidentifiedAncestorsWereLeftOut()
    {
        GivenTreeContains(Ada(), Person.CreatePhantom());
        var cut = Render<ExportPage>();

        cut.Find("[data-testid=export-download]").Click();

        Assert.Contains("unidentified ancestor",
            cut.Find("[data-testid=export-phantom-note]").TextContent);
    }

    [Fact]
    public void SaysNothingAboutPhantomsWhenThereAreNone()
    {
        GivenTreeContains(Ada());
        var cut = Render<ExportPage>();

        cut.Find("[data-testid=export-download]").Click();

        Assert.Empty(cut.FindAll("[data-testid=export-phantom-note]"));
    }
}
