using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CarWash.Hub.Application.Common.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CarWash.Hub.Infrastructure.Http;

/// <summary>
/// Writes RFC 7807 problem documents from outside the MVC result pipeline.
/// </summary>
/// <remarks>
/// Layer: Infrastructure.
/// <para>
/// Authentication challenges reject requests before MVC has a result pipeline to execute an
/// <c>ObjectResult</c> through, so the client-key handler must write the response itself. Sharing
/// this writer keeps a 401 for a missing <c>client-secret-key</c> structurally identical to every
/// other refusal - a machine's firmware parses one shape for every error.
/// </para>
/// <para>
/// A challenge that wrote nothing would produce an empty 401 at best, and with some handler
/// configurations an empty <c>200 OK</c> - which firmware checking the status code alone would
/// read as success.
/// </para>
/// </remarks>
public static class ProblemResponseWriter
{
    private static readonly JsonSerializerOptions ProblemJsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Matches the MVC pipeline's encoder so a problem document renders Arabic as Arabic,
        // exactly like every other response.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>
    /// Writes a problem document, unless the response has already begun.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="statusCode">The status to return.</param>
    /// <param name="detail">The already-localized message.</param>
    /// <param name="title">A short, non-localized label for the problem type.</param>
    /// <param name="code">The machine-readable code, usually the resource key of the message.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string detail,
        string title,
        string? code = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Response.HasStarted)
        {
            // The headers are already on the wire. Overwriting would throw and replace a
            // meaningful failure with a confusing one.
            return;
        }

        var correlationId = context.Items.TryGetValue(HttpContextItemKeys.CorrelationId, out var value)
            ? value as string
            : null;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = TypeUriFor(statusCode),
            Instance = context.Request.Path,
        };

        // Carried in the body as well as the X-Correlation-Id header, so a client that logs
        // only the payload still captures the value that finds the request in the server logs.
        problem.Extensions["correlationId"] = correlationId;

        // The same language-independent code the exception handler emits, so a client branches
        // on one field however the refusal was produced.
        if (code is not null)
        {
            problem.Extensions["code"] = code;
        }

        // Content-Language and X-Correlation-Id are set on the way in and must survive the
        // clear; see ResponseHeaderPreserver in the Api layer for the full reasoning.
        var contentLanguage = context.Response.Headers.ContentLanguage;
        var correlationHeader = context.Response.Headers[HttpHeaderNames.CorrelationId];

        context.Response.Clear();

        if (contentLanguage.Count > 0)
        {
            context.Response.Headers.ContentLanguage = contentLanguage;
        }

        if (correlationHeader.Count > 0)
        {
            context.Response.Headers[HttpHeaderNames.CorrelationId] = correlationHeader;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response
            .WriteAsync(JsonSerializer.Serialize(problem, ProblemJsonOptions), context.RequestAborted)
            .ConfigureAwait(false);
    }

    private static string TypeUriFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status401Unauthorized => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        StatusCodes.Status403Forbidden => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
    };
}
