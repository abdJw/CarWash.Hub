using CarWash.Hub.Domain.Common;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// The number of uses of one service on one machine in one UTC hour.
/// </summary>
/// <remarks>
/// Layer: Domain. A rollup of <see cref="ServiceUsage"/>, maintained by the processing worker in
/// the same transaction as the usage rows, so charts, KPIs and the run-out forecast read a few
/// hundred rows instead of scanning every use. Business days are whole hours apart from UTC in the
/// display time zone, so a day is always a whole number of these buckets.
/// </remarks>
public class ServiceUsageHourly : BaseEntity<long>
{
    /// <summary>Gets or sets the machine.</summary>
    public Guid MachineId { get; set; }

    /// <summary>Gets or sets the machine.</summary>
    public Machine Machine { get; set; } = null!;

    /// <summary>Gets or sets the start of the hour, in UTC, with zero minutes and seconds.</summary>
    public DateTimeOffset HourStart { get; set; }

    /// <summary>Gets or sets the service code.</summary>
    public string ServiceCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of uses in the hour.</summary>
    public int UsageCount { get; set; }
}
