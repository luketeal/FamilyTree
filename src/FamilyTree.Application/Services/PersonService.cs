using FamilyTree.Application.Common;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.Repositories;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Application.Services;

/// <summary>
/// Person creation, editing and removal. US-001 to US-004, US-041, US-054.
/// </summary>
public sealed class PersonService(IPersonRepository people)
{
    public sealed record PersonInput(
        string FirstName,
        string LastName,
        string? BirthSurname = null,
        PartialDate? BirthDate = null,
        string? BirthPlace = null,
        PartialDate? DeathDate = null,
        string? DeathPlace = null,
        Gender Gender = Gender.Unknown,
        string? Notes = null);

    public async Task<Result<PersonDetailDto>> CreateAsync(PersonInput input, CancellationToken ct = default)
    {
        var validation = Validate(input);
        if (validation is not null)
        {
            return Result<PersonDetailDto>.Failure(validation);
        }

        var person = new Person(input.FirstName, input.LastName, input.Gender);
        person.UpdateName(input.FirstName, input.LastName, input.BirthSurname);
        person.UpdateDates(input.BirthDate, input.BirthPlace, input.DeathDate, input.DeathPlace);
        person.UpdateNotes(input.Notes);

        var duplicate = await FindLikelyDuplicateAsync(person, ct);

        await people.AddAsync(person, ct);

        var dto = PersonDetailDto.From(person);
        return duplicate is null
            ? Result<PersonDetailDto>.Success(dto)
            : Result<PersonDetailDto>.SuccessWithWarning(dto, DuplicateWarning(duplicate));
    }

    public async Task<Result<PersonDetailDto>> UpdateAsync(Guid id, PersonInput input, CancellationToken ct = default)
    {
        var validation = Validate(input);
        if (validation is not null)
        {
            return Result<PersonDetailDto>.Failure(validation);
        }

        var person = await people.GetByIdAsync(id, ct);
        if (person is null)
        {
            return Result<PersonDetailDto>.Failure($"No person with id {id}.");
        }

        person.UpdateName(input.FirstName, input.LastName, input.BirthSurname);
        person.UpdateDates(input.BirthDate, input.BirthPlace, input.DeathDate, input.DeathPlace);
        person.UpdateGender(input.Gender);
        person.UpdateNotes(input.Notes);

        await people.UpdateAsync(person, ct);

        return Result<PersonDetailDto>.Success(PersonDetailDto.From(person));
    }

    public async Task<Result<PersonDetailDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var person = await people.GetByIdAsync(id, ct);
        return person is null
            ? Result<PersonDetailDto>.Failure($"No person with id {id}.")
            : Result<PersonDetailDto>.Success(PersonDetailDto.From(person));
    }

    /// <summary>
    /// Every real person, surname first. Phantoms are placeholders for unknown
    /// ancestors (US-054) and exist only as tree nodes, so they never appear in
    /// a list, a search result or an export.
    /// </summary>
    public async Task<Result<IReadOnlyList<PersonSummaryDto>>> GetAllAsync(CancellationToken ct = default)
    {
        var all = await people.GetAllAsync(ct);

        var summaries = all
            .Where(p => !p.IsPhantom)
            .Select(PersonSummaryDto.From)
            .OrderBy(p => p.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return Result<IReadOnlyList<PersonSummaryDto>>.Success(summaries);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (!await people.ExistsAsync(id, ct))
        {
            return Result.Failure($"No person with id {id}.");
        }

        await people.DeleteAsync(id, ct);
        return Result.Success();
    }

    /// <summary>
    /// Creates an unnamed placeholder for an ancestor known to exist but not
    /// identified. US-041, US-054.
    /// </summary>
    public async Task<Result<Guid>> CreatePhantomAsync(CancellationToken ct = default)
    {
        var phantom = Person.CreatePhantom();
        await people.AddAsync(phantom, ct);
        return Result<Guid>.Success(phantom.Id);
    }

    private static string? Validate(PersonInput input)
    {
        if (string.IsNullOrWhiteSpace(input.FirstName))
        {
            return "First name is required.";
        }

        if (string.IsNullOrWhiteSpace(input.LastName))
        {
            return "Last name is required.";
        }

        // Only compares what both dates actually record: a year-only death in
        // the same year as a full birth date is not evidence of an error.
        if (input.BirthDate is not null && input.DeathDate is not null
            && input.DeathDate.CompareTo(input.BirthDate) < 0)
        {
            return "Death date cannot be before birth date.";
        }

        return null;
    }

    /// <summary>
    /// Warns rather than blocks. Two cousins genuinely can share a name and a
    /// birth year, and refusing that would make the app wrong about real
    /// families — so this surfaces the collision and lets the user decide.
    /// </summary>
    private async Task<Person?> FindLikelyDuplicateAsync(Person candidate, CancellationToken ct)
    {
        var all = await people.GetAllAsync(ct);

        return all.FirstOrDefault(existing =>
            !existing.IsPhantom
            && existing.Id != candidate.Id
            && string.Equals(existing.FirstName, candidate.FirstName, StringComparison.CurrentCultureIgnoreCase)
            && string.Equals(existing.LastName, candidate.LastName, StringComparison.CurrentCultureIgnoreCase)
            && existing.BirthDate?.Year == candidate.BirthDate?.Year);
    }

    private static string DuplicateWarning(Person existing)
    {
        var year = existing.BirthDate?.Year;
        var born = year is null ? "no birth date recorded" : $"born {year}";
        return $"{existing.FirstName} {existing.LastName} ({born}) already exists. Added anyway — check this is not the same person.";
    }
}
