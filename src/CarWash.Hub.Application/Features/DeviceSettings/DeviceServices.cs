using CarWash.Hub.Domain.Entities;

namespace CarWash.Hub.Application.Features.DeviceSettings;

/// <summary>A service the event firmware can be configured for, and how.</summary>
/// <param name="Code">The service code, exactly as the firmware names it.</param>
/// <param name="Positions">The selector positions: 3, or 1 for a single-duration service.</param>
/// <param name="SuggestedSeconds">Starting run times, from the integration guide's example; not mandatory.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record DeviceService(string Code, int Positions, IReadOnlyList<int> SuggestedSeconds);

/// <summary>The services the machines' settings cover, and the limits the firmware enforces on them.</summary>
/// <remarks>
/// Layer: Application. From the ESP32 integration guide (protocol baseline 2026-11-03): a settings
/// response is always a complete snapshot of these four, in this order. A value outside these limits
/// would be refused by the machine as <c>invalid_settings</c>, so it is refused here first.
/// </remarks>
public static class DeviceServices
{
    /// <summary>The cheapest price a machine accepts, in qirsh (JOD 0.05).</summary>
    public const int MinPriceQirsh = 5;

    /// <summary>The dearest price a machine accepts, in qirsh (JOD 9.95).</summary>
    public const int MaxPriceQirsh = 995;

    /// <summary>The step prices come in, in qirsh: coins are counted in 5-qirsh units.</summary>
    public const int PriceStepQirsh = 5;

    /// <summary>Gets the configurable services, in the order the settings document lists them.</summary>
    public static IReadOnlyList<DeviceService> All { get; } =
    [
        new("water", 3, [180, 180, 210]),
        new("soap", 3, [60, 90, 90]),
        new("minute_water", 1, [60]),
        new("vacuum", 3, [180, 180, 210]),
    ];

    /// <summary>Returns the configurable service with a code, or null.</summary>
    /// <param name="code">The service code, any case.</param>
    /// <returns>The service, or null when it is not configurable on the machines.</returns>
    public static DeviceService? Find(string? code) =>
        code is null ? null : All.FirstOrDefault(service => string.Equals(service.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a price in dinars can be sent to a machine: 0.05 to 9.95, in steps of 0.05.</summary>
    /// <param name="price">The price, in dinars.</param>
    /// <returns><see langword="true"/> if the machine will accept it.</returns>
    public static bool IsDevicePrice(decimal price) => ToQirsh(price) is not null;

    /// <summary>Converts a price in dinars to the qirsh the machine is sent, if it can be sent.</summary>
    /// <param name="price">The price, in dinars.</param>
    /// <returns>The price in qirsh, or null when the machine would refuse it.</returns>
    public static int? ToQirsh(decimal price)
    {
        var qirsh = price * 100m;
        if (qirsh != decimal.Truncate(qirsh) || qirsh < MinPriceQirsh || qirsh > MaxPriceQirsh)
        {
            return null;
        }

        var whole = (int)qirsh;
        return whole % PriceStepQirsh == 0 ? whole : null;
    }

    /// <summary>Whether a run time can be sent to a machine.</summary>
    /// <param name="seconds">The run time.</param>
    /// <returns><see langword="true"/> if it is within the machine's limits.</returns>
    public static bool IsDuration(int seconds) =>
        seconds is >= StationServiceDuration.MinSeconds and <= StationServiceDuration.MaxSeconds;
}
