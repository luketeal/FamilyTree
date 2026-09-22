using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Application.Tests.Services;

/// <summary>
/// The rule that makes a stepparent label mean something: the marriage it rests
/// on has to involve a parent of the child. Plus the candidate list, which is the
/// only way a label gets applied.
/// </summary>
public class StepparentServiceTests
{
    private readonly InMemoryTree _tree = new();

    private StepparentService CreateService() => new(
        _tree.PersonRepository,
        _tree.BiologicalRepository,
        _tree.AdoptiveRepository,
        _tree.MarriageRepository,
        _tree.StepparentRepository);

    private MarriageService CreateMarriageService() => new(
        _tree.PersonRepository,
        _tree.MarriageRepository);

    private static Person Someone(string first, string last = "Whitfield", int? birthYear = null)
    {
        var person = new Person(first, last, Gender.Unknown);
        if (birthYear is int year)
        {
            person.UpdateDates(PartialDate.FromYear(year), null, null, null);
        }

        return person;
    }

    private static Marriage Married(
        Person one, Person two, int startYear, int? endYear = null, MarriageEndReason? reason = null)
    {
        var marriage = new Marriage(one.Id, two.Id, PartialDate.FromYear(startYear));
        if (endYear is not null || reason is not null)
        {
            marriage.UpdateDates(
                PartialDate.FromYear(startYear),
                null,
                endYear is int year ? PartialDate.FromYear(year) : null,
                reason);
        }

        return marriage;
    }

    /// <summary>
    /// The shape every test here needs: a child, their parent, and the person that
    /// parent married.
    /// </summary>
    private sealed record Blended(Person Child, Person Parent, Person Stepparent, Marriage Marriage);

    private Blended Household(int? marriageEndYear = null)
    {
        var daniel = Someone("Daniel", birthYear: 1963);
        var arthur = Someone("Arthur", birthYear: 1918);
        var vera = Someone("Vera", birthYear: 1930);
        var marriage = Married(
            arthur, vera, 1976, marriageEndYear,
            marriageEndYear is null ? null : MarriageEndReason.Divorce);

        _tree.With(daniel, arthur, vera)
            .With(new BiologicalParentChild(arthur.Id, daniel.Id))
            .With(marriage);

        return new Blended(daniel, arthur, vera, marriage);
    }

    // ---- Applying a label (US-038) ----

