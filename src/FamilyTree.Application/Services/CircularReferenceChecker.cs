using FamilyTree.Domain.Repositories;

namespace FamilyTree.Application.Services;

/// <summary>
/// Guards against a person becoming their own ancestor. US-040.
/// </summary>
/// <remarks>
/// A person can have both biological and adoptive parents (US-039), so the
/// family graph is a DAG rather than a tree and both link types have to be
/// walked together — a cycle can run up one kind of edge and back down the
/// other, and checking either alone would miss it.
/// </remarks>
public sealed class CircularReferenceChecker(
    IBiologicalRelationshipRepository biological,
    IAdoptiveRelationshipRepository adoptive)
{
    /// <summary>
    /// True when making <paramref name="proposedParentId"/> a parent of
    /// <paramref name="childId"/> would create a cycle — that is, when the
    /// child already appears among the proposed parent's ancestors.
    /// </summary>
    public async Task<bool> WouldCreateCycleAsync(
        Guid proposedParentId,
        Guid childId,
        CancellationToken ct = default)
    {
        // A person cannot be their own parent, and no repository read is needed
        // to know it.
        if (proposedParentId == childId)
        {
            return true;
        }

        // Breadth-first by generation rather than by node: one pair of reads per
        // level, however wide the level is. Depth is bounded by the tree's
        // generations, so this stays a handful of calls even on a large tree.
        var visited = new HashSet<Guid> { proposedParentId };
        var frontier = new List<Guid> { proposedParentId };

        while (frontier.Count > 0)
        {
            var biologicalLinks = await biological.GetParentLinksForChildrenAsync(frontier, ct);
            var adoptiveLinks = await adoptive.GetParentLinksForChildrenAsync(frontier, ct);

            var parents = biologicalLinks.Select(l => l.ParentId)
                .Concat(adoptiveLinks.Select(l => l.ParentId));

            var next = new List<Guid>();
            foreach (var parentId in parents)
            {
                if (parentId == childId)
                {
                    return true;
                }

                // visited also protects the walk from an existing cycle in the
                // stored data, which would otherwise loop forever.
                if (visited.Add(parentId))
                {
                    next.Add(parentId);
                }
            }

            frontier = next;
        }

        return false;
    }
}
