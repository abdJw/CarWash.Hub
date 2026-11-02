using System.ComponentModel.DataAnnotations;

namespace CarWash.Hub.Application.Common.Options;

/// <summary>
/// How the dashboard presents time and language, and where it lives.
/// </summary>
/// <remarks>Layer: Application. Bound from the <c>Dashboard</c> section and validated at startup.</remarks>
public sealed class DashboardOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Dashboard";

    /// <summary>
    /// Gets or sets the IANA time zone the dashboard shows times in, and in which "today" begins.
    /// The Windows id is accepted too, for hosts without ICU time-zone data.
    /// </summary>
    [Required]
    public string TimeZone { get; set; } = "Asia/Amman";

    /// <summary>Gets or sets the language the dashboard opens in until a user picks one.</summary>
    [Required]
    public string DefaultCulture { get; set; } = "ar-SA";

    /// <summary>Gets or sets the day a week starts on in the finance report's weekly totals.</summary>
    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Saturday;

    /// <summary>
    /// Gets or sets the dashboard's public address, for links in e-mails and push notifications.
    /// Null to derive it from the request where one exists.
    /// </summary>
    [Url]
    public string? PublicBaseUrl { get; set; }
}
