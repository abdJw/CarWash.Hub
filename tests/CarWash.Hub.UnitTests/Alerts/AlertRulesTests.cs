using System.ComponentModel.DataAnnotations;
using CarWash.Hub.Application.Features.Alerts;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.UnitTests.Alerts;

public class AlertRulesTests
{
    // A Wednesday noon, UTC.
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    // Amman's offset, fixed: the tests must not depend on the host's time-zone data.
    private static readonly TimeZoneInfo Amman = TimeZoneInfo.CreateCustomTimeZone("Test/Amman", TimeSpan.FromHours(3), "Amman", "Amman");

    private static readonly TimeOnly Eleven = new(23, 0);
    private static readonly TimeOnly Seven = new(7, 0);

    private static DateTimeOffset Local(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(3));

    [Theory]
    [InlineData(ServiceLevel.Overused, AlertType.UsedAfterEmpty)]
    [InlineData(ServiceLevel.Empty, AlertType.Empty)]
    [InlineData(ServiceLevel.Low, AlertType.Low)]
    public void An_urgent_level_calls_for_its_alert(ServiceLevel level, AlertType expected) =>
        AlertRules.DesiredLevelAlert(level, Now.AddHours(2), null, Now).ShouldBe(expected);

    [Theory]
    [InlineData(ServiceLevel.Ok)]
    [InlineData(ServiceLevel.NeverRefilled)]
    [InlineData(ServiceLevel.NotTracked)]
    public void A_calm_level_with_no_forecast_calls_for_nothing(ServiceLevel level) =>
        AlertRules.DesiredLevelAlert(level, null, null, Now).ShouldBeNull();

    [Theory]
    [InlineData(23.9, true)]
    [InlineData(24, true)]
    [InlineData(24.1, false)]
    [InlineData(-1, true)]    // overdue, still above the threshold
    public void An_ok_service_running_out_within_a_day_runs_out_soon(double hours, bool expected) =>
        (AlertRules.DesiredLevelAlert(ServiceLevel.Ok, Now.AddHours(hours), null, Now) == AlertType.RunsOutSoon).ShouldBe(expected);

