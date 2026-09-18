using FamilyTree.Application.Common;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Application.Tests.Services;

/// <summary>
/// The rules an <see cref="AdoptiveParentChild"/> cannot enforce for itself:
/// duplicates, cycles, the absence of a cap, and what the adoption date does.
/// </summary>
public class AdoptiveRelationshipServiceTests
{
    private readonly InMemoryTree _tree = new();

    private AdoptiveRelationshipService CreateService() => new(
        _tree.PersonRepository,
        _tree.AdoptiveRepository,
        new CircularReferenceChecker(_tree.BiologicalRepository, _tree.AdoptiveRepository));

    private BiologicalRelationshipService CreateBiologicalService() => new(
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

    // ---- Adding an adoptive parent (US-014) ----

    [Fact]
    public async Task RecordsAnAdoptiveParent()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent);

        var result = await CreateService().AddParentAsync(child.Id, parent.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(_tree.AdoptiveLinks);
        Assert.Equal(parent.Id, _tree.AdoptiveLinks[0].ParentId);
        Assert.Equal(child.Id, _tree.AdoptiveLinks[0].ChildId);
    }

    [Fact]
    public async Task StoresTheAdoptionDate()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent);

        await CreateService().AddParentAsync(child.Id, parent.Id, PartialDate.FromYear(1977));

        Assert.Equal(1977, _tree.AdoptiveLinks[0].AdoptionDate!.Year);
    }

    // US-014 and US-015 both treat an undated adoption as ordinary rather than
    // incomplete, so the link saves without one.
    [Fact]
    public async Task AcceptsAnAdoptionWithNoDate()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent);

        var result = await CreateService().AddParentAsync(child.Id, parent.Id, adoptionDate: null);

        Assert.True(result.IsSuccess);
        Assert.Null(_tree.AdoptiveLinks[0].AdoptionDate);
    }

    [Fact]
    public async Task DefaultsCertaintyToConfirmed()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent);

        await CreateService().AddParentAsync(child.Id, parent.Id);

        Assert.Equal(RelationshipCertainty.Confirmed, _tree.AdoptiveLinks[0].Certainty);
    }

    // One record is both directions, so the reciprocal adoptive child entry
    // US-014 asks for is structural rather than a second write that could fall
    // out of step.
    [Fact]
    public async Task TheSameRecordServesAsTheChildLink()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent);

        await CreateService().AddParentAsync(child.Id, parent.Id);
        var relations = await CreateService().GetForPersonAsync(parent.Id);

        Assert.Single(relations.Value!.Children);
        Assert.Equal(child.Id, relations.Value!.Children[0].Person.Id);
    }

    [Fact]
    public async Task RejectsADuplicateAdoptiveParent()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent).With(new AdoptiveParentChild(parent.Id, child.Id));

        var result = await CreateService().AddParentAsync(child.Id, parent.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already recorded as an adoptive parent", result.Error);
    }

    [Fact]
    public async Task RejectsSelfAdoption()
    {
        var person = Someone("Priya");
        _tree.With(person);

        var result = await CreateService().AddParentAsync(person.Id, person.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("cannot be their own adoptive parent", result.Error);
    }

    [Fact]
    public async Task RejectsAParentWhoDoesNotExist()
    {
        var child = Someone("Priya");
        _tree.With(child);

        var result = await CreateService().AddParentAsync(child.Id, Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Contains("No person with id", result.Error);
    }

    [Fact]
    public async Task RejectsAChildWhoDoesNotExist()
    {
        var parent = Someone("Susan");
        _tree.With(parent);

        var result = await CreateService().AddParentAsync(Guid.NewGuid(), parent.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("No person with id", result.Error);
    }

    // Reported as "choose somebody" rather than as self-adoption: two empty ids
    // are equal, so the self check would otherwise accuse the user of something
    // they did not do.
    [Fact]
    public async Task RejectsEmptyIdsWithoutClaimingSelfAdoption()
    {
        var result = await CreateService().AddParentAsync(Guid.Empty, Guid.Empty);

        Assert.False(result.IsSuccess);
        Assert.Contains("must be chosen", result.Error);
    }

    // ---- No cap (US-014, US-018) ----

    // The single behavioural difference from the biological service, and the one
    // most likely to be "tidied" back into a limit by somebody mirroring that
    // code: a child moved between placements accumulates adoptive parents, and a
    // tree that refused the third would be wrong about a real childhood.
    [Fact]
    public async Task AcceptsMoreThanTwoAdoptiveParents()
    {
        var child = Someone("Priya");
        var first = Someone("Susan");
        var second = Someone("Raymond");
        var third = Someone("Miriam");
        _tree.With(child, first, second, third);

        var service = CreateService();
        await service.AddParentAsync(child.Id, first.Id);
        await service.AddParentAsync(child.Id, second.Id);
        var result = await service.AddParentAsync(child.Id, third.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, _tree.AdoptiveLinks.Count);
    }

    [Fact]
    public async Task AcceptsManyAdoptiveChildren()
    {
        var parent = Someone("Susan");
        var children = new[] { Someone("Priya"), Someone("Tom"), Someone("Ana") };
        _tree.With(parent).With(children);

        var service = CreateService();
        foreach (var child in children)
        {
            Assert.True((await service.AddChildAsync(parent.Id, child.Id)).IsSuccess);
        }

        Assert.Equal(3, _tree.AdoptiveLinks.Count);
    }

    // ---- Both kinds of parent together (US-039) ----

    [Fact]
    public async Task AdoptiveParentsDoNotCountAgainstTheBiologicalCap()
    {
        var child = Someone("Priya");
        var bioMother = Someone("Elena");
        var bioFather = Someone("Marek");
        var adoptiveMother = Someone("Susan");
        _tree.With(child, bioMother, bioFather, adoptiveMother);

        await CreateBiologicalService().AddParentAsync(child.Id, bioMother.Id);
        await CreateBiologicalService().AddParentAsync(child.Id, bioFather.Id);
        var result = await CreateService().AddParentAsync(child.Id, adoptiveMother.Id);

        Assert.True(result.IsSuccess);
    }

    // The mirror of the above, and the one that would break silently if the
    // biological cap ever counted rows from both tables.
    [Fact]
    public async Task BiologicalParentsAreStillCappedAtTwoAlongsideAdoptiveOnes()
    {
        var child = Someone("Priya");
        var adoptive = Someone("Susan");
        var bioMother = Someone("Elena");
        var bioFather = Someone("Marek");
        var third = Someone("Nobody");
        _tree.With(child, adoptive, bioMother, bioFather, third);

        await CreateService().AddParentAsync(child.Id, adoptive.Id);
        await CreateBiologicalService().AddParentAsync(child.Id, bioMother.Id);
        await CreateBiologicalService().AddParentAsync(child.Id, bioFather.Id);
        var result = await CreateBiologicalService().AddParentAsync(child.Id, third.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already has 2 biological parents", result.Error);
    }

    // The same person may be recorded both ways. Legally this happens — a
    // biological parent formally adopting their own child — and the two tables
    // record different facts, so neither duplicate check sees the other.
    [Fact]
    public async Task AllowsTheSamePersonAsBothABiologicalAndAnAdoptiveParent()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent);

        await CreateBiologicalService().AddParentAsync(child.Id, parent.Id);
        var result = await CreateService().AddParentAsync(child.Id, parent.Id);

        Assert.True(result.IsSuccess);
    }

    // ---- Cycles (US-040) ----

    [Fact]
    public async Task RefusesAnAdoptiveLoop()
    {
        var elder = Someone("Susan");
        var younger = Someone("Priya");
        _tree.With(elder, younger).With(new AdoptiveParentChild(elder.Id, younger.Id));

        var result = await CreateService().AddParentAsync(elder.Id, younger.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already a descendant", result.Error);
    }

    // The case US-039 creates and neither checker would catch alone: the loop
    // runs up a biological edge and back down an adoptive one.
    [Fact]
    public async Task RefusesALoopThatRunsThroughABiologicalEdge()
    {
        var grandparent = Someone("Arthur");
        var parent = Someone("Susan");
        var child = Someone("Priya");
        _tree.With(grandparent, parent, child)
            .With(new BiologicalParentChild(grandparent.Id, parent.Id))
            .With(new AdoptiveParentChild(parent.Id, child.Id));

        var result = await CreateService().AddParentAsync(grandparent.Id, child.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already a descendant", result.Error);
    }

    // ---- Warnings, which never block (US-014) ----

    [Fact]
    public async Task WarnsWhenTheAdoptiveParentIsBornAfterTheChild()
    {
        var child = Someone("Priya", birthYear: 1970);
        var parent = Someone("Susan", birthYear: 1990);
        _tree.With(child, parent);

        var result = await CreateService().AddParentAsync(child.Id, parent.Id);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsWarning);
        Assert.Contains("born in 1990, after", result.Warning);
        Assert.Single(_tree.AdoptiveLinks);
    }

    [Fact]
    public async Task WarnsWhenBothAreBornInTheSameYearWithoutClaimingOneCameAfter()
    {
        var child = Someone("Priya", birthYear: 1970);
        var parent = Someone("Susan", birthYear: 1970);
        _tree.With(child, parent);

        var result = await CreateService().AddParentAsync(child.Id, parent.Id);

        Assert.True(result.IsWarning);
        Assert.Contains("both recorded as born in 1970", result.Warning);
        Assert.DoesNotContain("after", result.Warning);
    }

    // Specific to this relationship: an adoption cannot predate the child. It is
    // also the problem a correction can act on immediately, since it is about
    // the value just typed rather than about either person's record.
    [Fact]
    public async Task WarnsWhenTheAdoptionPredatesTheChildsBirth()
    {
        var child = Someone("Priya", birthYear: 1975);
        var parent = Someone("Susan", birthYear: 1950);
        _tree.With(child, parent);

        var result = await CreateService().AddParentAsync(
            child.Id, parent.Id, PartialDate.FromYear(1970));

        Assert.True(result.IsSuccess);
        Assert.Contains("dated 1970, before", result.Warning);
    }

    // Result carries one warning, so reporting only the first problem found
    // would hide the second until somebody fixed the first and saved again —
    // which reads as the app changing its mind.
    [Fact]
    public async Task ReportsBothDateProblemsInOneWarning()
    {
        var child = Someone("Priya", birthYear: 1975);
        var parent = Someone("Susan", birthYear: 1990);
        _tree.With(child, parent);

        var result = await CreateService().AddParentAsync(
            child.Id, parent.Id, PartialDate.FromYear(1970));

        Assert.Contains("born in 1990, after", result.Warning);
        Assert.Contains("dated 1970, before", result.Warning);
    }

    [Fact]
    public async Task DoesNotWarnWhenTheDatesAreOrdinary()
    {
        var child = Someone("Priya", birthYear: 1975);
        var parent = Someone("Susan", birthYear: 1950);
        _tree.With(child, parent);

        var result = await CreateService().AddParentAsync(
            child.Id, parent.Id, PartialDate.FromYear(1977));

        Assert.False(result.IsWarning);
    }

    [Fact]
    public async Task DoesNotWarnWhenABirthDateIsMissing()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan", birthYear: 1990);
        _tree.With(child, parent);

        var result = await CreateService().AddParentAsync(child.Id, parent.Id);

        Assert.False(result.IsWarning);
    }

    // ---- Removing (US-017, US-020) ----

    [Fact]
    public async Task RemovesTheLink()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent).With(new AdoptiveParentChild(parent.Id, child.Id));

        var result = await CreateService().RemoveAsync(parent.Id, child.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(_tree.AdoptiveLinks);
    }

    // The point both stories make: the relationship goes, the people stay.
    [Fact]
    public async Task RemovingTheLinkDeletesNeitherPerson()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent).With(new AdoptiveParentChild(parent.Id, child.Id));

        await CreateService().RemoveAsync(parent.Id, child.Id);

        Assert.Equal(2, _tree.People.Count);
    }

    [Fact]
    public async Task ReportsRemovingALinkThatIsNotThere()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent);

        var result = await CreateService().RemoveAsync(parent.Id, child.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("no adoptive relationship", result.Error);
    }

    // ---- Editing the details (US-016) ----

    [Fact]
    public async Task UpdatesTheAdoptionDate()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        var link = new AdoptiveParentChild(parent.Id, child.Id, PartialDate.FromYear(1970));
        _tree.With(child, parent).With(link);

        var result = await CreateService().UpdateAsync(
            parent.Id, child.Id, PartialDate.FromYear(1977), RelationshipCertainty.Confirmed);

        Assert.True(result.IsSuccess);
        Assert.Equal(1977, _tree.AdoptiveLinks[0].AdoptionDate!.Year);
    }

    // Clearing a date is a legitimate correction — somebody recorded a year they
    // could not source — so null has to mean "unknown" rather than "unchanged".
    [Fact]
    public async Task ClearsTheAdoptionDateWhenGivenNothing()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent)
            .With(new AdoptiveParentChild(parent.Id, child.Id, PartialDate.FromYear(1977)));

        await CreateService().UpdateAsync(
            parent.Id, child.Id, null, RelationshipCertainty.Confirmed);

        Assert.Null(_tree.AdoptiveLinks[0].AdoptionDate);
    }

    [Fact]
    public async Task UpdatesTheCertainty()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent).With(new AdoptiveParentChild(parent.Id, child.Id));

        await CreateService().UpdateAsync(
            parent.Id, child.Id, null, RelationshipCertainty.Speculative);

        Assert.Equal(RelationshipCertainty.Speculative, _tree.AdoptiveLinks[0].Certainty);
    }

    // The edit keeps the link's identity. A replace would have written a new row
    // with a new id to record a corrected year, which loses the thread between
    // the record and anything that later refers to it.
    [Fact]
    public async Task EditingKeepsTheSameLinkId()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        var link = new AdoptiveParentChild(parent.Id, child.Id);
        _tree.With(child, parent).With(link);

        await CreateService().UpdateAsync(
            parent.Id, child.Id, PartialDate.FromYear(1977), RelationshipCertainty.Confirmed);

        Assert.Equal(link.Id, _tree.AdoptiveLinks[0].Id);
    }

    [Fact]
    public async Task WarnsWhenAnEditedDatePredatesTheChildsBirth()
    {
        var child = Someone("Priya", birthYear: 1975);
        var parent = Someone("Susan", birthYear: 1950);
        _tree.With(child, parent).With(new AdoptiveParentChild(parent.Id, child.Id));

        var result = await CreateService().UpdateAsync(
            parent.Id, child.Id, PartialDate.FromYear(1970), RelationshipCertainty.Confirmed);

        Assert.True(result.IsSuccess);
        Assert.Contains("dated 1970, before", result.Warning);
    }

    [Fact]
    public async Task ReportsEditingALinkThatIsNotThere()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent);

        var result = await CreateService().UpdateAsync(
            parent.Id, child.Id, null, RelationshipCertainty.Confirmed);

        Assert.False(result.IsSuccess);
    }

    // ---- Replacing the person (US-016) ----

    [Fact]
    public async Task ReplacesTheAdoptiveParent()
    {
        var child = Someone("Priya");
        var wrong = Someone("Susan");
        var right = Someone("Miriam");
        _tree.With(child, wrong, right).With(new AdoptiveParentChild(wrong.Id, child.Id));

        var result = await CreateService().ReplaceParentAsync(child.Id, wrong.Id, right.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(_tree.AdoptiveLinks);
        Assert.Equal(right.Id, _tree.AdoptiveLinks[0].ParentId);
    }

    // The API-seam property, and the only place a test can see it: a
    // delete-then-add can half-apply and leave the child with neither parent.
    [Fact]
    public async Task ReplacesThroughOneRepositoryCall()
    {
        var child = Someone("Priya");
        var wrong = Someone("Susan");
        var right = Someone("Miriam");
        _tree.With(child, wrong, right).With(new AdoptiveParentChild(wrong.Id, child.Id));

        await CreateService().ReplaceParentAsync(child.Id, wrong.Id, right.Id);

        Assert.Equal(1, _tree.AdoptiveReplaceCount);
    }

    [Fact]
    public async Task ReplaceCarriesTheAdoptionDate()
    {
        var child = Someone("Priya");
        var wrong = Someone("Susan");
        var right = Someone("Miriam");
        _tree.With(child, wrong, right).With(new AdoptiveParentChild(wrong.Id, child.Id));

        await CreateService().ReplaceParentAsync(
            child.Id, wrong.Id, right.Id, PartialDate.FromYear(1977));

        Assert.Equal(1977, _tree.AdoptiveLinks[0].AdoptionDate!.Year);
    }

    [Fact]
    public async Task RefusesToReplaceAPersonWithThemselves()
    {
        var child = Someone("Priya");
        var parent = Someone("Susan");
        _tree.With(child, parent).With(new AdoptiveParentChild(parent.Id, child.Id));

        var result = await CreateService().ReplaceParentAsync(child.Id, parent.Id, parent.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already the recorded adoptive parent", result.Error);
    }

    [Fact]
    public async Task RefusesToReplaceALinkThatIsNotThere()
    {
        var child = Someone("Priya");
        var wrong = Someone("Susan");
        var right = Someone("Miriam");
        _tree.With(child, wrong, right);

        var result = await CreateService().ReplaceParentAsync(child.Id, wrong.Id, right.Id);

        Assert.False(result.IsSuccess);
    }

    // A correction that could create a loop would be a hole straight through
    // US-040, so the replacement is judged exactly as a fresh addition.
    [Fact]
    public async Task RefusesAReplacementThatWouldCreateALoop()
    {
        var child = Someone("Priya");
        var wrong = Someone("Susan");
        var descendant = Someone("Tom");
        _tree.With(child, wrong, descendant)
            .With(new AdoptiveParentChild(wrong.Id, child.Id))
            .With(new AdoptiveParentChild(child.Id, descendant.Id));

        var result = await CreateService().ReplaceParentAsync(child.Id, wrong.Id, descendant.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already a descendant", result.Error);
    }

    // The link being replaced is excluded from the duplicate check, or a
    // no-op-looking correction would report itself as a duplicate of itself.
    [Fact]
    public async Task RefusesAReplacementThatDuplicatesAnotherRecordedParent()
    {
        var child = Someone("Priya");
        var wrong = Someone("Susan");
        var already = Someone("Raymond");
        _tree.With(child, wrong, already)
            .With(new AdoptiveParentChild(wrong.Id, child.Id))
            .With(new AdoptiveParentChild(already.Id, child.Id));

        var result = await CreateService().ReplaceParentAsync(child.Id, wrong.Id, already.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already recorded as an adoptive parent", result.Error);
    }

    // ---- Reading a profile (US-015, US-019) ----

    [Fact]
    public async Task ReportsAnUnknownPerson()
    {
        var result = await CreateService().GetForPersonAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ReadsBothSectionsForSomebodyWithNeither()
    {
        var person = Someone("Priya");
        _tree.With(person);

        var result = await CreateService().GetForPersonAsync(person.Id);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsEmpty);
    }

    [Fact]
    public async Task SortsAdoptiveChildrenByAdoptionDateThenBirthDate()
    {
        var parent = Someone("Susan");
        var later = Someone("Ana", birthYear: 1960);
        var earlier = Someone("Tom", birthYear: 1990);
        _tree.With(parent, later, earlier)
            .With(new AdoptiveParentChild(parent.Id, later.Id, PartialDate.FromYear(1985)))
            .With(new AdoptiveParentChild(parent.Id, earlier.Id, PartialDate.FromYear(1977)));

        var result = await CreateService().GetForPersonAsync(parent.Id);

        Assert.Equal("Tom", result.Value!.Children[0].Person.DisplayName.Split(' ')[0]);
        Assert.Equal("Ana", result.Value!.Children[1].Person.DisplayName.Split(' ')[0]);
    }

    // A missing date is not year zero, and a record nobody has dated is not the
    // earliest thing that happened.
    [Fact]
    public async Task SortsUndatedAdoptionsLast()
    {
        var parent = Someone("Susan");
        var undated = Someone("Ana");
        var dated = Someone("Tom");
        _tree.With(parent, undated, dated)
            .With(new AdoptiveParentChild(parent.Id, undated.Id))
            .With(new AdoptiveParentChild(parent.Id, dated.Id, PartialDate.FromYear(1985)));

        var result = await CreateService().GetForPersonAsync(parent.Id);

        Assert.Equal(dated.Id, result.Value!.Children[0].Person.Id);
        Assert.Equal(undated.Id, result.Value!.Children[1].Person.Id);
    }

    [Fact]
    public async Task FallsBackToBirthDateWhenTwoAdoptionsShareADate()
    {
        var parent = Someone("Susan");
        var older = Someone("Ana", birthYear: 1968);
        var younger = Someone("Tom", birthYear: 1972);
        _tree.With(parent, younger, older)
            .With(new AdoptiveParentChild(parent.Id, younger.Id, PartialDate.FromYear(1977)))
            .With(new AdoptiveParentChild(parent.Id, older.Id, PartialDate.FromYear(1977)));

        var result = await CreateService().GetForPersonAsync(parent.Id);

        Assert.Equal(older.Id, result.Value!.Children[0].Person.Id);
    }

    // A link can outlive the person it names, because deleting a person does not
    // sweep their relationships. A blank row would be worse than an omitted one.
    [Fact]
    public async Task OmitsRowsWhosePersonIsGone()
    {
        var parent = Someone("Susan");
        _tree.With(parent).With(new AdoptiveParentChild(parent.Id, Guid.NewGuid()));

        var result = await CreateService().GetForPersonAsync(parent.Id);

        Assert.Empty(result.Value!.Children);
    }

    // ---- Phantoms ----

    // A phantom stands for an ancestor nobody has identified, so it belongs in a
    // parents section and nowhere else.
    [Fact]
    public async Task ShowsAPhantomAdoptiveParent()
    {
        var child = Someone("Priya");
        var phantom = Person.CreatePhantom();
        _tree.With(child, phantom).With(new AdoptiveParentChild(phantom.Id, child.Id));

        var result = await CreateService().GetForPersonAsync(child.Id);

        Assert.Single(result.Value!.Parents);
        Assert.True(result.Value!.Parents[0].IsPhantom);
    }

    [Fact]
    public async Task HidesAPhantomAdoptiveChild()
    {
        var parent = Someone("Susan");
        var phantom = Person.CreatePhantom();
        _tree.With(parent, phantom).With(new AdoptiveParentChild(parent.Id, phantom.Id));

        var result = await CreateService().GetForPersonAsync(parent.Id);

        Assert.Empty(result.Value!.Children);
    }
}
