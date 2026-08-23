using FamilyTree.Domain.Entities;

namespace FamilyTree.Domain.Repositories;

public interface IPersonRepository
{
    Task<Person?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Many people at once, in one call.
    /// </summary>
    /// <remarks>
    /// A profile shows parents, children and siblings, and resolving each of
    /// them through <see cref="GetByIdAsync"/> is the N+1 the API-seam discipline
    /// forbids: free against IndexedDB, one HTTP request per relative once the
    /// seam is swapped. Reading the whole table instead would be one request but
    /// the wrong one — a 500-person download to render a page that names six.
    /// <para>
    /// Ids that name nobody are simply absent from the result. A relationship
    /// pointing at a deleted person is a real state this app can reach, and it
    /// is the caller's business to decide what to do about it.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<Person>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    Task<IReadOnlyList<Person>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Person person, CancellationToken ct = default);
    Task UpdateAsync(Person person, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}
