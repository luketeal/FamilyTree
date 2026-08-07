using System.Globalization;
using System.Text.Json;
using FamilyTree.Application.Common;
using FamilyTree.Domain.Repositories;

namespace FamilyTree.Application.Services;

/// <summary>What an export contained, and what it deliberately left out.</summary>
public sealed record ExportSummary(
    int People,
    int Relationships,
    int PhantomsExcluded,
    int LinksToPhantomsExcluded)
{
    /// <summary>"10 people and 13 relationships", singular where it should be.</summary>
    public string Describe() =>
        $"{Count(People, "person", "people")} and {Count(Relationships, "relationship", "relationships")}";

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
}

/// <summary>A finished export, ready to be handed to the browser as a file.</summary>
public sealed record ExportPayload(
    string FileName,
    string Json,
    ExportSummary Summary,
    DateTimeOffset ExportedAt);

/// <summary>
/// Serialises the whole tree to the JSON backup format (US-048).
/// </summary>
/// <remarks>
/// The browser is the only copy of this data (ADR-004), so this is the backup
/// mechanism rather than a feature: everything else in the app can be rebuilt,
/// and the tree cannot.
/// </remarks>
public sealed class ExportService(
    IPersonRepository people,
    IBiologicalRelationshipRepository biological,
    IAdoptiveRelationshipRepository adoptive,
    IMarriageRepository marriages,
    TimeProvider clock)
{
    public async Task<Result<ExportPayload>> ExportToJsonAsync(CancellationToken ct = default)
    {
        // Four whole-set reads, one per store, rather than a walk that reads a
        // person and then their links. Free against IndexedDB either way; the
        // difference only shows up once these interfaces are backed by HTTP,
        // which is the seam this discipline exists to protect.
        var allPeople = await people.GetAllAsync(ct);
        var bio = await biological.GetAllAsync(ct);
        var adopt = await adoptive.GetAllAsync(ct);
        var married = await marriages.GetAllAsync(ct);

        // Phantoms are unnamed placeholders for ancestors nobody has identified,
        // and are excluded from exports by the same rule that keeps them out of
        // lists and search. Their links have to go with them: a link to a person
        // the file does not contain is a dangling reference, and import would
        // reject it on arrival. The counts are reported so this is visible to
        // the user rather than a silent subtraction.
        var phantomIds = allPeople.Where(p => p.IsPhantom).Select(p => p.Id).ToHashSet();
        var realPeople = allPeople.Where(p => !p.IsPhantom).ToList();

        var exportableBio = bio
            .Where(l => !phantomIds.Contains(l.ParentId) && !phantomIds.Contains(l.ChildId))
            .ToList();
        var exportableAdopt = adopt
            .Where(l => !phantomIds.Contains(l.ParentId) && !phantomIds.Contains(l.ChildId))
            .ToList();
        var exportableMarriages = married
            .Where(m => !phantomIds.Contains(m.Spouse1Id) && !phantomIds.Contains(m.Spouse2Id))
            .ToList();

        var relationships = exportableBio.Count + exportableAdopt.Count + exportableMarriages.Count;

        // An empty file is worse than no file: the natural thing to do with a
        // backup is save it over the last one, and a zero-record export would
        // then destroy the copy it replaced.
        if (realPeople.Count == 0 && relationships == 0)
        {
            return Result<ExportPayload>.Failure(
                "There is nothing to export yet — this tree has no people in it.");
        }

        var exportedAt = clock.GetUtcNow();

        var document = new ExportDocument
        {
            SchemaVersion = TreeSchema.Version,
            ExportedAt = exportedAt,
            Application = ExportDocument.ApplicationName,

            // Sorted so two exports of the same tree produce the same bytes.
            // A backup that reorders itself on every save cannot be diffed, and
            // diffing is how somebody checks a backup is what they think it is.
            People = realPeople
                .OrderBy(p => p.LastName, StringComparer.Ordinal)
                .ThenBy(p => p.FirstName, StringComparer.Ordinal)
                .ThenBy(p => p.Id)
                .Select(p => new ExportedPerson
                {
                    Id = p.Id,
                    FirstName = p.FirstName,
                    LastName = p.LastName,
                    BirthSurname = p.BirthSurname,
                    BirthDate = ExportedDate.From(p.BirthDate),
                    BirthPlace = p.BirthPlace,
                    DeathDate = ExportedDate.From(p.DeathDate),
                    DeathPlace = p.DeathPlace,
                    Gender = p.Gender,
                    PhotoPath = p.PhotoPath,
                    Notes = p.Notes,
                })
                .ToList(),

            BiologicalLinks = exportableBio
                .OrderBy(l => l.Id)
                .Select(l => new ExportedBiologicalLink
                {
                    Id = l.Id,
                    ParentId = l.ParentId,
                    ChildId = l.ChildId,
                    Certainty = l.Certainty,
                })
                .ToList(),

            AdoptiveLinks = exportableAdopt
                .OrderBy(l => l.Id)
                .Select(l => new ExportedAdoptiveLink
                {
                    Id = l.Id,
                    ParentId = l.ParentId,
                    ChildId = l.ChildId,
                    AdoptionDate = ExportedDate.From(l.AdoptionDate),
                    Certainty = l.Certainty,
                })
                .ToList(),

            Marriages = exportableMarriages
                .OrderBy(m => m.Id)
                .Select(m => new ExportedMarriage
                {
                    Id = m.Id,
                    Spouse1Id = m.Spouse1Id,
                    Spouse2Id = m.Spouse2Id,
                    StartDate = ExportedDate.From(m.StartDate),
                    StartPlace = m.StartPlace,
                    EndDate = ExportedDate.From(m.EndDate),
                    EndReason = m.EndReason,
                    Certainty = m.Certainty,
                })
                .ToList(),
        };

        var summary = new ExportSummary(
            realPeople.Count,
            relationships,
            phantomIds.Count,
            (bio.Count - exportableBio.Count)
                + (adopt.Count - exportableAdopt.Count)
                + (married.Count - exportableMarriages.Count));

        return Result<ExportPayload>.Success(new ExportPayload(
            FileNameFor(exportedAt),
            JsonSerializer.Serialize(document, ExportDocument.JsonOptions),
            summary,
            exportedAt));
    }

    /// <summary>
    /// "familytree-2026-08-07.json". Date-first and invariant, so a folder of
    /// backups sorts chronologically by name in every locale.
    /// </summary>
    public static string FileNameFor(DateTimeOffset moment) =>
        $"familytree-{moment.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.json";
}
