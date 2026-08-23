using FamilyTree.Application.Common;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Application.Tests.Services;

/// <summary>
/// The rules a <see cref="BiologicalParentChild"/> cannot enforce for itself:
/// the two-parent cap, duplicates, cycles, and what counts as a sibling.
/// </summary>
public class BiologicalRelationshipServiceTests
{
    private readonly InMemoryTree _tree = new();

    private BiologicalRelationshipService CreateService() => new(
        _tree.PersonRepository,
        _tree.BiologicalRepository,
        new CircularReferenceChecker(_tree.BiologicalRepository, _tree.AdoptiveRepository));

    private static Person Someone(string first, string last = "Whitfield", int? birthYear = null)
    {
        var person = new Person(first, last, Gender.Unknown);
        if (birthYear is int year)
        {
            person.UpdateDates(PartialDate.FromYear(year), null, null, null);
        }

        return person;
    }

    // ---- Adding a parent (US-007) ----

    [Fact]
    public async Task RecordsABiologicalParent()
    {
        var child = Someone("Susan");
        var parent = Someone("Arthur");
        _tree.With(child, parent);

        var result = await CreateService().AddParentAsync(child.Id, parent.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(_tree.BiologicalLinks);
        Assert.Equal(parent.Id, _tree.BiologicalLinks[0].ParentId);
        Assert.Equal(child.Id, _tree.BiologicalLinks[0].ChildId);
    }

    // One record is both directions, so the reciprocal child entry US-007 asks
    // for is structural rather than a second write that could fall out of step.
    [Fact]
    public async Task TheSameRecordServesAsTheChildLink()
    {
        var child = Someone("Susan");
        var parent = Someone("Arthur");
        _tree.With(child, parent);

        await CreateService().AddParentAsync(child.Id, parent.Id);
        var relations = await CreateService().GetForPersonAsync(parent.Id);

        Assert.Single(relations.Value!.Children);
        Assert.Equal(child.Id, relations.Value!.Children[0].Person.Id);
    }

    [Fact]
    public async Task DefaultsCertaintyToConfirmed()
    {
        var child = Someone("Susan");
        var parent = Someone("Arthur");
        _tree.With(child, parent);

        await CreateService().AddParentAsync(child.Id, parent.Id);

        Assert.Equal(RelationshipCertainty.Confirmed, _tree.BiologicalLinks[0].Certainty);
    }

    // US-055's value is captured now even though PR 13 gives it a visual
    // treatment. A control that collected it and dropped it would be worse than
    // no control.
    [Fact]
    public async Task StoresTheCertaintyItWasGiven()
    {
        var child = Someone("Priya");
        var parent = Someone("Anita");
        _tree.With(child, parent);

        await CreateService().AddParentAsync(child.Id, parent.Id, RelationshipCertainty.Speculative);

        Assert.Equal(RelationshipCertainty.Speculative, _tree.BiologicalLinks[0].Certainty);
    }

    [Fact]
    public async Task RejectsAPersonAsTheirOwnParent()
    {
        var person = Someone("Susan");
        _tree.With(person);

        var result = await CreateService().AddParentAsync(person.Id, person.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("their own biological parent", result.Error);
        Assert.Empty(_tree.BiologicalLinks);
    }

    [Fact]
    public async Task RejectsAChildWhoIsNotInTheTree()
    {
        var parent = Someone("Arthur");
        _tree.With(parent);
        var missing = Guid.NewGuid();

        var result = await CreateService().AddParentAsync(missing, parent.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains(missing.ToString(), result.Error);
    }

    [Fact]
    public async Task RejectsAParentWhoIsNotInTheTree()
    {
        var child = Someone("Susan");
        _tree.With(child);
        var missing = Guid.NewGuid();

        var result = await CreateService().AddParentAsync(child.Id, missing);

        Assert.False(result.IsSuccess);
        Assert.Contains(missing.ToString(), result.Error);
    }

    [Fact]
    public async Task RejectsTheSameParentTwice()
    {
        var child = Someone("Susan");
        var parent = Someone("Arthur");
        _tree.With(child, parent).With(new BiologicalParentChild(parent.Id, child.Id));

        var result = await CreateService().AddParentAsync(child.Id, parent.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already recorded as a biological parent", result.Error);
        Assert.Single(_tree.BiologicalLinks);
    }

    [Fact]
    public async Task RejectsAThirdBiologicalParent()
    {
        var child = Someone("Susan");
        var mother = Someone("Margaret");
        var father = Someone("Arthur");
        var third = Someone("Vera");
        _tree.With(child, mother, father, third)
            .With(new BiologicalParentChild(mother.Id, child.Id))
            .With(new BiologicalParentChild(father.Id, child.Id));

        var result = await CreateService().AddParentAsync(child.Id, third.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already has 2 biological parents", result.Error);
        Assert.Equal(2, _tree.BiologicalLinks.Count);
    }

    // Names them, because "this person already has two parents" leaves somebody
    // hunting for which two before they can decide what to correct.
    [Fact]
    public async Task NamesTheParentsAlreadyRecordedWhenTheCapIsReached()
    {
        var child = Someone("Susan");
        var mother = Someone("Margaret");
        var father = Someone("Arthur");
        var third = Someone("Vera");
        _tree.With(child, mother, father, third)
            .With(new BiologicalParentChild(mother.Id, child.Id))
            .With(new BiologicalParentChild(father.Id, child.Id));

        var result = await CreateService().AddParentAsync(child.Id, third.Id);

        Assert.Contains("Margaret Whitfield", result.Error);
        Assert.Contains("Arthur Whitfield", result.Error);
    }

    // US-040. The message names both people and says which way round the loop
    // runs, because "circular reference" tells nobody what to do about it.
    [Fact]
    public async Task RejectsAParentWhoIsAlreadyADescendant()
    {
        var grandparent = Someone("Arthur");
        var parent = Someone("Susan");
        var child = Someone("Eleanor");
        _tree.With(grandparent, parent, child)
            .With(new BiologicalParentChild(grandparent.Id, parent.Id))
            .With(new BiologicalParentChild(parent.Id, child.Id));

        var result = await CreateService().AddParentAsync(grandparent.Id, child.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("Eleanor Whitfield is already a descendant of Arthur Whitfield", result.Error);
    }

    // The graph is a DAG, not a tree (US-039), so a loop can run up a biological
    // edge and back down an adoptive one. Checking only biology would miss it.
    [Fact]
    public async Task RejectsALoopThatRunsThroughAnAdoptiveEdge()
    {
        var top = Someone("Arthur");
        var middle = Someone("Susan");
        _tree.With(top, middle)
            .With(new AdoptiveParentChild(top.Id, middle.Id, null));

        var result = await CreateService().AddParentAsync(top.Id, middle.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already a descendant", result.Error);
    }

    // A warning, not a refusal: partial and second-hand dates are the normal
    // material of genealogy, and blocking on one would make the app wrong about
    // a real family on the strength of a guessed year.
    [Fact]
    public async Task WarnsWhenTheParentWasBornAfterTheChild()
    {
        var child = Someone("Susan", birthYear: 1948);
        var parent = Someone("Arthur", birthYear: 1970);
        _tree.With(child, parent);

        var result = await CreateService().AddParentAsync(child.Id, parent.Id);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsWarning);
        Assert.Contains("1970", result.Warning);
        Assert.Single(_tree.BiologicalLinks);
    }

    [Fact]
    public async Task DoesNotWarnWhenTheParentIsOlder()
    {
        var child = Someone("Susan", birthYear: 1948);
        var parent = Someone("Arthur", birthYear: 1918);
        _tree.With(child, parent);

        var result = await CreateService().AddParentAsync(child.Id, parent.Id);

        Assert.False(result.IsWarning);
    }

    [Fact]
    public async Task DoesNotWarnWhenEitherBirthDateIsUnknown()
    {
        var child = Someone("Susan");
        var parent = Someone("Arthur", birthYear: 1918);
        _tree.With(child, parent);

        var result = await CreateService().AddParentAsync(child.Id, parent.Id);

        Assert.False(result.IsWarning);
    }

    // ---- Adding a child (US-011) ----

    [Fact]
    public async Task RecordsABiologicalChild()
    {
        var parent = Someone("Arthur");
        var child = Someone("Susan");
        _tree.With(parent, child);

        var result = await CreateService().AddChildAsync(parent.Id, child.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(parent.Id, _tree.BiologicalLinks[0].ParentId);
        Assert.Equal(child.Id, _tree.BiologicalLinks[0].ChildId);
    }

    // The cap belongs to the child, so it applies from this direction too —
    // otherwise the top-down path would be a way round it.
    [Fact]
    public async Task RefusesAChildWhoAlreadyHasTwoParents()
    {
        var parent = Someone("Vera");
        var child = Someone("Susan");
        _tree.With(parent, child, Someone("Margaret"), Someone("Arthur"));
        _tree.With(
            new BiologicalParentChild(_tree.People[2].Id, child.Id),
            new BiologicalParentChild(_tree.People[3].Id, child.Id));

        var result = await CreateService().AddChildAsync(parent.Id, child.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("Replace one of them instead", result.Error);
    }

    [Fact]
    public async Task RefusesAnAncestorAsAChild()
    {
        var grandparent = Someone("Arthur");
        var parent = Someone("Susan");
        var child = Someone("Eleanor");
        _tree.With(grandparent, parent, child)
            .With(new BiologicalParentChild(grandparent.Id, parent.Id))
            .With(new BiologicalParentChild(parent.Id, child.Id));

        var result = await CreateService().AddChildAsync(child.Id, grandparent.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already a descendant", result.Error);
    }

    // ---- Removing (US-010, US-013) ----

    [Fact]
    public async Task RemovesTheLink()
    {
        var parent = Someone("Arthur");
        var child = Someone("Susan");
        _tree.With(parent, child).With(new BiologicalParentChild(parent.Id, child.Id));

        var result = await CreateService().RemoveAsync(parent.Id, child.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(_tree.BiologicalLinks);
    }

    [Fact]
    public async Task RemovingALinkDeletesNeitherPerson()
    {
        var parent = Someone("Arthur");
        var child = Someone("Susan");
        _tree.With(parent, child).With(new BiologicalParentChild(parent.Id, child.Id));

        await CreateService().RemoveAsync(parent.Id, child.Id);

        Assert.Equal(2, _tree.People.Count);
    }

    [Fact]
    public async Task ReportsWhenThereIsNoLinkToRemove()
    {
        var parent = Someone("Arthur");
        var child = Someone("Susan");
        _tree.With(parent, child);

        var result = await CreateService().RemoveAsync(parent.Id, child.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("no biological relationship", result.Error);
    }

    // ---- Replacing (US-009) ----

    [Fact]
    public async Task ReplacesOneParentWithAnother()
    {
        var child = Someone("Susan");
        var wrong = Someone("Vera");
        var right = Someone("Margaret");
        _tree.With(child, wrong, right).With(new BiologicalParentChild(wrong.Id, child.Id));

        var result = await CreateService().ReplaceParentAsync(child.Id, wrong.Id, right.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(_tree.BiologicalLinks);
        Assert.Equal(right.Id, _tree.BiologicalLinks[0].ParentId);
    }

    // The API-seam requirement, and the reason the repository grew a method:
    // delete-then-add can half-apply, and its failure leaves the child with no
    // parent on that side and nothing to say who used to be there.
    [Fact]
    public async Task ReplacingIsOneRepositoryCall()
    {
        var child = Someone("Susan");
        var wrong = Someone("Vera");
        var right = Someone("Margaret");
        _tree.With(child, wrong, right).With(new BiologicalParentChild(wrong.Id, child.Id));

        await CreateService().ReplaceParentAsync(child.Id, wrong.Id, right.Id);

        Assert.Equal(1, _tree.BiologicalReplaceCount);
    }

    [Fact]
    public async Task ReplacingLeavesTheOtherParentAlone()
    {
        var child = Someone("Susan");
        var keeper = Someone("Arthur");
        var wrong = Someone("Vera");
        var right = Someone("Margaret");
        _tree.With(child, keeper, wrong, right)
            .With(new BiologicalParentChild(keeper.Id, child.Id))
            .With(new BiologicalParentChild(wrong.Id, child.Id));

        await CreateService().ReplaceParentAsync(child.Id, wrong.Id, right.Id);

        var parentIds = _tree.BiologicalLinks.Select(l => l.ParentId).ToHashSet();
        Assert.Contains(keeper.Id, parentIds);
        Assert.Contains(right.Id, parentIds);
        Assert.DoesNotContain(wrong.Id, parentIds);
    }

    // The cap must not block a swap that keeps the count the same, or correcting
    // a parent on a child who has two would be impossible.
    [Fact]
    public async Task ReplacingIsAllowedWhenBothParentSlotsAreFull()
    {
        var child = Someone("Susan");
        var keeper = Someone("Arthur");
        var wrong = Someone("Vera");
        var right = Someone("Margaret");
        _tree.With(child, keeper, wrong, right)
            .With(new BiologicalParentChild(keeper.Id, child.Id))
            .With(new BiologicalParentChild(wrong.Id, child.Id));

        var result = await CreateService().ReplaceParentAsync(child.Id, wrong.Id, right.Id);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RefusesToReplaceAParentWithTheOtherRecordedParent()
    {
        var child = Someone("Susan");
        var keeper = Someone("Arthur");
        var wrong = Someone("Vera");
        _tree.With(child, keeper, wrong)
            .With(new BiologicalParentChild(keeper.Id, child.Id))
            .With(new BiologicalParentChild(wrong.Id, child.Id));

        var result = await CreateService().ReplaceParentAsync(child.Id, wrong.Id, keeper.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already recorded as a biological parent", result.Error);
        Assert.Equal(2, _tree.BiologicalLinks.Count);
    }

    // A correction is a fresh judgement, not a rename: it goes through the same
    // cycle check, or US-040 has a hole straight through it.
    [Fact]
    public async Task RefusesAReplacementThatWouldCreateALoop()
    {
        var child = Someone("Eleanor");
        var wrong = Someone("Vera");
        var descendant = Someone("Grandchild");
        _tree.With(child, wrong, descendant)
            .With(new BiologicalParentChild(wrong.Id, child.Id))
            .With(new BiologicalParentChild(child.Id, descendant.Id));

        var result = await CreateService().ReplaceParentAsync(child.Id, wrong.Id, descendant.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already a descendant", result.Error);
        Assert.Equal(wrong.Id, _tree.BiologicalLinks.Single(l => l.ChildId == child.Id).ParentId);
    }

    [Fact]
    public async Task RefusesToReplaceALinkThatDoesNotExist()
    {
        var child = Someone("Susan");
        var stranger = Someone("Vera");
        var right = Someone("Margaret");
        _tree.With(child, stranger, right);

        var result = await CreateService().ReplaceParentAsync(child.Id, stranger.Id, right.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("no biological relationship", result.Error);
    }

    [Fact]
    public async Task RefusesToReplaceAParentWithThemselves()
    {
        var child = Someone("Susan");
        var parent = Someone("Arthur");
        _tree.With(child, parent).With(new BiologicalParentChild(parent.Id, child.Id));

        var result = await CreateService().ReplaceParentAsync(child.Id, parent.Id, parent.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already the recorded parent", result.Error);
    }

    // ---- Siblings (US-037, US-051) ----

    /// <summary>Susan and Thomas share both parents; Daniel shares only Arthur.</summary>
    private (Person Subject, Person Full, Person Half) SeedSiblings()
    {
        var arthur = Someone("Arthur", birthYear: 1918);
        var margaret = Someone("Margaret", birthYear: 1921);
        var susan = Someone("Susan", birthYear: 1948);
        var thomas = Someone("Thomas", birthYear: 1951);
        var daniel = Someone("Daniel", birthYear: 1963);

        _tree.With(arthur, margaret, susan, thomas, daniel)
            .With(
                new BiologicalParentChild(arthur.Id, susan.Id),
                new BiologicalParentChild(margaret.Id, susan.Id),
                new BiologicalParentChild(arthur.Id, thomas.Id),
                new BiologicalParentChild(margaret.Id, thomas.Id),
                new BiologicalParentChild(arthur.Id, daniel.Id));

        return (susan, thomas, daniel);
    }

    [Fact]
    public async Task ClassifiesSomeoneSharingBothParentsAsAFullSibling()
    {
        var (susan, thomas, _) = SeedSiblings();

        var result = await CreateService().GetSiblingsAsync(susan.Id);

        var sibling = result.Value!.Single(s => s.Person.Id == thomas.Id);
        Assert.Equal(SiblingKind.Full, sibling.Kind);
    }

    [Fact]
    public async Task ClassifiesSomeoneSharingOneParentAsAHalfSibling()
    {
        var (susan, _, daniel) = SeedSiblings();

        var result = await CreateService().GetSiblingsAsync(susan.Id);

        var sibling = result.Value!.Single(s => s.Person.Id == daniel.Id);
        Assert.Equal(SiblingKind.Half, sibling.Kind);
    }

    [Fact]
    public async Task NamesTheSharedParent()
    {
        var (susan, _, daniel) = SeedSiblings();

        var result = await CreateService().GetSiblingsAsync(susan.Id);

        var sibling = result.Value!.Single(s => s.Person.Id == daniel.Id);
        Assert.Equal(["Arthur Whitfield"], sibling.SharedParentNames);
    }

    // Deliberately not the seeded family: there the full sibling is also the
    // older one, so an implementation that sorted by birth date and ignored the
    // grouping entirely would pass. Here the half-sibling is older, so the only
    // way to get this order is to group first.
    [Fact]
    public async Task ListsFullSiblingsBeforeHalfSiblingsEvenWhenTheHalfSiblingIsOlder()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var subject = Someone("Susan", birthYear: 1948);
        var youngerFull = Someone("Thomas", birthYear: 1975);
        var olderHalf = Someone("Daniel", birthYear: 1930);

        _tree.With(arthur, margaret, subject, youngerFull, olderHalf)
            .With(
                new BiologicalParentChild(arthur.Id, subject.Id),
                new BiologicalParentChild(margaret.Id, subject.Id),
                new BiologicalParentChild(arthur.Id, youngerFull.Id),
                new BiologicalParentChild(margaret.Id, youngerFull.Id),
                new BiologicalParentChild(arthur.Id, olderHalf.Id));

        var result = await CreateService().GetSiblingsAsync(subject.Id);

        Assert.Equal([youngerFull.Id, olderHalf.Id], result.Value!.Select(s => s.Person.Id));
    }

    // Ordering within a group is by birth date, so a group of half-siblings does
    // not come back in whatever order the store happened to hold them.
    [Fact]
    public async Task SortsSiblingsByBirthDateWithinTheirGroup()
    {
        var arthur = Someone("Arthur");
        var subject = Someone("Susan", birthYear: 1948);
        var younger = Someone("Younger", birthYear: 1970);
        var older = Someone("Older", birthYear: 1940);

        _tree.With(arthur, subject, younger, older)
            .With(
                new BiologicalParentChild(arthur.Id, subject.Id),
                new BiologicalParentChild(arthur.Id, younger.Id),
                new BiologicalParentChild(arthur.Id, older.Id));

        var result = await CreateService().GetSiblingsAsync(subject.Id);

        Assert.Equal([older.Id, younger.Id], result.Value!.Select(s => s.Person.Id));
    }

    // Somebody with one recorded parent has no full siblings — not because there
    // are none, but because the record cannot show it, and saying "Full" would
    // be inventing a fact about the parent nobody has entered.
    [Fact]
    public async Task WillNotCallAnyoneAFullSiblingWhenOnlyOneParentIsKnown()
    {
        var arthur = Someone("Arthur");
        var subject = Someone("Susan");
        var other = Someone("Thomas");

        _tree.With(arthur, subject, other)
            .With(
                new BiologicalParentChild(arthur.Id, subject.Id),
                new BiologicalParentChild(arthur.Id, other.Id));

        var result = await CreateService().GetSiblingsAsync(subject.Id);

        Assert.Equal(SiblingKind.Half, result.Value!.Single().Kind);
    }

    [Fact]
    public async Task DoesNotListThePersonAsTheirOwnSibling()
    {
        var (susan, _, _) = SeedSiblings();

        var result = await CreateService().GetSiblingsAsync(susan.Id);

        Assert.DoesNotContain(result.Value!, s => s.Person.Id == susan.Id);
    }

    [Fact]
    public async Task ReportsNoSiblingsForSomeoneWithNoParents()
    {
        var loner = Someone("Solo");
        _tree.With(loner);

        var result = await CreateService().GetSiblingsAsync(loner.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task ReportsNotFoundForSiblingsOfSomebodyWhoIsNotThere()
    {
        var missing = Guid.NewGuid();

        var result = await CreateService().GetSiblingsAsync(missing);

        Assert.False(result.IsSuccess);
        Assert.Contains(missing.ToString(), result.Error);
    }

    // ---- The profile's combined read ----

    [Fact]
    public async Task ReportsNotFoundForAProfileThatDoesNotExist()
    {
        var missing = Guid.NewGuid();

        var result = await CreateService().GetForPersonAsync(missing);

        Assert.False(result.IsSuccess);
        Assert.Contains(missing.ToString(), result.Error);
    }

    [Fact]
    public async Task ReportsTwoEmptyParentSlotsForSomebodyWithNoParents()
    {
        var loner = Someone("Solo");
        _tree.With(loner);

        var result = await CreateService().GetForPersonAsync(loner.Id);

        Assert.Equal(2, result.Value!.UnknownParentSlots);
    }

    [Fact]
    public async Task ReportsOneEmptyParentSlotForSomebodyWithOneParent()
    {
        var child = Someone("Susan");
        var parent = Someone("Arthur");
        _tree.With(child, parent).With(new BiologicalParentChild(parent.Id, child.Id));

        var result = await CreateService().GetForPersonAsync(child.Id);

        Assert.Equal(1, result.Value!.UnknownParentSlots);
    }

    // US-012 asks for children in birth order, which is also the only ordering
    // that reads as anything.
    [Fact]
    public async Task SortsChildrenByBirthDate()
    {
        var parent = Someone("Arthur");
        var younger = Someone("Younger", birthYear: 1975);
        var older = Someone("Older", birthYear: 1948);
        _tree.With(parent, younger, older)
            .With(
                new BiologicalParentChild(parent.Id, younger.Id),
                new BiologicalParentChild(parent.Id, older.Id));

        var result = await CreateService().GetForPersonAsync(parent.Id);

        Assert.Equal([older.Id, younger.Id], result.Value!.Children.Select(c => c.Person.Id));
    }

    // A missing year is not year zero. Sorting undated people first would put
    // everybody nobody has dated above the whole family.
    [Fact]
    public async Task SortsChildrenWithNoBirthDateLast()
    {
        var parent = Someone("Arthur");
        var dated = Someone("Dated", birthYear: 1975);
        var undated = Someone("Undated");
        _tree.With(parent, undated, dated)
            .With(
                new BiologicalParentChild(parent.Id, undated.Id),
                new BiologicalParentChild(parent.Id, dated.Id));

        var result = await CreateService().GetForPersonAsync(parent.Id);

        Assert.Equal([dated.Id, undated.Id], result.Value!.Children.Select(c => c.Person.Id));
    }

    // ---- Phantoms (US-041, US-054) ----

    [Fact]
    public async Task ShowsAPhantomAsARecordedParent()
    {
        var child = Someone("Margaret");
        var phantom = Person.CreatePhantom();
        _tree.With(child, phantom).With(new BiologicalParentChild(phantom.Id, child.Id));

        var result = await CreateService().GetForPersonAsync(child.Id);

        Assert.True(result.Value!.Parents.Single().IsPhantom);
        Assert.Equal(1, result.Value!.UnknownParentSlots);
    }

    // The cross-cutting rule keeps unidentified ancestors out of every list but
    // the parents section, where they are the whole point.
    [Fact]
    public async Task KeepsPhantomsOutOfTheChildrenSection()
    {
        var parent = Someone("Arthur");
        var phantom = Person.CreatePhantom();
        _tree.With(parent, phantom).With(new BiologicalParentChild(parent.Id, phantom.Id));

        var result = await CreateService().GetForPersonAsync(parent.Id);

        Assert.Empty(result.Value!.Children);
    }

    [Fact]
    public async Task KeepsPhantomsOutOfTheSiblingsSection()
    {
        var parent = Someone("Arthur");
        var subject = Someone("Susan");
        var phantom = Person.CreatePhantom();
        _tree.With(parent, subject, phantom)
            .With(
                new BiologicalParentChild(parent.Id, subject.Id),
                new BiologicalParentChild(parent.Id, phantom.Id));

        var result = await CreateService().GetSiblingsAsync(subject.Id);

        Assert.Empty(result.Value!);
    }

    // A link can outlive the person it names, because deleting somebody does not
    // sweep their relationships. Rendering a blank row would be worse than
    // omitting it.
    [Fact]
    public async Task SkipsALinkWhosePersonIsGone()
    {
        var child = Someone("Susan");
        _tree.With(child).With(new BiologicalParentChild(Guid.NewGuid(), child.Id));

        var result = await CreateService().GetForPersonAsync(child.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Parents);
    }
}
