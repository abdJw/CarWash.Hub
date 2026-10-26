namespace CarWash.Hub.Application.Common.Constants;

/// <summary>
/// Keys for per-request values stashed in <c>HttpContext.Items</c>.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// Declared here, rather than in Api, because both the middleware that writes these values and
/// the Infrastructure services that read them need the same key - and Infrastructure cannot
/// reference Api. Constants rather than inline strings, so a typo is a compile error instead of a
/// silently null correlation id.
/// </para>
/// </remarks>
public static class HttpContextItemKeys
{
    /// <summary>The request's correlation identifier, set by the correlation middleware.</summary>
    public const string CorrelationId = "__CarWash_CorrelationId";

    /// <summary>
    /// A resource key that overrides the endpoint's <c>[ResponseMessage]</c> for this one response -
    /// for an action whose outcome decides the message, such as an idempotent replay.
    /// </summary>
    public const string ResponseMessageKey = "__CarWash_ResponseMessageKey";
}
