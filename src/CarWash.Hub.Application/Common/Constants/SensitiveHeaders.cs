namespace CarWash.Hub.Application.Common.Constants;

/// <summary>
/// Names whose values must never reach a log, wherever they appear - a header, a query-string
/// parameter or a form field.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// An explicit list of exact names, matched case-insensitively, rather than a substring test. A
/// substring test is how cyberani_oms_api came to miss <c>X-Api-Key</c>: "apikey" is not a
/// substring of "x-api-key". Exact names cannot drift that way; add a name here when a new secret
/// header appears.
/// </para>
/// </remarks>
public static class SensitiveHeaders
{
    /// <summary>The text written in place of a redacted value.</summary>
    public const string RedactedPlaceholder = "***REDACTED***";

    /// <summary>Gets the sensitive names.</summary>
    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        HttpHeaderNames.ClientSecretKey,
        HttpHeaderNames.ClientReadKey,
        "client_secret_key",
        "client_read_key",
        "clientSecretKey",
        "clientReadKey",
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "api_key",
        "apiKey",
    };

    /// <summary>Determines whether a header, parameter or field name carries a secret.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> if its value must be redacted.</returns>
    public static bool IsSensitive(string name) => Names.Contains(name);
}
