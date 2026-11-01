using CarWash.Hub.Application.Features.Dashboard;
using MudBlazor;

namespace CarWash.Hub.Web.Theming;

/// <summary>
/// Gives each service its colour slot and icon, the same on every page and chart.
/// </summary>
/// <remarks>
/// Layer: Web.
/// <para>
/// <b>Colour follows the service, never its rank.</b> Water, vacuum and soap own the first three
/// categorical slots - the three that stay distinguishable to every colour-vision type in every
/// chart form (validated in light and dark with the dataviz palette validator: worst pair ΔE 9.2
/// under CVD simulation, 20.9 for normal vision). Any other service takes the next free slot by
/// catalog order, so filtering a chart never repaints the services left on it. Past the eighth
/// slot a service is drawn in the neutral "other" colour rather than a generated hue.
/// </para>
/// <para>
/// The colours themselves are CSS custom properties (<c>--series-1</c> … in <c>dashboard.css</c>),
/// stepped separately for the light and dark surfaces; this class only hands out slot numbers.
/// </para>
/// </remarks>
public static class ServicePalette
{
    /// <summary>The number of categorical slots.</summary>
    public const int SlotCount = 8;

    private static readonly Dictionary<string, int> FixedSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        ["water"] = 1,
        ["vacuum"] = 2,
        ["soap"] = 3,
    };

    /// <summary>Returns each service's slot, 1-based; 0 means the neutral "other" colour.</summary>
    /// <param name="services">Every service, in catalog order.</param>
    /// <returns>The slot per service code.</returns>
    public static IReadOnlyDictionary<string, int> AssignSlots(IEnumerable<ServiceInfoDto> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var slots = new Dictionary<string, int>(FixedSlots, StringComparer.OrdinalIgnoreCase);
        var next = FixedSlots.Count + 1;

        foreach (var service in services.OrderBy(service => service.SortOrder).ThenBy(service => service.Code, StringComparer.Ordinal))
        {
            if (!slots.ContainsKey(service.Code))
            {
                slots[service.Code] = next <= SlotCount ? next++ : 0;
            }
        }

        return slots;
    }

    /// <summary>Returns the CSS class that sets <c>--c</c> to a slot's colour.</summary>
    /// <param name="slot">The slot, or 0 for "other".</param>
    /// <returns>The class, such as <c>svc-3</c>.</returns>
    public static string CssClass(int slot) => slot is >= 1 and <= SlotCount ? $"svc-{slot}" : "svc-other";

    /// <summary>Returns the icon for a service's icon key.</summary>
    /// <param name="iconKey">The catalog's icon key.</param>
    /// <returns>A MudBlazor SVG icon.</returns>
    public static string Icon(string? iconKey) => iconKey?.ToLowerInvariant() switch
    {
        "soap" => Icons.Material.Filled.Soap,
        "water" => Icons.Material.Filled.WaterDrop,
        "vacuum" => Icons.Material.Filled.CleaningServices,
        _ => Icons.Material.Filled.LocalCarWash,
    };
}
