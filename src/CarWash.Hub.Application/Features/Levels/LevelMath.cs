using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.Levels;

/// <summary>Decides a service's level from its last refill and the uses since.</summary>
/// <remarks>Layer: Application. Pure, so the boundaries are unit-tested exactly.</remarks>
public static class ServiceLevelCalculator
{
    /// <summary>Returns the level.</summary>
    /// <param name="tracked">Whether refills of the service are tracked.</param>
    /// <param name="capacity">The last refill's capacity; null if never refilled.</param>
    /// <param name="used">The uses since that refill.</param>
    /// <param name="lowThresholdPercent">The share left at or below which the service is low.</param>
    /// <returns>The level.</returns>
    public static ServiceLevel Compute(bool tracked, int? capacity, int used, int lowThresholdPercent)
    {
        if (!tracked)
        {
            return ServiceLevel.NotTracked;
        }

        if (capacity is not > 0)
        {
            return ServiceLevel.NeverRefilled;
        }

        var remaining = capacity.Value - used;
        return remaining switch
        {
            < 0 => ServiceLevel.Overused,
            0 => ServiceLevel.Empty,

            // remaining / capacity <= threshold%, in integers: no rounding decides a boundary.
            _ when (long)remaining * 100 <= (long)capacity.Value * lowThresholdPercent => ServiceLevel.Low,
            _ => ServiceLevel.Ok,
        };
    }

    /// <summary>Returns the share of the capacity left, 0 to 100.</summary>
    /// <param name="capacity">The capacity; null if never refilled.</param>
    /// <param name="used">The uses since the refill.</param>
    /// <returns>The percentage, or null when unknown.</returns>
    public static int? PercentRemaining(int? capacity, int used) =>
        capacity is > 0 ? Math.Clamp((int)Math.Floor(100.0 * (capacity.Value - used) / capacity.Value), 0, 100) : null;
}

/// <summary>A forecast of when a service runs out.</summary>
/// <param name="EmptyAt">When it is expected to run out; null when not within the horizon or not enough history.</param>
/// <param name="AverageDailyUses">The average uses per day in the history; null without history.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record UsageForecast(DateTimeOffset? EmptyAt, decimal? AverageDailyUses);

/// <summary>
/// Forecasts when a service runs out, from how the machine is usually used at each hour of the week.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// A car wash is busy on weekend afternoons and quiet at night, so a flat average would say "two
/// days" on a Thursday night that is really "Friday at 18:00". The forecaster builds the machine's
/// usual uses per hour of the week from up to <see cref="HistoryDays"/> days of history (per hour of
/// the day when less than a week is known), then walks forward from now, hour by hour, until the
/// remaining uses are spent.
/// </para>
/// <para>
/// It says nothing rather than something wrong: with fewer than <see cref="MinimumUses"/> uses of
/// history, or no run-out within <see cref="Horizon"/>, the forecast is null.
/// </para>
/// </remarks>
public static class UsageForecaster
{
    /// <summary>The most history the profile is built from, in days.</summary>
    public const int HistoryDays = 28;

    /// <summary>The fewest uses of history a forecast is made from.</summary>
    public const int MinimumUses = 5;

    /// <summary>The furthest ahead a run-out is forecast.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(30);

    /// <summary>Forecasts the run-out.</summary>
    /// <param name="remaining">The uses left.</param>
    /// <param name="hourly">Uses per UTC hour, any order; hours with none may be absent.</param>
    /// <param name="historyStart">The start of the period <paramref name="hourly"/> covers - the refill time or the first use, at most <see cref="HistoryDays"/> ago.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The forecast.</returns>
    public static UsageForecast Forecast(int remaining, IReadOnlyList<(DateTimeOffset HourStart, int Count)> hourly, DateTimeOffset historyStart, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(hourly);

        var earliest = now.AddDays(-HistoryDays);
        var start = historyStart < earliest ? earliest : historyStart;
        var days = Math.Max((now - start).TotalDays, 1d / 24);
        var inWindow = hourly.Where(hour => hour.HourStart >= Floor(start) && hour.HourStart <= now).ToList();
        var total = inWindow.Sum(hour => hour.Count);

        if (total < MinimumUses)
        {
            return new UsageForecast(null, total == 0 ? null : (decimal)Math.Round(total / days, 2));
        }

        var averageDaily = (decimal)Math.Round(total / Math.Max(days, 1), 2);
        if (remaining <= 0)
        {
            return new UsageForecast(null, averageDaily);
        }

        // Expected uses per slot: per hour of the week once a full week is known, per hour of the
        // day before that, and never zero everywhere (the overall rate is the floor of last resort).
        var weekly = days >= 7;
        var slots = weekly ? 7 * 24 : 24;
        var spans = weekly ? days / 7 : days;
        var profile = new double[slots];
        foreach (var hour in inWindow)
        {
            profile[Slot(hour.HourStart, weekly)] += hour.Count / spans;
        }

        var cursor = Floor(now);
        var left = (double)remaining;
        var end = now + Horizon;

        // The current hour has partly passed; only its remainder counts.
        var firstFraction = 1 - (now - cursor).TotalHours;
        for (var first = true; cursor < end; cursor = cursor.AddHours(1), first = false)
        {
            var expected = profile[Slot(cursor, weekly)] * (first ? firstFraction : 1);
            if (expected >= left)
            {
                var into = expected <= 0 ? 0 : left / expected;
                var at = first ? now + TimeSpan.FromHours(into * firstFraction) : cursor + TimeSpan.FromHours(into);
                return new UsageForecast(at, averageDaily);
            }

            left -= expected;
        }

        return new UsageForecast(null, averageDaily);
    }

    private static int Slot(DateTimeOffset instant, bool weekly)
    {
        var utc = instant.UtcDateTime;
        return weekly ? ((int)utc.DayOfWeek * 24) + utc.Hour : utc.Hour;
    }

    private static DateTimeOffset Floor(DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }
}
