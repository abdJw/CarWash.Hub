using MudBlazor;

namespace CarWash.Hub.Web.Components.Admin;

/// <summary>How the admin dialogs open.</summary>
/// <remarks>Layer: Web. Full screen on a phone, a comfortable width elsewhere.</remarks>
public static class AdminDialogs
{
    /// <summary>Gets the options every admin dialog opens with.</summary>
    public static DialogOptions Options { get; } = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        CloseButton = true,
        CloseOnEscapeKey = true,
        BackdropClick = false,
    };
}
