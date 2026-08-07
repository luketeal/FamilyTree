using Bunit;
using FamilyTree.Application.Common;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.UI.Pages.Import;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FamilyTree.UI.Tests.Pages;

/// <summary>
/// The restore half of the durability mechanism, and the one place in the app
/// that can replace a whole tree in a single click.
/// </summary>
public class ImportPageTests : ShellTestContext
{
    private const string AdaId = "11111111-1111-1111-1111-111111111111";
    private const string GraceId = "22222222-2222-2222-2222-222222222222";

    private static readonly string GoodFile = $$"""
        {
          "schemaVersion": {{TreeSchema.Version}},
          "exportedAt": "2026-08-01T09:00:00+00:00",
          "people": [
            {"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"},
            {"id": "{{GraceId}}", "firstName": "Grace", "lastName": "Hopper"}
          ]
        }
        """;

    private void GivenTreeContains(params Person[] people) =>
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(people);

    private static IRenderedComponent<ImportPage> Choose(
        IRenderedComponent<ImportPage> cut, string json, string name = "familytree-2026-08-01.json")
    {
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(json, name));
        return cut;
    }

    [Fact]
    public void ShowsNoPreviewBeforeAFileIsChosen()
    {
        var cut = Render<ImportPage>();

        Assert.Empty(cut.FindAll("[data-testid=import-preview]"));
    }

    [Fact]
    public void PreviewsWhatIsInTheFile()
    {
        var cut = Choose(Render<ImportPage>(), GoodFile);

        Assert.Contains("2 people", cut.Find("[data-testid=import-preview-counts]").TextContent);
    }

    [Fact]
    public void ShowsWhenTheFileWasWritten()
    {
        var cut = Choose(Render<ImportPage>(), GoodFile);

        Assert.Contains("2026", cut.Find("[data-testid=import-preview-exported]").TextContent);
    }

    // Nothing is written until the user has chosen what happens to what is
    // already here, so an unreadable file must stop at the preview.
    [Fact]
    public void RefusesAFileWithNoSchemaVersion()
    {
        var cut = Choose(Render<ImportPage>(), """{"people": []}""");

        Assert.Contains("schemaVersion", cut.Find("[data-testid=import-error]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=import-preview]"));
    }

    [Fact]
    public void RefusesAFileFromANewerVersion()
    {
        var cut = Choose(Render<ImportPage>(),
            $$"""{"schemaVersion": {{TreeSchema.Version + 1}}, "people": []}""");

        Assert.Contains("newer version", cut.Find("[data-testid=import-error]").TextContent);
    }

    [Fact]
    public void RefusesSomethingThatIsNotAnExport()
    {
        var cut = Choose(Render<ImportPage>(), "a photograph, probably", "holiday.jpg");

        Assert.Contains("not readable", cut.Find("[data-testid=import-error]").TextContent);
    }

    // Keeping what is here is the safe default, so it is the one already chosen.
    [Fact]
    public void DefaultsToKeepingWhatIsAlreadyHere()
    {
        var cut = Choose(Render<ImportPage>(), GoodFile);

        var chosen = cut.Find("[data-testid=resolution-skip] input");
        Assert.True(chosen.HasAttribute("checked"));
    }

    [Fact]
    public void ImportsImmediatelyWhenNothingWouldBeLost()
    {
        var cut = Choose(Render<ImportPage>(), GoodFile);

        cut.Find("[data-testid=import-run]").Click();

        TreeData.Verify(
            t => t.ReplaceAllAsync(It.IsAny<TreeSnapshot>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void ReportsWhatTheImportDid()
    {
        var cut = Choose(Render<ImportPage>(), GoodFile);

        cut.Find("[data-testid=import-run]").Click();

        Assert.Contains("2 added", cut.Find("[data-testid=import-result-summary]").TextContent);
    }

    // The file has been consumed; leaving the button on screen invites a second
    // press, which under "Replace everything" is a second irreversible prompt
    // for a tree that is already the file's.
    [Fact]
    public void ClearsThePreviewOnceTheFileHasBeenImported()
    {
        var cut = Choose(Render<ImportPage>(), GoodFile);

        cut.Find("[data-testid=import-run]").Click();

        Assert.Empty(cut.FindAll("[data-testid=import-preview]"));
    }

    // ---- The destructive path ----

    private static void ChooseOverwrite(IRenderedComponent<ImportPage> cut) =>
        cut.Find("[data-testid=resolution-overwrite] input").Change("Overwrite");

    [Fact]
    public void ReplacingATreeThatHasPeopleAsksFirst()
    {
        GivenTreeContains(new Person("Ada", "Lovelace", Gender.Female));
        var cut = Choose(Render<ImportPage>(), GoodFile);
        ChooseOverwrite(cut);

        cut.Find("[data-testid=import-run]").Click();

        Assert.NotNull(cut.Find("[data-testid=import-confirm]"));
        TreeData.Verify(
            t => t.ReplaceAllAsync(It.IsAny<TreeSnapshot>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Names what is about to be lost rather than asking a generic "are you sure".
    [Fact]
    public void TheConfirmationSaysHowMuchWouldBeDestroyed()
    {
        GivenTreeContains(
            new Person("Ada", "Lovelace", Gender.Female),
            new Person("Grace", "Hopper", Gender.Female));
        var cut = Choose(Render<ImportPage>(), GoodFile);
        ChooseOverwrite(cut);

        cut.Find("[data-testid=import-run]").Click();

        Assert.Contains("2 people", cut.Find("[data-testid=import-confirm-text]").TextContent);
    }

    [Fact]
    public void ConfirmingReplacesTheTree()
    {
        GivenTreeContains(new Person("Ada", "Lovelace", Gender.Female));
        var cut = Choose(Render<ImportPage>(), GoodFile);
        ChooseOverwrite(cut);
        cut.Find("[data-testid=import-run]").Click();

        cut.Find("[data-testid=import-confirm-run]").Click();

        TreeData.Verify(
            t => t.ReplaceAllAsync(It.IsAny<TreeSnapshot>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void CancellingLeavesTheTreeAlone()
    {
        GivenTreeContains(new Person("Ada", "Lovelace", Gender.Female));
        var cut = Choose(Render<ImportPage>(), GoodFile);
        ChooseOverwrite(cut);
        cut.Find("[data-testid=import-run]").Click();

        cut.Find("[data-testid=import-confirm-cancel]").Click();

        Assert.Empty(cut.FindAll("[data-testid=import-confirm]"));
        TreeData.Verify(
            t => t.ReplaceAllAsync(It.IsAny<TreeSnapshot>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // The prompt describes what "Replace everything" would delete. Moving to an
    // option that deletes nothing must take it off screen, or the next click
    // confirms something the user is no longer asking for.
    [Fact]
    public void ChangingTheChoiceWithdrawsTheConfirmation()
    {
        GivenTreeContains(new Person("Ada", "Lovelace", Gender.Female));
        var cut = Choose(Render<ImportPage>(), GoodFile);
        ChooseOverwrite(cut);
        cut.Find("[data-testid=import-run]").Click();

        cut.Find("[data-testid=resolution-skip] input").Change("Skip");

        Assert.Empty(cut.FindAll("[data-testid=import-confirm]"));
    }

    [Fact]
    public void EscapeWithdrawsTheConfirmation()
    {
        GivenTreeContains(new Person("Ada", "Lovelace", Gender.Female));
        var cut = Choose(Render<ImportPage>(), GoodFile);
        ChooseOverwrite(cut);
        cut.Find("[data-testid=import-run]").Click();

        cut.Find("[data-testid=import-confirm]").KeyDown(Key.Escape);

        Assert.Empty(cut.FindAll("[data-testid=import-confirm]"));
    }

    // An empty tree has nothing to lose, so the confirmation would be pure
    // friction. This pins that distinction so it does not drift.
    [Fact]
    public void ReplacingAnEmptyTreeDoesNotAsk()
    {
        var cut = Choose(Render<ImportPage>(), GoodFile);
        ChooseOverwrite(cut);

        cut.Find("[data-testid=import-run]").Click();

        Assert.Empty(cut.FindAll("[data-testid=import-confirm]"));
        TreeData.Verify(
            t => t.ReplaceAllAsync(It.IsAny<TreeSnapshot>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // A tree of nothing but unidentified ancestors displays as empty, and a
    // replace would still destroy the records in it.
    [Fact]
    public void ReplacingATreeOfOnlyPhantomsStillAsks()
    {
        GivenTreeContains(Person.CreatePhantom());
        var cut = Choose(Render<ImportPage>(), GoodFile);
        ChooseOverwrite(cut);

        cut.Find("[data-testid=import-run]").Click();

        Assert.NotNull(cut.Find("[data-testid=import-confirm]"));
    }

    [Fact]
    public void TellsTheRestOfTheAppThatTheTreeChanged()
    {
        var notified = 0;
        Services.GetRequiredService<TreeDataNotifier>().Changed += () =>
        {
            notified++;
            return Task.CompletedTask;
        };

        var cut = Choose(Render<ImportPage>(), GoodFile);
        cut.Find("[data-testid=import-run]").Click();

        Assert.Equal(1, notified);
    }

    [Fact]
    public void ShowsTheRecordsItCouldNotRead()
    {
        var cut = Choose(Render<ImportPage>(), $$"""
            {
              "schemaVersion": {{TreeSchema.Version}},
              "people": [
                {"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"},
                {"id": "{{GraceId}}", "firstName": "", "lastName": ""}
              ]
            }
            """);

        Assert.Contains("1 record", cut.Find("[data-testid=import-warnings]").TextContent);
    }
}
