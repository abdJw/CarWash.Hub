using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Infrastructure.Auth;

/// <summary>The names of the two client-key authentication schemes.</summary>
/// <remarks>Layer: Infrastructure.</remarks>
public static class ClientKeySchemes
{
    /// <summary>Reads <c>client-secret-key</c>; grants the <c>ingest</c> scope.</summary>
    public const string SecretKey = "ClientSecretKey";

    /// <summary>Reads <c>client-read-key</c>; grants the <c>read</c> scope.</summary>
    public const string ReadKey = "ClientReadKey";
}

/// <summary>The authorization policies endpoints name.</summary>
/// <remarks>
/// Layer: Infrastructure. Each policy names the one scheme it accepts, so a read endpoint never
/// even looks at <c>client-secret-key</c>.
/// </remarks>
public static class AuthorizationPolicies
{
    /// <summary>Push machine messages: requires <c>client-secret-key</c>.</summary>
    public const string Ingest = "Ingest";

    /// <summary>Read machines and messages: requires <c>client-read-key</c>.</summary>
    public const string Read = "Read";
}

/// <summary>
/// Registers client-key authentication and the authorization policies built on it.
/// </summary>
/// <remarks>Layer: Infrastructure.</remarks>
public static class ClientKeyAuthenticationExtensions
{
    /// <summary>
    /// Registers the two client-key schemes, the <c>Ingest</c> and <c>Read</c> policies, and a
    /// fallback policy that requires authentication everywhere else.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration, bound lazily.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Secure by default.</b> The fallback policy applies to every endpoint without an
    /// authorization attribute of its own, so an endpoint added later is protected even if
    /// someone forgets <c>[Authorize]</c>. Only the endpoints marked <c>AllowAnonymous</c> - the
    /// health probes and the API documentation UI - are open.
    /// </para>
    /// <para>
    /// No default scheme is set. A request is authenticated only by the scheme its endpoint's
    /// policy names, so the read endpoints never inspect <c>client-secret-key</c> and vice versa.
    /// Phase 2's dashboard adds a cookie scheme beside these without touching them.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddClientKeyAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ClientKeyOptions>()
            .Bind(configuration.GetSection(ClientKeyOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ClientKeyOptions>, ClientKeyOptionsValidator>();

        services.AddAuthentication()
            .AddScheme<ClientKeyAuthenticationOptions, ClientKeyAuthenticationHandler>(ClientKeySchemes.SecretKey, options =>
            {
                options.HeaderName = HttpHeaderNames.ClientSecretKey;
                options.Scope = ClientScopes.Ingest;
                options.MissingKeyCode = ResourceKeys.Auth.ClientSecretKeyMissing;
                options.InvalidKeyCode = ResourceKeys.Auth.ClientSecretKeyInvalid;
                options.SelectKeys = keys => keys.IngestKeys;
            })
            .AddScheme<ClientKeyAuthenticationOptions, ClientKeyAuthenticationHandler>(ClientKeySchemes.ReadKey, options =>
            {
                options.HeaderName = HttpHeaderNames.ClientReadKey;
                options.Scope = ClientScopes.Read;
                options.MissingKeyCode = ResourceKeys.Auth.ClientReadKeyMissing;
                options.InvalidKeyCode = ResourceKeys.Auth.ClientReadKeyInvalid;
                options.SelectKeys = keys => keys.ReadKeys;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Ingest, policy => policy
                .AddAuthenticationSchemes(ClientKeySchemes.SecretKey)
                .RequireAuthenticatedUser()
                .RequireClaim(CustomClaimTypes.Scope, ClientScopes.Ingest))
            .AddPolicy(AuthorizationPolicies.Read, policy => policy
                .AddAuthenticationSchemes(ClientKeySchemes.ReadKey)
                .RequireAuthenticatedUser()
                .RequireClaim(CustomClaimTypes.Scope, ClientScopes.Read))
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(ClientKeySchemes.SecretKey, ClientKeySchemes.ReadKey)
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }
}
