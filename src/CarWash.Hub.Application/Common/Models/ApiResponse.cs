namespace CarWash.Hub.Application.Common.Models;

/// <summary>
/// The envelope wrapped around every successful response.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
/// <param name="Success">Always <see langword="true"/> on this path; present so a client can branch on one field.</param>
/// <param name="Message">A localized, human-readable summary of what happened.</param>
/// <param name="Data">The payload.</param>
/// <param name="Errors">Per-field messages. Empty on success; see the remarks.</param>
/// <param name="CorrelationId">The request's correlation identifier.</param>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>Applied to 2xx responses only.</b> Failures return RFC 7807 <c>ProblemDetails</c> instead.
/// The two formats overlap, and this is a deliberate split rather than an inconsistency:
/// ProblemDetails is a published standard that HTTP clients, proxies and OpenAPI tooling
/// already understand, and re-wrapping it inside a bespoke envelope would discard that for no
/// gain. The envelope earns its place on success, where there is no standard shape and a
/// localized <c>Message</c> plus a <c>CorrelationId</c> are genuinely useful.
/// </para>
/// <para>
/// <c>Errors</c> is therefore empty on every response the envelope currently produces. It is
/// kept for operations that partially succeed - a bulk import reporting which rows failed while
/// still returning 200 - which is the only case where errors belong in a 2xx body.
/// </para>
/// <para>
/// Applied by <c>ApiResponseWrapperFilter</c>, not by controllers. Controllers keep returning
/// <c>Ok(dto)</c>, so the envelope can change shape without editing a single action.
/// </para>
/// </remarks>
public sealed record ApiResponse<T>(
    bool Success,
    string Message,
    T? Data,
    IReadOnlyDictionary<string, string[]> Errors,
    string? CorrelationId)
{
    /// <summary>Builds a successful envelope.</summary>
    /// <param name="data">The payload.</param>
    /// <param name="message">The localized summary.</param>
    /// <param name="correlationId">The request's correlation identifier.</param>
    /// <returns>The envelope.</returns>
    public static ApiResponse<T> Ok(T? data, string message, string? correlationId) =>
        new(true, message, data, EmptyErrors, correlationId);

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.Ordinal);
}

/// <summary>
/// Non-generic helpers for responses that carry no payload.
/// </summary>
/// <remarks>Layer: Application.</remarks>
public static class ApiResponse
{
    /// <summary>Builds a successful envelope with no payload.</summary>
    /// <param name="message">The localized summary.</param>
    /// <param name="correlationId">The request's correlation identifier.</param>
    /// <returns>The envelope.</returns>
    public static ApiResponse<object> Ok(string message, string? correlationId) =>
        ApiResponse<object>.Ok(null, message, correlationId);
}
