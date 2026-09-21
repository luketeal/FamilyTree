using FamilyTree.Domain.Entities;

namespace FamilyTree.Domain.Repositories;

/// <summary>
/// Stepparent labels, each hung off the marriage that justifies it (US-038).
/// </summary>
/// <remarks>
/// The store and the record have existed since PR 2; this is the first interface
/// over them, which is why the schema version moves in the same PR — until
/// something could write a stepparent link, a file that omitted the section was
/// telling the truth.
/// <para>
/// There is no <c>ReplaceAsync</c> here, and its absence is a decision rather
/// than an omission. Its counterparts on the biological and adoptive
/// repositories exist because those links carry fields — a date, a certainty —
/// that can be corrected while the link keeps its identity, so "change the
/// person" had to be distinguishable from "change the details". A stepparent
/// link is nothing but three ids: change any one of them and it is a different
/// claim about a different pair of people, which the UI expresses as removing
/// one label and applying another. A replace here would be a rename of delete
/// plus add with no atomicity to buy.
/// </para>
/// </remarks>
public interface IStepparentRelationshipRepository
{
    /// <summary>The whole set, for tree building, counts and export.</summary>
    Task<IReadOnlyList<StepparentRelationship>> GetAllAsync(CancellationToken ct = default);

    /// <summary>The labels naming this person as a stepchild.</summary>
    Task<IReadOnlyList<StepparentRelationship>> GetForStepchildAsync(
        Guid stepchildId, CancellationToken ct = default);

    /// <summary>The labels naming this person as a stepparent.</summary>
    Task<IReadOnlyList<StepparentRelationship>> GetForStepparentAsync(
        Guid stepparentId, CancellationToken ct = default);

    /// <summary>
    /// Labels for many stepchildren at once.
    /// </summary>
    /// <remarks>
    /// Exists for the reason the other bulk reads do: the tree view (PR 10) draws
    /// a step edge per child, and asking per child is the N+1 that is free
    /// against IndexedDB and a request storm over HTTP.
    /// </remarks>
    Task<IReadOnlyList<StepparentRelationship>> GetForStepchildrenAsync(
        IReadOnlyCollection<Guid> stepchildIds, CancellationToken ct = default);

    Task AddAsync(StepparentRelationship link, CancellationToken ct = default);

    /// <summary>
    /// Removes a label, reporting whether it was there.
    /// </summary>
    /// <remarks>
    /// The answer rather than void, so that "no such label" does not cost a read
    /// of the whole set to establish — which is what checking an id against
    /// <see cref="GetAllAsync"/> amounts to, and one request too many once this
    /// interface is backed by HTTP.
    /// </remarks>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
