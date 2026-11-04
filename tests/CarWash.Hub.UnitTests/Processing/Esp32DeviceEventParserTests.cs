using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.UnitTests.Processing;

/// <summary>The event firmware's messages, as given in the integration guide of 2026-11-03.</summary>
/// <remarks>Field lists are written with single quotes, which <see cref="Parse"/> turns into JSON quotes.</remarks>
public class Esp32DeviceEventParserTests
{
    private static readonly MachineMessageParser Parser = new(new Esp32ServiceEventParser());

    private const string Common =
        "'device_mac':'AA:BB:CC:DD:EE:FF','boot_id':'0123456789ABCDEF','event_id':'AA:BB:CC:DD:EE:FF-0123456789ABCDEF-x-1'";

    private static MessageParseResult Parse(string eventName, string fields)
    {
        var json = $"{{'event':'{eventName}',{Common}{(fields.Length == 0 ? "" : "," + fields)}}}";
        return Parser.Parse(json.Replace('\'', '"'));
    }

    private static MessageParseResult Payment(string fields) => Parse("payment_success", fields);

    [Fact]
    public void The_guide_payment_example_is_one_use_with_its_amounts()
    {
        var result = Parser.Parse("""
            {
              "event": "payment_success",
              "device_mac": "AA:BB:CC:DD:EE:FF",
              "boot_id": "0123456789ABCDEF",
              "event_id": "AA:BB:CC:DD:EE:FF-0123456789ABCDEF-payment_success-1",
              "service": "water",
              "price_qirsh": 50,
              "paid_qirsh": 50,
              "duration_seconds": 180,
              "uptime_ms": 120000,
              "water_available": true
            }
            """);

        result.Outcome.ShouldBe(MessageParseOutcome.Usage);
        var use = result.Events.ShouldHaveSingleItem();
        use.ServiceCode.ShouldBe("water");
        use.SaleAmount.ShouldBe(0.50m);
        use.PaidAmount.ShouldBe(0.50m);
        use.DurationSeconds.ShouldBe(180);
        use.DeviceTime.ShouldBeNull();
        result.Device!.DeviceMac.ShouldBe("AA:BB:CC:DD:EE:FF");
        result.Device.BootId.ShouldBe("0123456789ABCDEF");
        result.Device.WaterAvailable.ShouldBe(true);
        result.Device.IsBoot.ShouldBeFalse();
    }

    [Theory]
    [InlineData("water")]
    [InlineData("soap")]
    [InlineData("minute_water")]
    [InlineData("vacuum")]
    public void Each_device_service_is_a_use(string service) =>
        Payment($"'service':'{service}','price_qirsh':25,'paid_qirsh':100").Events.ShouldHaveSingleItem().ServiceCode.ShouldBe(service);

    [Fact]
    public void Overpayment_keeps_price_and_cash_apart()
    {
        var use = Payment("'service':'soap','price_qirsh':50,'paid_qirsh':100").Events.ShouldHaveSingleItem();

        use.SaleAmount.ShouldBe(0.50m);
        use.PaidAmount.ShouldBe(1.00m);
    }

    [Theory]
    [InlineData("'service':'Water','price_qirsh':50,'paid_qirsh':50")]
    [InlineData("'service':'water ','price_qirsh':50,'paid_qirsh':50")]
    [InlineData("'service':5,'price_qirsh':50,'paid_qirsh':50")]
    [InlineData("'price_qirsh':50,'paid_qirsh':50")]
    [InlineData("'service':'water','price_qirsh':50.5,'paid_qirsh':50")]
    [InlineData("'service':'water','price_qirsh':'50','paid_qirsh':50")]
    [InlineData("'service':'water','price_qirsh':-5,'paid_qirsh':50")]
    [InlineData("'service':'water','price_qirsh':100001,'paid_qirsh':100001")]
    [InlineData("'service':'water','paid_qirsh':50")]
    [InlineData("'service':'water','price_qirsh':50")]
    [InlineData("'service':'water','price_qirsh':50,'paid_qirsh':45")]
    public void A_payment_with_bad_money_fields_fails_and_records_no_use(string fields)
    {
        var result = Payment(fields);

        result.Outcome.ShouldBe(MessageParseOutcome.Failed);
        result.Events.ShouldBeEmpty();
        result.Reason.ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("'service':'water','price_qirsh':50,'paid_qirsh':50,'duration_seconds':0")]
    [InlineData("'service':'water','price_qirsh':50,'paid_qirsh':50,'duration_seconds':'180'")]
    [InlineData("'service':'water','price_qirsh':50,'paid_qirsh':50,'water_available':'yes'")]
    public void A_bad_non_money_field_is_dropped_and_the_sale_still_counts(string fields)
    {
        var result = Payment(fields);

        result.Outcome.ShouldBe(MessageParseOutcome.Usage);
        result.Events.ShouldHaveSingleItem().SaleAmount.ShouldBe(0.50m);
    }

    [Fact]
    public void A_boot_is_a_report_with_its_water_state()
    {
        var result = Parse("device_boot", "'water_available':false");

        result.Outcome.ShouldBe(MessageParseOutcome.Report);
        result.Events.ShouldBeEmpty();
        result.Device!.IsBoot.ShouldBeTrue();
        result.Device.WaterAvailable.ShouldBe(false);
    }

