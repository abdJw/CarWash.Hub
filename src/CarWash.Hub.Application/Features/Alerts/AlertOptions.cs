using System.ComponentModel.DataAnnotations;

namespace CarWash.Hub.Application.Features.Alerts;

/// <summary>How often alerts are checked, and how long a restart waits before calling machines silent.</summary>
/// <remarks>
/// Layer: Application. Bound from <c>Alerts</c>, the section that also holds
/// <c>NoActivityAfterHours</c> (read through <c>ActivityOptions</c>, so the dashboard's "silent" and
/// the alert agree).
/// </remarks>
public sealed class AlertOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Alerts";

    /// <summary>
    /// Gets or sets how often, in seconds, the worker re-checks every machine: forecasts coming due
    /// and machines going silent happen with the passing of time, not with a message.
    /// </summary>
    [Range(10, 3600)]
    public int CheckIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets how long after the worker starts, in minutes, before it may call a machine silent.
    /// Messages that arrived while the site was down are processed first.
    /// </summary>
    [Range(0, 1440)]
    public int StartupGraceMinutes { get; set; } = 15;
}
