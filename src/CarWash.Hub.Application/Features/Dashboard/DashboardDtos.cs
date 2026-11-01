using System.ComponentModel.DataAnnotations;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.Dashboard;

/// <summary>The time window a dashboard chart covers.</summary>
/// <remarks>Layer: Application.</remarks>
public enum DashboardRange
{
    /// <summary>The last 24 hours, in hourly buckets.</summary>
    Last24Hours,

    /// <summary>The last 7 business days including today, in daily buckets.</summary>
    Last7Days,

    /// <summary>The last 30 business days including today, in daily buckets.</summary>
    Last30Days,
}

/// <summary>How the dashboard judges a machine's activity.</summary>
/// <remarks>Layer: Application. Bound from <c>Alerts</c>; the M5 alert rules read the same section.</remarks>
public sealed class ActivityOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Alerts";

    /// <summary>
    /// Gets or sets how many hours without any message make a machine "silent". Machines send only
    /// when used, so this is the only sign of a machine that is off, offline or broken.
    /// </summary>
    [Range(1, 168)]
    public int NoActivityAfterHours { get; set; } = 6;

    /// <summary>Gets or sets how recent a use must be, in minutes, for a machine to count as busy now.</summary>
    [Range(1, 240)]
    public int BusyWithinMinutes { get; set; } = 15;
}

/// <summary>How a machine is doing, from its most recent messages.</summary>
/// <remarks>Layer: Application.</remarks>
public enum MachineActivity
{
    /// <summary>Never heard from since processing began.</summary>
    Unknown,

    /// <summary>A service was used within <see cref="ActivityOptions.BusyWithinMinutes"/>.</summary>
    Busy,

    /// <summary>Heard from recently, but not in use right now.</summary>
    Idle,

    /// <summary>Nothing received for <see cref="ActivityOptions.NoActivityAfterHours"/> or more.</summary>
    Silent,
}

/// <summary>A service as the dashboard shows it.</summary>
/// <param name="Code">The code machines send.</param>
/// <param name="Name">The name in the current language.</param>
/// <param name="Icon">The icon key.</param>
/// <param name="Color">An owner-chosen colour, or null for the palette's.</param>
/// <param name="TracksRefill">Whether refills are tracked.</param>
/// <param name="SortOrder">The position in lists and charts.</param>
/// <param name="IsConfigured">False for a code the machines introduced on their own.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ServiceInfoDto(string Code, string Name, string? Icon, string? Color, bool TracksRefill, int SortOrder, bool IsConfigured);

/// <summary>A number of uses of one service.</summary>
/// <param name="Service">The service code.</param>
/// <param name="Count">The uses.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ServiceCountDto(string Service, int Count);

/// <summary>Uses per service in one chart bucket.</summary>
/// <param name="Start">The bucket start: an hour, or a business day's local midnight.</param>
/// <param name="Counts">Uses per service; services with none are absent.</param>
/// <param name="Total">Uses across services.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record UsageBucketDto(DateTimeOffset Start, IReadOnlyList<ServiceCountDto> Counts, int Total);

/// <summary>Usage over a window, bucketed, every bucket present - empty ones included.</summary>
/// <param name="Range">The window.</param>
/// <param name="BucketSize">Each bucket's length.</param>
/// <param name="Buckets">The buckets, oldest first.</param>
/// <param name="Totals">Uses per service over the window, busiest first.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record UsageSeriesDto(DashboardRange Range, TimeSpan BucketSize, IReadOnlyList<UsageBucketDto> Buckets, IReadOnlyList<ServiceCountDto> Totals)
{
    /// <summary>Gets the uses across the whole window.</summary>
    public int Total => Totals.Sum(total => total.Count);
}

/// <summary>The fleet at a glance.</summary>
/// <param name="MachineCount">Active machines in scope.</param>
/// <param name="BusyCount">Machines used within the busy window.</param>
/// <param name="SilentCount">Machines silent past the no-activity threshold, or never heard from.</param>
/// <param name="UnassignedCount">Machines not yet assigned to a station; zero for a station user.</param>
/// <param name="UsesToday">Uses since local midnight.</param>
/// <param name="UsesYesterdaySoFar">Uses yesterday from midnight to the same time of day.</param>
/// <param name="TodayByService">Uses since local midnight, per service, busiest first.</param>
/// <param name="Last12Hours">Uses per hour over the last 12 hours, oldest first.</param>
/// <param name="Usage">Usage over the requested window.</param>
/// <param name="GeneratedAt">When the numbers were read, in UTC.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record FleetOverviewDto(
    int MachineCount,
    int BusyCount,
    int SilentCount,
    int UnassignedCount,
    int UsesToday,
    int UsesYesterdaySoFar,
    IReadOnlyList<ServiceCountDto> TodayByService,
    IReadOnlyList<int> Last12Hours,
    UsageSeriesDto Usage,
    DateTimeOffset GeneratedAt);

