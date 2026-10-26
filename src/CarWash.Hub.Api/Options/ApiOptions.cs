using System.ComponentModel.DataAnnotations;
using System.Net;

namespace CarWash.Hub.Api.Options;

/// <summary>
/// The reverse proxy or load balancer in front of the API.
/// </summary>
/// <remarks>
/// Layer: Api. Bound from the <c>ReverseProxy</c> configuration section.
/// <para>
/// When TLS ends at a proxy, the API sees plain HTTP from the proxy's address. Without the
/// forwarded headers every machine shares the proxy's address - one rate-limit bucket for the
/// whole fleet, the proxy's address stored on every message - and every request looks like HTTP,
/// so HSTS is never sent.
/// </para>
/// <para>
/// Only proxies listed here are believed. Trusting <c>X-Forwarded-For</c> from anyone would let a
/// caller write any address into the stored messages and dodge the rate limiter by inventing one
/// per request. Loopback is always trusted, for a proxy on the same host.
/// </para>
/// </remarks>
public sealed class ReverseProxyOptions : IValidatableObject
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "ReverseProxy";

    /// <summary>Gets or sets a value indicating whether <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> are read.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets the addresses of the proxies to believe, for example <c>10.0.4.11</c>.</summary>
    public IList<string> KnownProxies { get; } = [];

    /// <summary>Gets the networks of the proxies to believe, in CIDR form, for example <c>10.0.4.0/24</c>.</summary>
    public IList<string> KnownNetworks { get; } = [];

    /// <summary>Gets or sets how many proxies in a chain are unwound: 1 for a single proxy.</summary>
    [Range(1, 10)]
    public int ForwardLimit { get; set; } = 1;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var proxy in KnownProxies.Where(proxy => !IPAddress.TryParse(proxy, out _)))
        {
            yield return new ValidationResult($"'{proxy}' is not an IP address.", [nameof(KnownProxies)]);
        }

        foreach (var network in KnownNetworks.Where(network => !IPNetwork.TryParse(network, out _)))
        {
            yield return new ValidationResult($"'{network}' is not a network in CIDR form.", [nameof(KnownNetworks)]);
        }
    }
}

/// <summary>
/// Whether, and to whom, the API documentation is served: the OpenAPI JSON, Swagger UI and Scalar.
/// </summary>
/// <remarks>
/// Layer: Api. Bound from the <c>ApiDocumentation</c> configuration section.
/// <para>
/// Unset, <see cref="Enabled"/> means on everywhere except Production. The machine engineers test
/// against a staging server, where it is on; to expose it in Production as well, set it to
/// <see langword="true"/> and, ideally, limit it to their network with <see cref="AllowedNetworks"/>.
/// </para>
/// </remarks>
public sealed class ApiDocumentationOptions : IValidatableObject
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "ApiDocumentation";

    /// <summary>Gets or sets whether the documentation is served; unset means everywhere but Production.</summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets the networks, in CIDR form, allowed to read the documentation. Empty means everyone who
    /// can reach the API. Requests from elsewhere get 404, as if nothing were there.
    /// </summary>
    public IList<string> AllowedNetworks { get; } = [];

    /// <summary>Decides whether the documentation is served in an environment.</summary>
    /// <param name="environment">The hosting environment.</param>
    /// <returns><see langword="true"/> to serve it.</returns>
    public bool IsEnabledIn(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return Enabled ?? !environment.IsProduction();
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        AllowedNetworks
            .Where(network => !IPNetwork.TryParse(network, out _))
            .Select(network => new ValidationResult($"'{network}' is not a network in CIDR form.", [nameof(AllowedNetworks)]));
}

/// <summary>
/// Rate-limiting settings for the ingestion endpoint.
/// </summary>
/// <remarks>
/// Layer: Api. Bound from the <c>RateLimiting</c> configuration section.
/// <para>
/// <b>Partitioned by client address, not by key.</b> Every machine sends the same key, so a
/// per-key limit would be one bucket for the whole fleet - a single misbehaving machine would
/// throttle every station. Per address, a machine stuck in a retry loop is throttled alone (or, at
/// worst, with the machines sharing its station's internet connection). Size the limit for the
/// busiest address: machines behind one connection × messages per minute, with headroom.
/// </para>
/// </remarks>
public sealed class RateLimitingOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>The name of the policy applied to the ingestion endpoint.</summary>
    public const string IngestPolicyName = "ingest";

    /// <summary>Gets or sets a value indicating whether rate limiting is enforced.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets how many messages one address may send per window.</summary>
    [Range(1, 100_000)]
    public int IngestPermitLimit { get; set; } = 600;

    /// <summary>Gets or sets the window length, in seconds.</summary>
    [Range(1, 3600)]
    public int IngestWindowSeconds { get; set; } = 60;
}
