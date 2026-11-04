using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Common.Validation;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Admin;
using CarWash.Hub.Domain.Entities;
using FluentValidation;

namespace CarWash.Hub.Application.Features.Machines;

/// <summary>A machine's editable settings.</summary>
/// <param name="Id">The machine's key.</param>
/// <param name="Code">The identifier it sends - its MAC address. Not editable.</param>
/// <param name="DisplayName">The owner's name for it.</param>
/// <param name="GasStationId">Its station; null while unassigned.</param>
/// <param name="IsActive">Whether it is in service.</param>
/// <param name="FirstSeenAt">When its first message arrived.</param>
/// <param name="LastSeenAt">When its newest processed message arrived.</param>
/// <param name="TotalUses">Uses since it was first seen.</param>
/// <param name="RowVersion">The version an edit must start from.</param>
/// <param name="NoActivityAfterHours">Its own no-activity threshold; null for the default.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record MachineSettingsDto(
    Guid Id, string Code, string? DisplayName, Guid? GasStationId, bool IsActive,
    DateTimeOffset FirstSeenAt, DateTimeOffset? LastSeenAt, long TotalUses, string RowVersion, int? NoActivityAfterHours = null);

/// <summary>A change to a machine's settings.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed record UpdateMachineRequest
{
    /// <summary>Gets the machine.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the owner's name for it; blank clears it.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets its station; null leaves it unassigned.</summary>
    public Guid? GasStationId { get; init; }

    /// <summary>Gets a value indicating whether it is in service.</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Gets after how many hours without a message it raises a no-activity alert; null for the
    /// configured default.
    /// </summary>
    public int? NoActivityAfterHours { get; init; }

    /// <summary>Gets the version the edit started from.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>Names, assigns and retires machines.</summary>
/// <remarks>Layer: Application. Super Admins only.</remarks>
public interface IMachineAdminService
{
    /// <summary>Returns a machine's settings.</summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The settings.</returns>
    Task<MachineSettingsDto> GetAsync(Guid machineId, CancellationToken cancellationToken = default);

    /// <summary>Lists the machines that have reported but belong to no station yet, newest first.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The inbox.</returns>
    Task<IReadOnlyList<MachineSettingsDto>> ListUnassignedAsync(CancellationToken cancellationToken = default);

    /// <summary>Changes a machine's settings.</summary>
    /// <param name="request">The change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The settings as saved.</returns>
    Task<MachineSettingsDto> UpdateAsync(UpdateMachineRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IMachineAdminService"/>.</summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>The inbox is the defence against spoofed ids.</b> Any holder of the shared machine key can
/// invent a machine id; such a machine appears here and nowhere a station user can see it until the
/// owner assigns it. Telemetry never writes the machine row, so an edit here never conflicts with a
/// message - only with another admin's edit, which is a clean 409.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="currentUser">Must be a Super Admin.</param>
/// <param name="validator">Validates the change.</param>
/// <param name="localizer">Messages.</param>
/// <param name="settingsSignal">Tells the worker the machine's settings may have changed - it may have moved station.</param>
public sealed class MachineAdminService(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    IValidator<UpdateMachineRequest> validator,
    ILocalizationService localizer,
    IDeviceSettingsSignal settingsSignal) : IMachineAdminService
{
    /// <inheritdoc />
    public async Task<MachineSettingsDto> GetAsync(Guid machineId, CancellationToken cancellationToken = default)
    {
        AdminRules.EnsureSuperAdmin(currentUser);

        var machines = await unitOfWork.Machines
            .ListAsync(new MachineSettingsSpecification(machineId), Projection, cancellationToken)
            .ConfigureAwait(false);

        return machines.Count > 0
            ? ToDto(machines[0])
            : throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Machine), machineId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MachineSettingsDto>> ListUnassignedAsync(CancellationToken cancellationToken = default)
    {
        AdminRules.EnsureSuperAdmin(currentUser);

        var machines = await unitOfWork.Machines
            .ListAsync(new MachineSettingsSpecification(null, unassignedOnly: true), Projection, cancellationToken)
            .ConfigureAwait(false);

        return [.. machines.Select(ToDto)];
    }

    /// <inheritdoc />
    public async Task<MachineSettingsDto> UpdateAsync(UpdateMachineRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AdminRules.EnsureSuperAdmin(currentUser);
        await validator.ValidateAndThrowLocalizedAsync(request, cancellationToken).ConfigureAwait(false);

        var machine = await unitOfWork.Machines.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false)
                      ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Machine), request.Id);
        AdminRules.EnsureUnchanged(machine.RowVersion, request.RowVersion);

        if (request.GasStationId is { } stationId && stationId != machine.GasStationId)
        {
            var station = await unitOfWork.Repository<GasStation, Guid>().GetByIdAsync(stationId, cancellationToken).ConfigureAwait(false);
            if (station is not { IsActive: true })
            {
                throw new Exceptions.ValidationException(nameof(UpdateMachineRequest.GasStationId), localizer.GetString(ResourceKeys.Validation.StationInactive));
            }
        }

        machine.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        machine.GasStationId = request.GasStationId;
        machine.IsActive = request.IsActive;
        machine.NoActivityAfterHours = request.NoActivityAfterHours;

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        settingsSignal.Request();
        return await GetAsync(machine.Id, cancellationToken).ConfigureAwait(false);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Machine, MachineRow>> Projection = machine => new MachineRow(
        machine.Id,
        machine.Code,
        machine.DisplayName,
        machine.GasStationId,
        machine.IsActive,
        machine.CreatedAt,
        machine.State != null ? (DateTimeOffset?)machine.State.LastSeenAt : null,
        machine.State != null ? machine.State.TotalUses : 0L,
        machine.RowVersion,
        machine.NoActivityAfterHours);

    private static MachineSettingsDto ToDto(MachineRow row) => new(
        row.Id, row.Code, row.DisplayName, row.GasStationId, row.IsActive, row.FirstSeenAt, row.LastSeenAt, row.TotalUses,
        AdminRules.Token(row.RowVersion), row.NoActivityAfterHours);

    private sealed record MachineRow(
        Guid Id, string Code, string? DisplayName, Guid? GasStationId, bool IsActive,
        DateTimeOffset FirstSeenAt, DateTimeOffset? LastSeenAt, long TotalUses, byte[] RowVersion, int? NoActivityAfterHours);
}

/// <summary>Validates a change to a machine.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class UpdateMachineRequestValidator : AbstractValidator<UpdateMachineRequest>
{
    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public UpdateMachineRequestValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(request => request.DisplayName).Optional(localizer, ResourceKeys.Fields.DisplayName, 200);
        RuleFor(request => request.NoActivityAfterHours)
            .InclusiveBetween(1, 168).When(request => request.NoActivityAfterHours is not null)
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Range, localizer.GetString(ResourceKeys.Fields.NoActivityAfterHours), 1, 168));
    }
}

/// <summary>One machine, or the unassigned ones, newest first.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class MachineSettingsSpecification : BaseSpecification<Machine>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineId">One machine, or null for a list.</param>
    /// <param name="unassignedOnly">Whether to list only machines without a station.</param>
    public MachineSettingsSpecification(Guid? machineId, bool unassignedOnly = false)
    {
        if (machineId is { } id)
        {
            AddCriteria(machine => machine.Id == id);
        }

        if (unassignedOnly)
        {
            AddCriteria(machine => machine.GasStationId == null);
        }

        AddOrderBy(machine => machine.CreatedAt, descending: true);
    }
}
