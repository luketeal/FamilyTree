using FamilyTree.Application.Common;
using FamilyTree.Domain.Repositories;

namespace FamilyTree.Application.Services;

public sealed record TreeStats(int People, int Relationships)
{
    /// <summary>
    /// Everything currently stored, phantoms included. <see cref="People"/>
    /// counts only identified people, so a tree holding nothing but unidentified
    /// ancestors reports zero while still containing records a wipe would
    /// destroy. Anything deciding whether there is something to lose must ask
    /// this, not the displayed counts.
    /// </summary>
    public int StoredRecords { get; init; }

    public bool IsEmpty => StoredRecords == 0;

    public static TreeStats Empty { get; } = new(0, 0);

    /// <summary>"12 people · 11 relationships", singular where it should be.</summary>
    public override string ToString() =>
        $"{People} {(People == 1 ? "person" : "people")} · " +
        $"{Relationships} {(Relationships == 1 ? "relationship" : "relationships")}";
}

/// <summary>
/// Counts for the top bar. Reads each set once rather than per person, since
/// this runs on every page load and is the first thing to notice if the seam
/// starts issuing per-row requests.
/// </summary>
/// <remarks>
/// The result is cached between changes. Every subscriber to
/// <see cref="TreeDataNotifier"/> asks for these counts when the tree changes,
/// and each ask is a complete read of all four stores — free against IndexedDB,
/// four HTTP round trips per listener once the seam is swapped. A third
/// component now listens (the backup reminder, alongside the top bar and
/// Settings), which would make one save cost twelve requests: exactly the
/// pattern this class's own docstring existed to watch for.
///
/// The cache is dropped by the notifier <em>before</em> it dispatches, rather
/// than by a subscriber of its own. Subscribers are awaited together, so a
/// clearing handler racing the reading handlers would sometimes hand a listener
/// the counts from before the change.
/// </remarks>
public sealed class TreeStatsService
{
    private readonly IPersonRepository _people;
    private readonly IBiologicalRelationshipRepository _biological;
    private readonly IAdoptiveRelationshipRepository _adoptive;
    private readonly IMarriageRepository _marriages;

    private TreeStats? _cached;

    public TreeStatsService(
        IPersonRepository people,
        IBiologicalRelationshipRepository biological,
        IAdoptiveRelationshipRepository adoptive,
        IMarriageRepository marriages,
        TreeDataNotifier notifier)
    {
        _people = people;
        _biological = biological;
        _adoptive = adoptive;
        _marriages = marriages;

        notifier.BeforeNotifying(() =>
        {
            _cached = null;
            return Task.CompletedTask;
        });
    }

    /// <summary>The current counts, served from cache when nothing has changed.</summary>
    public async Task<Result<TreeStats>> GetAsync(CancellationToken ct = default) =>
        _cached is { } cached
            ? Result<TreeStats>.Success(cached)
            : await ReadFreshAsync(ct);

    /// <summary>
    /// Reads the stores regardless of what is cached.
    /// </summary>
    /// <remarks>
    /// For callers that must not act on a stale answer. Another tab on the same
    /// origin writes to the same database without raising this app's notifier,
    /// so a cached zero could let a destructive action skip its confirmation and
    /// wipe work this instance never saw.
    /// </remarks>
    public async Task<Result<TreeStats>> ReadFreshAsync(CancellationToken ct = default)
    {
        var allPeople = await _people.GetAllAsync(ct);
        var bio = await _biological.GetAllAsync(ct);
        var adopt = await _adoptive.GetAllAsync(ct);
        var married = await _marriages.GetAllAsync(ct);

        // Phantoms are placeholders for ancestors nobody has identified, so
        // counting them would overstate how much of the tree is actually known.
        var realPeople = allPeople.Count(p => !p.IsPhantom);

        // Stepparent links are deliberately absent: the store and the record
        // exist, but no repository writes them yet, so there is nothing to
        // count. Add them here at the same time as the stepparent repository,
        // or this total starts silently under-reporting.

        var relationships = bio.Count + adopt.Count + married.Count;

        _cached = new TreeStats(realPeople, relationships)
        {
            StoredRecords = allPeople.Count + relationships,
        };

        return Result<TreeStats>.Success(_cached);
    }
}

/// <summary>
/// Lets components refresh after something changes the tree. Scoped, so in
/// WebAssembly there is exactly one per application instance.
/// </summary>
public sealed class TreeDataNotifier
{
    private readonly List<Func<Task>> _before = [];

    public event Func<Task>? Changed;

    /// <summary>
    /// Registers work that must finish before any subscriber is told.
    /// </summary>
    /// <remarks>
    /// Two things need this: the tree-stats cache, which must be dropped before
    /// anybody reads it, and the backup journal, whose "last changed" timestamp
    /// the reminder reads on the very same notification. Both would be racing the
    /// handlers that read them if they ran as ordinary <see cref="Changed"/>
    /// subscribers, because those all run together — so half the subscribers
    /// would redraw from the state that existed before the change.
    ///
    /// Awaited in registration order rather than together, since these are short
    /// and ordering between them is easier to reason about than concurrency.
    /// </remarks>
    public void BeforeNotifying(Func<Task> work) => _before.Add(work);

    /// <summary>
    /// Awaits every subscriber, not just the last one.
    /// </summary>
    /// <remarks>
    /// Invoking a multicast Func&lt;Task&gt; directly returns only the final
    /// delegate's task, so every earlier subscriber becomes fire-and-forget and
    /// an exception inside one is never observed. That was harmless while the
    /// top bar was the only listener and stopped being so the moment a second
    /// component subscribed.
    /// </remarks>
    public async Task NotifyChangedAsync()
    {
        foreach (var work in _before)
        {
            await work();
        }

        if (Changed is null)
        {
            return;
        }

        await Task.WhenAll(Changed.GetInvocationList()
            .Cast<Func<Task>>()
            .Select(handler => handler()));
    }
}
