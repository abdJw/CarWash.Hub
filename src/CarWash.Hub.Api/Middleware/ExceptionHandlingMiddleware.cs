using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace CarWash.Hub.Api.Middleware;

/// <summary>
/// Converts unhandled exceptions into localized RFC 7807 problem documents.
/// </summary>
/// <remarks>
/// Layer: Api.
/// <para>
/// Exceptions deriving from <see cref="AppException"/> carry their own status code and a
/// resource key, so mapping is a property read rather than a chain of type checks that grows
/// with every feature. Anything else is an unhandled fault and becomes a 500.
/// </para>
/// <para>
/// <b>Stack traces appear only in Development.</b> In any other environment the response
/// carries the correlation id instead, which is enough to find the full exception in the logs
/// without handing an attacker a map of the internals.
/// </para>
/// <para>
/// <b>Server-level request errors are mapped too.</b> A body over <c>[RequestSizeLimit]</c> makes
/// Kestrel throw <see cref="BadHttpRequestException"/> with status 413 when the body is read.
/// cyberani_oms_api's version of this middleware had no case for it, so the client received a
/// 500 logged as a server fault; here it is the 413 (or 400) the exception carries.
/// </para>
/// </remarks>
/// <param name="next">The next middleware.</param>
/// <param name="localizer">Resolves messages for the current culture.</param>
/// <param name="environment">Used to decide whether diagnostics may be disclosed.</param>
/// <param name="logger">Logger.</param>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    IStringLocalizer<Application.SharedResource> localizer,
    IHostEnvironment environment,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions ProblemJsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Matches the MVC pipeline's encoder so a problem document renders Arabic as Arabic,
        // exactly like every other response.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>Processes a request.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items.TryGetValue(HttpContextItemKeys.CorrelationId, out var value)
            ? value as string
            : null;

        if (context.Response.HasStarted)
        {
            // The response is already on the wire, so it cannot be replaced. Logged rather than
            // rethrown, because rethrowing here would abort the connection mid-body and give
            // the client a truncated payload with no explanation.
            logger.LogError(exception,
                "An exception was thrown after the response had started. Correlation {CorrelationId}.",
                correlationId);
            return;
        }

        var problem = BuildProblem(exception, correlationId, out var logLevel);

        logger.Log(logLevel, exception,
            "Request {Method} {Path} failed with {StatusCode}. Correlation {CorrelationId}.",
            context.Request.Method, context.Request.Path.Value, problem.Status, correlationId);

        ResponseHeaderPreserver.ClearPreservingDiagnostics(context);

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        // Serialized against problem.GetType(), not the declared type. System.Text.Json writes
        // only the DECLARED type's properties, so serializing a ValidationProblemDetails through
        // a ProblemDetails-typed variable silently drops its "errors" dictionary - leaving the
        // caller a 400 that never says which field was wrong.
        await context.Response
            .WriteAsync(JsonSerializer.Serialize(problem, problem.GetType(), ProblemJsonOptions), context.RequestAborted)
            .ConfigureAwait(false);
    }

    private ProblemDetails BuildProblem(Exception exception, string? correlationId, out LogLevel logLevel)
    {
        ProblemDetails problem;

        switch (exception)
        {
            case ValidationException validation:
                // Expected in normal operation - a client sent something invalid. Logged at
                // Information so a log search for warnings surfaces real faults, not typos.
                logLevel = LogLevel.Information;
                problem = new ValidationProblemDetails(
                    validation.Errors.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                {
                    Status = validation.StatusCode,
                    Title = "Validation failed",
                    Detail = Localize(validation),
                };
                problem.Extensions["code"] = validation.MessageKey;
                break;

            case AppException app:
                logLevel = app.StatusCode >= StatusCodes.Status500InternalServerError
                    ? LogLevel.Error
                    : LogLevel.Information;
                problem = new ProblemDetails
                {
                    Status = app.StatusCode,
                    Title = app.GetType().Name.Replace("Exception", string.Empty, StringComparison.Ordinal),
                    Detail = Localize(app),
                };

                // The resource key doubles as a stable, language-independent code the client can
                // branch on, for example "Error_NotFound" -> check the machine id.
                problem.Extensions["code"] = app.MessageKey;
                foreach (var (name, extension) in app.Extensions ?? new Dictionary<string, object?>())
                {
                    problem.Extensions[name] = extension;
                }

                break;

            case DomainRuleException rule:
                // An aggregate refused a transition. The same 422 a service-level
                // BusinessRuleException produces, so the client cannot tell - and need not care -
                // which layer enforced the rule.
                logLevel = LogLevel.Information;
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Title = "BusinessRule",
                    Detail = rule.Arguments.Count > 0
                        ? localizer[rule.Code, [.. rule.Arguments]]
                        : localizer[rule.Code],
                };
                problem.Extensions["code"] = rule.Code;
                break;

            case BadHttpRequestException badRequest:
                // Fix B. Raised by the server itself while reading the request - most often 413
                // when a body exceeds [RequestSizeLimit]. The client's mistake, not a fault, and the
                // exception already carries the right status.
                logLevel = LogLevel.Information;
                var tooLarge = badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge;
                var key = tooLarge ? ResourceKeys.Errors.RequestTooLarge : ResourceKeys.Errors.BadRequest;
                problem = new ProblemDetails
                {
                    Status = badRequest.StatusCode,
                    Title = tooLarge ? "Payload too large" : "Bad request",
                    Detail = localizer[key],
                };
                problem.Extensions["code"] = key;
                break;

            case OperationCanceledException:
                // The client went away. Not a fault, and 499 is the conventional code for it.
                logLevel = LogLevel.Debug;
                problem = new ProblemDetails
                {
                    Status = 499,
                    Title = "Client closed request",
                    Detail = "The request was cancelled by the client.",
                };
                break;

            default:
                logLevel = LogLevel.Error;
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Internal server error",

                    // The correlation id is substituted into the message so a caller reporting
                    // a failure quotes the one value that finds it in the logs.
                    Detail = localizer[ResourceKeys.Errors.Unexpected, correlationId ?? "unknown"],
                };

                if (environment.IsDevelopment())
                {
                    problem.Extensions["exception"] = exception.GetType().FullName;
                    problem.Extensions["stackTrace"] = exception.ToString();
                }

                break;
        }

        problem.Extensions["correlationId"] = correlationId;
        return problem;
    }

    /// <summary>Resolves an exception's resource key for the current culture.</summary>
    private string Localize(AppException exception) =>
        exception.MessageArguments.Count > 0
            ? localizer[exception.MessageKey, [.. exception.MessageArguments]]
            : localizer[exception.MessageKey];
}
