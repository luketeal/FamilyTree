using Bunit;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.UI.Layout;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FamilyTree.UI.Tests.Layout;

/// <summary>
/// The standing warning that this browser holds work no file has a copy of.
/// </summary>
/// <remarks>
/// Its whole value is in when it appears. A banner that shows on an empty tree
/// is noise on the first screen a new user sees; one that stays quiet after the
/// user has entered fifty people since their last export is the reason somebody
/// loses that work without ever having been told it was possible.
///
/// The trigger is "has the tree changed since the last export", not "how long
/// has it been". An elapsed-time rule gets both of those cases backwards.
/// </remarks>
public class BackupReminderTests : ShellTestContext
{
    private void GivenTreeContains(params Person[] people) =>
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(people);

    private static Person Ada() => new("Ada", "Lovelace", Gender.Female);

    private void GivenExportedAt(int daysAgo) => Backups.LastExport = Clock.Now.AddDays(-daysAgo);

    private void GivenChangedAt(int daysAgo) => Backups.LastChange = Clock.Now.AddDays(-daysAgo);

    [Fact]
    public void WarnsWhenATreeHasNeverBeenExported()
    {
        GivenTreeContains(Ada());

        var cut = Render<BackupReminder>();

        Assert.Contains("never been backed up",
            cut.Find("[data-testid=backup-reminder-text]").TextContent);
    }

    [Fact]
    public void SaysNothingOnAnEmptyTree()
    {
        var cut = Render<BackupReminder>();

        Assert.Empty(cut.FindAll("[data-testid=backup-reminder]"));
    }

    // A tree of nothing but unidentified ancestors displays as empty and still
    // holds records a lost browser would destroy.
    [Fact]
    public void WarnsWhenTheTreeHoldsOnlyPhantoms()
    {
        GivenTreeContains(Person.CreatePhantom());

        var cut = Render<BackupReminder>();

        Assert.NotNull(cut.Find("[data-testid=backup-reminder]"));
    }

    // ---- The trigger ----

    [Fact]
    public void SaysNothingWhenNothingHasChangedSinceTheExport()
    {
        GivenTreeContains(Ada());
        GivenChangedAt(daysAgo: 10);
        GivenExportedAt(daysAgo: 9);

        var cut = Render<BackupReminder>();

        Assert.Empty(cut.FindAll("[data-testid=backup-reminder]"));
    }

    // The case an elapsed-time rule gets wrong in the direction that costs only
    // annoyance: exported once, untouched for a year, nagged every week since.
    [Fact]
    public void SaysNothingAboutAnOldExportOfAnUnchangedTree()
    {
        GivenTreeContains(Ada());
        GivenChangedAt(daysAgo: 400);
        GivenExportedAt(daysAgo: 399);

        var cut = Render<BackupReminder>();

        Assert.Empty(cut.FindAll("[data-testid=backup-reminder]"));
    }

    // The case it gets wrong in the direction that costs data: exported this
    // morning, fifty people entered since, and silent for the rest of the week.
    [Fact]
    public void WarnsWhenTheTreeChangedAfterAVeryRecentExport()
    {
        GivenTreeContains(Ada());
        GivenExportedAt(daysAgo: 0);
        Backups.LastChange = Clock.Now.AddMinutes(1);

        var cut = Render<BackupReminder>();

        Assert.Contains("changes that are not in any backup",
            cut.Find("[data-testid=backup-reminder-text]").TextContent);
    }

    [Fact]
    public void SaysHowOldTheLastBackupIsAlongsideTheWarning()
    {
        GivenTreeContains(Ada());
        GivenExportedAt(daysAgo: 3);
        GivenChangedAt(daysAgo: 1);

        var cut = Render<BackupReminder>();

        Assert.Contains("Last exported 3 days ago",
            cut.Find("[data-testid=backup-reminder-text]").TextContent);
    }

    // A browser that refuses the localStorage write leaves no change recorded.
    // Silence is the wrong failure, but a warning that can never be cleared is
    // worse: it trains people to ignore the one that matters.
    [Fact]
    public void SaysNothingWhenNoChangeWasEverRecordedAfterAnExport()
    {
        GivenTreeContains(Ada());
        GivenExportedAt(daysAgo: 2);

        var cut = Render<BackupReminder>();

        Assert.Empty(cut.FindAll("[data-testid=backup-reminder]"));
    }

    // ---- Reacting ----

    [Fact]
    public void OffersAWayToActOnIt()
    {
        GivenTreeContains(Ada());

        var cut = Render<BackupReminder>();

        Assert.Equal("export",
            cut.Find("[data-testid=backup-reminder-export]").GetAttribute("href"));
    }

    [Fact]
    public void CanBeDismissed()
    {
        GivenTreeContains(Ada());
        var cut = Render<BackupReminder>();

        cut.Find("[data-testid=backup-reminder-dismiss]").Click();

        Assert.Empty(cut.FindAll("[data-testid=backup-reminder]"));
    }

    // Dismissal covers the changes the user was told about, not every change
    // they will ever make. Waving the banner away and then entering a new person
    // is a new reason to be warned, and the old dismissal must not silence it.
    [Fact]
    public async Task ComesBackAfterAFurtherChange()
    {
        GivenTreeContains(Ada());
        var cut = Render<BackupReminder>();
        cut.Find("[data-testid=backup-reminder-dismiss]").Click();
        Assert.Empty(cut.FindAll("[data-testid=backup-reminder]"));

        await cut.InvokeAsync(() =>
            Services.GetRequiredService<TreeDataNotifier>().NotifyChangedAsync());

        Assert.NotNull(cut.Find("[data-testid=backup-reminder]"));
    }

    // Adding a first person is exactly the moment a previously empty tree
    // becomes something worth protecting, and it happens without this component
    // re-rendering unless it is listening.
    [Fact]
    public async Task AppearsWhenTheFirstPersonIsAdded()
    {
        var cut = Render<BackupReminder>();
        Assert.Empty(cut.FindAll("[data-testid=backup-reminder]"));

        GivenTreeContains(Ada());
        await cut.InvokeAsync(() =>
            Services.GetRequiredService<TreeDataNotifier>().NotifyChangedAsync());

        Assert.NotNull(cut.Find("[data-testid=backup-reminder]"));
    }

    // The tracker is what records a change, and it only exists once something
    // injects it. This component is what does — it renders on every page — so if
    // that wiring were dropped, no mutation would ever be noticed.
    [Fact]
    public async Task ChangingTheTreeIsRecordedAsWorkNoBackupHas()
    {
        GivenTreeContains(Ada());
        var cut = Render<BackupReminder>();
        Backups.LastExport = Clock.Now;
        Backups.LastChange = null;

        await cut.InvokeAsync(() =>
            Services.GetRequiredService<TreeDataNotifier>().NotifyChangedAsync());

        Assert.Equal(Clock.Now, Backups.LastChange);
    }
}
