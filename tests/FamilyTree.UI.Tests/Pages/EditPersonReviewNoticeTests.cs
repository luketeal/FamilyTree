using Bunit;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Entities;
using FamilyTree.Domain.Enums;
using FamilyTree.Domain.ValueObjects;
using FamilyTree.UI.Pages.People;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FamilyTree.UI.Tests.Pages;

/// <summary>
/// US-026's third criterion: editing a death date prompts a review of the
/// marriages it calls into question.
/// </summary>
/// <remarks>
/// The criterion is about a date that was <em>updated</em>, which is this page's
/// business rather than the service's — a service handed one person cannot see
/// what they looked like a moment ago. Without that gate the notice fired after
/// every save of anybody dead with an ongoing marriage, opening "X is now
/// recorded as dying in 1991" after an edit that touched a spelling. Four people
/// in the shipped sample family are in exactly that state.
/// </remarks>
public class EditPersonReviewNoticeTests : ShellTestContext
{
    private readonly List<Person> _people = [];
    private readonly List<Marriage> _marriages = [];

    /// <summary>
    /// Every toast raised during a test, captured at the service rather than from
    /// the rendered region: the region only shows what was raised while it was on
    /// screen, and this asks whether the page said anything at all.
    /// </summary>
    private readonly List<Toast> _raised = [];

    public EditPersonReviewNoticeTests() =>
        Services.GetRequiredService<ToastService>().Raised += _raised.Add;

    private Person Someone(string first, string last, int? deathYear = null)
    {
        var person = new Person(first, last, Gender.Unknown);
        person.UpdateDates(
            PartialDate.FromYear(1918),
            null,
            deathYear is int year ? PartialDate.FromYear(year) : null,
            null);

        _people.Add(person);
        return person;
    }

    private IRenderedComponent<EditPersonPage> RenderEdit(Person subject)
    {
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_people);
        People.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _people.FirstOrDefault(p => p.Id == id));
        People.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                _people.Where(p => ids.Contains(p.Id)).ToList());
        People.Setup(r => r.UpdateAsync(It.IsAny<Person>(), It.IsAny<CancellationToken>()))
            .Returns((Person person, CancellationToken _) =>
            {
                _people.RemoveAll(p => p.Id == person.Id);
                _people.Add(person);
                return Task.CompletedTask;
            });

        Marriages.Setup(r => r.GetForPersonAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                _marriages.Where(m => m.Spouse1Id == id || m.Spouse2Id == id).ToList());

        return Render<EditPersonPage>(p => p.Add(c => c.Id, subject.Id));
    }

    /// <summary>Arthur died in 1991 and his marriage to Vera never ended.</summary>
    private Person SeedWidowerWithAnOngoingMarriage()
    {
        var arthur = Someone("Arthur", "Whitfield", deathYear: 1991);
        var vera = Someone("Vera", "Whitfield");
        _marriages.Add(new Marriage(arthur.Id, vera.Id, PartialDate.FromYear(1976)));
        return arthur;
    }

    // The regression. An edit that leaves the death date alone has nothing to
    // review, and the notice's own first clause — "is now recorded as dying in
    // 1991" — is false when nothing changed.
    [Fact]
    public async Task SaysNothingWhenTheEditDoesNotTouchTheDeathDate()
    {
        var arthur = SeedWidowerWithAnOngoingMarriage();
        var cut = RenderEdit(arthur);

        await cut.Find("[data-testid='input-first-name']").InputAsync(new() { Value = "Arthurr" });
        await cut.Find("[data-testid='save-person']").ClickAsync(new());

        Assert.DoesNotContain("end date reviewed", Toasts());
    }

    [Fact]
    public async Task AsksForAReviewWhenTheDeathDateChanges()
    {
        var arthur = SeedWidowerWithAnOngoingMarriage();
        var cut = RenderEdit(arthur);

        await cut.Find("[data-testid='input-death-year']").InputAsync(new() { Value = "1998" });
        await cut.Find("[data-testid='save-person']").ClickAsync(new());

        Assert.Contains("end date reviewed", Toasts());
    }

    // Clearing a death date is a change too, and the one most likely to mean a
    // marriage end date recorded from it is now wrong.
    [Fact]
    public async Task AsksForAReviewWhenTheDeathDateIsCleared()
    {
        var arthur = SeedWidowerWithAnOngoingMarriage();
        var cut = RenderEdit(arthur);

        await cut.Find("[data-testid='input-death-year']").InputAsync(new() { Value = string.Empty });
        await cut.Find("[data-testid='save-person']").ClickAsync(new());

        // Nothing to review once the date is gone — the service is silent — but the
        // point is that the page asked rather than skipping the question.
        Assert.DoesNotContain("end date reviewed", Toasts());
        Assert.Null(_people.Single(p => p.Id == arthur.Id).DeathDate);
    }

    private string Toasts() => string.Join(" | ", _raised.Select(t => t.Message));
}
