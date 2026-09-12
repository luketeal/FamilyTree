using FamilyTree.Application.Common;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.Repositories;

namespace FamilyTree.Application.Services;

/// <summary>
/// Biological parent and child links, and the siblings they imply.
/// US-007 to US-013, US-037, US-039, US-051.
/// </summary>
/// <remarks>
/// The entity already refuses an empty parent, an empty child and
/// self-parenthood, so none of that is repeated here. What a
/// <see cref="BiologicalParentChild"/> cannot see is everything about the set it
/// joins: whether the child already has two parents, whether this pair is
/// already recorded, and whether the new edge closes a loop. Those are this
/// service's job.
/// <para>
/// One link record is both directions. "Adding a parent creates the reciprocal
/// child relationship" (US-007) and "removing the link removes the reciprocal
/// entry" (US-010, US-013) are therefore structural rather than something this
/// code has to remember to do — there is no second record that could fall out
/// of step.
/// </para>
/// </remarks>
public sealed class BiologicalRelationshipService(
    IPersonRepository people,
    IBiologicalRelationshipRepository biological,
    CircularReferenceChecker cycles)
{
    /// <summary>
    /// Everything the profile's biological sections show, in a bounded number of
    /// reads whatever the size of the family.
    /// </summary>
    public async Task<Result<BiologicalRelationshipsDto>> GetForPersonAsync(
        Guid personId, CancellationToken ct = default)
    {
        var parentLinks = await biological.GetParentLinksForChildAsync(personId, ct);
        var childLinks = await biological.GetChildLinksForParentAsync(personId, ct);
        var siblings = await ReadSiblingsAsync(personId, parentLinks, ct);

        // Every person named by any of the three sections, resolved together.
        // Reading them one at a time is the N+1 the seam discipline forbids, and
        // reading the whole table would be one request for a page that names a
        // handful of people.
        var wanted = parentLinks.Select(l => l.ParentId)
            .Concat(childLinks.Select(l => l.ChildId))
            .Concat(siblings.CandidateIds)
            .Concat(siblings.SharedParentIds)
            .Append(personId)
            .Distinct()
            .ToList();

        var byId = (await people.GetByIdsAsync(wanted, ct)).ToDictionary(p => p.Id);

        if (!byId.ContainsKey(personId))
        {
            return Result<BiologicalRelationshipsDto>.Failure($"No person with id {personId}.");
        }

        return Result<BiologicalRelationshipsDto>.Success(new BiologicalRelationshipsDto(
            Parents: Relate(parentLinks, l => l.ParentId, byId, includePhantoms: true),
            // Phantoms are placeholders for ancestors nobody has identified, so
            // they belong in the parents section and nowhere else. A phantom
            // child or sibling would be a placeholder for somebody with no
            // reason to exist.
            Children: Relate(childLinks, l => l.ChildId, byId, includePhantoms: false),
            Siblings: Classify(siblings, byId)));
    }

    /// <summary>
    /// People who share at least one biological parent with this one, classified
    /// full or half. US-037, US-051.
    /// </summary>
    public async Task<Result<IReadOnlyList<SiblingDto>>> GetSiblingsAsync(
        Guid personId, CancellationToken ct = default)
    {
        if (!await people.ExistsAsync(personId, ct))
        {
            return Result<IReadOnlyList<SiblingDto>>.Failure($"No person with id {personId}.");
        }

        var parentLinks = await biological.GetParentLinksForChildAsync(personId, ct);
        var siblings = await ReadSiblingsAsync(personId, parentLinks, ct);

        var wanted = siblings.CandidateIds.Concat(siblings.SharedParentIds).Distinct().ToList();
        var byId = (await people.GetByIdsAsync(wanted, ct)).ToDictionary(p => p.Id);

        return Result<IReadOnlyList<SiblingDto>>.Success(Classify(siblings, byId));
    }

    /// <summary>Records <paramref name="parentId"/> as a biological parent of <paramref name="childId"/>. US-007.</summary>
    public async Task<Result<Guid>> AddParentAsync(
        Guid childId,
        Guid parentId,
        RelationshipCertainty certainty = RelationshipCertainty.Confirmed,
        CancellationToken ct = default)
    {
        var check = await CheckAddableAsync(childId, parentId, ct);
        if (!check.IsSuccess)
        {
            return Result<Guid>.Failure(check.Error!);
        }

        var (child, parent, _) = check.Value!;
        var link = new BiologicalParentChild(parentId, childId, certainty);
        await biological.AddAsync(link, ct);

        return Warn(link.Id, parent, child);
    }

    /// <summary>
    /// Records <paramref name="childId"/> as a biological child of
    /// <paramref name="parentId"/>. US-011.
    /// </summary>
    /// <remarks>
    /// The same edge from the other end, so it delegates rather than repeating
    /// the guards. In particular the two-parent cap still applies to the child,
    /// which is what US-011 asks for — it is refused with a message naming the
    /// parents already recorded, rather than silently displacing one of them.
    /// Losing a recorded ancestor to make room for a new one is exactly the
    /// silent loss this project treats as a defect.
    /// </remarks>
    public Task<Result<Guid>> AddChildAsync(
        Guid parentId,
        Guid childId,
        RelationshipCertainty certainty = RelationshipCertainty.Confirmed,
        CancellationToken ct = default) =>
        AddParentAsync(childId, parentId, certainty, ct);

    /// <summary>
    /// Severs the link between the two, whichever profile asked. US-010, US-013.
    /// </summary>
    /// <remarks>
    /// Keyed by the pair rather than by the link id because that is what a
    /// profile row knows without a further read, and because it makes the
    /// reciprocity explicit: removing a parent from a child and removing that
    /// child from the parent are the same call on the same record.
    /// </remarks>
    public async Task<Result> RemoveAsync(Guid parentId, Guid childId, CancellationToken ct = default)
    {
        var link = await biological.GetAsync(parentId, childId, ct);
        if (link is null)
        {
            return Result.Failure("There is no biological relationship between those two people.");
        }

        await biological.DeleteAsync(link.Id, ct);
        return Result.Success();
    }

    /// <summary>
    /// Swaps one recorded parent for another. US-009.
    /// </summary>
    /// <remarks>
    /// One repository call, not a delete followed by an add. The API-seam rule
    /// asks for that so the mutation maps to one request later; the sharper
    /// reason is that the two-step version can half-apply, and its failure mode
    /// is a child whose parent slot silently emptied itself with nothing left to
    /// say who used to be in it.
    /// </remarks>
    public async Task<Result<Guid>> ReplaceParentAsync(
        Guid childId,
        Guid currentParentId,
        Guid newParentId,
        RelationshipCertainty certainty = RelationshipCertainty.Confirmed,
        CancellationToken ct = default)
    {
        if (currentParentId == newParentId)
        {
            return Result<Guid>.Failure("That person is already the recorded parent.");
        }

        var existing = await biological.GetAsync(currentParentId, childId, ct);
        if (existing is null)
        {
            return Result<Guid>.Failure("There is no biological relationship between those two people.");
        }

        // The replacement is judged exactly as a fresh addition would be, less
        // the link being replaced: the same cap, the same duplicate rule, the
        // same cycle check. A correction that could create a loop would be a
        // hole straight through US-040.
        var check = await CheckAddableAsync(childId, newParentId, ct, ignoringLinkId: existing.Id);
        if (!check.IsSuccess)
        {
            return Result<Guid>.Failure(check.Error!);
        }

        var (child, parent, _) = check.Value!;
        var replacement = new BiologicalParentChild(newParentId, childId, certainty);
        await biological.ReplaceParentAsync(existing.Id, replacement, ct);

        return Warn(replacement.Id, parent, child);
    }

    /// <summary>The subject, the prospective parent, and the links already recorded.</summary>
    private sealed record Addable(Person Child, Person Parent, IReadOnlyList<BiologicalParentChild> ParentLinks);

    /// <summary>
    /// Every rule standing between a proposed parent link and the store.
    /// </summary>
    /// <remarks>
    /// Shared by add and replace so the two cannot disagree about what is legal.
    /// Ordered cheapest and most specific first: a duplicate is reported as a
    /// duplicate rather than as a full parent slot, and neither costs the
    /// ancestor walk.
    /// </remarks>
    private async Task<Result<Addable>> CheckAddableAsync(
        Guid childId,
        Guid parentId,
        CancellationToken ct,
        Guid? ignoringLinkId = null)
    {
        if (parentId == childId)
        {
            return Result<Addable>.Failure("A person cannot be their own biological parent.");
        }

        if (parentId == Guid.Empty || childId == Guid.Empty)
        {
            return Result<Addable>.Failure("Both a parent and a child must be chosen.");
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

        var parentLinks = (await biological.GetParentLinksForChildAsync(childId, ct))
            .Where(l => l.Id != ignoringLinkId)
            .ToList();

        if (parentLinks.Any(l => l.ParentId == parentId))
        {
            return Result<Addable>.Failure(
                $"{Name(parent)} is already recorded as a biological parent of {Name(child)}.");
        }

        if (parentLinks.Count >= BiologicalParentChild.MaxParentsPerChild)
        {
            var recorded = (await people.GetByIdsAsync([.. parentLinks.Select(l => l.ParentId)], ct))
                .Select(Name)
                .ToList();

            return Result<Addable>.Failure(
                $"{Name(child)} already has {BiologicalParentChild.MaxParentsPerChild} biological parents"
                + $"{(recorded.Count == 0 ? string.Empty : $" ({string.Join(" and ", recorded)})")}. "
                + "Replace one of them instead.");
        }

        // Last, because it is the only check that walks the graph. It covers
        // biological and adoptive edges together: the family graph is a DAG, and
        // a loop can run up one kind of edge and back down the other (US-039).
        if (await cycles.WouldCreateCycleAsync(parentId, childId, ct))
        {
            return Result<Addable>.Failure(
                $"Cannot add {Name(parent)} as a parent of {Name(child)} — "
                + $"{Name(parent)} is already a descendant of {Name(child)}.");
        }

        return Result<Addable>.Success(new Addable(child, parent, parentLinks));
    }

    /// <summary>
    /// Succeeds either way, and says so when the dates disagree. US-007.
    /// </summary>
    /// <remarks>
    /// A warning rather than a refusal because the dates are the likelier
    /// mistake: partial, approximate and second-hand dates are the normal
    /// material of genealogy, and blocking the relationship would make the app
    /// wrong about a real family on the strength of a guessed year.
    /// </remarks>
    private static Result<Guid> Warn(Guid linkId, Person parent, Person child)
    {
        if (parent.BirthDate is null || child.BirthDate is null
            || parent.BirthDate.CompareTo(child.BirthDate) <= 0)
        {
            return Result<Guid>.Success(linkId);
        }

        return Result<Guid>.SuccessWithWarning(
            linkId,
            $"{Name(parent)} was born in {parent.BirthDate.Year}, after {Name(child)} "
            + $"in {child.BirthDate.Year}. Saved anyway — check the dates.");
    }

    /// <summary>Sibling candidates and how they are connected, before any person is resolved.</summary>
    private sealed record SiblingSearch(
        IReadOnlyList<Guid> CandidateIds,
        IReadOnlyList<Guid> SharedParentIds,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> SharedParentsByCandidate,
        int SubjectParentCount);

    /// <summary>
    /// Three rounds of reads regardless of how large the family is: the
    /// subject's parents, one children-of read per parent, and a single batched
    /// read of the candidates' own parents to classify them.
    /// </summary>
    /// <remarks>
    /// The per-parent loop is bounded by
    /// <see cref="BiologicalParentChild.MaxParentsPerChild"/> — two reads, not a
    /// walk. The classifying read is the one that would otherwise be per
    /// candidate, and <c>GetParentLinksForChildrenAsync</c> exists precisely so
    /// it is not.
    /// </remarks>
    private async Task<SiblingSearch> ReadSiblingsAsync(
        Guid personId,
        IReadOnlyList<BiologicalParentChild> parentLinks,
        CancellationToken ct)
    {
        var subjectParents = parentLinks.Select(l => l.ParentId).Distinct().ToList();
        if (subjectParents.Count == 0)
        {
            return new SiblingSearch([], [], new Dictionary<Guid, IReadOnlyList<Guid>>(), 0);
        }

        var candidates = new HashSet<Guid>();
        foreach (var parentId in subjectParents)
        {
            foreach (var link in await biological.GetChildLinksForParentAsync(parentId, ct))
            {
                if (link.ChildId != personId)
                {
                    candidates.Add(link.ChildId);
                }
            }
        }

        if (candidates.Count == 0)
        {
            return new SiblingSearch([], [], new Dictionary<Guid, IReadOnlyList<Guid>>(), subjectParents.Count);
        }

        var candidateList = candidates.ToList();
        var candidateParentLinks = await biological.GetParentLinksForChildrenAsync(candidateList, ct);

        var subjectParentSet = subjectParents.ToHashSet();
        var shared = new Dictionary<Guid, IReadOnlyList<Guid>>();

        foreach (var candidateId in candidateList)
        {
            var inCommon = candidateParentLinks
                .Where(l => l.ChildId == candidateId && subjectParentSet.Contains(l.ParentId))
                .Select(l => l.ParentId)
                .Distinct()
                .ToList();

            if (inCommon.Count > 0)
            {
                shared[candidateId] = inCommon;
            }
        }

        return new SiblingSearch(
            [.. shared.Keys],
            subjectParents,
            shared,
            subjectParents.Count);
    }

    private static IReadOnlyList<SiblingDto> Classify(
        SiblingSearch search, IReadOnlyDictionary<Guid, Person> byId)
    {
        var siblings = new List<SiblingDto>();

        foreach (var (candidateId, sharedParentIds) in search.SharedParentsByCandidate)
        {
            if (!byId.TryGetValue(candidateId, out var person) || person.IsPhantom)
            {
                continue;
            }

            // Full only when both of the subject's parents are shared. Somebody
            // with one recorded parent has no full siblings — not because there
            // are none, but because the record cannot show it, and claiming
            // otherwise would invent a fact about the other parent.
            var kind = sharedParentIds.Count >= BiologicalParentChild.MaxParentsPerChild
                && search.SubjectParentCount >= BiologicalParentChild.MaxParentsPerChild
                ? SiblingKind.Full
                : SiblingKind.Half;

            var names = sharedParentIds
                .Select(id => byId.TryGetValue(id, out var parent) ? Name(parent) : "Unknown")
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            siblings.Add(new SiblingDto(PersonSummaryDto.From(person), kind, names));
        }

        // Full siblings first, then half, each by birth date ascending (US-051).
        return
        [
            .. siblings
                .OrderBy(s => s.Kind)
                .ThenBy(s => s.Person.BirthYear ?? int.MaxValue)
                .ThenBy(s => s.Person.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    /// <summary>
    /// Turns links into rows, dropping any whose person is no longer there.
    /// </summary>
    /// <remarks>
    /// A link can outlive the person it names — deleting a person does not
    /// currently sweep their relationships — so the missing case is real rather
    /// than defensive. Rendering it as a blank row would be worse than omitting
    /// it, and the tree view will surface the same records again in PR 10.
    /// </remarks>
    private static IReadOnlyList<RelatedPersonDto> Relate(
        IReadOnlyList<BiologicalParentChild> links,
        Func<BiologicalParentChild, Guid> other,
        IReadOnlyDictionary<Guid, Person> byId,
        bool includePhantoms)
    {
        var rows = new List<RelatedPersonDto>(links.Count);

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

            rows.Add(new RelatedPersonDto(
                link.Id, PersonSummaryDto.From(person), link.Certainty, person.IsPhantom));
        }

        // Birth date ascending, which US-012 asks for on children and which is
        // the only ordering that reads as anything on the others. Undated people
        // sort last rather than first: a missing year is not year zero.
        return
        [
            .. rows
                .OrderBy(r => r.Person.BirthYear ?? int.MaxValue)
                .ThenBy(r => r.Person.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    private static string Name(Person person) =>
        person.IsPhantom ? "an unidentified ancestor" : $"{person.FirstName} {person.LastName}";
}
