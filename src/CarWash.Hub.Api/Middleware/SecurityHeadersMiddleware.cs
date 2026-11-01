namespace CarWash.Hub.Api.Middleware;

/// <summary>
/// Adds the security response headers: strict for the JSON API, and fitted to each of the two kinds
/// of HTML page the host also serves.
/// </summary>
/// <remarks>
/// Layer: Api.
/// <para>
/// The header set is chosen for an API, not copied from a web-page checklist. A
/// <c>Content-Security-Policy</c> that blocks everything is meaningful here precisely because
/// the API returns JSON - if one of its responses is ever rendered in a browser, it is because
/// something has gone wrong, and the policy limits what that can do.
/// </para>
/// <para>
/// The documentation UIs under <c>/swagger</c> and <c>/scalar</c> are the one exception. Those
/// pages are HTML by design and bootstrap themselves with inline script and style, so they get
/// a policy that permits same-origin assets and inline code while still forbidding framing and
/// every third-party origin. The rest of the header set is identical for both.
/// </para>
/// <para>
/// <b>The dashboard</b> - every path that is neither the API nor the documentation - gets a third
/// policy. Scripts only from this origin and never inline: the dashboard renders no inline script,
/// so an injected one cannot run. Inline styles are allowed, because MudBlazor sets style
/// attributes and the charts size their marks with them; styles cannot run code. The Blazor
/// connection's WebSocket is named explicitly in <c>connect-src</c>, because older Safari does not
/// match <c>wss:</c> to <c>'self'</c>. Dashboard HTML is also marked <c>no-store</c>: it shows a
/// signed-in user's data, which a shared or back-button cache must not keep. Static assets keep the
/// long-lived caching their fingerprinted URLs allow.
/// </para>
/// <para>
/// <c>X-XSS-Protection</c> is deliberately absent. It is obsolete, removed from modern browsers,
/// and its legacy filter could itself be abused; sending it signals diligence while adding risk.
/// </para>
/// <para>
/// <c>Strict-Transport-Security</c> (one year, sub-domains included) goes on HTTPS responses
/// outside Development, so a browser never again tries the API over plain
/// HTTP. Development is left out: a developer's localhost must stay free to serve HTTP.
/// </para>
/// </remarks>
/// <param name="next">The next middleware.</param>
/// <param name="environment">The hosting environment.</param>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    /// <summary>The HSTS policy: one year, sub-domains included.</summary>
    public const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    // Nothing is allowed to load, and nothing may frame it. An API serving only JSON has no
    // legitimate need for any of it.
    private const string ApiPolicy = "default-src 'none'; frame-ancestors 'none'";

    // Swagger UI and Scalar load their bundles from this origin, inject inline styles, fetch
    // the OpenAPI documents and "try it out" responses over XHR, and embed icons as data:
    // URIs; Scalar also bootstraps itself with an inline module script. No other origin is
    // permitted - Scalar's optional calls to fonts.scalar.com and api.scalar.com are blocked
    // on purpose.
    private const string DocumentationPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'";

    private static readonly PathString[] DocumentationPaths = ["/swagger", "/scalar"];

    // Machines and API clients: everything here is JSON (or the health probes).
    private static readonly PathString[] ApiPaths = ["/api", "/health"];

    /// <summary>Processes a request.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = context.Request.Path;
        var isDashboard = !StartsWithAny(path, ApiPaths) && !StartsWithAny(path, DocumentationPaths);
        var contentSecurityPolicy = isDashboard
            ? DashboardPolicy(context.Request)
            : StartsWithAny(path, DocumentationPaths) ? DocumentationPolicy : ApiPolicy;
        var strictTransport = context.Request.IsHttps && !environment.IsDevelopment();

        // Registered before the response starts. Setting headers after that point throws,
        // which is why this uses OnStarting rather than writing them directly.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Stops a browser second-guessing the declared content type. Without it, a JSON
            // response containing attacker-supplied text could be sniffed as HTML and executed.
            headers["X-Content-Type-Options"] = "nosniff";

            // This API is never meant to be framed. Largely redundant next to the CSP below,
            // kept for clients that predate frame-ancestors.
            headers["X-Frame-Options"] = "DENY";

            headers["Content-Security-Policy"] = contentSecurityPolicy;

            if (isDashboard && context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
            {
                headers.CacheControl = "no-store";
            }

            if (strictTransport)
            {
                headers["Strict-Transport-Security"] = StrictTransportSecurity;
            }

            // Keeps the API's URLs - which carry ids - out of Referer headers sent to third
            // parties when a response is opened in a browser.
            headers["Referrer-Policy"] = "no-referrer";

            // Denies device APIs that a JSON endpoint could never need.
            headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), microphone=(), payment=()";

            // Leaks the server implementation and version. Removing it is not a defence in
            // itself, but there is no reason to publish it.
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        });

        return next(context);
    }

    // Segment-aware, so /swagger and /swagger/index.html match but /swaggerish would not.
    private static bool StartsWithAny(PathString path, PathString[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (path.StartsWithSegments(prefix))
            {
                return true;
            }
        }

        return false;
    }

    // The WebSocket origin is this request's own host, over wss when the page is https. Development
    // also allows the browser-refresh socket that `dotnet watch` opens on another local port.
    private string DashboardPolicy(HttpRequest request)
    {
        var socket = $"{(request.IsHttps ? "wss" : "ws")}://{request.Host}";
        var connect = environment.IsDevelopment() ? $"'self' {socket} ws://localhost:* wss://localhost:*" : $"'self' {socket}";

        return "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; " +
               "script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; " +
               $"connect-src {connect}; worker-src 'self'; manifest-src 'self'; media-src 'self'";
    }
}
