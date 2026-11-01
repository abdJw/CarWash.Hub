using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Features.Dashboard;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Application.Features.Alerts;

/// <summary>Raises and resolves alerts so that exactly the right ones are open.</summary>
/// <remarks>Layer: Application. Run only by the processing worker, under its lease.</remarks>
public interface IAlertEvaluator
{
    /// <summary>
    /// Brings the open alerts of some machines (or all) in line with their current state, saves, and
    /// tells open dashboards.
    /// </summary>
    /// <param name="machineIds">The machines to check, or null for every machine.</param>
    /// <param name="now">The current time.</param>
    /// <param name="allowNoActivityRaise">
    /// Whether a silent machine may raise a no-activity alert now; false shortly after a restart,
    /// while messages that arrived during the outage are still being processed. Resolving is always
    /// allowed.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What changed.</returns>
    Task<AlertEvaluation> EvaluateAsync(
        IReadOnlyCollection<Guid>? machineIds,
        DateTimeOffset now,
        bool allowNoActivityRaise,
        CancellationToken cancellationToken = default);
}

/// <summary>What one evaluation changed.</summary>
/// <param name="Raised">Alerts raised.</param>
/// <param name="Resolved">Alerts resolved.</param>
/// <param name="MachineIds">The machines whose alerts changed.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record AlertEvaluation(int Raised, int Resolved, IReadOnlyCollection<Guid> MachineIds)
{
    /// <summary>Gets an evaluation that changed nothing.</summary>
    public static AlertEvaluation None { get; } = new(0, 0, []);
}

