using Bunit;
using FamilyTree.UI.Shared;

namespace FamilyTree.UI.Tests.Pages;

/// <summary>
/// PersonChip has no caller until relationships land in PRs 7–9. It is covered
/// here anyway: an unused component with no tests is the kind that quietly rots
/// before the PR that finally needs it.
/// </summary>
public class PersonChipTests : ShellTestContext
{
    private IRenderedComponent<PersonChip> RenderChip(
        PersonChip.ChipVariant variant, string? name = "Ada Lovelace", string? href = null) =>
        Render<PersonChip>(p => p
            .Add(c => c.Variant, variant)
            .Add(c => c.Name, name)
            .Add(c => c.Href, href));

    [Fact]
    public void RendersAsPlainTextWhenThereIsNowhereToGo()
    {
        var cut = RenderChip(PersonChip.ChipVariant.Biological);

        Assert.Empty(cut.FindAll("a"));
        Assert.Contains("Ada Lovelace", cut.Markup);
    }

    [Fact]
    public void RendersAsALinkWhenGivenAnHref()
    {
        var cut = RenderChip(PersonChip.ChipVariant.Biological, href: "people/1");

        Assert.Equal("people/1", cut.Find("a").GetAttribute("href"));
    }

    [Theory]
    [InlineData(PersonChip.ChipVariant.Biological, "chip--biological")]
    [InlineData(PersonChip.ChipVariant.Adoptive, "chip--adoptive")]
    [InlineData(PersonChip.ChipVariant.Marriage, "chip--marriage")]
    [InlineData(PersonChip.ChipVariant.Phantom, "chip--phantom")]
    public void CarriesTheVariantClass(PersonChip.ChipVariant variant, string expected)
    {
        var cut = RenderChip(variant);

        Assert.Contains(expected, cut.Find(".chip").GetAttribute("class"));
    }

    // Colour alone cannot carry the distinction, so an adoptive link is also
    // marked in text.
    [Fact]
    public void MarksAnAdoptiveLinkInTextAsWellAsStyle()
    {
        var cut = RenderChip(PersonChip.ChipVariant.Adoptive);

        Assert.Contains("(A)", cut.Markup);
    }

    [Fact]
    public void DoesNotMarkANonAdoptiveLink()
    {
        var cut = RenderChip(PersonChip.ChipVariant.Biological);

        Assert.DoesNotContain("(A)", cut.Markup);
    }

    // A phantom is an ancestor known to exist but not identified, so it reads as
    // an empty slot rather than a blank name.
    [Fact]
    public void LabelsAnUnnamedPhantomAsUnknown()
    {
        var cut = RenderChip(PersonChip.ChipVariant.Phantom, name: null);

        Assert.Contains("Unknown", cut.Markup);
    }

    [Fact]
    public void DescribesTheRelationshipKindForAssistiveTechnology()
    {
        var cut = RenderChip(PersonChip.ChipVariant.Marriage);

        Assert.Contains("marriage", cut.Find(".chip").GetAttribute("title")!);
    }
}
