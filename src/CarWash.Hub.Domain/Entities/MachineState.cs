using CarWash.Hub.Domain.Common;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// What is known about a machine from its messages: when it was last heard from and last used.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// <b>Why a separate row.</b> <see cref="Machine"/> is row-versioned for admin edits; writing
/// last-seen there on every message would make each edit fail with a concurrency conflict. This row
/// shares the machine's key, has no rowversion, and is written only by the processing worker, so the
/// dashboard reads a machine's live state with a key lookup instead of aggregating its messages.
/// </para>
/// </remarks>
public class MachineState : BaseEntity<Guid>
{
    /// <summary>The longest boot id kept; the firmware sends 16 hex digits.</summary>
    public const int BootIdMaxLength = 32;

    /// <summary>Gets or sets the machine; its key is this row's key.</summary>
    public Machine Machine { get; set; } = null!;

    /// <summary>Gets or sets when the newest processed message arrived, in UTC.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>Gets or sets the newest processed message.</summary>
    public long LastMessageId { get; set; }

    /// <summary>Gets or sets when a service was last used, in UTC; null if never.</summary>
    public DateTimeOffset? LastUsageAt { get; set; }

    /// <summary>Gets or sets the service used last; null if never.</summary>
    public string? LastServiceCode { get; set; }

    /// <summary>Gets or sets the number of uses recorded, across services, since the machine was first seen.</summary>
    public long TotalUses { get; set; }

    /// <summary>Gets or sets when the worker last wrote this row, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets whether the machine last reported water as available; null until it reports it.
    /// A last-known value, not evidence the machine is still online - read it with <see cref="LastSeenAt"/>.
    /// </summary>
    public bool? WaterAvailable { get; set; }

    /// <summary>Gets or sets when the message carrying <see cref="WaterAvailable"/> arrived, in UTC.</summary>
    public DateTimeOffset? WaterReportedAt { get; set; }

    /// <summary>Gets or sets when <see cref="WaterAvailable"/> last changed value, in UTC: "no water since".</summary>
    public DateTimeOffset? WaterChangedAt { get; set; }

    /// <summary>
    /// Gets or sets the message <see cref="WaterAvailable"/> came from. Messages can be processed out
    /// of order, so a report only replaces the water state if it came from a newer message.
    /// </summary>
    public long? WaterMessageId { get; set; }

    /// <summary>
    /// Gets or sets when the machine last sent a device report (boot, water status, payment or
    /// settings acknowledgement), in UTC. Null for firmware that only reports uses; non-null marks a
    /// machine that reports every 30 seconds, so silence means it is offline rather than just idle.
    /// </summary>
    public DateTimeOffset? LastHeartbeatAt { get; set; }

    /// <summary>Gets or sets when the machine last reported starting up, in UTC.</summary>
    public DateTimeOffset? LastBootAt { get; set; }

    /// <summary>Gets or sets the firmware's id for that start-up; a new one each boot.</summary>
    public string? LastBootId { get; set; }
}
