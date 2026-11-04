using System.Text.RegularExpressions;
using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Common.Validation;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Admin;
using CarWash.Hub.Domain.Entities;
using FluentValidation;

namespace CarWash.Hub.Application.Features.Stations;

/// <summary>A gas station as the admin pages show it.</summary>
/// <param name="Id">The station's key.</param>
/// <param name="Code">The short unique code.</param>
/// <param name="Name">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="City">The city.</param>
/// <param name="Address">The street address.</param>
/// <param name="IsActive">Whether the station is in service.</param>
/// <param name="MachineCount">Machines assigned to it.</param>
/// <param name="UserCount">Station users belonging to it.</param>
/// <param name="RowVersion">The version an edit must start from.</param>
/// <param name="QuietHoursStart">When its quiet hours start, local time; null when open 24 hours.</param>
/// <param name="QuietHoursEnd">When its quiet hours end, local time.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record StationDto(
    Guid Id, string Code, string Name, string NameAr, string? City, string? Address, bool IsActive, int MachineCount, int UserCount, string RowVersion,
    TimeOnly? QuietHoursStart = null, TimeOnly? QuietHoursEnd = null);

/// <summary>A station to create (no id) or update.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed record SaveStationRequest
{
    /// <summary>Gets the station to update; null to create one.</summary>
    public Guid? Id { get; init; }

    /// <summary>Gets the short unique code, for example <c>AMM-01</c>.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets the English name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the Arabic name.</summary>
    public string NameAr { get; init; } = string.Empty;

    /// <summary>Gets the city.</summary>
    public string? City { get; init; }

    /// <summary>Gets the street address.</summary>
    public string? Address { get; init; }

    /// <summary>Gets a value indicating whether the station is in service.</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Gets when quiet hours start, local time - silence then does not raise a no-activity alert.
    /// Null, with <see cref="QuietHoursEnd"/>, for a station open 24 hours.
    /// </summary>
    public TimeOnly? QuietHoursStart { get; init; } = GasStation.DefaultQuietHoursStart;

    /// <summary>Gets when quiet hours end, local time; may be the next day.</summary>
    public TimeOnly? QuietHoursEnd { get; init; } = GasStation.DefaultQuietHoursEnd;

    /// <summary>Gets the version the edit started from; required for an update.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>Manages gas stations.</summary>
/// <remarks>Layer: Application. Super Admins only.</remarks>
public interface IGasStationService
{
    /// <summary>Lists every station, active ones first.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stations.</returns>
    Task<IReadOnlyList<StationDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates or updates a station.</summary>
    /// <param name="request">The station.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The station as saved.</returns>
    Task<StationDto> SaveAsync(SaveStationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IGasStationService"/>.</summary>
/// <remarks>
/// Layer: Application. Stations are deactivated, never deleted: machines, users and their history
/// refer to them. A duplicate code surfaces from the unique index as a 409.
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="currentUser">Must be a Super Admin.</param>
/// <param name="validator">Validates the request.</param>
/// <param name="localizer">Names things in "not found" messages.</param>
/// <param name="settingsSignal">Tells the worker the machines' settings may have changed - the station may have been taken out of service.</param>
public sealed class GasStationService(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    IValidator<SaveStationRequest> validator,
    ILocalizationService localizer,
    IDeviceSettingsSignal settingsSignal) : IGasStationService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<StationDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        AdminRules.EnsureSuperAdmin(currentUser);

        var stations = await unitOfWork.Repository<GasStation, Guid>()
            .ListAsync(
                new StationsSpecification(),
                station => new
                {
                    station.Id,
                    station.Code,
                    station.Name,
                    station.NameAr,
                    station.City,
                    station.Address,
                    station.IsActive,
                    MachineCount = station.Machines.Count,
                    station.RowVersion,
                    station.QuietHoursStart,
                    station.QuietHoursEnd,
                },
                cancellationToken)
            .ConfigureAwait(false);

        var users = await unitOfWork.Repository<User, Guid>()
            .GroupAsync(
                new StationUsersSpecification(),
                user => user.GasStationId,
                (stationId, group) => new { StationId = stationId, Count = group.Count() },
                cancellationToken)
            .ConfigureAwait(false);

        var userCounts = users.Where(row => row.StationId is not null).ToDictionary(row => row.StationId!.Value, row => row.Count);

        return
        [
            .. stations.Select(station => new StationDto(
                station.Id, station.Code, station.Name, station.NameAr, station.City, station.Address, station.IsActive,
                station.MachineCount, userCounts.GetValueOrDefault(station.Id), AdminRules.Token(station.RowVersion),
                station.QuietHoursStart, station.QuietHoursEnd)),
        ];
    }

    /// <inheritdoc />
    public async Task<StationDto> SaveAsync(SaveStationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AdminRules.EnsureSuperAdmin(currentUser);
        await validator.ValidateAndThrowLocalizedAsync(request, cancellationToken).ConfigureAwait(false);

        var repository = unitOfWork.Repository<GasStation, Guid>();
        GasStation station;

        if (request.Id is { } id)
        {
            station = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
                      ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Station), id);
            AdminRules.EnsureUnchanged(station.RowVersion, request.RowVersion);
        }
        else
        {
            station = new GasStation { Id = Guid.CreateVersion7() };
            await repository.AddAsync(station, cancellationToken).ConfigureAwait(false);
        }

        station.Code = request.Code.Trim().ToUpperInvariant();
        station.Name = request.Name.Trim();
        station.NameAr = request.NameAr.Trim();
        station.City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim();
        station.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        station.IsActive = request.IsActive;
        station.QuietHoursStart = request.QuietHoursStart;
        station.QuietHoursEnd = request.QuietHoursEnd;

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        settingsSignal.Request();

        return (await ListAsync(cancellationToken).ConfigureAwait(false)).Single(saved => saved.Id == station.Id);
    }
}

