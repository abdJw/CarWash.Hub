namespace CarWash.Hub.Application.Abstractions.Services;

/// <summary>
/// Supplies the current time.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// Injected rather than calling <c>DateTimeOffset.UtcNow</c> directly so that token expiry,
/// lockout windows and audit timestamps are all testable without waiting in real time.
/// </para>
/// <para>
/// Always UTC. Timestamps are stored as <c>datetimeoffset</c> and compared across rows, so a
/// local offset here would make otherwise-identical instants sort and filter inconsistently.
/// Asia/Riyadh is applied only when a business date is shown or derived.
/// </para>
/// </remarks>
public interface IDateTimeProvider
{
    /// <summary>Gets the current instant in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
