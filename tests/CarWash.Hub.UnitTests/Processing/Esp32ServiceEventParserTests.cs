using CarWash.Hub.Application.Features.Processing;

namespace CarWash.Hub.UnitTests.Processing;

public class Esp32ServiceEventParserTests
{
    private static readonly Esp32ServiceEventParser Parser = new();

    // Verbatim from production, machine B0:CB:D8:D0:64:2C, 2026-10-31.
    [Theory]
    [InlineData("""{"service":"vacuum","macAddress":"B0:CB:D8:D0:64:2C","readAt":"2026-10-31T13:03:49Z"}""", "vacuum", "2026-10-31T13:03:49Z")]
    [InlineData("""{"service":"soap","macAddress":"B0:CB:D8:D0:64:2C","readAt":"2026-10-31T13:03:45Z"}""", "soap", "2026-10-31T13:03:45Z")]
    [InlineData("""{"service":"water","macAddress":"B0:CB:D8:D0:64:2C","readAt":"2026-10-31T13:03:37Z"}""", "water", "2026-10-31T13:03:37Z")]
    public void A_real_controller_payload_is_one_use_at_its_reading_time(string payload, string service, string readAt)
    {
        var result = Parser.Parse(payload);

        result.Outcome.ShouldBe(MessageParseOutcome.Usage);
        var usage = result.Events.ShouldHaveSingleItem();
        usage.ServiceCode.ShouldBe(service);
        usage.DeviceTime.ShouldBe(DateTimeOffset.Parse(readAt, System.Globalization.CultureInfo.InvariantCulture));
        usage.ItemIndex.ShouldBe((short)0);
    }

    [Fact]
    public void Property_names_and_the_service_code_are_case_insensitive()
    {
        var result = Parser.Parse("""{"Service":" SOAP ","READAT":"2026-10-31T13:03:45Z"}""");

        result.Events.ShouldHaveSingleItem().ServiceCode.ShouldBe("soap");
    }

    [Fact]
    public void An_array_yields_one_event_per_element_with_a_service()
    {
        var result = Parser.Parse("""[{"service":"soap"},{"note":"skipped"},{"service":"water"}]""");

        result.Outcome.ShouldBe(MessageParseOutcome.Usage);
        result.Events.Select(e => (e.ServiceCode, e.ItemIndex)).ShouldBe([("soap", (short)0), ("water", (short)2)]);
    }

    [Fact]
    public void A_missing_reading_time_is_not_an_error() =>
        Parser.Parse("""{"service":"soap"}""").Events.ShouldHaveSingleItem().DeviceTime.ShouldBeNull();

    [Theory]
    [InlineData("not a time")]
    [InlineData("")]
    public void An_unreadable_reading_time_is_dropped(string readAt) =>
        Parser.Parse($$"""{"service":"soap","readAt":"{{readAt}}"}""").Events.ShouldHaveSingleItem().DeviceTime.ShouldBeNull();

    [Theory]
    [InlineData("1793451825", "2026-10-31T13:03:45Z")]
    [InlineData("1793451825000", "2026-10-31T13:03:45Z")]
    public void A_unix_reading_time_in_seconds_or_milliseconds_is_understood(string readAt, string expected) =>
        Parser.Parse($$"""{"service":"soap","readAt":{{readAt}}}""").Events.ShouldHaveSingleItem().DeviceTime
            .ShouldBe(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void A_reading_time_without_an_offset_is_taken_as_utc() =>
        Parser.Parse("""{"service":"soap","readAt":"2026-10-31T13:03:45"}""").Events.ShouldHaveSingleItem().DeviceTime
            .ShouldBe(new DateTimeOffset(2026, 10, 31, 13, 3, 45, TimeSpan.Zero));

    [Theory]
    [InlineData("""{"status":"Idle","coins":12}""")]
    [InlineData("Idle,12,41.5")]
    [InlineData("42")]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("""[{"status":"Idle"}]""")]
    public void A_payload_without_a_service_is_ignored(string payload) =>
        Parser.Parse(payload).Outcome.ShouldBe(MessageParseOutcome.Ignored);

    [Theory]
    [InlineData("""{"service":"Wax Pro!"}""")]
    [InlineData("""{"service":""}""")]
    [InlineData("""{"service":12}""")]
    [InlineData("""{"service":null}""")]
    [InlineData("""{"service":"a-service-code-that-is-far-too-long-to-accept"}""")]
    public void An_invalid_service_code_fails_the_message(string payload)
    {
        var result = Parser.Parse(payload);

        result.Outcome.ShouldBe(MessageParseOutcome.Failed);
        result.Reason.ShouldNotBeNullOrWhiteSpace();
        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public void One_invalid_element_fails_the_whole_array() =>
        Parser.Parse("""[{"service":"soap"},{"service":"bad code"}]""").Outcome.ShouldBe(MessageParseOutcome.Failed);

    [Fact]
    public void The_failure_reason_never_carries_a_huge_value()
    {
        var huge = new string('x', 10_000);

        var result = Parser.Parse($$"""{"service":"{{huge}}"}""");

        result.Reason!.Length.ShouldBeLessThan(100);
    }
}
