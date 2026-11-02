using CarWash.Hub.Web.Auth;
using CarWash.Hub.Web.Components;
using CarWash.Hub.Web.Reports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace CarWash.Hub.Web;

/// <summary>
/// Adds the dashboard to the host's pipeline and endpoints.
/// </summary>
/// <remarks>Layer: Web.</remarks>
public static class DashboardEndpointExtensions
{
    private static readonly PathString[] NonDashboardPaths = ["/api", "/health", "/swagger", "/scalar"];

    /// <summary>Gets a value indicating whether a path belongs to the dashboard rather than the API.</summary>
    /// <param name="path">The request path.</param>
    /// <returns><see langword="true"/> for dashboard pages, assets and the Blazor connection.</returns>
    public static bool IsDashboardPath(PathString path) => !NonDashboardPaths.Any(prefix => path.StartsWithSegments(prefix));

    /// <summary>
    /// Shows dashboard failures as a page rather than as the API's JSON problem document.
    /// </summary>
    /// <param name="app">The pipeline.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    /// <remarks>
    /// Place it directly inside the API's exception middleware: it then sees dashboard exceptions
    /// first and re-renders <c>/error</c>, while API paths never enter this branch.
    /// </remarks>
    public static IApplicationBuilder UseDashboardErrorHandling(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var environment = app.ApplicationServices.GetService(typeof(IWebHostEnvironment)) as IWebHostEnvironment;

        return app.UseWhen(
            context => IsDashboardPath(context.Request.Path),
            branch =>
            {
                if (environment?.IsDevelopment() == true)
                {
                    branch.UseDeveloperExceptionPage();
                }
                else
                {
                    branch.UseExceptionHandler("/error", createScopeForErrors: true);
                }
            });
    }

    /// <summary>
    /// Maps the dashboard: its static assets, its pages and Blazor connection, and the account
    /// endpoints.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same <paramref name="endpoints"/>, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Every endpoint states its authorization.</b> The host's fallback policy demands a client
    /// key of anything that does not, so:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>static assets are anonymous - or the sign-in page would load unstyled and without scripts;</description></item>
    ///   <item><description>pages and the Blazor connection (<c>/_blazor</c>) require a signed-in user - the convention reaches the hub endpoints too, which is why the hub is never mapped separately;</description></item>
    ///   <item><description>the account pages opt out with <c>[AllowAnonymous]</c>.</description></item>
    /// </list>
    /// <para>
    /// The endpoint check covers the first request only. Navigation inside a running circuit is
    /// checked by the router against each page's own <c>[Authorize]</c>.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapStaticAssets().AllowAnonymous();

        endpoints.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode(options => options.ContentSecurityFrameAncestorsPolicy = "'none'")
            .RequireAuthorization(DashboardPolicies.Authenticated);

        endpoints.MapAccountEndpoints();
        endpoints.MapFinanceExport();

        return endpoints;
    }
}
