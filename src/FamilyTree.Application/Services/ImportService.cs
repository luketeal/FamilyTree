using System.Text.Json;
using FamilyTree.Application.Common;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.Repositories;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Application.Services;

/// <summary>What to do about a record the file and the tree both contain.</summary>
public enum ImportConflictResolution
{
    /// <summary>Keep what is already here; add only records the tree does not have.</summary>
    Skip,

    /// <summary>
    /// Replace the whole tree with the file. The restore-a-backup case, and the
    /// only option that deletes records the file does not mention.
    /// </summary>
    Overwrite,

    /// <summary>Add new records, and let the file's version win where ids collide.</summary>
    Merge,
}

/// <summary>What a file contains, read without touching the tree.</summary>
public sealed record ImportPreviewDto(
    int SchemaVersion,
    DateTimeOffset? ExportedAt,
    int People,
    int Relationships,
    int RecordsRejected,
    IReadOnlyList<string> Warnings);

/// <summary>What an import actually did.</summary>
public sealed record ImportResultDto(
    ImportConflictResolution Resolution,
    int PeopleAdded,
    int PeopleUpdated,
    int PeopleSkipped,
    int PeopleRemoved,
    int RelationshipsAdded,
    int RelationshipsUpdated,
    int RelationshipsSkipped,
    int RelationshipsRemoved,
    int RecordsRejected,
    IReadOnlyList<string> Warnings)
{
    public int TotalRemoved => PeopleRemoved + RelationshipsRemoved;

    /// <summary>One line a user can read without decoding a table.</summary>
    public string Describe()
    {
        var parts = new List<string>
        {
            $"{PeopleAdded + RelationshipsAdded} added",
        };

        if (PeopleUpdated + RelationshipsUpdated > 0)
        {
            parts.Add($"{PeopleUpdated + RelationshipsUpdated} updated");
        }

        if (PeopleSkipped + RelationshipsSkipped > 0)
        {
            parts.Add($"{PeopleSkipped + RelationshipsSkipped} left alone");
        }

        if (TotalRemoved > 0)
        {
            parts.Add($"{TotalRemoved} removed");
        }

        if (RecordsRejected > 0)
        {
            parts.Add($"{RecordsRejected} rejected");
        }

        return string.Join(", ", parts) + ".";
    }
}

