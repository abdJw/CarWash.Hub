using System.Text;
using CarWash.Hub.Application.Features.DeviceSettings;
using CarWash.Hub.Application.Features.Pricing;
using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.UnitTests.DeviceSettings;

/// <summary>The settings document's bytes, and the revision and acknowledgement rules.</summary>
public class DeviceSettingsRulesTests
{
    private const string GuideExample =
        """{"revision":1,"command":"update_settings","services":{"water":{"price_qirsh":50,"durations_seconds":[180,180,210]},"soap":{"price_qirsh":50,"durations_seconds":[60,90,90]},"minute_water":{"price_qirsh":25,"duration_seconds":60},"vacuum":{"price_qirsh":50,"durations_seconds":[180,180,210]}}}""";

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 4);
    private static readonly Guid Station = Guid.NewGuid();

    private static IReadOnlyList<DeviceServiceSettings> GuideServices() =>
    [
        new("water", 50, [180, 180, 210]),
        new("soap", 50, [60, 90, 90]),
        new("minute_water", 25, [60]),
        new("vacuum", 50, [180, 180, 210]),
    ];

    [Fact]
    public void The_document_is_the_guide_example_byte_for_byte()
    {
        var bytes = DeviceSettingsDocument.Write(1, DeviceSettingsDocument.WriteServices(GuideServices()));

        Encoding.ASCII.GetString(bytes).ShouldBe(GuideExample);
    }

    [Fact]
    public void No_settings_is_revision_zero() =>
        Encoding.ASCII.GetString(DeviceSettingsDocument.NoSettings.Span).ShouldBe("""{"revision":0}""");

    [Fact]
    public void The_largest_document_fits_the_modem_buffer()
    {
        IReadOnlyList<DeviceServiceSettings> largest =
        [
            new("water", 995, [5999, 5999, 5999]),
            new("soap", 995, [5999, 5999, 5999]),
            new("minute_water", 995, [5999]),
            new("vacuum", 995, [5999, 5999, 5999]),
        ];
        var services = DeviceSettingsDocument.WriteServices(largest);

        DeviceSettingsDocument.Write(uint.MaxValue, services).Length.ShouldBeLessThan(DeviceSettingsDocument.MaxBytes);
        DeviceSettingsDocument.CommandBytes(services).ShouldBeLessThan(DeviceSettingsDocument.MaxCommandBytes);
        services.Length.ShouldBeLessThanOrEqualTo(MachineDeviceSettings.ServicesJsonMaxLength);
    }

    [Fact]
    public void The_services_read_back_as_written() =>
        DeviceSettingsDocument.Parse(DeviceSettingsDocument.WriteServices(GuideServices())).ShouldBe(GuideServices(), new ServiceComparer());

    [Fact]
    public void A_fully_priced_station_builds_every_service_in_order()
    {
        var services = DeviceSettingsRules.TryBuild(Station, Prices(0.5m, 0.5m, 0.25m, 0.5m), Today, Durations(), out var issue, out var issueService);

        issue.ShouldBe(DeviceSettingsIssue.None);
        issueService.ShouldBeNull();
        DeviceSettingsDocument.Write(1, DeviceSettingsDocument.WriteServices(services!)).ShouldBe(Encoding.ASCII.GetBytes(GuideExample));
    }

    [Fact]
    public void A_missing_price_blocks_the_snapshot_and_names_the_service()
    {
        var book = new PriceBook(PricePoints(0.5m, 0.5m, null, 0.5m));

        DeviceSettingsRules.TryBuild(Station, book, Today, Durations(), out var issue, out var service).ShouldBeNull();

        issue.ShouldBe(DeviceSettingsIssue.MissingPrice);
        service.ShouldBe("minute_water");
    }

    [Fact]
    public void A_price_the_machine_cannot_take_blocks_the_snapshot()
    {
        DeviceSettingsRules.TryBuild(Station, Prices(0.125m, 0.5m, 0.25m, 0.5m), Today, Durations(), out var issue, out var service).ShouldBeNull();

        issue.ShouldBe(DeviceSettingsIssue.InvalidPrice);
        service.ShouldBe("water");
    }

    [Fact]
    public void Missing_run_times_block_the_snapshot()
    {
        var durations = Durations().Where(row => row.ServiceCode != "vacuum").ToList();

        DeviceSettingsRules.TryBuild(Station, Prices(0.5m, 0.5m, 0.25m, 0.5m), Today, durations, out var issue, out var service).ShouldBeNull();

        issue.ShouldBe(DeviceSettingsIssue.MissingDurations);
        service.ShouldBe("vacuum");
    }

    [Fact]
    public void Equal_values_never_get_a_new_revision()
    {
        var settings = new MachineDeviceSettings();

        DeviceSettingsRules.Publish(settings, "{\"a\":1}", Now).ShouldBeTrue();
        DeviceSettingsRules.Publish(settings, "{\"a\":1}", Now).ShouldBeFalse();
        DeviceSettingsRules.Publish(settings, "{\"a\":2}", Now).ShouldBeTrue();

        settings.DesiredRevision.ShouldBe(2);
    }

    [Fact]
    public void A_revision_a_machine_already_reported_is_never_reused()
    {
        var settings = new MachineDeviceSettings { MaxReportedRevision = 7 };

        DeviceSettingsRules.Publish(settings, "{\"a\":1}", Now);

        settings.DesiredRevision.ShouldBe(8);
    }

    [Theory]
    [InlineData(DeviceAckStatus.Saved)]
    [InlineData(DeviceAckStatus.Unchanged)]
    public void Saved_or_unchanged_applies_the_revision(DeviceAckStatus status)
    {
        var settings = Desired(3);

        DeviceSettingsRules.ApplyAck(settings, new SettingsAckReport(3, status), messageId: 10, Now);

        settings.AppliedRevision.ShouldBe(3);
        settings.AppliedAt.ShouldBe(Now);
        DeviceSettingsRules.StatusOf(settings).ShouldBe(DeviceSettingsStatus.Applied);
    }

    [Fact]
    public void Busy_then_saved_for_the_same_revision_ends_applied()
    {
        var settings = Desired(2);

        DeviceSettingsRules.ApplyAck(settings, new SettingsAckReport(2, DeviceAckStatus.Busy), 10, Now);
        DeviceSettingsRules.StatusOf(settings).ShouldBe(DeviceSettingsStatus.Busy);

        DeviceSettingsRules.ApplyAck(settings, new SettingsAckReport(2, DeviceAckStatus.Saved), 11, Now);
        DeviceSettingsRules.StatusOf(settings).ShouldBe(DeviceSettingsStatus.Applied);
    }

    [Fact]
    public void An_old_reply_never_undoes_a_newer_confirmation()
    {
        var settings = Desired(5);
        DeviceSettingsRules.ApplyAck(settings, new SettingsAckReport(5, DeviceAckStatus.Saved), 20, Now);

        // A retransmitted reply to an earlier revision, arriving late and out of order.
        DeviceSettingsRules.ApplyAck(settings, new SettingsAckReport(4, DeviceAckStatus.Saved), 15, Now.AddSeconds(5));
        DeviceSettingsRules.ApplyAck(settings, new SettingsAckReport(5, DeviceAckStatus.Busy), 18, Now.AddSeconds(6));

        settings.AppliedRevision.ShouldBe(5);
        settings.LastAckMessageId.ShouldBe(20);
        settings.LastAckStatus.ShouldBe(DeviceAckStatus.Saved);
        DeviceSettingsRules.StatusOf(settings).ShouldBe(DeviceSettingsStatus.Applied);
    }

    [Fact]
    public void A_confirmation_of_an_older_revision_leaves_the_newer_pending()
    {
        var settings = Desired(3);

        DeviceSettingsRules.ApplyAck(settings, new SettingsAckReport(2, DeviceAckStatus.Saved), 10, Now);

        settings.AppliedRevision.ShouldBe(2);
        DeviceSettingsRules.StatusOf(settings).ShouldBe(DeviceSettingsStatus.Pending);
    }

    [Theory]
    [InlineData(DeviceAckStatus.StorageError)]
    [InlineData(DeviceAckStatus.InvalidSettings)]
    [InlineData(DeviceAckStatus.InvalidCommand)]
    [InlineData(DeviceAckStatus.InvalidJson)]
    [InlineData(DeviceAckStatus.Unknown)]
    public void A_failure_is_never_applied(DeviceAckStatus status)
    {
        var settings = Desired(1);

        DeviceSettingsRules.ApplyAck(settings, new SettingsAckReport(1, status), 10, Now);

        settings.AppliedRevision.ShouldBe(0);
        DeviceSettingsRules.StatusOf(settings).ShouldBe(DeviceSettingsStatus.Failed);
    }

    [Fact]
    public void A_revision_the_server_never_issued_is_never_applied_but_is_remembered()
    {
        var settings = Desired(1);

        DeviceSettingsRules.ApplyAck(settings, new SettingsAckReport(9, DeviceAckStatus.Saved), 10, Now);

        settings.AppliedRevision.ShouldBe(0);
        settings.MaxReportedRevision.ShouldBe(9);
    }

    [Fact]
    public void A_settings_issue_shows_as_blocked_even_when_the_last_revision_is_applied()
    {
        var settings = Desired(1);
        settings.AppliedRevision = 1;
        settings.Issue = DeviceSettingsIssue.MissingPrice;

        DeviceSettingsRules.StatusOf(settings).ShouldBe(DeviceSettingsStatus.Blocked);
        DeviceSettingsRules.StatusOf(null).ShouldBe(DeviceSettingsStatus.NotConfigured);
    }

    private static MachineDeviceSettings Desired(long revision) => new() { DesiredRevision = revision, ServicesJson = "{}" };

    private static PriceBook Prices(decimal water, decimal soap, decimal minuteWater, decimal vacuum) =>
        new(PricePoints(water, soap, minuteWater, vacuum));

    private static List<PricePoint> PricePoints(decimal? water, decimal? soap, decimal? minuteWater, decimal? vacuum) =>
    [
        .. new (string Service, decimal? Price)[] { ("water", water), ("soap", soap), ("minute_water", minuteWater), ("vacuum", vacuum) }
            .Where(entry => entry.Price is not null)
            .Select(entry => new PricePoint(Guid.NewGuid(), Station, entry.Service, Today.AddDays(-1), entry.Price!.Value)),
    ];

    private static List<StationServiceDuration> Durations() =>
    [
        new() { GasStationId = Station, ServiceCode = "water", Duration1Seconds = 180, Duration2Seconds = 180, Duration3Seconds = 210 },
        new() { GasStationId = Station, ServiceCode = "soap", Duration1Seconds = 60, Duration2Seconds = 90, Duration3Seconds = 90 },
        new() { GasStationId = Station, ServiceCode = "minute_water", Duration1Seconds = 60 },
        new() { GasStationId = Station, ServiceCode = "vacuum", Duration1Seconds = 180, Duration2Seconds = 180, Duration3Seconds = 210 },
    ];

    private sealed class ServiceComparer : IEqualityComparer<DeviceServiceSettings>
    {
        public bool Equals(DeviceServiceSettings? x, DeviceServiceSettings? y) =>
            x is not null && y is not null && x.Service == y.Service && x.PriceQirsh == y.PriceQirsh && x.Seconds.SequenceEqual(y.Seconds);

        public int GetHashCode(DeviceServiceSettings obj) => obj.Service.GetHashCode(StringComparison.Ordinal);
    }
}
