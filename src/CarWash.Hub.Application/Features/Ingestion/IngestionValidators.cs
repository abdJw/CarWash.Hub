using System.Text.Json;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using FluentValidation;

namespace CarWash.Hub.Application.Features.Ingestion;

/// <summary>Validates a machine's status message.</summary>
/// <remarks>
/// Layer: Application. Run by the Api's <c>ValidationFilter</c> before the action executes. The
/// error keys are the JSON names (<c>machineId</c>, <c>data</c>), so firmware reading the problem
/// document sees the same names it sent.
/// </remarks>
public sealed class IngestMessageRequestValidator : AbstractValidator<IngestMessageRequest>
{
    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public IngestMessageRequestValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(request => request.MachineId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Required, localizer.GetString(ResourceKeys.Fields.MachineId)))
            .Must(machineId => MachineIdRules.IsValid(machineId!.Trim()))
            .WithMessage(request => localizer.GetString(ResourceKeys.Ingestion.MachineIdInvalid, Shorten(request.MachineId!), MachineIdRules.MaxLength))
            .OverridePropertyName("machineId");

        RuleFor(request => request.Data)
            .Cascade(CascadeMode.Stop)
            .Must(HasContent)
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Required, localizer.GetString(ResourceKeys.Fields.Data)))
            .Must(data => StoredPayload.SizeOf(StoredPayload.From(data!.Value)) <= StoredPayload.MaxBytes)
            .WithMessage(_ => localizer.GetString(ResourceKeys.Ingestion.DataTooLarge, StoredPayload.MaxBytes))
            .OverridePropertyName("data");
    }

    // Missing, null, or a blank string all mean "no status was sent".
    private static bool HasContent(JsonElement? data) =>
        data is { } value
        && value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
        && (value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString()));

    // Echoed values are capped, so an attacker-sized "id" cannot bloat the error response.
    private static string Shorten(string value) =>
        value.Length <= MachineIdRules.MaxLength + 20 ? value : string.Concat(value.AsSpan(0, MachineIdRules.MaxLength + 20), "…");
}
