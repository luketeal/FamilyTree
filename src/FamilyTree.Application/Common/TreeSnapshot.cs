using FamilyTree.Domain.Entities;

namespace FamilyTree.Application.Common;

/// <summary>
/// The whole tree in one value: every record the app currently persists.
/// </summary>
/// <remarks>
/// Exists so a multi-record mutation is a single call. Import rewrites people,
/// biological links, adoptive links, marriages and stepparent labels together,
/// and a sequence of five repository writes can half-apply — leaving links
/// pointing at people who were never written. Against an HTTP-backed
/// implementation of the same interfaces that also becomes five requests where
/// one belongs.
///
/// Stepparent labels joined the rest in PR 9, alongside the repository that can
/// write them. The store clears every object store in the same transaction, so
/// omitting them here would have made every import delete them.
/// </remarks>
public sealed record TreeSnapshot(
    IReadOnlyList<Person> People,
    IReadOnlyList<BiologicalParentChild> BiologicalLinks,
    IReadOnlyList<AdoptiveParentChild> AdoptiveLinks,
    IReadOnlyList<Marriage> Marriages,
    IReadOnlyList<StepparentRelationship> StepparentLinks)
{
    public static TreeSnapshot Empty { get; } = new([], [], [], [], []);

    public int RelationshipCount =>
        BiologicalLinks.Count + AdoptiveLinks.Count + Marriages.Count + StepparentLinks.Count;

    public int RecordCount => People.Count + RelationshipCount;
}