/// <summary>
/// Reads a JSON backup back into the tree (US-049).
/// </summary>
/// <remarks>
/// This is the first code in the app to consume a payload it did not write, and
/// so the first place the schema stamp is checked rather than merely written.
/// Everything here assumes the file is wrong until it proves otherwise: it may
/// have been hand-edited, truncated by a failed download, or written by a later
/// version of the app that means something different by the same field names.
///
/// Individual bad records are dropped with an explanation rather than aborting
/// the import. One impossible date in a thousand-person file should cost that
/// one record, not the restore.
/// </remarks>
public sealed class ImportService(
    IPersonRepository people,
    IBiologicalRelationshipRepository biological,
    IAdoptiveRelationshipRepository adoptive,
    IMarriageRepository marriages,
    ITreeDataAdministration administration)
{
    /// <summary>
    /// Reads a file and reports what is in it, without changing anything.
    /// </summary>
    /// <remarks>
    /// Deliberately does not consult the tree. Import is destructive under
    /// Overwrite, so the user needs to know what the file holds before choosing,
    /// and a preview that touched storage would be a worse thing to get wrong.
    /// </remarks>
    public Result<ImportPreviewDto> Preview(string json)
    {
        var parsed = Parse(json);
        if (!parsed.IsSuccess)
        {
            return Result<ImportPreviewDto>.Failure(parsed.Error!);
        }

        var file = parsed.Value!;
        return Result<ImportPreviewDto>.Success(new ImportPreviewDto(
            file.SchemaVersion,
            file.ExportedAt,
            file.People.Count,
            file.BiologicalLinks.Count + file.AdoptiveLinks.Count + file.Marriages.Count,
            file.Rejected,
            file.Warnings.Messages));
    }

    public async Task<Result<ImportResultDto>> ImportAsync(
        string json,
        ImportConflictResolution resolution,
        CancellationToken ct = default)
    {
        var parsed = Parse(json);
        if (!parsed.IsSuccess)
        {
            return Result<ImportResultDto>.Failure(parsed.Error!);
        }

        var file = parsed.Value!;
        // The same log the parse filled, so its suppression count keeps counting
        // rather than restarting partway down the report.
        var warnings = file.Warnings;
        var rejected = file.Rejected;

        // Whole-set reads, one per store. Nothing here walks the tree per record.
        var existingPeople = await people.GetAllAsync(ct);
        var existingBio = await biological.GetAllAsync(ct);
        var existingAdoptive = await adoptive.GetAllAsync(ct);
        var existingMarriages = await marriages.GetAllAsync(ct);

        var personMerge = Combine(existingPeople, file.People, p => p.Id, resolution);
        var bioMerge = Combine(existingBio, file.BiologicalLinks, l => l.Id, resolution);
        var adoptiveMerge = Combine(existingAdoptive, file.AdoptiveLinks, l => l.Id, resolution);
        var marriageMerge = Combine(existingMarriages, file.Marriages, m => m.Id, resolution);

        // A record that arrives with a fresh id but the same meaning as one
        // already present is still a duplicate. The domain forbids naming the
        // same person as a parent twice, and an import is the one path into the
        // store that never went through those guards.
        var bioLinks = Deduplicate(
            bioMerge.Final,
            l => (l.ParentId, l.ChildId),
            "the same biological parent and child",
            warnings,
            out var bioDuplicates);
        var adoptiveLinks = Deduplicate(
            adoptiveMerge.Final,
            l => (l.ParentId, l.ChildId),
            "the same adoptive parent and child",
            warnings,
            out var adoptiveDuplicates);
        var marriageList = Deduplicate(
            marriageMerge.Final,
            MarriageKey,
            "the same two spouses and start date",
            warnings,
            out var marriageDuplicates);

        // A link to somebody who is not in the tree is a link to nobody. Under
        // Skip and Merge the referent may come from what is already stored, so
        // this is checked against the final set rather than against the file.
        var personIds = personMerge.Final.Select(p => p.Id).ToHashSet();
        bioLinks = Connected(bioLinks, l => (l.ParentId, l.ChildId), "biological link", personIds, warnings, out var bioOrphans);
        adoptiveLinks = Connected(adoptiveLinks, l => (l.ParentId, l.ChildId), "adoptive link", personIds, warnings, out var adoptiveOrphans);
        marriageList = Connected(marriageList, m => (m.Spouse1Id, m.Spouse2Id), "marriage", personIds, warnings, out var marriageOrphans);

        var duplicates = bioDuplicates + adoptiveDuplicates + marriageDuplicates;
        rejected += bioOrphans + adoptiveOrphans + marriageOrphans;

        // One call, one transaction. Four repository writes could half-apply and
        // leave links pointing at people who were never stored — which is the
        // failure mode this whole PR exists to prevent.
        await administration.ReplaceAllAsync(
            new TreeSnapshot(personMerge.Final, bioLinks, adoptiveLinks, marriageList),
            ct);

        return Result<ImportResultDto>.Success(new ImportResultDto(
            resolution,
            personMerge.Added,
            personMerge.Updated,
            personMerge.Skipped,
            personMerge.Removed,
            bioMerge.Added + adoptiveMerge.Added + marriageMerge.Added,
            bioMerge.Updated + adoptiveMerge.Updated + marriageMerge.Updated,
            bioMerge.Skipped + adoptiveMerge.Skipped + marriageMerge.Skipped + duplicates,
            bioMerge.Removed + adoptiveMerge.Removed + marriageMerge.Removed,
            rejected,
            warnings.Messages));
    }

    private static (Guid, Guid, int, int?, int?) MarriageKey(Marriage m) => (
        // Unordered: which spouse is recorded first is an accident of entry, so
        // A-married-B and B-married-A are the same marriage.
        m.Spouse1Id.CompareTo(m.Spouse2Id) <= 0 ? m.Spouse1Id : m.Spouse2Id,
        m.Spouse1Id.CompareTo(m.Spouse2Id) <= 0 ? m.Spouse2Id : m.Spouse1Id,
        m.StartDate.Year,
        m.StartDate.Month,
        m.StartDate.Day);

    private sealed record MergeOutcome<T>(
        IReadOnlyList<T> Final, int Added, int Updated, int Skipped, int Removed);

    /// <summary>
    /// Applies the chosen resolution to one collection.
    /// </summary>
    /// <remarks>
    /// Order matters beyond bookkeeping: whichever side is listed first survives
    /// the natural-key deduplication that runs afterwards, so the winner of an
    /// id collision has to also be the winner of a meaning collision.
    /// </remarks>
    private static MergeOutcome<T> Combine<T>(
        IReadOnlyList<T> existing,
        IReadOnlyList<T> imported,
        Func<T, Guid> id,
        ImportConflictResolution resolution)
    {
        var existingIds = existing.Select(id).ToHashSet();
        var importedIds = imported.Select(id).ToHashSet();

        var added = imported.Count(r => !existingIds.Contains(id(r)));
        var colliding = imported.Count - added;

        return resolution switch
        {
            ImportConflictResolution.Skip => new MergeOutcome<T>(
                [.. existing, .. imported.Where(r => !existingIds.Contains(id(r)))],
                added, 0, colliding, 0),

            ImportConflictResolution.Merge => new MergeOutcome<T>(
                [.. imported, .. existing.Where(r => !importedIds.Contains(id(r)))],
                added, colliding, 0, 0),

            _ => new MergeOutcome<T>(
                [.. imported],
                added, colliding, 0, existing.Count(r => !importedIds.Contains(id(r)))),
        };
    }

    private static IReadOnlyList<T> Deduplicate<T, TKey>(
        IReadOnlyList<T> records,
        Func<T, TKey> key,
        string describe,
        WarningLog warnings,
        out int dropped)
        where TKey : notnull
    {
        var seen = new HashSet<TKey>();
        var kept = new List<T>(records.Count);
        dropped = 0;

        foreach (var record in records)
        {
            if (seen.Add(key(record)))
            {
                kept.Add(record);
                continue;
            }

            dropped++;
            warnings.Add($"Ignored a duplicate record: another one already has {describe}.");
        }

        return kept;
    }

    private static IReadOnlyList<T> Connected<T>(
        IReadOnlyList<T> records,
        Func<T, (Guid, Guid)> endpoints,
        string kind,
        IReadOnlySet<Guid> personIds,
        WarningLog warnings,
        out int dropped)
    {
        var kept = new List<T>(records.Count);
        dropped = 0;

        foreach (var record in records)
        {
            var (left, right) = endpoints(record);
            if (personIds.Contains(left) && personIds.Contains(right))
            {
                kept.Add(record);
                continue;
            }

            dropped++;
            var missing = personIds.Contains(left) ? right : left;
            warnings.Add($"Dropped a {kind}: it refers to person {missing}, who is not in the tree.");
        }

        return kept;
    }

    private sealed record ParsedFile(
        int SchemaVersion,
        DateTimeOffset? ExportedAt,
        IReadOnlyList<Person> People,
        IReadOnlyList<BiologicalParentChild> BiologicalLinks,
        IReadOnlyList<AdoptiveParentChild> AdoptiveLinks,
        IReadOnlyList<Marriage> Marriages,
        WarningLog Warnings,
        int Rejected);

    private static Result<ParsedFile> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result<ParsedFile>.Failure("That file is empty.");
        }

        ExportDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ExportDocument>(json, ExportDocument.JsonOptions);
        }
        catch (JsonException ex)
        {
            return Result<ParsedFile>.Failure($"That file is not readable as FamilyTree JSON: {ex.Message}");
        }

        if (document is null)
        {
            return Result<ParsedFile>.Failure("That file is not readable as FamilyTree JSON.");
        }

        // The stamp is checked before anything else is believed. Field names are
        // stable across versions and their meanings are not, so an unstamped or
        // future-stamped file is one whose contents cannot be interpreted — not
        // one to read optimistically and hope.
        if (document.SchemaVersion is not { } version)
        {
            return Result<ParsedFile>.Failure(
                "That file has no schemaVersion, so it was not written by FamilyTree.");
        }

        if (version > TreeSchema.Version)
        {
            return Result<ParsedFile>.Failure(
                $"That file was exported by a newer version of FamilyTree (format {version}; "
                + $"this version understands format {TreeSchema.Version}). Update the app, then import it.");
        }

        if (version < 1)
        {
            return Result<ParsedFile>.Failure(
                $"That file claims schema version {version}, which is not a version FamilyTree ever wrote.");
        }

        if (document.People is null
            && document.BiologicalLinks is null
            && document.AdoptiveLinks is null
            && document.Marriages is null)
        {
            return Result<ParsedFile>.Failure(
                "That file has no people or relationships in it, so there is nothing to import.");
        }

        var warnings = new WarningLog();
        var rejected = 0;

        var parsedPeople = ReadPeople(document.People, warnings, ref rejected);
        var parsedBio = ReadBiologicalLinks(document.BiologicalLinks, warnings, ref rejected);
        var parsedAdoptive = ReadAdoptiveLinks(document.AdoptiveLinks, warnings, ref rejected);
        var parsedMarriages = ReadMarriages(document.Marriages, warnings, ref rejected);

        if (parsedPeople.Count == 0
            && parsedBio.Count == 0
            && parsedAdoptive.Count == 0
            && parsedMarriages.Count == 0)
        {
            return Result<ParsedFile>.Failure(rejected == 0
                ? "That file has no people or relationships in it, so there is nothing to import."
                : $"Every record in that file was unreadable ({rejected} rejected). Nothing was changed.");
        }

        return Result<ParsedFile>.Success(new ParsedFile(
            version,
            document.ExportedAt,
            parsedPeople,
            parsedBio,
            parsedAdoptive,
            parsedMarriages,
            warnings,
            rejected));
    }

    private static List<Person> ReadPeople(
        IReadOnlyList<ExportedPerson>? source, WarningLog warnings, ref int rejected)
    {
        var result = new List<Person>();
        var seen = new HashSet<Guid>();

        foreach (var row in source ?? [])
        {
            var label = Label(row.FirstName, row.LastName, row.Id);

            if (row.Id == Guid.Empty)
            {
                Reject(warnings, ref rejected, $"Skipped {label}: the record has no id.");
                continue;
            }

            if (!seen.Add(row.Id))
            {
                Reject(warnings, ref rejected, $"Skipped {label}: the file lists id {row.Id} more than once.");
                continue;
            }

            // Rehydrate deliberately bypasses the domain's own validation so that
            // stored records round-trip exactly. That is right for data this app
            // wrote and wrong for a file it did not, so the names are checked
            // here — a person with no name cannot be found, edited or deleted.
            if (string.IsNullOrWhiteSpace(row.FirstName) || string.IsNullOrWhiteSpace(row.LastName))
            {
                Reject(warnings, ref rejected, $"Skipped person {row.Id}: a first and last name are required.");
                continue;
            }

            if (!TryDate(row.BirthDate, out var birthDate, out var birthError))
            {
                Reject(warnings, ref rejected, $"Skipped {label}: birth date {birthError}.");
                continue;
            }

            if (!TryDate(row.DeathDate, out var deathDate, out var deathError))
            {
                Reject(warnings, ref rejected, $"Skipped {label}: death date {deathError}.");
                continue;
            }

            // Kept rather than rejected. These are recoverable by editing the
            // person, and dropping somebody's ancestor to enforce a field limit
            // would be a worse outcome than importing a record that needs a fix.
            if (deathDate is not null && birthDate is not null && deathDate.CompareTo(birthDate) < 0)
            {
                warnings.Add($"{label} has a death date before their birth date. Imported as-is.");
            }

            if (row.Notes is { Length: > PersonService.NotesLimit })
            {
                warnings.Add(
                    $"{label} has notes longer than the {PersonService.NotesLimit:N0}-character limit. "
                    + "Imported in full; trim them before saving an edit.");
            }

            result.Add(Person.Rehydrate(
                row.Id,
                row.FirstName!.Trim(),
                row.LastName!.Trim(),
                row.BirthSurname?.Trim(),
                birthDate,
                row.BirthPlace?.Trim(),
                deathDate,
                row.DeathPlace?.Trim(),
                row.Gender ?? Gender.Unknown,
                row.PhotoPath,
                row.Notes,
                isPhantom: false));
        }

        return result;
    }

    private static List<BiologicalParentChild> ReadBiologicalLinks(
        IReadOnlyList<ExportedBiologicalLink>? source, WarningLog warnings, ref int rejected)
    {
        var result = new List<BiologicalParentChild>();
        var seen = new HashSet<Guid>();

        foreach (var row in source ?? [])
        {
            if (!ValidLink(row.Id, row.ParentId, row.ChildId, "biological link", seen, warnings, ref rejected))
            {
                continue;
            }

            result.Add(BiologicalParentChild.Rehydrate(
                row.Id, row.ParentId, row.ChildId, row.Certainty ?? RelationshipCertainty.Confirmed));
        }

        return result;
    }

    private static List<AdoptiveParentChild> ReadAdoptiveLinks(
        IReadOnlyList<ExportedAdoptiveLink>? source, WarningLog warnings, ref int rejected)
    {
        var result = new List<AdoptiveParentChild>();
        var seen = new HashSet<Guid>();

        foreach (var row in source ?? [])
        {
            if (!ValidLink(row.Id, row.ParentId, row.ChildId, "adoptive link", seen, warnings, ref rejected))
            {
                continue;
            }

            if (!TryDate(row.AdoptionDate, out var adoptionDate, out var error))
            {
                Reject(warnings, ref rejected, $"Skipped adoptive link {row.Id}: adoption date {error}.");
                continue;
            }

            result.Add(AdoptiveParentChild.Rehydrate(
                row.Id, row.ParentId, row.ChildId, adoptionDate,
                row.Certainty ?? RelationshipCertainty.Confirmed));
        }

        return result;
    }

    private static List<Marriage> ReadMarriages(
        IReadOnlyList<ExportedMarriage>? source, WarningLog warnings, ref int rejected)
    {
        var result = new List<Marriage>();
        var seen = new HashSet<Guid>();

        foreach (var row in source ?? [])
        {
            if (!ValidLink(row.Id, row.Spouse1Id, row.Spouse2Id, "marriage", seen, warnings, ref rejected))
            {
                continue;
            }

            // The domain makes a start date non-optional, so a marriage without
            // one has no shape to rehydrate into.
            if (row.StartDate is null)
            {
                Reject(warnings, ref rejected, $"Skipped marriage {row.Id}: it has no start date.");
                continue;
            }

            if (!TryDate(row.StartDate, out var startDate, out var startError))
            {
                Reject(warnings, ref rejected, $"Skipped marriage {row.Id}: start date {startError}.");
                continue;
            }

            if (!TryDate(row.EndDate, out var endDate, out var endError))
            {
                Reject(warnings, ref rejected, $"Skipped marriage {row.Id}: end date {endError}.");
                continue;
            }

            if (endDate is not null && endDate.CompareTo(startDate!) < 0)
            {
                warnings.Add($"Marriage {row.Id} ends before it starts. Imported as-is.");
            }

            result.Add(Marriage.Rehydrate(
                row.Id, row.Spouse1Id, row.Spouse2Id, startDate!, row.StartPlace?.Trim(),
                endDate, row.EndReason, row.Certainty ?? RelationshipCertainty.Confirmed));
        }

        return result;
    }

    private static bool ValidLink(
        Guid id, Guid left, Guid right, string kind,
        HashSet<Guid> seen, WarningLog warnings, ref int rejected)
    {
        if (id == Guid.Empty)
        {
            Reject(warnings, ref rejected, $"Skipped a {kind}: the record has no id.");
            return false;
        }

        if (!seen.Add(id))
        {
            Reject(warnings, ref rejected, $"Skipped a {kind}: the file lists id {id} more than once.");
            return false;
        }

        if (left == Guid.Empty || right == Guid.Empty)
        {
            Reject(warnings, ref rejected, $"Skipped {kind} {id}: it does not name two people.");
            return false;
        }

        // Rehydrate skips the constructor guards, so a file can express a
        // self-relationship that the app itself will not create. It would render
        // as an edge from a node to itself and has no meaning in any of the
        // three relationship types.
        if (left == right)
        {
            Reject(warnings, ref rejected, $"Skipped {kind} {id}: it names the same person twice.");
            return false;
        }

        return true;
    }

    private static bool TryDate(ExportedDate? source, out PartialDate? date, out string? error)
    {
        if (source is null)
        {
            date = null;
            error = null;
            return true;
        }

        return source.TryToDomain(out date, out error);
    }

    private static void Reject(WarningLog warnings, ref int rejected, string message)
    {
        rejected++;
        warnings.Add(message);
    }

    private static string Label(string? firstName, string? lastName, Guid id)
    {
        var name = $"{firstName} {lastName}".Trim();
        return name.Length == 0 ? $"person {id}" : name;
    }

    /// <summary>
    /// Collects problems, capped.
    /// </summary>
    /// <remarks>
    /// A file that is wrong in one way is usually wrong in that way a thousand
    /// times, and a thousand identical lines is not a report — it is a wall the
    /// user scrolls past. The count of what was suppressed is kept, because
    /// "and 812 more" is the part that tells them how bad it is.
    /// </remarks>
    private sealed class WarningLog
    {
        private const int Limit = 20;

        private readonly List<string> _messages = [];
        private int _suppressed;

        public void Add(string message)
        {
            if (_messages.Count < Limit)
            {
                _messages.Add(message);
            }
            else
            {
                _suppressed++;
            }
        }

        public IReadOnlyList<string> Messages => _suppressed == 0
            ? _messages
            : [.. _messages, $"…and {_suppressed} more problems like these."];
    }
}
