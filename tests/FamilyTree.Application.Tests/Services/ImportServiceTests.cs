using FamilyTree.Application.Common;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.Application.Tests.Services;

public class ImportServiceTests
{
    private const string AdaId = "11111111-1111-1111-1111-111111111111";
    private const string GraceId = "22222222-2222-2222-2222-222222222222";
    private const string LinkId = "33333333-3333-3333-3333-333333333333";

    private readonly InMemoryTree _tree = new();

    private ImportService CreateService() => new(
        _tree.PersonRepository,
        _tree.BiologicalRepository,
        _tree.AdoptiveRepository,
        _tree.MarriageRepository,
        _tree.Administration);

    private static string File(string body) =>
        $$"""{"schemaVersion": {{TreeSchema.Version}}, "exportedAt": "2026-08-07T12:00:00+00:00", {{body}}}""";

    private static string OnePerson(string id = AdaId, string first = "Ada", string last = "Lovelace") =>
        File($$"""
            "people": [{"id": "{{id}}", "firstName": "{{first}}", "lastName": "{{last}}", "gender": "Female"}]
            """);

    private static Person Stored(string id, string first, string last) => Person.Rehydrate(
        Guid.Parse(id), first, last, null, null, null, null, null,
        Gender.Unknown, null, null, isPhantom: false);

    private const string PhantomId = "44444444-4444-4444-4444-444444444444";

    // ---- The schema stamp. Import is the first code to read what PR 2 wrote. ----

    [Fact]
    public void RejectsAFileWithNoSchemaVersion()
    {
        var result = CreateService().Preview(
            """{"people": [{"id": "11111111-1111-1111-1111-111111111111", "firstName": "Ada", "lastName": "Lovelace"}]}""");

        Assert.False(result.IsSuccess);
        Assert.Contains("schemaVersion", result.Error);
    }

    // Field names are stable across versions and their meanings are not, so a
    // file from a later release cannot be read optimistically — it would be
    // read wrongly and written back as if it were right.
    [Fact]
    public void RejectsAFileFromANewerVersion()
    {
        var result = CreateService().Preview(
            $$"""{"schemaVersion": {{TreeSchema.Version + 1}}, "people": []}""");

        Assert.False(result.IsSuccess);
        Assert.Contains("newer version", result.Error);
    }

    [Fact]
    public void RejectsANonsenseSchemaVersion()
    {
        var result = CreateService().Preview("""{"schemaVersion": 0, "people": []}""");

        Assert.False(result.IsSuccess);
        Assert.Contains("not a version", result.Error);
    }

    [Fact]
    public void AcceptsAFileStampedWithTheCurrentVersion()
    {
        var result = CreateService().Preview(OnePerson());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(TreeSchema.Version, result.Value!.SchemaVersion);
    }

    // ---- Malformed input ----

    [Fact]
    public void RejectsAnEmptyFile()
    {
        var result = CreateService().Preview("   ");

        Assert.False(result.IsSuccess);
        Assert.Contains("empty", result.Error);
    }

    [Fact]
    public void RejectsSomethingThatIsNotJson()
    {
        var result = CreateService().Preview("this is not a family tree");

        Assert.False(result.IsSuccess);
        Assert.Contains("not readable", result.Error);
    }

    [Fact]
    public void RejectsAJsonFileWithNoRecordsAtAll()
    {
        var result = CreateService().Preview($$"""{"schemaVersion": {{TreeSchema.Version}}}""");

        Assert.False(result.IsSuccess);
        Assert.Contains("nothing to import", result.Error);
    }

    // ---- Preview reports without touching the tree ----

    [Fact]
    public void PreviewReportsWhatIsInTheFile()
    {
        var result = CreateService().Preview(File($$"""
            "people": [
                {"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"},
                {"id": "{{GraceId}}", "firstName": "Grace", "lastName": "Hopper"}
            ],
            "biologicalLinks": [{"id": "{{LinkId}}", "parentId": "{{GraceId}}", "childId": "{{AdaId}}"}]
            """));

        Assert.Equal(2, result.Value!.People);
        Assert.Equal(1, result.Value!.Relationships);
    }

