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

    public async Task<IReadOnlyList<Person>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        (await store.GetManyAsync<PersonRecord>(IndexedDbStore.People, ids, ct))
        .Select(r => r.ToDomain())
        .ToList();

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

    /// <summary>
    /// One IndexedDB transaction, so the correction cannot leave the child with
    /// neither the old parent nor the new one.
    /// </summary>
    public Task ReplaceParentAsync(
        Guid oldLinkId, BiologicalParentChild newLink, CancellationToken ct = default) =>
        store.ReplaceAsync(
            IndexedDbStore.BiologicalLinks, oldLinkId, BiologicalLinkRecord.From(newLink), ct);
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

    /// <summary>
    /// One IndexedDB transaction, so the correction cannot leave the child with
    /// neither the old adoptive parent nor the new one.
    /// </summary>
    public Task ReplaceParentAsync(
        Guid oldLinkId, AdoptiveParentChild newLink, CancellationToken ct = default) =>
        store.ReplaceAsync(
            IndexedDbStore.AdoptiveLinks, oldLinkId, AdoptiveLinkRecord.From(newLink), ct);
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

    public async Task<IReadOnlyList<Marriage>> GetForPeopleAsync(
        IReadOnlyCollection<Guid> personIds, CancellationToken ct = default)
    {
        if (personIds.Count == 0)
        {
            return [];
        }

        var wanted = personIds.ToHashSet();
        return (await AllAsync(ct))
            .Where(m => wanted.Contains(m.Spouse1Id) || wanted.Contains(m.Spouse2Id))
            .Select(m => m.ToDomain())
            .ToList();
    }

    public Task AddAsync(Marriage marriage, CancellationToken ct = default) =>
        store.PutAsync(IndexedDbStore.Marriages, MarriageRecord.From(marriage), ct);

    public Task UpdateAsync(Marriage marriage, CancellationToken ct = default) =>
        store.PutAsync(IndexedDbStore.Marriages, MarriageRecord.From(marriage), ct);

    /// <summary>
    /// One transaction over both stores, so a removed marriage cannot leave a
    /// stepparent label behind pointing at it.
    /// </summary>
    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        store.DeleteWithDependentsAsync<MarriageRecord>(
            IndexedDbStore.Marriages, id, IndexedDbStore.StepparentLinks, MarriageIdKey, ct: ct);

    /// <summary>
    /// The old record out, the new one in, and the old record's stepparent labels
    /// with it — all in one transaction.
    /// </summary>
    public Task ReplaceSpouseAsync(
        Guid oldMarriageId, Marriage replacement, CancellationToken ct = default) =>
        store.DeleteWithDependentsAsync(
            IndexedDbStore.Marriages,
            oldMarriageId,
            IndexedDbStore.StepparentLinks,
            MarriageIdKey,
            MarriageRecord.From(replacement),
            ct);

    /// <summary>
    /// The stepparent record's field naming its marriage, as it is spelt in the
    /// database.
    /// </summary>
    /// <remarks>
    /// Records are serialised with the web JSON defaults, which camel-case
    /// property names, so the stored key is <c>marriageId</c> rather than
    /// <c>MarriageId</c>. Named here rather than inline because getting it wrong
    /// fails silently: the scan simply matches nothing and the labels survive a
    /// deletion that was supposed to take them.
    /// </remarks>
    private const string MarriageIdKey = "marriageId";
}

public sealed class BrowserStepparentRelationshipRepository(IndexedDbStore store)
    : IStepparentRelationshipRepository
{
    private Task<IReadOnlyList<StepparentLinkRecord>> AllAsync(CancellationToken ct) =>
        store.GetAllAsync<StepparentLinkRecord>(IndexedDbStore.StepparentLinks, ct);

    public async Task<IReadOnlyList<StepparentRelationship>> GetAllAsync(CancellationToken ct = default) =>
        (await AllAsync(ct)).Select(l => l.ToDomain()).ToList();

    public async Task<IReadOnlyList<StepparentRelationship>> GetForStepchildAsync(
        Guid stepchildId, CancellationToken ct = default) =>
        (await AllAsync(ct)).Where(l => l.StepchildId == stepchildId).Select(l => l.ToDomain()).ToList();

    public async Task<IReadOnlyList<StepparentRelationship>> GetForStepparentAsync(
        Guid stepparentId, CancellationToken ct = default) =>
        (await AllAsync(ct)).Where(l => l.StepparentId == stepparentId).Select(l => l.ToDomain()).ToList();

    public async Task<IReadOnlyList<StepparentRelationship>> GetForStepchildrenAsync(
        IReadOnlyCollection<Guid> stepchildIds, CancellationToken ct = default)
    {
        if (stepchildIds.Count == 0)
        {
            return [];
        }

        var wanted = stepchildIds.ToHashSet();
        return (await AllAsync(ct))
            .Where(l => wanted.Contains(l.StepchildId))
            .Select(l => l.ToDomain())
            .ToList();
    }

    public Task AddAsync(StepparentRelationship link, CancellationToken ct = default) =>
        store.PutAsync(IndexedDbStore.StepparentLinks, StepparentLinkRecord.From(link), ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        store.DeleteAsync(IndexedDbStore.StepparentLinks, id, ct);
}
