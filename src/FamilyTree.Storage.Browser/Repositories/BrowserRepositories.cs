using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Repositories;
using FamilyTree.Storage.Browser.Records;

namespace FamilyTree.Storage.Browser.Repositories;

/// <summary>
/// IndexedDB-backed implementations of the domain repository interfaces.
/// </summary>
/// <remarks>
/// These are the swap point described in ADR-004: a FamilyTree.Storage.Api
/// implementing the same interfaces over HttpClient would replace them as a DI
/// registration, with no change above this layer. That only stays true while
/// callers read in bulk, which is why the batched methods exist.
/// </remarks>
public sealed class BrowserPersonRepository(IndexedDbStore store) : IPersonRepository
{
    public async Task<Person?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        (await store.GetAsync<PersonRecord>(IndexedDbStore.People, id, ct))?.ToDomain();

    public async Task<IReadOnlyList<Person>> GetAllAsync(CancellationToken ct = default) =>
        (await store.GetAllAsync<PersonRecord>(IndexedDbStore.People, ct))
        .Select(r => r.ToDomain())
        .ToList();

    public Task AddAsync(Person person, CancellationToken ct = default) =>
        store.PutAsync(IndexedDbStore.People, PersonRecord.From(person), ct);

    public Task UpdateAsync(Person person, CancellationToken ct = default) =>
        store.PutAsync(IndexedDbStore.People, PersonRecord.From(person), ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        store.DeleteAsync(IndexedDbStore.People, id, ct);

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        await store.GetAsync<PersonRecord>(IndexedDbStore.People, id, ct) is not null;
}

public sealed class BrowserBiologicalRelationshipRepository(IndexedDbStore store)
    : IBiologicalRelationshipRepository
{
    private Task<IReadOnlyList<BiologicalLinkRecord>> AllAsync(CancellationToken ct) =>
        store.GetAllAsync<BiologicalLinkRecord>(IndexedDbStore.BiologicalLinks, ct);

    public async Task<IReadOnlyList<BiologicalParentChild>> GetAllAsync(CancellationToken ct = default) =>
        (await AllAsync(ct)).Select(l => l.ToDomain()).ToList();

    public async Task<IReadOnlyList<BiologicalParentChild>> GetParentLinksForChildAsync(
        Guid childId, CancellationToken ct = default) =>
        (await AllAsync(ct)).Where(l => l.ChildId == childId).Select(l => l.ToDomain()).ToList();

    public async Task<IReadOnlyList<BiologicalParentChild>> GetParentLinksForChildrenAsync(
        IReadOnlyCollection<Guid> childIds, CancellationToken ct = default)
    {
        if (childIds.Count == 0)
        {
            return [];
        }

        var wanted = childIds.ToHashSet();
        return (await AllAsync(ct)).Where(l => wanted.Contains(l.ChildId)).Select(l => l.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<BiologicalParentChild>> GetChildLinksForParentAsync(
        Guid parentId, CancellationToken ct = default) =>
        (await AllAsync(ct)).Where(l => l.ParentId == parentId).Select(l => l.ToDomain()).ToList();

    public async Task<BiologicalParentChild?> GetAsync(Guid parentId, Guid childId, CancellationToken ct = default) =>
        (await AllAsync(ct)).FirstOrDefault(l => l.ParentId == parentId && l.ChildId == childId)?.ToDomain();

    public Task AddAsync(BiologicalParentChild link, CancellationToken ct = default) =>
        store.PutAsync(IndexedDbStore.BiologicalLinks, BiologicalLinkRecord.From(link), ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        store.DeleteAsync(IndexedDbStore.BiologicalLinks, id, ct);
}

public sealed class BrowserAdoptiveRelationshipRepository(IndexedDbStore store)
    : IAdoptiveRelationshipRepository
{
    private Task<IReadOnlyList<AdoptiveLinkRecord>> AllAsync(CancellationToken ct) =>
        store.GetAllAsync<AdoptiveLinkRecord>(IndexedDbStore.AdoptiveLinks, ct);

    public async Task<IReadOnlyList<AdoptiveParentChild>> GetAllAsync(CancellationToken ct = default) =>
        (await AllAsync(ct)).Select(l => l.ToDomain()).ToList();

    public async Task<IReadOnlyList<AdoptiveParentChild>> GetParentLinksForChildAsync(
        Guid childId, CancellationToken ct = default) =>
        (await AllAsync(ct)).Where(l => l.ChildId == childId).Select(l => l.ToDomain()).ToList();

    public async Task<IReadOnlyList<AdoptiveParentChild>> GetParentLinksForChildrenAsync(
        IReadOnlyCollection<Guid> childIds, CancellationToken ct = default)
    {
        if (childIds.Count == 0)
        {
            return [];
        }

        var wanted = childIds.ToHashSet();
        return (await AllAsync(ct)).Where(l => wanted.Contains(l.ChildId)).Select(l => l.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<AdoptiveParentChild>> GetChildLinksForParentAsync(
        Guid parentId, CancellationToken ct = default) =>
        (await AllAsync(ct)).Where(l => l.ParentId == parentId).Select(l => l.ToDomain()).ToList();

    public async Task<AdoptiveParentChild?> GetAsync(Guid parentId, Guid childId, CancellationToken ct = default) =>
        (await AllAsync(ct)).FirstOrDefault(l => l.ParentId == parentId && l.ChildId == childId)?.ToDomain();

    public Task AddAsync(AdoptiveParentChild link, CancellationToken ct = default) =>
        store.PutAsync(IndexedDbStore.AdoptiveLinks, AdoptiveLinkRecord.From(link), ct);

    public Task UpdateAsync(AdoptiveParentChild link, CancellationToken ct = default) =>
        store.PutAsync(IndexedDbStore.AdoptiveLinks, AdoptiveLinkRecord.From(link), ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        store.DeleteAsync(IndexedDbStore.AdoptiveLinks, id, ct);
}

public sealed class BrowserMarriageRepository(IndexedDbStore store) : IMarriageRepository
{
    private Task<IReadOnlyList<MarriageRecord>> AllAsync(CancellationToken ct) =>
        store.GetAllAsync<MarriageRecord>(IndexedDbStore.Marriages, ct);

    public async Task<IReadOnlyList<Marriage>> GetAllAsync(CancellationToken ct = default) =>
        (await AllAsync(ct)).Select(m => m.ToDomain()).ToList();

    public async Task<Marriage?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        (await store.GetAsync<MarriageRecord>(IndexedDbStore.Marriages, id, ct))?.ToDomain();

    public async Task<IReadOnlyList<Marriage>> GetForPersonAsync(Guid personId, CancellationToken ct = default) =>
        (await AllAsync(ct))
        .Where(m => m.Spouse1Id == personId || m.Spouse2Id == personId)
        .Select(m => m.ToDomain())
        .ToList();

    public Task AddAsync(Marriage marriage, CancellationToken ct = default) =>
        store.PutAsync(IndexedDbStore.Marriages, MarriageRecord.From(marriage), ct);

    public Task UpdateAsync(Marriage marriage, CancellationToken ct = default) =>
        store.PutAsync(IndexedDbStore.Marriages, MarriageRecord.From(marriage), ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        store.DeleteAsync(IndexedDbStore.Marriages, id, ct);
}
