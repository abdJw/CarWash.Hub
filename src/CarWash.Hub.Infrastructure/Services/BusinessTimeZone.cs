using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Infrastructure.Services;

/// <summary>
/// The dashboard's time zone, resolved once from <see cref="DashboardOptions.TimeZone"/>.
/// </summary>
/// <remarks>
/// Layer: Infrastructure.
/// <para>
/// IANA ids (<c>Asia/Amman</c>) resolve on Windows through ICU, which shared Windows hosting may
/// lack; the Windows ids of the zones the business uses are tried as a fallback, so the dashboard
/// shows local time either way. An id that resolves to nothing fails startup rather than silently
/// showing UTC.
/// </para>
/// </remarks>
public sealed class BusinessTimeZone : IBusinessTimeZone
{
    private static readonly Dictionary<string, string> WindowsFallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asia/Amman"] = "Jordan Standard Time",
        ["Asia/Riyadh"] = "Arab Standard Time",
        ["Asia/Dubai"] = "Arabian Standard Time",
    };

    /// <summary>Resolves the configured zone.</summary>
    /// <param name="options">The dashboard settings.</param>
    /// <exception cref="InvalidOperationException">The zone is unknown on this host.</exception>
    public BusinessTimeZone(IOptions<DashboardOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var id = options.Value.TimeZone;
        Zone = Find(id)
               ?? (WindowsFallbacks.TryGetValue(id, out var windowsId) ? Find(windowsId) : null)
               ?? throw new InvalidOperationException(
                   $"Time zone '{id}' (Dashboard:TimeZone) is not known on this host. Use an IANA id such as Asia/Amman or a Windows id such as Jordan Standard Time.");
    }

    /// <inheritdoc />
    public TimeZoneInfo Zone { get; }

    /// <inheritdoc />
    public DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    /// <inheritdoc />
    public DateTimeOffset StartOfDay(DateTimeOffset instant)
    {
        var local = ToLocal(instant);
        var midnight = local.Date;
        return new DateTimeOffset(midnight, Zone.GetUtcOffset(midnight));
    }

    private static TimeZoneInfo? Find(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null;
}
