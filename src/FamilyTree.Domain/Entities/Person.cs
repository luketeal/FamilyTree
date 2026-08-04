using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Domain.Entities;

public sealed class Person
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? BirthSurname { get; private set; }
    public PartialDate? BirthDate { get; private set; }
    public string? BirthPlace { get; private set; }
    public PartialDate? DeathDate { get; private set; }
    public string? DeathPlace { get; private set; }
    public Gender Gender { get; private set; }
    public string? PhotoPath { get; private set; }
    public string? Notes { get; private set; }
    public bool IsPhantom { get; private set; }

    private readonly List<BiologicalParentChild> _biologicalParentLinks = [];
    private readonly List<BiologicalParentChild> _biologicalChildLinks = [];
    private readonly List<AdoptiveParentChild> _adoptiveParentLinks = [];
    private readonly List<AdoptiveParentChild> _adoptiveChildLinks = [];
    private readonly List<Marriage> _marriagesAsSpouse1 = [];
    private readonly List<Marriage> _marriagesAsSpouse2 = [];
    private readonly List<StepparentRelationship> _stepparentLinks = [];
    private readonly List<StepparentRelationship> _stepchildLinks = [];

    public IReadOnlyList<BiologicalParentChild> BiologicalParentLinks => _biologicalParentLinks;
    public IReadOnlyList<BiologicalParentChild> BiologicalChildLinks => _biologicalChildLinks;
    public IReadOnlyList<AdoptiveParentChild> AdoptiveParentLinks => _adoptiveParentLinks;
    public IReadOnlyList<AdoptiveParentChild> AdoptiveChildLinks => _adoptiveChildLinks;
    public IReadOnlyList<Marriage> MarriagesAsSpouse1 => _marriagesAsSpouse1;
    public IReadOnlyList<Marriage> MarriagesAsSpouse2 => _marriagesAsSpouse2;
    public IReadOnlyList<StepparentRelationship> StepparentLinks => _stepparentLinks;
    public IReadOnlyList<StepparentRelationship> StepchildLinks => _stepchildLinks;

    private Person() { }

    public Person(string firstName, string lastName, Gender gender)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name is required.", nameof(lastName));

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Gender = gender;
    }

    public static Person CreatePhantom()
    {
        var phantom = new Person();
        phantom.IsPhantom = true;
        return phantom;
    }

    public void UpdateName(string firstName, string lastName, string? birthSurname)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name is required.", nameof(lastName));

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        BirthSurname = birthSurname?.Trim();
    }

    public void UpdateDates(PartialDate? birthDate, string? birthPlace, PartialDate? deathDate, string? deathPlace)
    {
        BirthDate = birthDate;
        BirthPlace = birthPlace?.Trim();
        DeathDate = deathDate;
        DeathPlace = deathPlace?.Trim();
    }

    public void UpdateGender(Gender gender) => Gender = gender;

    public void UpdatePhoto(string? photoPath) => PhotoPath = photoPath;

    public void UpdateNotes(string? notes) => Notes = notes;

    /// <summary>
    /// Reconstitutes a person from storage. Bypasses the validation in the
    /// public constructor deliberately: these values were validated when the
    /// person was created, and a stored record must round-trip exactly rather
    /// than be re-judged by rules that may have changed since.
    /// </summary>
    public static Person Rehydrate(
        Guid id, string firstName, string lastName, string? birthSurname,
        PartialDate? birthDate, string? birthPlace, PartialDate? deathDate, string? deathPlace,
        Gender gender, string? photoPath, string? notes, bool isPhantom) => new()
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            BirthSurname = birthSurname,
            BirthDate = birthDate,
            BirthPlace = birthPlace,
            DeathDate = deathDate,
            DeathPlace = deathPlace,
            Gender = gender,
            PhotoPath = photoPath,
            Notes = notes,
            IsPhantom = isPhantom,
        };
}
