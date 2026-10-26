using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using FluentValidation;

namespace CarWash.Hub.Application.Features.Machines;

/// <summary>Validates the machine list filters.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class MachineListQueryValidator : AbstractValidator<MachineListQuery>
{
    /// <summary>The longest search term accepted.</summary>
    public const int SearchTermMaxLength = 100;

    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public MachineListQueryValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(query => query.SearchTerm)
            .MaximumLength(SearchTermMaxLength)
            .WithMessage(_ => localizer.GetString(
                ResourceKeys.Validation.MaxLength, localizer.GetString(ResourceKeys.Fields.MachineId), SearchTermMaxLength));
    }
}

/// <summary>Validates a history window.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class MachineHistoryQueryValidator : AbstractValidator<MachineHistoryQuery>
{
    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public MachineHistoryQueryValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        // A backwards window would silently return nothing; saying so is more useful.
        RuleFor(query => query.From)
            .Must((query, from) => from is null || query.To is null || from <= query.To)
            .WithMessage(_ => localizer.GetString(
                ResourceKeys.Validation.DateRange,
                localizer.GetString(ResourceKeys.Fields.From),
                localizer.GetString(ResourceKeys.Fields.To)));
    }
}
