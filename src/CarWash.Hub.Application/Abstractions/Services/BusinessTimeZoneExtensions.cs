namespace CarWash.Hub.Application.Abstractions.Services;

/// <summary>Business days in the <see cref="IBusinessTimeZone"/>.</summary>
/// <remarks>Layer: Application.</remarks>
public static class BusinessTimeZoneExtensions
{
    /// <summary>Returns the business day an instant falls on.</summary>
    /// <param name="timeZone">The business time zone.</param>
    /// <param name="instant">The instant.</param>
    /// <returns>The local calendar date.</returns>
    public static DateOnly DateOf(this IBusinessTimeZone timeZone, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return DateOnly.FromDateTime(timeZone.ToLocal(instant).DateTime);
    }
}
