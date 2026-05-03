using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Domain.Tests.Entities;

public sealed class PersonTests
{
    // ── Constructor ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsTrimmedFirstName()
    {
        var person = new Person("  Alice  ", "Smith", Gender.Female);
        Assert.Equal("Alice", person.FirstName);
    }

    [Fact]
    public void Constructor_SetsTrimmedLastName()
    {
        var person = new Person("Alice", "  Smith  ", Gender.Female);
        Assert.Equal("Smith", person.LastName);
    }

    [Fact]
    public void Constructor_SetsGender()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        Assert.Equal(Gender.Female, person.Gender);
    }

    [Fact]
    public void Constructor_AssignsNonEmptyId()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        Assert.NotEqual(Guid.Empty, person.Id);
    }

    [Fact]
    public void Constructor_LeavesOptionalFieldsNull()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        Assert.Null(person.BirthSurname);
        Assert.Null(person.BirthDate);
        Assert.Null(person.BirthPlace);
        Assert.Null(person.DeathDate);
        Assert.Null(person.DeathPlace);
        Assert.Null(person.PhotoPath);
        Assert.Null(person.Notes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenFirstNameIsEmpty(string firstName)
    {
        Assert.Throws<ArgumentException>(() => new Person(firstName, "Smith", Gender.Female));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenLastNameIsEmpty(string lastName)
    {
        Assert.Throws<ArgumentException>(() => new Person("Alice", lastName, Gender.Female));
    }

    // ── UpdateName ────────────────────────────────────────────────────────

    [Fact]
    public void UpdateName_UpdatesFirstName()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        person.UpdateName("Betty", "Smith", null);
        Assert.Equal("Betty", person.FirstName);
    }

    [Fact]
    public void UpdateName_UpdatesLastName()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        person.UpdateName("Alice", "Jones", null);
        Assert.Equal("Jones", person.LastName);
    }

    [Fact]
    public void UpdateName_SetsTrimmedBirthSurname()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        person.UpdateName("Alice", "Jones", "  Smith  ");
        Assert.Equal("Smith", person.BirthSurname);
    }

    [Fact]
    public void UpdateName_ClearsBirthSurname_WhenNull()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        person.UpdateName("Alice", "Jones", "Smith");
        person.UpdateName("Alice", "Jones", null);
        Assert.Null(person.BirthSurname);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateName_Throws_WhenFirstNameIsEmpty(string firstName)
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        Assert.Throws<ArgumentException>(() => person.UpdateName(firstName, "Smith", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateName_Throws_WhenLastNameIsEmpty(string lastName)
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        Assert.Throws<ArgumentException>(() => person.UpdateName("Alice", lastName, null));
    }

    // ── UpdateDates ───────────────────────────────────────────────────────

    [Fact]
    public void UpdateDates_SetsBirthDate()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        var date = PartialDate.FromYear(1990);
        person.UpdateDates(date, null, null, null);
        Assert.Equal(date, person.BirthDate);
    }

    [Fact]
    public void UpdateDates_SetsTrimmedBirthPlace()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        person.UpdateDates(null, "  Springfield, IL  ", null, null);
        Assert.Equal("Springfield, IL", person.BirthPlace);
    }

    [Fact]
    public void UpdateDates_SetsDeathDate()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        var date = PartialDate.FromYear(2020);
        person.UpdateDates(null, null, date, null);
        Assert.Equal(date, person.DeathDate);
    }

    [Fact]
    public void UpdateDates_ClearsDeathDate_WhenNull()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        person.UpdateDates(null, null, PartialDate.FromYear(2020), null);
        person.UpdateDates(null, null, null, null);
        Assert.Null(person.DeathDate);
    }

    // ── UpdateGender / UpdatePhoto / UpdateNotes ──────────────────────────

    [Fact]
    public void UpdateGender_ChangesGender()
    {
        var person = new Person("Alex", "Smith", Gender.Unknown);
        person.UpdateGender(Gender.NonBinary);
        Assert.Equal(Gender.NonBinary, person.Gender);
    }

    [Fact]
    public void UpdatePhoto_SetsPhotoPath()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        person.UpdatePhoto("/photos/alice.jpg");
        Assert.Equal("/photos/alice.jpg", person.PhotoPath);
    }

    [Fact]
    public void UpdatePhoto_ClearsPhotoPath_WhenNull()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        person.UpdatePhoto("/photos/alice.jpg");
        person.UpdatePhoto(null);
        Assert.Null(person.PhotoPath);
    }

    [Fact]
    public void UpdateNotes_SetsNotes()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        person.UpdateNotes("Some biographical notes.");
        Assert.Equal("Some biographical notes.", person.Notes);
    }

    [Fact]
    public void UpdateNotes_ClearsNotes_WhenNull()
    {
        var person = new Person("Alice", "Smith", Gender.Female);
        person.UpdateNotes("notes");
        person.UpdateNotes(null);
        Assert.Null(person.Notes);
    }

    // ── Id uniqueness ─────────────────────────────────────────────────────

    [Fact]
    public void TwoPersons_HaveDifferentIds()
    {
        var a = new Person("Alice", "Smith", Gender.Female);
        var b = new Person("Bob", "Smith", Gender.Male);
        Assert.NotEqual(a.Id, b.Id);
    }
}