    [Fact]
    public void A_water_status_is_a_report()
    {
        var result = Parse("water_status", "'water_available':true,'uptime_ms':30000");

        result.Outcome.ShouldBe(MessageParseOutcome.Report);
        result.Device!.WaterAvailable.ShouldBe(true);
    }

    [Fact]
    public void A_water_status_without_a_boolean_fails() =>
        Parse("water_status", "'uptime_ms':30000").Outcome.ShouldBe(MessageParseOutcome.Failed);

    [Theory]
    [InlineData("saved", DeviceAckStatus.Saved)]
    [InlineData("unchanged", DeviceAckStatus.Unchanged)]
    [InlineData("busy", DeviceAckStatus.Busy)]
    [InlineData("storage_error", DeviceAckStatus.StorageError)]
    [InlineData("invalid_settings", DeviceAckStatus.InvalidSettings)]
    [InlineData("invalid_command", DeviceAckStatus.InvalidCommand)]
    [InlineData("invalid_json", DeviceAckStatus.InvalidJson)]
    [InlineData("Saved", DeviceAckStatus.Unknown)]
    [InlineData("rebooting", DeviceAckStatus.Unknown)]
    public void An_acknowledgement_is_a_report_with_its_status(string status, DeviceAckStatus expected)
    {
        var result = Parse("settings_ack", $"'revision':7,'status':'{status}','water_available':true");

        result.Outcome.ShouldBe(MessageParseOutcome.Report);
        result.Device!.Ack.ShouldBe(new SettingsAckReport(7, expected));
    }

    [Theory]
    [InlineData("'status':'saved'")]
    [InlineData("'revision':-1,'status':'saved'")]
    [InlineData("'revision':4294967296,'status':'saved'")]
    [InlineData("'revision':'1','status':'saved'")]
    [InlineData("'revision':1")]
    [InlineData("'revision':1,'status':1")]
    public void A_malformed_acknowledgement_fails(string fields) =>
        Parse("settings_ack", fields).Outcome.ShouldBe(MessageParseOutcome.Failed);

    [Fact]
    public void The_largest_revision_is_accepted() =>
        Parse("settings_ack", "'revision':4294967295,'status':'saved'").Device!.Ack!.Revision.ShouldBe(uint.MaxValue);

    [Theory]
    [InlineData("refund")]
    [InlineData("Payment_Success")]
    public void An_unknown_event_is_ignored(string eventName) =>
        Parse(eventName, "").Outcome.ShouldBe(MessageParseOutcome.Ignored);

    [Fact]
    public void A_boot_id_that_is_not_printable_is_dropped() =>
        Parser.Parse("""{"event":"device_boot","boot_id":"01 23","water_available":true}""").Device!.BootId.ShouldBeNull();

    [Fact]
    public void The_earlier_format_is_still_read_by_its_own_parser()
    {
        var result = Parser.Parse("""{"service":"soap","macAddress":"B0:CB:D8:D0:64:2C","readAt":"2026-10-31T13:03:45Z"}""");

        result.Outcome.ShouldBe(MessageParseOutcome.Usage);
        result.Device.ShouldBeNull();
        result.Events.ShouldHaveSingleItem().SaleAmount.ShouldBeNull();
    }

    [Fact]
    public void An_event_that_is_not_a_string_falls_back_to_the_earlier_format() =>
        Parser.Parse("""{"event":1,"service":"soap"}""").Device.ShouldBeNull();
}

public class DeviceEventRulesTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aa:bb:cc:dd:ee:ff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("AA-BB-CC-DD-EE-FF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("AABBCCDDEEFF", "aa:bb:cc:dd:ee:ff")]
    public void The_same_address_matches_whatever_its_spelling(string mac, string code) =>
        DeviceEventRules.MacMatches(mac, code).ShouldBeTrue();

    [Theory]
    [InlineData("AA:BB:CC:DD:EE:00", "AA:BB:CC:DD:EE:FF")]
    [InlineData("", "AA:BB:CC:DD:EE:FF")]
    [InlineData("::", "")]
    public void A_different_address_does_not(string mac, string code) =>
        DeviceEventRules.MacMatches(mac, code).ShouldBeFalse();

    [Fact]
    public void A_payment_for_another_machine_fails()
    {
        var parsed = new MachineMessageParser(new Esp32ServiceEventParser()).Parse(
            """{"event":"payment_success","device_mac":"AA:BB:CC:DD:EE:00","service":"water","price_qirsh":50,"paid_qirsh":50}""");

        var checkedResult = DeviceEventRules.CheckIdentity(parsed, "AA:BB:CC:DD:EE:FF");

        checkedResult.Outcome.ShouldBe(MessageParseOutcome.Failed);
        checkedResult.Events.ShouldBeEmpty();
        checkedResult.Reason!.ShouldContain("does not match");
    }

    [Fact]
    public void A_message_without_a_device_mac_is_left_alone()
    {
        var parsed = MessageParseResult.Ignored("x");
        DeviceEventRules.CheckIdentity(parsed, "AA:BB:CC:DD:EE:FF").ShouldBeSameAs(parsed);
    }
}
