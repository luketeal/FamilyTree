using FamilyTree.Application.Common;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Repositories;

namespace FamilyTree.Application.Services;

/// <summary>
/// Stepparent labels. US-038.
/// </summary>
/// <remarks>
/// The only relationship in this app that is not recorded directly: a stepparent
/// link is a claim <em>about</em> a marriage — "the person my parent married" —
/// so it cannot be applied to two people picked out of the tree. Everything here
/// follows from that.
/// <para>
/// The label is applied by hand and never inferred, which US-038 asks for
/// explicitly and which is the reason this service offers candidates rather than
/// creating links from them. A parent's spouse is frequently not a stepparent in
/// any sense the family would recognise — they may have married after the children
/// were grown, or never met them — and a tree that asserted the relationship
/// because the dates allowed it would be putting words in a family's mouth. So the
/// candidate list is a question, and only an answer writes a record.
/// </para>
/// <para>
/// Nothing here has a <c>Update</c> or a replace, for the reason the repository
/// interface gives: the record is three ids and no fields, so there is nothing to
/// correct without making it a different claim.
/// </para>
/// </remarks>
public sealed class StepparentService(
    IPersonRepository people,
    IBiologicalRelationshipRepository biological,
    IAdoptiveRelationshipRepository adoptive,
    IMarriageRepository marriages,
    IStepparentRelationshipRepository stepparents)
{
    /// <summary>
    /// Both step sections of a profile and the candidates for a new label, in a
    /// bounded number of reads. US-038.
    /// </summary>
    /// <remarks>
    /// Six reads whatever the size of the family, and none of them per row: the
    /// subject's parent links of both kinds, the labels pointing each way, the
    /// marriages of the subject's parents and of the subject, and one batch of
    /// people covering everybody named by any of it.
    /// </remarks>
    public async Task<Result<StepFamilyDto>> GetForPersonAsync(
        Guid personId, CancellationToken ct = default)
    {
        var asStepchild = await stepparents.GetForStepchildAsync(personId, ct);
        var asStepparent = await stepparents.GetForStepparentAsync(personId, ct);

        // The subject's own parents, both kinds together: US-038 says a
        // stepparent is the spouse of a biological *or* adoptive parent, and
        // splitting them would make an adoptive parent's spouse unrecordable.
        var parentIds = (await biological.GetParentLinksForChildAsync(personId, ct))
            .Select(l => l.ParentId)
            .Concat((await adoptive.GetParentLinksForChildAsync(personId, ct)).Select(l => l.ParentId))
            .Distinct()
            .ToList();

        // The subject's own marriages come along because the stepchildren section
        // needs them: a label naming this person as a stepparent rests on one of
        // them, and the row says whether that marriage is still current.
        var relevant = await marriages.GetForPeopleAsync([.. parentIds, personId], ct);
        var marriagesById = relevant.ToDictionary(m => m.Id);

        // Then the marriages the labels themselves name, for the ones that set
        // does not reach. Removing the parent link a label ran through leaves the
        // label and its marriage both intact and the marriage unreachable from
        // here — and the row vanished from this profile while still showing, with
        // a Remove button, on the stepparent's. Two profiles disagreeing about one
        // record is the shape the marriage cascade and the import filter exist to
        // prevent; this is the same shape reached down the other leg.
        var named = asStepchild.Concat(asStepparent)
            .Select(l => l.MarriageId)
            .Where(id => !marriagesById.ContainsKey(id))
            .Distinct()
            .ToList();

        foreach (var marriage in await marriages.GetByIdsAsync(named, ct))
        {
            marriagesById[marriage.Id] = marriage;
        }

        var wanted = asStepchild.Select(l => l.StepparentId)
            .Concat(asStepparent.Select(l => l.StepchildId))
            .Concat(parentIds)
            .Concat(marriagesById.Values.SelectMany(m => new[] { m.Spouse1Id, m.Spouse2Id }))
            .Append(personId)
            .Distinct()
            .ToList();

        var byId = (await people.GetByIdsAsync(wanted, ct)).ToDictionary(p => p.Id);

        if (!byId.ContainsKey(personId))
        {
            return Result<StepFamilyDto>.Failure($"No person with id {personId}.");
        }

        return Result<StepFamilyDto>.Success(new StepFamilyDto(
            Stepparents: Rows(asStepchild, l => l.StepparentId, marriagesById, byId),
            Stepchildren: Rows(asStepparent, l => l.StepchildId, marriagesById, byId),
            // Candidates come from the parents' marriages alone — the extra records
            // fetched above justify labels that already exist and are not offers.
            Candidates: Candidates(personId, parentIds, relevant, asStepchild, byId)));
    }

    /// <summary>
    /// Records that somebody is a stepparent of somebody else, by virtue of a
    /// particular marriage. US-038.
    /// </summary>
    /// <remarks>
    /// The marriage is an argument rather than something this method looks up,
    /// because a person may be married to two of a child's parents over a lifetime
    /// and the label has to say which marriage it rests on — that is what makes the
    /// cascade in US-038's last criterion meaningful.
    /// </remarks>
    public async Task<Result<Guid>> LabelAsync(
        Guid stepchildId,
        Guid stepparentId,
        Guid marriageId,
        CancellationToken ct = default)
    {
        // Emptiness first: two empty ids are equal, so the self-reference check
        // below would otherwise report that somebody was their own stepparent
        // when in fact nobody was chosen.
        if (stepchildId == Guid.Empty || stepparentId == Guid.Empty || marriageId == Guid.Empty)
        {
            return Result<Guid>.Failure("A stepparent, a stepchild and a marriage must all be named.");
        }

        if (stepchildId == stepparentId)
        {
            return Result<Guid>.Failure("A person cannot be their own stepparent.");
        }

        var found = (await people.GetByIdsAsync([stepchildId, stepparentId], ct))
            .ToDictionary(p => p.Id);

        if (!found.TryGetValue(stepchildId, out var stepchild))
        {
            return Result<Guid>.Failure($"No person with id {stepchildId}.");
        }

        if (!found.TryGetValue(stepparentId, out var stepparent))
        {
            return Result<Guid>.Failure($"No person with id {stepparentId}.");
        }

        var marriage = await marriages.GetByIdAsync(marriageId, ct);
        if (marriage is null)
        {
            return Result<Guid>.Failure("That marriage is no longer recorded.");
        }

        if (marriage.Spouse1Id != stepparentId && marriage.Spouse2Id != stepparentId)
        {
            return Result<Guid>.Failure(
                $"{Name(stepparent)} is not one of the spouses in that marriage.");
        }

        // The rule this service exists for: the other spouse has to be a parent of
        // the child. Without it the label would assert a step relationship between
        // two families that have nothing to do with each other, and nothing later
        // could tell it was wrong — a step edge in the tree (PR 10) would simply
        // appear between strangers.
        var otherSpouseId = marriage.Spouse1Id == stepparentId
            ? marriage.Spouse2Id
            : marriage.Spouse1Id;

        var parentIds = (await biological.GetParentLinksForChildAsync(stepchildId, ct))
            .Select(l => l.ParentId)
            .Concat((await adoptive.GetParentLinksForChildAsync(stepchildId, ct)).Select(l => l.ParentId))
            .ToHashSet();

        if (!parentIds.Contains(otherSpouseId))
        {
            return Result<Guid>.Failure(
                $"That marriage does not involve a parent of {Name(stepchild)}, so it cannot make "
                + $"{Name(stepparent)} a stepparent.");
        }

        // Somebody already recorded as a parent is not a stepparent. US-038 asks
        // for the three kinds to stay distinct, and this is the case that would
        // break it: a couple who are both parents of the same child are married to
        // each other, so each would otherwise qualify as the other's child's
        // stepparent.
        if (parentIds.Contains(stepparentId))
        {
            return Result<Guid>.Failure(
                $"{Name(stepparent)} is already recorded as a parent of {Name(stepchild)}.");
        }

        // Keyed on the pair rather than on the pair and the marriage. A second
        // label through a different marriage would be the same claim recorded
        // twice, and a profile showing the same stepparent on two rows is a
        // duplicate however the records differ.
        var existing = await stepparents.GetForStepchildAsync(stepchildId, ct);
        if (existing.Any(l => l.StepparentId == stepparentId))
        {
            return Result<Guid>.Failure(
                $"{Name(stepparent)} is already recorded as a stepparent of {Name(stepchild)}.");
        }

        var link = new StepparentRelationship(stepparentId, stepchildId, marriageId);
        await stepparents.AddAsync(link, ct);

        return Result<Guid>.Success(link.Id);
    }

    /// <summary>
    /// Removes a stepparent label. US-038.
    /// </summary>
    /// <remarks>
    /// Keyed on the link id, unlike the parent services' pair-keyed removes,
    /// because the row already knows it and because the pair is not enough to
    /// identify the record: the same two people can be connected through more than
    /// one marriage even though only one label between them is allowed at a time.
    /// Neither person is deleted, and neither is the marriage.
    /// </remarks>
    public async Task<Result> RemoveAsync(Guid linkId, CancellationToken ct = default)
    {
        // The repository answers "was it there" from the same call that removes
        // it. Establishing that by reading every label and looking for one id was
        // a whole-collection read on a mutation path — free here, a request of its
        // own once this interface is backed by HTTP.
        return await stepparents.DeleteAsync(linkId, ct)
            ? Result.Success()
            : Result.Failure("That stepparent label is no longer recorded.");
    }

    /// <summary>
    /// Turns labels into rows, dropping any whose person or marriage is gone.
    /// </summary>
    /// <remarks>
    /// The marriage should always be there — the repository cascade removes labels
    /// with it — but a file imported from elsewhere is not bound by that, so a
    /// label naming a marriage this tree does not have is a state the app can
    /// reach. Dropping the row is right: the section's whole claim is "by virtue of
    /// this marriage", and it cannot be made without one.
    /// </remarks>
    private static IReadOnlyList<StepRelativeDto> Rows(
        IReadOnlyList<StepparentRelationship> links,
        Func<StepparentRelationship, Guid> other,
        IReadOnlyDictionary<Guid, Marriage> marriagesById,
        IReadOnlyDictionary<Guid, Person> byId)
    {
        var rows = new List<StepRelativeDto>(links.Count);

        foreach (var link in links)
        {
            if (!byId.TryGetValue(other(link), out var person)
                || !marriagesById.TryGetValue(link.MarriageId, out var marriage))
            {
                continue;
            }

            // The parent the relationship runs through: the spouse in that
            // marriage who is not the stepparent. Named in the row because
            // "stepmother" on its own does not say whose spouse she is, and with
            // two marriages in play that is the only thing distinguishing the rows.
            //
            // A label whose stepparent is in neither side of its marriage is
            // dropped rather than described. LabelAsync refuses to create one and
            // import now refuses to carry one, but picking a spouse anyway would
            // have meant a row naming somebody at random as the parent it runs
            // through — a wrong answer where no answer exists.
            if (marriage.Spouse1Id != link.StepparentId && marriage.Spouse2Id != link.StepparentId)
            {
                continue;
            }

            var viaId = marriage.Spouse1Id == link.StepparentId
                ? marriage.Spouse2Id
                : marriage.Spouse1Id;

            rows.Add(new StepRelativeDto(
                link.Id,
                PersonSummaryDto.From(person),
                link.MarriageId,
                byId.TryGetValue(viaId, out var via) ? Name(via) : "a parent no longer recorded",
                marriage.IsOngoing));
        }

        return
        [
            .. rows
                .OrderBy(r => r.Person.BirthYear ?? int.MaxValue)
                .ThenBy(r => r.Person.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    /// <summary>
    /// The people a parent is currently married to who are not already accounted
    /// for. US-038.
    /// </summary>
    /// <remarks>
    /// Current marriages only, which is what US-038 asks for. An ended marriage
    /// puts nobody in a child's household now, and offering a divorced ex-spouse
    /// as a candidate would invite recording a step relationship that the record
    /// says ended. A label already applied survives its marriage ending — only
    /// deleting the marriage record removes it — so the two rules do not disagree:
    /// one is about what to offer, the other about what has been asserted.
    /// <para>
    /// Anyone already a parent of the child is excluded, because they are a parent
    /// rather than a stepparent — this is the couple who are both parents of the
    /// same child, which is the common case and would otherwise fill the candidate
    /// list with the child's own mother and father.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<StepparentCandidateDto> Candidates(
        Guid personId,
        IReadOnlyList<Guid> parentIds,
        IReadOnlyList<Marriage> relevant,
        IReadOnlyList<StepparentRelationship> alreadyLabelled,
        IReadOnlyDictionary<Guid, Person> byId)
    {
        var parents = parentIds.ToHashSet();
        var labelled = alreadyLabelled.Select(l => l.StepparentId).ToHashSet();

        var rows = new List<StepparentCandidateDto>();
        var offered = new HashSet<Guid>();

        foreach (var marriage in relevant.Where(m => m.IsOngoing))
        {
            foreach (var (spouseId, otherId) in new[]
            {
                (marriage.Spouse1Id, marriage.Spouse2Id),
                (marriage.Spouse2Id, marriage.Spouse1Id),
            })
            {
                // The marriage has to be a parent's, and the candidate is the
                // person on the other end of it.
                if (!parents.Contains(otherId) || parents.Contains(spouseId))
                {
                    continue;
                }

                if (spouseId == personId || labelled.Contains(spouseId))
                {
                    continue;
                }

                if (!byId.TryGetValue(spouseId, out var candidate)
                    || !byId.TryGetValue(otherId, out var via))
                {
                    continue;
                }

                // A phantom stands for an ancestor nobody has identified, so it
                // cannot be offered as somebody a family would name as a
                // stepparent.
                if (candidate.IsPhantom)
                {
                    continue;
                }

                // One offer per person, even when two of the child's parents
                // married them. The label records a pair, so a second row would be
                // a second way to write the same fact and the duplicate guard
                // would refuse it.
                if (!offered.Add(spouseId))
                {
                    continue;
                }

                rows.Add(new StepparentCandidateDto(
                    PersonSummaryDto.From(candidate), marriage.Id, Name(via)));
            }
        }

        return [.. rows.OrderBy(r => r.Person.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static string Name(Person person) =>
        person.IsPhantom ? "an unidentified ancestor" : $"{person.FirstName} {person.LastName}";
}
