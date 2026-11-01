using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CarWash.Hub.Application.Features.Levels;

/// <summary>
/// Keeps every machine's per-service level current. Used only by the processing worker.
/// </summary>
/// <remarks>Layer: Application.</remarks>
public interface IServiceLevelUpdater
{
    /// <summary>
    /// Counts a batch's new uses against each service's last refill. Runs inside the batch's
    /// transaction and stages its changes for the batch's own save.
    /// </summary>
    /// <param name="uses">The uses the batch recorded.</param>
    /// <param name="now">The processing time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the changes are staged.</returns>
    Task ApplyUsageAsync(IReadOnlyList<ServiceUsage> uses, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the pending rebuild requests: recomputes each machine's levels from its refills and
    /// usage history, saves, and deletes the requests.
    /// </summary>
    /// <param name="now">The processing time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The machines recomputed.</returns>
    Task<IReadOnlyCollection<Guid>> ApplyRebuildRequestsAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Recomputes the run-out forecast of every service that has some capacity left, and saves.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The machines whose forecast changed.</returns>
    Task<IReadOnlyCollection<Guid>> RefreshForecastsAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IServiceLevelUpdater"/>.</summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>Two paths to one answer.</b> On each use the count is incremented, which is cheap. On a
/// refill, a void or a catalog change the level is recomputed from scratch: the latest standing
/// refill, and a count of the uses since its <c>refilled_at</c>. A refill recorded late, a voided one
/// or a backdated one therefore needs no special case, and a counter can never drift for long.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="logger">Logger.</param>
public sealed class ServiceLevelUpdater(IUnitOfWork unitOfWork, ILogger<ServiceLevelUpdater> logger) : IServiceLevelUpdater
{
    /// <inheritdoc />
    public async Task ApplyUsageAsync(IReadOnlyList<ServiceUsage> uses, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uses);
        if (uses.Count == 0)
        {
            return;
        }

        var catalog = await CatalogAsync(cancellationToken).ConfigureAwait(false);
        var repository = unitOfWork.Repository<MachineServiceState, long>();
        var machineIds = uses.Select(use => use.MachineId).Distinct().ToList();

        var states = (await repository.ListAsync(new LevelStatesSpecification(machineIds), cancellationToken).ConfigureAwait(false))
            .ToDictionary(state => (state.MachineId, state.ServiceCode), PairComparer.Instance);

        foreach (var pair in uses.GroupBy(use => (use.MachineId, use.ServiceCode), PairComparer.Instance))
        {
            if (!states.TryGetValue(pair.Key, out var state))
            {
                state = new MachineServiceState { MachineId = pair.Key.MachineId, ServiceCode = pair.Key.ServiceCode };
                await repository.AddAsync(state, cancellationToken).ConfigureAwait(false);
                states[pair.Key] = state;
            }

            foreach (var use in pair)
            {
                // A use from before the refill (a delayed message) belongs to the previous fill.
                if (state.CurrentRefillId is not null && use.OccurredAt >= state.RefilledAt)
                {
                    state.UsedSinceRefill++;
                }

                if (state.LastUsageAt is null || use.OccurredAt > state.LastUsageAt)
                {
                    state.LastUsageAt = use.OccurredAt;
                }
            }

            Classify(state, catalog, now);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Guid>> ApplyRebuildRequestsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var requests = unitOfWork.Repository<StateRebuildRequest, long>();
        var pending = await requests.ListAsync(new PendingRebuildsSpecification(), cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return [];
        }

        var machineIds = pending.Any(request => request.MachineId is null)
            ? await AllTrackedMachinesAsync(cancellationToken).ConfigureAwait(false)
            : [.. pending.Select(request => request.MachineId!.Value).Distinct()];

        var catalog = await CatalogAsync(cancellationToken).ConfigureAwait(false);
        foreach (var machineId in machineIds)
        {
            await RebuildMachineAsync(machineId, catalog, now, cancellationToken).ConfigureAwait(false);
        }

        requests.RemoveRange(pending);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        unitOfWork.DiscardChanges();

        logger.LogInformation("Recomputed service levels of {Count} machine(s)", machineIds.Count);
        return machineIds;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Guid>> RefreshForecastsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var states = await unitOfWork.Repository<MachineServiceState, long>()
            .ListAsync(new ForecastableStatesSpecification(), cancellationToken)
            .ConfigureAwait(false);

        var changed = new HashSet<Guid>();
        foreach (var state in states)
        {
            var before = state.ForecastEmptyAt;
            await ForecastAsync(state, now, cancellationToken).ConfigureAwait(false);
            if (before is null != state.ForecastEmptyAt is null
                || (before is { } was && state.ForecastEmptyAt is { } @is && Math.Abs((was - @is).TotalMinutes) >= 30))
            {
                changed.Add(state.MachineId);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        unitOfWork.DiscardChanges();
        return changed;
    }

    private static void Classify(MachineServiceState state, IReadOnlyDictionary<string, ServiceDefinition> catalog, DateTimeOffset now)
    {
        var tracked = catalog.TryGetValue(state.ServiceCode, out var service) && service.TracksRefill && service.IsActive;
        var threshold = service?.LowThresholdPercent ?? ServiceDefinition.DefaultLowThresholdPercent;

        state.Level = ServiceLevelCalculator.Compute(tracked, state.Capacity, state.UsedSinceRefill, threshold);
        if (state.Level is not (ServiceLevel.Ok or ServiceLevel.Low))
        {
            state.ForecastEmptyAt = null;
        }

        state.UpdatedAt = now;
    }

    private async Task RebuildMachineAsync(Guid machineId, IReadOnlyDictionary<string, ServiceDefinition> catalog, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<MachineServiceState, long>();
        var states = (await repository.ListAsync(new LevelStatesSpecification([machineId]), cancellationToken).ConfigureAwait(false))
            .ToDictionary(state => state.ServiceCode, StringComparer.OrdinalIgnoreCase);

        // The latest standing refill of each service. Items of voided refills are hidden by the
        // soft-delete filter, so a void simply makes the previous refill the latest again.
        var latest = (await unitOfWork.Repository<RefillItem, Guid>()
                .ListAsync(
                    new MachineRefillItemsSpecification(machineId),
                    item => new { item.ServiceCode, item.RefillId, item.Refill.RefilledAt, item.Capacity },
                    cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(item => item.ServiceCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.MaxBy(item => item.RefilledAt)!, StringComparer.OrdinalIgnoreCase);

        foreach (var code in states.Keys.Union(latest.Keys, StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (!states.TryGetValue(code, out var state))
            {
                state = new MachineServiceState { MachineId = machineId, ServiceCode = code };
                await repository.AddAsync(state, cancellationToken).ConfigureAwait(false);
            }

            if (latest.TryGetValue(code, out var refill))
            {
                state.CurrentRefillId = refill.RefillId;
                state.RefilledAt = refill.RefilledAt;
                state.Capacity = refill.Capacity;
                state.UsedSinceRefill = await unitOfWork.Repository<ServiceUsage, long>()
                    .CountAsync(new UsesSinceSpecification(machineId, code, refill.RefilledAt), cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                state.CurrentRefillId = null;
                state.RefilledAt = null;
                state.Capacity = null;
                state.UsedSinceRefill = 0;
            }

            Classify(state, catalog, now);
            if (state.Level is ServiceLevel.Ok or ServiceLevel.Low)
            {
                await ForecastAsync(state, now, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ForecastAsync(MachineServiceState state, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var from = OccurrenceTimeResolver.HourStart(now.AddDays(-UsageForecaster.HistoryDays));
        var history = await unitOfWork.Repository<ServiceUsageHourly, long>()
            .ListAsync(
                new ServiceHourlyHistorySpecification(state.MachineId, state.ServiceCode, from),
                row => new { row.HourStart, row.UsageCount },
                cancellationToken)
            .ConfigureAwait(false);

        // History starts at the first use known in the window, so a machine installed last week is
        // not averaged over four weeks of silence it never had.
        var historyStart = history.Count > 0 ? history.Min(row => row.HourStart) : now;
        var forecast = UsageForecaster.Forecast(
            state.Remaining ?? 0,
            [.. history.Select(row => (row.HourStart, row.UsageCount))],
            historyStart,
            now);

        state.ForecastEmptyAt = forecast.EmptyAt;
        state.AverageDailyUses = forecast.AverageDailyUses;
        state.UpdatedAt = now;
    }

    private async Task<IReadOnlyDictionary<string, ServiceDefinition>> CatalogAsync(CancellationToken cancellationToken) =>
        (await unitOfWork.Repository<ServiceDefinition, Guid>()
            .ListAsync(new AllServiceDefinitionsSpecification(), cancellationToken)
            .ConfigureAwait(false))
        .Where(service => !service.IsDeleted)
        .ToDictionary(service => service.Code, StringComparer.OrdinalIgnoreCase);

    private async Task<List<Guid>> AllTrackedMachinesAsync(CancellationToken cancellationToken)
    {
        var withStates = await unitOfWork.Repository<MachineServiceState, long>()
            .ListAsync(new LevelStatesSpecification(null), state => state.MachineId, cancellationToken)
            .ConfigureAwait(false);
        var withRefills = await unitOfWork.Repository<Refill, Guid>()
            .ListAsync(new AllRefillsSpecification(), refill => refill.MachineId, cancellationToken)
            .ConfigureAwait(false);

        return [.. withStates.Union(withRefills)];
    }

    private sealed class PairComparer : IEqualityComparer<(Guid MachineId, string ServiceCode)>
    {
        public static readonly PairComparer Instance = new();

        public bool Equals((Guid MachineId, string ServiceCode) x, (Guid MachineId, string ServiceCode) y) =>
            x.MachineId == y.MachineId && string.Equals(x.ServiceCode, y.ServiceCode, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid MachineId, string ServiceCode) obj) =>
            HashCode.Combine(obj.MachineId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ServiceCode));
    }
}

/// <summary>The level rows of some machines (or all), tracked to be updated.</summary>
/// <remarks>Layer: Application. Includes rows of deleted machines, so none is inserted twice.</remarks>
public sealed class LevelStatesSpecification : BaseSpecification<MachineServiceState>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineIds">The machines, or null for every one.</param>
    public LevelStatesSpecification(IReadOnlyCollection<Guid>? machineIds)
    {
        if (machineIds is not null)
        {
            AddCriteria(state => machineIds.Contains(state.MachineId));
        }

        IncludeSoftDeleted();
        EnableTracking();
    }
}

/// <summary>The standing refill items of one machine.</summary>
/// <remarks>Layer: Application. Voided refills' items are filtered out by the soft-delete filter.</remarks>
public sealed class MachineRefillItemsSpecification : BaseSpecification<RefillItem>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineId">The machine.</param>
    public MachineRefillItemsSpecification(Guid machineId)
        : base(item => item.Refill.MachineId == machineId)
    {
    }
}

/// <summary>Every standing refill.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class AllRefillsSpecification : BaseSpecification<Refill>
{
}

/// <summary>A service's uses on a machine since a time - the count against a refill.</summary>
/// <remarks>Layer: Application. A range count on <c>(machine_id, service_code, occurred_at)</c>.</remarks>
public sealed class UsesSinceSpecification : BaseSpecification<ServiceUsage>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="serviceCode">The service.</param>
    /// <param name="since">The refill time, inclusive.</param>
    public UsesSinceSpecification(Guid machineId, string serviceCode, DateTimeOffset since)
        : base(use => use.MachineId == machineId && use.ServiceCode == serviceCode && use.OccurredAt >= since)
    {
        IncludeSoftDeleted();
    }
}

/// <summary>A service's hourly uses on a machine since an hour - the forecast's history.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class ServiceHourlyHistorySpecification : BaseSpecification<ServiceUsageHourly>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="serviceCode">The service.</param>
    /// <param name="fromHour">The first hour, inclusive.</param>
    public ServiceHourlyHistorySpecification(Guid machineId, string serviceCode, DateTimeOffset fromHour)
        : base(row => row.MachineId == machineId && row.ServiceCode == serviceCode && row.HourStart >= fromHour)
    {
        IncludeSoftDeleted();
    }
}

/// <summary>The pending rebuild requests, oldest first, tracked to be deleted.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class PendingRebuildsSpecification : BaseSpecification<StateRebuildRequest>
{
    /// <summary>Builds the specification.</summary>
    public PendingRebuildsSpecification()
    {
        AddOrderBy(request => request.Id);
        EnableTracking();
    }
}

/// <summary>The levels a forecast means something for: refilled, and not yet empty.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class ForecastableStatesSpecification : BaseSpecification<MachineServiceState>
{
    /// <summary>Builds the specification.</summary>
    public ForecastableStatesSpecification()
        : base(state => state.CurrentRefillId != null && (state.Level == ServiceLevel.Ok || state.Level == ServiceLevel.Low))
    {
        EnableTracking();
    }
}
