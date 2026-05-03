using FamilyTree.Domain.Enums;

namespace FamilyTree.Domain.Entities;

public sealed class BiologicalParentChild
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ParentId { get; private set; }
    public Guid ChildId { get; private set; }
    public RelationshipCertainty Certainty { get; private set; }

    public Person Parent { get; private set; } = null!;
    public Person Child { get; private set; } = null!;

    private BiologicalParentChild() { }

    public BiologicalParentChild(Guid parentId, Guid childId, RelationshipCertainty certainty = RelationshipCertainty.Confirmed)
    {
        if (parentId == Guid.Empty) throw new ArgumentException("ParentId must not be empty.", nameof(parentId));
        if (childId == Guid.Empty) throw new ArgumentException("ChildId must not be empty.", nameof(childId));
        if (parentId == childId) throw new ArgumentException("A person cannot be their own biological parent.");

        ParentId = parentId;
        ChildId = childId;
        Certainty = certainty;
    }

    public void UpdateCertainty(RelationshipCertainty certainty) => Certainty = certainty;
}
