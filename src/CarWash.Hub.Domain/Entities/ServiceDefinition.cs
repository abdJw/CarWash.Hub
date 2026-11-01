using CarWash.Hub.Domain.Common;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// A service a machine sells - soap, water, vacuum - as named by the <c>service</c> field of its
/// messages, with how the dashboard shows it and how its refills are tracked.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// <b>Self-registering, like machines.</b> The catalog is seeded with the services known today. A
/// code nobody has configured yet - new firmware sending <c>wax</c> - is added automatically, with
/// <see cref="IsConfigured"/> false and refill tracking off, so its usage is recorded from the
/// first message and the owner names it later.
/// </para>
/// <para>
/// Other tables refer to a service by <see cref="Code"/>, which is therefore immutable.
/// </para>
/// </remarks>
public class ServiceDefinition : AuditableEntity<Guid>, IHasRowVersion
{
    /// <summary>The longest service code accepted.</summary>
    public const int CodeMaxLength = 32;

    /// <summary>The default share of capacity left at which a service counts as running low.</summary>
    public const int DefaultLowThresholdPercent = 20;

    /// <summary>Gets or sets the code machines send, lower-case, for example <c>soap</c>.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the English name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the Arabic name.</summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>Gets or sets the icon key the dashboard maps to an icon, for example <c>soap</c>.</summary>
    public string? Icon { get; set; }

    /// <summary>Gets or sets the colour used for this service in chips and charts, as <c>#RRGGBB</c>.</summary>
    public string? Color { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether refills of this service are tracked - remaining
    /// level, run-out forecast and refill alerts.
    /// </summary>
    public bool TracksRefill { get; set; }

    /// <summary>Gets or sets the capacity in uses a refill form suggests when a machine has no earlier refill.</summary>
    public int? DefaultCapacity { get; set; }

    /// <summary>Gets or sets the share of capacity left, in percent, at which the service counts as running low.</summary>
    public int LowThresholdPercent { get; set; } = DefaultLowThresholdPercent;

    /// <summary>Gets or sets the position of the service in lists and charts.</summary>
    public int SortOrder { get; set; }

    /// <summary>Gets or sets a value indicating whether the service is shown and tracked.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the owner has reviewed this service. False for a code
    /// the machines introduced on their own.
    /// </summary>
    public bool IsConfigured { get; set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; set; } = [];
}
