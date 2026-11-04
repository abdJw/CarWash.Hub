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

/// <summary>A machine in the archive: deleted, with everything it recorded kept.</summary>
/// <param name="Id">The machine's key.</param>
/// <param name="Code">The identifier it sends - its MAC address.</param>
/// <param name="DisplayName">The owner's name for it.</param>
/// <param name="StationName">The station it stood at, in the caller's language; null if none, or if that station is gone too.</param>
/// <param name="DeletedAt">When it was deleted.</param>
/// <param name="DeletedBy">Who deleted it; null when unknown.</param>
/// <param name="LastSeenAt">When its newest processed message arrived - after <paramref name="DeletedAt"/> if it still reports.</param>
/// <param name="TotalUses">Uses since it was first seen.</param>
/// <param name="RowVersion">The version a restore must start from.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ArchivedMachineDto(
    Guid Id, string Code, string? DisplayName, string? StationName, DateTimeOffset DeletedAt, string? DeletedBy,
    DateTimeOffset? LastSeenAt, long TotalUses, string RowVersion)
{
    /// <summary>Gets the name to show: the owner's, or the MAC address.</summary>
    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? Code : DisplayName;

    /// <summary>Gets a value indicating whether it has sent messages since it was deleted.</summary>
    public bool StillReporting => LastSeenAt > DeletedAt;
}

