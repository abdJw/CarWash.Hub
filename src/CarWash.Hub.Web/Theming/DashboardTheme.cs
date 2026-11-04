using MudBlazor;

namespace CarWash.Hub.Web.Theming;

/// <summary>
/// The dashboard's MudBlazor theme: quiet chrome so the data is the only loud thing on the page.
/// </summary>
/// <remarks>
/// Layer: Web.
/// <para>
/// Cool slate surfaces, ink and hairlines with an ocean-blue primary and a cyan accent; the chart
/// colours were validated against the earlier, warmer surfaces and keep their contrast on these
/// (white is lighter; the dark navy has the same luminance as the old dark grey). The status colours are the
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
            Primary = "#2563eb",
            PrimaryContrastText = "#ffffff",
            Secondary = "#475569",
            Tertiary = "#06b6d4",
            Background = "#f4f7fb",
            Surface = "#ffffff",
            AppbarBackground = "#ffffff",
            AppbarText = "#0f172a",
            DrawerBackground = "#ffffff",
            DrawerText = "#0f172a",
            DrawerIcon = "#475569",
            TextPrimary = "#0f172a",
            TextSecondary = "#475569",
            TextDisabled = "#94a3b8",
            ActionDefault = "#475569",
            Divider = "#e2e8f0",
            DividerLight = "#e2e8f0",
            LinesDefault = "#e2e8f0",
            LinesInputs = "#cbd5e1",
            TableLines = "#e2e8f0",
            Success = "#0ca30c",
            Warning = "#fab219",
            WarningContrastText = "#0b0b0b",
            Error = "#d03b3b",
            Info = "#3b82f6",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#60a5fa",
            PrimaryContrastText = "#0b1220",
            Secondary = "#94a3b8",
            Tertiary = "#22d3ee",
            Background = "#0b1220",
            Surface = "#121a2b",
            AppbarBackground = "#121a2b",
            AppbarText = "#f1f5f9",
            DrawerBackground = "#121a2b",
            DrawerText = "#f1f5f9",
            DrawerIcon = "#94a3b8",
            TextPrimary = "#f1f5f9",
            TextSecondary = "#94a3b8",
            TextDisabled = "#64748b",
            ActionDefault = "#94a3b8",
            Divider = "#1f2a3d",
            DividerLight = "#1f2a3d",
            LinesDefault = "#1f2a3d",
            LinesInputs = "#334155",
            TableLines = "#1f2a3d",
            Success = "#0ca30c",
            Warning = "#fab219",
            WarningContrastText = "#0b0b0b",
            Error = "#d03b3b",
            Info = "#60a5fa",
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
            // Normal letter spacing: any tracking pulls joined Arabic letters apart.
            Button = new ButtonTypography { FontFamily = FontStack, TextTransform = "none", LetterSpacing = "normal", FontWeight = "600" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
            AppbarHeight = "60px",
        },
    };
}
