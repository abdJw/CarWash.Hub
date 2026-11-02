using System.Globalization;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Features.Dashboard;
using CarWash.Hub.Application.Features.Finance;
using CarWash.Hub.Web.Localization;
using Microsoft.Extensions.Localization;

namespace CarWash.Hub.Web.Formatting;

/// <summary>
/// Formats numbers, times and durations the way the dashboard shows them.
/// </summary>
/// <remarks>
/// Layer: Web. Times are shown in the business time zone and the current culture - Arabic dates in
/// the Gregorian calendar, which the culture setup guarantees. Large standalone numbers use
/// proportional figures and grouping (<c>1,284</c>), compacted only past five digits.
/// </remarks>
/// <param name="localizer">The dashboard's words.</param>
/// <param name="timeZone">The business time zone.</param>
public sealed class DashboardFormat(IStringLocalizer<DashboardResource> localizer, IBusinessTimeZone timeZone)
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>Formats a count with grouping, compacting it past 99,999.</summary>
    /// <param name="value">The count.</param>
    /// <returns>For example <c>1,284</c> or <c>128.4K</c>.</returns>
    public static string Number(long value) =>
        Math.Abs(value) < 100_000
            ? value.ToString("N0", Culture)
            : Math.Abs(value) < 1_000_000
                ? (value / 1_000d).ToString("0.#", Culture) + "K"
                : (value / 1_000_000d).ToString("0.#", Culture) + "M";

    /// <summary>Formats a signed difference.</summary>
    /// <param name="value">The difference.</param>
    /// <returns>For example <c>+12</c> or <c>−3</c>.</returns>
    public static string Signed(long value) => value switch
    {
        > 0 => "+" + Number(value),
        < 0 => "−" + Number(-value),
        _ => "0",
    };

    /// <summary>Formats an amount of money to the fils, without the currency.</summary>
    /// <param name="value">The amount, in dinars.</param>
    /// <returns>For example <c>1,284.500</c>.</returns>
    public static string Amount(decimal value) => value.ToString("N3", Culture);

    /// <summary>Formats a change as a signed whole percentage.</summary>
    /// <param name="current">The value now.</param>
    /// <param name="previous">The value it is compared with.</param>
    /// <returns>For example <c>+12%</c>; null when there is nothing to compare with.</returns>
    public static string? Change(decimal current, decimal previous)
    {
        if (previous == 0)
        {
            return null;
        }

        var percent = (long)Math.Round((current - previous) * 100 / previous, MidpointRounding.AwayFromZero);
        return Signed(percent) + "%";
    }

    /// <summary>Formats a share from 0 to 1 as a whole percentage.</summary>
    /// <param name="share">The share.</param>
    /// <returns>For example <c>37%</c>.</returns>
    public static string Share(decimal share) => Math.Round(share * 100, MidpointRounding.AwayFromZero).ToString("0", Culture) + "%";

    /// <summary>Formats an amount of money with the currency.</summary>
    /// <param name="value">The amount, in dinars.</param>
    /// <returns>For example <c>1,284.500 JOD</c>.</returns>
    public string Money(decimal value) => $"{Amount(value)} {localizer[DashboardText.FinanceCurrency]}";

    /// <summary>Formats a business date.</summary>
    /// <param name="date">The date.</param>
    /// <returns>For example "2 Nov 2026".</returns>
    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", Culture);

    /// <summary>Formats a finance bucket's start for its axis label.</summary>
    /// <param name="start">The bucket's first day.</param>
    /// <param name="grain">The bucket size.</param>
    /// <returns>A day, or a month for monthly buckets.</returns>
    public static string PeriodLabel(DateOnly start, FinanceGrain grain) =>
        start.ToString(grain == FinanceGrain.Month ? "MMM yyyy" : "d MMM", Culture);

    /// <summary>Formats a finance bucket's full span, for its tooltip and table row.</summary>
    /// <param name="start">The bucket's first day.</param>
    /// <param name="end">Its last day.</param>
    /// <param name="grain">The bucket size.</param>
    /// <returns>For example "Monday 2 November", "31 Oct – 6 Nov" or "November 2026".</returns>
    public static string PeriodTitle(DateOnly start, DateOnly end, FinanceGrain grain) => grain switch
    {
        FinanceGrain.Day => start.ToString("dddd d MMMM yyyy", Culture),
        FinanceGrain.Month when start.Day == 1 && end == start.AddMonths(1).AddDays(-1) => start.ToString("MMMM yyyy", Culture),
        _ => $"{start.ToString("d MMM", Culture)} – {end.ToString("d MMM yyyy", Culture)}",
    };

    /// <summary>Formats how long ago an instant was.</summary>
    /// <param name="instant">The instant.</param>
    /// <param name="now">The current time.</param>
    /// <returns>For example "3 min ago".</returns>
    public string Ago(DateTimeOffset instant, DateTimeOffset now)
    {
        var elapsed = now - instant;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return localizer[DashboardText.TimeJustNow];
        }

        return elapsed switch
        {
            { TotalHours: < 1 } => localizer[DashboardText.TimeMinutesAgo, (int)elapsed.TotalMinutes],
            { TotalDays: < 2 } => localizer[DashboardText.TimeHoursAgo, (int)elapsed.TotalHours],
            _ => localizer[DashboardText.TimeDaysAgo, (int)elapsed.TotalDays],
        };
    }

    /// <summary>Formats a duration in its largest whole unit.</summary>
    /// <param name="duration">The duration.</param>
    /// <returns>For example "5 h".</returns>
    public string Duration(TimeSpan duration) => duration switch
    {
        { TotalHours: < 1 } => localizer[DashboardText.DurationMinutes, Math.Max(1, (int)duration.TotalMinutes)],
        { TotalDays: < 2 } => localizer[DashboardText.DurationHours, (int)duration.TotalHours],
        _ => localizer[DashboardText.DurationDays, (int)duration.TotalDays],
    };

    /// <summary>Formats an instant as a local date and time.</summary>
    /// <param name="instant">The instant.</param>
    /// <returns>For example "31 Oct 2026, 16:03".</returns>
    public string DateTime(DateTimeOffset instant) => timeZone.ToLocal(instant).ToString("d MMM yyyy, HH:mm", Culture);

    /// <summary>Formats an instant as a local time of day, with seconds.</summary>
    /// <param name="instant">The instant.</param>
    /// <returns>For example "16:03:45".</returns>
    public string TimeOfDay(DateTimeOffset instant) => timeZone.ToLocal(instant).ToString("HH:mm:ss", Culture);

    /// <summary>Formats a chart bucket's start for its axis label.</summary>
    /// <param name="start">The bucket start.</param>
    /// <param name="range">The chart window.</param>
    /// <returns>An hour for a 24-hour chart, a day otherwise.</returns>
    public string BucketLabel(DateTimeOffset start, DashboardRange range)
    {
        var local = timeZone.ToLocal(start);
        return range switch
        {
            DashboardRange.Last24Hours => local.ToString("HH:mm", Culture),
            DashboardRange.Last7Days => local.ToString("ddd d", Culture),
            _ => local.ToString("d MMM", Culture),
        };
    }

    /// <summary>Formats a chart bucket's full span, for its tooltip and table row.</summary>
    /// <param name="start">The bucket start.</param>
    /// <param name="range">The chart window.</param>
    /// <returns>For example "Sat 31 Oct, 16:00–17:00".</returns>
    public string BucketTitle(DateTimeOffset start, DashboardRange range)
    {
        var local = timeZone.ToLocal(start);
        return range == DashboardRange.Last24Hours
            ? $"{local.ToString("ddd d MMM, HH:mm", Culture)}–{local.AddHours(1).ToString("HH:mm", Culture)}"
            : local.ToString("dddd d MMMM", Culture);
    }
}
