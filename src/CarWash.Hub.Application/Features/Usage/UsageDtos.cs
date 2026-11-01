namespace CarWash.Hub.Application.Features.Usage;

/// <summary>The time window of a usage query.</summary>
/// <remarks>
/// Layer: Application. Both ends are optional: the window ends now and starts 24 hours before its
/// end unless given. Counts are per whole UTC hour, so the window is widened to the hours it touches.
/// </remarks>
public sealed record UsageQuery
{
    /// <summary>The default window length.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    /// <summary>The longest window accepted.</summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(31);

    /// <summary>Gets the window start, inclusive.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Gets the window end, inclusive.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Resolves the window against the current time.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>The start and end.</returns>
    public (DateTimeOffset From, DateTimeOffset To) Resolve(DateTimeOffset now)
    {
        var to = To ?? now;
        return (From ?? to - DefaultWindow, to);
    }
}

/// <summary>A machine's usage over a window: totals per service and counts per hour.</summary>
/// <param name="MachineId">The identifier the machine sends.</param>
/// <param name="From">The window start, as resolved.</param>
/// <param name="To">The window end, as resolved.</param>
/// <param name="Totals">Uses per service over the whole window, busiest first.</param>
/// <param name="Hourly">Uses per service per UTC hour, oldest first; hours with no use are absent.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record MachineUsageDto(
    string MachineId,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ServiceUsageTotalDto> Totals,
    IReadOnlyList<HourlyUsageDto> Hourly);

/// <summary>Uses of one service over a window.</summary>
/// <param name="Service">The service code.</param>
/// <param name="Count">The number of uses.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ServiceUsageTotalDto(string Service, int Count);

/// <summary>Uses of one service in one hour.</summary>
/// <param name="HourStart">The start of the hour, in UTC.</param>
/// <param name="Service">The service code.</param>
/// <param name="Count">The number of uses.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record HourlyUsageDto(DateTimeOffset HourStart, string Service, int Count);
