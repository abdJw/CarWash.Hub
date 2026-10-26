using CarWash.Hub.Application.Abstractions.Services;

namespace CarWash.Hub.Infrastructure.Services;

/// <summary>
/// Supplies the system clock in UTC.
/// </summary>
/// <remarks>
/// Layer: Infrastructure. Registered as a singleton - it holds no state, and allocating one
/// per request for a property read would be waste.
/// </remarks>
public sealed class DateTimeProvider : IDateTimeProvider
{
    /// <inheritdoc />
    /// <remarks>
    /// <see cref="DateTimeOffset.UtcNow"/> always carries a zero offset. Returning <c>Now</c>
    /// here would store the server's local offset, so rows written by two servers in
    /// different zones would no longer compare cleanly.
    /// </remarks>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
