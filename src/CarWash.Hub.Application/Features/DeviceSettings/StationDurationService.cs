using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Common.Validation;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Admin;
using CarWash.Hub.Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace CarWash.Hub.Application.Features.DeviceSettings;

/// <summary>One service's run times at a station.</summary>
/// <param name="Service">The service code.</param>
/// <param name="Positions">How many selector positions it has: 3, or 1.</param>
/// <param name="Seconds">The run times, one per position.</param>
/// <param name="IsSuggested">
/// Whether these are only the suggested starting values, because none were set yet. Suggested values
/// are not sent to the machines; the station's settings stay incomplete until they are saved.
/// </param>
/// <param name="SetAt">When they were last saved; null while suggested.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ServiceDurationsDto(string Service, int Positions, IReadOnlyList<int> Seconds, bool IsSuggested, DateTimeOffset? SetAt);

/// <summary>A station's run times for every service the machines can be configured for.</summary>
/// <param name="StationId">The station.</param>
/// <param name="Services">One entry per configurable service, in settings order.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record StationDurationsDto(Guid StationId, IReadOnlyList<ServiceDurationsDto> Services);

/// <summary>The run times to set for one service at one station.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed record SetStationDurationsRequest
{
    /// <summary>Gets the station.</summary>
    public Guid StationId { get; init; }

    /// <summary>Gets the service code.</summary>
    public string Service { get; init; } = string.Empty;

    /// <summary>Gets the run times in seconds, one per selector position: three, or one for minute water.</summary>
    public IReadOnlyList<int> Seconds { get; init; } = [];
}

/// <summary>Reads and sets the run times each station's machines offer.</summary>
/// <remarks>Layer: Application. Super Admins set them; a station user may read its own station's.</remarks>
public interface IStationDurationService
{
    /// <summary>Returns a station's run times, with suggested values where none are set.</summary>
    /// <param name="stationId">The station.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The run times.</returns>
    /// <exception cref="NotFoundException">No such station, or not the caller's.</exception>
    Task<StationDurationsDto> GetAsync(Guid stationId, CancellationToken cancellationToken = default);

    /// <summary>Sets one service's run times at a station.</summary>
    /// <param name="request">The run times.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when they are saved.</returns>
    Task SetAsync(SetStationDurationsRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IStationDurationService"/>.</summary>
/// <remarks>
/// Layer: Application. A save only changes what the machines should run; each machine picks it up on
/// its next settings poll, and the dashboard shows it as pending until the machine confirms.
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="currentUser">Must be a Super Admin to change run times.</param>
/// <param name="scopeProvider">The stations the caller may see.</param>
/// <param name="validator">Validates the run times.</param>
/// <param name="localizer">Messages.</param>
/// <param name="settingsSignal">Tells the worker the machines' settings changed.</param>
/// <param name="logger">Logger.</param>
public sealed class StationDurationService(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    IMachineAccessScopeProvider scopeProvider,
    IValidator<SetStationDurationsRequest> validator,
    ILocalizationService localizer,
    IDeviceSettingsSignal settingsSignal,
    ILogger<StationDurationService> logger) : IStationDurationService
{
    /// <inheritdoc />
    public async Task<StationDurationsDto> GetAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        var scope = scopeProvider.GetScope();

        // Not found rather than forbidden for another station: its existence is not revealed.
        _ = (scope.IsRestricted && scope.GasStationId != stationId
                ? null
                : await unitOfWork.Repository<GasStation, Guid>().GetByIdAsync(stationId, cancellationToken).ConfigureAwait(false))
            ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Station), stationId);

        var rows = await unitOfWork.Repository<StationServiceDuration, Guid>()
            .ListAsync(new LiveDurationsSpecification([stationId]), cancellationToken)
            .ConfigureAwait(false);

