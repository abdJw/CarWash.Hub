using System.Security.Claims;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Domain.Constants;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;

namespace CarWash.Hub.Infrastructure.Services;

/// <summary>
/// Reads the current caller's principal and connection details.
/// </summary>
/// <remarks>
/// Layer: Infrastructure.
/// <para>
/// Every member tolerates the absence of an HTTP context: migrations and startup run with no
/// request in scope, and the audit interceptor consults this service on every save - so throwing
/// here would break migrations rather than reporting a problem.
/// </para>
/// <para>
/// The principal is the one set in <see cref="CurrentPrincipalHolder"/> for a dashboard operation,
/// otherwise the one the authorization policy authenticated for the request: a client-key principal
/// carries <c>key_id</c> and <c>scope</c>; a dashboard cookie carries the user's id, role and
/// station. Registered scoped, matching the request or operation it describes.
/// </para>
/// </remarks>
/// <param name="httpContextAccessor">Provides access to the current HTTP context, if any.</param>
/// <param name="holder">The scope's caller, when it is not the HTTP request's.</param>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor, CurrentPrincipalHolder holder) : ICurrentUserService
{
    private HttpContext? Context => httpContextAccessor.HttpContext;

    private ClaimsPrincipal? Principal => holder.Principal ?? Context?.User;

    /// <inheritdoc />
    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(CustomClaimTypes.UserId), out var id) ? id : null;

    /// <inheritdoc />
    public string? UserName => UserId is null ? null : Principal?.FindFirstValue(CustomClaimTypes.UserName);

    /// <inheritdoc />
    public UserRole? Role =>
        UserId is not null && Enum.TryParse<UserRole>(Principal?.FindFirstValue(CustomClaimTypes.Role), out var role) && Enum.IsDefined(role)
            ? role
            : null;

    /// <inheritdoc />
    public Guid? GasStationId =>
        Guid.TryParse(Principal?.FindFirstValue(CustomClaimTypes.GasStationId), out var stationId) ? stationId : null;

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
    public string? IpAddress => holder.Principal is not null ? holder.IpAddress : Context?.Connection.RemoteIpAddress?.ToString();

    /// <inheritdoc />
    public string? UserAgent => holder.Principal is not null
        ? holder.UserAgent
        : Context?.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null;

    /// <inheritdoc />
    public string? CorrelationId => holder.Principal is not null
        ? holder.CorrelationId
        : Context?.Items.TryGetValue(HttpContextItemKeys.CorrelationId, out var value) == true ? value as string : null;
}
