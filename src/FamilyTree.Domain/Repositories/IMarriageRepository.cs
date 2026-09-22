using FamilyTree.Domain.Entities;

namespace FamilyTree.Domain.Repositories;

public interface IMarriageRepository
{
    /// <summary>The whole set, for tree building, counts and export.</summary>
    Task<IReadOnlyList<Marriage>> GetAllAsync(CancellationToken ct = default);

    Task<Marriage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Marriage>> GetForPersonAsync(Guid personId, CancellationToken ct = default);

    /// <summary>
    /// Marriages involving any of these people, in one read.
    /// </summary>
    /// <remarks>
    /// A stepparent candidate list asks "who are this child's parents married
    /// to", which is a handful of people at once. Per person it is the N+1 the
    /// seam discipline forbids.
    /// </remarks>
    Task<IReadOnlyList<Marriage>> GetForPeopleAsync(
        IReadOnlyCollection<Guid> personIds, CancellationToken ct = default);

    /// <summary>
    /// Marriages by id, in one read.
    /// </summary>
    /// <remarks>
    /// A stepparent label names the marriage that justifies it, and that marriage
    /// is not always reachable from the people a profile already knows about: if
    /// the parent link the label ran through is removed, the marriage is still
    /// there and still the label's justification, but nothing on the stepchild's
    /// profile points at it any more. Reading them by id is what keeps the two
    /// ends of that record agreeing about it.
    /// </remarks>
    Task<IReadOnlyList<Marriage>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    Task AddAsync(Marriage marriage, CancellationToken ct = default);
    Task UpdateAsync(Marriage marriage, CancellationToken ct = default);

    /// <summary>
    /// Removes the marriage and every stepparent label that named it. US-024,
    /// US-038.
    /// </summary>
    /// <remarks>
    /// The cascade is part of the delete rather than a second call the caller
    /// makes afterwards, for two reasons. It spans two record types, so as two
    /// calls it can half-apply and leave a label pointing at a marriage that no
    /// longer exists — a step relationship nothing in the app can explain or
    /// reach to remove. And a non-cascading delete alongside a cascading one
    /// would be a trap: the orphaning version is the shorter name and the one a
    /// caller reaches for first.
    /// <para>
    /// Over a future HTTP implementation this is the one <c>DELETE
    /// /marriages/{id}</c> the server cascades, which is what the seam is meant
    /// to keep available.
    /// </para>
    /// </remarks>
    /// <returns>How many stepparent labels went with it.</returns>
    Task<int> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Swaps a marriage record for one naming a different spouse, as a single
    /// mutation. US-023.
    /// </summary>
    /// <remarks>
    /// Exists for the same reason as the biological and adoptive replaces: the
    /// interface could not otherwise express the change as one call, and a delete
    /// followed by an add is the half-failing sequence the API-seam rule exists
    /// to prevent. Here the failure mode is a marriage that vanished from both
    /// spouses' profiles with nothing recorded in its place.
    /// <para>
    /// A new record rather than an edit, because <see cref="Marriage"/> does not
    /// let its spouses be reassigned: naming a different person is a different
    /// marriage, not a corrected field. The stepparent labels hanging off the old
    /// record are removed in the same transaction — a label means "stepparent by
    /// virtue of <em>this</em> marriage", and that marriage is what is being
    /// withdrawn. Callers are expected to say how many went, since a label
    /// disappearing unannounced is the silent loss this project treats as a bug.
    /// </para>
    /// </remarks>
    /// <returns>How many stepparent labels went with the old record.</returns>
    Task<int> ReplaceSpouseAsync(
        Guid oldMarriageId, Marriage replacement, CancellationToken ct = default);
}
