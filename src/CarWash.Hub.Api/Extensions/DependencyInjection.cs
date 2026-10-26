using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Asp.Versioning;
using CarWash.Hub.Api.Filters;
using CarWash.Hub.Api.OpenApi;
using CarWash.Hub.Api.Options;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Api.Extensions;

/// <summary>
/// Composition root for the Api layer.
/// </summary>
/// <remarks>Layer: Api.</remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Registers Api-layer services: controllers, filters, API versioning, rate limiting, health
    /// checks, ProblemDetails and the OpenAPI documents.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RequestLoggingOptions>()
            .Bind(configuration.GetSection(RequestLoggingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The Application layer declares the SharedResource marker and owns the .resx files but
        // references only abstractions; the concrete localization services live in the ASP.NET Core
        // shared framework, available here. ResourcesPath names the folder inside the Application
        // assembly where the .resx files are embedded.
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.AddControllers(mvc =>
        {
            // Global filters, so no controller opts in and none can forget.
            mvc.Filters.Add<ValidationFilter>();
            mvc.Filters.Add<ApiResponseWrapperFilter>();
        })
        .AddJsonOptions(json =>
        {
            // Without this, System.Text.Json escapes every non-ASCII character, so each letter of an
            // Arabic message ships as a six-character escape sequence - valid JSON, unreadable in a
            // log or a debugger.
            json.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);

            // Enums as their names: "sortDirection": "Descending" is self-describing; 1 is not.
            json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.ConfigureLocalizedModelStateErrors();

        services.AddProblemDetails();
        services.AddEndpointsApiExplorer();

        services.AddApiVersioningSupport();
        services.AddReverseProxySupport(configuration);
        services.AddConfiguredRateLimiting();
        services.AddConfiguredHealthChecks();

        services.AddOptions<ApiDocumentationOptions>()
            .Bind(configuration.GetSection(ApiDocumentationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOpenApiDocumentation();

        return services;
    }

    /// <summary>
    /// Replaces the framework's English model-state 400 with a localized problem document.
    /// </summary>
    /// <remarks>
    /// <c>ValidationFilter</c> handles rules; this handles the layer beneath it - a query parameter
    /// that is not a number, a date that does not parse. Those fail during model binding, before any
    /// validator runs, and the framework's default response is neither localized nor shaped like
    /// the rest of this API's errors.
    /// </remarks>
    private static IServiceCollection ConfigureLocalizedModelStateErrors(this IServiceCollection services) =>
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var localizer = context.HttpContext.RequestServices.GetRequiredService<ILocalizationService>();

                var correlationId = context.HttpContext.Items
                    .TryGetValue(HttpContextItemKeys.CorrelationId, out var value)
                    ? value as string
                    : null;

                // A conversion failure is reported by the framework with raw exception text that
                // can name internal CLR types; replaced with one clean, localized message per field.
                // A JSON body that does not parse is reported twice - under the JSON path "$" and
                // under the parameter ("request") - so both map to "request" and are merged.
                var message = localizer.GetString(ResourceKeys.Validation.MalformedRequest);
                var errors = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .Select(entry => entry.Key.TrimStart('$', '.') is { Length: > 0 } key ? key : "request")
                    .Distinct(StringComparer.Ordinal)
                    .ToDictionary(field => field, _ => new[] { message }, StringComparer.Ordinal);

                var problem = new ValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Validation failed",
                    Detail = localizer.GetString(ResourceKeys.Errors.ValidationFailed),
                    Instance = context.HttpContext.Request.Path,
                };

                problem.Extensions["code"] = ResourceKeys.Validation.MalformedRequest;
                problem.Extensions["correlationId"] = correlationId;

                // A JsonResult, not a BadRequestObjectResult: [Produces("application/json")] on the
                // controllers rewrites the content type of every ObjectResult, which would label this
                // problem document as plain JSON. JsonResult serializes the runtime type, so the
                // "errors" member is kept.
                return new JsonResult(problem)
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentType = "application/problem+json",
                };
            };
        });

    /// <summary>Configures URL-segment API versioning.</summary>
    private static IServiceCollection AddApiVersioningSupport(this IServiceCollection services)
    {
        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);

                // AssumeDefaultVersionWhenUnspecified is deliberately NOT set: every controller
                // declares [ApiVersion], so an unversioned request is a client mistake and should 404
                // rather than be silently routed to v1 - which would quietly break that client the
                // day v2 becomes the default.
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            // Required for controller-based APIs: without it [ApiVersion] is never read.
            .AddMvc()
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        return services;
    }

    /// <summary>Registers the ingestion rate-limiting policy.</summary>
    private static IServiceCollection AddConfiguredRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(rateLimiter =>
        {
            rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            rateLimiter.AddPolicy(RateLimitingOptions.IngestPolicyName, context =>
            {
                // Resolved per request, so the limits reflect current configuration.
                var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

                if (!limits.Enabled)
                {
                    return RateLimitPartition.GetNoLimiter("disabled");
                }

                // Partitioned by client address, not by key: every machine sends the same key, so a
                // per-key limit would be one bucket for the whole fleet. Behind a proxy this is the
                // machine's address only when ReverseProxy is configured.
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.IngestPermitLimit,
                    Window = TimeSpan.FromSeconds(limits.IngestWindowSeconds),

                    // Refuse immediately rather than queue: a machine that is told to back off
                    // retries later; a queued request just holds a connection open.
                    QueueLimit = 0,
                });
            });

            rateLimiter.OnRejected = async (context, cancellationToken) =>
            {
                // Tells a well-behaved client when to try again instead of leaving it to guess.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var localizer = context.HttpContext.RequestServices.GetRequiredService<ILocalizationService>();
                var correlationId = context.HttpContext.Items
                    .TryGetValue(HttpContextItemKeys.CorrelationId, out var value)
                    ? value as string
                    : null;

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = localizer.GetString(ResourceKeys.Errors.TooManyRequests),
                        Extensions =
                        {
                            ["code"] = ResourceKeys.Errors.TooManyRequests,
                            ["correlationId"] = correlationId,
                        },
                    },
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken).ConfigureAwait(false);
            };
        });

    /// <summary>Registers liveness and readiness health checks.</summary>
    /// <remarks>
    /// Liveness answers "is the process up?" and must not touch the database - a database blip
    /// would otherwise make an orchestrator restart a healthy process in a loop. Readiness answers
    /// "can it serve traffic?" and does check the database.
    /// </remarks>
    private static IServiceCollection AddConfiguredHealthChecks(this IServiceCollection services)
    {
        // Connection string resolved when the check first runs, not at registration.
        services.AddHealthChecks()
            .AddSqlServer(
                connectionStringFactory: serviceProvider => serviceProvider.GetRequiredService<IConfiguration>()
                    .GetConnectionString("Default") ?? string.Empty,
                name: "sqlserver",
                tags: ["ready", "db"]);

        return services;
    }
}
