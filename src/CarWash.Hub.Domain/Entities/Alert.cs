using CarWash.Hub.Domain.Common;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// Something about a machine that needs attention: a service running low or out, or a machine that
/// has gone quiet. Open until the condition clears; kept afterwards as history.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// <b>Raised and resolved only by the processing worker</b>, which compares what should be open
/// with what is open after every batch and once a minute. The dashboard only acknowledges: the
/// worker never writes the acknowledgement columns and acknowledging writes nothing else, so the
/// two never overwrite each other and the row needs no rowversion.
/// </para>
/// <para>
/// The details - what was left, the forecast, when the machine was last heard from - are a snapshot
/// taken when the alert was raised; the machine's page shows the live level.
/// </para>
/// </remarks>
public class Alert : BaseEntity<long>
{
    /// <summary>Gets or sets the machine.</summary>
    public Guid MachineId { get; set; }

    /// <summary>Gets or sets the machine.</summary>
    public Machine Machine { get; set; } = null!;

    /// <summary>Gets or sets the service a level alert is about; null for <see cref="AlertType.NoActivity"/>.</summary>
    public string? ServiceCode { get; set; }

    /// <summary>Gets or sets what the alert is about.</summary>
    public AlertType Type { get; set; }

    /// <summary>Gets or sets how urgent it is.</summary>
    public AlertSeverity Severity { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether raising it should notify people (a toast now; email
    /// and push later). Off when it only restates an alert they already had.
    /// </summary>
    public bool Notify { get; set; }

    /// <summary>Gets or sets when it was raised, in UTC.</summary>
    public DateTimeOffset RaisedAt { get; set; }

    /// <summary>Gets or sets when it closed, in UTC; null while open.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Gets or sets why it closed; null while open.</summary>
    public AlertResolution? Resolution { get; set; }

    /// <summary>
    /// Gets or sets when the service had last been refilled at the time of the alert; a later refill
    /// resolves it as <see cref="AlertResolution.Refilled"/>. A void moves the refill time back, so it
    /// is never mistaken for a refill.
    /// </summary>
    public DateTimeOffset? BaselineRefilledAt { get; set; }

    /// <summary>Gets or sets the uses left when raised (negative after empty).</summary>
    public int? Remaining { get; set; }

    /// <summary>Gets or sets the capacity of the refill being used up.</summary>
    public int? Capacity { get; set; }

    /// <summary>Gets or sets the forecast run-out time when raised.</summary>
    public DateTimeOffset? ForecastEmptyAt { get; set; }

    /// <summary>Gets or sets when the machine was last heard from, for <see cref="AlertType.NoActivity"/>.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>Gets or sets when someone said they are handling it, in UTC.</summary>
    public DateTimeOffset? AcknowledgedAt { get; set; }

    /// <summary>Gets or sets who acknowledged it.</summary>
    public Guid? AcknowledgedBy { get; set; }
}
