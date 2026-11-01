using MudBlazor;

namespace CarWash.Hub.Web.Theming;

/// <summary>
/// The dashboard's MudBlazor theme: quiet chrome so the data is the only loud thing on the page.
/// </summary>
/// <remarks>
/// Layer: Web.
/// <para>
/// Surfaces, ink and hairlines are the reference data-viz palette's chart chrome, so chart colours -
/// validated against exactly these surfaces - keep their contrast. The status colours are the
/// reserved good / warning / critical steps, used only for states and always with an icon and a
/// label, never for a service.
/// </para>
/// <para>
/// One system sans for everything, Arabic included: <c>system-ui</c> resolves to Segoe UI on
/// Windows, San Francisco on Apple devices and Roboto/Noto on Android, each with Arabic glyphs -
/// so no web font is downloaded, and the strict content-security policy needs no font origin.
/// </para>
/// </remarks>
public static class DashboardTheme
{
    private static readonly string[] FontStack = ["system-ui", "-apple-system", "Segoe UI", "Roboto", "Noto Sans Arabic", "Tahoma", "sans-serif"];

    /// <summary>Gets the theme.</summary>
    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1c5cab",
            PrimaryContrastText = "#ffffff",
            Secondary = "#52514e",
            Background = "#f9f9f7",
            Surface = "#fcfcfb",
            AppbarBackground = "#fcfcfb",
            AppbarText = "#0b0b0b",
            DrawerBackground = "#fcfcfb",
            DrawerText = "#0b0b0b",
            DrawerIcon = "#52514e",
            TextPrimary = "#0b0b0b",
            TextSecondary = "#52514e",
            TextDisabled = "#898781",
            ActionDefault = "#52514e",
            Divider = "#e1e0d9",
            DividerLight = "#e1e0d9",
            LinesDefault = "#e1e0d9",
            LinesInputs = "#c3c2b7",
            TableLines = "#e1e0d9",
            Success = "#0ca30c",
            Warning = "#fab219",
            WarningContrastText = "#0b0b0b",
            Error = "#d03b3b",
            Info = "#2a78d6",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#5598e7",
            PrimaryContrastText = "#0b0b0b",
            Secondary = "#c3c2b7",
            Background = "#0d0d0d",
            Surface = "#1a1a19",
            AppbarBackground = "#1a1a19",
            AppbarText = "#ffffff",
            DrawerBackground = "#1a1a19",
            DrawerText = "#ffffff",
            DrawerIcon = "#c3c2b7",
            TextPrimary = "#ffffff",
            TextSecondary = "#c3c2b7",
            TextDisabled = "#898781",
            ActionDefault = "#c3c2b7",
            Divider = "#2c2c2a",
            DividerLight = "#2c2c2a",
            LinesDefault = "#2c2c2a",
            LinesInputs = "#383835",
            TableLines = "#2c2c2a",
            Success = "#0ca30c",
            Warning = "#fab219",
            WarningContrastText = "#0b0b0b",
            Error = "#d03b3b",
            Info = "#3987e5",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = FontStack },
            H1 = new H1Typography { FontFamily = FontStack },
            H2 = new H2Typography { FontFamily = FontStack },
            H3 = new H3Typography { FontFamily = FontStack },
            H4 = new H4Typography { FontFamily = FontStack },
            H5 = new H5Typography { FontFamily = FontStack },
            H6 = new H6Typography { FontFamily = FontStack },
            Button = new ButtonTypography { FontFamily = FontStack, TextTransform = "none" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "10px",
            AppbarHeight = "60px",
        },
    };
}
