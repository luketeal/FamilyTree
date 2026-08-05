using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Storage.Browser.Records;

/// <summary>
/// Flat mirrors of the domain entities, one per IndexedDB object store.
/// </summary>
/// <remarks>
/// The entities cannot be serialized directly: they have private constructors
/// and private setters, and their navigation collections point back at each
/// other, so a JSON serializer both fails to reconstruct them and recurses
/// forever trying. These records carry exactly the persisted state, matching
/// the shape a table — or a future API resource — would have.
/// </remarks>
public sealed record PartialDateRecord(int Year, int? Month, int? Day, bool IsApproximate)
{
    public static PartialDateRecord? From(PartialDate? date) => date is null
        ? null
        : new PartialDateRecord(date.Year, date.Month, date.Day, date.IsApproximate);

    public PartialDate ToDomain() => (Month, Day) switch
    {
        (null, _) => PartialDate.FromYear(Year, IsApproximate),
        (int month, null) => PartialDate.FromYearMonth(Year, month, IsApproximate),
        (int month, int day) => PartialDate.FromYearMonthDay(Year, month, day, IsApproximate),
    };
}

public sealed record PersonRecord(
    Guid Id,
    string FirstName,
    string LastName,
    string? BirthSurname,
    PartialDateRecord? BirthDate,
    string? BirthPlace,
    PartialDateRecord? DeathDate,
    string? DeathPlace,
    Gender Gender,
    string? PhotoPath,
    string? Notes,
    bool IsPhantom)
{
    public static PersonRecord From(Person p) => new(
        p.Id, p.FirstName, p.LastName, p.BirthSurname,
        PartialDateRecord.From(p.BirthDate), p.BirthPlace,
        PartialDateRecord.From(p.DeathDate), p.DeathPlace,
        p.Gender, p.PhotoPath, p.Notes, p.IsPhantom);

    public Person ToDomain() => Person.Rehydrate(
        Id, FirstName, LastName, BirthSurname,
        BirthDate?.ToDomain(), BirthPlace,
        DeathDate?.ToDomain(), DeathPlace,
        Gender, PhotoPath, Notes, IsPhantom);
}

public sealed record BiologicalLinkRecord(Guid Id, Guid ParentId, Guid ChildId, RelationshipCertainty Certainty)
{
    public static BiologicalLinkRecord From(BiologicalParentChild l) =>
        new(l.Id, l.ParentId, l.ChildId, l.Certainty);

    public BiologicalParentChild ToDomain() =>
        BiologicalParentChild.Rehydrate(Id, ParentId, ChildId, Certainty);
}

public sealed record AdoptiveLinkRecord(
    Guid Id, Guid ParentId, Guid ChildId, PartialDateRecord? AdoptionDate, RelationshipCertainty Certainty)
{
    public static AdoptiveLinkRecord From(AdoptiveParentChild l) =>
        new(l.Id, l.ParentId, l.ChildId, PartialDateRecord.From(l.AdoptionDate), l.Certainty);

    public AdoptiveParentChild ToDomain() =>
        AdoptiveParentChild.Rehydrate(Id, ParentId, ChildId, AdoptionDate?.ToDomain(), Certainty);
}

public sealed record MarriageRecord(
    Guid Id, Guid Spouse1Id, Guid Spouse2Id,
    PartialDateRecord StartDate, string? StartPlace,
    PartialDateRecord? EndDate, MarriageEndReason? EndReason,
    RelationshipCertainty Certainty)
{
    public static MarriageRecord From(Marriage m) => new(
        m.Id, m.Spouse1Id, m.Spouse2Id,
        PartialDateRecord.From(m.StartDate)!, m.StartPlace,
        PartialDateRecord.From(m.EndDate), m.EndReason, m.Certainty);

    public Marriage ToDomain() => Marriage.Rehydrate(
        Id, Spouse1Id, Spouse2Id, StartDate.ToDomain(), StartPlace,
        EndDate?.ToDomain(), EndReason, Certainty);
}

public sealed record StepparentLinkRecord(Guid Id, Guid StepparentId, Guid StepchildId, Guid MarriageId)
{
    public static StepparentLinkRecord From(StepparentRelationship s) =>
        new(s.Id, s.StepparentId, s.StepchildId, s.MarriageId);

    public StepparentRelationship ToDomain() =>
        StepparentRelationship.Rehydrate(Id, StepparentId, StepchildId, MarriageId);
}

/// <summary>
/// The whole dataset in one shape, for atomic replacement (sample data, import)
/// and for the schema stamp that lets a shape change be detected rather than
/// crashing on stale records.
/// </summary>
public sealed record DatasetRecord(
    IReadOnlyList<PersonRecord> People,
    IReadOnlyList<BiologicalLinkRecord> BiologicalLinks,
    IReadOnlyList<AdoptiveLinkRecord> AdoptiveLinks,
    IReadOnlyList<MarriageRecord> Marriages,
    IReadOnlyList<StepparentLinkRecord> StepparentLinks);
