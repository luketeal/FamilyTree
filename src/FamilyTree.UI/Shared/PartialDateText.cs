using System.Globalization;
using FamilyTree.Application.Services;
using FamilyTree.Domain.ValueObjects;

namespace FamilyTree.UI.Shared;

/// <summary>
/// What the user has typed into a date control, before it is a date.
/// </summary>
/// <remarks>
/// This is the seed contract for <c>PartialDateInput</c>, deliberately raw text
/// rather than <see cref="PartialDate"/>. A <see cref="PartialDate"/> cannot
/// hold a year outside 1..9999, or a 31st of February, so "the user typed
/// something this type cannot represent" is inexpressible in it — and a caller
/// handed that contract has no choice but to guess. The guess is what loses
/// data: the carry-over from quick add used to report that it had *dropped* an
/// out-of-range year rather than putting it in the box where the user could fix
/// it. Raw text in, parsed on emit, is the same fix that resolved the quick-add
/// <c>int?</c> case.
/// </remarks>
public sealed record PartialDateText(string Year, string Month, string Day, bool IsApproximate)
{
    public static readonly PartialDateText Empty = new(string.Empty, string.Empty, string.Empty, false);

    /// <summary>Round-trips a stored date back into the boxes it came from.</summary>
    public static PartialDateText From(PartialDate? date) => date is null
        ? Empty
        : new PartialDateText(
            date.Year.ToString(CultureInfo.InvariantCulture),
            date.Month?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            date.Day?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            date.IsApproximate);

    /// <summary>
    /// Seeds a year-only control from text of unknown validity — a query string,
    /// typically, which anybody can hand-edit.
    /// </summary>
    public static PartialDateText FromYearText(string? yearText) =>
        Empty with { Year = yearText?.Trim() ?? string.Empty };

    public bool IsBlank =>
        Year.Trim().Length == 0 && Month.Trim().Length == 0 && Day.Trim().Length == 0;

    // Shared with PersonService rather than copied. This rule previously had
    // three UI copies and they drifted, which is how an out-of-range year
    // reached storage.
    private static int MaxYear => PersonService.MaxYear;

    /// <summary>
    /// Converts to the domain value, or explains why it is not one yet.
    /// </summary>
    /// <remarks>
    /// Every factory on <see cref="PartialDate"/> validates eagerly and throws —
    /// including <c>DateTime.DaysInMonth</c> for the day, so 31 February is an
    /// <c>ArgumentOutOfRangeException</c>, and an exception reaching the renderer
    /// is a blank screen. Rather than catch that, each part is checked against
    /// the same rule first, so the factories are only ever called with arguments
    /// they accept and the user gets a sentence instead of a stack trace.
    /// <para>
    /// That makes the year check load-bearing in a way worth naming: it stops at
    /// <see cref="PersonService.MaxYear"/>, which is inside the 1..9999 that
    /// <c>ValidateYear</c> enforces. Widening MaxYear past 9999 would let a year
    /// through here that <see cref="PartialDate.FromYear"/> still rejects, and
    /// the throw this method exists to prevent would be live again.
    /// </para>
    /// </remarks>
    public bool TryToDomain(out PartialDate? date, out string? error)
    {
        date = null;
        error = null;

        // Trimmed once, up front. Reading the raw text for "is this box empty"
        // while parsing the trimmed text made a box holding only spaces report a
        // range error rather than reading as blank — and left this control
        // disagreeing with quick add about the same keystrokes.
        var yearText = Year.Trim();
        var monthText = Month.Trim();
        var dayText = Day.Trim();

        var year = Parse(yearText);
        var month = Parse(monthText);
        var day = Parse(dayText);

        // An empty control is a perfectly good answer — a genealogy record
        // frequently knows no date at all. Only a partly filled one is a problem.
        if (yearText.Length == 0)
        {
            if (monthText.Length == 0 && dayText.Length == 0)
            {
                return true;
            }

            error = "Enter a year before a month.";
            return false;
        }

        if (year is not int y || y < 1 || y > MaxYear)
        {
            error = $"Year must be between 1 and {MaxYear}.";
            return false;
        }

        if (monthText.Length == 0)
        {
            // A day without a month is not a date, and ExportedDate rejects the
            // combination on import — so this control must not be able to
            // produce it. The UI cascade normally prevents it; this is the
            // backstop for anything that sets the fields directly.
            if (dayText.Length > 0)
            {
                error = "Enter a month before a day.";
                return false;
            }

            date = PartialDate.FromYear(y, IsApproximate);
            return true;
        }

        if (month is not int m || m < 1 || m > 12)
        {
            error = "Month must be between 1 and 12.";
            return false;
        }

        if (dayText.Length == 0)
        {
            date = PartialDate.FromYearMonth(y, m, IsApproximate);
            return true;
        }

        var lastDay = DateTime.DaysInMonth(y, m);
        if (day is not int d || d < 1 || d > lastDay)
        {
            error = $"Day must be between 1 and {lastDay} for "
                + $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m)} {y}.";
            return false;
        }

        date = PartialDate.FromYearMonthDay(y, m, d, IsApproximate);
        return true;
    }

    /// <summary>
    /// Null for anything that is not an integer, including values past
    /// <see cref="int.MaxValue"/> — which is a typo, not a blank field, and the
    /// callers above distinguish the two by the length of the text.
    /// </summary>
    private static int? Parse(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
