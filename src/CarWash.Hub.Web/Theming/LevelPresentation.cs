using CarWash.Hub.Application.Features.Alerts;
using CarWash.Hub.Application.Features.Dashboard;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.Web.Localization;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace CarWash.Hub.Web.Theming;

/// <summary>
/// How a service level and a machine's urgency look: the reserved status colour, an icon and a word,
/// so the state never rests on colour alone.
/// </summary>
/// <remarks>Layer: Web.</remarks>
public static class LevelPresentation
{
    /// <summary>A run-out this close counts as "soon".</summary>
    public static readonly TimeSpan Soon = AlertRules.RunsOutSoonWindow;

    /// <summary>Returns the status kind - the CSS modifier - for a level.</summary>
    /// <param name="level">The level.</param>
    /// <param name="forecastEmptyAt">The forecast run-out.</param>
    /// <param name="now">The current time.</param>
    /// <returns><c>good</c>, <c>warning</c>, <c>critical</c> or <c>muted</c>.</returns>
    public static string Kind(ServiceLevel level, DateTimeOffset? forecastEmptyAt, DateTimeOffset now) => level switch
    {
        ServiceLevel.Overused or ServiceLevel.Empty => "critical",
        ServiceLevel.Low => "warning",
        ServiceLevel.Ok when forecastEmptyAt is { } at && at - now <= Soon => "warning",
        ServiceLevel.Ok => "good",
        _ => "muted",
    };

    /// <summary>Returns the status icon for a level.</summary>
    /// <param name="level">The level.</param>
    /// <returns>A MudBlazor icon.</returns>
    public static string Icon(ServiceLevel level) => level switch
    {
        ServiceLevel.Overused => Icons.Material.Rounded.Report,
        ServiceLevel.Empty => Icons.Material.Rounded.Error,
        ServiceLevel.Low => Icons.Material.Rounded.Warning,
        ServiceLevel.Ok => Icons.Material.Rounded.CheckCircle,
        _ => Icons.Material.Rounded.HelpOutline,
    };

    /// <summary>Returns the word for a level.</summary>
    /// <param name="level">The level.</param>
    /// <param name="localizer">The dashboard's words.</param>
    /// <returns>The word.</returns>
    public static string Word(ServiceLevel level, IStringLocalizer<DashboardResource> localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return level switch
        {
            ServiceLevel.Overused => localizer[DashboardText.LevelOverused],
            ServiceLevel.Empty => localizer[DashboardText.LevelEmpty],
            ServiceLevel.Low => localizer[DashboardText.LevelLow],
            ServiceLevel.Ok => localizer[DashboardText.LevelOk],
            _ => localizer[DashboardText.LevelNeverRefilled],
        };
    }

    /// <summary>
    /// Scores how urgently a machine needs someone: a service used after empty first, then empty, low,
    /// running out within a day, and a silent machine. Zero means nothing to do.
    /// </summary>
    /// <param name="machine">The machine.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The score; higher is more urgent.</returns>
    public static int Urgency(MachineCardDto machine, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(machine);

        var level = machine.Levels.Count == 0 ? 0 : machine.Levels.Max(service => service.Level switch
        {
            ServiceLevel.Overused => 50,
            ServiceLevel.Empty => 40,
            ServiceLevel.Low => 30,
            ServiceLevel.Ok when service.ForecastEmptyAt is { } at && at - now <= Soon => 20,
            _ => 0,
        });

        return Math.Max(level, machine.Activity == MachineActivity.Silent && machine.IsActive ? 15 : 0);
    }

    /// <summary>Returns whether any of a machine's services is low, empty or used after empty.</summary>
    /// <param name="machine">The machine.</param>
    /// <returns><see langword="true"/> when one is.</returns>
    public static bool NeedsRefill(MachineCardDto machine) =>
        machine?.Levels.Any(level => level.Level is ServiceLevel.Low or ServiceLevel.Empty or ServiceLevel.Overused) ?? false;

    /// <summary>Returns whether any of a machine's services is expected to run out within <see cref="Soon"/>.</summary>
    /// <param name="machine">The machine.</param>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when one is.</returns>
    public static bool RunsOutSoon(MachineCardDto machine, DateTimeOffset now) =>
        machine?.Levels.Any(level => level.ForecastEmptyAt is { } at && at - now <= Soon) ?? false;
}
