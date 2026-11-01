using CarWash.Hub.Application.Features.Levels;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.UnitTests.Levels;

public class LevelMathTests
{
    // A Wednesday noon, UTC.
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, 300, 10, ServiceLevel.NotTracked)]
    [InlineData(true, null, 10, ServiceLevel.NeverRefilled)]
    [InlineData(true, 300, 0, ServiceLevel.Ok)]
    [InlineData(true, 300, 239, ServiceLevel.Ok)]      // 61 left: 20.3 %
    [InlineData(true, 300, 240, ServiceLevel.Low)]     // 60 left: exactly 20 %
    [InlineData(true, 300, 299, ServiceLevel.Low)]
    [InlineData(true, 300, 300, ServiceLevel.Empty)]
    [InlineData(true, 300, 301, ServiceLevel.Overused)]
    public void The_level_follows_the_uses_left(bool tracked, int? capacity, int used, ServiceLevel expected) =>
        ServiceLevelCalculator.Compute(tracked, capacity, used, lowThresholdPercent: 20).ShouldBe(expected);

    [Theory]
    [InlineData(300, 0, 100)]
    [InlineData(300, 240, 20)]
    [InlineData(300, 299, 0)]
    [InlineData(300, 450, 0)]
    public void The_percent_left_is_floored_and_clamped(int capacity, int used, int expected) =>
        ServiceLevelCalculator.PercentRemaining(capacity, used).ShouldBe(expected);

    [Fact]
    public void Without_a_refill_there_is_no_percent() => ServiceLevelCalculator.PercentRemaining(null, 5).ShouldBeNull();

    private static List<(DateTimeOffset, int)> Steady(int days, int perHour) =>
    [
        .. Enumerable.Range(1, days * 24).Select(h => (Hour(Now.AddHours(-h)), perHour)),
    ];

    private static DateTimeOffset Hour(DateTimeOffset instant) =>
        new(instant.Year, instant.Month, instant.Day, instant.Hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_steady_pace_runs_out_when_the_remaining_uses_are_spent()
    {
        // Two uses an hour, every hour, for two weeks: 100 uses last 50 hours.
        var forecast = UsageForecaster.Forecast(100, Steady(14, 2), Now.AddDays(-14), Now);

        forecast.EmptyAt.ShouldNotBeNull();
        forecast.EmptyAt!.Value.ShouldBe(Now.AddHours(50), TimeSpan.FromMinutes(1));
        forecast.AverageDailyUses.ShouldBe(48m);
    }

    [Fact]
    public void A_weekly_pattern_is_followed_rather_than_averaged()
    {
        // Busy only on Fridays 15:00-21:00 UTC (10 uses an hour), silent otherwise, for four weeks.
        var history = Enumerable.Range(1, 28 * 24)
            .Select(h => Hour(Now.AddHours(-h)))
            .Select(hour => (hour, hour.DayOfWeek == DayOfWeek.Friday && hour.Hour is >= 15 and < 21 ? 10 : 0))
            .ToList();

        // 30 uses left: three busy Friday hours.
        var forecast = UsageForecaster.Forecast(30, history, Now.AddDays(-28), Now);

        forecast.EmptyAt.ShouldNotBeNull();
        forecast.EmptyAt!.Value.DayOfWeek.ShouldBe(DayOfWeek.Friday);
        forecast.EmptyAt.Value.Hour.ShouldBe(18);
    }

    [Fact]
    public void Less_than_a_week_of_history_uses_the_hour_of_the_day()
    {
        // Three days of 1 use an hour between 08:00 and 20:00 only.
        var history = Enumerable.Range(1, 3 * 24)
            .Select(h => Hour(Now.AddHours(-h)))
            .Select(hour => (hour, hour.Hour is >= 8 and < 20 ? 1 : 0))
            .ToList();

        var forecast = UsageForecaster.Forecast(12, history, Now.AddDays(-3), Now);

        // From Wednesday noon: 8 more uses today (12:00-20:00), none overnight, 4 tomorrow by 12:00.
        forecast.EmptyAt!.Value.ShouldBe(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Too_little_history_gives_no_forecast() =>
        UsageForecaster.Forecast(100, [(Hour(Now.AddHours(-1)), 3)], Now.AddDays(-1), Now).EmptyAt.ShouldBeNull();

    [Fact]
    public void A_run_out_beyond_thirty_days_is_not_forecast() =>
        UsageForecaster.Forecast(100_000, Steady(14, 1), Now.AddDays(-14), Now).EmptyAt.ShouldBeNull();

    [Fact]
    public void An_empty_service_has_no_forecast() =>
        UsageForecaster.Forecast(0, Steady(14, 2), Now.AddDays(-14), Now).EmptyAt.ShouldBeNull();
}