/// <summary>A machine to delete or restore, as of the version the admin saw.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed record MachineArchiveRequest
{
    /// <summary>Gets the machine.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the version the admin saw; a change since is a conflict.</summary>
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

    /// <summary>
    /// Deletes a machine: it moves to the archive. A soft delete - its messages, uses, refills and
    /// the money it earned are all kept, and it can be restored.
    /// </summary>
    /// <param name="request">The machine and the version the admin saw.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when it is archived.</returns>
    /// <exception cref="NotFoundException">No such machine, or it is already archived.</exception>
    /// <exception cref="ConflictException">Someone changed it since.</exception>
    Task DeleteAsync(MachineArchiveRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists the archived machines, most recently deleted first.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The archive.</returns>
    Task<IReadOnlyList<ArchivedMachineDto>> ListArchivedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores an archived machine to where it was: its station, or the inbox if that station has
    /// been deleted since.
    /// </summary>
    /// <param name="request">The machine and the version the admin saw.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The machine's settings as restored.</returns>
    /// <exception cref="NotFoundException">No such machine in the archive.</exception>
    /// <exception cref="ConflictException">Someone changed it since.</exception>
    Task<MachineSettingsDto> RestoreAsync(MachineArchiveRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IMachineAdminService"/>.</summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>Deleting archives.</b> A deleted machine is only flagged: the soft-delete filter takes it out
/// of every list, page and alert, while its history and revenue stay in the reports. Its id stays
/// taken - the code index is not filtered - so messages it still sends are stored against it, and
/// restoring it simply clears the flag.
/// </para>
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
/// <param name="logger">Logger.</param>
public sealed class MachineAdminService(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    IValidator<UpdateMachineRequest> validator,
    ILocalizationService localizer,
    IDeviceSettingsSignal settingsSignal,
    ILogger<MachineAdminService> logger) : IMachineAdminService
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

    /// <inheritdoc />
    public async Task DeleteAsync(MachineArchiveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AdminRules.EnsureSuperAdmin(currentUser);

        // The soft-delete filter applies: an archived machine is not found, so it cannot be deleted twice.
        var machine = await unitOfWork.Machines.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false)
                      ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Machine), request.Id);
        AdminRules.EnsureUnchanged(machine.RowVersion, request.RowVersion);

        // Removed, which the audit interceptor turns into the flag, the time and who did it. Its
        // open alerts close as no longer eligible at the worker's next check.
        unitOfWork.Machines.Remove(machine);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        settingsSignal.Request();

        logger.LogInformation("Machine {MachineCode} ({MachineId}) archived by {UserName}", machine.Code, machine.Id, currentUser.UserName);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArchivedMachineDto>> ListArchivedAsync(CancellationToken cancellationToken = default)
    {
        AdminRules.EnsureSuperAdmin(currentUser);

        var rows = await unitOfWork.Machines
            .ListAsync(
                new ArchivedMachinesSpecification(null),
                machine => new
                {
                    machine.Id,
                    machine.Code,
                    machine.DisplayName,
                    StationName = machine.GasStation != null && !machine.GasStation.IsDeleted ? machine.GasStation.Name : null,
                    StationNameAr = machine.GasStation != null && !machine.GasStation.IsDeleted ? machine.GasStation.NameAr : null,
                    machine.DeletedAt,
                    machine.DeletedBy,
                    LastSeenAt = machine.State != null ? (DateTimeOffset?)machine.State.LastSeenAt : null,
                    TotalUses = machine.State != null ? machine.State.TotalUses : 0L,
                    machine.RowVersion,
                },
                cancellationToken)
            .ConfigureAwait(false);

        var deleterIds = rows.Select(row => row.DeletedBy).OfType<Guid>().Distinct().ToList();
        var deleters = deleterIds.Count == 0
            ? []
            : (await unitOfWork.Repository<User, Guid>()
                    .ListAsync(new ArchiveUsersSpecification(deleterIds), user => new { user.Id, user.DisplayName }, cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(user => user.Id, user => user.DisplayName);

        return
        [
            .. rows.Select(row => new ArchivedMachineDto(
                row.Id,
                row.Code,
                row.DisplayName,
                row.StationName is null ? null : localizer.Localize(row.StationName, row.StationNameAr),
                row.DeletedAt ?? DateTimeOffset.MinValue,
                row.DeletedBy is { } by ? deleters.GetValueOrDefault(by) : null,
                row.LastSeenAt,
                row.TotalUses,
                AdminRules.Token(row.RowVersion))),
        ];
    }

    /// <inheritdoc />
    public async Task<MachineSettingsDto> RestoreAsync(MachineArchiveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AdminRules.EnsureSuperAdmin(currentUser);

        var machine = await unitOfWork.Machines
                          .FirstOrDefaultAsync(new ArchivedMachinesSpecification(request.Id, tracked: true), cancellationToken)
                          .ConfigureAwait(false)
                      ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Machine), request.Id);
        AdminRules.EnsureUnchanged(machine.RowVersion, request.RowVersion);

        // Back to its station - unless that station was deleted meanwhile, when the machine waits
        // in the inbox to be assigned again rather than pointing at a station no one can see.
        if (machine.GasStationId is { } stationId
            && !await unitOfWork.Repository<GasStation, Guid>().ExistsAsync(station => station.Id == stationId, cancellationToken).ConfigureAwait(false))
        {
            machine.GasStationId = null;
        }

        machine.IsDeleted = false;
        machine.DeletedAt = null;
        machine.DeletedBy = null;

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        settingsSignal.Request();

        logger.LogInformation("Machine {MachineCode} ({MachineId}) restored by {UserName}", machine.Code, machine.Id, currentUser.UserName);
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

/// <summary>The archived machines - all, or one - most recently deleted first.</summary>
/// <remarks>Layer: Application. Bypasses the soft-delete filter, then keeps only what it hid.</remarks>
public sealed class ArchivedMachinesSpecification : BaseSpecification<Machine>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineId">One machine, or null for the whole archive.</param>
    /// <param name="tracked">Whether the machine is to be restored.</param>
    public ArchivedMachinesSpecification(Guid? machineId, bool tracked = false)
        : base(machine => machine.IsDeleted)
    {
        IncludeSoftDeleted();

        if (machineId is { } id)
        {
            AddCriteria(machine => machine.Id == id);
        }

        if (tracked)
        {
            EnableTracking();
        }

        AddOrderBy(machine => machine.DeletedAt, descending: true);
    }
}

/// <summary>Users by id, deleted ones included - for who archived a machine.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class ArchiveUsersSpecification : BaseSpecification<User>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="ids">The users.</param>
    public ArchiveUsersSpecification(IReadOnlyCollection<Guid> ids)
        : base(user => ids.Contains(user.Id))
    {
        IncludeSoftDeleted();
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
