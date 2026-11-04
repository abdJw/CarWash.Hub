using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Features.Ingestion;
using CarWash.Hub.Domain.Entities;
using FluentValidation;

namespace CarWash.Hub.Application.Features.DeviceSettings;

/// <summary>A machine's settings poll: <c>GET /api/v1/settings?machineId=…</c>.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed record DeviceSettingsQuery
{
    /// <summary>Gets the machine asking - its MAC address, as it sends it in every message.</summary>
    public string? MachineId { get; init; }
}

/// <summary>Answers a machine's settings poll.</summary>
/// <remarks>Layer: Application.</remarks>
public interface IDeviceSettingsService
{
    /// <summary>Returns the settings document a machine should run, as the bytes to send it.</summary>
    /// <param name="query">The poll, already validated by <see cref="DeviceSettingsQueryValidator"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document; <c>{"revision":0}</c> when the machine has no desired settings.</returns>
    Task<ReadOnlyMemory<byte>> GetDocumentAsync(DeviceSettingsQuery query, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IDeviceSettingsService"/>.</summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>A pure read.</b> The answer is whatever the worker last issued: one cached machine lookup and
/// one row read by key, every 15 seconds per machine. An unknown machine is not registered by
/// asking, and is simply told there is nothing to apply - a normal case, never a 404.
/// </para>
/// <para>
/// The machine identifier is not a credential: anyone holding the fleet's key can read any
/// machine's settings. They are prices and run times, nothing secret, and writing still needs the
/// dashboard.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
public sealed class DeviceSettingsService(IUnitOfWork unitOfWork) : IDeviceSettingsService
{
    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>> GetDocumentAsync(DeviceSettingsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var code = query.MachineId?.Trim()
            ?? throw new ArgumentException("MachineId is required.", nameof(query));

        if (await unitOfWork.Machines.FindRegistrationAsync(code, cancellationToken).ConfigureAwait(false) is not { } machine)
        {
            return DeviceSettingsDocument.NoSettings;
        }

        var rows = await unitOfWork.Repository<MachineDeviceSettings, Guid>()
            .ListAsync(
                new DeviceSettingsRowsSpecification([machine.Id], tracked: false),
                row => new { row.DesiredRevision, row.ServicesJson },
                cancellationToken)
            .ConfigureAwait(false);

        return rows.Count == 1 && rows[0] is { DesiredRevision: > 0, ServicesJson: { } json }
            ? DeviceSettingsDocument.Write(rows[0].DesiredRevision, json)
            : DeviceSettingsDocument.NoSettings;
    }
}

/// <summary>Validates a settings poll.</summary>
/// <remarks>
/// Layer: Application. Run by the Api's <c>ValidationFilter</c>. The firmware always sends its own
/// MAC address, so a refusal here is a configuration error - which is what a 400 means to it.
/// </remarks>
public sealed class DeviceSettingsQueryValidator : AbstractValidator<DeviceSettingsQuery>
{
    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public DeviceSettingsQueryValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(query => query.MachineId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Required, localizer.GetString(ResourceKeys.Fields.MachineId)))
            .Must(machineId => MachineIdRules.IsValid(machineId!.Trim()))
            .WithMessage(query => localizer.GetString(
                ResourceKeys.Ingestion.MachineIdInvalid,
                query.MachineId!.Length <= 40 ? query.MachineId : query.MachineId[..40] + "…",
                MachineIdRules.MaxLength))
            .OverridePropertyName("machineId");
    }
}
