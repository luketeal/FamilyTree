using Bunit;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;
using FamilyTree.UI.Pages.People;
using Moq;

namespace FamilyTree.UI.Tests.Pages;

/// <summary>
/// The biological sections of a profile: US-008, US-012, US-051, US-041.
/// </summary>
public class PersonProfileRelationsTests : ShellTestContext
{
    private readonly List<Person> _people = [];
    private readonly List<BiologicalParentChild> _links = [];

    private Person Someone(string first, string last = "Whitfield", int? birthYear = null)
    {
        var person = new Person(first, last, Gender.Unknown);
        if (birthYear is int year)
        {
            person.UpdateDates(PartialDate.FromYear(year), null, null, null);
        }

        _people.Add(person);
        return person;
    }

    private Person Phantom()
    {
        var phantom = Person.CreatePhantom();
        _people.Add(phantom);
        return phantom;
    }

    private void Link(Person parent, Person child) =>
        _links.Add(new BiologicalParentChild(parent.Id, child.Id));

    /// <summary>
    /// Wires the mocks from the lists above, so a test reads as a family rather
    /// than as five Setup calls.
    /// </summary>
    private IRenderedComponent<PersonProfilePage> RenderProfile(Person subject)
    {
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_people);
        People.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _people.FirstOrDefault(p => p.Id == id));
        People.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                _people.Where(p => ids.Contains(p.Id)).ToList());
        People.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _people.Any(p => p.Id == id));

        Biological.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_links);
        Biological.Setup(r => r.GetParentLinksForChildAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _links.Where(l => l.ChildId == id).ToList());
        Biological.Setup(r => r.GetChildLinksForParentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _links.Where(l => l.ParentId == id).ToList());
        Biological.Setup(r => r.GetParentLinksForChildrenAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                _links.Where(l => ids.Contains(l.ChildId)).ToList());
        Biological.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid parentId, Guid childId, CancellationToken _) =>
                _links.FirstOrDefault(l => l.ParentId == parentId && l.ChildId == childId));

        return Render<PersonProfilePage>(p => p.Add(c => c.Id, subject.Id));
    }

    /// <summary>Susan and Thomas are full siblings; Daniel shares Arthur only.</summary>
    private (Person Susan, Person Thomas, Person Daniel, Person Arthur, Person Margaret) SeedFamily()
    {
        var arthur = Someone("Arthur", birthYear: 1918);
        var margaret = Someone("Margaret", birthYear: 1921);
        var susan = Someone("Susan", birthYear: 1948);
        var thomas = Someone("Thomas", birthYear: 1951);
        var daniel = Someone("Daniel", birthYear: 1963);

        Link(arthur, susan);
        Link(margaret, susan);
        Link(arthur, thomas);
        Link(margaret, thomas);
        Link(arthur, daniel);

        return (susan, thomas, daniel, arthur, margaret);
    }

    // ---- Parents (US-008) ----

    [Fact]
    public void ListsTheBiologicalParents()
    {
        var (susan, _, _, arthur, margaret) = SeedFamily();

        var cut = RenderProfile(susan);

        Assert.NotNull(cut.Find($"[data-testid='parent-{arthur.Id}']"));
        Assert.NotNull(cut.Find($"[data-testid='parent-{margaret.Id}']"));
    }

    [Fact]
    public void LabelsEachParentAsBiological()
    {
        var (susan, _, _, _, _) = SeedFamily();

        var cut = RenderProfile(susan);

        Assert.Contains("(Biological)",
            cut.Find("[data-testid='parents-list']").TextContent);
    }

    [Fact]
    public void ShowsEachParentsLifeSpanAndALinkToTheirProfile()
    {
        var (susan, _, _, arthur, _) = SeedFamily();

        var cut = RenderProfile(susan);

        var chip = cut.Find($"[data-testid='parent-chip-{arthur.Id}']");
        Assert.Equal($"people/{arthur.Id}", chip.GetAttribute("href"));
        Assert.Contains("1918–", chip.TextContent);
    }

    // US-041: not knowing an ancestor is the normal state of a genealogy record,
    // so an empty slot is a prompt rather than an error.
    [Fact]
    public void ShowsAnUnknownSlotForEachMissingParent()
    {
        var loner = Someone("Solo");

        var cut = RenderProfile(loner);

        Assert.Equal(2, cut.FindAll("[data-testid='parent-unknown-slot']").Count);
    }

    [Fact]
    public void ShowsOneUnknownSlotWhenOneParentIsRecorded()
    {
        var child = Someone("Susan");
        var parent = Someone("Arthur");
        Link(parent, child);

        var cut = RenderProfile(child);

        Assert.Single(cut.FindAll("[data-testid='parent-unknown-slot']"));
    }

    [Fact]
    public void HidesTheAddParentButtonWhenBothSlotsAreFull()
    {
        var (susan, _, _, _, _) = SeedFamily();

        var cut = RenderProfile(susan);

        Assert.Empty(cut.FindAll("[data-testid='add-biological-parent']"));
    }

    // A phantom is a stored record standing for an ancestor known to exist,
    // which is not the same as an empty slot — so it renders as a chip that can
    // be removed rather than as an Add prompt.
    [Fact]
    public void ShowsARecordedPhantomAsAnUnknownParentChip()
    {
        var margaret = Someone("Margaret");
        var phantom = Phantom();
        Link(phantom, margaret);

        var cut = RenderProfile(margaret);

        Assert.NotNull(cut.Find($"[data-testid='parent-chip-{phantom.Id}']"));
        Assert.Single(cut.FindAll("[data-testid='parent-unknown-slot']"));
    }

    // The two rows above are both a dashed "Unknown" chip, so the label is the
    // only thing saying which is a record and which is a gap. Asserting the
    // recorded row alone would pass on a label applied to both, so the empty
    // slot is checked in the same test.
    [Fact]
    public void MarksARecordedPhantomParentAsUnidentifiedAndLeavesTheEmptySlotPlain()
    {
        var margaret = Someone("Margaret");
        var phantom = Phantom();
        Link(phantom, margaret);

        var cut = RenderProfile(margaret);

        var recorded = cut.Find($"[data-testid='parent-{phantom.Id}']").TextContent;
        var empty = cut.Find("[data-testid='parent-unknown-slot']").TextContent;

        Assert.Contains("unidentified", recorded);
        Assert.DoesNotContain("unidentified", empty);
    }

    // ---- Children (US-012) ----

    [Fact]
    public void ListsBiologicalChildrenInBirthOrder()
    {
        var arthur = Someone("Arthur");
        var younger = Someone("Younger", birthYear: 1975);
        var older = Someone("Older", birthYear: 1948);
        Link(arthur, younger);
        Link(arthur, older);

        var cut = RenderProfile(arthur);

        var rows = cut.FindAll("[data-testid='children-list'] > li")
            .Select(e => e.GetAttribute("data-testid"))
            .ToList();

        Assert.Equal([$"child-{older.Id}", $"child-{younger.Id}"], rows);
    }

    // The story asks for this button always: building the tree downwards starts
    // from somebody with no children recorded yet.
    [Fact]
    public void OffersToAddAChildEvenWhenThereAreNone()
    {
        var loner = Someone("Solo");

        var cut = RenderProfile(loner);

        Assert.NotNull(cut.Find("[data-testid='add-biological-child']"));
        Assert.NotNull(cut.Find("[data-testid='children-empty']"));
    }

    // ---- Siblings (US-051, US-037) ----

    [Fact]
    public void LabelsAFullSibling()
    {
        var (susan, thomas, _, _, _) = SeedFamily();

        var cut = RenderProfile(susan);

        Assert.Equal("(Full)",
            cut.Find($"[data-testid='sibling-kind-{thomas.Id}']").TextContent.Trim());
    }

    [Fact]
    public void LabelsAHalfSibling()
    {
        var (susan, _, daniel, _, _) = SeedFamily();

        var cut = RenderProfile(susan);

        Assert.Equal("(Half)",
            cut.Find($"[data-testid='sibling-kind-{daniel.Id}']").TextContent.Trim());
    }

    [Fact]
    public void NamesTheParentAHalfSiblingIsSharedThrough()
    {
        var (susan, _, daniel, _, _) = SeedFamily();

        var cut = RenderProfile(susan);

        Assert.Contains("Arthur Whitfield",
            cut.Find($"[data-testid='sibling-shared-{daniel.Id}']").TextContent);
    }

    [Fact]
    public void SaysWhenThereAreNoSiblings()
    {
        var loner = Someone("Solo");

        var cut = RenderProfile(loner);

        Assert.Equal("No known siblings",
            cut.Find("[data-testid='siblings-empty']").TextContent.Trim());
    }

    // ---- Removing (US-010, US-013) ----

    [Fact]
    public void AsksBeforeRemovingAParentLink()
    {
        var (susan, _, _, arthur, _) = SeedFamily();
        var cut = RenderProfile(susan);

        cut.Find($"[data-testid='remove-parent-{arthur.Id}']").Click();

        Assert.NotNull(cut.Find("[data-testid='remove-link-modal']"));
        Biological.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The risk is somebody reading "Remove" as "Delete". The confirmation has to
    // say which it is before they can act on it.
    [Fact]
    public void SaysThatNeitherPersonIsDeleted()
    {
        var (susan, _, _, arthur, _) = SeedFamily();
        var cut = RenderProfile(susan);

        cut.Find($"[data-testid='remove-parent-{arthur.Id}']").Click();

        var body = cut.Find("[data-testid='remove-link-modal-body']").TextContent;
        Assert.Contains("the relationship only", body);
        Assert.Contains("Arthur Whitfield", body);
        Assert.Contains("Susan Whitfield", body);
    }

    [Fact]
    public void RemovesTheParentLinkOnceConfirmed()
    {
        var (susan, _, _, arthur, _) = SeedFamily();
        var link = _links.Single(l => l.ParentId == arthur.Id && l.ChildId == susan.Id);
        var cut = RenderProfile(susan);

        cut.Find($"[data-testid='remove-parent-{arthur.Id}']").Click();
        cut.Find("[data-testid='confirm-remove-link']").Click();

        Biological.Verify(r => r.DeleteAsync(link.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // One record is both directions, so removing a child from a parent's profile
    // must delete the same record the child's own profile would have deleted.
    [Fact]
    public void RemovingAChildDeletesTheSameRecordAsRemovingTheParent()
    {
        var (susan, _, _, arthur, _) = SeedFamily();
        var link = _links.Single(l => l.ParentId == arthur.Id && l.ChildId == susan.Id);
        var cut = RenderProfile(arthur);

        cut.Find($"[data-testid='remove-child-{susan.Id}']").Click();
        cut.Find("[data-testid='confirm-remove-link']").Click();

        Biological.Verify(r => r.DeleteAsync(link.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void CancellingLeavesTheLinkAlone()
    {
        var (susan, _, _, arthur, _) = SeedFamily();
        var cut = RenderProfile(susan);

        cut.Find($"[data-testid='remove-parent-{arthur.Id}']").Click();
        cut.Find("[data-testid='cancel-remove-link']").Click();

        Assert.Empty(cut.FindAll("[data-testid='remove-link-modal']"));
        Biological.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- The dialog's exclusions ----

    // The service refuses these anyway. Excluding them stops the list offering
    // choices whose only outcome is an error.
    [Fact]
    public void DoesNotOfferAnExistingParentWhenAddingAnother()
    {
        var child = Someone("Susan");
        var parent = Someone("Arthur");
        var stranger = Someone("Vera");
        Link(parent, child);
        var cut = RenderProfile(child);

        cut.Find("[data-testid='add-biological-parent']").Click();

        Assert.Empty(cut.FindAll($"[data-testid='relationship-dialog-search-option-{parent.Id}']"));
        Assert.NotNull(cut.Find($"[data-testid='relationship-dialog-search-option-{stranger.Id}']"));
    }

    [Fact]
    public void DoesNotOfferTheSubjectThemselves()
    {
        var child = Someone("Susan");
        Someone("Vera");
        var cut = RenderProfile(child);

        cut.Find("[data-testid='add-biological-parent']").Click();

        Assert.Empty(cut.FindAll($"[data-testid='relationship-dialog-search-option-{child.Id}']"));
    }

    // ---- Replacing (US-009) ----

    [Fact]
    public void OpensTheWizardNamingTheParentBeingReplaced()
    {
        var child = Someone("Susan");
        var wrong = Someone("Vera");
        Someone("Margaret");
        Link(wrong, child);
        var cut = RenderProfile(child);

        cut.Find($"[data-testid='replace-parent-{wrong.Id}']").Click();

        Assert.Contains("Replace a parent",
            cut.Find("[data-testid='relationship-dialog']").TextContent);
    }
}
