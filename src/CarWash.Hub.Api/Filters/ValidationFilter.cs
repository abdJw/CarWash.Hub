using CarWash.Hub.Application.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using ValidationException = CarWash.Hub.Application.Exceptions.ValidationException;

namespace CarWash.Hub.Api.Filters;

/// <summary>
/// Runs FluentValidation over every action argument before the action executes.
/// </summary>
/// <remarks>
/// Layer: Api.
/// <para>
/// A filter rather than <c>ModelState</c> checks inside each action. Three consequences worth
/// naming: an action can never run with invalid input because someone forgot the check; the
/// failure shape is identical everywhere; and a new endpoint is validated the moment a validator
/// for its request type exists, with no wiring of its own.
/// </para>
/// <para>
/// It resolves validators from the container rather than constructing them, because the
/// validators take <c>ILocalizationService</c> - their messages are resolved in the caller's
/// language, not in English and translated afterwards.
/// </para>
/// <para>
/// Failures are raised as <see cref="ValidationException"/> rather than written here directly,
/// so the exception-handling middleware remains the single place a response body is shaped. A
/// filter writing its own response would be a second error format to keep in step.
/// </para>
/// </remarks>
/// <param name="serviceProvider">Resolves validators for the argument types encountered.</param>
public sealed class ValidationFilter(IServiceProvider serviceProvider) : IAsyncActionFilter
{
    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var failures = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            // No validator registered for this type is the normal case - route parameters,
            // cancellation tokens and primitives all land here. Not an error.
            if (serviceProvider.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator
                .ValidateAsync(validationContext, context.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (result.IsValid)
            {
                continue;
            }

            foreach (var failure in result.Errors)
            {
                // Grouped by property, so a field with several problems reports all of them at
                // once. Reporting only the first would make a caller fix and resubmit repeatedly.
                if (!failures.TryGetValue(failure.PropertyName, out var messages))
                {
                    messages = [];
                    failures[failure.PropertyName] = messages;
                }

                messages.Add(failure.ErrorMessage);
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(
                failures.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
        }

        await next().ConfigureAwait(false);
    }
}
