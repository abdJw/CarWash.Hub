using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.Alerts;

/// <summary>Which alerts a list shows.</summary>
/// <remarks>Layer: Application.</remarks>
public enum AlertKind
{
    /// <summary>Every alert.</summary>
    All,

    /// <summary>Alerts about a service's level.</summary>
    Refill,

    /// <summary>Alerts about the machine as a whole: gone quiet, offline, or out of water.</summary>
    NoActivity,
}

/// <summary>A request for a page of alerts, newest first.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed record AlertQuery
{
    /// <summary>The most alerts a page returns.</summary>
    public const int MaxTake = 200;

    /// <summary>Gets a value indicating whether only open alerts are listed.</summary>
    public bool OpenOnly { get; init; }

    /// <summary>Gets the machine to list alerts of; null for every machine the caller sees.</summary>
    public Guid? MachineId { get; init; }

    /// <summary>Gets which alerts to list.</summary>
    public AlertKind Kind { get; init; }

    /// <summary>Gets how many to return.</summary>
    public int Take { get; init; } = 50;

    /// <summary>Gets an alert id to continue after: only older alerts are returned.</summary>
    public long? BeforeId { get; init; }
}

/// <summary>An alert as the dashboard shows it.</summary>
/// <param name="Id">The alert.</param>
/// <param name="MachineId">The machine.</param>
/// <param name="MachineCode">The machine's MAC address.</param>
/// <param name="MachineName">The owner's name for it, if any.</param>
/// <param name="StationName">Its station, in the caller's language.</param>
/// <param name="Service">The service code, for a level alert.</param>
/// <param name="ServiceName">The service's name in the caller's language.</param>
/// <param name="Type">What it is about.</param>
/// <param name="Severity">How urgent it is.</param>
/// <param name="Notify">Whether raising it notified people.</param>
/// <param name="RaisedAt">When it was raised.</param>
/// <param name="ResolvedAt">When it closed; null while open.</param>
/// <param name="Resolution">Why it closed.</param>
/// <param name="AcknowledgedAt">When someone took it on.</param>
/// <param name="AcknowledgedBy">Who took it on.</param>
/// <param name="Remaining">Uses left when raised.</param>
/// <param name="Capacity">The capacity of the refill being used up.</param>
/// <param name="ForecastEmptyAt">The forecast run-out time when raised.</param>
/// <param name="LastSeenAt">When the machine was last heard from, for a no-activity or offline alert.</param>
/// <param name="LastSaleAt">When it last sold, for a no-activity alert measured in sales.</param>
/// <param name="WaterUnavailableSince">Since when it has reported no water.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record AlertDto(
    long Id,
    Guid MachineId,
    string MachineCode,
    string? MachineName,
    string? StationName,
    string? Service,
    string? ServiceName,
    AlertType Type,
    AlertSeverity Severity,
    bool Notify,
    DateTimeOffset RaisedAt,
    DateTimeOffset? ResolvedAt,
    AlertResolution? Resolution,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedBy,
    int? Remaining,
    int? Capacity,
    DateTimeOffset? ForecastEmptyAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? LastSaleAt = null,
    DateTimeOffset? WaterUnavailableSince = null)
{
    /// <summary>Gets the machine's name to show: the owner's, or the MAC address.</summary>
    public string MachineTitle => string.IsNullOrWhiteSpace(MachineName) ? MachineCode : MachineName;

    /// <summary>Gets a value indicating whether the alert is still open.</summary>
    public bool IsOpen => ResolvedAt is null;
}

/// <summary>The open alerts the caller sees, for the bell.</summary>
/// <param name="Open">How many are open.</param>
/// <param name="Unacknowledged">How many are open and not yet taken on: the badge.</param>
/// <param name="UnacknowledgedCritical">How many of those are critical.</param>
/// <param name="LatestId">The newest alert id the caller sees, open or not; pass it back to learn what is new.</param>
/// <param name="Latest">The newest open alerts, for the bell's menu.</param>
/// <param name="Fresh">Open alerts that notify and are newer than the id passed in, newest first, for toasts.</param>
/// <param name="FreshCount">How many such alerts there are, beyond those in <paramref name="Fresh"/>.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record AlertSummaryDto(
    int Open,
    int Unacknowledged,
    int UnacknowledgedCritical,
    long LatestId,
    IReadOnlyList<AlertDto> Latest,
    IReadOnlyList<AlertDto> Fresh,
    int FreshCount);

/// <summary>Lists and acknowledges alerts, within the caller's stations.</summary>
/// <remarks>Layer: Application. Raising and resolving belong to <see cref="IAlertEvaluator"/>.</remarks>
public interface IAlertService
{
    /// <summary>Lists alerts, newest first.</summary>
    /// <param name="query">Which alerts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Up to <see cref="AlertQuery.Take"/> alerts.</returns>
    Task<IReadOnlyList<AlertDto>> ListAsync(AlertQuery query, CancellationToken cancellationToken = default);

