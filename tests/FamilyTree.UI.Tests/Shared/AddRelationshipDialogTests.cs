using Bunit;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;
using FamilyTree.UI.Shared;
using Moq;

namespace FamilyTree.UI.Tests.Shared;

using Kind = AddRelationshipDialog.RelationshipKind;

public class AddRelationshipDialogTests : ShellTestContext
{
    private readonly Person _subject = new("Susan", "Hartley", Gender.Female);
    private readonly Person _candidate = new("Arthur", "Whitfield", Gender.Male);

    private void GivenRoster(params Person[] extra) =>
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_subject, _candidate, .. extra]);

    private IRenderedComponent<AddRelationshipDialog> RenderDialog(
        IReadOnlyCollection<Kind>? kinds = null,
        Guid? replacing = null,
        string? replacingName = null,
        Action? onSaved = null) =>
        Render<AddRelationshipDialog>(p =>
        {
            p.Add(c => c.Visible, true);
            p.Add(c => c.SubjectId, _subject.Id);
            p.Add(c => c.SubjectName, "Susan Hartley");
            p.Add(c => c.Kinds, kinds ?? [Kind.BiologicalParent, Kind.BiologicalChild]);
            p.Add(c => c.ReplacingParentId, replacing);
            p.Add(c => c.ReplacingParentName, replacingName);
            if (onSaved is not null)
            {
                p.Add(c => c.Saved, onSaved);
            }
        });

    /// <summary>Walks the wizard as far as the details step with a person chosen.</summary>
    private void ChooseCandidate(IRenderedComponent<AddRelationshipDialog> cut)
    {
        if (cut.FindAll("[data-testid='relationship-dialog-search-query']").Count == 0)
        {
            cut.Find("[data-testid='relationship-dialog-next']").Click();
        }

        cut.Find($"[data-testid='relationship-dialog-search-option-{_candidate.Id}']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();
    }

    // ---- The wizard ----

    [Fact]
    public void StartsOnTheKindStepWhenThereIsAChoiceToMake()
    {
        GivenRoster();

        var cut = RenderDialog();

        Assert.NotNull(cut.Find("[data-testid='relationship-dialog-kind-biologicalparent']"));
    }

    // Making somebody confirm the only option is a step that exists for the
    // code's convenience rather than theirs.
    [Fact]
    public void SkipsTheKindStepWhenOnlyOneKindIsOffered()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);

        Assert.Empty(cut.FindAll("[data-testid='relationship-dialog-kind-biologicalparent']"));
        Assert.NotNull(cut.Find("[data-testid='relationship-dialog-search-query']"));
    }

    [Fact]
    public void ShowsTwoStepsRatherThanThreeWhenTheKindIsAlreadyKnown()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);

        Assert.Equal(2, cut.FindAll("[data-testid^='relationship-dialog-step-']").Count);
    }

    // The wizard must not advance past a step it has no answer for, or the
    // details step describes a relationship with nobody at one end.
    [Fact]
    public void WillNotLeaveThePersonStepUntilSomebodyIsChosen()
    {
        GivenRoster();
        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);

        Assert.True(cut.Find("[data-testid='relationship-dialog-next']").HasAttribute("disabled"));
    }

    [Fact]
    public void AdvancesOnceSomebodyIsChosen()
    {
        GivenRoster();
        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);

        cut.Find($"[data-testid='relationship-dialog-search-option-{_candidate.Id}']").Click();

        Assert.False(cut.Find("[data-testid='relationship-dialog-next']").HasAttribute("disabled"));
    }

    [Fact]
    public void SaysWhatIsAboutToBeSaved()
    {
        GivenRoster();
        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);

        ChooseCandidate(cut);

        Assert.Contains(
            "Arthur Whitfield will be recorded as a biological parent of Susan Hartley",
            cut.Find("[data-testid='relationship-dialog-summary']").TextContent);
    }

    [Fact]
    public void DescribesAChildRatherThanAParentWhenThatIsTheKind()
    {
        GivenRoster();
        var cut = RenderDialog(kinds: [Kind.BiologicalChild]);

        ChooseCandidate(cut);

        Assert.Contains("biological child of Susan Hartley",
            cut.Find("[data-testid='relationship-dialog-summary']").TextContent);
    }

    [Fact]
    public void GoesBackToThePersonStep()
    {
        GivenRoster();
        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);
        ChooseCandidate(cut);

        cut.Find("[data-testid='relationship-dialog-back']").Click();

        Assert.NotNull(cut.Find("[data-testid='relationship-dialog-search-query']"));
    }

    // ---- Saving ----

    [Fact]
    public void WritesTheLink()
    {
        GivenRoster();
        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);
        ChooseCandidate(cut);

        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Biological.Verify(
            r => r.AddAsync(
                It.Is<BiologicalParentChild>(l =>
                    l.ParentId == _candidate.Id && l.ChildId == _subject.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void WritesTheLinkTheOtherWayRoundForAChild()
    {
        GivenRoster();
        var cut = RenderDialog(kinds: [Kind.BiologicalChild]);
        ChooseCandidate(cut);

        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Biological.Verify(
            r => r.AddAsync(
                It.Is<BiologicalParentChild>(l =>
                    l.ParentId == _subject.Id && l.ChildId == _candidate.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void DefaultsToConfirmedCertainty()
    {
        GivenRoster();
        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);
        ChooseCandidate(cut);

        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Biological.Verify(
            r => r.AddAsync(
                It.Is<BiologicalParentChild>(l => l.Certainty == RelationshipCertainty.Confirmed),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // PR 13 gives Likely and Speculative a visual treatment. The value has to be
    // captured and stored now, or that PR has nothing to render.
    [Fact]
    public void StoresTheChosenCertainty()
    {
        GivenRoster();
        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);
        ChooseCandidate(cut);

        cut.Find("[data-testid='relationship-dialog-certainty']")
            .Change(nameof(RelationshipCertainty.Speculative));
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Biological.Verify(
            r => r.AddAsync(
                It.Is<BiologicalParentChild>(l => l.Certainty == RelationshipCertainty.Speculative),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void TellsTheCallerToReload()
    {
        GivenRoster();
        var saved = 0;
        var cut = RenderDialog(kinds: [Kind.BiologicalParent], onSaved: () => saved++);
        ChooseCandidate(cut);

        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Assert.Equal(1, saved);
    }

    // A refusal has to leave the wizard where it was. Closing on one would lose
    // the chosen person and make the user rediscover the rule by repeating the
    // whole thing.
    [Fact]
    public void StaysOpenAndExplainsWhenTheServiceRefuses()
    {
        GivenRoster();
        Biological
            .Setup(r => r.GetParentLinksForChildAsync(_subject.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BiologicalParentChild(_candidate.Id, _subject.Id)]);

        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Assert.Contains("already recorded as a biological parent",
            cut.Find("[data-testid='relationship-dialog-error']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='relationship-dialog-save']"));
    }

    [Fact]
    public void WritesNothingWhenTheServiceRefuses()
    {
        GivenRoster();
        Biological
            .Setup(r => r.GetParentLinksForChildAsync(_subject.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BiologicalParentChild(_candidate.Id, _subject.Id)]);

        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Biological.Verify(
            r => r.AddAsync(It.IsAny<BiologicalParentChild>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---- Replacing (US-009) ----

    // "A confirmation dialog shows both old and new parent before saving" is what
    // the details step becomes in this mode, rather than a fourth dialog.
    [Fact]
    public void NamesBothTheOldAndTheNewParentBeforeReplacing()
    {
        var wrong = new Person("Vera", "Whitfield", Gender.Female);
        GivenRoster(wrong);
        var cut = RenderDialog(
            kinds: [Kind.BiologicalParent],
            replacing: wrong.Id,
            replacingName: "Vera Whitfield");

        ChooseCandidate(cut);

        var summary = cut.Find("[data-testid='relationship-dialog-summary']").TextContent;
        Assert.Contains("Vera Whitfield", summary);
        Assert.Contains("Arthur Whitfield", summary);
    }

    [Fact]
    public void ReplacesThroughTheAtomicRepositoryCall()
    {
        var wrong = new Person("Vera", "Whitfield", Gender.Female);
        var existing = new BiologicalParentChild(wrong.Id, _subject.Id);
        GivenRoster(wrong);
        Biological.Setup(r => r.GetAsync(wrong.Id, _subject.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        Biological
            .Setup(r => r.GetParentLinksForChildAsync(_subject.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);

        var cut = RenderDialog(
            kinds: [Kind.BiologicalParent],
            replacing: wrong.Id,
            replacingName: "Vera Whitfield");
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Biological.Verify(
            r => r.ReplaceParentAsync(
                existing.Id,
                It.Is<BiologicalParentChild>(l => l.ParentId == _candidate.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Biological.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Reopening ----

    // A cancel halfway through must not leave a person selected who is then
    // saved against whatever the dialog is opened for next.
    [Fact]
    public void ForgetsThePreviousChoiceWhenItIsReopened()
    {
        GivenRoster();
        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);
        cut.Find($"[data-testid='relationship-dialog-search-option-{_candidate.Id}']").Click();
        cut.Find("[data-testid='relationship-dialog-cancel']").Click();

        cut.Render(p => p
            .Add(c => c.Visible, true)
            .Add(c => c.SubjectId, _subject.Id)
            .Add(c => c.SubjectName, "Susan Hartley")
            .Add(c => c.Kinds, new[] { Kind.BiologicalParent }));

        Assert.True(cut.Find("[data-testid='relationship-dialog-next']").HasAttribute("disabled"));
    }

    // The panel is fixed-position and scroll-locked by [data-overlay]; without
    // the attribute the page behind it scrolls, which on iOS happens the moment
    // the keyboard shrinks the visual viewport.
    [Fact]
    public void MarksItselfAsAnOverlay()
    {
        GivenRoster();

        var cut = RenderDialog();

        Assert.NotNull(cut.Find("[data-overlay]"));
    }
}
