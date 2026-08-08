using FamilyTree.UI.Shared;

namespace FamilyTree.UI.Tests.Shared;

public class FileSizeMessageTests
{
    private const long Megabyte = 1024 * 1024;

    // The bug this exists for: integer division made the message contradict
    // itself, reporting the offending file at exactly the limit it exceeded.
    [Fact]
    public void DistinguishesAFileFromTheLimitItExceeds()
    {
        var message = FileSizeMessage.TooLarge((long)(64.5 * Megabyte), 64 * Megabyte);

        Assert.Contains("64.5 MB", message);
        Assert.Contains("larger than the 64 MB", message);
    }

    [Fact]
    public void DoesNotDecorateAWholeNumberOfMegabytes()
    {
        var message = FileSizeMessage.TooLarge(70 * Megabyte, 64 * Megabyte);

        Assert.Contains("70 MB", message);
        Assert.DoesNotContain("70.0 MB", message);
    }

    // Cannot happen through the guard itself, but a rounding rule that turns a
    // real file into "0 MB" would be worse than the bug it replaced.
    [Fact]
    public void DoesNotRoundASmallFileAwayToNothing()
    {
        var message = FileSizeMessage.TooLarge(400 * 1024, 64 * Megabyte);

        Assert.Contains("0.4 MB", message);
    }
}
