using Bunit;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;
using FamilyTree.UI.Pages.People;
using Moq;

namespace FamilyTree.UI.Tests.Pages;

/// <summary>
/// The marriage and step sections of a profile: US-022, US-024, US-025, US-038,
/// US-042 and US-044.
/// </summary>
/// <remarks>
/// Markup and behaviour only. What these rows look like — the dotted step chip,
/// the "Current" badge, whether any of it fits at 390px — has no CSS engine here
/// and belongs in FamilyTree.E2E.Tests.
/// </remarks>
public class PersonProfileMarriageTests : ShellTestContext
{
    private readonly List<Person> _people = [];
    private readonly List<BiologicalParentChild> _links = [];
    private readonly List<Marriage> _marriages = [];
    private readonly List<StepparentRelationship> _steps = [];

    private Person Someone(string first, string last = "Whitfield", int? birthYear = null, int? deathYear = null)
    {
        var person = new Person(first, last, Gender.Unknown);
        if (birthYear is not null || deathYear is not null)
        {
            person.UpdateDates(
                birthYear is int b ? PartialDate.FromYear(b) : null,
                null,
                deathYear is int d ? PartialDate.FromYear(d) : null,
                null);
        }

        _people.Add(person);
        return person;
    }

    private void Link(Person parent, Person child) =>
        _links.Add(new BiologicalParentChild(parent.Id, child.Id));

    private Marriage Married(
        Person one, Person two, int startYear, string? place = null,
        int? endYear = null, MarriageEndReason? reason = null)
    {
        var marriage = new Marriage(one.Id, two.Id, PartialDate.FromYear(startYear), place);
        if (endYear is not null || reason is not null)
        {
            marriage.UpdateDates(
                PartialDate.FromYear(startYear),
                place,
                endYear is int year ? PartialDate.FromYear(year) : null,
                reason);
        }

        _marriages.Add(marriage);
        return marriage;
    }

    private StepparentRelationship Label(Person stepparent, Person stepchild, Marriage marriage)
    {
        var link = new StepparentRelationship(stepparent.Id, stepchild.Id, marriage.Id);
        _steps.Add(link);
        return link;
    }

