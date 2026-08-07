using Bunit;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.UI.Layout;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FamilyTree.UI.Tests.Layout;

/// <summary>
/// The standing warning that this browser holds the only copy.
/// </summary>
/// <remarks>
/// Its whole value is in when it appears. A banner that shows on an empty tree
/// is noise on the first screen a new user sees; one that fails to show when the
/// tree has never been exported is the reason somebody loses years of research
/// without ever having been told it was possible.
/// </remarks>
public class BackupReminderTests : ShellTestContext
{
    private void GivenTreeContains(params Person[] people) =>
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(people);

    private static Person Ada() => new("Ada", "Lovelace", Gender.Female);

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

    [Fact]
    public void SaysNothingWhenTheBackupIsRecent()
    {
        GivenTreeContains(Ada());
        ExportHistory.LastExport = Clock.Now.AddDays(-2);

        var cut = Render<BackupReminder>();

        Assert.Empty(cut.FindAll("[data-testid=backup-reminder]"));
    }

    [Fact]
    public void WarnsOnceTheBackupIsAWeekOld()
    {
        GivenTreeContains(Ada());
        ExportHistory.LastExport = Clock.Now.AddDays(-BackupStatus.StaleAfterDays);

        var cut = Render<BackupReminder>();

        Assert.Contains($"{BackupStatus.StaleAfterDays} days ago",
            cut.Find("[data-testid=backup-reminder-text]").TextContent);
    }

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
}
