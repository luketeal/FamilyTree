using FamilyTree.Application.Common;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Repositories;

namespace FamilyTree.Application.Tests;

/// <summary>
/// The four repository interfaces and the administration seam, backed by lists.
/// </summary>
/// <remarks>
/// Hand-written rather than Moq, because import reads the tree and then writes it
/// back: the behaviour under test is what the store contains afterwards, and a
/// mock that returns canned lists cannot answer that. These still stand at the
/// same boundary the discipline names — the domain repository interfaces — so
/// nothing here touches IndexedDB or a browser.
/// </remarks>
internal sealed class InMemoryTree
{
    public List<Person> People { get; private set; } = [];

    public List<BiologicalParentChild> BiologicalLinks { get; private set; } = [];

    public List<AdoptiveParentChild> AdoptiveLinks { get; private set; } = [];

    public List<Marriage> Marriages { get; private set; } = [];

    /// <summary>How many times the whole tree was replaced.</summary>
    /// <remarks>
    /// Pinned by the tests because "one call" is the API-seam requirement, not an
    /// implementation detail: four separate writes can half-apply and leave
    /// relationships pointing at people who were never stored.
    /// </remarks>
    public int ReplaceCount { get; private set; }

    /// <summary>How many times a biological parent was swapped through the atomic path.</summary>
    public int BiologicalReplaceCount { get; set; }

    public IPersonRepository PersonRepository => new FakePersonRepository(this);

    public IBiologicalRelationshipRepository BiologicalRepository => new FakeBiologicalRepository(this);

    public IAdoptiveRelationshipRepository AdoptiveRepository => new FakeAdoptiveRepository(this);

    public IMarriageRepository MarriageRepository => new FakeMarriageRepository(this);

    public ITreeDataAdministration Administration => new Administrator(this);

    public InMemoryTree With(params Person[] people)
    {
        People.AddRange(people);
        return this;
    }

    public InMemoryTree With(params BiologicalParentChild[] links)
    {
        BiologicalLinks.AddRange(links);
        return this;
    }

    public InMemoryTree With(params AdoptiveParentChild[] links)
    {
        AdoptiveLinks.AddRange(links);
        return this;
    }

    public InMemoryTree With(params Marriage[] marriages)
    {
        Marriages.AddRange(marriages);
        return this;
    }

