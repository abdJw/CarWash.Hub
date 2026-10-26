using CarWash.Hub.Application.Common.Constants;
using Serilog.Context;

namespace CarWash.Hub.Api.Middleware;

/// <summary>
/// Assigns every request a correlation identifier and threads it through logs and responses.
/// </summary>
/// <remarks>
/// Layer: Api.
/// <para>
/// Accepts an inbound <c>X-Correlation-Id</c> so a trace started by an upstream caller or a
/// gateway continues across service boundaries, and generates one when absent. The value is
/// echoed on the response, stashed in <c>HttpContext.Items</c> for
/// <c>ICurrentUserService</c> to read, and pushed onto Serilog's <c>LogContext</c>.
/// </para>
/// <para>
/// The <c>LogContext</c> push is what makes the identifier appear on <em>every</em> log line
/// written during the request rather than only the ones that remember to include it - and it
/// is the same value stored on every machine message, so a stored message can be matched to
/// the exact request that produced it.
/// </para>
/// <para>
/// Runs near the top of the pipeline, before the exception handler, so that a failure
/// response can quote the identifier the client should report.
/// </para>
/// </remarks>
/// <param name="next">The next middleware.</param>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>The log property name the identifier is published under.</summary>
    public const string LogPropertyName = "CorrelationId";

    /// <summary>Processes a request.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = ResolveCorrelationId(context);

        context.Items[HttpContextItemKeys.CorrelationId] = correlationId;

        // Registered on the response before anything writes to the body: once the response has
        // started, headers are already on the wire and mutating them throws.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HttpHeaderNames.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        var inbound = context.Request.Headers[HttpHeaderNames.CorrelationId].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(inbound))
        {
            return Guid.CreateVersion7().ToString("N");
        }

        // Bounded and restricted to an allow-list before use. The value is attacker-supplied
        // and ends up in log files and in a response header, so an unbounded or newline-bearing
        // value would permit log forging and header injection.
        //
        // Hyphens are permitted alongside letters and digits: dashed UUIDs and W3C trace
        // identifiers are the usual inbound format, and stripping the separators would mangle a
        // trace started upstream into something that no longer matches - defeating the purpose
        // of accepting an inbound identifier at all.
        var sanitised = new string([.. inbound.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').Take(64)]);

        return sanitised.Length == 0 ? Guid.CreateVersion7().ToString("N") : sanitised;
    }
}
