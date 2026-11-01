using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using FluentValidation;

namespace CarWash.Hub.Application.Features.Usage;

/// <summary>Validates a usage window.</summary>
/// <remarks>
/// Layer: Application. The window is checked as it will be resolved - an open end is now - so a
/// query naming only a start a year ago is refused rather than silently clipped.
/// </remarks>
public sealed class UsageQueryValidator : AbstractValidator<UsageQuery>
{
    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    /// <param name="clock">Resolves an open window end.</param>
    public UsageQueryValidator(ILocalizationService localizer, IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(clock);

        RuleFor(query => query.From)
            .Must((query, _) =>
            {
                var (from, to) = query.Resolve(clock.UtcNow);
                return from <= to;
            })
            .WithMessage(_ => localizer.GetString(
                ResourceKeys.Validation.DateRange,
                localizer.GetString(ResourceKeys.Fields.From),
                localizer.GetString(ResourceKeys.Fields.To)));

        RuleFor(query => query.To)
            .Must((query, _) =>
            {
                var (from, to) = query.Resolve(clock.UtcNow);
                return to - from <= UsageQuery.MaxWindow;
            })
            .WithMessage(_ => localizer.GetString(
                ResourceKeys.Validation.RangeTooLong,
                localizer.GetString(ResourceKeys.Fields.From),
                localizer.GetString(ResourceKeys.Fields.To),
                (int)UsageQuery.MaxWindow.TotalDays));
    }
}
