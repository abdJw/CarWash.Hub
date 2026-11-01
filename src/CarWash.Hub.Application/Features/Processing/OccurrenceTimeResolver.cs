using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.Processing;

/// <summary>
/// Decides when a usage event happened: the machine's own reading time when it can be trusted,
/// otherwise the time the server received it.
/// </summary>
/// <remarks>
/// Layer: Application. The device time is better when the clock is right - a message delayed by a
/// network retry still lands in the right hour - but an ESP32 that lost its NTP sync reports 1970,
/// and one that drifted reports the future. Both are replaced rather than believed.
/// </remarks>
public static class OccurrenceTimeResolver
{
    /// <summary>Resolves the occurrence time of one event.</summary>
    /// <param name="deviceTime">The machine's reading time, if it sent one.</param>
    /// <param name="receivedAt">When the server received the message.</param>
    /// <param name="maxPastSkew">How far before <paramref name="receivedAt"/> a device time is still trusted.</param>
    /// <param name="maxFutureSkew">How far after <paramref name="receivedAt"/> a device time is still trusted.</param>
    /// <returns>The occurrence time, in UTC, and which clock it came from.</returns>
    public static (DateTimeOffset OccurredAt, OccurrenceTimeSource Source) Resolve(
        DateTimeOffset? deviceTime,
        DateTimeOffset receivedAt,
        TimeSpan maxPastSkew,
        TimeSpan maxFutureSkew)
    {
        if (deviceTime is { } device
            && device >= receivedAt - maxPastSkew
            && device <= receivedAt + maxFutureSkew)
        {
            return (device.ToUniversalTime(), OccurrenceTimeSource.Device);
        }

        return (receivedAt.ToUniversalTime(), OccurrenceTimeSource.Server);
    }

    /// <summary>Returns the start of the UTC hour containing an instant.</summary>
    /// <param name="instant">The instant.</param>
    /// <returns>The hour start, at offset zero.</returns>
    public static DateTimeOffset HourStart(DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }
}
