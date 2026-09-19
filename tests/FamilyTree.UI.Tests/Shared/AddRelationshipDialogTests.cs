using Bunit;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;
using FamilyTree.UI.Shared;
using Moq;

namespace FamilyTree.UI.Tests.Shared;

using FamilyTree.Application.Common;

using Kind = AddRelationshipDialog.RelationshipKind;

// The entity name says which table it lives in; in a test about a dialog, what
// matters is that it is the adoptive link.
using AdoptiveLink = FamilyTree.Domain.Entities.AdoptiveParentChild;

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
        AddRelationshipDialog.AdoptiveEdit? editing = null,
        Action? onSaved = null) =>
        Render<AddRelationshipDialog>(p =>
        {
            p.Add(c => c.Visible, true);
            p.Add(c => c.SubjectId, _subject.Id);
            p.Add(c => c.SubjectName, "Susan Hartley");
            p.Add(c => c.Kinds, kinds ?? [Kind.BiologicalParent, Kind.BiologicalChild]);
            p.Add(c => c.ReplacingParentId, replacing);
            p.Add(c => c.ReplacingParentName, replacingName);
            p.Add(c => c.Editing, editing);
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

    // ---- The adoptive kinds (US-014, US-018) ----

    [Fact]
    public void OffersTheAdoptiveKinds()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent, Kind.AdoptiveChild]);

        Assert.NotNull(cut.Find("[data-testid='relationship-dialog-kind-adoptiveparent']"));
        Assert.NotNull(cut.Find("[data-testid='relationship-dialog-kind-adoptivechild']"));
    }

    [Fact]
    public void AsksWhoTheAdoptiveParentIs()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent]);

        Assert.Contains("Who is the adoptive parent?", cut.Markup);
    }

    [Fact]
    public void AsksWhoTheAdoptedChildIs()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.AdoptiveChild]);

        Assert.Contains("Who is the adopted child?", cut.Markup);
    }

    [Fact]
    public void SummarisesAnAdoptiveParent()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent]);
        ChooseCandidate(cut);

        Assert.Contains(
            "Arthur Whitfield will be recorded as an adoptive parent of Susan Hartley.",
            cut.Find("[data-testid='relationship-dialog-summary']").TextContent);
    }

    [Fact]
    public void SummarisesAnAdoptiveChild()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.AdoptiveChild]);
        ChooseCandidate(cut);

        Assert.Contains(
            "Arthur Whitfield will be recorded as an adoptive child of Susan Hartley.",
            cut.Find("[data-testid='relationship-dialog-summary']").TextContent);
    }

    // ---- The adoption date (US-014, US-015) ----

    [Fact]
    public void OffersAnAdoptionDateOnTheAdoptiveKinds()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent]);
        ChooseCandidate(cut);

        Assert.NotNull(cut.Find("[data-testid='relationship-dialog-adoption-date']"));
    }

    // A biological link has no date to carry, so a box for one would collect a
    // value with nowhere to go.
    [Fact]
    public void DoesNotOfferAnAdoptionDateOnTheBiologicalKinds()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);
        ChooseCandidate(cut);

        Assert.Empty(cut.FindAll("[data-testid='relationship-dialog-adoption-date']"));
    }

    // Empty means "Date unknown" rather than "not filled in yet": the date an
    // adoption was finalised is frequently not in the record at all.
    [Fact]
    public void SavesAnAdoptiveLinkWithNoDate()
    {
        GivenRoster();
        AdoptiveLink? saved = null;
        Adoptive.Setup(r => r.AddAsync(It.IsAny<AdoptiveLink>(), It.IsAny<CancellationToken>()))
            .Callback((AdoptiveLink l, CancellationToken _) => saved = l)
            .Returns(Task.CompletedTask);

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Assert.NotNull(saved);
        Assert.Null(saved!.AdoptionDate);
    }

    [Fact]
    public void CarriesTheTypedAdoptionDateThrough()
    {
        GivenRoster();
        AdoptiveLink? saved = null;
        Adoptive.Setup(r => r.AddAsync(It.IsAny<AdoptiveLink>(), It.IsAny<CancellationToken>()))
            .Callback((AdoptiveLink l, CancellationToken _) => saved = l)
            .Returns(Task.CompletedTask);

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-adoption-date']").Input("1977");
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Assert.Equal(1977, saved!.AdoptionDate!.Year);
    }

    // The control already says what is wrong with the date; a save that bounced
    // off it would report the same problem a second time somewhere else.
    [Fact]
    public void RefusesToSaveWhileTheAdoptionDateIsUnparseable()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-adoption-date']").Input("not a year");

        Assert.True(cut.Find("[data-testid='relationship-dialog-save']").HasAttribute("disabled"));
    }

    // ---- Editing an adoptive link (US-016) ----

    private AddRelationshipDialog.AdoptiveEdit EditOf(Person parent, int? adoptionYear = null) =>
        new(PersonSummaryDto.From(parent),
            adoptionYear is int year ? PartialDate.FromYear(year) : null,
            RelationshipCertainty.Confirmed);

    // Opening on the recorded person is what lets US-016 offer one control for
    // both corrections: leaving them alone and changing only the date has to be
    // a legitimate outcome.
    [Fact]
    public void OpensAnEditOnTheDetailsOfTheRecordedLink()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent], editing: EditOf(_candidate, 1977));

        Assert.Contains("Edit an adoptive parent of Susan Hartley", cut.Markup);

        // Straight to the details step: the person is already chosen, which is
        // the whole difference between this mode and adding a link.
        cut.Find("[data-testid='relationship-dialog-next']").Click();

        Assert.Equal("1977",
            cut.Find("[data-testid='relationship-dialog-adoption-date']").GetAttribute("value"));
    }

    [Fact]
    public void SaysNothingIsBeingReplacedWhenThePersonIsUnchanged()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent], editing: EditOf(_candidate, 1977));
        cut.Find("[data-testid='relationship-dialog-next']").Click();

        Assert.Contains("stays an adoptive parent of Susan Hartley",
            cut.Find("[data-testid='relationship-dialog-summary']").TextContent);
    }

    // Editing a date must not delete the link and write a new one with a new id.
    [Fact]
    public void EditingTheDateUpdatesTheLinkInPlace()
    {
        GivenRoster();
        var link = new AdoptiveLink(_candidate.Id, _subject.Id, PartialDate.FromYear(1970));
        Adoptive.Setup(r => r.GetAsync(_candidate.Id, _subject.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(link);
        AdoptiveLink? updated = null;
        Adoptive.Setup(r => r.UpdateAsync(It.IsAny<AdoptiveLink>(), It.IsAny<CancellationToken>()))
            .Callback((AdoptiveLink l, CancellationToken _) => updated = l)
            .Returns(Task.CompletedTask);

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent], editing: EditOf(_candidate, 1970));
        cut.Find("[data-testid='relationship-dialog-next']").Click();
        cut.Find("[data-testid='relationship-dialog-adoption-date']").Input("1977");
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Assert.NotNull(updated);
        Assert.Equal(link.Id, updated!.Id);
        Assert.Equal(1977, updated.AdoptionDate!.Year);
        Adoptive.Verify(
            r => r.ReplaceParentAsync(It.IsAny<Guid>(), It.IsAny<AdoptiveLink>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // The other half of US-016: changing who the adoptive parent is ends one
    // relationship and starts another, through the atomic path.
    [Fact]
    public void ChangingThePersonGoesThroughTheAtomicReplace()
    {
        var other = new Person("Miriam", "Okonjo", Gender.Female);
        GivenRoster(other);
        var link = new AdoptiveLink(_candidate.Id, _subject.Id);
        Adoptive.Setup(r => r.GetAsync(_candidate.Id, _subject.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(link);

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent], editing: EditOf(_candidate));
        cut.Find($"[data-testid='relationship-dialog-search-option-{other.Id}']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Adoptive.Verify(
            r => r.ReplaceParentAsync(link.Id, It.IsAny<AdoptiveLink>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void LabelsTheEditButtonAsSavingChanges()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent], editing: EditOf(_candidate));
        cut.Find("[data-testid='relationship-dialog-next']").Click();

        Assert.Contains("Save changes",
            cut.Find("[data-testid='relationship-dialog-save']").TextContent);
    }

    // ---- The adoption date across step navigation ----
    //
    // PartialDateInput is destroyed when the wizard leaves the Details step and
    // rebuilt on return, so whatever the dialog remembers and whatever the
    // control displays have to be the same thing. They were not: the dialog kept
    // the parsed value while the control re-seeded from the seed it was first
    // given, and the two drifted apart in three separate ways.

    // The serious one. A save that writes a value the user cannot see is the
    // silent-data-loss class this project treats as a defect, not an edge case.
    [Fact]
    public void KeepsTheTypedAdoptionDateAcrossAStepChange()
    {
        GivenRoster();
        AdoptiveLink? saved = null;
        Adoptive.Setup(r => r.AddAsync(It.IsAny<AdoptiveLink>(), It.IsAny<CancellationToken>()))
            .Callback((AdoptiveLink l, CancellationToken _) => saved = l)
            .Returns(Task.CompletedTask);

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-adoption-date']").Input("1977");
        cut.Find("[data-testid='relationship-dialog-back']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();

        // Displayed and stored agree, which is the actual requirement — blanking
        // the field and saving null would agree too, and would throw away what
        // the user typed.
        Assert.Equal("1977",
            cut.Find("[data-testid='relationship-dialog-adoption-date']").GetAttribute("value"));

        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Assert.Equal(1977, saved!.AdoptionDate!.Year);
    }

    // Unparseable text survives too. PartialDateText exists precisely to carry a
    // value the domain type cannot express, so re-seeding from the parsed value
    // would discard exactly what it is for.
    [Fact]
    public void KeepsUnparseableAdoptionDateTextAcrossAStepChange()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-adoption-date']").Input("not a year");
        cut.Find("[data-testid='relationship-dialog-back']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();

        Assert.Equal("not a year",
            cut.Find("[data-testid='relationship-dialog-adoption-date']").GetAttribute("value"));
    }

    // Save stays disabled on an invalid date, but the reason has to be on screen.
    // A dead button next to a blank field is unescapable except by cancelling.
    [Fact]
    public void ShowsWhySaveIsDisabledAfterReturningToAnInvalidDate()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-adoption-date']").Input("not a year");
        cut.Find("[data-testid='relationship-dialog-back']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();

        Assert.True(cut.Find("[data-testid='relationship-dialog-save']").HasAttribute("disabled"));
        Assert.NotEmpty(cut.FindAll("[data-testid='error-relationship-dialog-adoption-date']"));
    }

    // Clearing a date is a legitimate correction, and a step change must not
    // quietly undo it by re-seeding from what was originally recorded.
    [Fact]
    public void KeepsAClearedAdoptionDateClearedAcrossAStepChange()
    {
        GivenRoster();
        var link = new AdoptiveLink(_candidate.Id, _subject.Id, PartialDate.FromYear(1990));
        Adoptive.Setup(r => r.GetAsync(_candidate.Id, _subject.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(link);
        AdoptiveLink? updated = null;
        Adoptive.Setup(r => r.UpdateAsync(It.IsAny<AdoptiveLink>(), It.IsAny<CancellationToken>()))
            .Callback((AdoptiveLink l, CancellationToken _) => updated = l)
            .Returns(Task.CompletedTask);

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent], editing: EditOf(_candidate, 1990));
        cut.Find("[data-testid='relationship-dialog-next']").Click();
        cut.Find("[data-testid='relationship-dialog-adoption-date']").Input(string.Empty);
        cut.Find("[data-testid='relationship-dialog-back']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();

        Assert.Equal(string.Empty,
            cut.Find("[data-testid='relationship-dialog-adoption-date']").GetAttribute("value"));

        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Assert.NotNull(updated);
        Assert.Null(updated!.AdoptionDate);
    }

    // A biological link has no date field, so a date left invalid on an adoptive
    // kind must not disable its save. Not reachable from the profile today, which
    // passes one kind per entry point — but this component offers the kind step
    // to anyone who asks for more than one.
    [Fact]
    public void AnInvalidAdoptionDateDoesNotBlockSavingABiologicalLink()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.AdoptiveParent, Kind.BiologicalParent]);
        cut.Find("[data-testid='relationship-dialog-kind-adoptiveparent']").Click();
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-adoption-date']").Input("not a year");

        cut.Find("[data-testid='relationship-dialog-back']").Click();
        cut.Find("[data-testid='relationship-dialog-back']").Click();
        cut.Find("[data-testid='relationship-dialog-kind-biologicalparent']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();

        Assert.Empty(cut.FindAll("[data-testid='relationship-dialog-adoption-date']"));
        Assert.False(cut.Find("[data-testid='relationship-dialog-save']").HasAttribute("disabled"));
    }
}
