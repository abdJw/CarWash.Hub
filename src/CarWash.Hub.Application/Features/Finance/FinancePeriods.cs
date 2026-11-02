namespace CarWash.Hub.Application.Features.Finance;

/// <summary>Cuts a range of business days into the finance report's buckets.</summary>
/// <remarks>Layer: Application. Pure: dates in, dates out.</remarks>
public static class FinancePeriods
{
    /// <summary>The longest range, in days, drawn one bucket per day when the grain is automatic.</summary>
    public const int AutoDailyUpToDays = 31;

    /// <summary>The longest range, in days, drawn one bucket per week when the grain is automatic.</summary>
    public const int AutoWeeklyUpToDays = 120;

    /// <summary>Resolves <see cref="FinanceGrain.Auto"/> from the range's length.</summary>
    /// <param name="grain">The grain asked for.</param>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <returns>Day, Week or Month.</returns>
    public static FinanceGrain Resolve(FinanceGrain grain, DateOnly from, DateOnly to)
    {
        if (grain != FinanceGrain.Auto)
        {
            return grain;
        }

        var days = to.DayNumber - from.DayNumber + 1;
        return days <= AutoDailyUpToDays ? FinanceGrain.Day : days <= AutoWeeklyUpToDays ? FinanceGrain.Week : FinanceGrain.Month;
    }

    /// <summary>Returns the buckets covering a range, the first and last clipped to it.</summary>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="grain">A resolved grain.</param>
    /// <param name="firstDayOfWeek">The day weeks start on.</param>
    /// <returns>The buckets, oldest first, with inclusive ends.</returns>
    public static IReadOnlyList<(DateOnly Start, DateOnly End)> Buckets(DateOnly from, DateOnly to, FinanceGrain grain, DayOfWeek firstDayOfWeek)
    {
        var buckets = new List<(DateOnly Start, DateOnly End)>();

        for (var start = from; start <= to;)
        {
            var next = grain switch
            {
                FinanceGrain.Week => StartOfWeek(start, firstDayOfWeek).AddDays(7),
                FinanceGrain.Month => new DateOnly(start.Year, start.Month, 1).AddMonths(1),
                _ => start.AddDays(1),
            };

            var end = next.AddDays(-1);
            buckets.Add((start, end < to ? end : to));
            start = next;
        }

        return buckets;
    }

    /// <summary>Returns the first day of the week containing a day.</summary>
    /// <param name="day">The day.</param>
    /// <param name="firstDayOfWeek">The day weeks start on.</param>
    /// <returns>That week's first day.</returns>
    public static DateOnly StartOfWeek(DateOnly day, DayOfWeek firstDayOfWeek) =>
        day.AddDays(-(((int)day.DayOfWeek - (int)firstDayOfWeek + 7) % 7));
}
