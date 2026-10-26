namespace CarWash.Hub.Domain.Common;

/// <summary>
/// An aggregate refused a state change: an illegal transition or a broken business rule.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// The Domain cannot reference the Application exception family, so this exception carries a
/// code instead, which is also the resource key of the message. The exception-handling middleware
/// maps it to the same <c>422</c> problem document a service-level <c>BusinessRuleException</c>
/// produces.
/// </para>
/// </remarks>
public sealed class DomainRuleException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="code">The rule that was broken; also the resource key of the message.</param>
    /// <param name="arguments">Values substituted into the localized message.</param>
    public DomainRuleException(string code, params object[] arguments)
        : base(code)
    {
        Code = code;
        Arguments = arguments;
    }

    /// <summary>Creates the exception with no code; prefer the full constructor.</summary>
    public DomainRuleException()
        : this("Error_Conflict")
    {
    }

    /// <summary>Creates the exception with a message and cause; prefer the full constructor.</summary>
    /// <param name="message">The code.</param>
    /// <param name="innerException">The cause.</param>
    public DomainRuleException(string message, Exception innerException)
        : base(message, innerException) =>
        (Code, Arguments) = (message, []);

    /// <summary>Gets the rule that was broken; also the resource key of the message.</summary>
    public string Code { get; }

    /// <summary>Gets the values substituted into the localized message.</summary>
    public IReadOnlyList<object> Arguments { get; }
}