    private IRenderedComponent<PersonProfilePage> RenderProfile(Person subject)
    {
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_people);
        People.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _people.FirstOrDefault(p => p.Id == id));
        People.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                _people.Where(p => ids.Contains(p.Id)).ToList());

        Biological.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_links);
        Biological.Setup(r => r.GetParentLinksForChildAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _links.Where(l => l.ChildId == id).ToList());
        Biological.Setup(r => r.GetChildLinksForParentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _links.Where(l => l.ParentId == id).ToList());
        Biological.Setup(r => r.GetParentLinksForChildrenAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                _links.Where(l => ids.Contains(l.ChildId)).ToList());

        Marriages.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_marriages);
        Marriages.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _marriages.FirstOrDefault(m => m.Id == id));
        Marriages.Setup(r => r.GetForPersonAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                _marriages.Where(m => m.Spouse1Id == id || m.Spouse2Id == id).ToList());
        Marriages.Setup(r => r.GetForPeopleAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                _marriages.Where(m => ids.Contains(m.Spouse1Id) || ids.Contains(m.Spouse2Id)).ToList());
        Marriages.Setup(r => r.GetByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                _marriages.Where(m => ids.Contains(m.Id)).ToList());
        // Writes go through the same lists, so a row removed by a click is a row
        // gone from the next render rather than one the mock still returns.
        Marriages.Setup(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
            {
                _marriages.RemoveAll(m => m.Id == id);
                return _steps.RemoveAll(l => l.MarriageId == id);
            });

        Stepparents.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_steps);
        Stepparents.Setup(r => r.GetForStepchildAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                _steps.Where(l => l.StepchildId == id).ToList());
        Stepparents.Setup(r => r.GetForStepparentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                _steps.Where(l => l.StepparentId == id).ToList());
        Stepparents.Setup(r => r.AddAsync(
                It.IsAny<StepparentRelationship>(), It.IsAny<CancellationToken>()))
            .Returns((StepparentRelationship link, CancellationToken _) =>
            {
                _steps.Add(link);
                return Task.CompletedTask;
            });
        Stepparents.Setup(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _steps.RemoveAll(l => l.Id == id) > 0);

        return Render<PersonProfilePage>(p => p.Add(c => c.Id, subject.Id));
    }

    /// <summary>
    /// Arthur was widowed and remarried; Daniel is his son and Vera's stepchild.
    /// </summary>
    private (Person Arthur, Person Margaret, Person Vera, Person Daniel, Marriage First, Marriage Second)
        SeedBlendedFamily()
    {
        var arthur = Someone("Arthur", birthYear: 1918);
        var margaret = Someone("Margaret", birthYear: 1921, deathYear: 1973);
        var vera = Someone("Vera", birthYear: 1930);
        var daniel = Someone("Daniel", birthYear: 1963);

        Link(arthur, daniel);

        var first = Married(arthur, margaret, 1946, "Leeds", 1973, MarriageEndReason.DeathOfSpouse);
        var second = Married(arthur, vera, 1976, "Harrogate");

        return (arthur, margaret, vera, daniel, first, second);
    }

    // ---- Marriages (US-022, US-042) ----

    [Fact]
    public void SaysSoWhenThereAreNoMarriages()
    {
        var arthur = Someone("Arthur");

        var cut = RenderProfile(arthur);

        Assert.NotNull(cut.Find("[data-testid='marriages-empty']"));
    }

    [Fact]
    public void ListsEveryMarriage()
    {
        var (arthur, _, _, _, first, second) = SeedBlendedFamily();

        var cut = RenderProfile(arthur);

        Assert.NotNull(cut.Find($"[data-testid='marriage-{first.Id}']"));
        Assert.NotNull(cut.Find($"[data-testid='marriage-{second.Id}']"));
    }

    // US-042: chronological, so a marital history reads in the order it happened.
    [Fact]
    public void OrdersMarriagesChronologically()
    {
        var (arthur, _, _, _, first, second) = SeedBlendedFamily();

        var cut = RenderProfile(arthur);
        var rows = cut.FindAll("[data-testid='marriages-list'] > li")
            .Select(r => r.GetAttribute("data-testid"))
            .ToList();

        Assert.Equal([$"marriage-{first.Id}", $"marriage-{second.Id}"], rows);
    }

    // US-022 asks for each entry to show the spouse, the dates, the end date or
    // "Ongoing", and the reason. The DTO words it; this is that it reaches the row.
    [Fact]
    public void ShowsTheDatesAndHowItEnded()
    {
        var (arthur, _, _, _, first, _) = SeedBlendedFamily();

        var cut = RenderProfile(arthur);

        Assert.Equal(
            "1946 – 1973 (Widowed)",
            cut.Find($"[data-testid='marriage-dates-{first.Id}']").TextContent.Trim());
    }

    [Fact]
    public void SaysOngoingForACurrentMarriage()
    {
        var (arthur, _, _, _, _, second) = SeedBlendedFamily();

        var cut = RenderProfile(arthur);

        Assert.Equal(
            "1976 – Ongoing",
            cut.Find($"[data-testid='marriage-dates-{second.Id}']").TextContent.Trim());
    }

    // US-022's fourth criterion. The badge, not a colour: the chip's own colour
    // already means "marriage".
    [Fact]
    public void MarksTheCurrentMarriage()
    {
        var (arthur, _, _, _, first, second) = SeedBlendedFamily();

        var cut = RenderProfile(arthur);

        Assert.NotNull(cut.Find($"[data-testid='marriage-ongoing-{second.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='marriage-ongoing-{first.Id}']"));
    }

    [Fact]
    public void ShowsThePlaceWhenOneWasRecorded()
    {
        var (arthur, _, _, _, first, _) = SeedBlendedFamily();

        var cut = RenderProfile(arthur);

        Assert.Equal("Leeds", cut.Find($"[data-testid='marriage-place-{first.Id}']").TextContent.Trim());
    }

    // US-022: the spouse's name links to their profile, which is how the section
    // is navigated.
    [Fact]
    public void LinksToTheSpouse()
    {
        var (arthur, margaret, _, _, first, _) = SeedBlendedFamily();

        var cut = RenderProfile(arthur);

        Assert.Equal(
            $"people/{margaret.Id}",
            cut.Find($"[data-testid='marriage-chip-{first.Id}']").GetAttribute("href"));
    }

    // One record, two profiles. The row on each shows the other person.
    [Fact]
    public void ShowsTheOtherSpouseOnEachProfile()
    {
        var (arthur, margaret, _, _, first, _) = SeedBlendedFamily();

        var hers = RenderProfile(margaret);

        Assert.Equal(
            $"people/{arthur.Id}",
            hers.Find($"[data-testid='marriage-chip-{first.Id}']").GetAttribute("href"));
    }

    // ---- Removing a marriage (US-024) ----

    [Fact]
    public async Task RemovingAMarriageAsksFirst()
    {
        var (arthur, _, _, _, _, second) = SeedBlendedFamily();
        var cut = RenderProfile(arthur);

        await cut.Find($"[data-testid='remove-marriage-{second.Id}']").ClickAsync(new());

        Assert.NotNull(cut.Find("[data-testid='remove-marriage-modal']"));
    }

    // US-024 is explicit that neither person is deleted, and the confirmation is
    // where that has to be said — "Remove" next to a name reads as "Delete" to
    // somebody moving quickly.
    [Fact]
    public async Task TheConfirmationSaysNeitherPersonIsDeleted()
    {
        var (arthur, _, _, _, _, second) = SeedBlendedFamily();
        var cut = RenderProfile(arthur);

        await cut.Find($"[data-testid='remove-marriage-{second.Id}']").ClickAsync(new());

        Assert.Contains("is deleted", cut.Find("[data-testid='remove-marriage-modal']").TextContent);
        Assert.Contains("stepparent labels", cut.Find("[data-testid='remove-marriage-modal']").TextContent);
    }

    [Fact]
    public async Task RemovesTheMarriageOnConfirmation()
    {
        var (arthur, _, _, _, _, second) = SeedBlendedFamily();
        var cut = RenderProfile(arthur);

        await cut.Find($"[data-testid='remove-marriage-{second.Id}']").ClickAsync(new());
        await cut.Find("[data-testid='confirm-remove-marriage']").ClickAsync(new());

        Assert.Empty(cut.FindAll($"[data-testid='marriage-{second.Id}']"));
        Assert.DoesNotContain(second, _marriages);
    }

    // ---- Stepparents and stepchildren (US-038) ----

    [Fact]
    public void SaysSoWhenThereAreNoStepparents()
    {
        var arthur = Someone("Arthur");

        var cut = RenderProfile(arthur);

        Assert.NotNull(cut.Find("[data-testid='stepparents-empty']"));
        Assert.NotNull(cut.Find("[data-testid='stepchildren-empty']"));
    }

    // US-038's first criterion: the action sits next to a parent's current spouse,
    // rather than behind a search of the whole tree.
    [Fact]
    public void OffersAParentsCurrentSpouseForLabelling()
    {
        var (_, _, vera, daniel, _, _) = SeedBlendedFamily();

        var cut = RenderProfile(daniel);

        Assert.NotNull(cut.Find($"[data-testid='stepparent-candidate-{vera.Id}']"));
        Assert.NotNull(cut.Find($"[data-testid='label-stepparent-{vera.Id}']"));
    }

    // US-038's second criterion, said out loud on the page: the app is not going to
    // infer this, and the note is where that is explained.
    [Fact]
    public void SaysTheLabelIsNotAssumed()
    {
        var (_, _, _, daniel, _, _) = SeedBlendedFamily();

        var cut = RenderProfile(daniel);

        Assert.Contains(
            "nothing is assumed",
            cut.Find("[data-testid='stepparent-candidates-note']").TextContent);
    }

    [Fact]
    public async Task LabellingACandidateRecordsTheStepRelationship()
    {
        var (_, _, vera, daniel, _, second) = SeedBlendedFamily();
        var cut = RenderProfile(daniel);

        await cut.Find($"[data-testid='label-stepparent-{vera.Id}']").ClickAsync(new());

        var link = Assert.Single(_steps);
        Assert.Equal(vera.Id, link.StepparentId);
        Assert.Equal(daniel.Id, link.StepchildId);
        Assert.Equal(second.Id, link.MarriageId);
        Assert.NotNull(cut.Find($"[data-testid='stepparent-{link.Id}']"));
    }

    [Fact]
    public async Task ACandidateStopsBeingOfferedOnceLabelled()
    {
        var (_, _, vera, daniel, _, _) = SeedBlendedFamily();
        var cut = RenderProfile(daniel);

        await cut.Find($"[data-testid='label-stepparent-{vera.Id}']").ClickAsync(new());

        Assert.Empty(cut.FindAll($"[data-testid='stepparent-candidate-{vera.Id}']"));
    }

    // The row says which parent the relationship runs through. With two marriages
    // in play it is the only thing distinguishing two rows.
    [Fact]
    public void AStepparentRowNamesTheParentItRunsThrough()
    {
        var (_, _, vera, daniel, _, second) = SeedBlendedFamily();
        var label = Label(vera, daniel, second);

        var cut = RenderProfile(daniel);

        Assert.Contains(
            "Arthur Whitfield",
            cut.Find($"[data-testid='stepparent-via-{label.Id}']").TextContent);
    }

    // US-038's fourth criterion: the same record, from the stepparent's side.
    [Fact]
    public void ShowsTheStepchildOnTheStepparentsProfile()
    {
        var (_, _, vera, daniel, _, second) = SeedBlendedFamily();
        var label = Label(vera, daniel, second);

        var cut = RenderProfile(vera);

        Assert.NotNull(cut.Find($"[data-testid='stepchild-{label.Id}']"));
        Assert.Contains("Daniel", cut.Find($"[data-testid='stepchild-chip-{label.Id}']").TextContent);
    }

    // US-038's third criterion. A stepparent is not a parent, and the profile has
    // to keep them apart rather than merge them into one "parents" list.
    [Fact]
    public void AStepparentDoesNotAppearInTheBiologicalParentsSection()
    {
        var (_, _, vera, daniel, _, second) = SeedBlendedFamily();
        Label(vera, daniel, second);

        var cut = RenderProfile(daniel);

        Assert.Empty(cut.FindAll($"[data-testid='parent-{vera.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='adoptive-parent-{vera.Id}']"));
    }

    [Fact]
    public async Task RemovingAStepLabelSaysTheMarriageStays()
    {
        var (_, _, vera, daniel, _, second) = SeedBlendedFamily();
        var label = Label(vera, daniel, second);
        var cut = RenderProfile(daniel);

        await cut.Find($"[data-testid='remove-stepparent-{label.Id}']").ClickAsync(new());

        Assert.Contains(
            "marriage it rests on stays recorded",
            cut.Find("[data-testid='remove-step-modal']").TextContent);
    }

    [Fact]
    public async Task RemovesTheStepLabelOnConfirmation()
    {
        var (_, _, vera, daniel, _, second) = SeedBlendedFamily();
        var label = Label(vera, daniel, second);
        var cut = RenderProfile(daniel);

        await cut.Find($"[data-testid='remove-stepparent-{label.Id}']").ClickAsync(new());
        await cut.Find("[data-testid='confirm-remove-step']").ClickAsync(new());

        Assert.Empty(_steps);
        Assert.Single(_marriages, m => m.Id == second.Id);
    }

    // US-038's last criterion, through the UI: removing the marriage record takes
    // the label with it, and the child's profile stops showing it.
    [Fact]
    public async Task RemovingTheMarriageRemovesTheStepLabel()
    {
        var (arthur, _, vera, daniel, _, second) = SeedBlendedFamily();
        var label = Label(vera, daniel, second);

        var his = RenderProfile(arthur);
        await his.Find($"[data-testid='remove-marriage-{second.Id}']").ClickAsync(new());
        await his.Find("[data-testid='confirm-remove-marriage']").ClickAsync(new());

        Assert.Empty(_steps);

        var childs = RenderProfile(daniel);
        Assert.Empty(childs.FindAll($"[data-testid='stepparent-{label.Id}']"));
    }
}
