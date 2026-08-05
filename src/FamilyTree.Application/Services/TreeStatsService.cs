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
public sealed class TreeStatsService(
    IPersonRepository people,
    IBiologicalRelationshipRepository biological,
    IAdoptiveRelationshipRepository adoptive,
    IMarriageRepository marriages)
{
    public async Task<Result<TreeStats>> GetAsync(CancellationToken ct = default)
    {
        var allPeople = await people.GetAllAsync(ct);
        var bio = await biological.GetAllAsync(ct);
        var adopt = await adoptive.GetAllAsync(ct);
        var married = await marriages.GetAllAsync(ct);

        // Phantoms are placeholders for ancestors nobody has identified, so
        // counting them would overstate how much of the tree is actually known.
        var realPeople = allPeople.Count(p => !p.IsPhantom);

        // Stepparent links are deliberately absent: the store and the record
        // exist, but no repository writes them yet, so there is nothing to
        // count. Add them here at the same time as the stepparent repository,
        // or this total starts silently under-reporting.

        var relationships = bio.Count + adopt.Count + married.Count;

        return Result<TreeStats>.Success(
            new TreeStats(realPeople, relationships)
            {
                StoredRecords = allPeople.Count + relationships,
            });
    }
}

/// <summary>
/// Lets components refresh after something changes the tree. Scoped, so in
/// WebAssembly there is exactly one per application instance.
/// </summary>
public sealed class TreeDataNotifier
{
    public event Func<Task>? Changed;

    public async Task NotifyChangedAsync()
    {
        if (Changed is not null)
        {
            await Changed.Invoke();
        }
    }
}
