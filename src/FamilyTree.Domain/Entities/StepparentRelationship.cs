namespace FamilyTree.Domain.Entities;

/// <summary>
/// Manually applied label connecting a stepparent to a stepchild via a specific marriage.
/// Deleting the underlying marriage cascades to remove this record (US-038).
/// </summary>
public sealed class StepparentRelationship
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid StepparentId { get; private set; }
    public Guid StepchildId { get; private set; }
    public Guid MarriageId { get; private set; }

    public Person Stepparent { get; private set; } = null!;
    public Person Stepchild { get; private set; } = null!;
    public Marriage Marriage { get; private set; } = null!;

    private StepparentRelationship() { }

    public StepparentRelationship(Guid stepparentId, Guid stepchildId, Guid marriageId)
    {
        if (stepparentId == Guid.Empty) throw new ArgumentException("StepparentId must not be empty.", nameof(stepparentId));
        if (stepchildId == Guid.Empty) throw new ArgumentException("StepchildId must not be empty.", nameof(stepchildId));
        if (marriageId == Guid.Empty) throw new ArgumentException("MarriageId must not be empty.", nameof(marriageId));
        if (stepparentId == stepchildId) throw new ArgumentException("A person cannot be their own stepparent.");

        StepparentId = stepparentId;
        StepchildId = stepchildId;
        MarriageId = marriageId;
    }
}