        return new StationDurationsDto(
            stationId,
            [
                .. DeviceServices.All.Select(service =>
                    rows.FirstOrDefault(row => string.Equals(row.ServiceCode, service.Code, StringComparison.OrdinalIgnoreCase)) is { } row
                    && row.Seconds().Count == service.Positions
                        ? new ServiceDurationsDto(service.Code, service.Positions, row.Seconds(), IsSuggested: false, row.ModifiedAt ?? row.CreatedAt)
                        : new ServiceDurationsDto(service.Code, service.Positions, service.SuggestedSeconds, IsSuggested: true, SetAt: null)),
            ]);
    }

    /// <inheritdoc />
    public async Task SetAsync(SetStationDurationsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AdminRules.EnsureSuperAdmin(currentUser);
        await validator.ValidateAndThrowLocalizedAsync(request, cancellationToken).ConfigureAwait(false);

        _ = await unitOfWork.Repository<GasStation, Guid>().GetByIdAsync(request.StationId, cancellationToken).ConfigureAwait(false)
            ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Station), request.StationId);

        var service = DeviceServices.Find(request.Service)!;
        var repository = unitOfWork.Repository<StationServiceDuration, Guid>();
        var row = await repository
            .FirstOrDefaultAsync(new StationDurationSpecification(request.StationId, service.Code), cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new StationServiceDuration { Id = Guid.CreateVersion7(), GasStationId = request.StationId, ServiceCode = service.Code };
            await repository.AddAsync(row, cancellationToken).ConfigureAwait(false);
        }

        row.Duration1Seconds = request.Seconds[0];
        row.Duration2Seconds = service.Positions == 3 ? request.Seconds[1] : null;
        row.Duration3Seconds = service.Positions == 3 ? request.Seconds[2] : null;

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        settingsSignal.Request();

        logger.LogInformation(
            "Run times of {Service} at station {StationId} set to {Seconds} s by {UserName}",
            service.Code, request.StationId, string.Join("/", request.Seconds), currentUser.UserName);
    }
}

/// <summary>Validates a station's run times for one service.</summary>
/// <remarks>Layer: Application. The limits are the firmware's own, so a machine never refuses them.</remarks>
public sealed class SetStationDurationsRequestValidator : AbstractValidator<SetStationDurationsRequest>
{
    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public SetStationDurationsRequestValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(request => request.StationId)
            .NotEmpty().WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Required, localizer.GetString(ResourceKeys.Fields.Station)));

        RuleFor(request => request.Service)
            .Must(service => DeviceServices.Find(service) is not null)
            .WithMessage(request => localizer.GetString(ResourceKeys.Validation.NotDeviceService, request.Service));

        RuleFor(request => request.Seconds)
            .Must((request, seconds) => seconds.Count == DeviceServices.Find(request.Service)!.Positions)
            .WithMessage(request => localizer.GetString(
                ResourceKeys.Validation.DurationCount,
                DeviceServices.Find(request.Service)!.Code,
                DeviceServices.Find(request.Service)!.Positions))
            .Must(seconds => seconds.All(DeviceServices.IsDuration))
            .WithMessage(_ => localizer.GetString(
                ResourceKeys.Validation.DurationRange, StationServiceDuration.MinSeconds, StationServiceDuration.MaxSeconds))
            .When(request => DeviceServices.Find(request.Service) is not null);
    }
}

/// <summary>The live run-time rows of some stations - or all.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class LiveDurationsSpecification : BaseSpecification<StationServiceDuration>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="stationIds">The stations; null for every station.</param>
    public LiveDurationsSpecification(IReadOnlyCollection<Guid>? stationIds)
    {
        if (stationIds is not null)
        {
            AddCriteria(row => stationIds.Contains(row.GasStationId));
        }
    }
}

/// <summary>One station's live run-time row for a service, tracked to be edited.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class StationDurationSpecification : BaseSpecification<StationServiceDuration>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="stationId">The station.</param>
    /// <param name="service">The service code.</param>
    public StationDurationSpecification(Guid stationId, string service)
        : base(row => row.GasStationId == stationId && row.ServiceCode == service)
    {
        EnableTracking();
    }
}