    [Theory]
    [InlineData(27, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void An_open_runs_out_soon_clears_only_past_thirty_hours(double hours, bool stillOpen) =>
        (AlertRules.DesiredLevelAlert(ServiceLevel.Ok, Now.AddHours(hours), AlertType.RunsOutSoon, Now) == AlertType.RunsOutSoon).ShouldBe(stillOpen);

    [Fact]
    public void A_forecast_that_disappears_clears_runs_out_soon() =>
        AlertRules.DesiredLevelAlert(ServiceLevel.Ok, null, AlertType.RunsOutSoon, Now).ShouldBeNull();

    [Theory]
    [InlineData(AlertType.RunsOutSoon, AlertSeverity.Warning)]
    [InlineData(AlertType.Low, AlertSeverity.Warning)]
    [InlineData(AlertType.NoActivity, AlertSeverity.Warning)]
    [InlineData(AlertType.Empty, AlertSeverity.Critical)]
    [InlineData(AlertType.UsedAfterEmpty, AlertSeverity.Critical)]
    public void Customers_affected_now_is_critical(AlertType type, AlertSeverity expected) =>
        AlertRules.SeverityOf(type).ShouldBe(expected);

    [Theory]
    [InlineData(null, AlertType.Low, true)]                        // first alert
    [InlineData(null, AlertType.RunsOutSoon, true)]
    [InlineData(AlertType.RunsOutSoon, AlertType.Low, false)]       // still a warning: no news
    [InlineData(AlertType.Low, AlertType.Empty, true)]              // now critical
    [InlineData(AlertType.RunsOutSoon, AlertType.Empty, true)]
    [InlineData(AlertType.Empty, AlertType.UsedAfterEmpty, true)]   // always
    [InlineData(AlertType.Empty, AlertType.Low, false)]             // calmer
    [InlineData(null, AlertType.NoActivity, true)]
    public void People_are_told_when_it_is_new_or_worse(AlertType? replaced, AlertType raised, bool expected) =>
        AlertRules.ShouldNotify(replaced, raised).ShouldBe(expected);

    [Fact]
    public void A_first_refill_counts_as_a_refill() =>
        AlertRules.IsRefill(null, Now).ShouldBeTrue();

    [Fact]
    public void No_refill_at_all_is_not_a_refill() =>
        AlertRules.IsRefill(null, null).ShouldBeFalse();

    [Fact]
    public void A_backdated_refill_still_counts()
    {
        // Raised at 10:00 against Monday's refill; the 09:00 refill was recorded at 11:00.
        var monday = Now.AddDays(-2);
        AlertRules.IsRefill(monday, Now.AddHours(-3)).ShouldBeTrue();
    }

    [Fact]
    public void Voiding_the_latest_refill_is_not_a_refill()
    {
        var older = Now.AddDays(-5);
        var voided = Now.AddDays(-1);

        AlertRules.IsRefill(voided, older).ShouldBeFalse();
        AlertRules.IsRefill(voided, null).ShouldBeFalse();
    }

    [Fact]
    public void The_same_refill_is_not_a_new_one() =>
        AlertRules.IsRefill(Now, Now).ShouldBeFalse();

    [Fact]
    public void Without_quiet_hours_all_silence_counts() =>
        AlertRules.ActiveSilence(Now.AddHours(-7), Now, null, null, Amman).ShouldBe(TimeSpan.FromHours(7));

    [Fact]
    public void A_night_across_midnight_does_not_count()
    {
        // Last used 22:00, checked 08:00 the next morning: only 22-23 and 07-08 count.
        AlertRules.ActiveSilence(Local(3, 22), Local(4, 8), Eleven, Seven, Amman).ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Silence_that_started_during_quiet_hours_counts_from_their_end() =>
        AlertRules.ActiveSilence(Local(4, 2), Local(4, 9), Eleven, Seven, Amman).ShouldBe(TimeSpan.FromHours(2));

    [Fact]
    public void Quiet_hours_inside_one_day_are_skipped_too() =>
        AlertRules.ActiveSilence(Local(4, 0), Local(4, 6), new TimeOnly(1, 0), new TimeOnly(5, 0), Amman).ShouldBe(TimeSpan.FromHours(2));

    [Fact]
    public void Every_night_is_skipped_over_several_days()
    {
        // Two full days: 48 hours less two 8-hour nights.
        AlertRules.ActiveSilence(Local(2, 12), Local(4, 12), Eleven, Seven, Amman).ShouldBe(TimeSpan.FromHours(32));
    }

    [Fact]
    public void Checking_during_quiet_hours_stops_the_clock()
    {
        var atClosing = AlertRules.ActiveSilence(Local(3, 17), Local(3, 23), Eleven, Seven, Amman);
        var atNight = AlertRules.ActiveSilence(Local(3, 17), Local(4, 3), Eleven, Seven, Amman);

        atNight.ShouldBe(atClosing);
    }

    [Fact]
    public void A_very_long_silence_is_still_long() =>
        AlertRules.ActiveSilence(Now.AddDays(-90), Now, Eleven, Seven, Amman).ShouldBeGreaterThan(TimeSpan.FromDays(30));

    [Fact]
    public void A_last_seen_time_in_the_future_is_no_silence() =>
        AlertRules.ActiveSilence(Now.AddMinutes(5), Now, Eleven, Seven, Amman).ShouldBe(TimeSpan.Zero);

    [Theory]
    [InlineData(60, 15, true)]
    [InlineData(5, 15, false)]
    [InlineData(60, -1, false)]
    public void The_alert_options_are_range_checked(int checkInterval, int grace, bool valid)
    {
        var options = new AlertOptions { CheckIntervalSeconds = checkInterval, StartupGraceMinutes = grace };
        Validator.TryValidateObject(options, new ValidationContext(options), null, validateAllProperties: true).ShouldBe(valid);
    }
}
