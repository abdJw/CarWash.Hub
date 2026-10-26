using CarWash.Hub.Application.Common.Constants;
using Microsoft.Net.Http.Headers;

namespace CarWash.Hub.Api.Middleware;

/// <summary>
/// Discards a partially-built response without losing the headers that describe it.
/// </summary>
/// <remarks>
/// Layer: Api.
/// <para>
/// <c>HttpResponse.Clear()</c> removes <em>every</em> header, which is right for the ones the
/// aborted response set and wrong for the ones the pipeline set on the way in. Two matter:
/// </para>
/// <list type="bullet">
///   <item><description><c>Content-Language</c>, written by the request-localization middleware.
///   Losing it means an Arabic error body arrives claiming no language, so a client cannot tell
///   whether its requested culture was honoured.</description></item>
///   <item><description><c>X-Correlation-Id</c>, the one value a caller can quote to have the
///   failure found in the logs - and never more wanted than on an error.</description></item>
/// </list>
/// <para>
/// This was found by observing that no error response carried <c>Content-Language</c> despite
/// localization working correctly: the header was being set and then cleared away.
/// </para>
/// </remarks>
public static class ResponseHeaderPreserver
{
    /// <summary>
    /// Clears the response, then restores the diagnostic headers.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public static void ClearPreservingDiagnostics(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var contentLanguage = context.Response.Headers[HeaderNames.ContentLanguage];
        var correlationId = context.Response.Headers[HttpHeaderNames.CorrelationId];

        context.Response.Clear();

        if (contentLanguage.Count > 0)
        {
            context.Response.Headers[HeaderNames.ContentLanguage] = contentLanguage;
        }

        if (correlationId.Count > 0)
        {
            context.Response.Headers[HttpHeaderNames.CorrelationId] = correlationId;
        }
    }
}
