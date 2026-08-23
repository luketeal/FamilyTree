using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;

namespace FamilyTree.Application.Common;

/// <summary>
/// Somebody on the other end of a relationship, as a profile section needs them.
/// </summary>
/// <remarks>
/// Carries the link's own id as well as the person's, because every action a
/// relationship section offers — remove, replace, change certainty — addresses
/// the link rather than either person. Without it the component would have to
/// look the link up again by its endpoints, which is a read per row.
/// </remarks>
public sealed record RelatedPersonDto(
    Guid LinkId,
    PersonSummaryDto Person,
    RelationshipCertainty Certainty,
    bool IsPhantom);

/// <summary>How much of a sibling somebody is (US-037, US-051).</summary>
public enum SiblingKind
{
    /// <summary>Both biological parents in common.</summary>
    Full,

    /// <summary>Exactly one biological parent in common.</summary>
    Half,
}

/// <summary>
/// A sibling, derived rather than stored.
/// </summary>
/// <remarks>
/// No link id: there is no sibling record to address. Siblings exist because two
/// people name the same parent, so the way to change one is to change a parent
/// link — which is why this section offers navigation and nothing else.
/// </remarks>
public sealed record SiblingDto(
    PersonSummaryDto Person,
    SiblingKind Kind,
    IReadOnlyList<string> SharedParentNames);

/// <summary>
/// Everything the biological half of a profile shows, from one service call.
/// </summary>
/// <remarks>
/// One DTO rather than three calls because the three sections share their
/// reads: the subject's parent links determine the parents section and are the
/// starting point for siblings, and the people named across all three are
/// fetched together. Three separate calls would re-read the same links and
/// resolve the same people up to three times, which is free against IndexedDB
/// and three times the requests once the seam is swapped.
/// </remarks>
public sealed record BiologicalRelationshipsDto(
    IReadOnlyList<RelatedPersonDto> Parents,
    IReadOnlyList<RelatedPersonDto> Children,
    IReadOnlyList<SiblingDto> Siblings)
{
    public static BiologicalRelationshipsDto Empty { get; } = new([], [], []);

    /// <summary>
    /// How many parent slots are still empty. US-008 and US-041 both ask for
    /// these to read as "Unknown" with an Add prompt rather than as an error —
    /// not knowing an ancestor is the normal state of genealogy, not a mistake.
    /// </summary>
    public int UnknownParentSlots =>
        Math.Max(0, BiologicalParentChild.MaxParentsPerChild - Parents.Count);

    public IEnumerable<SiblingDto> FullSiblings => Siblings.Where(s => s.Kind == SiblingKind.Full);

    public IEnumerable<SiblingDto> HalfSiblings => Siblings.Where(s => s.Kind == SiblingKind.Half);
}
