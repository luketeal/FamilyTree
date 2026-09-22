using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

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

/// <summary>
/// Somebody on the other end of an adoptive link, as a profile section needs them.
/// </summary>
/// <remarks>
/// A separate shape from <see cref="RelatedPersonDto"/> rather than a nullable
/// date bolted onto it. The adoption date is not an optional extra on a
/// relationship that might not have one — it is the field US-015 and US-019 both
/// build their section around, and the two stories ask for it to be *said* when
/// absent. Widening the biological DTO would have put a date on sibling and
/// biological-parent rows that can never carry one.
/// </remarks>
public sealed record AdoptiveRelativeDto(
    Guid LinkId,
    PersonSummaryDto Person,
    RelationshipCertainty Certainty,
    bool IsPhantom,
    PartialDate? AdoptionDate)
{
    /// <summary>
    /// The adoption date, or "Date unknown". US-015 and US-019.
    /// </summary>
    /// <remarks>
    /// Here rather than in the component so the parents and children sections
    /// cannot word it differently, and so the absent case is covered by a unit
    /// test rather than only by looking at the page. An adoption whose date
    /// nobody recorded is the ordinary state of an old record, so it reads as a
    /// fact about the research rather than as a blank.
    /// </remarks>
    public string AdoptionLabel => AdoptionDate?.ToString() ?? "Date unknown";
}

/// <summary>
/// Everything the adoptive half of a profile shows, from one service call.
/// </summary>
/// <remarks>
/// Both sections in one DTO for the same reason the biological one groups three:
/// they resolve overlapping sets of people, and asking twice would fetch the same
/// records twice — free against IndexedDB, two requests once the seam is swapped.
/// <para>
/// There is no <c>UnknownParentSlots</c> counterpart. Biological parenthood has
/// exactly two slots, so an empty one is a known gap worth drawing; adoptive
/// parenthood has no cap (US-014), so there is no such thing as a missing
/// adoptive parent to leave a space for.
/// </para>
/// </remarks>
public sealed record AdoptiveRelationshipsDto(
    IReadOnlyList<AdoptiveRelativeDto> Parents,
    IReadOnlyList<AdoptiveRelativeDto> Children)
{
    public static AdoptiveRelationshipsDto Empty { get; } = new([], []);

    public bool IsEmpty => Parents.Count == 0 && Children.Count == 0;
}

/// <summary>
/// A marriage or partnership as a profile row needs it (US-022).
/// </summary>
/// <remarks>
/// Named for the row rather than the record: it holds the <em>other</em> spouse,
/// because one marriage appears on two profiles and each shows the person the
/// reader is not currently looking at. The record's own pair of ids would make
/// every component work out which end it was standing on.
/// <para>
/// Gender-neutral throughout, in the type as well as in the wording: there is no
/// husband or wife field to fill in, so US-044 cannot be violated by a component
/// picking the wrong one.
/// </para>
/// </remarks>
public sealed record MarriageDto(
    Guid MarriageId,
    PersonSummaryDto Spouse,
    PartialDate StartDate,
    string? StartPlace,
    PartialDate? EndDate,
    MarriageEndReason? EndReason,
    RelationshipCertainty Certainty)
{
    /// <summary>
    /// Whether this is a current marriage. US-022 asks for the ongoing one to be
    /// distinguished, and the domain's own definition is used rather than a
    /// second one here: a marriage is ongoing until something says how it ended.
    /// </summary>
    public bool IsOngoing => EndDate is null && EndReason is null;

    /// <summary>
    /// "1946 – 1973 (Widowed)", or "1946 – Ongoing". US-022, US-025, US-027.
    /// </summary>
    /// <remarks>
    /// Worded here rather than in the component so the profile, the toast and any
    /// later tree label cannot describe the same marriage differently, and so the
    /// ended and ongoing cases are covered by a unit test rather than by looking
    /// at the page. An en dash separates the years because this is a date range
    /// and not a subtraction.
    /// </remarks>
    public string DatesLabel
    {
        get
        {
            var ending = EndDate?.ToString() ?? (IsOngoing ? "Ongoing" : "date unknown");
            var reason = EndReasonLabel;
            return reason is null
                ? $"{StartDate} – {ending}"
                : $"{StartDate} – {ending} ({reason})";
        }
    }

    /// <summary>
    /// How it ended, in the words the stories ask for, or null while it has not.
    /// </summary>
    /// <remarks>
    /// "Widowed" rather than "Death of spouse" (US-026) and "Divorced" rather
    /// than "Divorce" (US-025), because the row reads as a state of the marriage
    /// rather than as an event type from a dropdown. <see cref="Separation"/> is
    /// included for completeness even though no story names its wording, and
    /// <see cref="MarriageEndReason.Unknown"/> deliberately reads as "Ended": the
    /// marriage is over and nobody recorded why, which is a fact about the
    /// research rather than an empty field.
    /// </remarks>
    public string? EndReasonLabel => EndReason switch
    {
        MarriageEndReason.Divorce => "Divorced",
        MarriageEndReason.DeathOfSpouse => "Widowed",
        MarriageEndReason.Annulment => "Annulled",
        MarriageEndReason.Separation => "Separated",
        MarriageEndReason.Unknown => "Ended",
        _ => null,
    };
}

