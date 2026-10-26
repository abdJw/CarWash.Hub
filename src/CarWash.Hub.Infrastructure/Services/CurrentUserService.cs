using System.Security.Claims;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Domain.Constants;
using Microsoft.AspNetCore.Http;

namespace CarWash.Hub.Infrastructure.Services;

/// <summary>
/// Reads the current request's principal and connection details.
/// </summary>
/// <remarks>
/// Layer: Infrastructure.
/// <para>
/// Every member tolerates the absence of an HTTP context: migrations and startup run with no
/// request in scope, and the audit interceptor consults this service on every save - so throwing
/// here would break migrations rather than reporting a problem.
/// </para>
/// <para>
/// The principal is the one the authorization policy authenticated: a client-key principal carries
/// <c>key_id</c> and <c>scope</c> claims. Registered scoped, matching the request it describes.
/// </para>
/// </remarks>
/// <param name="httpContextAccessor">Provides access to the current HTTP context, if any.</param>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private HttpContext? Context => httpContextAccessor.HttpContext;

    private ClaimsPrincipal? Principal => Context?.User;

    /// <inheritdoc />
    /// <remarks>Phase 1 has no user accounts; Phase 2's cookie sign-in issues the claim this will read.</remarks>
    public Guid? UserId => null;

    /// <inheritdoc />
    public bool IsAuthenticated => Principal?.Identities.Any(identity => identity.IsAuthenticated) ?? false;

    /// <inheritdoc />
    public string? ClientKeyId => Principal?.FindFirstValue(CustomClaimTypes.KeyId);

    /// <inheritdoc />
    public IReadOnlyCollection<string> Scopes =>
        Principal is null
            ? []
            : [.. Principal.FindAll(CustomClaimTypes.Scope).Select(claim => claim.Value).Distinct(StringComparer.Ordinal)];

    /// <inheritdoc />
    /// <remarks>
    /// The connection's remote address. Behind a proxy this is the proxy's address unless
    /// <c>ReverseProxy:Enabled</c> is set, in which case the first middleware replaces it with the
    /// client's from <c>X-Forwarded-For</c> - but only for the proxies listed there.
    /// </remarks>
    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();

    /// <inheritdoc />
    public string? UserAgent => Context?.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent
        ? agent
        : null;

    /// <inheritdoc />
    public string? CorrelationId =>
        Context?.Items.TryGetValue(HttpContextItemKeys.CorrelationId, out var value) == true
            ? value as string
            : null;
}
