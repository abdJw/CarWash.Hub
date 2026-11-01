using CarWash.Hub.Application.Features.Alerts;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.Web.Formatting;
using CarWash.Hub.Web.Localization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace CarWash.Hub.Web.Theming;

/// <summary>
/// How an alert looks and reads: the reserved status colour of its severity, an icon and a word for
/// its type, and one plain sentence - the same in the bell, a toast and the alert list.
/// </summary>
/// <remarks>Layer: Web.</remarks>
public static class AlertPresentation
{
    // First-strong isolates: a MAC address or a Latin service name keeps its own order inside an
    // Arabic sentence, also in a toast, which is plain text and cannot use <bdi>.
    private const char Isolate = '⁨';
    private const char EndIsolate = '⁩';

    /// <summary>Returns the status kind - the CSS modifier - for a severity.</summary>
    /// <param name="severity">The severity.</param>
    /// <returns><c>critical</c> or <c>warning</c>.</returns>
    public static string Kind(AlertSeverity severity) => severity == AlertSeverity.Critical ? "critical" : "warning";

    /// <summary>Returns the snackbar severity for an alert's severity.</summary>
    /// <param name="severity">The severity.</param>
    /// <returns>Error for critical, warning otherwise.</returns>
    public static Severity ToastSeverity(AlertSeverity severity) => severity == AlertSeverity.Critical ? Severity.Error : Severity.Warning;

    /// <summary>Returns the icon for an alert type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>A Material icon.</returns>
    public static string Icon(AlertType type) => type switch
    {
        AlertType.RunsOutSoon => Icons.Material.Rounded.HourglassBottom,
        AlertType.Low => Icons.Material.Rounded.Warning,
        AlertType.Empty => Icons.Material.Rounded.Error,
        AlertType.UsedAfterEmpty => Icons.Material.Rounded.Report,
        _ => Icons.Material.Rounded.SensorsOff,
    };

    /// <summary>Returns the word for an alert type.</summary>
    /// <param name="type">The type.</param>
    /// <param name="localizer">The dashboard's words.</param>
    /// <returns>For example "Low".</returns>
    public static string Word(AlertType type, IStringLocalizer<DashboardResource> localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer[type switch
        {
            AlertType.RunsOutSoon => DashboardText.AlertTypeRunsOutSoon,
            AlertType.Low => DashboardText.AlertTypeLow,
            AlertType.Empty => DashboardText.AlertTypeEmpty,
            AlertType.UsedAfterEmpty => DashboardText.AlertTypeUsedAfterEmpty,
            _ => DashboardText.AlertTypeNoActivity,
        }];
    }

    /// <summary>Returns why an alert closed, as a short phrase.</summary>
    /// <param name="resolution">The resolution.</param>
    /// <param name="localizer">The dashboard's words.</param>
    /// <returns>For example "Closed: refilled".</returns>
    public static string Resolution(AlertResolution resolution, IStringLocalizer<DashboardResource> localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer[resolution switch
        {
            AlertResolution.Refilled => DashboardText.AlertResolutionRefilled,
            AlertResolution.Escalated => DashboardText.AlertResolutionEscalated,
            AlertResolution.ActivityResumed => DashboardText.AlertResolutionActivityResumed,
            AlertResolution.NotEligible => DashboardText.AlertResolutionNotEligible,
            _ => DashboardText.AlertResolutionCleared,
        }];
    }

    /// <summary>Returns the alert as one sentence, for example "Soap is low on Bay 2: 6 of 40 uses left."</summary>
    /// <param name="alert">The alert.</param>
    /// <param name="localizer">The dashboard's words.</param>
    /// <param name="format">Numbers, times and durations.</param>
    /// <param name="now">The current time, for "runs out in".</param>
    /// <returns>The sentence.</returns>
    public static string Sentence(AlertDto alert, IStringLocalizer<DashboardResource> localizer, DashboardFormat format, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(alert);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(format);

        var machine = Isolated(alert.MachineTitle);
        var service = Isolated(alert.ServiceName ?? alert.Service ?? string.Empty);

        return alert.Type switch
        {
            AlertType.RunsOutSoon when alert.ForecastEmptyAt is { } at && alert.IsOpen && at > now =>
                localizer[DashboardText.AlertTextRunsOutSoon, service, machine, format.Duration(at - now)],
            AlertType.RunsOutSoon =>
                localizer[DashboardText.AlertTextRunsOutSoonAt, service, machine, alert.ForecastEmptyAt is { } at ? format.DateTime(at) : "—"],
            AlertType.Low =>
                localizer[DashboardText.AlertTextLow, service, machine, DashboardFormat.Number(Math.Max(0, alert.Remaining ?? 0)), DashboardFormat.Number(alert.Capacity ?? 0)],
            AlertType.Empty => localizer[DashboardText.AlertTextEmpty, service, machine],
            AlertType.UsedAfterEmpty => localizer[DashboardText.AlertTextUsedAfterEmpty, service, machine],
            _ => localizer[DashboardText.AlertTextNoActivity, machine, alert.LastSeenAt is { } seen ? format.DateTime(seen) : "—"],
        };
    }

    private static string Isolated(string text) => $"{Isolate}{text}{EndIsolate}";
}
