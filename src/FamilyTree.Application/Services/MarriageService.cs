using FamilyTree.Application.Common;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.Repositories;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Application.Services;

/// <summary>
/// Marriages and partnerships. US-021 to US-027, US-042, US-044.
/// </summary>
/// <remarks>
/// A third sibling of the two parent-link services rather than an extension of
/// either, and it differs from them more than they differ from each other. A
/// marriage is symmetric — there is no parent end and child end, so there is
/// nothing to replace "from" and no cycle to create, and
/// <see cref="CircularReferenceChecker"/> has no business here: two people
/// marrying makes neither one's ancestor.
/// <para>
/// What it has instead is a lifetime. The same record is written when a marriage
/// is recorded, edited, and ended, and US-025 to US-027 are three wordings of one
/// operation: set an end date and a reason. That is why there is no
/// <c>EndMarriageAsync</c> — it would be <see cref="UpdateAsync"/> with two of
/// its arguments spelt out, and a second path to the same write is a second place
/// for the end-after-start rule to be forgotten.
/// </para>
/// <para>
/// Nothing here mentions gender, and that is the whole of US-044: there is no
/// husband or wife to assign, no constraint to check, and the DTO offers no field
/// a component could word as one.
/// </para>
/// </remarks>
public sealed class MarriageService(
    IPersonRepository people,
    IMarriageRepository marriages,
    IStepparentRelationshipRepository stepparents)
{
    /// <summary>
    /// Every marriage on one profile, chronologically, in a bounded number of
    /// reads. US-022, US-042.
    /// </summary>
    public async Task<Result<MarriagesDto>> GetForPersonAsync(
        Guid personId, CancellationToken ct = default)
    {
        var records = await marriages.GetForPersonAsync(personId, ct);

        // Both spouses of every marriage in one read, plus the subject. Resolving
        // a spouse per row is the N+1 the seam discipline forbids, and it would be
        // the most visible one in the app: a remarried ancestor with four records
        // is four requests for one section.
        var wanted = records
            .SelectMany(m => new[] { m.Spouse1Id, m.Spouse2Id })
            .Append(personId)
            .Distinct()
            .ToList();

        var byId = (await people.GetByIdsAsync(wanted, ct)).ToDictionary(p => p.Id);

        if (!byId.ContainsKey(personId))
        {
            return Result<MarriagesDto>.Failure($"No person with id {personId}.");
        }

        return Result<MarriagesDto>.Success(new MarriagesDto(Describe(records, personId, byId)));
    }

    /// <summary>
    /// Records a marriage between two people. US-021, US-042, US-044.
    /// </summary>
    /// <remarks>
    /// One record serves both profiles, so "appears on both spouses' profiles"
    /// (US-021) is structural rather than a second write this code has to
    /// remember — there is no reciprocal row that could fall out of step.
    /// </remarks>
    public async Task<Result<Guid>> AddAsync(
        Guid personId,
        Guid spouseId,
        PartialDate startDate,
        string? startPlace = null,
        PartialDate? endDate = null,
        MarriageEndReason? endReason = null,
        RelationshipCertainty certainty = RelationshipCertainty.Confirmed,
        CancellationToken ct = default)
    {
        if (startDate is null)
        {
            return Result<Guid>.Failure("A marriage needs a start date.");
        }

        var check = await CheckAddableAsync(personId, spouseId, ct);
        if (!check.IsSuccess)
        {
            return Result<Guid>.Failure(check.Error!);
        }

        if (EndsBeforeItStarts(startDate, endDate) is { } invalid)
        {
            return Result<Guid>.Failure(invalid);
        }

        var (person, spouse) = check.Value!;

        // Judged before the write, against what is stored now. Afterwards the new
        // record is itself in the set and would have to be excluded, which is the
        // kind of off-by-one that turns an overlap warning into a warning about
        // overlapping itself.
        var overlap = await DescribeOverlapAsync(personId, spouseId, startDate, endDate, ct);

        var marriage = new Marriage(personId, spouseId, startDate, startPlace, certainty);
        if (endDate is not null || endReason is not null)
        {
            marriage.UpdateDates(startDate, startPlace, endDate, endReason);
        }

        await marriages.AddAsync(marriage, ct);

        var problems = Problems(person, spouse, startDate, endDate);
        if (overlap is not null)
        {
            problems.Add(overlap);
        }

        return Warn(marriage.Id, problems);
    }

    /// <summary>
    /// Corrects the details of an existing marriage, including ending it.
    /// US-023, US-025, US-026, US-027.
    /// </summary>
    /// <remarks>
    /// The third correction shape in this app, and unlike the other two it touches
    /// no identity at all: the record keeps its id and both spouses, and only what
    /// is known about the marriage changes. Ending a marriage is this call with an
    /// end date and a reason, which is why divorce, widowhood and annulment need
    /// no code of their own — they differ in one enum value, and three methods
    /// would be three places to forget that the end cannot precede the start.
    /// </remarks>
    public async Task<Result> UpdateAsync(
        Guid marriageId,
        PartialDate startDate,
        string? startPlace,
        PartialDate? endDate,
        MarriageEndReason? endReason,
        RelationshipCertainty certainty,
        CancellationToken ct = default)
    {
        if (startDate is null)
        {
            return Result.Failure("A marriage needs a start date.");
        }

        var marriage = await marriages.GetByIdAsync(marriageId, ct);
        if (marriage is null)
        {
            return Result.Failure("That marriage is no longer recorded.");
        }

        if (EndsBeforeItStarts(startDate, endDate) is { } invalid)
        {
            return Result.Failure(invalid);
        }

        var overlap = await DescribeOverlapAsync(
            marriage.Spouse1Id, marriage.Spouse2Id, startDate, endDate, ct, ignoring: marriageId);

        marriage.UpdateDates(startDate, startPlace, endDate, endReason);
        marriage.UpdateCertainty(certainty);
        await marriages.UpdateAsync(marriage, ct);

        var found = (await people.GetByIdsAsync([marriage.Spouse1Id, marriage.Spouse2Id], ct))
            .ToDictionary(p => p.Id);

        // The people are read only to word a warning. If either is missing the
        // edit still stands, because it was applied to the marriage record — there
        // is simply nothing left to say about the dates.
        if (!found.TryGetValue(marriage.Spouse1Id, out var one)
            || !found.TryGetValue(marriage.Spouse2Id, out var two))
        {
            return overlap is null ? Result.Success() : Result.SuccessWithWarning(Sentence([overlap]));
        }

        var problems = Problems(one, two, startDate, endDate);
        if (overlap is not null)
        {
            problems.Add(overlap);
        }

        return problems.Count == 0 ? Result.Success() : Result.SuccessWithWarning(Sentence(problems));
    }

    /// <summary>
    /// Swaps the recorded spouse for somebody else. US-023.
    /// </summary>
    /// <remarks>
    /// One repository call, for the reason the other two replaces give: as a
    /// delete and an add it can half-apply, and what is left is a marriage that
    /// disappeared from both profiles with nothing recorded in its place.
    /// <para>
    /// The new record takes the details it is given rather than copying the old
    /// ones, because the one form that reaches this can change the spouse and the
    /// dates together (US-023 asks for both) — and a second write to fix up the
    /// dates afterwards is exactly the two-step this method exists to avoid. It
    /// does not keep the id: naming a different person is a different marriage
    /// rather than a corrected field.
    /// </para>
    /// <para>
    /// The stepparent labels that rested on the old record go with it, and the
    /// warning says how many: a label vanishing unannounced is the silent loss
    /// this project treats as a bug, and the alternative — re-pointing them at the
    /// new marriage — would assert that the new spouse is a stepparent to children
    /// nobody said they had any connection to.
    /// </para>
    /// </remarks>
    public async Task<Result<Guid>> ReplaceSpouseAsync(
        Guid marriageId,
        Guid personId,
        Guid newSpouseId,
        PartialDate startDate,
        string? startPlace = null,
        PartialDate? endDate = null,
        MarriageEndReason? endReason = null,
        RelationshipCertainty certainty = RelationshipCertainty.Confirmed,
        CancellationToken ct = default)
    {
        if (startDate is null)
        {
            return Result<Guid>.Failure("A marriage needs a start date.");
        }

        var marriage = await marriages.GetByIdAsync(marriageId, ct);
        if (marriage is null)
        {
            return Result<Guid>.Failure("That marriage is no longer recorded.");
        }

        var currentSpouseId = Other(marriage, personId);
        if (currentSpouseId == Guid.Empty)
        {
            return Result<Guid>.Failure("That marriage does not involve that person.");
        }

        if (currentSpouseId == newSpouseId)
        {
            return Result<Guid>.Failure("That person is already the recorded spouse.");
        }

        // Judged exactly as a fresh record would be, less the one being replaced:
        // the same self-reference rule and the same active-duplicate rule. A
        // correction that could produce a duplicate would be a hole straight
        // through the guard that stops one being added.
        var check = await CheckAddableAsync(personId, newSpouseId, ct, ignoring: marriageId);
        if (!check.IsSuccess)
        {
            return Result<Guid>.Failure(check.Error!);
        }

        if (EndsBeforeItStarts(startDate, endDate) is { } invalid)
        {
            return Result<Guid>.Failure(invalid);
        }

        var (person, spouse) = check.Value!;

        var overlap = await DescribeOverlapAsync(
            personId, newSpouseId, startDate, endDate, ct, ignoring: marriageId);

        var losing = (await stepparents.GetAllAsync(ct)).Count(s => s.MarriageId == marriageId);

        var replacement = new Marriage(personId, newSpouseId, startDate, startPlace, certainty);
        replacement.UpdateDates(startDate, startPlace, endDate, endReason);

        await marriages.ReplaceSpouseAsync(marriageId, replacement, ct);

        var problems = Problems(person, spouse, startDate, endDate);
        if (overlap is not null)
        {
            problems.Add(overlap);
        }

        // The lost labels are reported as their own sentence rather than folded
        // in with the date problems, because the two call for different things:
        // "Saved anyway — check the dates" is what a questionable year deserves
        // and is misleading about a record that is simply gone.
        var dates = problems.Count == 0 ? null : Sentence(problems);
        var lost = LabelsLost(losing, "when the record was replaced");

        return (dates, lost) switch
        {
            (null, null) => Result<Guid>.Success(replacement.Id),
            (null, { } only) => Result<Guid>.SuccessWithWarning(replacement.Id, only),
            ({ } only, null) => Result<Guid>.SuccessWithWarning(replacement.Id, only),
            var (both, also) => Result<Guid>.SuccessWithWarning(replacement.Id, $"{both} {also}"),
        };
    }

    /// <summary>
    /// How many stepparent labels went with a marriage record, in one sentence,
    /// or null when none did.
    /// </summary>
    /// <remarks>
    /// Shared by the replace and the remove so the two cannot word the same loss
    /// differently, and singular where it should be: a report that reads "1 label
    /// were removed" is the sort of thing that makes a user doubt the rest of it.
    /// </remarks>
    private static string? LabelsLost(int count, string fate) => count switch
    {
        <= 0 => null,
        1 => $"1 stepparent label rested on that marriage and was removed {fate}.",
        _ => $"{count} stepparent labels rested on that marriage and were removed {fate}.",
    };

    /// <summary>
    /// Deletes a marriage record, and the stepparent labels it justified.
    /// US-024, US-038.
    /// </summary>
    /// <remarks>
    /// Neither person is deleted, which is the point US-024 makes. The cascade is
    /// the repository's, in one transaction, so the labels cannot survive the
    /// marriage they were defined by — but it is reported here, because a step
    /// relationship disappearing from a third person's profile is not something
    /// the user would otherwise connect to the marriage they just removed.
    /// </remarks>
    public async Task<Result> RemoveAsync(Guid marriageId, CancellationToken ct = default)
    {
        var marriage = await marriages.GetByIdAsync(marriageId, ct);
        if (marriage is null)
        {
            return Result.Failure("That marriage is no longer recorded.");
        }

        var losing = (await stepparents.GetAllAsync(ct)).Count(s => s.MarriageId == marriageId);

        await marriages.DeleteAsync(marriageId, ct);

        return LabelsLost(losing, "with it") is { } lost
            ? Result.SuccessWithWarning(lost)
            : Result.Success();
    }

    /// <summary>
    /// The end date a death implies, when there is one to offer. US-026.
    /// </summary>
    /// <remarks>
    /// Takes both people rather than "the spouse", because either of them may be
    /// the one who died and the marriage ended at the first death whichever it
    /// was. The earlier date wins for that reason; the name is carried so the
    /// offer can say where the year came from, since a date filled in without
    /// explanation is a guess the user cannot check.
    /// <para>
    /// Keyed on people rather than on a marriage id so the same call serves the
    /// add form, where no marriage exists yet. It offers and does not write: the
    /// story asks for a suggestion, and a service that quietly dated a marriage
    /// from a death date would be inventing a fact about a family.
    /// </para>
    /// </remarks>
    public async Task<Result<EndDateSuggestion?>> SuggestEndDateFromSpouseDeathAsync(
        Guid personId, Guid spouseId, CancellationToken ct = default)
    {
        var found = await people.GetByIdsAsync([personId, spouseId], ct);

        var earliest = found
            .Where(p => p.DeathDate is not null)
            .OrderBy(p => p.DeathDate)
            .FirstOrDefault();

        return Result<EndDateSuggestion?>.Success(earliest is null
            ? null
            : new EndDateSuggestion(earliest.DeathDate!, Name(earliest)));
    }

    /// <summary>
    /// Whether a changed death date leaves a marriage end date worth revisiting.
    /// US-026.
    /// </summary>
    /// <remarks>
    /// The story's third criterion: editing somebody's death date later must
    /// prompt a review of the marriages that ended with it. Reported as a sentence
    /// for the caller to surface rather than written through, because the app does
    /// not know which is right — the marriage may genuinely have ended earlier,
    /// and quietly rewriting a recorded end date would overwrite research with an
    /// inference.
    /// <para>
    /// Deliberately silent when there is nothing to say, so the ordinary case of
    /// editing a person raises no noise.
    /// </para>
    /// </remarks>
    public async Task<string?> DescribeEndDateReviewAsync(
        Guid personId, CancellationToken ct = default)
    {
        var person = await people.GetByIdAsync(personId, ct);
        if (person?.DeathDate is not { } death)
        {
            return null;
        }

        var theirs = await marriages.GetForPersonAsync(personId, ct);

        // Ongoing marriages are included, and are the most useful case: somebody
        // recorded as dead whose marriage never ended is a record that now
        // disagrees with itself. Marriages ended for another reason are not —
        // a divorce in 1970 is not called into question by a death in 1998.
        var affected = theirs
            .Where(m => m.IsOngoing
                || (m.EndReason == MarriageEndReason.DeathOfSpouse
                    && (m.EndDate is null || m.EndDate.Year != death.Year)))
            .ToList();

        if (affected.Count == 0)
        {
            return null;
        }

        return $"{Name(person)} is now recorded as dying in {death.Year}. "
            + $"{(affected.Count == 1 ? "A marriage" : $"{affected.Count} marriages")} on "
            + "this profile may need the end date reviewed.";
    }

    /// <summary>The two people, both confirmed to exist.</summary>
    private sealed record Addable(Person Person, Person Spouse);

    /// <summary>
    /// Every rule standing between a proposed marriage and the store.
    /// </summary>
    /// <remarks>
    /// Shared by add and replace so the two cannot disagree about what is legal.
    /// <para>
    /// The duplicate rule is narrower than the parent services': it forbids a
    /// second <em>active</em> marriage between the same two people (US-021), not a
    /// second marriage. Remarrying the same person after a divorce is a real
    /// thing that happens, and a rule that refused it would make the app wrong
    /// about those families. Two ended records between the same pair are therefore
    /// legitimate, and an overlap between them is a warning rather than a refusal.
    /// </para>
    /// </remarks>
    private async Task<Result<Addable>> CheckAddableAsync(
        Guid personId,
        Guid spouseId,
        CancellationToken ct,
        Guid? ignoring = null)
    {
        // Emptiness first: two empty ids are equal, so the self-marriage check
        // would otherwise report that somebody married themselves when in fact
        // nobody was chosen at all.
        if (personId == Guid.Empty || spouseId == Guid.Empty)
        {
            return Result<Addable>.Failure("Both people must be chosen.");
        }

        if (personId == spouseId)
        {
            return Result<Addable>.Failure("A person cannot marry themselves.");
        }

        var found = (await people.GetByIdsAsync([personId, spouseId], ct)).ToDictionary(p => p.Id);

        if (!found.TryGetValue(personId, out var person))
        {
            return Result<Addable>.Failure($"No person with id {personId}.");
        }

        if (!found.TryGetValue(spouseId, out var spouse))
        {
            return Result<Addable>.Failure($"No person with id {spouseId}.");
        }

        var theirs = await marriages.GetForPersonAsync(personId, ct);

        var duplicate = theirs.Any(m =>
            m.Id != ignoring
            && m.IsOngoing
            && (m.Spouse1Id == spouseId || m.Spouse2Id == spouseId));

        if (duplicate)
        {
            return Result<Addable>.Failure(
                $"{Name(person)} and {Name(spouse)} already have a current marriage recorded. "
                + "Record an end date on that one before adding another.");
        }

        return Result<Addable>.Success(new Addable(person, spouse));
    }

    /// <summary>
    /// The one rule here that refuses rather than warns. US-021, US-023, US-025.
    /// </summary>
    /// <remarks>
    /// Unlike a questionable birth year, this is not a fact about a family that
    /// might be true — a marriage that ends before it begins is not a record of
    /// anything, and three stories ask for it to be prevented rather than flagged.
    /// Same-year is allowed: a marriage annulled within months of starting is
    /// ordinary, and both dates may be year-only.
    /// </remarks>
    private static string? EndsBeforeItStarts(PartialDate startDate, PartialDate? endDate) =>
        endDate is not null && endDate.Year < startDate.Year
            ? $"A marriage cannot end in {endDate.Year}, before it started in {startDate.Year}."
            : null;

    /// <summary>
    /// Whether this marriage's dates overlap another of the same person's.
    /// US-042.
    /// </summary>
    /// <remarks>
    /// A warning and never a refusal, which US-042 asks for explicitly. Overlapping
    /// records are frequently what the evidence actually says: a separation with
    /// no recorded end, a second marriage whose start is known and whose
    /// predecessor's end is not, or a bigamous marriage that genuinely happened.
    /// <para>
    /// Compared by year, for the reason the parent services give at length:
    /// <see cref="PartialDate.CompareTo"/> ranks a year-only date before a fully
    /// dated one in the same year, so whether an overlap was noticed would
    /// otherwise depend on which of two records somebody had got round to dating
    /// precisely. An open-ended marriage counts as running to the present.
    /// </para>
    /// </remarks>
    private async Task<string?> DescribeOverlapAsync(
        Guid personId,
        Guid spouseId,
        PartialDate startDate,
        PartialDate? endDate,
        CancellationToken ct,
        Guid? ignoring = null)
    {
        // One read for both people rather than one each: this runs on every save,
        // and "a read per person involved" is how an N+1 starts.
        var others = (await marriages.GetForPeopleAsync([personId, spouseId], ct))
            .Where(m => m.Id != ignoring)
            .ToList();

        var overlapping = others.Count(m =>
            m.StartDate.Year <= (endDate?.Year ?? int.MaxValue)
            && startDate.Year <= (m.EndDate?.Year ?? int.MaxValue));

        return overlapping == 0
            ? null
            : $"this overlaps {(overlapping == 1 ? "another marriage" : $"{overlapping} other marriages")} "
                + "already recorded for one of them";
    }

    /// <summary>
    /// Succeeds either way, and says so when something about the record is
    /// questionable.
    /// </summary>
    /// <remarks>
    /// Warnings rather than refusals, for the reason the parent services give:
    /// partial, approximate and second-hand dates are the normal material of
    /// genealogy. Every problem is reported in one sentence rather than the first
    /// one found, because <see cref="Result{T}"/> carries a single warning and
    /// showing only the earlier of two would hide the other until somebody fixed
    /// the first and saved again.
    /// </remarks>
    private static Result<Guid> Warn(Guid marriageId, List<string> problems) =>
        problems.Count == 0
            ? Result<Guid>.Success(marriageId)
            : Result<Guid>.SuccessWithWarning(marriageId, Sentence(problems));

    private static string Sentence(List<string> problems) =>
        $"{Capitalise(string.Join("; and ", problems))}. Saved anyway — check the dates.";

    /// <summary>
    /// Everything questionable about the dates, as phrases to be joined.
    /// </summary>
    /// <remarks>
    /// A marriage dated before somebody was born, or after they died, is the
    /// marriage equivalent of the parent services' impossible birth order — worth
    /// saying, not worth blocking, since the wrong record may be the death date
    /// rather than the wedding.
    /// </remarks>
    private static List<string> Problems(
        Person one, Person two, PartialDate startDate, PartialDate? endDate)
    {
        var problems = new List<string>(3);

        foreach (var person in new[] { one, two })
        {
            if (person.BirthDate is not null && startDate.Year < person.BirthDate.Year)
            {
                problems.Add($"the marriage is dated {startDate.Year}, before {Name(person)} "
                    + $"was born in {person.BirthDate.Year}");
            }

            // The end date is exempt: a marriage ending in the year a spouse died
            // is the ordinary widowhood case (US-026), and an end date after a
            // death is what "Death of spouse" means. Only the start is impossible
            // after a death.
            if (person.DeathDate is not null && startDate.Year > person.DeathDate.Year)
            {
                problems.Add($"the marriage is dated {startDate.Year}, after {Name(person)} "
                    + $"died in {person.DeathDate.Year}");
            }
        }

        if (endDate is not null && endDate.Year < startDate.Year)
        {
            problems.Add($"it is recorded as ending in {endDate.Year}, before it started");
        }

        return problems;
    }

    /// <summary>
    /// Turns records into rows from one profile's point of view, dropping any
    /// whose spouse is no longer there.
    /// </summary>
    /// <remarks>
    /// A marriage can outlive the people it names — deleting a person does not
    /// currently sweep their relationships — so the missing case is real rather
    /// than defensive, and a row with a blank name would be worse than an omitted
    /// one.
    /// </remarks>
    private static IReadOnlyList<MarriageDto> Describe(
        IReadOnlyList<Marriage> records,
        Guid personId,
        IReadOnlyDictionary<Guid, Person> byId)
    {
        var rows = new List<MarriageDto>(records.Count);

        foreach (var marriage in records)
        {
            var spouseId = Other(marriage, personId);
            if (spouseId == Guid.Empty || !byId.TryGetValue(spouseId, out var spouse))
            {
                continue;
            }

            rows.Add(new MarriageDto(
                marriage.Id,
                PersonSummaryDto.From(spouse),
                marriage.StartDate,
                marriage.StartPlace,
                marriage.EndDate,
                marriage.EndReason,
                marriage.Certainty));
        }

        // Chronological by start date (US-022, US-042). Not by whether it is
        // ongoing: a marital history reads as a sequence, and floating the current
        // marriage to the top would put the present before the past on the one
        // section whose job is the order things happened in.
        return [.. rows.OrderBy(r => r.StartDate).ThenBy(r => r.Spouse.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// The spouse who is not this person, or <see cref="Guid.Empty"/> when the
    /// marriage does not involve them at all.
    /// </summary>
    /// <remarks>
    /// Empty rather than an exception because "not involved" is a question the
    /// callers ask on purpose — it is how a marriage id arriving from a stale page
    /// is rejected.
    /// </remarks>
    private static Guid Other(Marriage marriage, Guid personId) =>
        marriage.Spouse1Id == personId ? marriage.Spouse2Id
        : marriage.Spouse2Id == personId ? marriage.Spouse1Id
        : Guid.Empty;

    private static string Capitalise(string sentence) =>
        sentence.Length == 0 ? sentence : char.ToUpper(sentence[0]) + sentence[1..];

    private static string Name(Person person) =>
        person.IsPhantom ? "an unidentified ancestor" : $"{person.FirstName} {person.LastName}";
}
