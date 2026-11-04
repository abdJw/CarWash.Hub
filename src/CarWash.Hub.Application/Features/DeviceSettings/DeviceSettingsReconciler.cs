using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Features.Finance;
using CarWash.Hub.Application.Features.Pricing;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CarWash.Hub.Application.Features.DeviceSettings;

/// <summary>How often the machines' desired settings are recomputed without being asked.</summary>
/// <remarks>Layer: Application. Bound to the <c>DeviceSettings</c> configuration section.</remarks>
public sealed class DeviceSettingsOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "DeviceSettings";

    /// <summary>
    /// Gets or sets the longest time between two recomputes, in seconds. A save in the dashboard
    /// recomputes at once; this is what makes a dated price that starts at midnight reach the
    /// machines within a minute.
    /// </summary>
    public int ReconcileIntervalSeconds { get; set; } = 60;
}

/// <summary>Brings every machine's desired settings in line with its station's prices and run times.</summary>
/// <remarks>Layer: Application. Run by the processing worker only - the single writer of the desired side.</remarks>
public interface IDeviceSettingsReconciler
{
    /// <summary>Recomputes every machine's desired settings, issuing a revision where the values changed.</summary>
    /// <param name="now">The moment to compute for; today's business date decides the prices.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The machines whose settings or settings issue changed.</returns>
    Task<IReadOnlyList<Guid>> ReconcileAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IDeviceSettingsReconciler"/>.</summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>Desired state, not events.</b> Rather than reacting to each price or run-time change, every
/// machine's snapshot is rebuilt from its station's values as they are now and compared with what it
/// was last sent. A change from any cause - a save, a dated price taking effect, a machine moving
/// station - is therefore caught the same way, and recomputing twice is harmless: equal values never
/// get a new revision.
/// </para>
/// <para>
/// <b>Never a partial snapshot.</b> If a station is missing a price or run time, its machines keep
/// being sent their last complete settings and the row records the issue for the dashboard.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="timeZone">The business day prices are in effect on.</param>
/// <param name="events">Tells open dashboards which machines changed.</param>
/// <param name="logger">Logger.</param>
public sealed class DeviceSettingsReconciler(
    IUnitOfWork unitOfWork,
    IBusinessTimeZone timeZone,
    IDashboardEventPublisher events,
    ILogger<DeviceSettingsReconciler> logger) : IDeviceSettingsReconciler
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ReconcileAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var today = timeZone.DateOf(now);

        var machines = await unitOfWork.Repository<Machine, Guid>()
            .ListAsync(new LiveMachinesSpecification(), machine => new { machine.Id, machine.GasStationId }, cancellationToken)
            .ConfigureAwait(false);

        if (machines.Count == 0)
        {
            return [];
        }

        List<Guid> stationIds = [.. machines.Select(machine => machine.GasStationId).OfType<Guid>().Distinct()];

        var activeStations = stationIds.Count == 0
            ? []
            : (await unitOfWork.Repository<GasStation, Guid>()
                    .ListAsync(new StationsByIdsSpecification(stationIds), station => new { station.Id, station.IsActive, station.IsDeleted }, cancellationToken)
                    .ConfigureAwait(false))
                .Where(station => station.IsActive && !station.IsDeleted)
                .Select(station => station.Id)
                .ToHashSet();

        var prices = stationIds.Count == 0
            ? []
            : await unitOfWork.Repository<StationServicePrice, Guid>()
                .ListAsync(
                    new LivePricesSpecification(stationIds, [.. DeviceServices.All.Select(service => service.Code)], until: today),
                    price => new PricePoint(price.Id, price.GasStationId, price.ServiceCode, price.EffectiveFrom, price.UnitPrice),
                    cancellationToken)
                .ConfigureAwait(false);

        var durations = stationIds.Count == 0
            ? []
            : await unitOfWork.Repository<StationServiceDuration, Guid>()
                .ListAsync(new LiveDurationsSpecification(stationIds), cancellationToken)
                .ConfigureAwait(false);

        var repository = unitOfWork.Repository<MachineDeviceSettings, Guid>();
        var rows = (await repository.ListAsync(new DeviceSettingsRowsSpecification(null), cancellationToken).ConfigureAwait(false))
            .ToDictionary(row => row.Id);

        var book = new PriceBook(prices);

        // One computation per station: its machines all get the same snapshot.
        var snapshots = new Dictionary<Guid, (string? Json, DeviceSettingsIssue Issue, string? IssueService)>();
        foreach (var stationId in stationIds)
        {
            if (!activeStations.Contains(stationId))
            {
                snapshots[stationId] = (null, DeviceSettingsIssue.StationInactive, null);
                continue;
            }

            var services = DeviceSettingsRules.TryBuild(stationId, book, today, durations, out var issue, out var issueService);
            snapshots[stationId] = (services is null ? null : DeviceSettingsDocument.WriteServices(services), issue, issueService);
        }

        var changed = new List<Guid>();
        foreach (var machine in machines)
        {
            var (json, issue, issueService) = machine.GasStationId is { } stationId
                ? snapshots[stationId]
                : (null, DeviceSettingsIssue.Unassigned, null);

            if (!rows.TryGetValue(machine.Id, out var row))
            {
                row = new MachineDeviceSettings { Id = machine.Id, UpdatedAt = now };
                await repository.AddAsync(row, cancellationToken).ConfigureAwait(false);
                rows[machine.Id] = row;
            }

            var published = json is not null && DeviceSettingsRules.Publish(row, json, now);
            var issueChanged = row.Issue != issue || !string.Equals(row.IssueService, issueService, StringComparison.Ordinal);

            if (!published && !issueChanged)
            {
                continue;
            }

            row.Issue = issue;
            row.IssueService = issueService;
            row.UpdatedAt = now;
            changed.Add(machine.Id);

            if (published)
            {
                logger.LogInformation("Machine {MachineId} settings revision {Revision} issued", machine.Id, row.DesiredRevision);
            }
        }

        if (changed.Count == 0)
        {
            unitOfWork.DiscardChanges();
            return [];
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        unitOfWork.DiscardChanges();

        events.Publish(new MachineActivityEvent(now, changed, []));
        return changed;
    }
}

/// <summary>Every machine that is not deleted.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class LiveMachinesSpecification : BaseSpecification<Machine>;

/// <summary>Machines' settings rows - some, or all - tracked to be updated.</summary>
/// <remarks>Layer: Application. The table has no soft-delete filter, so a deleted machine's row is found too.</remarks>
public sealed class DeviceSettingsRowsSpecification : BaseSpecification<MachineDeviceSettings>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineIds">The machines; null for every machine.</param>
    /// <param name="tracked">Whether the rows are to be updated.</param>
    public DeviceSettingsRowsSpecification(IReadOnlyCollection<Guid>? machineIds, bool tracked = true)
    {
        if (machineIds is not null)
        {
            AddCriteria(row => machineIds.Contains(row.Id));
        }

        if (tracked)
        {
            EnableTracking();
        }
    }
}
