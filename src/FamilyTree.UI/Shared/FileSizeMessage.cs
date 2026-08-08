using System.Globalization;

namespace FamilyTree.UI.Shared;

/// <summary>
/// Wording for the upload size guard.
/// </summary>
/// <remarks>
/// A separate class so it can be tested without allocating a file large enough
/// to trip the limit. Integer megabytes were the bug worth extracting it for: a
/// 64.5 MB file read as "That file is 64 MB, which is larger than the 64 MB this
/// app will read", which reads as a contradiction rather than an explanation.
/// </remarks>
public static class FileSizeMessage
{
    private const double Megabyte = 1024 * 1024;

    public static string TooLarge(long actualBytes, long limitBytes) =>
        $"That file is {Describe(actualBytes)}, which is larger than the {Describe(limitBytes)} "
        + "this app will read. Is it definitely a FamilyTree export?";

    /// <summary>
    /// "64 MB" for a whole number, "64.5 MB" otherwise, and "0.4 MB" below one.
    /// </summary>
    private static string Describe(long bytes)
    {
        var megabytes = bytes / Megabyte;
        var rounded = Math.Round(megabytes, 1);

        return rounded == Math.Floor(rounded)
            ? $"{rounded.ToString("0", CultureInfo.InvariantCulture)} MB"
            : $"{rounded.ToString("0.0", CultureInfo.InvariantCulture)} MB";
    }
}
