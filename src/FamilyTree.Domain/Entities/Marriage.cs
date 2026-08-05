using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Domain.Entities;

public sealed class Marriage
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid Spouse1Id { get; private set; }
    public Guid Spouse2Id { get; private set; }
    public PartialDate StartDate { get; private set; } = null!;
    public string? StartPlace { get; private set; }
    public PartialDate? EndDate { get; private set; }
    public MarriageEndReason? EndReason { get; private set; }
    public RelationshipCertainty Certainty { get; private set; }

    public Person Spouse1 { get; private set; } = null!;
    public Person Spouse2 { get; private set; } = null!;

    private readonly List<StepparentRelationship> _stepparentRelationships = [];
    public IReadOnlyList<StepparentRelationship> StepparentRelationships => _stepparentRelationships;

    private Marriage() { }

    public Marriage(Guid spouse1Id, Guid spouse2Id, PartialDate startDate, string? startPlace = null, RelationshipCertainty certainty = RelationshipCertainty.Confirmed)
    {
        if (spouse1Id == Guid.Empty) throw new ArgumentException("Spouse1Id must not be empty.", nameof(spouse1Id));
        if (spouse2Id == Guid.Empty) throw new ArgumentException("Spouse2Id must not be empty.", nameof(spouse2Id));
        if (spouse1Id == spouse2Id) throw new ArgumentException("A person cannot marry themselves.");
        ArgumentNullException.ThrowIfNull(startDate);

        Spouse1Id = spouse1Id;
        Spouse2Id = spouse2Id;
        StartDate = startDate;
        StartPlace = startPlace?.Trim();
        Certainty = certainty;
    }

    public void UpdateDates(PartialDate startDate, string? startPlace, PartialDate? endDate, MarriageEndReason? endReason)
    {
        ArgumentNullException.ThrowIfNull(startDate);
        StartDate = startDate;
        StartPlace = startPlace?.Trim();
        EndDate = endDate;
        EndReason = endReason;
    }

    public void UpdateCertainty(RelationshipCertainty certainty) => Certainty = certainty;

    /// <summary>Reconstitutes a marriage from storage, preserving its identity.</summary>
    public static Marriage Rehydrate(
        Guid id, Guid spouse1Id, Guid spouse2Id, PartialDate startDate, string? startPlace,
        PartialDate? endDate, MarriageEndReason? endReason, RelationshipCertainty certainty) => new()
        {
            Id = id,
            Spouse1Id = spouse1Id,
            Spouse2Id = spouse2Id,
            StartDate = startDate,
            StartPlace = startPlace,
            EndDate = endDate,
            EndReason = endReason,
            Certainty = certainty,
        };

    public bool IsOngoing => EndDate is null && EndReason is null;
}
