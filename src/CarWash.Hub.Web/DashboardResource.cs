namespace CarWash.Hub.Web;

/// <summary>
/// Marker type for the dashboard's string resources, <c>Resources/DashboardResource.resx</c> and
/// its Arabic twin. Inject <c>IStringLocalizer&lt;DashboardResource&gt;</c> and look up a
/// <see cref="Localization.DashboardText"/> key.
/// </summary>
/// <remarks>Layer: Web. In the root namespace so the resource name resolves without configuration.</remarks>
public sealed class DashboardResource;