    private sealed class FakePersonRepository(InMemoryTree tree) : IPersonRepository
    {
        public Task<Person?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(tree.People.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<Person>> GetByIdsAsync(
            IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Person>>([.. tree.People.Where(p => ids.Contains(p.Id))]);

        public Task<IReadOnlyList<Person>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Person>>([.. tree.People]);

        public Task AddAsync(Person person, CancellationToken ct = default)
        {
            tree.People.Add(person);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Person person, CancellationToken ct = default)
        {
            tree.People.RemoveAll(p => p.Id == person.Id);
            tree.People.Add(person);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            tree.People.RemoveAll(p => p.Id == id);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(tree.People.Any(p => p.Id == id));
    }

    private sealed class FakeBiologicalRepository(InMemoryTree tree) : IBiologicalRelationshipRepository
    {
        public Task<IReadOnlyList<BiologicalParentChild>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BiologicalParentChild>>([.. tree.BiologicalLinks]);

        public Task<IReadOnlyList<BiologicalParentChild>> GetParentLinksForChildAsync(
            Guid childId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BiologicalParentChild>>(
                [.. tree.BiologicalLinks.Where(l => l.ChildId == childId)]);

        public Task<IReadOnlyList<BiologicalParentChild>> GetParentLinksForChildrenAsync(
            IReadOnlyCollection<Guid> childIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BiologicalParentChild>>(
                [.. tree.BiologicalLinks.Where(l => childIds.Contains(l.ChildId))]);

        public Task<IReadOnlyList<BiologicalParentChild>> GetChildLinksForParentAsync(
            Guid parentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BiologicalParentChild>>(
                [.. tree.BiologicalLinks.Where(l => l.ParentId == parentId)]);

        public Task<BiologicalParentChild?> GetAsync(Guid parentId, Guid childId, CancellationToken ct = default) =>
            Task.FromResult(tree.BiologicalLinks.FirstOrDefault(
                l => l.ParentId == parentId && l.ChildId == childId));

        public Task AddAsync(BiologicalParentChild link, CancellationToken ct = default)
        {
            tree.BiologicalLinks.Add(link);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            tree.BiologicalLinks.RemoveAll(l => l.Id == id);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Both halves together, matching the single IndexedDB transaction the
        /// real implementation uses. Counted, because "one call" is the property
        /// the seam discipline asks for and a test can only see it here.
        /// </summary>
        public Task ReplaceParentAsync(
            Guid oldLinkId, BiologicalParentChild newLink, CancellationToken ct = default)
        {
            tree.BiologicalReplaceCount++;
            tree.BiologicalLinks.RemoveAll(l => l.Id == oldLinkId);
            tree.BiologicalLinks.Add(newLink);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAdoptiveRepository(InMemoryTree tree) : IAdoptiveRelationshipRepository
    {
        public Task<IReadOnlyList<AdoptiveParentChild>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AdoptiveParentChild>>([.. tree.AdoptiveLinks]);

        public Task<IReadOnlyList<AdoptiveParentChild>> GetParentLinksForChildAsync(
            Guid childId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AdoptiveParentChild>>(
                [.. tree.AdoptiveLinks.Where(l => l.ChildId == childId)]);

        public Task<IReadOnlyList<AdoptiveParentChild>> GetParentLinksForChildrenAsync(
            IReadOnlyCollection<Guid> childIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AdoptiveParentChild>>(
                [.. tree.AdoptiveLinks.Where(l => childIds.Contains(l.ChildId))]);

        public Task<IReadOnlyList<AdoptiveParentChild>> GetChildLinksForParentAsync(
            Guid parentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AdoptiveParentChild>>(
                [.. tree.AdoptiveLinks.Where(l => l.ParentId == parentId)]);

        public Task<AdoptiveParentChild?> GetAsync(Guid parentId, Guid childId, CancellationToken ct = default) =>
            Task.FromResult(tree.AdoptiveLinks.FirstOrDefault(
                l => l.ParentId == parentId && l.ChildId == childId));

        public Task AddAsync(AdoptiveParentChild link, CancellationToken ct = default)
        {
            tree.AdoptiveLinks.Add(link);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(AdoptiveParentChild link, CancellationToken ct = default)
        {
            tree.AdoptiveLinks.RemoveAll(l => l.Id == link.Id);
            tree.AdoptiveLinks.Add(link);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            tree.AdoptiveLinks.RemoveAll(l => l.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMarriageRepository(InMemoryTree tree) : IMarriageRepository
    {
        public Task<IReadOnlyList<Marriage>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Marriage>>([.. tree.Marriages]);

        public Task<Marriage?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(tree.Marriages.FirstOrDefault(m => m.Id == id));

        public Task<IReadOnlyList<Marriage>> GetForPersonAsync(Guid personId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Marriage>>(
                [.. tree.Marriages.Where(m => m.Spouse1Id == personId || m.Spouse2Id == personId)]);

        public Task AddAsync(Marriage marriage, CancellationToken ct = default)
        {
            tree.Marriages.Add(marriage);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Marriage marriage, CancellationToken ct = default)
        {
            tree.Marriages.RemoveAll(m => m.Id == marriage.Id);
            tree.Marriages.Add(marriage);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            tree.Marriages.RemoveAll(m => m.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class Administrator(InMemoryTree tree) : ITreeDataAdministration
    {
        public Task LoadSampleFamilyAsync(CancellationToken ct = default) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task ReplaceAllAsync(TreeSnapshot snapshot, CancellationToken ct = default)
        {
            tree.ReplaceCount++;
            tree.People = [.. snapshot.People];
            tree.BiologicalLinks = [.. snapshot.BiologicalLinks];
            tree.AdoptiveLinks = [.. snapshot.AdoptiveLinks];
            tree.Marriages = [.. snapshot.Marriages];
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken ct = default)
        {
            tree.People.Clear();
            tree.BiologicalLinks.Clear();
            tree.AdoptiveLinks.Clear();
            tree.Marriages.Clear();
            return Task.CompletedTask;
        }

        public Task<StorageDurability> EnsureDurableAsync(CancellationToken ct = default) =>
            Task.FromResult(new StorageDurability(true, true));
    }
}

/// <summary>A clock that does not move, so "N days ago" is a fact rather than a wait.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
