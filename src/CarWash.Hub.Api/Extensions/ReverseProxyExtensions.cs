using System.Net;
using CarWash.Hub.Api.Options;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Api.Extensions;

/// <summary>
/// Reads the client's address and scheme from the proxy in front of the API.
/// </summary>
/// <remarks>Layer: Api. Configured by <see cref="ReverseProxyOptions"/>.</remarks>
public static class ReverseProxyExtensions
{
    /// <summary>Binds <see cref="ReverseProxyOptions"/> and builds the framework's forwarded-headers options from it.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ReverseProxyOptions>()
            .Bind(configuration.GetSection(ReverseProxyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ReverseProxyOptions>>((forwarded, configured) =>
            {
                var proxy = configured.Value;

                // Address and scheme only. X-Forwarded-Host is left alone: nothing here builds
                // links from the request's host, and believing it would invite host-header tricks.
                forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                forwarded.ForwardLimit = proxy.ForwardLimit;

                // Added to the framework's defaults, which already trust loopback.
                foreach (var address in proxy.KnownProxies)
                {
                    forwarded.KnownProxies.Add(IPAddress.Parse(address));
                }

                foreach (var network in proxy.KnownNetworks)
                {
                    // Qualified: HttpOverrides has an obsolete IPNetwork of its own.
                    forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                }
            });

        return services;
    }

    /// <summary>
    /// Applies the forwarded headers when <see cref="ReverseProxyOptions.Enabled"/> is set. Must be the first middleware,
    /// so everything after it - logs, the rate limiter, the stored source IP, HSTS - sees the client rather than the proxy.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    public static IApplicationBuilder UseReverseProxySupport(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var proxy = app.ApplicationServices.GetRequiredService<IOptions<ReverseProxyOptions>>().Value;
        return proxy.Enabled ? app.UseForwardedHeaders() : app;
    }
}
