namespace CarWash.Hub.Application.Exceptions;

/// <summary>
/// Base for exceptions that carry a known HTTP status and a localizable message.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// The hierarchy exists so that the exception-handling middleware maps outcomes to status
/// codes from a single place. Throwing <see cref="NotFoundException"/> in a service is
/// enough to produce a localized 404 - the service never names a status code, and the
/// middleware never grows a chain of type checks specific to one feature.
/// </para>
/// <para>
/// <see cref="MessageKey"/> is a resource key rather than a message. An exception thrown
/// deep in a service has no idea which culture the request asked for, so it carries the key
/// and lets the middleware resolve it once the culture is known.
/// </para>
/// </remarks>
/// <param name="messageKey">Resource key for the message shown to the caller.</param>
/// <param name="messageArguments">Values substituted into the resolved message.</param>
public abstract class AppException(string messageKey, params object[] messageArguments)
    : Exception(messageKey)
{
    /// <summary>Gets the resource key for the caller-facing message.</summary>
    public string MessageKey { get; } = messageKey;

    /// <summary>Gets the values substituted into the resolved message.</summary>
    public IReadOnlyList<object> MessageArguments { get; } = messageArguments;

    /// <summary>Gets the HTTP status code this exception maps to.</summary>
    public abstract int StatusCode { get; }

    /// <summary>
    /// Gets extra members for the problem document, beside <c>code</c> - for example the limit
    /// that was exceeded, so the client can react without parsing the localized message.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }
}
