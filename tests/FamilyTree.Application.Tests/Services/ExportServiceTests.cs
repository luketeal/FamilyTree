using System.Text.Json;
using FamilyTree.Application.Common;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Application.Tests.Services;

public class ExportServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTree _tree = new();

    private ExportService CreateService(DateTimeOffset? now = null) => new(
        _tree.PersonRepository,
        _tree.BiologicalRepository,
        _tree.AdoptiveRepository,
        _tree.MarriageRepository,
        new FixedClock(now ?? Noon));

    private static Person Ada() => Person.Rehydrate(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "Ada", "Lovelace", "Byron",
        PartialDate.FromYearMonthDay(1815, 12, 10), "London",
        PartialDate.FromYear(1852), null,
        Gender.Female, null, "Wrote the first algorithm.", isPhantom: false);

    private static Person Grace() => Person.Rehydrate(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "Grace", "Hopper", null,
        PartialDate.FromYear(1906, isApproximate: true), null, null, null,
        Gender.Female, null, null, isPhantom: false);

    private async Task<JsonDocument> ExportAsync()
    {
        var result = await CreateService().ExportToJsonAsync();
        Assert.True(result.IsSuccess, result.Error);
        return JsonDocument.Parse(result.Value!.Json);
    }

    // The premise of the whole feature: an empty file would be a backup of
    // nothing, and the natural thing to do with a backup is save it over the
    // last one.
    [Fact]
    public async Task RefusesToExport_WhenTheTreeIsEmpty()
    {
        var result = await CreateService().ExportToJsonAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("nothing to export", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StampsTheSchemaVersion()
    {
        _tree.With(Ada());

        using var json = await ExportAsync();

        Assert.Equal(TreeSchema.Version, json.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task StampsTheExportTime()
    {
        _tree.With(Ada());

        using var json = await ExportAsync();

        Assert.Equal(Noon, json.RootElement.GetProperty("exportedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task NamesTheApplicationThatWroteIt()
    {
        _tree.With(Ada());

        using var json = await ExportAsync();

        Assert.Equal("FamilyTree", json.RootElement.GetProperty("application").GetString());
    }

    [Fact]
    public async Task IncludesEveryRealPerson()
    {
        _tree.With(Ada(), Grace());

        using var json = await ExportAsync();

        Assert.Equal(2, json.RootElement.GetProperty("people").GetArrayLength());
    }

    // Precision is the thing PartialDate exists to carry. A year-only date that
    // comes back claiming a month is a fact the file invented.
    [Fact]
    public async Task KeepsDatePrecisionAndTheCircaFlag()
    {
        _tree.With(Grace());

        using var json = await ExportAsync();
        var date = json.RootElement.GetProperty("people")[0].GetProperty("birthDate");

        Assert.Equal(1906, date.GetProperty("year").GetInt32());
        Assert.True(date.GetProperty("isApproximate").GetBoolean());
        Assert.False(date.TryGetProperty("month", out _));
    }

    [Fact]
    public async Task KeepsAFullDate()
    {
        _tree.With(Ada());

        using var json = await ExportAsync();
        var date = json.RootElement.GetProperty("people")[0].GetProperty("birthDate");

        Assert.Equal(12, date.GetProperty("month").GetInt32());
        Assert.Equal(10, date.GetProperty("day").GetInt32());
    }

    // Ordinals would silently change meaning the moment a member is inserted
    // into the enum, and every file already written would then say something
    // else.
    [Fact]
    public async Task WritesEnumsAsNames()
    {
        _tree.With(Ada());

        using var json = await ExportAsync();

        Assert.Equal("Female", json.RootElement.GetProperty("people")[0].GetProperty("gender").GetString());
    }

    [Fact]
    public async Task ExcludesPhantomPeople()
    {
        _tree.With(Ada(), Person.CreatePhantom());

        using var json = await ExportAsync();

        Assert.Equal(1, json.RootElement.GetProperty("people").GetArrayLength());
    }

    // A link to somebody the file does not contain is a dangling reference that
    // import would have to reject on arrival, so it never gets written.
    [Fact]
    public async Task ExcludesLinksThatReferenceAPhantom()
    {
        var ada = Ada();
        var phantom = Person.CreatePhantom();
        _tree.With(ada, phantom).With(new BiologicalParentChild(phantom.Id, ada.Id));

        using var json = await ExportAsync();

        Assert.Equal(0, json.RootElement.GetProperty("biologicalLinks").GetArrayLength());
    }

    // The exclusion is a subtraction from what the user believes they have, so
    // it is reported rather than done quietly.
    [Fact]
    public async Task ReportsWhatWasLeftOut()
    {
        var ada = Ada();
        var phantom = Person.CreatePhantom();
        _tree.With(ada, phantom).With(new BiologicalParentChild(phantom.Id, ada.Id));

        var result = await CreateService().ExportToJsonAsync();

        Assert.Equal(1, result.Value!.Summary.PhantomsExcluded);
        Assert.Equal(1, result.Value!.Summary.LinksToPhantomsExcluded);
    }

    [Fact]
    public async Task CountsWhatWasIncluded()
    {
        var ada = Ada();
        var grace = Grace();
        _tree.With(ada, grace).With(new BiologicalParentChild(grace.Id, ada.Id));

        var result = await CreateService().ExportToJsonAsync();

        Assert.Equal(2, result.Value!.Summary.People);
        Assert.Equal(1, result.Value!.Summary.Relationships);
    }

    [Fact]
    public async Task IncludesRelationshipsOfEveryKind()
    {
        var ada = Ada();
        var grace = Grace();
        _tree.With(ada, grace)
            .With(new BiologicalParentChild(grace.Id, ada.Id))
            .With(new AdoptiveParentChild(grace.Id, ada.Id, PartialDate.FromYear(1820)))
            .With(new Marriage(ada.Id, grace.Id, PartialDate.FromYear(1835), "London"));

        using var json = await ExportAsync();

        Assert.Equal(1, json.RootElement.GetProperty("biologicalLinks").GetArrayLength());
        Assert.Equal(1, json.RootElement.GetProperty("adoptiveLinks").GetArrayLength());
        Assert.Equal(1, json.RootElement.GetProperty("marriages").GetArrayLength());
    }

    // A backup that reorders itself on every save cannot be diffed, and diffing
    // is how somebody checks a backup is what they think it is.
    [Fact]
    public async Task ProducesTheSameBytesForTheSameTree()
    {
        _tree.With(Grace(), Ada());
        var first = await CreateService().ExportToJsonAsync();

        var reordered = new InMemoryTree().With(Ada(), Grace());
        var second = await new ExportService(
            reordered.PersonRepository,
            reordered.BiologicalRepository,
            reordered.AdoptiveRepository,
            reordered.MarriageRepository,
            new FixedClock(Noon)).ExportToJsonAsync();

        Assert.Equal(first.Value!.Json, second.Value!.Json);
    }

    [Fact]
    public async Task NamesTheFileByDateSoAFolderOfBackupsSortsChronologically()
    {
        _tree.With(Ada());

        var result = await CreateService(new DateTimeOffset(2026, 3, 9, 22, 30, 0, TimeSpan.Zero))
            .ExportToJsonAsync();

        Assert.Equal("familytree-2026-03-09.json", result.Value!.FileName);
    }

    // A file somebody has to repair by hand at 2am is one they can read.
    [Fact]
    public async Task WritesIndentedJson()
    {
        _tree.With(Ada());

        var result = await CreateService().ExportToJsonAsync();

        Assert.Contains('\n', result.Value!.Json);
    }
}
