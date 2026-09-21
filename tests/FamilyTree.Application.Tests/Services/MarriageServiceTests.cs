using FamilyTree.Application.Common;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Application.Tests.Services;

/// <summary>
/// The rules a <see cref="Marriage"/> cannot enforce for itself: active
/// duplicates, overlaps, the end-after-start rule, and what happens to the
/// stepparent labels resting on a record that is removed or replaced.
/// </summary>
public class MarriageServiceTests
{
    private readonly InMemoryTree _tree = new();

    private MarriageService CreateService() => new(
        _tree.PersonRepository,
        _tree.MarriageRepository);

    private static Person Someone(
        string first, string last = "Whitfield", int? birthYear = null, int? deathYear = null)
    {
        var person = new Person(first, last, Gender.Unknown);
        if (birthYear is not null || deathYear is not null)
        {
            person.UpdateDates(
                birthYear is int b ? PartialDate.FromYear(b) : null,
                null,
                deathYear is int d ? PartialDate.FromYear(d) : null,
                null);
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

    // ---- Recording a marriage (US-021, US-044) ----

    [Fact]
    public async Task RecordsAMarriage()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret);

        var result = await CreateService().AddAsync(
            arthur.Id, margaret.Id, PartialDate.FromYear(1946), "Leeds");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(_tree.Marriages);
        Assert.Equal("Leeds", _tree.Marriages[0].StartPlace);
    }

