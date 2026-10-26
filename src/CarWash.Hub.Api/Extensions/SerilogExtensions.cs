using CarWash.Hub.Application.Abstractions.Services;
using Serilog;

namespace CarWash.Hub.Api.Extensions;

/// <summary>
/// Serilog bootstrap and per-request enrichment.
/// </summary>
/// <remarks>Layer: Api.</remarks>
public static class SerilogExtensions
{
    /// <summary>
    /// Installs a bootstrap logger before the host is built.
    /// </summary>
    /// <remarks>
    /// Configuration binding, options validation (a missing client key, for one) and the database
    /// connection all happen while the host is being built - and all of them can fail. Without a
    /// logger in place first, those failures would be written nowhere: the hardest kind to diagnose
    /// in a container that exits on startup. Replaced by the configured logger in
    /// <see cref="UseConfiguredSerilog"/>.
    /// </remarks>
    public static void CreateBootstrapLogger() =>
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateBootstrapLogger();

    /// <summary>
    /// Replaces the bootstrap logger with the one described by configuration.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <remarks>
    /// Sinks, levels and enrichers come from the <c>Serilog</c> section of <c>appsettings.json</c>
    /// rather than from code, so verbosity can change per environment without a rebuild.
    /// </remarks>
    public static IHostApplicationBuilder UseConfiguredSerilog(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSerilog((services, configuration) => configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            // Required for the correlation-id property pushed by CorrelationIdMiddleware to reach
            // the sinks; without it, LogContext.PushProperty has no effect.
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "CarWash.Hub.Api")
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName));

        return builder;
    }

    /// <summary>
    /// Adds the authenticated client key's id to the ambient log context for the rest of the request.
    /// </summary>
    /// <param name="app">The application pipeline.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    /// <remarks>
    /// Placed after authorization, which is where an endpoint's policy authenticates the caller.
    /// Every log line from then on carries the key id - never the key - so "what did the machines
    /// on the old key send" is a log query.
    /// </remarks>
    public static IApplicationBuilder UseClientLogEnrichment(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var currentUser = context.RequestServices.GetRequiredService<ICurrentUserService>();

            if (currentUser.ClientKeyId is { } keyId)
            {
                using (Serilog.Context.LogContext.PushProperty("ClientKeyId", keyId))
                {
                    await next().ConfigureAwait(false);
                    return;
                }
            }

            await next().ConfigureAwait(false);
        });
    }
}
