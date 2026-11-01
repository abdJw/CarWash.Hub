using CarWash.Hub.Application.Features.Dashboard;

namespace CarWash.Hub.Web.Theming;

/// <summary>
/// Everything a component needs to draw a service: its name, icon, colour slot and position.
/// </summary>
/// <remarks>
/// Layer: Web. Built once per page from the catalog, so every chart, chip and legend on the page
/// agrees. A code missing from the catalog - usage of a service retired since - is still drawn, by
/// its code, in the neutral colour.
/// </remarks>
public sealed class ServiceLookup
{
    private readonly Dictionary<string, ServiceInfoDto> _byCode;
    private readonly IReadOnlyDictionary<string, int> _slots;

    /// <summary>Builds the lookup.</summary>
    /// <param name="services">The catalog, in display order.</param>
    public ServiceLookup(IReadOnlyList<ServiceInfoDto> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Ordered = services;
        _byCode = services.ToDictionary(service => service.Code, StringComparer.OrdinalIgnoreCase);
        _slots = ServicePalette.AssignSlots(services);
    }

    /// <summary>Gets a lookup with no services, for the moment before the catalog has loaded.</summary>
    public static ServiceLookup Empty { get; } = new([]);

    /// <summary>Gets the services in display order.</summary>
    public IReadOnlyList<ServiceInfoDto> Ordered { get; }

    /// <summary>Gets a value indicating whether any service is refilled, so a refill can be recorded at all.</summary>
    public bool AnyTracked => Ordered.Any(service => service.TracksRefill);

    /// <summary>Returns a service's name in the current language.</summary>
    /// <param name="code">The service code.</param>
    /// <returns>The name, or the code when the service is unknown.</returns>
    public string Name(string code) => _byCode.TryGetValue(code, out var service) ? service.Name : code;

    /// <summary>Returns a service's icon.</summary>
    /// <param name="code">The service code.</param>
    /// <returns>A MudBlazor SVG icon.</returns>
    public string Icon(string code) => ServicePalette.Icon(_byCode.TryGetValue(code, out var service) ? service.Icon : null);

    /// <summary>Returns the CSS class carrying a service's colour.</summary>
    /// <param name="code">The service code.</param>
    /// <returns>For example <c>svc-3</c>.</returns>
    public string Css(string code) => ServicePalette.CssClass(_slots.TryGetValue(code, out var slot) ? slot : 0);

    /// <summary>Returns a service's position, for ordering segments and rows.</summary>
    /// <param name="code">The service code.</param>
    /// <returns>Its sort order; unknown services last.</returns>
    public int Order(string code) => _byCode.TryGetValue(code, out var service) ? service.SortOrder : int.MaxValue;

    /// <summary>Orders service counts by display order.</summary>
    /// <param name="counts">The counts.</param>
    /// <returns>The counts, in the order services are drawn.</returns>
    public IEnumerable<ServiceCountDto> InOrder(IEnumerable<ServiceCountDto> counts) =>
        counts.OrderBy(count => Order(count.Service)).ThenBy(count => count.Service, StringComparer.Ordinal);
}
