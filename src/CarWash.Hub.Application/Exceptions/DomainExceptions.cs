using CarWash.Hub.Application.Common.Constants;

namespace CarWash.Hub.Application.Exceptions;

/// <summary>A requested resource does not exist. Maps to 404.</summary>
/// <remarks>Layer: Application.</remarks>
/// <param name="messageKey">Resource key for the message.</param>
/// <param name="messageArguments">Values substituted into the message.</param>
public sealed class NotFoundException(string messageKey, params object[] messageArguments)
    : AppException(messageKey, messageArguments)
{
    /// <inheritdoc />
    public override int StatusCode => 404;

    /// <summary>Creates a not-found exception naming the thing and key that were missing.</summary>
    /// <param name="entityName">What was searched for, already localized.</param>
    /// <param name="key">The key that produced no result.</param>
    /// <returns>The exception.</returns>
    public static NotFoundException For(string entityName, object key) =>
        new(ResourceKeys.Errors.NotFound, entityName, key);
}

/// <summary>The request conflicts with current state, such as a duplicate. Maps to 409.</summary>
/// <remarks>Layer: Application.</remarks>
/// <param name="messageKey">Resource key for the message.</param>
/// <param name="messageArguments">Values substituted into the message.</param>
public sealed class ConflictException(string messageKey, params object[] messageArguments)
    : AppException(messageKey, messageArguments)
{
    /// <inheritdoc />
    public override int StatusCode => 409;
}

/// <summary>
/// The caller is authenticated but not permitted to perform the action. Maps to 403.
/// </summary>
/// <remarks>
/// Layer: Application. Distinct from an authentication failure: 403 says "we know who you are and
/// the answer is still no", which is a different instruction to the client than 401.
/// </remarks>
/// <param name="messageKey">Resource key for the message.</param>
/// <param name="messageArguments">Values substituted into the message.</param>
public sealed class ForbiddenException(string messageKey, params object[] messageArguments)
    : AppException(messageKey, messageArguments)
{
    /// <inheritdoc />
    public override int StatusCode => 403;
}

/// <summary>The caller is not authenticated, or the credentials supplied are wrong. Maps to 401.</summary>
/// <remarks>Layer: Application.</remarks>
/// <param name="messageKey">Resource key for the message.</param>
/// <param name="messageArguments">Values substituted into the message.</param>
public sealed class UnauthorizedException(string messageKey, params object[] messageArguments)
    : AppException(messageKey, messageArguments)
{
    /// <inheritdoc />
    public override int StatusCode => 401;
}

/// <summary>
/// The request is well-formed but breaks a business rule - reusing an idempotency key for a
/// different message, for example. Maps to 422.
/// </summary>
/// <remarks>
/// Layer: Application. Distinct from <see cref="ValidationException"/> (400, a malformed field) and
/// <see cref="ConflictException"/> (409, a race or duplicate): the input was valid, but the action
/// is not allowed. The message key doubles as the machine-readable <c>code</c>.
/// </remarks>
/// <param name="messageKey">Resource key for the message; also the problem's <c>code</c>.</param>
/// <param name="messageArguments">Values substituted into the message.</param>
public sealed class BusinessRuleException(string messageKey, params object[] messageArguments)
    : AppException(messageKey, messageArguments)
{
    /// <inheritdoc />
    public override int StatusCode => 422;
}

/// <summary>Input failed validation. Maps to 400, carrying per-field errors.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class ValidationException : AppException
{
    /// <summary>Creates a validation exception from per-field errors.</summary>
    /// <param name="errors">Field name to the messages for that field.</param>
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base(ResourceKeys.Errors.ValidationFailed) => Errors = errors;

    /// <summary>Creates a validation exception for a single field.</summary>
    /// <param name="field">The field in error.</param>
    /// <param name="message">The already-resolved message.</param>
    public ValidationException(string field, string message)
        : base(ResourceKeys.Errors.ValidationFailed) =>
        Errors = new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] };

    /// <inheritdoc />
    public override int StatusCode => 400;

    /// <summary>Gets the per-field validation errors.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
