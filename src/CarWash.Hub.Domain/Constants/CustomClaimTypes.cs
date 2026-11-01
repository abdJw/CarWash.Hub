namespace CarWash.Hub.Domain.Constants;

/// <summary>
/// Claim types issued by this application.
/// </summary>
/// <remarks>
/// Layer: Domain. Short, explicit names rather than the long WS-Federation URIs, so they read the
/// same in a log, a test and the code. A machine or tester carries <see cref="KeyId"/> and
/// <see cref="Scope"/>; a dashboard user carries the rest.
/// </remarks>
public static class CustomClaimTypes
{
    /// <summary>The id of the configured client key that authenticated the request - never the key.</summary>
    public const string KeyId = "key_id";

    /// <summary>A scope the key grants; see <see cref="ClientScopes"/>.</summary>
    public const string Scope = "scope";

    /// <summary>The dashboard user's id.</summary>
    public const string UserId = "sub";

    /// <summary>The dashboard user's sign-in name.</summary>
    public const string UserName = "name";

    /// <summary>The dashboard user's name as shown in the dashboard.</summary>
    public const string DisplayName = "display_name";

    /// <summary>The dashboard user's role; see <see cref="Enums.UserRole"/>.</summary>
    public const string Role = "role";

    /// <summary>The gas station a station user belongs to.</summary>
    public const string GasStationId = "station_id";

    /// <summary>
    /// The user's security stamp when the session began. A password reset or a deactivation changes
    /// the stamp, which ends every session issued before it.
    /// </summary>
    public const string SecurityStamp = "security_stamp";

    /// <summary>Present while the user must choose a new password before using the dashboard.</summary>
    public const string MustChangePassword = "must_change_password";
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
