using System.Globalization;
using System.Text.Json;
using FamilyTree.Application.Common;
using FamilyTree.Domain.Repositories;

namespace FamilyTree.Application.Services;

/// <summary>What an export contained.</summary>
/// <remarks>
/// <see cref="People"/> counts identified people only, matching the number the
/// app displays everywhere else. <see cref="Phantoms"/> is reported alongside
/// rather than folded in: the placeholders are in the file — that is the point
/// of the section — but calling them people would overstate how much of the
/// tree is actually known.
/// </remarks>
public sealed record ExportSummary(
    int People,
    int Relationships,
    int Phantoms)
{
    /// <summary>"10 people and 14 relationships", singular where it should be.</summary>
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

        // Phantoms go into their own section rather than into the people list.
        // They stay out of anything that reads `people`, which is the rule that
        // keeps unidentified ancestors out of lists and searches, and the links
        // that name them now have somewhere to point — so a tree comes back from
        // its own backup the same size it went in. Excluding them outright cost
        // one relationship per unidentified ancestor on every round trip, which
        // is silent data loss rather than tidiness (ADR-007).
        var phantoms = allPeople.Where(p => p.IsPhantom).ToList();
        var realPeople = allPeople.Where(p => !p.IsPhantom).ToList();

        var relationships = bio.Count + adopt.Count + married.Count;

        // An empty file is worse than no file: the natural thing to do with a
        // backup is save it over the last one, and a zero-record export would
        // then destroy the copy it replaced. A tree of nothing but placeholders
        // is still nothing anybody can restore anything from, so phantoms do not
        // count towards having something to save.
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

            // Phantom ids are Guids like any other, so they sort into the same
            // stable order and need no special handling below.
            Phantoms = phantoms
                .OrderBy(p => p.Id)
                .Select(p => new ExportedPhantom { Id = p.Id })
                .ToList(),

            BiologicalLinks = bio
                .OrderBy(l => l.Id)
                .Select(l => new ExportedBiologicalLink
                {
                    Id = l.Id,
                    ParentId = l.ParentId,
                    ChildId = l.ChildId,
                    Certainty = l.Certainty,
                })
                .ToList(),

            AdoptiveLinks = adopt
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

            Marriages = married
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

        var summary = new ExportSummary(realPeople.Count, relationships, phantoms.Count);

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
