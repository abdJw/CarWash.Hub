using CarWash.Hub.Domain.Common;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// The price of one use of a service at a station, from a business day on.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// Prices are dated rather than edited: the price on a day is the row with the latest
/// <see cref="EffectiveFrom"/> on or before it, so a new price never rewrites what earlier days
/// earned. A price entered by mistake is deleted - soft-deleted, so the history keeps who set and
/// who removed it - and the one before it applies again.
/// </para>
/// <para>In Jordanian dinars, to the fils (three decimals).</para>
/// </remarks>
public class StationServicePrice : AuditableEntity<Guid>
{
    /// <summary>The highest price accepted for one use.</summary>
    public const decimal MaxUnitPrice = 999.999m;

    /// <summary>The longest note kept.</summary>
    public const int NoteMaxLength = 200;

    /// <summary>Gets or sets the station.</summary>
    public Guid GasStationId { get; set; }

    /// <summary>Gets or sets the service code.</summary>
    public string ServiceCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the price of one use, in dinars.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Gets or sets the first business day the price applies to.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Gets or sets a free-text note, such as why the price changed.</summary>
    public string? Note { get; set; }
}
