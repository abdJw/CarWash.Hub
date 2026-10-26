using CarWash.Hub.Domain.Common;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// A gas station hosting one or more self-service car wash machines.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// Created in Phase 1 with no endpoints so that <see cref="Machine.GasStationId"/> can be a real
/// foreign key from day one. Phase 2 adds station management and the station portal on top of
/// this table without restructuring it.
/// </para>
/// </remarks>
public class GasStation : AuditableEntity<Guid>, IHasRowVersion
{
    /// <summary>Gets or sets the short, unique station code, for example <c>RUH-01</c>.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the English name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the Arabic name.</summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>Gets or sets the city.</summary>
    public string? City { get; set; }

    /// <summary>Gets or sets the street address.</summary>
    public string? Address { get; set; }

    /// <summary>Gets or sets a value indicating whether the station is in service.</summary>
    public bool IsActive { get; set; } = true;

    /// <inheritdoc />
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Gets the machines installed at this station.</summary>
    public ICollection<Machine> Machines { get; } = [];
}
