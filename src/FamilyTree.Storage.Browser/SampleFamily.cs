using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;
using FamilyTree.Storage.Browser.Records;

namespace FamilyTree.Storage.Browser;

/// <summary>
/// A demonstration family that exercises the cases this app exists to handle.
/// </summary>
/// <remarks>
/// Testers given an empty tree report nothing useful, and a tidy nuclear family
/// would exercise none of the hard parts. This one deliberately contains
/// half-siblings through a shared parent, an adoption, a remarriage after
/// widowhood, an unidentified ancestor, and a relationship nobody is sure
/// about — the situations that make a family tree difficult.
/// </remarks>
public static class SampleFamily
{
    public static DatasetRecord Build()
    {
        // First generation. Margaret's mother is known to have existed but was
        // never identified, so she is a phantom rather than a gap (US-054).
        var arthur = Person("Arthur", "Whitfield", Gender.Male, 1918, 1991);
        var margaret = Person("Margaret", "Whitfield", Gender.Female, 1921, 2004, birthSurname: "Ellery");
        var unknownGrandmother = Domain.Entities.Person.CreatePhantom();

        // Arthur remarried after Margaret died (US-042, US-026).
        var vera = Person("Vera", "Whitfield", Gender.Female, 1930, 2011, birthSurname: "Nash");

        // Second generation. Susan and Thomas are full siblings; Daniel shares
        // only Arthur, making him their half-brother (US-037).
        var susan = Person("Susan", "Hartley", Gender.Female, 1948, birthSurname: "Whitfield");
        var thomas = Person("Thomas", "Whitfield", Gender.Male, 1951);
        var daniel = Person("Daniel", "Whitfield", Gender.Male, 1963);

        var raymond = Person("Raymond", "Hartley", Gender.Male, 1945, 2019);

        // Third generation. Priya was adopted by Susan and Raymond (US-014),
        // and also has a recorded biological mother, so she carries both kinds
        // of parent at once (US-039).
        var eleanor = Person("Eleanor", "Hartley", Gender.Female, 1972);
        var priya = Person("Priya", "Hartley", Gender.Female, 1975, birthSurname: "Chandra");
        var biologicalMother = Person("Anita", "Chandra", Gender.Female, 1950);

        var people = new[]
        {
            arthur, margaret, vera, susan, thomas, daniel,
            raymond, eleanor, priya, biologicalMother, unknownGrandmother,
        };

        var marriages = new[]
        {
            // Ended by Margaret's death, which is what US-026 auto-fills from.
            Marriage(arthur, margaret, 1946, "Leeds", endYear: 1973, MarriageEndReason.DeathOfSpouse),
            Marriage(arthur, vera, 1976, "Harrogate"),
            Marriage(susan, raymond, 1970, "Sheffield"),
        };

        var biological = new[]
        {
            Bio(arthur, susan),
            Bio(margaret, susan),
            Bio(arthur, thomas),
            Bio(margaret, thomas),

            // Daniel shares Arthur only — the half-sibling case.
            Bio(arthur, daniel),

            Bio(unknownGrandmother, margaret),

            Bio(susan, eleanor),
            Bio(raymond, eleanor),

            // Priya's biological mother is recorded even though she was raised
            // by the Hartleys. Speculative: the family is not certain (US-055).
            Bio(biologicalMother, priya, RelationshipCertainty.Speculative),
        };

        var adoptive = new[]
        {
            Adoptive(susan, priya, 1977),
            Adoptive(raymond, priya, 1977),
        };

        return new DatasetRecord(
            people.Select(PersonRecord.From).ToList(),
            biological.Select(BiologicalLinkRecord.From).ToList(),
            adoptive.Select(AdoptiveLinkRecord.From).ToList(),
            marriages.Select(MarriageRecord.From).ToList(),
            []);
    }

    private static Person Person(
        string first, string last, Gender gender,
        int birthYear, int? deathYear = null, string? birthSurname = null)
    {
        var person = new Person(first, last, gender);
        person.UpdateName(first, last, birthSurname);
        person.UpdateDates(
            PartialDate.FromYear(birthYear),
            null,
            deathYear is int year ? PartialDate.FromYear(year) : null,
            null);
        return person;
    }

    private static Marriage Marriage(
        Person a, Person b, int startYear, string? place,
        int? endYear = null, MarriageEndReason? endReason = null)
    {
        var marriage = new Marriage(a.Id, b.Id, PartialDate.FromYear(startYear), place);
        if (endYear is int year)
        {
            marriage.UpdateDates(PartialDate.FromYear(startYear), place, PartialDate.FromYear(year), endReason);
        }

        return marriage;
    }

    private static BiologicalParentChild Bio(
        Person parent, Person child, RelationshipCertainty certainty = RelationshipCertainty.Confirmed) =>
        new(parent.Id, child.Id, certainty);

    private static AdoptiveParentChild Adoptive(Person parent, Person child, int adoptionYear) =>
        new(parent.Id, child.Id, PartialDate.FromYear(adoptionYear));
}
