using FamilyTree.Domain.Entities;

namespace FamilyTree.Application.Common;

/// <summary>
/// The whole tree in one value: every record the app currently persists.
/// </summary>
/// <remarks>
/// Exists so a multi-record mutation is a single call. Import rewrites people,
/// biological links, adoptive links and marriages together, and a sequence of
/// four repository writes can half-apply — leaving links pointing at people who
/// were never written. Against an HTTP-backed implementation of the same
/// interfaces that also becomes four requests where one belongs.
///
/// Stepparent links are absent because no repository writes them yet (PR 9 adds
/// the interface). They must be added here in the same PR that adds that
/// repository, or import will silently discard them.
/// </remarks>
public sealed record TreeSnapshot(
    IReadOnlyList<Person> People,
    IReadOnlyList<BiologicalParentChild> BiologicalLinks,
    IReadOnlyList<AdoptiveParentChild> AdoptiveLinks,
    IReadOnlyList<Marriage> Marriages)
{
    public static TreeSnapshot Empty { get; } = new([], [], [], []);

    public int RelationshipCount => BiologicalLinks.Count + AdoptiveLinks.Count + Marriages.Count;

    public int RecordCount => People.Count + RelationshipCount;
}
