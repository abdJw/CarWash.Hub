using System.Reflection;
using Asp.Versioning.ApiExplorer;
using CarWash.Hub.Api.Options;
using CarWash.Hub.Application;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Domain;
using CarWash.Hub.Infrastructure;
using CarWash.Hub.Persistence;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CarWash.Hub.Api.OpenApi;

/// <summary>
/// Swagger generation, Swagger UI and the Scalar UI.
/// </summary>
/// <remarks>
/// Layer: Api.
/// <para>
/// <b>Swashbuckle for generation, two UIs over the same documents.</b> Swagger UI at
/// <c>/swagger</c> is the page the launch profile opens and the one most engineers know; Scalar at
/// <c>/scalar</c> renders the same documents with generated client code in several languages, which
/// helps whoever writes the firmware's HTTP call.
/// </para>
/// <para>
/// <b>Two security schemes, one per header.</b> Each is an <c>apiKey</c> in a header, so the
/// Authorize dialog has a box for <c>client-secret-key</c> and one for <c>client-read-key</c>, and
/// <see cref="ScopeOperationFilter"/> attaches each to exactly the operations that need it.
/// </para>
/// </remarks>
public static class OpenApiConfiguration
{
    /// <summary>The security scheme for the shared machine key.</summary>
    public const string SecretKeySchemeName = "ClientSecretKey";

    /// <summary>The security scheme for the read key.</summary>
    public const string ReadKeySchemeName = "ClientReadKey";

    private const string Title = "CarWash Hub API";

