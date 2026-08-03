namespace FamilyTree.E2E.Tests;

/// <summary>
/// First-load payload is the central risk ADR-004 accepts: the whole framework
/// ships to the browser before anything renders, and it only grows as features
/// land. This turns that risk into a guarded number rather than something a
/// human has to remember to eyeball on a phone.
/// </summary>
[Collection(nameof(StaticSiteCollection))]
public class PayloadBudgetTests(StaticSiteFixture fixture)
{
    // Headroom over the current figure, not a target to grow into. Raising it
    // should be a deliberate decision recorded in the PR that raises it.
    private const long BudgetBytes = 3_500_000;

    [Fact]
    public void CompressedFirstLoad_StaysWithinBudget()
    {
        var framework = Path.Combine(fixture.PublishedRoot, "_framework");
        Assert.True(Directory.Exists(framework), $"No published _framework at {framework}");

        // Brotli is what a browser negotiates for these assets, so it is the
        // number that matters rather than the size on disk.
        var compressed = Directory
            .EnumerateFiles(framework, "*.br", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);

        Assert.True(
            compressed > 0,
            "Found no .br assets — publish compression changed, so this budget is no longer measuring anything.");

        Assert.True(
            compressed <= BudgetBytes,
            $"Compressed first-load payload is {compressed / 1_000_000.0:F2} MB, over the " +
            $"{BudgetBytes / 1_000_000.0:F2} MB budget. Either trim it or raise the budget deliberately.");
    }
}
