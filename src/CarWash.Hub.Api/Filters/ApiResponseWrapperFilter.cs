using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CarWash.Hub.Api.Filters;

/// <summary>
/// Names the localized message an endpoint's successful response should carry.
/// </summary>
/// <remarks>
/// Layer: Api. Optional - without it an endpoint gets the generic success message.
/// </remarks>
/// <param name="messageKey">A key from <c>ResourceKeys.Messages</c>.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ResponseMessageAttribute(string messageKey) : Attribute
{
    /// <summary>Gets the resource key for the message.</summary>
    public string MessageKey { get; } = messageKey;
}

/// <summary>
/// Wraps successful action results in <see cref="ApiResponse{T}"/>.
/// </summary>
/// <remarks>
/// Layer: Api.
/// <para>
/// A result filter rather than a base-controller method or a change to every action. Controllers
/// keep returning <c>Ok(dto)</c> and know nothing about the envelope, so its shape can change
/// without editing a single action - which is the whole point of "applied uniformly".
/// </para>
/// <para>
/// <b>What it deliberately leaves alone:</b> anything that is not a 2xx <c>ObjectResult</c>.
/// Problem documents, file downloads and redirects pass through untouched. Wrapping a
/// <c>ProblemDetails</c> would destroy a standard error format that clients already parse, and
/// wrapping a file stream would corrupt it.
/// </para>
/// </remarks>
/// <param name="localizer">Resolves the message for the current culture.</param>
public sealed class ApiResponseWrapperFilter(ILocalizationService localizer) : IAsyncResultFilter
{
    /// <inheritdoc />
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (TryWrap(context, out var wrapped) && wrapped is not null)
        {
            context.Result = wrapped;
        }

        await next().ConfigureAwait(false);
    }

    // Returns true only when the result must be REPLACED (a bare status code gains a body). An
    // ObjectResult is wrapped in place and returns false, so its own behaviour - Location on a
    // CreatedAtActionResult - survives.
    private bool TryWrap(ResultExecutingContext context, out IActionResult? wrapped)
    {
        wrapped = null;

        var correlationId = context.HttpContext.Items
            .TryGetValue(HttpContextItemKeys.CorrelationId, out var value)
            ? value as string
            : null;

        var messageKey =
            (context.HttpContext.Items.TryGetValue(HttpContextItemKeys.ResponseMessageKey, out var overrideKey)
                ? overrideKey as string
                : null)
            ?? context.ActionDescriptor.EndpointMetadata.OfType<ResponseMessageAttribute>().FirstOrDefault()?.MessageKey
            ?? ResourceKeys.Messages.Success;

        var message = localizer.GetString(messageKey);

        switch (context.Result)
        {
            // 204 No Content carries no body by definition, so there is nothing to wrap. Giving
            // it one would change the status code's meaning and break clients that treat 204 as
            // "read nothing".
            case NoContentResult:
                return false;

            case ObjectResult objectResult:
                var status = objectResult.StatusCode ?? StatusCodes.Status200OK;

                // Not a success, or already a problem document: leave it exactly as it is.
                if (status is < 200 or >= 300 || objectResult.Value is ProblemDetails)
                {
                    return false;
                }

                // Already wrapped - a controller that built the envelope itself, or a second
                // pass over the same result. Wrapping again would nest the envelope.
                if (objectResult.Value?.GetType() is { IsGenericType: true } type
                    && type.GetGenericTypeDefinition() == typeof(ApiResponse<>))
                {
                    return false;
                }

                // Changed in place, never replaced. CreatedAtActionResult writes its Location
                // header in its own OnFormatting override; swapping it for a plain ObjectResult -
                // as cyberani_oms_api's version of this filter did - silently drops Location from
                // every 201. DeclaredType is cleared so the envelope, not the DTO, is serialized.
                objectResult.Value = BuildEnvelope(objectResult.Value, message, correlationId);
                objectResult.DeclaredType = null;
                objectResult.StatusCode = status;
                return false;

            case StatusCodeResult statusCodeResult
                when statusCodeResult.StatusCode is >= 200 and < 300:
                wrapped = new ObjectResult(ApiResponse.Ok(message, correlationId))
                {
                    StatusCode = statusCodeResult.StatusCode,
                };
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Builds a closed <c>ApiResponse&lt;T&gt;</c> around a payload whose type is only known at
    /// run time.
    /// </summary>
    /// <remarks>
    /// Reflection is unavoidable here: the filter sees <c>object</c>, but the envelope must be
    /// serialized as <c>ApiResponse&lt;TPayload&gt;</c> so the JSON carries the payload's own
    /// shape rather than <c>object</c>'s. Using <c>ApiResponse&lt;object&gt;</c> throughout would
    /// be simpler but would make the OpenAPI schema say every endpoint returns an untyped blob.
    /// </remarks>
    private static object BuildEnvelope(object? payload, string message, string? correlationId)
    {
        if (payload is null)
        {
            return ApiResponse.Ok(message, correlationId);
        }

        var envelopeType = typeof(ApiResponse<>).MakeGenericType(payload.GetType());
        var factory = envelopeType.GetMethod(nameof(ApiResponse<object>.Ok))!;

        return factory.Invoke(null, [payload, message, correlationId])!;
    }
}
