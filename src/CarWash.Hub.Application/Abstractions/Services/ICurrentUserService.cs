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
/// <b>Two kinds of caller.</b> In Phase 1 every caller is a machine or a tester holding a shared
/// client key, described by <see cref="ClientKeyId"/> and <see cref="Scopes"/>; <see cref="UserId"/>
/// is always null. Phase 2's dashboard sign-in fills <see cref="UserId"/> and adds the user's role
/// and station beside it, without changing the members already here.
/// </para>
/// <para>
/// Every member is nullable or empty by design: migrations and startup run with no request in
/// scope, and that must be an ordinary case rather than an exception.
/// </para>
/// </remarks>
public interface ICurrentUserService
{
    /// <summary>Gets the signed-in dashboard user's identifier; always null in Phase 1.</summary>
    Guid? UserId { get; }

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
