using CarWash.Hub.Domain.Common;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// A self-service car wash machine, identified by the <c>machineId</c> its firmware sends.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// <b>Registered automatically.</b> The first message carrying an unknown <c>machineId</c> creates
/// the row, so engineers can start sending before anyone has typed the fleet in. Such a machine
/// belongs to no station (<see cref="GasStationId"/> is null) until the owner assigns it in the
/// Phase 2 dashboard - which also keeps a spoofed id off every station's dashboard.
/// </para>
/// <para>
/// <b>Last-seen is not stored here.</b> It is the newest <see cref="MachineMessage.ReceivedAt"/>,
/// read through the <c>(machine_id, received_at DESC)</c> index. Writing it here on every message
/// would bump <see cref="RowVersion"/> constantly and make every admin edit of the machine fail
/// with a concurrency conflict. <see cref="AuditableEntity{TKey}.CreatedAt"/> is when the machine
/// was first seen.
/// </para>
/// </remarks>
public class Machine : AuditableEntity<Guid>, IHasRowVersion
{
    /// <summary>The longest machine identifier accepted.</summary>
    public const int CodeMaxLength = 100;

    /// <summary>
    /// Gets or sets the identifier the machine reports as <c>machineId</c>. Unique, and compared
    /// case-insensitively by the database collation.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets an optional friendly name, set by the owner.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Gets or sets the station the machine is installed at; null until assigned.</summary>
    public Guid? GasStationId { get; set; }

    /// <summary>Gets or sets the station the machine is installed at.</summary>
    public GasStation? GasStation { get; set; }

    /// <summary>Gets or sets a value indicating whether the machine is in service.</summary>
    public bool IsActive { get; set; } = true;

    /// <inheritdoc />
    public byte[] RowVersion { get; set; } = [];

    /// <summary>
    /// Gets the messages received from this machine. Used in projections only (latest, last-seen);
    /// never loaded whole, since a machine accumulates messages indefinitely.
    /// </summary>
    public ICollection<MachineMessage> Messages { get; } = [];
}
