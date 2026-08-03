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
        var root = fixture.PublishedRoot;
        Assert.True(Directory.Exists(root), $"No published output at {root}");

        // Everything the browser fetches, not just the framework: self-hosted
        // fonts and stylesheets are part of first load too. Count the brotli
        // variant where one exists, since that is what gets negotiated, and the
        // raw file otherwise. 404.html is excluded as a duplicate of index.html
        // that no single visit downloads alongside it.
        var assets = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".br", StringComparison.Ordinal))
            .Where(f => !f.EndsWith(".gz", StringComparison.Ordinal))
            .Where(f => Path.GetFileName(f) != "404.html")
            .ToArray();

        Assert.NotEmpty(assets);

        var total = assets.Sum(f =>
        {
            var brotli = new FileInfo(f + ".br");
            return brotli.Exists ? brotli.Length : new FileInfo(f).Length;
        });

        var largest = assets
            .Select(f => (Name: Path.GetRelativePath(root, f),
                          Size: File.Exists(f + ".br") ? new FileInfo(f + ".br").Length : new FileInfo(f).Length))
            .OrderByDescending(a => a.Size)
            .Take(3)
            .Select(a => $"{a.Name} {a.Size / 1000}kB");

        Assert.True(
            total <= BudgetBytes,
            $"First-load payload is {total / 1_000_000.0:F2} MB, over the {BudgetBytes / 1_000_000.0:F2} MB " +
            $"budget. Largest: {string.Join(", ", largest)}. Either trim it or raise the budget deliberately.");
    }
}