/// <summary>
/// Every marriage on one profile, chronologically. US-022, US-042.
/// </summary>
public sealed record MarriagesDto(IReadOnlyList<MarriageDto> Marriages)
{
    public static MarriagesDto Empty { get; } = new([]);

    public bool IsEmpty => Marriages.Count == 0;

    /// <summary>
    /// The current marriage, when there is exactly one.
    /// </summary>
    /// <remarks>
    /// Null when two are somehow ongoing at once rather than picking one: that
    /// state is reachable (US-042 warns about overlaps and saves anyway), and a
    /// profile silently nominating one of them as <em>the</em> current marriage
    /// would be asserting something nobody recorded.
    /// </remarks>
    public MarriageDto? Current =>
        Marriages.Count(m => m.IsOngoing) == 1 ? Marriages.First(m => m.IsOngoing) : null;
}

/// <summary>
/// An end date offered from a spouse's recorded death. US-026.
/// </summary>
/// <remarks>
/// Carries whose death it came from, because the offer is only trustworthy if it
/// says so — "we filled in 1973 for you" is a guess the user has to be able to
/// check, and either spouse may be the one who died.
/// </remarks>
public sealed record EndDateSuggestion(PartialDate Date, string PersonName);

/// <summary>
/// A step relationship as a profile row needs it. US-038.
/// </summary>
/// <remarks>
/// Carries the marriage it rests on, and the parent it runs through, because a
/// step relationship is not a fact about two people on its own: it says "the
/// person my parent married". A row that only named the stepparent would leave
/// the reader unable to tell which of two marriages put them there, and unable
/// to understand why removing a marriage record removed this.
/// </remarks>
public sealed record StepRelativeDto(
    Guid LinkId,
    PersonSummaryDto Person,
    Guid MarriageId,
    string ViaName,
    bool ViaIsOngoing);

/// <summary>
/// Somebody who could be labelled a stepparent, and the marriage that would
/// justify it. US-038.
/// </summary>
/// <remarks>
/// Offered as a derived list rather than through the person search, because
/// US-038 asks for the action to sit next to a parent's current spouses. That is
/// the whole rule: a stepparent is somebody a parent married, so searching the
/// whole tree for one would invite recording a step relationship that nothing in
/// the record supports.
/// </remarks>
public sealed record StepparentCandidateDto(
    PersonSummaryDto Person,
    Guid MarriageId,
    string ViaName);

/// <summary>
/// Both step sections of a profile plus the candidates for a new label, from one
/// service call.
/// </summary>
/// <remarks>
/// One DTO because the three overlap heavily in their reads: the candidate list
/// needs the subject's parents and their marriages, the stepparent list needs the
/// same marriages to say what each label rests on, and all three resolve people
/// from one batch.
/// </remarks>
public sealed record StepFamilyDto(
    IReadOnlyList<StepRelativeDto> Stepparents,
    IReadOnlyList<StepRelativeDto> Stepchildren,
    IReadOnlyList<StepparentCandidateDto> Candidates)
{
    public static StepFamilyDto Empty { get; } = new([], [], []);

    public bool IsEmpty =>
        Stepparents.Count == 0 && Stepchildren.Count == 0 && Candidates.Count == 0;
}
