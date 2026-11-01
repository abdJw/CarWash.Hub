using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using FluentValidation;
using AppValidationException = CarWash.Hub.Application.Exceptions.ValidationException;

namespace CarWash.Hub.Application.Common.Validation;

/// <summary>
/// Validation for commands that do not come through MVC.
/// </summary>
/// <remarks>
/// Layer: Application. The API's <c>ValidationFilter</c> validates controller arguments, but the
/// dashboard calls services directly, so its commands validate themselves - with the same validators
/// and the same field-by-field errors.
/// </remarks>
public static class ValidationExtensions
{
    /// <summary>Validates, and throws the application's validation exception when invalid.</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="validator">The validator.</param>
    /// <param name="instance">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the request is valid.</returns>
    /// <exception cref="AppValidationException">The request is invalid; errors are keyed by property.</exception>
    public static async Task ValidateAndThrowLocalizedAsync<T>(this IValidator<T> validator, T instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var result = await validator.ValidateAsync(instance, cancellationToken).ConfigureAwait(false);
        if (result.IsValid)
        {
            return;
        }

        throw new AppValidationException(result.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal));
    }

    /// <summary>A required text field with a maximum length.</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="rule">The rule.</param>
    /// <param name="localizer">Resolves messages.</param>
    /// <param name="fieldKey">The field's resource key.</param>
    /// <param name="maxLength">The longest value accepted.</param>
    /// <returns>The rule, for chaining.</returns>
    public static IRuleBuilderOptions<T, string> Required<T>(this IRuleBuilder<T, string> rule, ILocalizationService localizer, string fieldKey, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        return rule
            .NotEmpty().WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Required, localizer.GetString(fieldKey)))
            .MaximumLength(maxLength).WithMessage(_ => localizer.GetString(ResourceKeys.Validation.MaxLength, localizer.GetString(fieldKey), maxLength));
    }

    /// <summary>An optional text field with a maximum length.</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="rule">The rule.</param>
    /// <param name="localizer">Resolves messages.</param>
    /// <param name="fieldKey">The field's resource key.</param>
    /// <param name="maxLength">The longest value accepted.</param>
    /// <returns>The rule, for chaining.</returns>
    public static IRuleBuilderOptions<T, string?> Optional<T>(this IRuleBuilder<T, string?> rule, ILocalizationService localizer, string fieldKey, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        return rule.MaximumLength(maxLength)
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.MaxLength, localizer.GetString(fieldKey), maxLength));
    }

    /// <summary>A password: required, 8 to 128 characters.</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="rule">The rule.</param>
    /// <param name="localizer">Resolves messages.</param>
    /// <returns>The rule, for chaining.</returns>
    public static IRuleBuilderOptions<T, string> Password<T>(this IRuleBuilder<T, string> rule, ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        return rule.Required(localizer, ResourceKeys.Fields.Password, 128)
            .MinimumLength(8).WithMessage(_ => localizer.GetString(ResourceKeys.Validation.MinLength, localizer.GetString(ResourceKeys.Fields.Password), 8));
    }
}