    [Fact]
    public async Task LabelsAParentsSpouseAsAStepparent()
    {
        var home = Household();

        var result = await CreateService().LabelAsync(
            home.Child.Id, home.Stepparent.Id, home.Marriage.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(_tree.StepparentLinks);
        Assert.Equal(home.Stepparent.Id, _tree.StepparentLinks[0].StepparentId);
        Assert.Equal(home.Marriage.Id, _tree.StepparentLinks[0].MarriageId);
    }

    // The spouse of an *adoptive* parent counts too. US-038 says biological or
    // adoptive, and splitting them would make an adoptive parent's spouse
    // unrecordable.
    [Fact]
    public async Task LabelsTheSpouseOfAnAdoptiveParent()
    {
        var priya = Someone("Priya");
        var susan = Someone("Susan");
        var partner = Someone("Nadia", "Reid");
        var marriage = Married(susan, partner, 1980);
        _tree.With(priya, susan, partner)
            .With(new AdoptiveParentChild(susan.Id, priya.Id, PartialDate.FromYear(1977)))
            .With(marriage);

        var result = await CreateService().LabelAsync(priya.Id, partner.Id, marriage.Id);

        Assert.True(result.IsSuccess, result.Error);
    }

    // The rule this service exists for. Without it the label would assert a step
    // relationship between two families with nothing to do with each other, and a
    // step edge in the tree would appear between strangers.
    [Fact]
    public async Task RejectsALabelWhoseMarriageDoesNotInvolveAParentOfTheChild()
    {
        var daniel = Someone("Daniel");
        var stranger = Someone("Ellen", "Reid");
        var theirSpouse = Someone("Nadia", "Reid");
        var marriage = Married(stranger, theirSpouse, 1990);
        _tree.With(daniel, stranger, theirSpouse).With(marriage);

        var result = await CreateService().LabelAsync(daniel.Id, theirSpouse.Id, marriage.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("does not involve a parent", result.Error);
        Assert.Empty(_tree.StepparentLinks);
    }

    [Fact]
    public async Task RejectsALabelNamingSomebodyWhoIsNotInThatMarriage()
    {
        var home = Household();
        var outsider = Someone("Ellen", "Reid");
        _tree.With(outsider);

        var result = await CreateService().LabelAsync(home.Child.Id, outsider.Id, home.Marriage.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("not one of the spouses", result.Error);
    }

    // The couple who are both parents of the same child. Each is married to the
    // other, so without this rule each would qualify as their own child's
    // stepparent — and US-038 asks for the three kinds to stay distinct.
    [Fact]
    public async Task RejectsLabellingSomebodyWhoIsAlreadyAParentOfTheChild()
    {
        var eleanor = Someone("Eleanor");
        var susan = Someone("Susan");
        var raymond = Someone("Raymond", "Hartley");
        var marriage = Married(susan, raymond, 1970);
        _tree.With(eleanor, susan, raymond)
            .With(new BiologicalParentChild(susan.Id, eleanor.Id))
            .With(new BiologicalParentChild(raymond.Id, eleanor.Id))
            .With(marriage);

        var result = await CreateService().LabelAsync(eleanor.Id, raymond.Id, marriage.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already recorded as a parent", result.Error);
    }

    [Fact]
    public async Task RejectsADuplicateLabel()
    {
        var home = Household();
        _tree.With(new StepparentRelationship(
            home.Stepparent.Id, home.Child.Id, home.Marriage.Id));

        var result = await CreateService().LabelAsync(
            home.Child.Id, home.Stepparent.Id, home.Marriage.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("already recorded as a stepparent", result.Error);
        Assert.Single(_tree.StepparentLinks);
    }

    // Keyed on the pair rather than on the pair and the marriage: a second label
    // through another marriage is the same claim recorded twice, and a profile
    // showing the same stepparent on two rows is a duplicate however the records
    // differ.
    [Fact]
    public async Task RejectsASecondLabelForTheSamePairThroughAnotherMarriage()
    {
        var daniel = Someone("Daniel");
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var vera = Someone("Vera");
        var first = Married(arthur, vera, 1976, 1990, MarriageEndReason.Divorce);
        // Vera later married Daniel's other parent as well.
        var second = Married(margaret, vera, 1995);
        _tree.With(daniel, arthur, margaret, vera)
            .With(new BiologicalParentChild(arthur.Id, daniel.Id))
            .With(new BiologicalParentChild(margaret.Id, daniel.Id))
            .With(first)
            .With(second)
            .With(new StepparentRelationship(vera.Id, daniel.Id, first.Id));

        var result = await CreateService().LabelAsync(daniel.Id, vera.Id, second.Id);

        Assert.False(result.IsSuccess);
        Assert.Single(_tree.StepparentLinks);
    }

    [Fact]
    public async Task RejectsSelfStepparenthood()
    {
        var home = Household();

        var result = await CreateService().LabelAsync(
            home.Child.Id, home.Child.Id, home.Marriage.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("their own stepparent", result.Error);
    }

    [Fact]
    public async Task RejectsALabelWithNothingNamed()
    {
        var result = await CreateService().LabelAsync(Guid.Empty, Guid.Empty, Guid.Empty);

        Assert.False(result.IsSuccess);
        Assert.Contains("must all be named", result.Error);
    }

    [Fact]
    public async Task RejectsALabelRestingOnAMarriageThatIsGone()
    {
        var home = Household();

        var result = await CreateService().LabelAsync(
            home.Child.Id, home.Stepparent.Id, Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Contains("no longer recorded", result.Error);
    }

    [Fact]
    public async Task RejectsALabelNamingSomebodyWhoIsNotInTheTree()
    {
        var home = Household();

        var result = await CreateService().LabelAsync(
            Guid.NewGuid(), home.Stepparent.Id, home.Marriage.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("No person with id", result.Error);
    }

    // ---- Reading a profile (US-038) ----

    [Fact]
    public async Task ShowsTheStepparentOnTheChildsProfile()
    {
        var home = Household();
        await CreateService().LabelAsync(home.Child.Id, home.Stepparent.Id, home.Marriage.Id);

        var result = await CreateService().GetForPersonAsync(home.Child.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(result.Value!.Stepparents);
        Assert.Equal(home.Stepparent.Id, result.Value!.Stepparents[0].Person.Id);
    }

    // The row says which parent the relationship runs through: with two marriages
    // in play that is the only thing distinguishing two rows, and it is why
    // removing a marriage removes one of them.
    [Fact]
    public async Task SaysWhichParentTheStepRelationshipRunsThrough()
    {
        var home = Household();
        await CreateService().LabelAsync(home.Child.Id, home.Stepparent.Id, home.Marriage.Id);

        var result = await CreateService().GetForPersonAsync(home.Child.Id);

        Assert.Equal("Arthur Whitfield", result.Value!.Stepparents[0].ViaName);
    }

    [Fact]
    public async Task ShowsTheStepchildOnTheStepparentsProfile()
    {
        var home = Household();
        await CreateService().LabelAsync(home.Child.Id, home.Stepparent.Id, home.Marriage.Id);

        var result = await CreateService().GetForPersonAsync(home.Stepparent.Id);

        Assert.Single(result.Value!.Stepchildren);
        Assert.Equal(home.Child.Id, result.Value!.Stepchildren[0].Person.Id);
    }

    // US-038: a stepparent does not appear in the biological or adoptive sections.
    // Structural rather than filtered — the label is a different record in a
    // different store — but worth pinning, since the whole point is the distinction.
    [Fact]
    public async Task AStepparentIsNotABiologicalOrAdoptiveParent()
    {
        var home = Household();
        await CreateService().LabelAsync(home.Child.Id, home.Stepparent.Id, home.Marriage.Id);

        var biological = new BiologicalRelationshipService(
            _tree.PersonRepository,
            _tree.BiologicalRepository,
            new CircularReferenceChecker(_tree.BiologicalRepository, _tree.AdoptiveRepository));
        var adoptive = new AdoptiveRelationshipService(
            _tree.PersonRepository,
            _tree.AdoptiveRepository,
            new CircularReferenceChecker(_tree.BiologicalRepository, _tree.AdoptiveRepository));

        var bio = await biological.GetForPersonAsync(home.Child.Id);
        var adopt = await adoptive.GetForPersonAsync(home.Child.Id);

        Assert.DoesNotContain(home.Stepparent.Id, bio.Value!.Parents.Select(p => p.Person.Id));
        Assert.Empty(adopt.Value!.Parents);
    }

    // The label's justification survives the parent link that led to it. Reading
    // only the parents' marriages made the row vanish from this profile while it
    // still showed, with a Remove button, on the stepparent's — two profiles
    // disagreeing about one record.
    [Fact]
    public async Task KeepsTheStepparentRowWhenTheParentLinkIsRemoved()
    {
        var home = Household();
        await CreateService().LabelAsync(home.Child.Id, home.Stepparent.Id, home.Marriage.Id);

        _tree.BiologicalLinks.RemoveAll(l => l.ChildId == home.Child.Id);

        var result = await CreateService().GetForPersonAsync(home.Child.Id);

        Assert.True(result.IsSuccess, result.Error);
        var row = Assert.Single(result.Value!.Stepparents);
        Assert.Equal(home.Stepparent.Id, row.Person.Id);
        Assert.Equal("Arthur Whitfield", row.ViaName);
    }

    // The other end of the same record, which always worked — asserted alongside so
    // that a regression on either side shows up as the two disagreeing again.
    [Fact]
    public async Task BothProfilesAgreeAfterTheParentLinkIsRemoved()
    {
        var home = Household();
        await CreateService().LabelAsync(home.Child.Id, home.Stepparent.Id, home.Marriage.Id);

        _tree.BiologicalLinks.RemoveAll(l => l.ChildId == home.Child.Id);

        var childs = await CreateService().GetForPersonAsync(home.Child.Id);
        var stepparents = await CreateService().GetForPersonAsync(home.Stepparent.Id);

        Assert.Single(childs.Value!.Stepparents);
        Assert.Single(stepparents.Value!.Stepchildren);
    }

    // Removing the parent link removes the reason to offer anybody, so the
    // candidate list empties even though the label already applied stays.
    [Fact]
    public async Task StopsOfferingCandidatesWhenTheParentLinkIsRemoved()
    {
        var home = Household();

        _tree.BiologicalLinks.RemoveAll(l => l.ChildId == home.Child.Id);

        var result = await CreateService().GetForPersonAsync(home.Child.Id);

        Assert.Empty(result.Value!.Candidates);
    }

    // A label naming a marriage this tree does not have renders nowhere, so the row
    // is dropped rather than shown blank. Reachable through an imported file.
    [Fact]
    public async Task OmitsALabelWhoseMarriageIsMissing()
    {
        var home = Household();
        _tree.With(new StepparentRelationship(
            home.Stepparent.Id, home.Child.Id, Guid.NewGuid()));

        var result = await CreateService().GetForPersonAsync(home.Child.Id);

        Assert.Empty(result.Value!.Stepparents);
    }

    [Fact]
    public async Task ReportsTheNotFoundCase()
    {
        var result = await CreateService().GetForPersonAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Contains("No person with id", result.Error);
    }

    // ---- The candidate list (US-038) ----

    [Fact]
    public async Task OffersAParentsCurrentSpouseAsACandidate()
    {
        var home = Household();

        var result = await CreateService().GetForPersonAsync(home.Child.Id);

        Assert.Single(result.Value!.Candidates);
        Assert.Equal(home.Stepparent.Id, result.Value!.Candidates[0].Person.Id);
        Assert.Equal(home.Marriage.Id, result.Value!.Candidates[0].MarriageId);
    }

    // US-038 says current spouses. An ended marriage puts nobody in the household
    // now, and offering a divorced ex-spouse would invite recording a relationship
    // the record says ended.
    [Fact]
    public async Task DoesNotOfferAnExSpouseAsACandidate()
    {
        var home = Household(marriageEndYear: 1990);

        var result = await CreateService().GetForPersonAsync(home.Child.Id);

        Assert.Empty(result.Value!.Candidates);
    }

    // The label survives its marriage ending. Only deleting the marriage record
    // removes it (US-038), so "what to offer" and "what has been asserted" are
    // different questions.
    [Fact]
    public async Task ALabelSurvivesTheMarriageEnding()
    {
        var home = Household();
        await CreateService().LabelAsync(home.Child.Id, home.Stepparent.Id, home.Marriage.Id);

        await CreateMarriageService().UpdateAsync(
            home.Marriage.Id, PartialDate.FromYear(1976), null,
            PartialDate.FromYear(1990), MarriageEndReason.Divorce,
            RelationshipCertainty.Confirmed);

        var result = await CreateService().GetForPersonAsync(home.Child.Id);

        Assert.Single(result.Value!.Stepparents);
        Assert.False(result.Value!.Stepparents[0].ViaIsOngoing);
    }

    [Fact]
    public async Task StopsOfferingSomebodyOnceTheyAreLabelled()
    {
        var home = Household();
        await CreateService().LabelAsync(home.Child.Id, home.Stepparent.Id, home.Marriage.Id);

        var result = await CreateService().GetForPersonAsync(home.Child.Id);

        Assert.Empty(result.Value!.Candidates);
    }

    // The child's own parents are married to each other, and a parent is not a
    // stepparent. Without this the list would offer the child's own mother and
    // father.
    [Fact]
    public async Task DoesNotOfferTheChildsOwnParents()
    {
        var eleanor = Someone("Eleanor");
        var susan = Someone("Susan");
        var raymond = Someone("Raymond", "Hartley");
        _tree.With(eleanor, susan, raymond)
            .With(new BiologicalParentChild(susan.Id, eleanor.Id))
            .With(new BiologicalParentChild(raymond.Id, eleanor.Id))
            .With(Married(susan, raymond, 1970));

        var result = await CreateService().GetForPersonAsync(eleanor.Id);

        Assert.Empty(result.Value!.Candidates);
    }

    // The subject's own marriage is read (the stepchildren rows need it), so their
    // own spouse must not turn up as a candidate stepparent for them.
    [Fact]
    public async Task DoesNotOfferTheSubjectsOwnSpouse()
    {
        var home = Household();
        var danielsWife = Someone("Ellen", "Reid");
        _tree.With(danielsWife).With(Married(home.Child, danielsWife, 1990));

        var result = await CreateService().GetForPersonAsync(home.Child.Id);

        Assert.DoesNotContain(danielsWife.Id, result.Value!.Candidates.Select(c => c.Person.Id));
    }

    // A phantom stands for an ancestor nobody has identified, so it cannot be
    // offered as somebody a family would name as a stepparent.
    [Fact]
    public async Task DoesNotOfferAPhantom()
    {
        var daniel = Someone("Daniel");
        var arthur = Someone("Arthur");
        var phantom = Person.CreatePhantom();
        _tree.With(daniel, arthur, phantom)
            .With(new BiologicalParentChild(arthur.Id, daniel.Id))
            .With(Married(arthur, phantom, 1976));

        var result = await CreateService().GetForPersonAsync(daniel.Id);

        Assert.Empty(result.Value!.Candidates);
    }

    // Two of the child's parents married the same person over a lifetime. The
    // label records a pair, so a second offer would be a second way to write the
    // same fact and the duplicate guard would refuse it.
    [Fact]
    public async Task OffersEachCandidateOnceEvenWhenTwoParentsMarriedThem()
    {
        var daniel = Someone("Daniel");
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var vera = Someone("Vera");
        _tree.With(daniel, arthur, margaret, vera)
            .With(new BiologicalParentChild(arthur.Id, daniel.Id))
            .With(new BiologicalParentChild(margaret.Id, daniel.Id))
            .With(Married(arthur, vera, 1976))
            .With(Married(margaret, vera, 1995));

        var result = await CreateService().GetForPersonAsync(daniel.Id);

        Assert.Single(result.Value!.Candidates);
    }

    [Fact]
    public async Task OffersNobodyWhenTheChildHasNoParents()
    {
        var daniel = Someone("Daniel");
        _tree.With(daniel);

        var result = await CreateService().GetForPersonAsync(daniel.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value!.Candidates);
    }

    // ---- Removing a label (US-038) ----

    [Fact]
    public async Task RemovesALabelAndLeavesTheMarriageAlone()
    {
        var home = Household();
        var label = await CreateService().LabelAsync(
            home.Child.Id, home.Stepparent.Id, home.Marriage.Id);

        var result = await CreateService().RemoveAsync(label.Value);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(_tree.StepparentLinks);
        Assert.Single(_tree.Marriages);
        Assert.Equal(3, _tree.People.Count);
    }

    [Fact]
    public async Task ReportsAMissingLabelOnRemove()
    {
        var result = await CreateService().RemoveAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Contains("no longer recorded", result.Error);
    }

    // US-038's last criterion, from this side: removing the marriage removes the
    // label, and the child's profile stops showing it.
    [Fact]
    public async Task RemovingTheMarriageRemovesTheLabel()
    {
        var home = Household();
        await CreateService().LabelAsync(home.Child.Id, home.Stepparent.Id, home.Marriage.Id);

        await CreateMarriageService().RemoveAsync(home.Marriage.Id);

        var result = await CreateService().GetForPersonAsync(home.Child.Id);

        Assert.Empty(result.Value!.Stepparents);
        Assert.Empty(_tree.StepparentLinks);
    }
}
