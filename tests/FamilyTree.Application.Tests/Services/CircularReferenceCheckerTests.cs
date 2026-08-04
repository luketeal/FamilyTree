using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Repositories;
using Moq;

namespace FamilyTree.Application.Tests.Services;

public class CircularReferenceCheckerTests
{
    private readonly Mock<IBiologicalRelationshipRepository> _biological = new();
    private readonly Mock<IAdoptiveRelationshipRepository> _adoptive = new();

    private CircularReferenceChecker CreateChecker() => new(_biological.Object, _adoptive.Object);

    /// <summary>Wires the mocks so each child id resolves to the given parents.</summary>
    private void GivenBiologicalParents(Dictionary<Guid, Guid[]> parentsByChild)
    {
        _biological
            .Setup(r => r.GetParentLinksForChildrenAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids
                .SelectMany(id => parentsByChild.TryGetValue(id, out var parents) ? parents : [])
                .Select(parentId => new BiologicalParentChild(parentId, Guid.NewGuid()))
                .ToList());
    }

    private void GivenNoAdoptiveParents() =>
        _adoptive
            .Setup(r => r.GetParentLinksForChildrenAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

    private void GivenNoBiologicalParents() =>
        _biological
            .Setup(r => r.GetParentLinksForChildrenAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

    [Fact]
    public async Task AllowsAParentWhoIsUnrelated()
    {
        GivenNoBiologicalParents();
        GivenNoAdoptiveParents();

        var result = await CreateChecker().WouldCreateCycleAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task RejectsAPersonAsTheirOwnParent()
    {
        var person = Guid.NewGuid();

        var result = await CreateChecker().WouldCreateCycleAsync(person, person);

        Assert.True(result);
    }

    [Fact]
    public async Task RejectsAPersonAsTheirOwnParentWithoutReadingAnyRepository()
    {
        var person = Guid.NewGuid();

        await CreateChecker().WouldCreateCycleAsync(person, person);

        _biological.Verify(
            r => r.GetParentLinksForChildrenAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RejectsMakingAChildTheParentOfTheirOwnParent()
    {
        var child = Guid.NewGuid();
        var parent = Guid.NewGuid();

        // parent's parent is child, so child cannot also be parent's child.
        GivenBiologicalParents(new() { [parent] = [child] });
        GivenNoAdoptiveParents();

        var result = await CreateChecker().WouldCreateCycleAsync(parent, child);

        Assert.True(result);
    }

    [Fact]
    public async Task RejectsACycleSeveralGenerationsUp()
    {
        var child = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var grandparent = Guid.NewGuid();
        var greatGrandparent = Guid.NewGuid();

        GivenBiologicalParents(new()
        {
            [parent] = [grandparent],
            [grandparent] = [greatGrandparent],
            [greatGrandparent] = [child],
        });
        GivenNoAdoptiveParents();

        var result = await CreateChecker().WouldCreateCycleAsync(parent, child);

        Assert.True(result);
    }

    // US-039: a person may have both biological and adoptive parents, so a cycle
    // can run up one kind of edge and back down the other.
    [Fact]
    public async Task RejectsACycleThatCrossesFromBiologicalToAdoptiveLinks()
    {
        var child = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var adoptiveGrandparent = Guid.NewGuid();

        GivenBiologicalParents(new() { [parent] = [adoptiveGrandparent] });

        _adoptive
            .Setup(r => r.GetParentLinksForChildrenAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.Contains(adoptiveGrandparent)
                ? [new AdoptiveParentChild(child, adoptiveGrandparent)]
                : []);

        var result = await CreateChecker().WouldCreateCycleAsync(parent, child);

        Assert.True(result);
    }

    [Fact]
    public async Task AllowsAParentInADisconnectedBranch()
    {
        var child = Guid.NewGuid();
        var proposedParent = Guid.NewGuid();
        var unrelatedAncestor = Guid.NewGuid();

        GivenBiologicalParents(new() { [proposedParent] = [unrelatedAncestor] });
        GivenNoAdoptiveParents();

        var result = await CreateChecker().WouldCreateCycleAsync(proposedParent, child);

        Assert.False(result);
    }

    // Stored data can already contain a cycle — imported badly, or written before
    // this check existed. The walk must terminate rather than loop forever.
    [Fact]
    public async Task TerminatesWhenTheStoredGraphAlreadyContainsACycle()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        GivenBiologicalParents(new() { [a] = [b], [b] = [a] });
        GivenNoAdoptiveParents();

        var result = await CreateChecker()
            .WouldCreateCycleAsync(a, Guid.NewGuid())
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result);
    }

    // One pair of reads per generation, not per ancestor: the seam has to
    // survive being backed by HTTP one day.
    [Fact]
    public async Task ReadsOncePerGenerationRatherThanOncePerAncestor()
    {
        var proposedParent = Guid.NewGuid();
        var grandparentA = Guid.NewGuid();
        var grandparentB = Guid.NewGuid();

        GivenBiologicalParents(new() { [proposedParent] = [grandparentA, grandparentB] });
        GivenNoAdoptiveParents();

        await CreateChecker().WouldCreateCycleAsync(proposedParent, Guid.NewGuid());

        // Two generations reached: the proposed parent, then both grandparents
        // together in a single batched read.
        _biological.Verify(
            r => r.GetParentLinksForChildrenAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
