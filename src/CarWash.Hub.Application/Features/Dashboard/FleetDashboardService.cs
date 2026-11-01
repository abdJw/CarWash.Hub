using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Alerts;
using CarWash.Hub.Application.Features.Levels;
using CarWash.Hub.Application.Features.Machines;
using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Application.Features.Refills;
using CarWash.Hub.Domain.Entities;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Application.Features.Dashboard;

/// <summary>Reads what the dashboard shows: the fleet, each machine, and their usage.</summary>
/// <remarks>Layer: Application. Every read is limited to the caller's <see cref="MachineAccessScope"/>.</remarks>
public interface IFleetDashboardService
{
    /// <summary>Returns the services, in display order and the caller's language.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active services.</returns>
    Task<IReadOnlyList<ServiceInfoDto>> GetServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the fleet at a glance, with usage over a window.</summary>
    /// <param name="range">The chart window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The overview.</returns>
    Task<FleetOverviewDto> GetOverviewAsync(DashboardRange range, CancellationToken cancellationToken = default);

    /// <summary>Returns every machine in scope as a card.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cards, by name.</returns>
    Task<IReadOnlyList<MachineCardDto>> GetMachinesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns one machine's detail page.</summary>
    /// <param name="machineId">The machine's key.</param>
    /// <param name="range">The chart window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The detail.</returns>
    /// <exception cref="NotFoundException">No such machine in the caller's scope.</exception>
    Task<MachineDetailDto> GetMachineAsync(Guid machineId, DashboardRange range, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IFleetDashboardService"/>.</summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>Cheap by construction.</b> Machine state is one row per machine, and usage comes from the
/// hourly rollup grouped in the database - a 30-day fleet chart is at most 720 hours × services
/// rows - so the dashboard can refresh on every processed batch without scanning raw history.
/// </para>
/// <para>
/// <b>Business days.</b> "Today" and daily buckets start at local midnight in
/// <see cref="IBusinessTimeZone"/>. Its offset is a whole number of hours, so every day is a whole
/// set of UTC hourly rows.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="scopeProvider">The machines the caller may see.</param>
/// <param name="timeZone">The business time zone.</param>
/// <param name="clock">The current time.</param>
/// <param name="localizer">Station and service names in the caller's language.</param>
/// <param name="activityOptions">The busy and silent thresholds.</param>
public sealed class FleetDashboardService(
    IUnitOfWork unitOfWork,
    IMachineAccessScopeProvider scopeProvider,
    IBusinessTimeZone timeZone,
    IDateTimeProvider clock,
    ILocalizationService localizer,
    IOptions<ActivityOptions> activityOptions) : IFleetDashboardService
{
    private const int RecentItems = 20;
    private const int PayloadPreviewLength = 300;

    private readonly ActivityOptions _activity = activityOptions.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceInfoDto>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        var services = await unitOfWork.Repository<ServiceDefinition, Guid>()
            .ListAsync(new ActiveServicesSpecification(), cancellationToken)
            .ConfigureAwait(false);

        return [.. services.Select(service => new ServiceInfoDto(
            service.Code,
            localizer.Localize(service.Name, service.NameAr),
            service.Icon,
            service.Color,
            service.TracksRefill,
            service.SortOrder,
            service.IsConfigured))];
    }

    /// <inheritdoc />
    public async Task<FleetOverviewDto> GetOverviewAsync(DashboardRange range, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var scope = scopeProvider.GetScope();
        var machines = await ListMachinesAsync(scope, null, cancellationToken).ConfigureAwait(false);
        var active = machines.Where(machine => machine.IsActive).ToList();

        var todayStart = timeZone.StartOfDay(now);
        var currentHour = OccurrenceTimeResolver.HourStart(now);

        // One grouped read covers the chart window, today, yesterday so far and the sparkline.
        var windowStart = Earliest(WindowStart(range, now, todayStart), todayStart.AddDays(-1), currentHour.AddHours(-11));
        var hourly = await HourlyByServiceAsync(scope, windowStart, currentHour, null, cancellationToken).ConfigureAwait(false);

        var yesterdayStart = todayStart.AddDays(-1);
        var yesterdayUntil = currentHour.AddDays(-1);

        return new FleetOverviewDto(
            MachineCount: active.Count,
            BusyCount: active.Count(machine => ActivityOf(machine, now) == MachineActivity.Busy),
            SilentCount: active.Count(machine => ActivityOf(machine, now) is MachineActivity.Silent or MachineActivity.Unknown),
            UnassignedCount: scope.IsRestricted ? 0 : machines.Count(machine => machine.GasStationId is null),
            UsesToday: hourly.Where(row => row.HourStart >= todayStart).Sum(row => row.Count),
            UsesYesterdaySoFar: hourly.Where(row => row.HourStart >= yesterdayStart && row.HourStart <= yesterdayUntil).Sum(row => row.Count),
            TodayByService: Totals(hourly.Where(row => row.HourStart >= todayStart)),
            Last12Hours: HourTotals(hourly, currentHour),
            Usage: BuildSeries(range, now, todayStart, hourly),
            GeneratedAt: now);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MachineCardDto>> GetMachinesAsync(CancellationToken cancellationToken = default)
    {
        var scope = scopeProvider.GetScope();
        var machines = await ListMachinesAsync(scope, null, cancellationToken).ConfigureAwait(false);
        return await ToCardsAsync(scope, machines, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<MachineDetailDto> GetMachineAsync(Guid machineId, DashboardRange range, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var scope = scopeProvider.GetScope();

        var machines = await ListMachinesAsync(scope, machineId, cancellationToken).ConfigureAwait(false);
        if (machines.Count == 0)
        {
            throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Machine), machineId);
        }

        var card = (await ToCardsAsync(scope, machines, cancellationToken).ConfigureAwait(false))[0];

        var todayStart = timeZone.StartOfDay(now);
        var hourly = await HourlyByServiceAsync(
            scope, WindowStart(range, now, todayStart), OccurrenceTimeResolver.HourStart(now), machineId, cancellationToken).ConfigureAwait(false);

        var recentUses = await unitOfWork.Repository<ServiceUsage, long>()
            .ListAsync(
                new RecentUsesSpecification(machineId, RecentItems),
                usage => new UsageEventDto(usage.OccurredAt, usage.ServiceCode),
                cancellationToken)
            .ConfigureAwait(false);

        var recentMessages = await unitOfWork.MachineMessages
            .ListAsync(
                new MachineMessagesSpecification(machineId, new MachineHistoryQuery { PageNumber = 1, PageSize = RecentItems }),
                message => new MessageSummaryDto(message.Id, message.ReceivedAt, message.Payload, message.ProcessingStatus, message.ProcessingError),
                cancellationToken)
            .ConfigureAwait(false);

        return new MachineDetailDto(
            card,
            BuildSeries(range, now, todayStart, hourly),
            recentUses,
            [.. recentMessages.Select(message => message with { Payload = Clip(message.Payload) })]);
    }

    private static DateTimeOffset Earliest(params DateTimeOffset[] instants) => instants.Min();

    private static string Clip(string payload) =>
        payload.Length <= PayloadPreviewLength ? payload : string.Concat(payload.AsSpan(0, PayloadPreviewLength), "…");

    private static List<ServiceCountDto> Totals(IEnumerable<HourServiceCount> rows) =>
    [
        .. rows
            .GroupBy(row => row.Service, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ServiceCountDto(group.Key, group.Sum(row => row.Count)))
            .Where(total => total.Count > 0)
            .OrderByDescending(total => total.Count)
            .ThenBy(total => total.Service, StringComparer.Ordinal),
    ];

    // Twelve values, oldest first, ending with the current (partial) hour; empty hours are zero.
    private static List<int> HourTotals(IReadOnlyList<HourServiceCount> rows, DateTimeOffset currentHour) =>
    [
        .. Enumerable.Range(0, 12)
            .Select(offset => currentHour.AddHours(offset - 11))
            .Select(hour => rows.Where(row => row.HourStart == hour).Sum(row => row.Count)),
    ];

    private DateTimeOffset WindowStart(DashboardRange range, DateTimeOffset now, DateTimeOffset todayStart) => range switch
    {
        DashboardRange.Last7Days => DayStart(todayStart, -6),
        DashboardRange.Last30Days => DayStart(todayStart, -29),
        _ => OccurrenceTimeResolver.HourStart(now).AddHours(-23),
    };

    // Midday of the target day, then back to its midnight: correct even across a DST change.
    private DateTimeOffset DayStart(DateTimeOffset todayStart, int days) => timeZone.StartOfDay(todayStart.AddDays(days).AddHours(12));

    private UsageSeriesDto BuildSeries(DashboardRange range, DateTimeOffset now, DateTimeOffset todayStart, IReadOnlyList<HourServiceCount> hourly)
    {
        List<(DateTimeOffset Start, DateTimeOffset End)> buckets;
        TimeSpan size;

        if (range == DashboardRange.Last24Hours)
        {
            size = TimeSpan.FromHours(1);
            var first = OccurrenceTimeResolver.HourStart(now).AddHours(-23);
            buckets = [.. Enumerable.Range(0, 24).Select(i => (first.AddHours(i), first.AddHours(i + 1)))];
        }
        else
        {
            size = TimeSpan.FromDays(1);
            var days = range == DashboardRange.Last7Days ? 7 : 30;
            buckets = [.. Enumerable.Range(0, days).Select(i =>
            {
                var start = DayStart(todayStart, i - (days - 1));
                return (timeZone.ToLocal(start), DayStart(todayStart, i - (days - 2)));
            })];
        }

        var inWindow = hourly.Where(row => row.HourStart >= buckets[0].Start && row.HourStart < buckets[^1].End).ToList();

        var series = buckets.Select(bucket =>
        {
            var counts = Totals(inWindow.Where(row => row.HourStart >= bucket.Start && row.HourStart < bucket.End));
            return new UsageBucketDto(bucket.Start, counts, counts.Sum(count => count.Count));
        }).ToList();

        return new UsageSeriesDto(range, size, series, Totals(inWindow));
    }

    private async Task<List<HourServiceCount>> HourlyByServiceAsync(
        MachineAccessScope scope,
        DateTimeOffset from,
        DateTimeOffset toHour,
        Guid? machineId,
        CancellationToken cancellationToken)
    {
        var rows = await unitOfWork.Repository<ServiceUsageHourly, long>()
            .GroupAsync(
                new ScopedHourlyUsageSpecification(scope, OccurrenceTimeResolver.HourStart(from), toHour, machineId),
                row => new { row.HourStart, row.ServiceCode },
                (key, group) => new HourServiceCount(key.HourStart, key.ServiceCode, group.Sum(row => row.UsageCount)),
                cancellationToken)
            .ConfigureAwait(false);

        return [.. rows];
    }

    private async Task<IReadOnlyList<MachineRow>> ListMachinesAsync(MachineAccessScope scope, Guid? machineId, CancellationToken cancellationToken) =>
        await unitOfWork.Machines
            .ListAsync(
                new DashboardMachinesSpecification(scope, machineId),
                machine => new MachineRow(
                    machine.Id,
                    machine.Code,
                    machine.DisplayName,
                    machine.GasStationId,
                    machine.GasStation != null ? machine.GasStation.Name : null,
                    machine.GasStation != null ? machine.GasStation.NameAr : null,
                    machine.IsActive,
                    machine.CreatedAt,
                    machine.State != null ? (DateTimeOffset?)machine.State.LastSeenAt : null,
                    machine.State != null ? machine.State.LastUsageAt : null,
                    machine.State != null ? machine.State.LastServiceCode : null,
                    machine.State != null ? machine.State.TotalUses : 0L,
                    machine.NoActivityAfterHours,
                    machine.GasStation != null ? machine.GasStation.QuietHoursStart : null,
                    machine.GasStation != null ? machine.GasStation.QuietHoursEnd : null),
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<List<MachineCardDto>> ToCardsAsync(MachineAccessScope scope, IReadOnlyList<MachineRow> machines, CancellationToken cancellationToken)
    {
        if (machines.Count == 0)
        {
            return [];
        }

        var now = clock.UtcNow;
        var currentHour = OccurrenceTimeResolver.HourStart(now);
        var todayStart = timeZone.StartOfDay(now);
        var from = Earliest(todayStart, currentHour.AddHours(-11));
        Guid? onlyMachine = machines.Count == 1 ? machines[0].Id : null;

        var rows = await unitOfWork.Repository<ServiceUsageHourly, long>()
            .GroupAsync(
                new ScopedHourlyUsageSpecification(scope, OccurrenceTimeResolver.HourStart(from), currentHour, onlyMachine),
                row => new { row.MachineId, row.HourStart, row.ServiceCode },
                (key, group) => new MachineHourServiceCount(key.MachineId, key.HourStart, key.ServiceCode, group.Sum(row => row.UsageCount)),
                cancellationToken)
            .ConfigureAwait(false);

        var byMachine = rows.ToLookup(row => row.MachineId);

        var levels = (await unitOfWork.Repository<MachineServiceState, long>()
                .ListAsync(
                    new MachineLevelsSpecification([.. machines.Select(machine => machine.Id)]),
                    state => new
                    {
                        state.MachineId,
                        Level = new ServiceLevelDto(
                            state.ServiceCode, state.Level, state.Capacity, state.UsedSinceRefill, state.Capacity - state.UsedSinceRefill,
                            null, state.ForecastEmptyAt, state.RefilledAt),
                    },
                    cancellationToken)
                .ConfigureAwait(false))
            .Where(row => row.Level.Level != Domain.Enums.ServiceLevel.NotTracked)
            .ToLookup(row => row.MachineId, row => row.Level with
            {
                PercentRemaining = ServiceLevelCalculator.PercentRemaining(row.Level.Capacity, row.Level.Used),
            });

        return [.. machines.Select(machine =>
        {
            var own = byMachine[machine.Id].Select(row => new HourServiceCount(row.HourStart, row.Service, row.Count)).ToList();

            return new MachineCardDto(
                machine.Id,
                machine.Code,
                machine.DisplayName,
                machine.GasStationId,
                machine.StationName is null ? null : localizer.Localize(machine.StationName, machine.StationNameAr),
                machine.IsActive,
                machine.FirstSeenAt,
                machine.LastSeenAt,
                machine.LastUsageAt,
                machine.LastServiceCode,
                machine.TotalUses,
                ActivityOf(machine, now),
                Totals(own.Where(row => row.HourStart >= todayStart)),
                HourTotals(own, currentHour),
                [.. levels[machine.Id]]);
        })];
    }

    private MachineActivity ActivityOf(MachineRow machine, DateTimeOffset now)
    {
        if (machine.LastSeenAt is not { } lastSeen)
        {
            return MachineActivity.Unknown;
        }

        // The same rule as the no-activity alert, so a "silent" card always has (or is about to have)
        // an alert: the machine's own threshold, not counting its station's quiet hours.
        var silence = AlertRules.ActiveSilence(lastSeen, now, machine.QuietHoursStart, machine.QuietHoursEnd, timeZone.Zone);
        if (silence >= TimeSpan.FromHours(machine.NoActivityAfterHours ?? _activity.NoActivityAfterHours))
        {
            return MachineActivity.Silent;
        }

        return machine.LastUsageAt is { } lastUse && now - lastUse <= TimeSpan.FromMinutes(_activity.BusyWithinMinutes)
            ? MachineActivity.Busy
            : MachineActivity.Idle;
    }

    private sealed record MachineRow(
        Guid Id,
        string Code,
        string? DisplayName,
        Guid? GasStationId,
        string? StationName,
        string? StationNameAr,
        bool IsActive,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset? LastSeenAt,
        DateTimeOffset? LastUsageAt,
        string? LastServiceCode,
        long TotalUses,
        int? NoActivityAfterHours,
        TimeOnly? QuietHoursStart,
        TimeOnly? QuietHoursEnd);

    private sealed record HourServiceCount(DateTimeOffset HourStart, string Service, int Count);

    private sealed record MachineHourServiceCount(Guid MachineId, DateTimeOffset HourStart, string Service, int Count);
}
