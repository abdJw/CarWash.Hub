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
    /// <summary>The quiet hours a new station starts with: 23:00.</summary>
    public static readonly TimeOnly DefaultQuietHoursStart = new(23, 0);

    /// <summary>The quiet hours a new station starts with: until 07:00.</summary>
    public static readonly TimeOnly DefaultQuietHoursEnd = new(7, 0);

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

    /// <summary>
    /// Gets or sets when the station's quiet hours start, in its local time - usually closing time.
    /// Silence during quiet hours does not count towards a no-activity alert. Null, with
    /// <see cref="QuietHoursEnd"/>, for a station open 24 hours.
    /// </summary>
    public TimeOnly? QuietHoursStart { get; set; } = DefaultQuietHoursStart;

    /// <summary>Gets or sets when the station's quiet hours end, in its local time; may be the next day.</summary>
    public TimeOnly? QuietHoursEnd { get; set; } = DefaultQuietHoursEnd;

    /// <inheritdoc />
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Gets the machines installed at this station.</summary>
    public ICollection<Machine> Machines { get; } = [];
}
