namespace CarWash.Hub.Application.Common.Constants;

/// <summary>
/// Custom HTTP header names this API accepts or emits.
/// </summary>
/// <remarks>Layer: Application. Shared by the code that reads them, the Swagger filters that
/// document them, and the tests that assert on them.</remarks>
public static class HttpHeaderNames
{
    /// <summary>
    /// The shared secret every machine sends to push messages. Required on
    /// <c>POST /api/v1/machine-messages</c>.
    /// </summary>
    /// <remarks>
    /// Header names are case-insensitive in HTTP, so <c>Client-Secret-Key</c> works too. The key is
    /// only ever read from this header - never from the query string, where it would end up in
    /// access logs and browser history.
    /// </remarks>
    public const string ClientSecretKey = "client-secret-key";

    /// <summary>
    /// The separate key required by the read endpoints. A machine never holds it, so a key pulled
    /// out of a machine cannot read any station's data.
    /// </summary>
    public const string ClientReadKey = "client-read-key";

    /// <summary>
    /// The custom language header. Takes precedence over <c>Accept-Language</c>.
    /// </summary>
    public const string Language = "X-Lang";

    /// <summary>The correlation identifier, accepted inbound and echoed on the response.</summary>
    public const string CorrelationId = "X-Correlation-Id";
}
