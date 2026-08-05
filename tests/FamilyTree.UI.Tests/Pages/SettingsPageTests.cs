using Bunit;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.UI.Pages;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FamilyTree.UI.Tests.Pages;

/// <summary>
/// Both buttons on this page destroy the entire tree, and the browser holds the
/// only copy of it. Export and undo do not exist yet, so the confirmation is the
/// sole thing standing between a misclick and unrecoverable loss.
/// </summary>
public class SettingsPageTests : ShellTestContext
{
    private void GivenTreeContains(int peopleCount)
    {
        var people = Enumerable.Range(0, peopleCount)
            .Select(i => new Person($"Person{i}", "Test", Gender.Unknown))
            .ToArray();

        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(people);
    }

    private Mock<ITreeDataAdministration> ReplaceTreeDataWithSpy()
    {
        var spy = new Mock<ITreeDataAdministration>();
        spy.Setup(t => t.EnsureDurableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageDurability(true, false));

        Services.AddSingleton(spy.Object);
        return spy;
    }

    [Fact]
    public void LoadingTheSample_DoesNotWipeImmediately_WhenTheTreeHasPeople()
    {
        GivenTreeContains(3);
        var treeData = ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=load-sample]").Click();

        treeData.Verify(t => t.LoadSampleFamilyAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Clearing_DoesNotWipeImmediately_WhenTheTreeHasPeople()
    {
        GivenTreeContains(3);
        var treeData = ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=clear-data]").Click();

        treeData.Verify(t => t.ClearAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ConfirmationNamesWhatWillBeLost()
    {
        GivenTreeContains(3);
        ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=load-sample]").Click();

        Assert.Contains(
            "3 people and 0 relationships",
            cut.Find("[data-testid=settings-confirm-text]").TextContent);
    }

    [Fact]
    public void ConfirmationSaysThereIsNoUndo()
    {
        GivenTreeContains(3);
        ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=clear-data]").Click();

        Assert.Contains("no undo", cut.Find("[data-testid=settings-confirm-text]").TextContent);
    }

    // A count of one must not read "1 people" in a message whose whole job is to
    // be read carefully before an irreversible action.
    [Fact]
    public void ConfirmationUsesSingularWordingForASinglePerson()
    {
        GivenTreeContains(1);
        ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=clear-data]").Click();

        Assert.Contains("1 person and", cut.Find("[data-testid=settings-confirm-text]").TextContent);
    }

    [Fact]
    public void Cancelling_LeavesTheDataAlone()
    {
        GivenTreeContains(3);
        var treeData = ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=clear-data]").Click();
        cut.Find("[data-testid=cancel-destructive]").Click();

        treeData.Verify(t => t.ClearAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(cut.FindAll("[data-testid=settings-confirm]"));
    }

    [Fact]
    public void Confirming_PerformsTheClear()
    {
        GivenTreeContains(3);
        var treeData = ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=clear-data]").Click();
        cut.Find("[data-testid=confirm-destructive]").Click();

        treeData.Verify(t => t.ClearAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Confirming_LoadsTheSample()
    {
        GivenTreeContains(3);
        var treeData = ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=load-sample]").Click();
        cut.Find("[data-testid=confirm-destructive]").Click();

        treeData.Verify(t => t.LoadSampleFamilyAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Cancelling a clear and then confirming a load must not run the clear the
    // user backed out of.
    [Fact]
    public void ConfirmingRunsOnlyTheMostRecentlyRequestedAction()
    {
        GivenTreeContains(3);
        var treeData = ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=clear-data]").Click();
        cut.Find("[data-testid=cancel-destructive]").Click();
        cut.Find("[data-testid=load-sample]").Click();
        cut.Find("[data-testid=confirm-destructive]").Click();

        treeData.Verify(t => t.LoadSampleFamilyAsync(It.IsAny<CancellationToken>()), Times.Once);
        treeData.Verify(t => t.ClearAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void AnEmptyTreeSkipsTheConfirmation()
    {
        var treeData = ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=load-sample]").Click();

        Assert.Empty(cut.FindAll("[data-testid=settings-confirm]"));
        treeData.Verify(t => t.LoadSampleFamilyAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Another tab writes to the same database. Trusting the count this page
    // loaded with means a tab opened against an empty tree skips the
    // confirmation and silently destroys work it never saw.
    [Fact]
    public void RechecksStorageBeforeDecidingTheTreeIsEmpty()
    {
        var treeData = ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        // Rendered empty; something else populates the tree afterwards.
        GivenTreeContains(4);

        cut.Find("[data-testid=load-sample]").Click();

        Assert.Contains(
            "4 people",
            cut.Find("[data-testid=settings-confirm-text]").TextContent);
        treeData.Verify(t => t.LoadSampleFamilyAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Phantoms are excluded from the displayed count but not from the wipe, so
    // a tree of nothing but unidentified ancestors displays as empty while
    // still holding records a load would destroy.
    [Fact]
    public void APhantomOnlyTreeIsNotTreatedAsEmpty()
    {
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Person.CreatePhantom(), Person.CreatePhantom()]);
        var treeData = ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=load-sample]").Click();

        Assert.NotEmpty(cut.FindAll("[data-testid=settings-confirm]"));
        treeData.Verify(t => t.LoadSampleFamilyAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // role="alertdialog" is a promise about behaviour, not just a label.
    [Fact]
    public void EscapeCancelsTheConfirmation()
    {
        GivenTreeContains(3);
        var treeData = ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=load-sample]").Click();
        cut.Find("[data-testid=settings-confirm]").KeyDown(Key.Escape);

        Assert.Empty(cut.FindAll("[data-testid=settings-confirm]"));
        treeData.Verify(t => t.LoadSampleFamilyAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void AnUnrelatedKeyDoesNotCancelTheConfirmation()
    {
        GivenTreeContains(3);
        ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=load-sample]").Click();
        cut.Find("[data-testid=settings-confirm]").KeyDown(Key.Enter);

        Assert.NotEmpty(cut.FindAll("[data-testid=settings-confirm]"));
    }

    [Fact]
    public void ConfirmationPanelCanTakeFocus()
    {
        GivenTreeContains(3);
        ReplaceTreeDataWithSpy();
        var cut = Render<SettingsPage>();

        cut.Find("[data-testid=load-sample]").Click();

        Assert.Equal("-1", cut.Find("[data-testid=settings-confirm]").GetAttribute("tabindex"));
    }
}
