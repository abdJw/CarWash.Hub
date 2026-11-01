using System.Security.Claims;

namespace CarWash.Hub.Infrastructure.Auth;

/// <summary>
/// The caller of the current DI scope, when that is not the current HTTP request's caller.
/// </summary>
/// <remarks>
/// Layer: Infrastructure.
/// <para>
/// <b>Why it exists.</b> A Blazor Server page runs on a long-lived connection, where
/// <c>IHttpContextAccessor</c> describes the connection's first request rather than the person now
/// clicking - and nothing at all inside a background worker. The dashboard runs each operation in a
/// fresh scope and puts the signed-in user here first, so <see cref="Services.CurrentUserService"/>,
/// the audit stamps and the station scope all see the right person. Empty for ordinary HTTP
/// requests, which keep using the request's principal.
/// </para>
/// </remarks>
public sealed class CurrentPrincipalHolder
{
    /// <summary>Gets the principal, or null to use the HTTP request's.</summary>
    public ClaimsPrincipal? Principal { get; private set; }

    /// <summary>Gets the caller's IP address, when <see cref="Principal"/> is set.</summary>
    public string? IpAddress { get; private set; }

    /// <summary>Gets the caller's user agent, when <see cref="Principal"/> is set.</summary>
    public string? UserAgent { get; private set; }

    /// <summary>Gets the operation's correlation id, when <see cref="Principal"/> is set.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Sets the caller of this scope.</summary>
    /// <param name="principal">The signed-in user.</param>
    /// <param name="ipAddress">Their IP address, if known.</param>
    /// <param name="userAgent">Their browser, if known.</param>
    /// <param name="correlationId">An id for this operation's log lines.</param>
    public void Set(ClaimsPrincipal principal, string? ipAddress, string? userAgent, string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(principal);

        Principal = principal;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        CorrelationId = correlationId;
    }
}
