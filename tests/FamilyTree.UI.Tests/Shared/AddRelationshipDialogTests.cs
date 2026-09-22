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
        MarriageDto? editingMarriage = null,
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
            p.Add(c => c.EditingMarriage, editingMarriage);
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

    // ---- The spouse kind (US-021, US-023, US-025 to US-027, US-044) ----

    private static MarriageDto MarriageOf(
        Person spouse, Guid marriageId, int startYear = 1946, string? place = null,
        int? endYear = null, MarriageEndReason? reason = null) =>
        new(marriageId,
            PersonSummaryDto.From(spouse),
            PartialDate.FromYear(startYear),
            place,
            endYear is int year ? PartialDate.FromYear(year) : null,
            reason,
            RelationshipCertainty.Confirmed);

    [Fact]
    public void OffersTheSpouseKind()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.BiologicalParent, Kind.Spouse]);

        Assert.NotNull(cut.Find("[data-testid='relationship-dialog-kind-spouse']"));
    }

    // US-044: the question asks for a spouse or partner rather than a husband or a
    // wife, so nothing in the flow depends on either person's gender.
    [Fact]
    public void AsksWhoTheSpouseOrPartnerIs()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.Spouse]);

        Assert.Contains("spouse or partner", cut.Markup);
    }

    [Fact]
    public void OffersTheMarriageFieldsOnTheSpouseKind()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        ChooseCandidate(cut);

        Assert.NotNull(cut.Find("[data-testid='relationship-dialog-start-date-field']"));
        Assert.NotNull(cut.Find("[data-testid='relationship-dialog-place']"));
        Assert.NotNull(cut.Find("[data-testid='relationship-dialog-end-reason']"));
        Assert.NotNull(cut.Find("[data-testid='relationship-dialog-end-date-field']"));
    }

    [Fact]
    public void DoesNotOfferTheMarriageFieldsOnTheBiologicalKinds()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.BiologicalParent]);
        ChooseCandidate(cut);

        Assert.Empty(cut.FindAll("[data-testid='relationship-dialog-start-date-field']"));
        Assert.Empty(cut.FindAll("[data-testid='relationship-dialog-end-reason']"));
    }

    [Fact]
    public void SavesAMarriageWithItsDateAndPlace()
    {
        GivenRoster();
        Marriage? saved = null;
        Marriages.Setup(r => r.AddAsync(It.IsAny<Marriage>(), It.IsAny<CancellationToken>()))
            .Callback((Marriage m, CancellationToken _) => saved = m)
            .Returns(Task.CompletedTask);

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1946");
        cut.Find("[data-testid='relationship-dialog-place']").Input("Leeds");
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Assert.NotNull(saved);
        Assert.Equal(1946, saved!.StartDate.Year);
        Assert.Equal("Leeds", saved.StartPlace);
        Assert.Equal(_candidate.Id, saved.Spouse2Id);
    }

    // US-021 makes the start date required, which is the one required date in the
    // app. Save stays disabled rather than failing on submit.
    [Fact]
    public void RefusesToSaveAMarriageWithNoStartDate()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        ChooseCandidate(cut);

        Assert.True(cut.Find("[data-testid='relationship-dialog-save']").HasAttribute("disabled"));
    }

    [Fact]
    public void AllowsSavingOnceTheStartDateIsTyped()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1946");

        Assert.False(cut.Find("[data-testid='relationship-dialog-save']").HasAttribute("disabled"));
    }

    [Fact]
    public void RefusesToSaveWhileTheEndDateIsUnparseable()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1946");
        cut.Find("[data-testid='relationship-dialog-end-date']").Input("not a year");

        Assert.True(cut.Find("[data-testid='relationship-dialog-save']").HasAttribute("disabled"));
    }

    // A marriage start date left invalid must not disable Save on a biological
    // link, whose form shows no date field to correct it in.
    [Fact]
    public void AMissingMarriageDateDoesNotBlockSavingABiologicalLink()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.Spouse, Kind.BiologicalParent]);
        cut.Find("[data-testid='relationship-dialog-kind-biologicalparent']").Click();
        ChooseCandidate(cut);

        Assert.False(cut.Find("[data-testid='relationship-dialog-save']").HasAttribute("disabled"));
    }

    // US-025: recording a divorce is recording an end reason and a date.
    [Fact]
    public void RecordsADivorceWithItsDate()
    {
        GivenRoster();
        Marriage? saved = null;
        Marriages.Setup(r => r.AddAsync(It.IsAny<Marriage>(), It.IsAny<CancellationToken>()))
            .Callback((Marriage m, CancellationToken _) => saved = m)
            .Returns(Task.CompletedTask);

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1946");
        cut.Find("[data-testid='relationship-dialog-end-reason']").Change("Divorce");
        cut.Find("[data-testid='relationship-dialog-end-date']").Input("1960");
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Assert.Equal(MarriageEndReason.Divorce, saved!.EndReason);
        Assert.Equal(1960, saved.EndDate!.Year);
    }

    // US-027: annulment is a selectable reason, distinct from divorce.
    [Fact]
    public void RecordsAnAnnulment()
    {
        GivenRoster();
        Marriage? saved = null;
        Marriages.Setup(r => r.AddAsync(It.IsAny<Marriage>(), It.IsAny<CancellationToken>()))
            .Callback((Marriage m, CancellationToken _) => saved = m)
            .Returns(Task.CompletedTask);

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1946");
        cut.Find("[data-testid='relationship-dialog-end-reason']").Change("Annulment");
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Assert.Equal(MarriageEndReason.Annulment, saved!.EndReason);
    }

    // The default is a current marriage, so recording one needs nothing said about
    // how it ended.
    [Fact]
    public void RecordsAnOngoingMarriageByDefault()
    {
        GivenRoster();
        Marriage? saved = null;
        Marriages.Setup(r => r.AddAsync(It.IsAny<Marriage>(), It.IsAny<CancellationToken>()))
            .Callback((Marriage m, CancellationToken _) => saved = m)
            .Returns(Task.CompletedTask);

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1976");
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Assert.True(saved!.IsOngoing);
    }

    // ---- The end-date offer (US-026) ----

    [Fact]
    public void OffersTheEndDateFromASpousesDeathWhenWidowhoodIsChosen()
    {
        var deceased = new Person("Margaret", "Whitfield", Gender.Female);
        deceased.UpdateDates(PartialDate.FromYear(1921), null, PartialDate.FromYear(1973), null);
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_subject, deceased]);

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        cut.Find($"[data-testid='relationship-dialog-search-option-{deceased.Id}']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1946");
        cut.Find("[data-testid='relationship-dialog-end-reason']").Change("DeathOfSpouse");

        var offer = cut.Find("[data-testid='relationship-dialog-use-death-date']");
        Assert.Contains("1973", offer.TextContent);
        Assert.Contains("Margaret Whitfield", offer.TextContent);
    }

    // An offer rather than an auto-fill: nothing is filled in until it is taken.
    [Fact]
    public void DoesNotFillTheEndDateUntilTheOfferIsTaken()
    {
        var deceased = new Person("Margaret", "Whitfield", Gender.Female);
        deceased.UpdateDates(PartialDate.FromYear(1921), null, PartialDate.FromYear(1973), null);
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_subject, deceased]);
        Marriage? saved = null;
        Marriages.Setup(r => r.AddAsync(It.IsAny<Marriage>(), It.IsAny<CancellationToken>()))
            .Callback((Marriage m, CancellationToken _) => saved = m)
            .Returns(Task.CompletedTask);

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        cut.Find($"[data-testid='relationship-dialog-search-option-{deceased.Id}']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1946");
        cut.Find("[data-testid='relationship-dialog-end-reason']").Change("DeathOfSpouse");
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Assert.Null(saved!.EndDate);
    }

    // The assertion that was missing when this shipped broken: the offer set the
    // dialog's own value and the box stayed empty, so the year was saved without
    // ever being displayed. PartialDateInput reads its seed once and then owns its
    // contents, which is right for a control that must not have half-typed input
    // yanked out from under it — and means a programmatic fill has to rebuild it.
    [Fact]
    public void TakingTheOfferShowsTheDateInTheBox()
    {
        var deceased = new Person("Margaret", "Whitfield", Gender.Female);
        deceased.UpdateDates(PartialDate.FromYear(1921), null, PartialDate.FromYear(1973), null);
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_subject, deceased]);

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        cut.Find($"[data-testid='relationship-dialog-search-option-{deceased.Id}']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1946");
        cut.Find("[data-testid='relationship-dialog-end-reason']").Change("DeathOfSpouse");
        cut.Find("[data-testid='relationship-dialog-use-death-date']").Click();

        Assert.Equal(
            "1973",
            cut.Find("[data-testid='relationship-dialog-end-date']").GetAttribute("value"));
    }

    // Once taken, the offer stops being offered: it exists to fill an empty box,
    // and re-offering over a value it just wrote would invite overwriting a date
    // the user then corrected.
    [Fact]
    public void TheOfferDisappearsOnceTaken()
    {
        var deceased = new Person("Margaret", "Whitfield", Gender.Female);
        deceased.UpdateDates(PartialDate.FromYear(1921), null, PartialDate.FromYear(1973), null);
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_subject, deceased]);

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        cut.Find($"[data-testid='relationship-dialog-search-option-{deceased.Id}']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1946");
        cut.Find("[data-testid='relationship-dialog-end-reason']").Change("DeathOfSpouse");
        cut.Find("[data-testid='relationship-dialog-use-death-date']").Click();

        Assert.Empty(cut.FindAll("[data-testid='relationship-dialog-use-death-date']"));
    }

    [Fact]
    public void TakingTheOfferFillsTheEndDate()
    {
        var deceased = new Person("Margaret", "Whitfield", Gender.Female);
        deceased.UpdateDates(PartialDate.FromYear(1921), null, PartialDate.FromYear(1973), null);
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_subject, deceased]);
        Marriage? saved = null;
        Marriages.Setup(r => r.AddAsync(It.IsAny<Marriage>(), It.IsAny<CancellationToken>()))
            .Callback((Marriage m, CancellationToken _) => saved = m)
            .Returns(Task.CompletedTask);

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        cut.Find($"[data-testid='relationship-dialog-search-option-{deceased.Id}']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1946");
        cut.Find("[data-testid='relationship-dialog-end-reason']").Change("DeathOfSpouse");
        cut.Find("[data-testid='relationship-dialog-use-death-date']").Click();
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Assert.Equal(1973, saved!.EndDate!.Year);
    }

    // The offer belongs to whoever was chosen when the reason was picked. Stepping
    // back and changing the spouse left it on screen showing the previous person's
    // death year under the new person's form — a date presented as theirs that was
    // somebody else's.
    [Fact]
    public void TheOfferDoesNotSurviveAChangeOfSpouse()
    {
        var deceased = new Person("Margaret", "Whitfield", Gender.Female);
        deceased.UpdateDates(PartialDate.FromYear(1921), null, PartialDate.FromYear(1973), null);
        var living = new Person("Vera", "Nash", Gender.Female);
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_subject, deceased, living]);

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        cut.Find($"[data-testid='relationship-dialog-search-option-{deceased.Id}']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1946");
        cut.Find("[data-testid='relationship-dialog-end-reason']").Change("DeathOfSpouse");
        Assert.NotNull(cut.Find("[data-testid='relationship-dialog-use-death-date']"));

        cut.Find("[data-testid='relationship-dialog-back']").Click();
        cut.Find($"[data-testid='relationship-dialog-search-option-{living.Id}']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();

        Assert.Empty(cut.FindAll("[data-testid='relationship-dialog-use-death-date']"));
    }

    // Nothing to offer when nobody has a death date recorded, and an offer that
    // proposed nothing would be worse than none.
    [Fact]
    public void MakesNoOfferWhenNeitherSpouseHasADeathDate()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1946");
        cut.Find("[data-testid='relationship-dialog-end-reason']").Change("DeathOfSpouse");

        Assert.Empty(cut.FindAll("[data-testid='relationship-dialog-use-death-date']"));
    }

    // ---- Editing a marriage (US-023, US-025) ----

    [Fact]
    public void OpensAnEditOnTheRecordedMarriage()
    {
        GivenRoster();
        var marriageId = Guid.NewGuid();

        var cut = RenderDialog(
            kinds: [Kind.Spouse],
            editingMarriage: MarriageOf(_candidate, marriageId, 1946, "Leeds"));
        cut.Find("[data-testid='relationship-dialog-next']").Click();

        Assert.Equal("1946", cut.Find("[data-testid='relationship-dialog-start-date']").GetAttribute("value"));
        Assert.Equal("Leeds", cut.Find("[data-testid='relationship-dialog-place']").GetAttribute("value"));
        Assert.Contains("Save changes", cut.Find("[data-testid='relationship-dialog-save']").TextContent);
    }

    // The ordinary case, and the one that matters most: ending a marriage keeps the
    // record's identity, so the stepparent labels resting on it survive.
    [Fact]
    public void EndingAMarriageUpdatesTheRecordInPlace()
    {
        GivenRoster();
        var marriageId = Guid.NewGuid();
        var marriage = new Marriage(_subject.Id, _candidate.Id, PartialDate.FromYear(1946));
        Marriages.Setup(r => r.GetByIdAsync(marriageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(marriage);

        var cut = RenderDialog(
            kinds: [Kind.Spouse],
            editingMarriage: MarriageOf(_candidate, marriageId));
        cut.Find("[data-testid='relationship-dialog-next']").Click();
        cut.Find("[data-testid='relationship-dialog-end-reason']").Change("Divorce");
        cut.Find("[data-testid='relationship-dialog-end-date']").Input("1960");
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Marriages.Verify(
            r => r.UpdateAsync(It.IsAny<Marriage>(), It.IsAny<CancellationToken>()), Times.Once);
        Marriages.Verify(
            r => r.ReplaceSpouseAsync(
                It.IsAny<Guid>(), It.IsAny<Marriage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // US-023's third criterion: changing the spouse goes through the atomic path,
    // because the alternative can leave the marriage gone from both profiles with
    // nothing in its place.
    [Fact]
    public void ChangingTheSpouseGoesThroughTheAtomicReplace()
    {
        var other = new Person("Vera", "Nash", Gender.Female);
        GivenRoster(other);
        var marriageId = Guid.NewGuid();
        var marriage = new Marriage(_subject.Id, _candidate.Id, PartialDate.FromYear(1946));
        Marriages.Setup(r => r.GetByIdAsync(marriageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(marriage);

        var cut = RenderDialog(
            kinds: [Kind.Spouse],
            editingMarriage: MarriageOf(_candidate, marriageId));
        cut.Find($"[data-testid='relationship-dialog-search-option-{other.Id}']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();
        cut.Find("[data-testid='relationship-dialog-save']").Click();

        Marriages.Verify(
            r => r.ReplaceSpouseAsync(
                marriageId, It.IsAny<Marriage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void SaysNothingIsBeingReplacedWhenTheSpouseIsUnchanged()
    {
        GivenRoster();

        var cut = RenderDialog(
            kinds: [Kind.Spouse],
            editingMarriage: MarriageOf(_candidate, Guid.NewGuid()));
        cut.Find("[data-testid='relationship-dialog-next']").Click();

        Assert.Contains(
            "stays married to",
            cut.Find("[data-testid='relationship-dialog-summary']").TextContent);
    }

    // The same seed discipline the adoption date needed: the control is destroyed
    // when the wizard leaves the Details step, so what the dialog remembers and
    // what the boxes show have to be the same thing.
    [Fact]
    public void KeepsTheTypedMarriageDatesAcrossAStepChange()
    {
        GivenRoster();

        var cut = RenderDialog(kinds: [Kind.Spouse]);
        ChooseCandidate(cut);
        cut.Find("[data-testid='relationship-dialog-start-date']").Input("1946");
        cut.Find("[data-testid='relationship-dialog-place']").Input("Leeds");
        cut.Find("[data-testid='relationship-dialog-back']").Click();
        cut.Find("[data-testid='relationship-dialog-next']").Click();

        Assert.Equal("1946", cut.Find("[data-testid='relationship-dialog-start-date']").GetAttribute("value"));
        Assert.Equal("Leeds", cut.Find("[data-testid='relationship-dialog-place']").GetAttribute("value"));
    }
}
