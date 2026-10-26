using System.Diagnostics;
using CarWash.Hub.Api.Options;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Api.Middleware;

/// <summary>
/// Logs one structured line per request: method, path, status, duration, key id and address.
/// </summary>
/// <remarks>
/// Layer: Api.
/// <para>
/// <b>What it never logs:</b> headers and bodies. The client keys travel in headers, and the bodies
/// are already stored in the database. The query string is logged with any parameter named like a
/// secret (<see cref="SensitiveHeaders"/>) redacted, because a key pasted into a URL by mistake
/// must not survive in a log file.
/// </para>
/// <para>
/// Properties are logged structurally (<c>{StatusCode}</c>, <c>{ClientKeyId}</c>) rather than
/// interpolated, so a log store can filter on "every 401 in the last hour" without parsing text.
/// </para>
/// </remarks>
/// <param name="next">The next middleware.</param>
/// <param name="logger">Logger.</param>
/// <param name="options">Request logging configuration.</param>
public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger,
    IOptions<RequestLoggingOptions> options)
{
    private readonly RequestLoggingOptions _options = options.Value;

    /// <summary>Processes a request.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="currentUser">Describes the caller, once the endpoint's policy has authenticated it.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    public async Task InvokeAsync(HttpContext context, ICurrentUserService currentUser)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(currentUser);

        if (!_options.Enabled || IsExcluded(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Stopwatch.GetTimestamp rather than a Stopwatch instance: no allocation per request.
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var statusCode = context.Response.StatusCode;

            // Severity follows the outcome, so a search for warnings surfaces real problems: server
            // faults and slow requests. A 4xx is a client being told "no" - information.
            var level = statusCode >= StatusCodes.Status500InternalServerError
                ? LogLevel.Error
                : elapsed > _options.SlowRequestThresholdMs
                    ? LogLevel.Warning
                    : LogLevel.Information;

            logger.Log(
                level,
                "HTTP {Method} {Path}{QueryString} responded {StatusCode} in {DurationMs:0.0} ms (key {ClientKeyId}, ip {IpAddress})",
                context.Request.Method,
                context.Request.Path.Value,
                RedactQueryString(context.Request.QueryString.Value),
                statusCode,
                elapsed,
                currentUser.ClientKeyId,
                currentUser.IpAddress);
        }
    }

    private bool IsExcluded(PathString path) =>
        path.HasValue
        && _options.ExcludedPathPrefixes.Any(prefix => path.Value!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string RedactQueryString(string? queryString)
    {
        if (string.IsNullOrEmpty(queryString) || queryString == "?")
        {
            return string.Empty;
        }

        var pairs = queryString.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        var rebuilt = pairs.Select(pair =>
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                return pair;
            }

            var key = Uri.UnescapeDataString(pair[..separator]);
            return SensitiveHeaders.IsSensitive(key) ? $"{pair[..separator]}={SensitiveHeaders.RedactedPlaceholder}" : pair;
        });

        return $"?{string.Join('&', rebuilt)}";
    }
}
