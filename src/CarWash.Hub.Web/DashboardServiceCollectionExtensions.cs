using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Web.Auth;
using CarWash.Hub.Web.Circuits;
using CarWash.Hub.Web.Formatting;
using CarWash.Hub.Web.Realtime;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;

namespace CarWash.Hub.Web;

/// <summary>
/// Composition root for the dashboard.
/// </summary>
/// <remarks>Layer: Web. Called by the host after the other layers' registrations, which it builds on.</remarks>
public static class DashboardServiceCollectionExtensions
{
    /// <summary>
    /// Registers the dashboard: Blazor Server, MudBlazor, cookie sign-in and its policies, the
    /// sign-in rate limit, the per-operation runner and the live event bus.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddDashboard(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddRazorComponents()
            .AddInteractiveServerComponents(options =>
            {
                // Recycles and flaky mobile networks drop connections; keep enough circuits around for
                // their tabs to rejoin where they were.
                options.DisconnectedCircuitMaxRetained = 50;
                options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
            });

        // Arabic text is written as itself rather than as &#x...; entities: a third of the size, and
        // readable in the page source. Still HTML-encoded where it must be (<, >, &, quotes).
        services.AddWebEncoders(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

        services.AddMudServices(options =>
        {
            options.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter;
            options.SnackbarConfiguration.PreventDuplicates = true;
            options.SnackbarConfiguration.VisibleStateDuration = 6000;
        });

        AddSignIn(services, configuration);

        services.AddSingleton<DashboardEventBus>();
        services.Replace(ServiceDescriptor.Singleton<IDashboardEventPublisher>(provider => provider.GetRequiredService<DashboardEventBus>()));

        // Per circuit.
        services.AddScoped<LiveUpdates>();
        services.AddScoped<DashboardOperationRunner>();
        services.AddScoped<DashboardFormat>();

        return services;
    }

    private static void AddSignIn(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DashboardCookieOptions>()
            .Bind(configuration.GetSection(DashboardCookieOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The cookie becomes the default scheme, so [Authorize] on a page and the Blazor router see
        // the signed-in user. The API is unaffected: its policies name their client-key schemes,
        // which replace the request's user before they are evaluated.
        services.AddAuthentication(DashboardAuthentication.Scheme)
            .AddCookie(DashboardAuthentication.Scheme);

        services.AddOptions<CookieAuthenticationOptions>(DashboardAuthentication.Scheme)
            .Configure<IOptions<DashboardCookieOptions>, IHostEnvironment>((options, settings, environment) =>
                DashboardAuthentication.ConfigureCookie(options, settings.Value, environment.IsDevelopment()));

        services.AddAuthorizationBuilder()
            .AddPolicy(DashboardPolicies.Authenticated, policy => policy
                .AddAuthenticationSchemes(DashboardAuthentication.Scheme)
                .RequireAuthenticatedUser()
                .RequireRole(DashboardPolicies.AllRoles))
            .AddPolicy(DashboardPolicies.SuperAdmin, policy => policy
                .AddAuthenticationSchemes(DashboardAuthentication.Scheme)
                .RequireAuthenticatedUser()
                .RequireRole(nameof(Domain.Enums.UserRole.SuperAdmin)));

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, DashboardAuthenticationStateProvider>();

        // Per client address, like the ingest limit; on top of the per-account lockout, it slows a
        // password-guessing run across many accounts. Off whenever RateLimiting:Enabled is.
        services.AddRateLimiter(options => options.AddPolicy(DashboardAuthentication.SignInRateLimitPolicy, context =>
        {
            var enabled = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:Enabled", true);
            var partition = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return enabled
                ? RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                })
                : RateLimitPartition.GetNoLimiter(partition);
        }));
    }
}
