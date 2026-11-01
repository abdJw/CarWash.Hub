using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.UnitTests.Processing;

public class OccurrenceTimeResolverTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 31, 13, 3, 51, TimeSpan.Zero);
    private static readonly TimeSpan PastSkew = TimeSpan.FromDays(7);
    private static readonly TimeSpan FutureSkew = TimeSpan.FromMinutes(10);

    private static (DateTimeOffset OccurredAt, OccurrenceTimeSource Source) Resolve(DateTimeOffset? device) =>
        OccurrenceTimeResolver.Resolve(device, ReceivedAt, PastSkew, FutureSkew);

    [Fact]
    public void A_device_time_a_few_seconds_old_is_trusted()
    {
        var device = ReceivedAt.AddSeconds(-2);

        Resolve(device).ShouldBe((device, OccurrenceTimeSource.Device));
    }

    [Fact]
    public void A_device_time_within_the_future_skew_is_trusted() =>
        Resolve(ReceivedAt.AddMinutes(9)).Source.ShouldBe(OccurrenceTimeSource.Device);

    [Fact]
    public void No_device_time_falls_back_to_the_receive_time() =>
        Resolve(null).ShouldBe((ReceivedAt, OccurrenceTimeSource.Server));

    [Fact]
    public void An_unset_clock_reporting_1970_falls_back_to_the_receive_time() =>
        Resolve(DateTimeOffset.UnixEpoch).ShouldBe((ReceivedAt, OccurrenceTimeSource.Server));

    [Fact]
    public void A_clock_far_ahead_falls_back_to_the_receive_time() =>
        Resolve(ReceivedAt.AddHours(1)).Source.ShouldBe(OccurrenceTimeSource.Server);

    [Fact]
    public void The_result_is_always_utc() =>
        Resolve(new DateTimeOffset(2026, 10, 31, 16, 3, 50, TimeSpan.FromHours(3))).OccurredAt.Offset.ShouldBe(TimeSpan.Zero);

    [Fact]
    public void The_hour_start_drops_minutes_and_seconds_in_utc() =>
        OccurrenceTimeResolver.HourStart(new DateTimeOffset(2026, 10, 31, 16, 59, 59, TimeSpan.FromHours(3)))
            .ShouldBe(new DateTimeOffset(2026, 10, 31, 13, 0, 0, TimeSpan.Zero));
}