/// <summary>Validates a station.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed partial class SaveStationRequestValidator : AbstractValidator<SaveStationRequest>
{
    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public SaveStationRequestValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(request => request.Code)
            .NotEmpty().WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Required, localizer.GetString(ResourceKeys.Fields.StationCode)))
            .MaximumLength(32).WithMessage(_ => localizer.GetString(ResourceKeys.Validation.MaxLength, localizer.GetString(ResourceKeys.Fields.StationCode), 32))
            .Must(code => CodePattern().IsMatch(code.Trim()))
            .When(request => !string.IsNullOrWhiteSpace(request.Code))
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.InvalidCode, localizer.GetString(ResourceKeys.Fields.StationCode)));

        RuleFor(request => request.Name).Required(localizer, ResourceKeys.Fields.Name, 200);
        RuleFor(request => request.NameAr).Required(localizer, ResourceKeys.Fields.NameAr, 200);
        RuleFor(request => request.City).Optional(localizer, ResourceKeys.Fields.City, 100);
        RuleFor(request => request.Address).Optional(localizer, ResourceKeys.Fields.Address, 500);

        // Both or neither (open 24 hours), and not the same time: that would be no quiet hours at all.
        RuleFor(request => request.QuietHoursEnd)
            .Must((request, end) => request.QuietHoursStart is null ? end is null : end is { } value && value != request.QuietHoursStart)
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.QuietHours));
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex CodePattern();
}

/// <summary>Every station, active first, then by code.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class StationsSpecification : BaseSpecification<GasStation>
{
    /// <summary>Builds the specification.</summary>
    public StationsSpecification()
    {
        AddOrderBy(station => station.IsActive, descending: true);
        AddOrderBy(station => station.Code);
    }
}

/// <summary>Users that belong to a station.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class StationUsersSpecification : BaseSpecification<User>
{
    /// <summary>Builds the specification.</summary>
    public StationUsersSpecification()
        : base(user => user.GasStationId != null)
    {
    }
}
