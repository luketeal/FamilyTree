using FamilyTree.Domain.Enums;

namespace FamilyTree.Domain.Entities;

public sealed class BiologicalParentChild
{
    /// <summary>
    /// Biology allows two, so the tree does too.
    /// </summary>
    /// <remarks>
    /// Lives on the entity because it is a fact about the relationship rather
    /// than a policy of any one screen, but it cannot be <em>enforced</em> here:
    /// a link sees only itself, and the rule is about the set of links naming
    /// one child. <c>BiologicalRelationshipService</c> is where that set exists.
    /// </remarks>
    public const int MaxParentsPerChild = 2;

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

    /// <summary>Reconstitutes a link from storage, preserving its identity.</summary>
    public static BiologicalParentChild Rehydrate(
        Guid id, Guid parentId, Guid childId, RelationshipCertainty certainty) => new()
        {
            Id = id,
            ParentId = parentId,
            ChildId = childId,
            Certainty = certainty,
        };
}
