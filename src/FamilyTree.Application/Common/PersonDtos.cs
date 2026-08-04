using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Application.Common;

/// <summary>
/// A person as lists and search results need them. Flat by design: the UI never
/// touches domain entities, so navigation properties cannot be walked from a
/// component and turned into per-row reads.
/// </summary>
public sealed record PersonSummaryDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? BirthSurname,
    int? BirthYear,
    int? DeathYear,
    bool IsDeceased)
{
    /// <summary>"Jane Smith (née Brown)" when a birth surname is recorded.</summary>
    public string DisplayName => BirthSurname is null
        ? $"{FirstName} {LastName}"
        : $"{FirstName} {LastName} (née {BirthSurname})";

    /// <summary>"1920–1998", "1920–", or empty when no dates are known.</summary>
    public string LifeSpan => (BirthYear, DeathYear) switch
    {
        (null, null) => string.Empty,
        (int birth, null) => $"{birth}–",
        (null, int death) => $"–{death}",
        (int birth, int death) => $"{birth}–{death}",
    };

    public static PersonSummaryDto From(Person person) => new(
        person.Id,
        person.FirstName,
        person.LastName,
        person.BirthSurname,
        person.BirthDate?.Year,
        person.DeathDate?.Year,
        person.DeathDate is not null);
}

/// <summary>A person as the profile page needs them, dates intact.</summary>
public sealed record PersonDetailDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? BirthSurname,
    PartialDate? BirthDate,
    string? BirthPlace,
    PartialDate? DeathDate,
    string? DeathPlace,
    Gender Gender,
    string? PhotoPath,
    string? Notes,
    bool IsPhantom)
{
    public string DisplayName => BirthSurname is null
        ? $"{FirstName} {LastName}"
        : $"{FirstName} {LastName} (née {BirthSurname})";

    public bool IsDeceased => DeathDate is not null;

    /// <summary>
    /// Age in whole years, null when it cannot be established. Approximate on
    /// either date makes the result approximate, which the UI marks with "~"
    /// rather than implying a precision the record does not have.
    /// </summary>
    public (int Years, bool IsApproximate)? Age
    {
        get
        {
            if (BirthDate is null)
            {
                return null;
            }

            var endYear = DeathDate?.Year ?? DateTime.UtcNow.Year;
            var years = endYear - BirthDate.Year;
            if (years < 0)
            {
                return null;
            }

            var approximate = BirthDate.IsApproximate
                || DeathDate?.IsApproximate == true
                || BirthDate.Month is null
                || (DeathDate is not null && DeathDate.Month is null);

            return (years, approximate);
        }
    }

    public static PersonDetailDto From(Person person) => new(
        person.Id,
        person.FirstName,
        person.LastName,
        person.BirthSurname,
        person.BirthDate,
        person.BirthPlace,
        person.DeathDate,
        person.DeathPlace,
        person.Gender,
        person.PhotoPath,
        person.Notes,
        person.IsPhantom);
}
