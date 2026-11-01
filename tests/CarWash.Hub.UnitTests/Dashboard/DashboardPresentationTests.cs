using System.Globalization;
using CarWash.Hub.Application.Common.Options;
using CarWash.Hub.Application.Features.Dashboard;
using CarWash.Hub.Infrastructure.Localization;
using CarWash.Hub.Infrastructure.Services;
using CarWash.Hub.Web.Theming;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.UnitTests.Dashboard;

/// <summary>
/// The pieces that decide what the dashboard shows: service colours, the business day, and Arabic
/// dates.
/// </summary>
public class DashboardPresentationTests
{
    private static ServiceInfoDto Service(string code, int sortOrder) => new(code, code, null, null, true, sortOrder, true);

    [Fact]
    public void The_three_known_services_own_the_first_three_colour_slots()
    {
        var slots = ServicePalette.AssignSlots([Service("soap", 10), Service("water", 20), Service("vacuum", 30)]);

        slots["water"].ShouldBe(1);
        slots["vacuum"].ShouldBe(2);
        slots["soap"].ShouldBe(3);
    }

    [Fact]
    public void A_new_service_takes_the_next_slot_and_never_repaints_the_others()
    {
        var before = ServicePalette.AssignSlots([Service("soap", 10), Service("water", 20), Service("vacuum", 30)]);
        var after = ServicePalette.AssignSlots([Service("soap", 10), Service("wax", 15), Service("water", 20), Service("vacuum", 30)]);

        after["wax"].ShouldBe(4);
        after["soap"].ShouldBe(before["soap"]);
        after["water"].ShouldBe(before["water"]);
        after["vacuum"].ShouldBe(before["vacuum"]);
    }

    [Fact]
    public void Services_past_the_eighth_slot_are_drawn_as_other_rather_than_a_generated_hue()
    {
        var services = Enumerable.Range(1, 9).Select(i => Service($"extra{i}", i)).ToList();

        var slots = ServicePalette.AssignSlots(services);

        // Water, vacuum and soap keep 1-3 even when absent, so extras fill 4-8 and the rest fold.
        slots.Values.Where(slot => slot > 0).ShouldBeUnique();
        slots["extra1"].ShouldBe(4);
        slots["extra5"].ShouldBe(ServicePalette.SlotCount);
        slots["extra6"].ShouldBe(0);
        ServicePalette.CssClass(0).ShouldBe("svc-other");
    }

    [Fact]
    public void The_business_day_starts_at_midnight_in_Amman()
    {
        var zone = new BusinessTimeZone(Options.Create(new DashboardOptions { TimeZone = "Asia/Amman" }));

        // 21:30 UTC on 31 October is 00:30 on 1 November in Amman (UTC+3).
        var start = zone.StartOfDay(new DateTimeOffset(2026, 10, 31, 21, 30, 0, TimeSpan.Zero));

        start.ShouldBe(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(3)));
        start.UtcDateTime.ShouldBe(new DateTime(2026, 10, 31, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void An_unknown_time_zone_fails_startup_rather_than_showing_utc() =>
        Should.Throw<InvalidOperationException>(() => new BusinessTimeZone(Options.Create(new DashboardOptions { TimeZone = "Mars/Olympus" })));

    [Fact]
    public void Arabic_dates_use_the_gregorian_calendar()
    {
        var arabic = LocalizationExtensions.SupportedCultures.Single(culture => culture.Name == "ar-SA");

        arabic.DateTimeFormat.Calendar.ShouldBeOfType<GregorianCalendar>();
        new DateTime(2026, 10, 31).ToString("yyyy", arabic).ShouldBe("2026");
    }
}