/// <summary>
/// Compares the alerts that should be open with those that are, and raises, keeps or resolves.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>Desired state, not transitions.</b> Each run reads the machines' current levels and last-seen
/// times and works out from scratch which alerts belong open, so it is idempotent: running it twice,
/// or after a missed run, gives the same result. That is what lets the worker call it after every
/// batch for the machines just touched and once a minute for all of them.
/// </para>
/// <para>
/// <b>Level alerts.</b> A service has at most one open: the most urgent that applies. A more urgent
/// one replaces it (<see cref="AlertResolution.Escalated"/>); a refill resolves it
/// (<see cref="AlertResolution.Refilled"/>), detected by the refill time moving past the one the
/// alert started from, so a backdated refill counts and a void does not.
/// </para>
/// <para>
/// <b>No activity.</b> Raised when a machine's silence outside its station's quiet hours reaches its
/// threshold; resolved by its next message.
/// </para>
/// <para>
/// Only machines assigned to an active station and in service raise alerts - an unassigned id may be
/// a test rig or an invented one. When a machine stops qualifying, its alerts are resolved as
/// <see cref="AlertResolution.NotEligible"/>.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="timeZone">The zone quiet hours are written in.</param>
/// <param name="activityOptions">The default no-activity threshold.</param>
/// <param name="events">Tells open dashboards.</param>
/// <param name="logger">Logger.</param>
public sealed class AlertEvaluator(
    IUnitOfWork unitOfWork,
    IBusinessTimeZone timeZone,
    IOptions<ActivityOptions> activityOptions,
    IDashboardEventPublisher events,
    ILogger<AlertEvaluator> logger) : IAlertEvaluator
{
    private readonly int _defaultNoActivityHours = activityOptions.Value.NoActivityAfterHours;

    /// <inheritdoc />
    public async Task<AlertEvaluation> EvaluateAsync(
        IReadOnlyCollection<Guid>? machineIds,
        DateTimeOffset now,
        bool allowNoActivityRaise,
        CancellationToken cancellationToken = default)
    {
        if (machineIds is { Count: 0 })
        {
            return AlertEvaluation.None;
        }

        var alerts = unitOfWork.Repository<Alert, long>();
        var open = await alerts.ListAsync(new OpenAlertsSpecification(machineIds), cancellationToken).ConfigureAwait(false);

        var machines = await unitOfWork.Machines
            .ListAsync(
                new AlertEligibleMachinesSpecification(machineIds),
                machine => new EligibleMachine(
                    machine.Id,
                    machine.NoActivityAfterHours,
                    machine.GasStation!.QuietHoursStart,
                    machine.GasStation.QuietHoursEnd,
                    machine.State != null ? (DateTimeOffset?)machine.State.LastSeenAt : null),
                cancellationToken)
            .ConfigureAwait(false);

        var levels = machines.Count == 0
            ? []
            : await unitOfWork.Repository<MachineServiceState, long>()
                .ListAsync(
                    new AlertLevelStatesSpecification([.. machines.Select(machine => machine.Id)]),
                    state => new LevelRow(
                        state.MachineId, state.ServiceCode, state.Level, state.Capacity, state.UsedSinceRefill, state.RefilledAt, state.ForecastEmptyAt),
                    cancellationToken)
                .ConfigureAwait(false);

        var run = new Run(now);
        var eligible = machines.Select(machine => machine.Id).ToHashSet();
        foreach (var alert in open.Where(alert => !eligible.Contains(alert.MachineId)))
        {
            run.Resolve(alert, AlertResolution.NotEligible);
        }

        var openByMachine = open.ToLookup(alert => alert.MachineId);
        var levelsByMachine = levels.ToLookup(level => level.MachineId);
        foreach (var machine in machines)
        {
            var machineAlerts = openByMachine[machine.Id].ToList();
            EvaluateLevels(run, machine.Id, levelsByMachine[machine.Id], machineAlerts.Where(alert => AlertRules.IsLevelAlert(alert.Type)));
            EvaluateActivity(run, machine, machineAlerts.Where(alert => alert.Type == AlertType.NoActivity), allowNoActivityRaise);
        }

        if (!run.HasChanges)
        {
            return AlertEvaluation.None;
        }

        await alerts.AddRangeAsync(run.Raised, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        unitOfWork.DiscardChanges();

        var changed = run.Machines.ToList();
        if (run.Raised.Count > 0 || run.ResolvedCount > 0)
        {
            events.Publish(new AlertsChangedEvent(now, changed));
            logger.LogInformation(
                "Alerts: {Raised} raised, {Resolved} resolved, on {Machines} machine(s)", run.Raised.Count, run.ResolvedCount, changed.Count);
        }

        return new AlertEvaluation(run.Raised.Count, run.ResolvedCount, changed);
    }

    private static void EvaluateLevels(Run run, Guid machineId, IEnumerable<LevelRow> levels, IEnumerable<Alert> openLevelAlerts)
    {
        var states = levels.ToDictionary(level => level.ServiceCode, StringComparer.OrdinalIgnoreCase);
        var openByService = openLevelAlerts
            .Where(alert => alert.ServiceCode is not null)
            .GroupBy(alert => alert.ServiceCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(alert => alert.Type).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var service in states.Keys.Union(openByService.Keys, StringComparer.OrdinalIgnoreCase).ToList())
        {
            states.TryGetValue(service, out var state);
            var current = openByService.TryGetValue(service, out var list) ? list : [];

            // One level alert per service; anything beyond the most urgent is left over from a bug.
            foreach (var extra in current.Skip(1))
            {
                run.Resolve(extra, AlertResolution.Cleared);
            }

            var openAlert = current.FirstOrDefault();
            var desired = state is null ? null : AlertRules.DesiredLevelAlert(state.Level, state.ForecastEmptyAt, openAlert?.Type, run.Now);
            var refilled = openAlert is not null && AlertRules.IsRefill(openAlert.BaselineRefilledAt, state?.RefilledAt);

            if (openAlert is not null && desired == openAlert.Type)
            {
                // Still the same alert. Refilled but straight back to it (a backdated refill already
                // used up): keep it open, measured from the new refill.
                if (refilled)
                {
                    run.Touch(openAlert);
                    openAlert.BaselineRefilledAt = state!.RefilledAt;
                }

                continue;
            }

            if (openAlert is not null)
            {
                run.Resolve(openAlert, desired is { } next && next > openAlert.Type
                    ? AlertResolution.Escalated
                    : refilled ? AlertResolution.Refilled : AlertResolution.Cleared);
            }

            if (desired is { } type && state is not null)
            {
                run.Raise(new Alert
                {
                    MachineId = machineId,
                    ServiceCode = state.ServiceCode,
                    Type = type,
                    Severity = AlertRules.SeverityOf(type),
                    Notify = AlertRules.ShouldNotify(openAlert?.Type, type),
                    RaisedAt = run.Now,
                    BaselineRefilledAt = state.RefilledAt,
                    Remaining = state.Capacity - state.UsedSinceRefill,
                    Capacity = state.Capacity,
                    ForecastEmptyAt = state.ForecastEmptyAt,
                });
            }
        }
    }

    private void EvaluateActivity(Run run, EligibleMachine machine, IEnumerable<Alert> openActivityAlerts, bool allowRaise)
    {
        var current = openActivityAlerts.OrderBy(alert => alert.Id).ToList();
        foreach (var extra in current.Skip(1))
        {
            run.Resolve(extra, AlertResolution.Cleared);
        }

        var openAlert = current.FirstOrDefault();
        var threshold = TimeSpan.FromHours(machine.NoActivityAfterHours ?? _defaultNoActivityHours);
        var silent = machine.LastSeenAt is { } lastSeen
                     && AlertRules.ActiveSilence(lastSeen, run.Now, machine.QuietHoursStart, machine.QuietHoursEnd, timeZone.Zone) >= threshold;

        if (openAlert is not null && !silent)
        {
            // Heard from again - or, with no new message, the threshold or quiet hours were changed.
            run.Resolve(openAlert, machine.LastSeenAt > openAlert.LastSeenAt ? AlertResolution.ActivityResumed : AlertResolution.Cleared);
        }
        else if (openAlert is null && silent && allowRaise)
        {
            run.Raise(new Alert
            {
                MachineId = machine.Id,
                Type = AlertType.NoActivity,
                Severity = AlertRules.SeverityOf(AlertType.NoActivity),
                Notify = AlertRules.ShouldNotify(null, AlertType.NoActivity),
                RaisedAt = run.Now,
                LastSeenAt = machine.LastSeenAt,
            });
        }
    }

    private sealed record EligibleMachine(Guid Id, int? NoActivityAfterHours, TimeOnly? QuietHoursStart, TimeOnly? QuietHoursEnd, DateTimeOffset? LastSeenAt);

    private sealed record LevelRow(
        Guid MachineId,
        string ServiceCode,
        ServiceLevel Level,
        int? Capacity,
        int UsedSinceRefill,
        DateTimeOffset? RefilledAt,
        DateTimeOffset? ForecastEmptyAt);

    // The changes of one evaluation, applied to tracked alerts and saved together.
    private sealed class Run(DateTimeOffset now)
    {
        public DateTimeOffset Now { get; } = now;

        public List<Alert> Raised { get; } = [];

        public int ResolvedCount { get; private set; }

        public HashSet<Guid> Machines { get; } = [];

        public bool HasChanges => Machines.Count > 0;

        public void Raise(Alert alert)
        {
            Raised.Add(alert);
            Machines.Add(alert.MachineId);
        }

        public void Resolve(Alert alert, AlertResolution resolution)
        {
            alert.ResolvedAt = Now;
            alert.Resolution = resolution;
            ResolvedCount++;
            Machines.Add(alert.MachineId);
        }

        public void Touch(Alert alert) => Machines.Add(alert.MachineId);
    }
}

/// <summary>The open alerts of some machines (or all), tracked to be resolved.</summary>
/// <remarks>Layer: Application. Includes alerts of deleted machines, which must be resolved too.</remarks>
public sealed class OpenAlertsSpecification : BaseSpecification<Alert>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineIds">The machines, or null for every one.</param>
    public OpenAlertsSpecification(IReadOnlyCollection<Guid>? machineIds)
        : base(alert => alert.ResolvedAt == null)
    {
        if (machineIds is not null)
        {
            AddCriteria(alert => machineIds.Contains(alert.MachineId));
        }

        IncludeSoftDeleted();
        EnableTracking();
    }
}

/// <summary>Machines that may raise alerts: assigned to an active station and in service.</summary>
/// <remarks>Layer: Application. Deleted machines and stations are excluded by the soft-delete filter.</remarks>
public sealed class AlertEligibleMachinesSpecification : BaseSpecification<Machine>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineIds">The machines, or null for every one.</param>
    public AlertEligibleMachinesSpecification(IReadOnlyCollection<Guid>? machineIds)
        : base(machine => machine.IsActive && machine.GasStationId != null && machine.GasStation!.IsActive)
    {
        if (machineIds is not null)
        {
            AddCriteria(machine => machineIds.Contains(machine.Id));
        }
    }
}

/// <summary>The service levels of some machines, read-only.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class AlertLevelStatesSpecification : BaseSpecification<MachineServiceState>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineIds">The machines.</param>
    public AlertLevelStatesSpecification(IReadOnlyCollection<Guid> machineIds)
        : base(state => machineIds.Contains(state.MachineId))
    {
    }
}
