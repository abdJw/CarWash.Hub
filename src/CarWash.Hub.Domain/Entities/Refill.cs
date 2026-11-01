using CarWash.Hub.Domain.Common;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// A refill transaction: someone refilled some of a machine's services at a given time, each to a
/// capacity in uses.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// <b>Immutable, except for voiding.</b> A refill recorded by mistake is voided - soft-deleted, with
/// a reason - rather than edited; the levels are recomputed as if it had never happened, and the
/// history keeps it, struck through. <see cref="AuditableEntity{TKey}.CreatedBy"/> is who recorded
/// it and <see cref="AuditableEntity{TKey}.DeletedBy"/> who voided it.
/// </para>
/// <para>
/// <see cref="RefilledAt"/> is when the tank was actually refilled, which may be earlier than when it
/// was recorded: only uses after that time count against the new capacity.
/// </para>
/// </remarks>
public class Refill : AuditableEntity<Guid>
{
    /// <summary>The longest note kept.</summary>
    public const int NotesMaxLength = 500;

    /// <summary>The longest void reason kept.</summary>
    public const int VoidReasonMaxLength = 300;

    /// <summary>Gets or sets the machine refilled.</summary>
    public Guid MachineId { get; set; }

    /// <summary>Gets or sets the machine refilled.</summary>
    public Machine Machine { get; set; } = null!;

    /// <summary>Gets or sets when the refill was done, in UTC.</summary>
    public DateTimeOffset RefilledAt { get; set; }

    /// <summary>Gets or sets a free-text note, such as the product or batch used.</summary>
    public string? Notes { get; set; }

    /// <summary>Gets or sets why the refill was voided; null while it stands.</summary>
    public string? VoidReason { get; set; }

    /// <summary>Gets the services refilled, one item each.</summary>
    public ICollection<RefillItem> Items { get; } = [];
}

/// <summary>One service refilled in a <see cref="Refill"/>.</summary>
/// <remarks>Layer: Domain. Hidden with its refill when that is voided.</remarks>
public class RefillItem : BaseEntity<Guid>
{
    /// <summary>The largest capacity accepted, in uses.</summary>
    public const int MaxCapacity = 100_000;

    /// <summary>Gets or sets the refill.</summary>
    public Guid RefillId { get; set; }

    /// <summary>Gets or sets the refill.</summary>
    public Refill Refill { get; set; } = null!;

    /// <summary>Gets or sets the service refilled.</summary>
    public string ServiceCode { get; set; } = string.Empty;

    /// <summary>Gets or sets how many uses the service must give after this refill.</summary>
    public int Capacity { get; set; }
}

/// <summary>
/// The level of one service on one machine: how much of its last refill is left.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// Written only by the processing worker - on each use, and by recomputing from the usage history
/// whenever a refill is recorded or voided or the catalog changes - so the UI never races it.
/// Not row-versioned for the same reason as <see cref="MachineState"/>.
/// </para>
/// </remarks>
public class MachineServiceState : BaseEntity<long>
{
    /// <summary>Gets or sets the machine.</summary>
    public Guid MachineId { get; set; }

    /// <summary>Gets or sets the machine.</summary>
    public Machine Machine { get; set; } = null!;

    /// <summary>Gets or sets the service.</summary>
    public string ServiceCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the refill the level is measured from; null if never refilled.</summary>
    public Guid? CurrentRefillId { get; set; }

    /// <summary>Gets or sets when that refill was done.</summary>
    public DateTimeOffset? RefilledAt { get; set; }

    /// <summary>Gets or sets that refill's capacity for this service, in uses.</summary>
    public int? Capacity { get; set; }

    /// <summary>Gets or sets the uses since that refill.</summary>
    public int UsedSinceRefill { get; set; }

    /// <summary>Gets or sets the level.</summary>
    public ServiceLevel Level { get; set; }

    /// <summary>Gets or sets when the service is expected to run out at its usual pace; null when unknown or far off.</summary>
    public DateTimeOffset? ForecastEmptyAt { get; set; }

    /// <summary>Gets or sets the average uses per day the forecast is based on.</summary>
    public decimal? AverageDailyUses { get; set; }

    /// <summary>Gets or sets when the service was last used on this machine.</summary>
    public DateTimeOffset? LastUsageAt { get; set; }

    /// <summary>Gets or sets when the worker last wrote this row.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets the uses left before the capacity is reached; null if never refilled. Negative once overused.</summary>
    public int? Remaining => Capacity - UsedSinceRefill;
}

/// <summary>
/// A request for the worker to recompute derived state, written in the same save as the change that
/// needs it.
/// </summary>
/// <remarks>
/// Layer: Domain. Durable, so a change saved just before a recycle is still applied after it; the
/// worker deletes each request once applied.
/// </remarks>
public class StateRebuildRequest : BaseEntity<long>
{
    /// <summary>Gets or sets the machine to recompute; null for every machine.</summary>
    public Guid? MachineId { get; set; }

    /// <summary>Gets or sets what changed.</summary>
    public RebuildReason Reason { get; set; }

    /// <summary>Gets or sets when the request was made, in UTC.</summary>
    public DateTimeOffset RequestedAt { get; set; }
}