    // US-021: one record serves both profiles, which is why nothing here writes a
    // reciprocal row that could fall out of step.
    [Fact]
    public async Task OneRecordAppearsOnBothProfiles()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret);

        await CreateService().AddAsync(arthur.Id, margaret.Id, PartialDate.FromYear(1946));

        var his = await CreateService().GetForPersonAsync(arthur.Id);
        var hers = await CreateService().GetForPersonAsync(margaret.Id);

        Assert.Single(his.Value!.Marriages);
        Assert.Single(hers.Value!.Marriages);
        Assert.Single(_tree.Marriages);
    }

    // US-022: each profile shows the spouse the reader is not looking at.
    [Fact]
    public async Task EachProfileShowsTheOtherSpouse()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret).With(Married(arthur, margaret, 1946));

        var his = await CreateService().GetForPersonAsync(arthur.Id);

        Assert.Equal(margaret.Id, his.Value!.Marriages[0].Spouse.Id);
    }

    // US-044: no gender constraint on either spouse, in the type or the rules.
    [Fact]
    public async Task RecordsAMarriageBetweenTwoPeopleOfTheSameGender()
    {
        var one = new Person("Ellen", "Reid", Gender.Female);
        var two = new Person("Nadia", "Reid", Gender.Female);
        _tree.With(one, two);

        var result = await CreateService().AddAsync(one.Id, two.Id, PartialDate.FromYear(2015));

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task RejectsAMarriageWithNoStartDate()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret);

        var result = await CreateService().AddAsync(arthur.Id, margaret.Id, null!);

        Assert.False(result.IsSuccess);
        Assert.Contains("start date", result.Error);
        Assert.Empty(_tree.Marriages);
    }

    [Fact]
    public async Task RejectsSelfMarriage()
    {
        var arthur = Someone("Arthur");
        _tree.With(arthur);

        var result = await CreateService().AddAsync(arthur.Id, arthur.Id, PartialDate.FromYear(1946));

        Assert.False(result.IsSuccess);
        Assert.Contains("cannot marry themselves", result.Error);
    }

    // Emptiness is reported as emptiness. Two empty ids are equal, so without this
    // the self-marriage rule would claim somebody married themselves when in fact
    // nobody was chosen.
    [Fact]
    public async Task RejectsAMarriageWithNobodyChosen()
    {
        var result = await CreateService().AddAsync(
            Guid.Empty, Guid.Empty, PartialDate.FromYear(1946));

        Assert.False(result.IsSuccess);
        Assert.Contains("Both people must be chosen", result.Error);
    }

    [Fact]
    public async Task RejectsAMarriageToSomebodyWhoIsNotInTheTree()
    {
        var arthur = Someone("Arthur");
        _tree.With(arthur);

        var result = await CreateService().AddAsync(
            arthur.Id, Guid.NewGuid(), PartialDate.FromYear(1946));

        Assert.False(result.IsSuccess);
        Assert.Contains("No person with id", result.Error);
    }

    [Fact]
    public async Task RejectsASecondCurrentMarriageBetweenTheSameTwoPeople()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret).With(Married(arthur, margaret, 1946));

        var result = await CreateService().AddAsync(
            arthur.Id, margaret.Id, PartialDate.FromYear(1950));

        Assert.False(result.IsSuccess);
        Assert.Contains("already have a current marriage", result.Error);
        Assert.Single(_tree.Marriages);
    }

    // The duplicate rule is about *active* marriages, not about pairs. Remarrying
    // somebody after a divorce is a real thing, and a rule that refused it would
    // make the app wrong about those families.
    [Fact]
    public async Task AllowsRemarryingTheSamePersonAfterTheFirstMarriageEnded()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret)
            .With(Married(arthur, margaret, 1946, 1960, MarriageEndReason.Divorce));

        var result = await CreateService().AddAsync(
            arthur.Id, margaret.Id, PartialDate.FromYear(1965));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, _tree.Marriages.Count);
    }

    // The same rule from the other side: the duplicate check must look at the
    // pair, not merely at whether this person is married to anybody.
    [Fact]
    public async Task AllowsASecondMarriageToADifferentPerson()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var vera = Someone("Vera");
        _tree.With(arthur, margaret, vera)
            .With(Married(arthur, margaret, 1946, 1973, MarriageEndReason.DeathOfSpouse));

        var result = await CreateService().AddAsync(arthur.Id, vera.Id, PartialDate.FromYear(1976));

        Assert.True(result.IsSuccess, result.Error);
    }

    // US-021 forbids a second *active* marriage, not a second marriage. A couple
    // married now may turn out to have married and divorced earlier, and refusing
    // that record told them to end a marriage that is not over.
    [Fact]
    public async Task AllowsAnEarlierEndedMarriageForACoupleWhoAreMarriedNow()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret).With(Married(arthur, margaret, 1990));

        var result = await CreateService().AddAsync(
            arthur.Id, margaret.Id, PartialDate.FromYear(1970),
            endDate: PartialDate.FromYear(1975), endReason: MarriageEndReason.Divorce);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, _tree.Marriages.Count);
    }

    // ---- Reopening an ended marriage (US-021 via the edit path) ----

    // The hole the replace path's docstring names, in the one path that did not
    // run the guard: clearing the end date on an old record while a current one
    // exists left two current marriages between the same pair, two "Current"
    // badges, and Current reporting neither.
    [Fact]
    public async Task RefusesToReopenAnEndedMarriageWhileAnotherIsCurrent()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var ended = Married(arthur, margaret, 1970, 1975, MarriageEndReason.Divorce);
        _tree.With(arthur, margaret).With(ended).With(Married(arthur, margaret, 1990));

        var result = await CreateService().UpdateAsync(
            ended.Id, PartialDate.FromYear(1970), null, null, null,
            RelationshipCertainty.Confirmed);

        Assert.False(result.IsSuccess);
        Assert.Contains("already have a current marriage", result.Error);
        Assert.False(_tree.Marriages.Single(m => m.Id == ended.Id).IsOngoing);
    }

    [Fact]
    public async Task AllowsReopeningAnEndedMarriageWhenNoOtherIsCurrent()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var ended = Married(arthur, margaret, 1970, 1975, MarriageEndReason.Divorce);
        _tree.With(arthur, margaret).With(ended);

        var result = await CreateService().UpdateAsync(
            ended.Id, PartialDate.FromYear(1970), null, null, null,
            RelationshipCertainty.Confirmed);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(_tree.Marriages.Single().IsOngoing);
    }

    // Editing an ended record's dates is not reopening it, so the duplicate rule
    // must not fire — otherwise correcting a divorce year would be refused because
    // the couple remarried.
    [Fact]
    public async Task AllowsEditingAnEndedMarriageWhileAnotherIsCurrent()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var ended = Married(arthur, margaret, 1970, 1975, MarriageEndReason.Divorce);
        _tree.With(arthur, margaret).With(ended).With(Married(arthur, margaret, 1990));

        var result = await CreateService().UpdateAsync(
            ended.Id, PartialDate.FromYear(1970), null,
            PartialDate.FromYear(1976), MarriageEndReason.Divorce,
            RelationshipCertainty.Confirmed);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1976, _tree.Marriages.Single(m => m.Id == ended.Id).EndDate!.Year);
    }

    // Ending a marriage must keep working when a spouse's record has since been
    // deleted — it is part of how somebody cleans that up — so the edit path does
    // not inherit the add path's existence checks.
    [Fact]
    public async Task EndsAMarriageWhoseSpouseRecordIsGone()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var marriage = Married(arthur, margaret, 1946);
        _tree.With(arthur).With(marriage);

        var result = await CreateService().UpdateAsync(
            marriage.Id, PartialDate.FromYear(1946), null,
            PartialDate.FromYear(1973), MarriageEndReason.DeathOfSpouse,
            RelationshipCertainty.Confirmed);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1973, _tree.Marriages.Single().EndDate!.Year);
    }

    // ---- The end-after-start rule (US-021, US-023, US-025) ----

    [Fact]
    public async Task RejectsAMarriageThatEndsBeforeItStarts()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret);

        var result = await CreateService().AddAsync(
            arthur.Id, margaret.Id, PartialDate.FromYear(1946),
            endDate: PartialDate.FromYear(1940), endReason: MarriageEndReason.Divorce);

        Assert.False(result.IsSuccess);
        Assert.Contains("cannot end on 1940", result.Error);
        Assert.Empty(_tree.Marriages);
    }

    // Year-only leniency is not licence to accept a January end on a June start.
    // The year comparison alone did, against US-023 and US-025's "on or after" —
    // and ImportService compares these in full, so the record tripped its warning
    // on its own round trip.
    [Fact]
    public async Task RejectsAMarriageThatEndsEarlierInTheYearThanItStarted()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret);

        var result = await CreateService().AddAsync(
            arthur.Id, margaret.Id, PartialDate.FromYearMonthDay(1970, 6, 15),
            endDate: PartialDate.FromYearMonthDay(1970, 1, 3),
            endReason: MarriageEndReason.Annulment);

        Assert.False(result.IsSuccess);
        Assert.Empty(_tree.Marriages);
    }

    [Fact]
    public async Task AcceptsAMarriageThatEndsLaterInTheYearThanItStarted()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret);

        var result = await CreateService().AddAsync(
            arthur.Id, margaret.Id, PartialDate.FromYearMonthDay(1970, 1, 3),
            endDate: PartialDate.FromYearMonthDay(1970, 6, 15),
            endReason: MarriageEndReason.Annulment);

        Assert.True(result.IsSuccess, result.Error);
    }

    // The leniency itself: a year-only pair says nothing about the months, so
    // same-year stays acceptable rather than being refused on a guess.
    [Fact]
    public async Task AcceptsASameYearPairWhenOnlyOneSideCarriesAMonth()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret);

        var result = await CreateService().AddAsync(
            arthur.Id, margaret.Id, PartialDate.FromYearMonth(1970, 6),
            endDate: PartialDate.FromYear(1970), endReason: MarriageEndReason.Annulment);

        Assert.True(result.IsSuccess, result.Error);
    }

    // Same-year is allowed: an annulment within months of the wedding is ordinary,
    // and both dates may be year-only.
    [Fact]
    public async Task AcceptsAMarriageThatEndsInTheYearItStarted()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret);

        var result = await CreateService().AddAsync(
            arthur.Id, margaret.Id, PartialDate.FromYear(1946),
            endDate: PartialDate.FromYear(1946), endReason: MarriageEndReason.Annulment);

        Assert.True(result.IsSuccess, result.Error);
    }

    // ---- Overlap warnings (US-042) ----

    [Fact]
    public async Task WarnsWhenTwoMarriagesOverlapButSavesAnyway()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var vera = Someone("Vera");
        _tree.With(arthur, margaret, vera).With(Married(arthur, margaret, 1946));

        var result = await CreateService().AddAsync(arthur.Id, vera.Id, PartialDate.FromYear(1976));

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.IsWarning);
        Assert.Contains("overlaps", result.Warning);
        Assert.Equal(2, _tree.Marriages.Count);
    }

    [Fact]
    public async Task DoesNotWarnWhenTheFirstMarriageEndedBeforeTheSecondBegan()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var vera = Someone("Vera");
        _tree.With(arthur, margaret, vera)
            .With(Married(arthur, margaret, 1946, 1973, MarriageEndReason.DeathOfSpouse));

        var result = await CreateService().AddAsync(arthur.Id, vera.Id, PartialDate.FromYear(1976));

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.IsWarning);
    }

    // A marriage recorded before either spouse was born is worth saying and not
    // worth blocking: the wrong record may be the birth date.
    [Fact]
    public async Task WarnsWhenTheMarriageIsDatedBeforeASpouseWasBorn()
    {
        var arthur = Someone("Arthur", birthYear: 1918);
        var margaret = Someone("Margaret", birthYear: 1921);
        _tree.With(arthur, margaret);

        var result = await CreateService().AddAsync(
            arthur.Id, margaret.Id, PartialDate.FromYear(1910));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("before Arthur Whitfield was born", result.Warning);
        Assert.Single(_tree.Marriages);
    }

    [Fact]
    public async Task WarnsWhenTheMarriageIsDatedAfterASpouseDied()
    {
        var arthur = Someone("Arthur", birthYear: 1918, deathYear: 1991);
        var vera = Someone("Vera", birthYear: 1930);
        _tree.With(arthur, vera);

        var result = await CreateService().AddAsync(
            arthur.Id, vera.Id, PartialDate.FromYear(1995));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("after Arthur Whitfield died in 1991", result.Warning);
    }

    // An end date after a death is what "Death of spouse" means, so only the start
    // is impossible after one.
    [Fact]
    public async Task DoesNotWarnWhenTheMarriageEndedInTheYearASpouseDied()
    {
        var arthur = Someone("Arthur", birthYear: 1918, deathYear: 1991);
        var margaret = Someone("Margaret", birthYear: 1921, deathYear: 1973);
        _tree.With(arthur, margaret);

        var result = await CreateService().AddAsync(
            arthur.Id, margaret.Id, PartialDate.FromYear(1946),
            endDate: PartialDate.FromYear(1973), endReason: MarriageEndReason.DeathOfSpouse);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.IsWarning);
    }

    // ---- Ordering (US-022, US-042) ----

    [Fact]
    public async Task OrdersMarriagesChronologically()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var vera = Someone("Vera");
        _tree.With(arthur, margaret, vera)
            .With(Married(arthur, vera, 1976))
            .With(Married(arthur, margaret, 1946, 1973, MarriageEndReason.DeathOfSpouse));

        var result = await CreateService().GetForPersonAsync(arthur.Id);

        Assert.Equal(
            ["Margaret Whitfield", "Vera Whitfield"],
            result.Value!.Marriages.Select(m => m.Spouse.DisplayName));
    }

    // The current marriage is distinguished but not floated: a marital history
    // reads as a sequence, and putting the present first would put it before the
    // past on the one section ordered by when things happened.
    [Fact]
    public async Task DoesNotFloatTheCurrentMarriageToTheTop()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var vera = Someone("Vera");
        _tree.With(arthur, margaret, vera)
            .With(Married(arthur, margaret, 1946, 1973, MarriageEndReason.DeathOfSpouse))
            .With(Married(arthur, vera, 1976));

        var result = await CreateService().GetForPersonAsync(arthur.Id);

        Assert.False(result.Value!.Marriages[0].IsOngoing);
        Assert.True(result.Value!.Marriages[1].IsOngoing);
        Assert.Equal(vera.Id, result.Value!.Current!.Spouse.Id);
    }

    // Two ongoing marriages is a reachable state (US-042 warns and saves), and
    // nominating one of them as *the* current marriage would assert something
    // nobody recorded.
    [Fact]
    public async Task ReportsNoCurrentMarriageWhenTwoAreOngoing()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var vera = Someone("Vera");
        _tree.With(arthur, margaret, vera)
            .With(Married(arthur, margaret, 1946))
            .With(Married(arthur, vera, 1976));

        var result = await CreateService().GetForPersonAsync(arthur.Id);

        Assert.Null(result.Value!.Current);
    }

    // A marriage can outlive the person it names, since deleting a person does not
    // sweep their relationships. A row with a blank name would be worse than an
    // omitted one.
    [Fact]
    public async Task OmitsAMarriageWhoseSpouseIsGone()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur).With(Married(arthur, margaret, 1946));

        var result = await CreateService().GetForPersonAsync(arthur.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value!.Marriages);
    }

    [Fact]
    public async Task ReportsTheNotFoundCase()
    {
        var result = await CreateService().GetForPersonAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Contains("No person with id", result.Error);
    }

    // ---- Editing and ending (US-023, US-025, US-026, US-027) ----

    [Fact]
    public async Task EndsAMarriageByDivorce()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var marriage = Married(arthur, margaret, 1946);
        _tree.With(arthur, margaret).With(marriage);

        var result = await CreateService().UpdateAsync(
            marriage.Id, PartialDate.FromYear(1946), null,
            PartialDate.FromYear(1960), MarriageEndReason.Divorce,
            RelationshipCertainty.Confirmed);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1960, _tree.Marriages[0].EndDate!.Year);
        Assert.Equal(MarriageEndReason.Divorce, _tree.Marriages[0].EndReason);
    }

    // Ending a marriage keeps the record's identity, which is what makes the
    // stepparent labels resting on it survive. A replace would have taken them.
    [Fact]
    public async Task EndingAMarriageKeepsItsIdAndItsStepparentLabels()
    {
        var arthur = Someone("Arthur");
        var vera = Someone("Vera");
        var daniel = Someone("Daniel");
        var marriage = Married(arthur, vera, 1976);
        _tree.With(arthur, vera, daniel)
            .With(marriage)
            .With(new StepparentRelationship(vera.Id, daniel.Id, marriage.Id));

        await CreateService().UpdateAsync(
            marriage.Id, PartialDate.FromYear(1976), null,
            PartialDate.FromYear(1990), MarriageEndReason.Divorce,
            RelationshipCertainty.Confirmed);

        Assert.Equal(marriage.Id, _tree.Marriages[0].Id);
        Assert.Single(_tree.StepparentLinks);
    }

    [Fact]
    public async Task RejectsAnEndDateBeforeTheStartOnEdit()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var marriage = Married(arthur, margaret, 1946);
        _tree.With(arthur, margaret).With(marriage);

        var result = await CreateService().UpdateAsync(
            marriage.Id, PartialDate.FromYear(1946), null,
            PartialDate.FromYear(1930), MarriageEndReason.Divorce,
            RelationshipCertainty.Confirmed);

        Assert.False(result.IsSuccess);
        Assert.Null(_tree.Marriages[0].EndDate);
    }

    [Fact]
    public async Task ReportsAMissingMarriageOnEdit()
    {
        var result = await CreateService().UpdateAsync(
            Guid.NewGuid(), PartialDate.FromYear(1946), null, null, null,
            RelationshipCertainty.Confirmed);

        Assert.False(result.IsSuccess);
        Assert.Contains("no longer recorded", result.Error);
    }

    // ---- Replacing a spouse (US-023) ----

    [Fact]
    public async Task ReplacesTheRecordedSpouse()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var vera = Someone("Vera");
        var marriage = Married(arthur, margaret, 1946);
        _tree.With(arthur, margaret, vera).With(marriage);

        var result = await CreateService().ReplaceSpouseAsync(
            marriage.Id, arthur.Id, vera.Id, PartialDate.FromYear(1946), "Leeds");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(_tree.Marriages);
        Assert.Equal(vera.Id, _tree.Marriages[0].Spouse2Id);
        Assert.Equal("Leeds", _tree.Marriages[0].StartPlace);
    }

    // One repository call, not a delete and an add: the two-step version can
    // half-apply and leave the marriage gone from both profiles with nothing in
    // its place.
    [Fact]
    public async Task ReplacesTheSpouseInOneCall()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var vera = Someone("Vera");
        var marriage = Married(arthur, margaret, 1946);
        _tree.With(arthur, margaret, vera).With(marriage);

        await CreateService().ReplaceSpouseAsync(
            marriage.Id, arthur.Id, vera.Id, PartialDate.FromYear(1946));

        Assert.Equal(1, _tree.MarriageReplaceCount);
    }

    // The labels rested on a marriage record that no longer exists, so they go —
    // and the caller is told, because a label vanishing unannounced is the silent
    // loss this project treats as a bug.
    [Fact]
    public async Task ReplacingASpouseRemovesTheStepparentLabelsAndSaysSo()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var vera = Someone("Vera");
        var daniel = Someone("Daniel");
        var marriage = Married(arthur, margaret, 1946);
        _tree.With(arthur, margaret, vera, daniel)
            .With(marriage)
            .With(new StepparentRelationship(margaret.Id, daniel.Id, marriage.Id));

        var result = await CreateService().ReplaceSpouseAsync(
            marriage.Id, arthur.Id, vera.Id, PartialDate.FromYear(1946));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(_tree.StepparentLinks);
        Assert.Contains("1 stepparent label", result.Warning);
    }

    [Fact]
    public async Task RejectsReplacingASpouseWithThemselves()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var marriage = Married(arthur, margaret, 1946);
        _tree.With(arthur, margaret).With(marriage);

        var result = await CreateService().ReplaceSpouseAsync(
            marriage.Id, arthur.Id, margaret.Id, PartialDate.FromYear(1946));

        Assert.False(result.IsSuccess);
        Assert.Contains("already the recorded spouse", result.Error);
    }

    // A marriage id arriving from a stale page, naming a person it does not
    // involve. Refused rather than applied to whichever spouse happened to be
    // first.
    [Fact]
    public async Task RejectsReplacingOnAMarriageThatDoesNotInvolveThePerson()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var stranger = Someone("Nadia", "Reid");
        var marriage = Married(arthur, margaret, 1946);
        _tree.With(arthur, margaret, stranger).With(marriage);

        var result = await CreateService().ReplaceSpouseAsync(
            marriage.Id, stranger.Id, arthur.Id, PartialDate.FromYear(1946));

        Assert.False(result.IsSuccess);
        Assert.Contains("does not involve that person", result.Error);
    }

    // The replace is judged by the same rules as a fresh record, less the one it is
    // replacing. Without that, a correction would be a hole through the duplicate
    // guard.
    [Fact]
    public async Task RejectsAReplacementThatWouldDuplicateACurrentMarriage()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var vera = Someone("Vera");
        var toCorrect = Married(arthur, margaret, 1946);
        _tree.With(arthur, margaret, vera)
            .With(toCorrect)
            .With(Married(arthur, vera, 1976));

        var result = await CreateService().ReplaceSpouseAsync(
            toCorrect.Id, arthur.Id, vera.Id, PartialDate.FromYear(1946));

        Assert.False(result.IsSuccess);
        Assert.Contains("already have a current marriage", result.Error);
    }

    // ---- Removing (US-024, US-038) ----

    [Fact]
    public async Task RemovesAMarriageWithoutDeletingEitherPerson()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var marriage = Married(arthur, margaret, 1946);
        _tree.With(arthur, margaret).With(marriage);

        var result = await CreateService().RemoveAsync(marriage.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(_tree.Marriages);
        Assert.Equal(2, _tree.People.Count);
    }

    // US-038's last criterion. The cascade is the repository's; this is the report
    // of it, because the label may be on a third person's profile and nothing else
    // would connect its disappearance to the marriage just removed.
    [Fact]
    public async Task RemovingAMarriageRemovesItsStepparentLabelsAndSaysSo()
    {
        var arthur = Someone("Arthur");
        var vera = Someone("Vera");
        var daniel = Someone("Daniel");
        var marriage = Married(arthur, vera, 1976);
        _tree.With(arthur, vera, daniel)
            .With(marriage)
            .With(new StepparentRelationship(vera.Id, daniel.Id, marriage.Id));

        var result = await CreateService().RemoveAsync(marriage.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(_tree.StepparentLinks);
        Assert.True(result.IsWarning);
        Assert.Contains("1 stepparent label", result.Warning);
    }

    [Fact]
    public async Task RemovingAMarriageWithNoLabelsSaysNothing()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var marriage = Married(arthur, margaret, 1946);
        _tree.With(arthur, margaret).With(marriage);

        var result = await CreateService().RemoveAsync(marriage.Id);

        Assert.False(result.IsWarning);
    }

    [Fact]
    public async Task ReportsAMissingMarriageOnRemove()
    {
        var result = await CreateService().RemoveAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Contains("no longer recorded", result.Error);
    }

    // Labels resting on a *different* marriage are untouched — the cascade is keyed
    // on the record being removed, not on the people in it.
    [Fact]
    public async Task LeavesLabelsRestingOnAnotherMarriageAlone()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        var vera = Someone("Vera");
        var daniel = Someone("Daniel");
        var first = Married(arthur, margaret, 1946, 1973, MarriageEndReason.DeathOfSpouse);
        var second = Married(arthur, vera, 1976);
        _tree.With(arthur, margaret, vera, daniel)
            .With(first)
            .With(second)
            .With(new StepparentRelationship(vera.Id, daniel.Id, second.Id));

        await CreateService().RemoveAsync(first.Id);

        Assert.Single(_tree.StepparentLinks);
    }

    // ---- The end-date suggestion (US-026) ----

    [Fact]
    public async Task SuggestsTheEndDateFromASpousesDeath()
    {
        var arthur = Someone("Arthur", birthYear: 1918, deathYear: 1991);
        var margaret = Someone("Margaret", birthYear: 1921, deathYear: 1973);
        _tree.With(arthur, margaret);

        var result = await CreateService().SuggestEndDateFromSpouseDeathAsync(arthur.Id, margaret.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1973, result.Value!.Date.Year);
        Assert.Equal("Margaret Whitfield", result.Value!.PersonName);
    }

    // Either spouse may be the one who died, and the marriage ended at the first
    // death whichever it was.
    [Fact]
    public async Task SuggestsTheEarlierOfTwoDeaths()
    {
        var arthur = Someone("Arthur", birthYear: 1918, deathYear: 1991);
        var vera = Someone("Vera", birthYear: 1930, deathYear: 2011);
        _tree.With(arthur, vera);

        var result = await CreateService().SuggestEndDateFromSpouseDeathAsync(vera.Id, arthur.Id);

        Assert.Equal(1991, result.Value!.Date.Year);
        Assert.Equal("Arthur Whitfield", result.Value!.PersonName);
    }

    [Fact]
    public async Task SuggestsNothingWhenNeitherSpouseHasADeathDate()
    {
        var arthur = Someone("Arthur", birthYear: 1918);
        var vera = Someone("Vera", birthYear: 1930);
        _tree.With(arthur, vera);

        var result = await CreateService().SuggestEndDateFromSpouseDeathAsync(arthur.Id, vera.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value);
    }

    // ---- The review notice (US-026's third criterion) ----

    [Fact]
    public async Task AsksForAReviewWhenADeathDateArrivesOnAnOngoingMarriage()
    {
        var arthur = Someone("Arthur", birthYear: 1918, deathYear: 1991);
        var vera = Someone("Vera", birthYear: 1930);
        _tree.With(arthur, vera).With(Married(arthur, vera, 1976));

        var review = await CreateService().DescribeEndDateReviewAsync(arthur.Id);

        Assert.NotNull(review);
        Assert.Contains("1991", review);
        Assert.Contains("end date reviewed", review);
    }

    [Fact]
    public async Task AsksForAReviewWhenADeathDateDisagreesWithARecordedWidowhood()
    {
        var arthur = Someone("Arthur", birthYear: 1918, deathYear: 1991);
        var margaret = Someone("Margaret", birthYear: 1921, deathYear: 1973);
        _tree.With(arthur, margaret)
            .With(Married(arthur, margaret, 1946, 1960, MarriageEndReason.DeathOfSpouse));

        var review = await CreateService().DescribeEndDateReviewAsync(margaret.Id);

        Assert.NotNull(review);
    }

    [Fact]
    public async Task SaysNothingWhenTheWidowhoodAlreadyMatchesTheDeathYear()
    {
        var arthur = Someone("Arthur", birthYear: 1918, deathYear: 1991);
        var margaret = Someone("Margaret", birthYear: 1921, deathYear: 1973);
        _tree.With(arthur, margaret)
            .With(Married(arthur, margaret, 1946, 1973, MarriageEndReason.DeathOfSpouse));

        var review = await CreateService().DescribeEndDateReviewAsync(margaret.Id);

        Assert.Null(review);
    }

    // A divorce in 1960 is not called into question by a death in 1991, so the
    // ordinary edit raises no noise.
    [Fact]
    public async Task SaysNothingAboutAMarriageThatEndedForAnotherReason()
    {
        var arthur = Someone("Arthur", birthYear: 1918, deathYear: 1991);
        var margaret = Someone("Margaret", birthYear: 1921);
        _tree.With(arthur, margaret)
            .With(Married(arthur, margaret, 1946, 1960, MarriageEndReason.Divorce));

        var review = await CreateService().DescribeEndDateReviewAsync(arthur.Id);

        Assert.Null(review);
    }

    [Fact]
    public async Task SaysNothingWhenThePersonHasNoDeathDate()
    {
        var arthur = Someone("Arthur", birthYear: 1918);
        var vera = Someone("Vera", birthYear: 1930);
        _tree.With(arthur, vera).With(Married(arthur, vera, 1976));

        var review = await CreateService().DescribeEndDateReviewAsync(arthur.Id);

        Assert.Null(review);
    }

    // ---- The row's wording (US-022, US-025, US-026, US-027) ----

    [Theory]
    [InlineData(MarriageEndReason.Divorce, "Divorced")]
    [InlineData(MarriageEndReason.DeathOfSpouse, "Widowed")]
    [InlineData(MarriageEndReason.Annulment, "Annulled")]
    [InlineData(MarriageEndReason.Separation, "Separated")]
    [InlineData(MarriageEndReason.Unknown, "Ended")]
    public async Task WordsHowTheMarriageEnded(MarriageEndReason reason, string expected)
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret).With(Married(arthur, margaret, 1946, 1973, reason));

        var result = await CreateService().GetForPersonAsync(arthur.Id);

        Assert.Equal($"1946 – 1973 ({expected})", result.Value!.Marriages[0].DatesLabel);
    }

    [Fact]
    public async Task SaysOngoingWhenTheMarriageHasNotEnded()
    {
        var arthur = Someone("Arthur");
        var vera = Someone("Vera");
        _tree.With(arthur, vera).With(Married(arthur, vera, 1976));

        var result = await CreateService().GetForPersonAsync(arthur.Id);

        Assert.Equal("1976 – Ongoing", result.Value!.Marriages[0].DatesLabel);
    }

    // A marriage that ended without a recorded date. "date unknown" rather than
    // "Ongoing", because something did say it ended.
    [Fact]
    public async Task SaysTheEndDateIsUnknownWhenOnlyTheReasonWasRecorded()
    {
        var arthur = Someone("Arthur");
        var margaret = Someone("Margaret");
        _tree.With(arthur, margaret)
            .With(Married(arthur, margaret, 1946, reason: MarriageEndReason.Divorce));

        var result = await CreateService().GetForPersonAsync(arthur.Id);

        Assert.Equal("1946 – date unknown (Divorced)", result.Value!.Marriages[0].DatesLabel);
    }
}
