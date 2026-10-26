namespace CarWash.Hub.Domain.Constants;

/// <summary>
/// Claim types issued by this application.
/// </summary>
/// <remarks>
/// Layer: Domain. Short, explicit names rather than the long WS-Federation URIs, so they read the
/// same in a log, a test and the code. Phase 2 adds the dashboard user's claims (user id, role,
/// gas station) beside these.
/// </remarks>
public static class CustomClaimTypes
{
    /// <summary>The id of the configured client key that authenticated the request - never the key.</summary>
    public const string KeyId = "key_id";

    /// <summary>A scope the key grants; see <see cref="ClientScopes"/>.</summary>
    public const string Scope = "scope";
}

/// <summary>
/// The scopes a client key can grant.
/// </summary>
/// <remarks>
/// Layer: Domain. Each configured key list grants exactly one scope, so a key pulled out of a
/// machine at a gas station (scope <see cref="Ingest"/>) can push data but never read it.
/// </remarks>
public static class ClientScopes
{
    /// <summary>Push machine messages. Granted by the shared <c>client-secret-key</c>.</summary>
    public const string Ingest = "ingest";

    /// <summary>Read machines and their messages. Granted by the <c>client-read-key</c>.</summary>
    public const string Read = "read";
}
