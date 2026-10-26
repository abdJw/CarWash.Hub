using CarWash.Hub.Api.Extensions;
using CarWash.Hub.Api.Middleware;
using CarWash.Hub.Api.OpenApi;
using CarWash.Hub.Application;
using CarWash.Hub.Infrastructure;
using CarWash.Hub.Infrastructure.Localization;
using CarWash.Hub.Persistence;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

// A logger must exist before the host is built: configuration binding, options validation (a
// missing client key) and the database connection all happen during build and all of them can
// fail. Without this, those failures would be written nowhere.
SerilogExtensions.CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.UseConfiguredSerilog();

    // One registration extension per layer. Program.cs stays a readable list of what the
    // application is composed of, never a dumping ground for individual registrations.
    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration)
        .AddPersistence(builder.Configuration)
        .AddApiServices(builder.Configuration);

    var app = builder.Build();

    // Applies pending migrations when Seed:ApplyMigrations is on. Routed through the
    // IDbInitializer abstraction, so this file never names a Persistence type - AddPersistence
    // above is the only place the Api layer is permitted to do that.
    await app.Services.InitializeDatabaseAsync().ConfigureAwait(false);

    // =======================================================================================
    // MIDDLEWARE ORDER IS LOAD-BEARING. Every position below is justified; README.md repeats
    // the order under "Middleware ordering".
    // =======================================================================================

    // 0. Forwarded headers from a configured proxy, before anything reads the client's address
    //    or the scheme: logs, the rate limiter, the stored source IP and HSTS.
    app.UseReverseProxySupport();

    // 1. Correlation id first, so every log line, stored message and error response produced
    //    below carries the same identifier.
    app.UseMiddleware<CorrelationIdMiddleware>();

    // 2. Request logging wraps everything downstream, so the recorded status and duration are
    //    what the client actually observed - including time spent handling an error.
    app.UseMiddleware<RequestLoggingMiddleware>();

    // 3. Localization ABOVE the exception handler. CultureInfo.CurrentUICulture flows through an
    //    AsyncLocal, and a value set DOWNSTREAM is not visible to a handler further UP the call
    //    stack: below the handler, every error body would come back in English however the client
    //    asked. Safe to run outside the handler only because the X-Lang provider never throws.
    app.UseConfiguredRequestLocalization();

    // 4. Exception handling: converts anything thrown below into a localized ProblemDetails.
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // 5. Security headers, registered via OnStarting so they land on every response - including
    //    the error responses produced above.
    app.UseMiddleware<SecurityHeadersMiddleware>();

    app.UseHttpsRedirection();

    // 6. API documentation BEFORE routing and authorization. The authorization fallback policy
    //    applies to every request that reaches it without an endpoint - and Swagger's documents
    //    and UI are middleware, not endpoints - so placed after UseAuthorization they would demand
    //    a client key. Here they answer first. Off in Production unless configured.
    app.UseOpenApiDocumentation();

    // 7. Routing before rate limiting and authorization - both read endpoint metadata that does
    //    not exist until a route has matched.
    app.UseRouting();

    // 8. Authentication. No default scheme is configured: each endpoint's policy authenticates
    //    with the one client-key scheme it names (step 10). Kept in place for Phase 2, whose
    //    dashboard adds a default cookie scheme.
    app.UseAuthentication();

    // 9. The per-address ingest limit. Before authorization, so a flood is refused before any
    //    key is hashed.
    app.UseRateLimiter();

    // 10. Authorization: the Ingest and Read policies authenticate client-secret-key or
    //     client-read-key, and the fallback policy refuses everything else that is not explicitly
    //     anonymous - including unknown paths, which answer 401 to a caller without a key.
    app.UseAuthorization();

    // 11. Adds the authenticated key's id to the log context - after authorization, because that
    //     is where the policy put the principal.
    app.UseClientLogEnrichment();

    app.MapControllers();

    // Liveness: is the process up? Deliberately does NOT touch the database, so a database blip
    // does not make an orchestrator kill a healthy process. Anonymous: probes carry no key.
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    }).AllowAnonymous().ExcludeFromDescription();

    // Readiness: can it serve traffic? Includes the database.
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    }).AllowAnonymous().ExcludeFromDescription();

    await app.RunAsync().ConfigureAwait(false);
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // HostAbortedException is excluded deliberately: `dotnet ef` builds and aborts the host to
    // obtain the model, and treating that as a crash would make every migration command log a
    // fatal error.
    Log.Fatal(ex, "The application terminated unexpectedly during startup.");
    throw;
}
finally
{
    // Flushes buffered sinks; without this the last log lines - the interesting ones on a crash -
    // are lost when the process exits.
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

/// <summary>
/// Entry point marker. Declared explicitly so that <c>WebApplicationFactory&lt;Program&gt;</c> in the
/// integration tests has a public type to bind to.
/// </summary>
public partial class Program;
