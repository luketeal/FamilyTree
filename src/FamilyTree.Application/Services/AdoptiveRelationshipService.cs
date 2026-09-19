using FamilyTree.Application.Common;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.Repositories;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Application.Services;

/// <summary>
/// Adoptive parent and child links. US-014 to US-020, US-039.
/// </summary>
/// <remarks>
/// Deliberately a sibling of <see cref="BiologicalRelationshipService"/> rather
/// than a shared base class with a switch on kind. The two agree on their
/// guards and disagree on everything the guards are for: adoptive parenthood has
/// no upper limit (US-014), carries a date of its own, and implies no siblings —
/// a person's adoptive parent's other children are not automatically their
/// siblings in any sense the record can assert. Factoring the agreement out
/// would leave a base class whose every method took a flag.
/// <para>
/// What is shared is shared properly: <see cref="CircularReferenceChecker"/>
/// already walks biological and adoptive edges together, because a loop can run
/// up one kind and back down the other (US-039, US-040).
/// </para>
/// <para>
/// One link record is both directions, so "adding the link creates the
/// reciprocal entry" (US-014, US-018) and "removing it severs the reciprocal
/// entry" (US-017, US-020) are structural rather than something this code has to
/// remember — there is no second record that could fall out of step.
/// </para>
/// </remarks>
public sealed class AdoptiveRelationshipService(
    IPersonRepository people,
    IAdoptiveRelationshipRepository adoptive,
    CircularReferenceChecker cycles)
{
    /// <summary>
    /// Both adoptive sections of a profile, in a bounded number of reads whatever
    /// the size of the family.
    /// </summary>
    public async Task<Result<AdoptiveRelationshipsDto>> GetForPersonAsync(
        Guid personId, CancellationToken ct = default)
    {
        var parentLinks = await adoptive.GetParentLinksForChildAsync(personId, ct);
        var childLinks = await adoptive.GetChildLinksForParentAsync(personId, ct);

        // Everybody named by either section, resolved together. One read per row
        // is the N+1 the seam discipline forbids, and reading the whole table
        // would be one request for a page that names a handful of people.
        var wanted = parentLinks.Select(l => l.ParentId)
            .Concat(childLinks.Select(l => l.ChildId))
            .Append(personId)
            .Distinct()
            .ToList();

        var byId = (await people.GetByIdsAsync(wanted, ct)).ToDictionary(p => p.Id);

        if (!byId.ContainsKey(personId))
        {
            return Result<AdoptiveRelationshipsDto>.Failure($"No person with id {personId}.");
        }

        return Result<AdoptiveRelationshipsDto>.Success(new AdoptiveRelationshipsDto(
            Parents: Relate(parentLinks, l => l.ParentId, byId, includePhantoms: true),
            // As with biological children: a phantom stands for an ancestor
            // nobody has identified, so it belongs in a parents section and
            // nowhere else. A phantom adopted child would be a placeholder for
            // somebody with no reason to exist.
            Children: Relate(childLinks, l => l.ChildId, byId, includePhantoms: false)));
    }

    /// <summary>Records <paramref name="parentId"/> as an adoptive parent of <paramref name="childId"/>. US-014.</summary>
    public async Task<Result<Guid>> AddParentAsync(
        Guid childId,
        Guid parentId,
        PartialDate? adoptionDate = null,
        RelationshipCertainty certainty = RelationshipCertainty.Confirmed,
        CancellationToken ct = default)
    {
        var check = await CheckAddableAsync(childId, parentId, ct);
        if (!check.IsSuccess)
        {
            return Result<Guid>.Failure(check.Error!);
        }

        var (child, parent) = check.Value!;
        var link = new AdoptiveParentChild(parentId, childId, adoptionDate, certainty);
        await adoptive.AddAsync(link, ct);

        return Warn(link.Id, parent, child, adoptionDate);
    }

    /// <summary>
    /// Records <paramref name="childId"/> as an adoptive child of
    /// <paramref name="parentId"/>. US-018.
    /// </summary>
    /// <remarks>
    /// The same edge from the other end, so it delegates rather than repeating
    /// the guards. Unlike its biological counterpart there is no cap to run into
    /// from either direction — US-014 and US-018 both say so explicitly, which
    /// is the one place the two services genuinely differ in behaviour rather
    /// than in wording.
    /// </remarks>
    public Task<Result<Guid>> AddChildAsync(
        Guid parentId,
        Guid childId,
        PartialDate? adoptionDate = null,
        RelationshipCertainty certainty = RelationshipCertainty.Confirmed,
        CancellationToken ct = default) =>
        AddParentAsync(childId, parentId, adoptionDate, certainty, ct);

    /// <summary>
    /// Severs the adoptive link between the two, whichever profile asked.
    /// US-017, US-020.
    /// </summary>
    /// <remarks>
    /// Keyed by the pair rather than by the link id, because that is what a
    /// profile row knows without a further read and because it makes the
    /// reciprocity explicit: removing an adoptive parent from a child and
    /// removing that child from the parent are the same call on the same record.
    /// Neither person is deleted, which is the point both stories make.
    /// </remarks>
    public async Task<Result> RemoveAsync(Guid parentId, Guid childId, CancellationToken ct = default)
    {
        var link = await adoptive.GetAsync(parentId, childId, ct);
        if (link is null)
        {
            return Result.Failure("There is no adoptive relationship between those two people.");
        }

        await adoptive.DeleteAsync(link.Id, ct);
        return Result.Success();
    }

    /// <summary>
    /// Corrects the adoption date or the certainty of an existing link. US-016.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ReplaceParentAsync"/> because US-016 asks for two
    /// different corrections and they are not the same operation: changing the
    /// date edits this relationship, while changing the person ends one
    /// relationship and starts another. Collapsing them would mean re-running the
    /// cycle check to edit a date, and — worse — would make "I mistyped the year"
    /// and "I named the wrong person" indistinguishable in the record.
    /// </remarks>
    public async Task<Result> UpdateAsync(
        Guid parentId,
        Guid childId,
        PartialDate? adoptionDate,
        RelationshipCertainty certainty,
        CancellationToken ct = default)
    {
        var link = await adoptive.GetAsync(parentId, childId, ct);
        if (link is null)
        {
            return Result.Failure("There is no adoptive relationship between those two people.");
        }

        link.UpdateAdoptionDate(adoptionDate);
        link.UpdateCertainty(certainty);
        await adoptive.UpdateAsync(link, ct);

        var found = (await people.GetByIdsAsync([childId, parentId], ct)).ToDictionary(p => p.Id);

        // The people are read only to word a warning. If either has gone missing
        // the edit still stands — it was applied to the link, which is what was
        // asked for — and there is simply nothing to warn about.
        if (!found.TryGetValue(childId, out var child) || !found.TryGetValue(parentId, out var parent))
        {
            return Result.Success();
        }

        var warning = Describe(parent, child, adoptionDate);
        return warning is null ? Result.Success() : Result.SuccessWithWarning(warning);
    }

    /// <summary>
    /// Swaps one recorded adoptive parent for another. US-016.
    /// </summary>
    /// <remarks>
    /// One repository call, not a delete followed by an add — the same reasoning
    /// as the biological replace. The two-step version can half-apply, and its
    /// failure mode is a child whose adoptive parent silently emptied itself with
    /// nothing left to say who used to be in it.
    /// </remarks>
    public async Task<Result<Guid>> ReplaceParentAsync(
        Guid childId,
        Guid currentParentId,
        Guid newParentId,
        PartialDate? adoptionDate = null,
        RelationshipCertainty certainty = RelationshipCertainty.Confirmed,
        CancellationToken ct = default)
    {
        if (currentParentId == newParentId)
        {
            return Result<Guid>.Failure("That person is already the recorded adoptive parent.");
        }

        var existing = await adoptive.GetAsync(currentParentId, childId, ct);
        if (existing is null)
        {
            return Result<Guid>.Failure("There is no adoptive relationship between those two people.");
        }

        // Judged exactly as a fresh addition would be, less the link being
        // replaced: the same duplicate rule and the same cycle check. A
        // correction that could create a loop would be a hole straight through
        // US-040.
        var check = await CheckAddableAsync(childId, newParentId, ct, ignoringLinkId: existing.Id);
        if (!check.IsSuccess)
        {
            return Result<Guid>.Failure(check.Error!);
        }

        var (child, parent) = check.Value!;
        var replacement = new AdoptiveParentChild(newParentId, childId, adoptionDate, certainty);
        await adoptive.ReplaceParentAsync(existing.Id, replacement, ct);

        return Warn(replacement.Id, parent, child, adoptionDate);
    }

    /// <summary>The subject and the prospective adoptive parent, both confirmed to exist.</summary>
    private sealed record Addable(Person Child, Person Parent);

    /// <summary>
    /// Every rule standing between a proposed adoptive link and the store.
    /// </summary>
    /// <remarks>
    /// Shared by add and replace so the two cannot disagree about what is legal.
    /// Ordered cheapest and most specific first, so a duplicate is reported as a
    /// duplicate and does not cost the ancestor walk.
    /// <para>
    /// Conspicuously absent is a cap. US-014 says there is none, and the reason
    /// is worth recording next to the code that does not enforce it: two adoptive
    /// parents is the common case, but a child moved between placements
    /// accumulates more, and a tree that refused to record the third would be
    /// wrong about a real childhood.
    /// </para>
    /// </remarks>
    private async Task<Result<Addable>> CheckAddableAsync(
        Guid childId,
        Guid parentId,
        CancellationToken ct,
        Guid? ignoringLinkId = null)
    {
        // Emptiness first: two empty ids are equal, so the self-parenthood check
        // would otherwise claim somebody was their own adoptive parent when in
        // fact nobody was chosen at all.
        if (parentId == Guid.Empty || childId == Guid.Empty)
        {
            return Result<Addable>.Failure("Both a parent and a child must be chosen.");
        }

        if (parentId == childId)
        {
            return Result<Addable>.Failure("A person cannot be their own adoptive parent.");
        }

        // Both people in one read rather than two, and it doubles as the
        // existence check for each.
        var found = (await people.GetByIdsAsync([childId, parentId], ct)).ToDictionary(p => p.Id);

        if (!found.TryGetValue(childId, out var child))
        {
            return Result<Addable>.Failure($"No person with id {childId}.");
        }

        if (!found.TryGetValue(parentId, out var parent))
        {
            return Result<Addable>.Failure($"No person with id {parentId}.");
        }

        var parentLinks = (await adoptive.GetParentLinksForChildAsync(childId, ct))
            .Where(l => l.Id != ignoringLinkId)
            .ToList();

        if (parentLinks.Any(l => l.ParentId == parentId))
        {
            return Result<Addable>.Failure(
                $"{Name(parent)} is already recorded as an adoptive parent of {Name(child)}.");
        }

        // Last, because it is the only check that walks the graph. It covers
        // biological and adoptive edges together: the family graph is a DAG, and
        // a loop can run up one kind of edge and back down the other (US-039).
        if (await cycles.WouldCreateCycleAsync(parentId, childId, ct))
        {
            return Result<Addable>.Failure(
                $"Cannot add {Name(parent)} as an adoptive parent of {Name(child)} — "
                + $"{Name(parent)} is already a descendant of {Name(child)}.");
        }

        return Result<Addable>.Success(new Addable(child, parent));
    }

    /// <summary>
    /// Succeeds either way, and says so when the dates disagree.
    /// </summary>
    /// <remarks>
    /// Warnings rather than refusals, for the same reason the biological service
    /// gives: partial, approximate and second-hand dates are the normal material
    /// of genealogy, and blocking a relationship on a guessed year would make the
    /// app wrong about a real family.
    /// </remarks>
    private static Result<Guid> Warn(Guid linkId, Person parent, Person child, PartialDate? adoptionDate)
    {
        var message = Describe(parent, child, adoptionDate);
        return message is null
            ? Result<Guid>.Success(linkId)
            : Result<Guid>.SuccessWithWarning(linkId, message);
    }

    /// <summary>
    /// Everything questionable about a proposed link, in one sentence, or null
    /// when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// Both checks are reported together rather than the first one found.
    /// <see cref="Result{T}"/> carries a single warning, and returning only the
    /// earlier of two problems would quietly hide the other until somebody fixed
    /// the first and saved again — which is the shape of a bug that looks like
    /// the app changing its mind.
    /// </remarks>
    private static string? Describe(Person parent, Person child, PartialDate? adoptionDate)
    {
        var problems = new List<string>(2);

        // Compared by year rather than by PartialDate.CompareTo, for the reason
        // set out at length in BiologicalRelationshipService.Warn: CompareTo
        // ranks a year-only date before a fully dated one in the same year, so
        // whether an impossible pair is noticed would otherwise depend on which
        // of the two records somebody had got round to dating precisely. Year is
        // also the only thing the sentence quotes.
        if (parent.BirthDate is not null && child.BirthDate is not null
            && parent.BirthDate.Year >= child.BirthDate.Year)
        {
            problems.Add(parent.BirthDate.Year == child.BirthDate.Year
                ? $"{Name(parent)} and {Name(child)} are both recorded as born in {parent.BirthDate.Year}"
                : $"{Name(parent)} was born in {parent.BirthDate.Year}, after {Name(child)} "
                    + $"in {child.BirthDate.Year}");
        }

        // An adoption dated before the child was born. Worth flagging on its own
        // account: unlike the pair above it is a problem with the value just
        // typed rather than with either person's record, so it is the one a
        // correction can act on immediately.
        if (adoptionDate is not null && child.BirthDate is not null
            && adoptionDate.Year < child.BirthDate.Year)
        {
            problems.Add($"the adoption is dated {adoptionDate.Year}, before {Name(child)} "
                + $"was born in {child.BirthDate.Year}");
        }

        return problems.Count == 0
            ? null
            : $"{Capitalise(string.Join("; and ", problems))}. Saved anyway — check the dates.";
    }

    private static string Capitalise(string sentence) =>
        sentence.Length == 0 ? sentence : char.ToUpper(sentence[0]) + sentence[1..];

    /// <summary>
    /// Turns links into rows, dropping any whose person is no longer there.
    /// </summary>
    /// <remarks>
    /// A link can outlive the person it names — deleting a person does not
    /// currently sweep their relationships — so the missing case is real rather
    /// than defensive, and a blank row would be worse than an omitted one.
    /// </remarks>
    private static IReadOnlyList<AdoptiveRelativeDto> Relate(
        IReadOnlyList<AdoptiveParentChild> links,
        Func<AdoptiveParentChild, Guid> other,
        IReadOnlyDictionary<Guid, Person> byId,
        bool includePhantoms)
    {
        var rows = new List<AdoptiveRelativeDto>(links.Count);

        foreach (var link in links)
        {
            if (!byId.TryGetValue(other(link), out var person))
            {
                continue;
            }

            if (person.IsPhantom && !includePhantoms)
            {
                continue;
            }

            rows.Add(new AdoptiveRelativeDto(
                link.Id, PersonSummaryDto.From(person), link.Certainty, person.IsPhantom, link.AdoptionDate));
        }

        // Adoption date, then birth date (US-019). Undated adoptions sort last
        // rather than first: a missing date is not year zero, and a record
        // nobody has dated is not the earliest thing that happened.
        return
        [
            .. rows
                .OrderBy(r => r.AdoptionDate is null)
                .ThenBy(r => r.AdoptionDate)
                .ThenBy(r => r.Person.BirthYear ?? int.MaxValue)
                .ThenBy(r => r.Person.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    private static string Name(Person person) =>
        person.IsPhantom ? "an unidentified ancestor" : $"{person.FirstName} {person.LastName}";
}