    /// <summary>Registers Swagger generation with versioned documents and the operation filters.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddOpenApiDocumentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The per-version documents depend on IApiVersionDescriptionProvider, which only exists
        // once the container is built. IConfigureOptions defers the setup until then.
        services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition(SecretKeySchemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = HttpHeaderNames.ClientSecretKey,
                Description =
                    "The shared key every machine sends to push its status. Required on " +
                    "`POST /api/v1/machine-messages` only.",
            });

            options.AddSecurityDefinition(ReadKeySchemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = HttpHeaderNames.ClientReadKey,
                Description =
                    "The key for reading machines and stored messages. Never installed on a machine.",
            });

            options.OperationFilter<ScopeOperationFilter>();
            options.OperationFilter<LanguageHeaderOperationFilter>();
            options.OperationFilter<IngestRequestExamplesOperationFilter>();

            // XML comments from every project, so DTO properties defined in Application are
            // documented too - not only the controllers in this one.
            foreach (var assembly in DocumentedAssemblies)
            {
                var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.xml");
                if (File.Exists(xmlPath))
                {
                    options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
                }
            }

            // Schema ids default to the bare type name, so two DTOs of the same name in different
            // features would collide. Full names avoid it; generics are flattened.
            options.CustomSchemaIds(type => type.FullName!
                .Replace("CarWash.Hub.Application.", string.Empty, StringComparison.Ordinal)
                .Replace("CarWash.Hub.Domain.", string.Empty, StringComparison.Ordinal)
                .Replace("`1[[", "Of", StringComparison.Ordinal)
                .Replace("+", ".", StringComparison.Ordinal)
                .Split(',')[0]
                .Replace("]]", string.Empty, StringComparison.Ordinal));

            options.SupportNonNullableReferenceTypes();
            options.UseInlineDefinitionsForEnums();
        });

        return services;
    }

    /// <summary>
    /// Serves the generated documents, Swagger UI and the Scalar UI - unless
    /// <see cref="ApiDocumentationOptions"/> turns them off, as it does in Production by default.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    public static WebApplication UseOpenApiDocumentation(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var settings = app.Services.GetRequiredService<IOptions<ApiDocumentationOptions>>().Value;
        if (!settings.IsEnabledIn(app.Environment))
        {
            // Not mapped at all, so /swagger and /scalar answer 404 like any unknown path.
            return app;
        }

        if (settings.AllowedNetworks.Count > 0)
        {
            RestrictToNetworks(app, [.. settings.AllowedNetworks.Select(System.Net.IPNetwork.Parse)]);
        }

        var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

        // Swashbuckle serves /swagger/{documentName}/swagger.json. These are middleware, not
        // endpoints - and the authorization fallback policy applies to every request reaching it
        // without an endpoint - so Program.cs calls this BEFORE UseRouting/UseAuthorization.
        app.UseSwagger();

        app.UseSwaggerUI(options =>
        {
            options.DocumentTitle = Title;
            options.DisplayRequestDuration();

            // Keeps the keys entered in the Authorize dialog across page reloads, in the tester's
            // own browser storage - so an engineer pastes them once per session.
            options.EnablePersistAuthorization();

            foreach (var description in provider.ApiVersionDescriptions)
            {
                options.SwaggerEndpoint(
                    $"/swagger/{description.GroupName}/swagger.json",
                    $"{Title} {description.GroupName.ToUpperInvariant()}");
            }
        });

        // Scalar is an endpoint, so it must opt out of the fallback policy explicitly - otherwise
        // the documentation page itself would demand a client key.
        app.MapScalarApiReference("/scalar", options =>
        {
            options
                .WithTitle(Title)
                .WithTheme(ScalarTheme.Purple)
                // Scalar otherwise pulls fonts from fonts.scalar.com, which the documentation CSP
                // blocks. System fonts keep the page same-origin.
                .DisableDefaultFonts()
                .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl)
                .WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json")
                .AddPreferredSecuritySchemes(SecretKeySchemeName, ReadKeySchemeName);

            foreach (var description in provider.ApiVersionDescriptions)
            {
                options.AddDocument(description.GroupName, $"{Title} {description.GroupName.ToUpperInvariant()}");
            }
        }).AllowAnonymous();

        return app;
    }

    /// <summary>Answers 404 on the documentation paths to anyone outside the allowed networks.</summary>
    /// <remarks>
    /// Reads the connection's address, which is the client's only when the API sees it directly or
    /// <c>ReverseProxy</c> is configured; behind an unconfigured proxy everyone looks like the proxy.
    /// </remarks>
    private static void RestrictToNetworks(WebApplication app, System.Net.IPNetwork[] networks) =>
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/swagger") || context.Request.Path.StartsWithSegments("/scalar"),
            branch => branch.Use(async (context, next) =>
            {
                var address = context.Connection.RemoteIpAddress;
                if (address is { IsIPv4MappedToIPv6: true })
                {
                    address = address.MapToIPv4();
                }

                if (address is null || !networks.Any(network => network.Contains(address)))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                await next(context).ConfigureAwait(false);
            }));

    private static IEnumerable<Assembly> DocumentedAssemblies =>
    [
        typeof(Program).Assembly,
        ApplicationAssemblyReference.Assembly,
        DomainAssemblyReference.Assembly,
        InfrastructureAssemblyReference.Assembly,
        PersistenceAssemblyReference.Assembly,
    ];

    /// <summary>
    /// Creates one Swagger document per discovered API version.
    /// </summary>
    /// <remarks>
    /// Adding a v2 controller produces a v2 document automatically; nothing here names a version.
    /// </remarks>
    /// <param name="provider">Enumerates the API versions present in the application.</param>
    private sealed class ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
        : IConfigureOptions<SwaggerGenOptions>
    {
        public void Configure(SwaggerGenOptions options)
        {
            foreach (var description in provider.ApiVersionDescriptions)
            {
                options.SwaggerDoc(description.GroupName, new OpenApiInfo
                {
                    Title = Title,
                    Version = description.ApiVersion.ToString(),
                    Description = BuildDescription(description),
                    Contact = new OpenApiContact { Name = "CarWash Hub" },
                });
            }
        }

        private static string BuildDescription(ApiVersionDescription description)
        {
            var text =
                "Machine data ingestion for self-service car wash machines.\n\n" +
                "**Push** a machine's status with `POST /api/v1/machine-messages` and the shared " +
                "`client-secret-key` header. The body is JSON with two fields: `machineId` (a string) and " +
                "`data` (any JSON - object, array or string), which is stored as text exactly as sent.\n\n" +
                "**Read** machines and stored messages with the separate `client-read-key` header.\n\n" +
                "Successful responses are wrapped in an `ApiResponse<T>` envelope; failures are RFC 7807 " +
                "problem documents with a stable `code`. Send `X-Lang: ar` for Arabic messages.";

            if (description.IsDeprecated)
            {
                text += "\n\n**This API version has been deprecated.**";
            }

            return text;
        }
    }
}