    [Fact]
    public void PreviewReportsWhenTheFileWasWritten()
    {
        var result = CreateService().Preview(OnePerson());

        Assert.Equal(
            new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero),
            result.Value!.ExportedAt);
    }

    [Fact]
    public void PreviewDoesNotChangeTheTree()
    {
        _tree.With(Stored(GraceId, "Grace", "Hopper"));

        CreateService().Preview(OnePerson());

        Assert.Single(_tree.People);
        Assert.Equal(0, _tree.ReplaceCount);
    }

    // ---- Writing ----

    [Fact]
    public async Task ImportsAPersonIntoAnEmptyTree()
    {
        await CreateService().ImportAsync(OnePerson(), ImportConflictResolution.Skip);

        Assert.Equal("Ada", Assert.Single(_tree.People).FirstName);
    }

    // The API-seam requirement, not an implementation detail: four separate
    // writes can half-apply and leave links pointing at people never stored.
    [Fact]
    public async Task WritesTheWholeTreeInOneCall()
    {
        await CreateService().ImportAsync(File($$"""
            "people": [
                {"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"},
                {"id": "{{GraceId}}", "firstName": "Grace", "lastName": "Hopper"}
            ],
            "biologicalLinks": [{"id": "{{LinkId}}", "parentId": "{{GraceId}}", "childId": "{{AdaId}}"}]
            """), ImportConflictResolution.Overwrite);

        Assert.Equal(1, _tree.ReplaceCount);
    }

    // Exports never contain phantoms, so nothing arriving through a file is one.
    // Rehydrate would happily make one, which would be a person invisible to
    // every list and search in the app.
    [Fact]
    public async Task ImportedPeopleAreNeverPhantoms()
    {
        await CreateService().ImportAsync(OnePerson(), ImportConflictResolution.Skip);

        Assert.False(Assert.Single(_tree.People).IsPhantom);
    }

    [Fact]
    public async Task ImportsEveryRelationshipKind()
    {
        await CreateService().ImportAsync(File($$"""
            "people": [
                {"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"},
                {"id": "{{GraceId}}", "firstName": "Grace", "lastName": "Hopper"}
            ],
            "biologicalLinks": [
                {"id": "44444444-4444-4444-4444-444444444444", "parentId": "{{GraceId}}", "childId": "{{AdaId}}", "certainty": "Speculative"}
            ],
            "adoptiveLinks": [
                {"id": "55555555-5555-5555-5555-555555555555", "parentId": "{{GraceId}}", "childId": "{{AdaId}}",
                 "adoptionDate": {"year": 1820, "isApproximate": false} }
            ],
            "marriages": [
                {"id": "66666666-6666-6666-6666-666666666666", "spouse1Id": "{{AdaId}}", "spouse2Id": "{{GraceId}}",
                 "startDate": {"year": 1835, "isApproximate": false}, "startPlace": "London",
                 "endReason": "Divorce", "endDate": {"year": 1840, "isApproximate": false} }
            ]
            """), ImportConflictResolution.Overwrite);

        Assert.Equal(RelationshipCertainty.Speculative, Assert.Single(_tree.BiologicalLinks).Certainty);
        Assert.Equal(1820, Assert.Single(_tree.AdoptiveLinks).AdoptionDate!.Year);
        Assert.Equal(MarriageEndReason.Divorce, Assert.Single(_tree.Marriages).EndReason);
    }

    // ---- Conflict resolution ----

    private const string CollidingFile = $$"""
        {"schemaVersion": 1, "people": [{"id": "{{AdaId}}", "firstName": "Augusta", "lastName": "King"}]}
        """;

    [Fact]
    public async Task SkipKeepsTheRecordAlreadyInTheTree()
    {
        _tree.With(Stored(AdaId, "Ada", "Lovelace"));

        await CreateService().ImportAsync(CollidingFile, ImportConflictResolution.Skip);

        Assert.Equal("Ada", Assert.Single(_tree.People).FirstName);
    }

    [Fact]
    public async Task SkipReportsTheCollisionRatherThanSwallowingIt()
    {
        _tree.With(Stored(AdaId, "Ada", "Lovelace"));

        var result = await CreateService().ImportAsync(CollidingFile, ImportConflictResolution.Skip);

        Assert.Equal(1, result.Value!.PeopleSkipped);
        Assert.Equal(0, result.Value!.PeopleAdded);
    }

    [Fact]
    public async Task SkipStillAddsRecordsTheTreeDoesNotHave()
    {
        _tree.With(Stored(GraceId, "Grace", "Hopper"));

        await CreateService().ImportAsync(OnePerson(), ImportConflictResolution.Skip);

        Assert.Equal(2, _tree.People.Count);
    }

    [Fact]
    public async Task MergeLetsTheFileWin()
    {
        _tree.With(Stored(AdaId, "Ada", "Lovelace"));

        await CreateService().ImportAsync(CollidingFile, ImportConflictResolution.Merge);

        Assert.Equal("Augusta", Assert.Single(_tree.People).FirstName);
    }

    [Fact]
    public async Task MergeKeepsRecordsTheFileDoesNotMention()
    {
        _tree.With(Stored(GraceId, "Grace", "Hopper"));

        await CreateService().ImportAsync(OnePerson(), ImportConflictResolution.Merge);

        Assert.Contains(_tree.People, p => p.FirstName == "Grace");
    }

    [Fact]
    public async Task MergeReportsAnUpdateRatherThanAnAddition()
    {
        _tree.With(Stored(AdaId, "Ada", "Lovelace"));

        var result = await CreateService().ImportAsync(CollidingFile, ImportConflictResolution.Merge);

        Assert.Equal(1, result.Value!.PeopleUpdated);
        Assert.Equal(0, result.Value!.PeopleAdded);
    }

    [Fact]
    public async Task OverwriteDiscardsWhatTheFileDoesNotContain()
    {
        _tree.With(Stored(GraceId, "Grace", "Hopper"));

        await CreateService().ImportAsync(OnePerson(), ImportConflictResolution.Overwrite);

        Assert.Equal("Ada", Assert.Single(_tree.People).FirstName);
    }

    // Overwrite is the only resolution that destroys anything, so the count of
    // what it destroyed is the number the user most needs to be told.
    [Fact]
    public async Task OverwriteReportsWhatItRemoved()
    {
        _tree.With(Stored(GraceId, "Grace", "Hopper"));

        var result = await CreateService().ImportAsync(OnePerson(), ImportConflictResolution.Overwrite);

        Assert.Equal(1, result.Value!.PeopleRemoved);
    }

    // Phantoms are records the display count hides. Overwrite takes them with
    // everything else, and Skip has to leave them alone — a tree of nothing but
    // unidentified ancestors is still somebody's work.
    [Fact]
    public async Task SkipLeavesPhantomsAlone()
    {
        _tree.With(Person.CreatePhantom());

        await CreateService().ImportAsync(OnePerson(), ImportConflictResolution.Skip);

        Assert.Contains(_tree.People, p => p.IsPhantom);
    }

    [Fact]
    public async Task OverwriteRemovesPhantomsWithEverythingElse()
    {
        _tree.With(Person.CreatePhantom());

        await CreateService().ImportAsync(OnePerson(), ImportConflictResolution.Overwrite);

        Assert.DoesNotContain(_tree.People, p => p.IsPhantom);
    }

    // ---- Bad records inside an otherwise good file ----

    [Fact]
    public async Task RejectsANamelessPersonWithoutLosingTheRestOfTheFile()
    {
        var result = await CreateService().ImportAsync(File($$"""
            "people": [
                {"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"},
                {"id": "{{GraceId}}", "firstName": "", "lastName": ""}
            ]
            """), ImportConflictResolution.Overwrite);

        Assert.Single(_tree.People);
        Assert.Equal(1, result.Value!.RecordsRejected);
    }

    [Fact]
    public async Task RejectsAPersonWithAnImpossibleDate()
    {
        var result = await CreateService().ImportAsync(File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace",
                        "birthDate": {"year": 1815, "month": 2, "day": 31, "isApproximate": false} }]
            """), ImportConflictResolution.Overwrite);

        Assert.False(result.IsSuccess);
        Assert.Empty(_tree.People);
    }

    [Fact]
    public async Task RejectsAPersonWithNoId()
    {
        var result = await CreateService().ImportAsync(File($$"""
            "people": [
                {"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"},
                {"firstName": "Grace", "lastName": "Hopper"}
            ]
            """), ImportConflictResolution.Overwrite);

        Assert.Single(_tree.People);
        Assert.Equal(1, result.Value!.RecordsRejected);
    }

    [Fact]
    public async Task RejectsTheSecondCopyOfARepeatedId()
    {
        var result = await CreateService().ImportAsync(File($$"""
            "people": [
                {"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"},
                {"id": "{{AdaId}}", "firstName": "Augusta", "lastName": "King"}
            ]
            """), ImportConflictResolution.Overwrite);

        Assert.Equal("Ada", Assert.Single(_tree.People).FirstName);
        Assert.Equal(1, result.Value!.RecordsRejected);
    }

    // Notes over the limit and a death before a birth are wrong but recoverable
    // by editing the person. Dropping somebody's ancestor to enforce a field
    // limit would be the worse outcome.
    [Fact]
    public async Task KeepsAPersonWhoseDatesAreOutOfOrder_AndSaysSo()
    {
        var result = await CreateService().ImportAsync(File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace",
                        "birthDate": {"year": 1852, "isApproximate": false},
                        "deathDate": {"year": 1815, "isApproximate": false} }]
            """), ImportConflictResolution.Overwrite);

        Assert.Single(_tree.People);
        Assert.Contains(result.Value!.Warnings, w => w.Contains("death date before"));
    }

    [Fact]
    public async Task KeepsAPersonWithOverlongNotes_AndSaysSo()
    {
        var notes = new string('x', PersonService.NotesLimit + 1);
        var result = await CreateService().ImportAsync(File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace", "notes": "{{notes}}"}]
            """), ImportConflictResolution.Overwrite);

        Assert.Single(_tree.People);
        Assert.Contains(result.Value!.Warnings, w => w.Contains("longer than"));
    }

    // ---- Referential integrity ----

    [Fact]
    public async Task DropsALinkToSomebodyWhoIsNotInTheTree()
    {
        var result = await CreateService().ImportAsync(File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"}],
            "biologicalLinks": [{"id": "{{LinkId}}", "parentId": "{{GraceId}}", "childId": "{{AdaId}}"}]
            """), ImportConflictResolution.Overwrite);

        Assert.Empty(_tree.BiologicalLinks);
        Assert.Equal(1, result.Value!.RecordsRejected);
    }

    // Under Skip and Merge the missing referent may already be stored, so a link
    // that would dangle against the file alone is perfectly valid against the
    // tree it is being added to.
    [Fact]
    public async Task KeepsALinkWhoseOtherEndIsAlreadyInTheTree()
    {
        _tree.With(Stored(GraceId, "Grace", "Hopper"));

        await CreateService().ImportAsync(File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"}],
            "biologicalLinks": [{"id": "{{LinkId}}", "parentId": "{{GraceId}}", "childId": "{{AdaId}}"}]
            """), ImportConflictResolution.Skip);

        Assert.Single(_tree.BiologicalLinks);
    }

    // Rehydrate skips the constructor guards, so a file can express a
    // relationship the app itself refuses to create.
    [Fact]
    public async Task RejectsALinkFromAPersonToThemselves()
    {
        var result = await CreateService().ImportAsync(File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"}],
            "biologicalLinks": [{"id": "{{LinkId}}", "parentId": "{{AdaId}}", "childId": "{{AdaId}}"}]
            """), ImportConflictResolution.Overwrite);

        Assert.Empty(_tree.BiologicalLinks);
        Assert.Equal(1, result.Value!.RecordsRejected);
    }

    [Fact]
    public async Task RejectsAMarriageWithNoStartDate()
    {
        var result = await CreateService().ImportAsync(File($$"""
            "people": [
                {"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"},
                {"id": "{{GraceId}}", "firstName": "Grace", "lastName": "Hopper"}
            ],
            "marriages": [{"id": "{{LinkId}}", "spouse1Id": "{{AdaId}}", "spouse2Id": "{{GraceId}}"}]
            """), ImportConflictResolution.Overwrite);

        Assert.Empty(_tree.Marriages);
        Assert.Equal(1, result.Value!.RecordsRejected);
    }

    // ---- Duplicates by meaning rather than by id ----

    [Fact]
    public async Task DropsASecondLinkBetweenTheSameParentAndChild()
    {
        var ada = Guid.Parse(AdaId);
        var grace = Guid.Parse(GraceId);
        _tree.With(Stored(AdaId, "Ada", "Lovelace"), Stored(GraceId, "Grace", "Hopper"))
            .With(new BiologicalParentChild(grace, ada));

        var result = await CreateService().ImportAsync(File($$"""
            "biologicalLinks": [{"id": "{{LinkId}}", "parentId": "{{GraceId}}", "childId": "{{AdaId}}"}]
            """), ImportConflictResolution.Skip);

        Assert.Single(_tree.BiologicalLinks);
        Assert.Equal(1, result.Value!.RelationshipsSkipped);
    }

    // Which spouse is recorded first is an accident of data entry.
    [Fact]
    public async Task TreatsAMarriageAsTheSameWhicheverSpouseIsListedFirst()
    {
        var ada = Guid.Parse(AdaId);
        var grace = Guid.Parse(GraceId);
        _tree.With(Stored(AdaId, "Ada", "Lovelace"), Stored(GraceId, "Grace", "Hopper"))
            .With(new Marriage(ada, grace, PartialDate.FromYear(1835)));

        await CreateService().ImportAsync(File($$"""
            "marriages": [{"id": "{{LinkId}}", "spouse1Id": "{{GraceId}}", "spouse2Id": "{{AdaId}}",
                           "startDate": {"year": 1835, "isApproximate": false} }]
            """), ImportConflictResolution.Skip);

        Assert.Single(_tree.Marriages);
    }

    // A couple genuinely can remarry each other, so the start date is part of
    // what makes a marriage the same marriage.
    [Fact]
    public async Task KeepsARemarriageBetweenTheSameTwoPeople()
    {
        var ada = Guid.Parse(AdaId);
        var grace = Guid.Parse(GraceId);
        _tree.With(Stored(AdaId, "Ada", "Lovelace"), Stored(GraceId, "Grace", "Hopper"))
            .With(new Marriage(ada, grace, PartialDate.FromYear(1835)));

        await CreateService().ImportAsync(File($$"""
            "marriages": [{"id": "{{LinkId}}", "spouse1Id": "{{AdaId}}", "spouse2Id": "{{GraceId}}",
                           "startDate": {"year": 1849, "isApproximate": false} }]
            """), ImportConflictResolution.Skip);

        Assert.Equal(2, _tree.Marriages.Count);
    }

    // The counts are what a user reads to confirm a restore worked, on a page
    // whose entire purpose is proving the backup came back intact. A summary
    // claiming a relationship was added when it was dropped undercuts that,
    // even though the tree itself is written correctly.
    [Fact]
    public async Task DoesNotCountADroppedOrphanLinkAsAdded()
    {
        var result = await CreateService().ImportAsync(File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"}],
            "biologicalLinks": [{"id": "{{LinkId}}", "parentId": "{{GraceId}}", "childId": "{{AdaId}}"}]
            """), ImportConflictResolution.Overwrite);

        Assert.Empty(_tree.BiologicalLinks);
        Assert.Equal(0, result.Value!.RelationshipsAdded);
        Assert.Equal(1, result.Value!.PeopleAdded);
    }

    // Two browsers each recording the same parent and child independently, then
    // merged. Different ids, same meaning — realistic for this app rather than
    // hypothetical.
    [Fact]
    public async Task DoesNotCountADroppedDuplicateLinkAsAdded()
    {
        var ada = Guid.Parse(AdaId);
        var grace = Guid.Parse(GraceId);
        _tree.With(Stored(AdaId, "Ada", "Lovelace"), Stored(GraceId, "Grace", "Hopper"))
            .With(new BiologicalParentChild(grace, ada));

        var result = await CreateService().ImportAsync(File($$"""
            "biologicalLinks": [{"id": "{{LinkId}}", "parentId": "{{GraceId}}", "childId": "{{AdaId}}"}]
            """), ImportConflictResolution.Skip);

        Assert.Single(_tree.BiologicalLinks);
        Assert.Equal(0, result.Value!.RelationshipsAdded);
        Assert.Equal(1, result.Value!.RelationshipsSkipped);
    }

    // The summary a user actually reads. "2 added" for one stored record is the
    // shape of the bug, so the sentence is asserted rather than just the fields.
    [Fact]
    public async Task DescribesOnlyWhatWasActuallyStored()
    {
        var result = await CreateService().ImportAsync(File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"}],
            "biologicalLinks": [{"id": "{{LinkId}}", "parentId": "{{GraceId}}", "childId": "{{AdaId}}"}]
            """), ImportConflictResolution.Overwrite);

        Assert.Equal("1 added, 1 rejected.", result.Value!.Describe());
    }

    // Counting an update as an addition would overstate the restore in the other
    // direction, so the distinction is pinned alongside the fix.
    [Fact]
    public async Task CountsAReplacedRecordAsUpdatedRatherThanAdded()
    {
        _tree.With(Stored(AdaId, "Ada", "Lovelace"), Stored(GraceId, "Grace", "Hopper"));

        var result = await CreateService().ImportAsync(File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Augusta", "lastName": "King"}]
            """), ImportConflictResolution.Merge);

        Assert.Equal(0, result.Value!.PeopleAdded);
        Assert.Equal(1, result.Value!.PeopleUpdated);
        Assert.Equal(0, result.Value!.PeopleRemoved);
    }

    // ---- Round trip ----

    // The property the whole PR exists for: what comes out goes back in
    // unchanged. Asserted through both services rather than on a fixture, so a
    // change to either format or parser that breaks the pair is caught here.
    [Fact]
    public async Task ExportedTreeReimportsIdentically()
    {
        var ada = Person.Rehydrate(
            Guid.Parse(AdaId), "Ada", "Lovelace", "Byron",
            PartialDate.FromYearMonthDay(1815, 12, 10), "London",
            PartialDate.FromYear(1852, isApproximate: true), "Marylebone",
            Gender.Female, null, "Notes survive too.", isPhantom: false);
        var grace = Stored(GraceId, "Grace", "Hopper");

        _tree.With(ada, grace)
            .With(new BiologicalParentChild(grace.Id, ada.Id, RelationshipCertainty.Likely))
            .With(new AdoptiveParentChild(grace.Id, ada.Id, PartialDate.FromYearMonth(1820, 6)))
            .With(new Marriage(ada.Id, grace.Id, PartialDate.FromYear(1835), "London"));

        var exported = await new ExportService(
            _tree.PersonRepository, _tree.BiologicalRepository, _tree.AdoptiveRepository,
            _tree.MarriageRepository, new FixedClock(DateTimeOffset.UtcNow)).ExportToJsonAsync();

        var restored = new InMemoryTree();
        var import = new ImportService(
            restored.PersonRepository, restored.BiologicalRepository, restored.AdoptiveRepository,
            restored.MarriageRepository, restored.Administration);

        var result = await import.ImportAsync(exported.Value!.Json, ImportConflictResolution.Overwrite);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value!.RecordsRejected);
        Assert.Empty(result.Value!.Warnings);

        var reloaded = restored.People.Single(p => p.Id == ada.Id);
        Assert.Equal("Ada", reloaded.FirstName);
        Assert.Equal("Byron", reloaded.BirthSurname);
        Assert.Equal(PartialDate.FromYearMonthDay(1815, 12, 10), reloaded.BirthDate);
        Assert.Equal(PartialDate.FromYear(1852, isApproximate: true), reloaded.DeathDate);
        Assert.Equal("Marylebone", reloaded.DeathPlace);
        Assert.Equal(Gender.Female, reloaded.Gender);
        Assert.Equal("Notes survive too.", reloaded.Notes);

        Assert.Equal(RelationshipCertainty.Likely, restored.BiologicalLinks.Single().Certainty);
        Assert.Equal(PartialDate.FromYearMonth(1820, 6), restored.AdoptiveLinks.Single().AdoptionDate);
        Assert.Equal("London", restored.Marriages.Single().StartPlace);
    }

    // ---- Reporting ----

    // A file that is wrong in one way is usually wrong in that way a thousand
    // times, and a thousand identical lines is a wall rather than a report.
    [Fact]
    public async Task CapsTheWarningListButSaysHowMuchItLeftOut()
    {
        var bad = string.Join(",", Enumerable.Range(0, 40)
            .Select(i => $$"""{"id": "{{Guid.NewGuid()}}", "firstName": "", "lastName": ""}"""));

        var result = await CreateService().ImportAsync(
            File($$""" "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"}, {{bad}}] """),
            ImportConflictResolution.Overwrite);

        Assert.Equal(40, result.Value!.RecordsRejected);
        Assert.True(result.Value!.Warnings.Count <= 21, "The warning list was not capped.");
        Assert.Contains(result.Value!.Warnings, w => w.Contains("more problems"));
    }

    [Fact]
    public async Task RefusesAFileWhereNothingSurvivedParsing()
    {
        var result = await CreateService().ImportAsync(File("""
            "people": [{"id": "11111111-1111-1111-1111-111111111111", "firstName": "", "lastName": ""}]
            """), ImportConflictResolution.Overwrite);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, _tree.ReplaceCount);
    }

    // ---- Unidentified ancestors (ADR-007, closed in PR 7) ----

    // The round trip the export used to lose. A relationship to an unidentified
    // ancestor is research, and it was disappearing between a backup and its
    // restore because the file had nowhere to put the placeholder.
    [Fact]
    public async Task RestoresAPhantomAndTheLinkThatNamesIt()
    {
        var json = File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"}],
            "phantoms": [{"id": "{{PhantomId}}"}],
            "biologicalLinks": [{"id": "{{LinkId}}", "parentId": "{{PhantomId}}", "childId": "{{AdaId}}"}]
            """);

        var result = await CreateService().ImportAsync(json, ImportConflictResolution.Overwrite);

        Assert.True(result.IsSuccess);
        Assert.Single(_tree.BiologicalLinks);
        Assert.Contains(_tree.People, p => p.Id == Guid.Parse(PhantomId) && p.IsPhantom);
    }

    [Fact]
    public async Task RestoresAPhantomWithNoNameAndNoDates()
    {
        var json = File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"}],
            "phantoms": [{"id": "{{PhantomId}}"}]
            """);

        await CreateService().ImportAsync(json, ImportConflictResolution.Overwrite);

        var phantom = _tree.People.Single(p => p.IsPhantom);
        Assert.Equal(string.Empty, phantom.FirstName);
        Assert.Equal(string.Empty, phantom.LastName);
        Assert.Null(phantom.BirthDate);
    }

    // Counted apart from people, so the preview's "1 person" matches what the
    // app will show afterwards rather than the number of rows written.
    [Fact]
    public void CountsPhantomsApartFromPeopleInThePreview()
    {
        var json = File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"}],
            "phantoms": [{"id": "{{PhantomId}}"}]
            """);

        var result = CreateService().Preview(json);

        Assert.Equal(1, result.Value!.People);
        Assert.Equal(1, result.Value!.Phantoms);
    }

    // A version 1 file predates the section. It is not an error and not a
    // migration — it simply has no placeholders, exactly as when it was written.
    [Fact]
    public async Task ReadsAVersionOneFileThatHasNoPhantomsSection()
    {
        var json = $$"""
            {"schemaVersion": 1, "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"}]}
            """;

        var result = await CreateService().ImportAsync(json, ImportConflictResolution.Overwrite);

        Assert.True(result.IsSuccess);
        Assert.Single(_tree.People);
        Assert.DoesNotContain(_tree.People, p => p.IsPhantom);
    }

    // Under Overwrite the final set is the file's set. Leaving phantoms out of
    // the merge would delete every placeholder the file carried and drop the
    // links naming them as orphans — the loss the section exists to stop.
    [Fact]
    public async Task OverwriteKeepsThePhantomsTheFileCarries()
    {
        _tree.With(Stored(GraceId, "Grace", "Hopper"));

        var json = File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"}],
            "phantoms": [{"id": "{{PhantomId}}"}],
            "biologicalLinks": [{"id": "{{LinkId}}", "parentId": "{{PhantomId}}", "childId": "{{AdaId}}"}]
            """);

        await CreateService().ImportAsync(json, ImportConflictResolution.Overwrite);

        Assert.DoesNotContain(_tree.People, p => p.Id == Guid.Parse(GraceId));
        Assert.Contains(_tree.People, p => p.IsPhantom);
        Assert.Single(_tree.BiologicalLinks);
    }

    // Skip leaves the tree alone, so a phantom already stored survives an import
    // that does not mention it.
    [Fact]
    public async Task SkipLeavesAStoredPhantomInPlace()
    {
        _tree.With(Person.CreatePhantom());

        await CreateService().ImportAsync(OnePerson(), ImportConflictResolution.Skip);

        Assert.Contains(_tree.People, p => p.IsPhantom);
    }

    // One id cannot name both a person and a placeholder: the tree would hold
    // two records for one node and the links could not say which they meant.
    [Fact]
    public void RejectsAPhantomThatReusesAPersonId()
    {
        var json = File($$"""
            "people": [{"id": "{{AdaId}}", "firstName": "Ada", "lastName": "Lovelace"}],
            "phantoms": [{"id": "{{AdaId}}"}]
            """);

        var result = CreateService().Preview(json);

        Assert.Equal(1, result.Value!.RecordsRejected);
        Assert.Equal(0, result.Value!.Phantoms);
    }

    // A file of nothing but empty slots restores nothing anybody can read, and
    // importing it over a real tree would replace it with a set of blanks.
    [Fact]
    public void RefusesAFileOfNothingButPhantoms()
    {
        var result = CreateService().Preview(File($$""" "phantoms": [{"id": "{{PhantomId}}"}] """));

        Assert.False(result.IsSuccess);
        Assert.Contains("nothing to import", result.Error);
    }
}
