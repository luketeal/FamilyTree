using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Domain.Entities;

public sealed class AdoptiveParentChild
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ParentId { get; private set; }
    public Guid ChildId { get; private set; }
    public PartialDate? AdoptionDate { get; private set; }
    public RelationshipCertainty Certainty { get; private set; }

    public Person Parent { get; private set; } = null!;
    public Person Child { get; private set; } = null!;

    private AdoptiveParentChild() { }

    public AdoptiveParentChild(Guid parentId, Guid childId, PartialDate? adoptionDate = null, RelationshipCertainty certainty = RelationshipCertainty.Confirmed)
    {
        if (parentId == Guid.Empty) throw new ArgumentException("ParentId must not be empty.", nameof(parentId));
        if (childId == Guid.Empty) throw new ArgumentException("ChildId must not be empty.", nameof(childId));
        if (parentId == childId) throw new ArgumentException("A person cannot be their own adoptive parent.");

        ParentId = parentId;
        ChildId = childId;
        AdoptionDate = adoptionDate;
        Certainty = certainty;
    }

    public void UpdateAdoptionDate(PartialDate? adoptionDate) => AdoptionDate = adoptionDate;

    public void UpdateCertainty(RelationshipCertainty certainty) => Certainty = certainty;

    /// <summary>Reconstitutes a link from storage, preserving its identity.</summary>
    public static AdoptiveParentChild Rehydrate(
        Guid id, Guid parentId, Guid childId, PartialDate? adoptionDate,
        RelationshipCertainty certainty) => new()
        {
            Id = id,
            ParentId = parentId,
            ChildId = childId,
            AdoptionDate = adoptionDate,
            Certainty = certainty,
        };
}