/// <summary>How much of a tracked service's last refill is left on one machine.</summary>
/// <param name="Service">The service code.</param>
/// <param name="Level">The level.</param>
/// <param name="Capacity">The last refill's capacity; null if never refilled.</param>
/// <param name="Used">Uses since that refill.</param>
/// <param name="Remaining">Uses left; negative once overused; null if never refilled.</param>
/// <param name="PercentRemaining">The share left, 0 to 100; null if never refilled.</param>
/// <param name="ForecastEmptyAt">When it is expected to run out; null when unknown or over 30 days away.</param>
/// <param name="RefilledAt">When it was last refilled.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ServiceLevelDto(
    string Service, ServiceLevel Level, int? Capacity, int Used, int? Remaining, int? PercentRemaining, DateTimeOffset? ForecastEmptyAt, DateTimeOffset? RefilledAt);

/// <summary>One machine's card on the dashboard.</summary>
/// <param name="Id">The machine's key; dashboard links use it.</param>
/// <param name="Code">The identifier the machine sends - its MAC address.</param>
/// <param name="DisplayName">The owner's name for it, if any.</param>
/// <param name="GasStationId">Its station; null until assigned.</param>
/// <param name="StationName">The station's name in the current language.</param>
/// <param name="IsActive">Whether the machine is in service.</param>
/// <param name="FirstSeenAt">When its first message arrived.</param>
/// <param name="LastSeenAt">When its newest processed message arrived.</param>
/// <param name="LastUsageAt">When a service was last used.</param>
/// <param name="LastServiceCode">The service used last.</param>
/// <param name="TotalUses">Uses since it was first seen.</param>
/// <param name="Activity">Busy, idle or silent.</param>
/// <param name="UsesToday">Uses since local midnight, per service.</param>
/// <param name="Last12Hours">Uses per hour over the last 12 hours, oldest first.</param>
/// <param name="Levels">The refill level of each tracked service, in no particular order.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record MachineCardDto(
    Guid Id,
    string Code,
    string? DisplayName,
    Guid? GasStationId,
    string? StationName,
    bool IsActive,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? LastUsageAt,
    string? LastServiceCode,
    long TotalUses,
    MachineActivity Activity,
    IReadOnlyList<ServiceCountDto> UsesToday,
    IReadOnlyList<int> Last12Hours,
    IReadOnlyList<ServiceLevelDto> Levels)
{
    /// <summary>Gets the name to show: the owner's, or the MAC address.</summary>
    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? Code : DisplayName;

    /// <summary>Gets the uses since local midnight, across services.</summary>
    public int UsesTodayTotal => UsesToday.Sum(count => count.Count);
}

/// <summary>One use, for an activity feed.</summary>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Service">The service code.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record UsageEventDto(DateTimeOffset OccurredAt, string Service);

/// <summary>A stored message, as a machine's detail page lists it.</summary>
/// <param name="Id">The message id.</param>
/// <param name="ReceivedAt">When it arrived.</param>
/// <param name="Payload">The stored data, clipped for display.</param>
/// <param name="Status">Where it is in processing.</param>
/// <param name="ProcessingError">Why it was ignored or failed.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record MessageSummaryDto(long Id, DateTimeOffset ReceivedAt, string Payload, ProcessingStatus Status, string? ProcessingError);

/// <summary>A machine's detail page.</summary>
/// <param name="Card">The machine as its card shows it.</param>
/// <param name="Usage">Usage over the requested window.</param>
/// <param name="RecentUses">The latest uses, newest first.</param>
/// <param name="RecentMessages">The latest messages, newest first.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record MachineDetailDto(
    MachineCardDto Card,
    UsageSeriesDto Usage,
    IReadOnlyList<UsageEventDto> RecentUses,
    IReadOnlyList<MessageSummaryDto> RecentMessages);
