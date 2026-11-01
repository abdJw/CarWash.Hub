using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.Alerts;

/// <summary>
/// The pure rules behind alerts: which alert a service's level calls for, how urgent it is, who
/// hears about it, and how long a machine has really been silent.
/// </summary>
/// <remarks>Layer: Application. No I/O and no clock, so every rule is tested on its own.</remarks>
public static class AlertRules
{
    /// <summary>A service forecast to run out within this long, though not yet low, raises <see cref="AlertType.RunsOutSoon"/>.</summary>
    public static readonly TimeSpan RunsOutSoonWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// An open <see cref="AlertType.RunsOutSoon"/> closes only once the forecast moves past this, so a
    /// forecast wobbling around <see cref="RunsOutSoonWindow"/> does not raise and clear it all day.
    /// </summary>
    public static readonly TimeSpan RunsOutSoonClearAt = TimeSpan.FromHours(30);

    // Silence older than this is counted whole: thresholds stop at a week, and quiet hours never
    // cover a whole day, so a longer look-back cannot change whether an alert is due.
    private static readonly TimeSpan MaxQuietHoursLookBack = TimeSpan.FromDays(30);

    /// <summary>Returns whether an alert type is about a service's level.</summary>
    /// <param name="type">The alert type.</param>
    /// <returns><see langword="true"/> for the four level alerts.</returns>
    public static bool IsLevelAlert(AlertType type) =>
        type is AlertType.RunsOutSoon or AlertType.Low or AlertType.Empty or AlertType.UsedAfterEmpty;

    /// <summary>Returns the level alert a service calls for: the most urgent that applies, or none.</summary>
    /// <param name="level">The service's level.</param>
    /// <param name="forecastEmptyAt">When it is forecast to run out, if known.</param>
    /// <param name="open">The level alert already open for the service, if any.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The alert type, or null when the service is fine.</returns>
    public static AlertType? DesiredLevelAlert(ServiceLevel level, DateTimeOffset? forecastEmptyAt, AlertType? open, DateTimeOffset now) => level switch
    {
        ServiceLevel.Overused => AlertType.UsedAfterEmpty,
        ServiceLevel.Empty => AlertType.Empty,
        ServiceLevel.Low => AlertType.Low,
        ServiceLevel.Ok when forecastEmptyAt is { } at
                             && at - now <= (open == AlertType.RunsOutSoon ? RunsOutSoonClearAt : RunsOutSoonWindow) => AlertType.RunsOutSoon,
        _ => null,
    };

    /// <summary>Returns how urgent an alert type is.</summary>
    /// <param name="type">The alert type.</param>
    /// <returns>Critical once customers are affected, otherwise a warning.</returns>
    public static AlertSeverity SeverityOf(AlertType type) =>
        type is AlertType.Empty or AlertType.UsedAfterEmpty ? AlertSeverity.Critical : AlertSeverity.Warning;

    /// <summary>
    /// Returns whether raising an alert should notify people: on a first alert, when it gets more
    /// urgent, and always for a service used after empty or a silent machine. Swapping one warning for
    /// another ("runs out soon" becoming "low") does not notify again.
    /// </summary>
    /// <param name="replaced">The alert it replaces for the same service, if any.</param>
    /// <param name="raised">The alert being raised.</param>
    /// <returns><see langword="true"/> to notify.</returns>
    public static bool ShouldNotify(AlertType? replaced, AlertType raised) =>
        raised is AlertType.UsedAfterEmpty or AlertType.NoActivity
        || replaced is null
        || SeverityOf(raised) > SeverityOf(replaced.Value);

    /// <summary>
    /// Returns whether the service was refilled since an alert was raised. Compares refill times, not
    /// refill ids: a backdated refill still counts, and a void - which moves the time back - does not.
    /// </summary>
    /// <param name="baseline">When the service had last been refilled when the alert was raised.</param>
    /// <param name="refilledAt">When it was last refilled now.</param>
    /// <returns><see langword="true"/> after a new refill.</returns>
    public static bool IsRefill(DateTimeOffset? baseline, DateTimeOffset? refilledAt) =>
        refilledAt is { } current && (baseline is null || current > baseline);

    /// <summary>
    /// Returns how long a machine has been silent, not counting its station's quiet hours: a machine
    /// last used before closing is not "silent" overnight.
    /// </summary>
    /// <param name="lastSeen">When the machine was last heard from.</param>
    /// <param name="now">The current time.</param>
    /// <param name="quietStart">When quiet hours start, in local time; null when there are none.</param>
    /// <param name="quietEnd">When quiet hours end, in local time; may be the next day.</param>
    /// <param name="zone">The station's time zone.</param>
    /// <returns>The silence outside quiet hours; zero if <paramref name="lastSeen"/> is in the future.</returns>
    public static TimeSpan ActiveSilence(DateTimeOffset lastSeen, DateTimeOffset now, TimeOnly? quietStart, TimeOnly? quietEnd, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (now <= lastSeen)
        {
            return TimeSpan.Zero;
        }

        if (quietStart is not { } start || quietEnd is not { } end || start == end)
        {
            return now - lastSeen;
        }

        var from = lastSeen < now - MaxQuietHoursLookBack ? now - MaxQuietHoursLookBack : lastSeen;
        var older = from - lastSeen;
        var quiet = TimeSpan.Zero;

        // Every quiet window that can overlap [from, now]: one starting on each local day, from the
        // day before 'from' (a window crossing midnight) to the day of 'now'.
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).DateTime).AddDays(-1);
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            var windowStart = AtLocal(day, start, zone);
            var windowEnd = AtLocal(end > start ? day : day.AddDays(1), end, zone);

            var overlapStart = windowStart > from ? windowStart : from;
            var overlapEnd = windowEnd < now ? windowEnd : now;
            if (overlapEnd > overlapStart)
            {
                quiet += overlapEnd - overlapStart;
            }
        }

        return older + (now - from) - quiet;
    }

    private static DateTimeOffset AtLocal(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);

        // A local time skipped by a clock change does not exist; move it past the gap.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
