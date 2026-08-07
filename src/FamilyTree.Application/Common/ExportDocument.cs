using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Application.Common;

/// <summary>
/// The on-disk backup format. This is a file contract, not a storage detail.
/// </summary>
/// <remarks>
/// Deliberately separate from <c>FamilyTree.Storage.Browser</c>'s record types
/// even though the shapes currently agree. A file written today may be the only
/// surviving copy of somebody's research in twenty years, so it cannot be
/// coupled to how the browser happens to store things this month — reshaping an
/// IndexedDB store must not silently reshape every backup ever written.
///
/// Everything is nullable so that a truncated or hand-edited file parses far
/// enough for <see cref="Services.ImportService"/> to say what is wrong with it,
/// rather than throwing on the first missing property and reporting nothing.
/// </remarks>
public sealed record ExportDocument
{
    /// <summary>
    /// Nullable so an absent stamp is distinguishable from a zero. A file with no
    /// version cannot be trusted to mean what its fields appear to say, which is
    /// exactly the case import has to refuse.
    /// </summary>
    public int? SchemaVersion { get; init; }

    public DateTimeOffset? ExportedAt { get; init; }

    /// <summary>Names the writer, so a stranger opening the file knows what it is.</summary>
    public string? Application { get; init; }

    public IReadOnlyList<ExportedPerson>? People { get; init; }

    public IReadOnlyList<ExportedBiologicalLink>? BiologicalLinks { get; init; }

    public IReadOnlyList<ExportedAdoptiveLink>? AdoptiveLinks { get; init; }

    public IReadOnlyList<ExportedMarriage>? Marriages { get; init; }

    public const string ApplicationName = "FamilyTree";

    /// <summary>
    /// Indented and camel-cased, with enums as names.
    /// </summary>
    /// <remarks>
    /// Indentation costs bytes on a file nobody transmits, and buys a backup a
    /// person can read, diff and repair by hand. Enum names rather than ordinals
    /// for the same reason and a stronger one: inserting a member into
    /// <see cref="Gender"/> would renumber every later one, silently changing the
    /// meaning of every file already written.
    /// </remarks>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>A date that may be year-only, year+month or full, with a circa flag.</summary>
public sealed record ExportedDate(int Year, int? Month, int? Day, bool IsApproximate)
{
    public static ExportedDate? From(PartialDate? date) => date is null
        ? null
        : new ExportedDate(date.Year, date.Month, date.Day, date.IsApproximate);

    /// <summary>
    /// Rebuilds the value object, or reports why the file's date is impossible.
    /// </summary>
    /// <remarks>
    /// Returns rather than throws because the caller is import, and one bad date
    /// in a thousand-person file must cost that one record — not the import.
    /// </remarks>
    public bool TryToDomain(out PartialDate? date, out string? error)
    {
        date = null;
        error = null;

        try
        {
            date = (Month, Day) switch
            {
                (null, not null) => throw new ArgumentException("a day without a month"),
                (null, _) => PartialDate.FromYear(Year, IsApproximate),
                (int month, null) => PartialDate.FromYearMonth(Year, month, IsApproximate),
                (int month, int day) => PartialDate.FromYearMonthDay(Year, month, day, IsApproximate),
            };
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            error = $"{Year}-{Month?.ToString() ?? "?"}-{Day?.ToString() ?? "?"} is not a date ({ex.Message})";
            return false;
        }
    }
}

/// <summary>
/// A person as exported. No <c>isPhantom</c> field: phantoms are unnamed
/// placeholders for unidentified ancestors and are excluded from exports, so
/// every person in a file is a real one.
/// </summary>
public sealed record ExportedPerson
{
    public Guid Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? BirthSurname { get; init; }
    public ExportedDate? BirthDate { get; init; }
    public string? BirthPlace { get; init; }
    public ExportedDate? DeathDate { get; init; }
    public string? DeathPlace { get; init; }
    public Gender? Gender { get; init; }
    public string? PhotoPath { get; init; }
    public string? Notes { get; init; }
}

public sealed record ExportedBiologicalLink
{
    public Guid Id { get; init; }
    public Guid ParentId { get; init; }
    public Guid ChildId { get; init; }
    public RelationshipCertainty? Certainty { get; init; }
}

public sealed record ExportedAdoptiveLink
{
    public Guid Id { get; init; }
    public Guid ParentId { get; init; }
    public Guid ChildId { get; init; }
    public ExportedDate? AdoptionDate { get; init; }
    public RelationshipCertainty? Certainty { get; init; }
}

public sealed record ExportedMarriage
{
    public Guid Id { get; init; }
    public Guid Spouse1Id { get; init; }
    public Guid Spouse2Id { get; init; }
    public ExportedDate? StartDate { get; init; }
    public string? StartPlace { get; init; }
    public ExportedDate? EndDate { get; init; }
    public MarriageEndReason? EndReason { get; init; }
    public RelationshipCertainty? Certainty { get; init; }
}
