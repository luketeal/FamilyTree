using System.Globalization;

namespace FamilyTree.Domain.ValueObjects;

/// <summary>
/// A date that may be year-only, year+month, or full (year+month+day), with an optional "circa" flag.
/// Stored as three nullable integer columns via EF Core owned-type configuration.
/// </summary>
public sealed class PartialDate : IComparable<PartialDate>, IEquatable<PartialDate>
{
    public int Year { get; }
    public int? Month { get; }
    public int? Day { get; }
    public bool IsApproximate { get; }

    private PartialDate(int year, int? month, int? day, bool isApproximate)
    {
        Year = year;
        Month = month;
        Day = day;
        IsApproximate = isApproximate;
    }

    public static PartialDate FromYear(int year, bool isApproximate = false)
    {
        ValidateYear(year);
        return new PartialDate(year, null, null, isApproximate);
    }

    public static PartialDate FromYearMonth(int year, int month, bool isApproximate = false)
    {
        ValidateYear(year);
        ValidateMonth(month);
        return new PartialDate(year, month, null, isApproximate);
    }

    public static PartialDate FromYearMonthDay(int year, int month, int day, bool isApproximate = false)
    {
        ValidateYear(year);
        ValidateMonth(month);
        ValidateDay(year, month, day);
        return new PartialDate(year, month, day, isApproximate);
    }

    /// <summary>
    /// Formats the date for display. Examples: "c. 1920", "March 1920", "15 March 1920".
    /// </summary>
    public override string ToString()
    {
        var prefix = IsApproximate ? "c. " : string.Empty;

        if (Day.HasValue)
        {
            var d = new DateOnly(Year, Month!.Value, Day.Value);
            return prefix + d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        }

        if (Month.HasValue)
        {
            var d = new DateOnly(Year, Month.Value, 1);
            return prefix + d.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        }

        return prefix + Year.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Sorts by year, then month (year-only sorts before any month), then day (month-only sorts before any day).
    /// IsApproximate is ignored for ordering.
    /// </summary>
    public int CompareTo(PartialDate? other)
    {
        if (other is null) return 1;

        var cmp = Year.CompareTo(other.Year);
        if (cmp != 0) return cmp;

        if (Month is null && other.Month is null) return 0;
        if (Month is null) return -1;
        if (other.Month is null) return 1;

        cmp = Month.Value.CompareTo(other.Month.Value);
        if (cmp != 0) return cmp;

        if (Day is null && other.Day is null) return 0;
        if (Day is null) return -1;
        if (other.Day is null) return 1;

        return Day.Value.CompareTo(other.Day.Value);
    }

    /// <summary>
    /// Whether this date is <em>known</em> to fall before <paramref name="other"/>,
    /// judged only at the precision both dates actually carry.
    /// </summary>
    /// <remarks>
    /// This is the question to ask of two partial dates when the answer decides
    /// whether a record is contradictory — "did this end before it began?" — and
    /// <see cref="CompareTo"/> is not it. <see cref="CompareTo"/> is a total sort
    /// order, so it has to put "June 1920" somewhere relative to "15 June 1920"
    /// and puts it first; correct for a list, and wrong as evidence, because a
    /// date that never named a day says nothing about which day it was.
    /// <para>
    /// So a field is consulted only when both dates name it, and a blank on
    /// either side ends the comparison as "not known to be before". Same-year and
    /// same-month pairs at differing precision are therefore ordinary rather than
    /// backwards, which is what the records people actually hold look like.
    /// </para>
    /// </remarks>
    public bool IsKnownToPrecede(PartialDate other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Year != other.Year)
        {
            return Year < other.Year;
        }

        if (Month is not int month || other.Month is not int otherMonth)
        {
            return false;
        }

        if (month != otherMonth)
        {
            return month < otherMonth;
        }

        if (Day is not int day || other.Day is not int otherDay)
        {
            return false;
        }

        return day < otherDay;
    }

    public bool Equals(PartialDate? other)
    {
        if (other is null) return false;
        return Year == other.Year
            && Month == other.Month
            && Day == other.Day
            && IsApproximate == other.IsApproximate;
    }

    public override bool Equals(object? obj) => obj is PartialDate other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Year, Month, Day, IsApproximate);

    private static void ValidateYear(int year)
    {
        if (year < 1 || year > 9999)
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be between 1 and 9999.");
    }

    private static void ValidateMonth(int month)
    {
        if (month < 1 || month > 12)
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");
    }

    private static void ValidateDay(int year, int month, int day)
    {
        var maxDay = DateTime.DaysInMonth(year, month);
        if (day < 1 || day > maxDay)
            throw new ArgumentOutOfRangeException(nameof(day), day,
                $"Day must be between 1 and {maxDay} for {year}-{month:D2}.");
    }
}
