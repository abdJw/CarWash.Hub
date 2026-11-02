using CarWash.Hub.Application.Features.Finance;

namespace CarWash.Hub.UnitTests.Finance;

/// <summary>How the finance report's range is cut into days, weeks and months.</summary>
public class FinancePeriodsTests
{
    [Theory]
    [InlineData(1, FinanceGrain.Day)]
    [InlineData(31, FinanceGrain.Day)]
    [InlineData(32, FinanceGrain.Week)]
    [InlineData(120, FinanceGrain.Week)]
    [InlineData(121, FinanceGrain.Month)]
    [InlineData(367, FinanceGrain.Month)]
    public void The_automatic_grain_follows_the_range_length(int days, FinanceGrain expected)
    {
        var from = new DateOnly(2026, 1, 1);

        FinancePeriods.Resolve(FinanceGrain.Auto, from, from.AddDays(days - 1)).ShouldBe(expected);
    }

    [Fact]
    public void A_chosen_grain_is_kept() =>
        FinancePeriods.Resolve(FinanceGrain.Month, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3)).ShouldBe(FinanceGrain.Month);

    [Fact]
    public void Days_are_one_bucket_each()
    {
        var buckets = FinancePeriods.Buckets(new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 2), FinanceGrain.Day, DayOfWeek.Saturday);

        buckets.Count.ShouldBe(4);
        buckets.ShouldAllBe(bucket => bucket.Start == bucket.End);
        buckets[^1].Start.ShouldBe(new DateOnly(2026, 11, 2));
    }

    [Fact]
    public void Weeks_start_on_the_configured_day_and_are_clipped_to_the_range()
    {
        // 2 Nov 2026 is a Monday; with Saturday-first weeks it belongs to the week of 31 Oct.
        var buckets = FinancePeriods.Buckets(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 18), FinanceGrain.Week, DayOfWeek.Saturday);

        buckets.ShouldBe(
        [
            (new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6)),
            (new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 13)),
            (new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 18)),
        ]);
    }

    [Fact]
    public void Months_follow_the_calendar_and_are_clipped_to_the_range()
    {
        var buckets = FinancePeriods.Buckets(new DateOnly(2026, 1, 15), new DateOnly(2026, 3, 10), FinanceGrain.Month, DayOfWeek.Saturday);

        buckets.ShouldBe(
        [
            (new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 31)),
            (new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)),
            (new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 10)),
        ]);
    }

    [Theory]
    [InlineData(DayOfWeek.Saturday, 2026, 10, 31)]
    [InlineData(DayOfWeek.Sunday, 2026, 11, 1)]
    [InlineData(DayOfWeek.Monday, 2026, 11, 2)]
    public void The_start_of_a_week_depends_on_the_first_day(DayOfWeek first, int year, int month, int day) =>
        FinancePeriods.StartOfWeek(new DateOnly(2026, 11, 2), first).ShouldBe(new DateOnly(year, month, day));
}
