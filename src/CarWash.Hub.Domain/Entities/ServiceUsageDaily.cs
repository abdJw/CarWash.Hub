using CarWash.Hub.Domain.Common;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// The number of uses of one service on one machine, at one station, on one business day.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// The finance rollup of <see cref="ServiceUsage"/>, maintained by the processing worker in the same
/// transaction as the usage rows. A day is a business day in the display time zone, which is also
/// the grain prices change at, so revenue is <see cref="UsageCount"/> times one price.
/// </para>
/// <para>
/// <see cref="GasStationId"/> is the station the use was stamped with, not the machine's current
/// one. There are deliberately no navigations: a soft-deleted machine or station must not hide the
/// money it earned. Kept forever - housekeeping must never prune this table.
/// </para>
/// </remarks>
public class ServiceUsageDaily : BaseEntity<long>
{
    /// <summary>Gets or sets the business day.</summary>
    public DateOnly BusinessDate { get; set; }

    /// <summary>Gets or sets the station the uses were made at; null for an unassigned machine.</summary>
    public Guid? GasStationId { get; set; }

    /// <summary>Gets or sets the machine.</summary>
    public Guid MachineId { get; set; }

    /// <summary>Gets or sets the service code.</summary>
    public string ServiceCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of uses on the day.</summary>
    public int UsageCount { get; set; }
}
