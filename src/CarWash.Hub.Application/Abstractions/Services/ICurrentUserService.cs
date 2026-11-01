using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Abstractions.Services;

/// <summary>
/// Describes whoever is making the current request.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// An abstraction over <c>IHttpContextAccessor</c> so that the Persistence layer can stamp audit
/// columns without referencing ASP.NET Core, and so that tests can supply a caller without
/// constructing an <c>HttpContext</c>.
/// </para>
/// <para>
/// <b>Two kinds of caller.</b> A machine or a tester holds a shared client key, described by
/// <see cref="ClientKeyId"/> and <see cref="Scopes"/>, and has no <see cref="UserId"/>. A dashboard
/// user is described by <see cref="UserId"/>, <see cref="Role"/> and <see cref="GasStationId"/>, and
/// has no key.
/// </para>
/// <para>
/// Every member is nullable or empty by design: migrations and startup run with no request in
/// scope, and that must be an ordinary case rather than an exception.
/// </para>
/// </remarks>
public interface ICurrentUserService
{
    /// <summary>Gets the signed-in dashboard user's identifier, or null for a client key or no caller.</summary>
    Guid? UserId { get; }

    /// <summary>Gets the signed-in dashboard user's sign-in name, or null.</summary>
    string? UserName { get; }

    /// <summary>Gets the signed-in dashboard user's role, or null for anyone else.</summary>
    UserRole? Role { get; }

    /// <summary>Gets the station of a signed-in station user, or null.</summary>
    Guid? GasStationId { get; }

    /// <summary>Gets a value indicating whether the request carries an authenticated principal.</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the id of the configured client key the request was authenticated with, or null. The
    /// id, never the key.
    /// </summary>
    string? ClientKeyId { get; }

    /// <summary>Gets the scopes granted to the current principal, such as <c>ingest</c> or <c>read</c>.</summary>
    IReadOnlyCollection<string> Scopes { get; }

    /// <summary>Gets the client IP address, or null outside a request.</summary>
    string? IpAddress { get; }

    /// <summary>Gets the client user agent, or null outside a request.</summary>
    string? UserAgent { get; }

    /// <summary>Gets the correlation identifier for the current request, or null.</summary>
    string? CorrelationId { get; }
}