    /// <summary>Summarises the open alerts, and which are new since the caller last looked.</summary>
    /// <param name="afterId">The <see cref="AlertSummaryDto.LatestId"/> of the previous summary; null on first load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The summary.</returns>
    Task<AlertSummaryDto> GetSummaryAsync(long? afterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the caller is handling an open alert. Acknowledging a resolved or already
    /// acknowledged alert does nothing.
    /// </summary>
    /// <param name="alertId">The alert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when saved.</returns>
    /// <exception cref="NotFoundException">The alert does not exist or is on a machine the caller cannot see.</exception>
    Task AcknowledgeAsync(long alertId, CancellationToken cancellationToken = default);
}

/// <summary>Lists and acknowledges alerts.</summary>
/// <remarks>
/// Layer: Application. Every query is limited to the caller's stations, so a station user never
/// learns of another station's alert - out of scope is "not found", never "forbidden".
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="scopeProvider">The machines the caller may see.</param>
/// <param name="currentUser">Who is acknowledging.</param>
/// <param name="clock">The current time.</param>
/// <param name="localizer">Station and service names in the caller's language.</param>
/// <param name="events">Tells other open dashboards about an acknowledgement.</param>
public sealed class AlertService(
    IUnitOfWork unitOfWork,
    IMachineAccessScopeProvider scopeProvider,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    ILocalizationService localizer,
    IDashboardEventPublisher events) : IAlertService
{
    private const int LatestCount = 8;
    private const int FreshCount = 4;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AlertDto>> ListAsync(AlertQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await ListRowsAsync(new AlertListSpecification(scopeProvider.GetScope(), query), cancellationToken).ConfigureAwait(false);
        return await ToDtosAsync(rows, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AlertSummaryDto> GetSummaryAsync(long? afterId, CancellationToken cancellationToken = default)
    {
        var scope = scopeProvider.GetScope();

        // Open alerts are few - a handful per machine at most - so they are read whole and counted here.
        var open = await ListRowsAsync(
                new AlertListSpecification(scope, new AlertQuery { OpenOnly = true, Take = AlertQuery.MaxTake }),
                cancellationToken)
            .ConfigureAwait(false);

        var latestId = (await unitOfWork.Repository<Alert, long>()
                .ListAsync(new LatestAlertIdSpecification(scope), alert => alert.Id, cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault();

        var fresh = afterId is { } after
            ? open.Where(alert => alert.Id > after && alert.Notify).ToList()
            : [];

        var dtos = await ToDtosAsync([.. open.Take(LatestCount).Union(fresh.Take(FreshCount))], cancellationToken).ConfigureAwait(false);
        var byId = dtos.ToDictionary(alert => alert.Id);

        return new AlertSummaryDto(
            open.Count,
            open.Count(alert => alert.AcknowledgedAt is null),
            open.Count(alert => alert.AcknowledgedAt is null && alert.Severity == AlertSeverity.Critical),
            latestId,
            [.. open.Take(LatestCount).Select(alert => byId[alert.Id])],
            [.. fresh.Take(FreshCount).Select(alert => byId[alert.Id])],
            fresh.Count);
    }

    /// <inheritdoc />
    public async Task AcknowledgeAsync(long alertId, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException(ResourceKeys.Errors.Forbidden);

        var alert = await unitOfWork.Repository<Alert, long>()
                        .FirstOrDefaultAsync(new AlertToAcknowledgeSpecification(alertId, scopeProvider.GetScope()), cancellationToken)
                        .ConfigureAwait(false)
                    ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Alert), alertId);

        if (alert.ResolvedAt is not null || alert.AcknowledgedAt is not null)
        {
            return;
        }

        // Only the acknowledgement columns change, so this never overwrites the worker's resolving.
        alert.AcknowledgedAt = clock.UtcNow;
        alert.AcknowledgedBy = userId;
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        events.Publish(new AlertsChangedEvent(clock.UtcNow, [alert.MachineId]));
    }

    private async Task<IReadOnlyList<AlertRow>> ListRowsAsync(AlertListSpecification specification, CancellationToken cancellationToken) =>
        await unitOfWork.Repository<Alert, long>()
            .ListAsync(
                specification,
                alert => new AlertRow(
                    alert.Id,
                    alert.MachineId,
                    alert.Machine.Code,
                    alert.Machine.DisplayName,
                    alert.Machine.GasStation != null ? alert.Machine.GasStation.Name : null,
                    alert.Machine.GasStation != null ? alert.Machine.GasStation.NameAr : null,
                    alert.ServiceCode,
                    alert.Type,
                    alert.Severity,
                    alert.Notify,
                    alert.RaisedAt,
                    alert.ResolvedAt,
                    alert.Resolution,
                    alert.AcknowledgedAt,
                    alert.AcknowledgedBy,
                    alert.Remaining,
                    alert.Capacity,
                    alert.ForecastEmptyAt,
                    alert.LastSeenAt,
                    alert.LastSaleAt,
                    alert.WaterUnavailableSince),
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<AlertDto>> ToDtosAsync(IReadOnlyList<AlertRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var services = (await unitOfWork.Repository<ServiceDefinition, Guid>()
                .ListAsync(new AllServiceDefinitionsSpecification(), cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(service => service.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => localizer.Localize(group.First().Name, group.First().NameAr), StringComparer.OrdinalIgnoreCase);

        var acknowledgerIds = rows.Where(row => row.AcknowledgedBy is not null).Select(row => row.AcknowledgedBy!.Value).Distinct().ToList();
        var acknowledgers = acknowledgerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await unitOfWork.Repository<User, Guid>()
                    .ListAsync(new UsersByIdSpecification(acknowledgerIds), user => new { user.Id, user.DisplayName }, cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(user => user.Id, user => user.DisplayName);

        return [.. rows.Select(row => new AlertDto(
            row.Id,
            row.MachineId,
            row.MachineCode,
            row.MachineName,
            row.StationName is null ? null : localizer.Localize(row.StationName, row.StationNameAr),
            row.ServiceCode,
            row.ServiceCode is null ? null : services.GetValueOrDefault(row.ServiceCode, row.ServiceCode),
            row.Type,
            row.Severity,
            row.Notify,
            row.RaisedAt,
            row.ResolvedAt,
            row.Resolution,
            row.AcknowledgedAt,
            row.AcknowledgedBy is { } by ? acknowledgers.GetValueOrDefault(by) : null,
            row.Remaining,
            row.Capacity,
            row.ForecastEmptyAt,
            row.LastSeenAt,
            row.LastSaleAt,
            row.WaterUnavailableSince))];
    }

    private sealed record AlertRow(
        long Id,
        Guid MachineId,
        string MachineCode,
        string? MachineName,
        string? StationName,
        string? StationNameAr,
        string? ServiceCode,
        AlertType Type,
        AlertSeverity Severity,
        bool Notify,
        DateTimeOffset RaisedAt,
        DateTimeOffset? ResolvedAt,
        AlertResolution? Resolution,
        DateTimeOffset? AcknowledgedAt,
        Guid? AcknowledgedBy,
        int? Remaining,
        int? Capacity,
        DateTimeOffset? ForecastEmptyAt,
        DateTimeOffset? LastSeenAt,
        DateTimeOffset? LastSaleAt,
        DateTimeOffset? WaterUnavailableSince);
}

/// <summary>A page of the alerts a caller sees, newest first.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class AlertListSpecification : BaseSpecification<Alert>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="scope">The machines the caller may see.</param>
    /// <param name="query">Which alerts.</param>
    public AlertListSpecification(MachineAccessScope scope, AlertQuery query)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        if (scope.IsRestricted)
        {
            AddCriteria(alert => alert.Machine.GasStationId == scope.GasStationId);
        }

        if (query.OpenOnly)
        {
            AddCriteria(alert => alert.ResolvedAt == null);
        }

        if (query.MachineId is { } machineId)
        {
            AddCriteria(alert => alert.MachineId == machineId);
        }

        // By range: the level alerts are numbered below the machine alerts, so a new machine alert
        // type is never filed as a refill.
        if (query.Kind == AlertKind.Refill)
        {
            AddCriteria(alert => alert.Type < AlertType.NoActivity);
        }
        else if (query.Kind == AlertKind.NoActivity)
        {
            AddCriteria(alert => alert.Type >= AlertType.NoActivity);
        }

        if (query.BeforeId is { } beforeId)
        {
            AddCriteria(alert => alert.Id < beforeId);
        }

        AddOrderBy(alert => alert.Id, descending: true);
        ApplyPaging(0, Math.Clamp(query.Take, 1, AlertQuery.MaxTake));
    }
}

/// <summary>The newest alert a caller sees.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class LatestAlertIdSpecification : BaseSpecification<Alert>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="scope">The machines the caller may see.</param>
    public LatestAlertIdSpecification(MachineAccessScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsRestricted)
        {
            AddCriteria(alert => alert.Machine.GasStationId == scope.GasStationId);
        }

        AddOrderBy(alert => alert.Id, descending: true);
        ApplyPaging(0, 1);
    }
}

/// <summary>One alert the caller sees, tracked to be acknowledged.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class AlertToAcknowledgeSpecification : BaseSpecification<Alert>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="alertId">The alert.</param>
    /// <param name="scope">The machines the caller may see.</param>
    public AlertToAcknowledgeSpecification(long alertId, MachineAccessScope scope)
        : base(alert => alert.Id == alertId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.IsRestricted)
        {
            AddCriteria(alert => alert.Machine.GasStationId == scope.GasStationId);
        }

        EnableTracking();
    }
}

/// <summary>Some users, by id.</summary>
/// <remarks>Layer: Application. Includes deactivated and deleted users: they still acknowledged what they did.</remarks>
public sealed class UsersByIdSpecification : BaseSpecification<User>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="userIds">The users.</param>
    public UsersByIdSpecification(IReadOnlyCollection<Guid> userIds)
        : base(user => userIds.Contains(user.Id))
    {
        IncludeSoftDeleted();
    }
}
