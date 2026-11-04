using CarWash.Hub.Domain.Common;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// The run times a station's machines offer for one service: one per position of the selector
/// switch, in seconds.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// Sent to the machines with the station's current prices, as their settings. Unlike prices these
/// are not dated: a run time changes what the machine does from now on, not what anything earned,
/// so the row is simply edited. Most services have three positions; a single-position service
/// (minute water) uses <see cref="Duration1Seconds"/> only.
/// </para>
/// </remarks>
public class StationServiceDuration : AuditableEntity<Guid>
{
    /// <summary>The shortest run time a machine accepts, in seconds.</summary>
    public const int MinSeconds = 1;

    /// <summary>The longest run time a machine accepts, in seconds.</summary>
    public const int MaxSeconds = 5999;

    /// <summary>Gets or sets the station.</summary>
    public Guid GasStationId { get; set; }

    /// <summary>Gets or sets the service code.</summary>
    public string ServiceCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the run time of selector position 1, in seconds.</summary>
    public int Duration1Seconds { get; set; }

    /// <summary>Gets or sets the run time of selector position 2; null for a single-position service.</summary>
    public int? Duration2Seconds { get; set; }

    /// <summary>Gets or sets the run time of selector position 3; null for a single-position service.</summary>
    public int? Duration3Seconds { get; set; }

    /// <summary>Returns the run times in selector order, without the unused positions.</summary>
    /// <returns>One to three run times.</returns>
    public IReadOnlyList<int> Seconds() =>
        Duration2Seconds is { } second && Duration3Seconds is { } third
            ? [Duration1Seconds, second, third]
            : [Duration1Seconds];
}
