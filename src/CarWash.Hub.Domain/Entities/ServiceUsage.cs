using CarWash.Hub.Domain.Common;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// One use of one service on one machine, parsed from a stored message.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// The structured form of the raw <see cref="MachineMessage"/> history: counting uses since a
/// refill is a range count over <c>(machine_id, service_code, occurred_at)</c>. Append-only and
/// derived - deleting a machine's rows and resetting its messages to pending rebuilds them, which is
/// how a parser fix reaches the past.
/// </para>
/// <para>
/// <see cref="MachineMessageId"/> plus <see cref="ItemIndex"/> is unique, so processing a message
/// twice can never count it twice.
/// </para>
/// </remarks>
public class ServiceUsage : BaseEntity<long>
{
    /// <summary>Gets or sets the machine the service was used on.</summary>
    public Guid MachineId { get; set; }

    /// <summary>Gets or sets the machine the service was used on.</summary>
    public Machine Machine { get; set; } = null!;

    /// <summary>
    /// Gets or sets the station the machine stood at when the use was processed; null while it was
    /// unassigned.
    /// </summary>
    /// <remarks>
    /// Stamped once and never moved: a machine that changes station takes its future uses with it,
    /// but the money it already earned stays with the station that earned it.
    /// </remarks>
    public Guid? GasStationId { get; set; }

    /// <summary>Gets or sets the service code, matching <see cref="ServiceDefinition.Code"/>.</summary>
    public string ServiceCode { get; set; } = string.Empty;

    /// <summary>Gets or sets when the use happened, in UTC.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Gets or sets which clock <see cref="OccurredAt"/> came from.</summary>
    public OccurrenceTimeSource OccurredAtSource { get; set; }

    /// <summary>Gets or sets when the message carrying it arrived, in UTC.</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Gets or sets the message this use was parsed from.</summary>
    public long MachineMessageId { get; set; }

    /// <summary>Gets or sets the position of the event within its message: 0, or its index in an array.</summary>
    public short ItemIndex { get; set; }

    /// <summary>
    /// Gets or sets the service price the machine charged, in JOD (<c>price_qirsh</c> / 100); null when
    /// the firmware does not report prices, and the use is then valued at the station's price list.
    /// </summary>
    public decimal? SaleAmount { get; set; }

    /// <summary>
    /// Gets or sets the cash the customer inserted, in JOD (<c>paid_qirsh</c> / 100). May exceed
    /// <see cref="SaleAmount"/>; kept separately so sales and collected cash are never mixed.
    /// </summary>
    public decimal? PaidAmount { get; set; }

    /// <summary>Gets or sets the run time the customer selected, in seconds; null when not reported.</summary>
    public int? DurationSeconds { get; set; }
}
