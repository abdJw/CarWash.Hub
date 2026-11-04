using System.Text.Json;
using CarWash.Hub.Application.Features.Ingestion;

namespace CarWash.Hub.UnitTests.Ingestion;

public class DeviceEventIdTests
{
    private static string? Read(string dataJson)
    {
        using var document = JsonDocument.Parse(dataJson);
        return DeviceEventId.TryRead(document.RootElement);
    }

    [Fact]
    public void The_firmware_event_id_is_read() =>
        Read("""{"event":"payment_success","event_id":"AA:BB:CC:DD:EE:FF-0123456789ABCDEF-payment_success-1"}""")
            .ShouldBe("AA:BB:CC:DD:EE:FF-0123456789ABCDEF-payment_success-1");

    [Fact]
    public void The_earlier_firmware_format_has_no_id() =>
        Read("""{"service":"soap","macAddress":"B0:CB:D8:D0:64:2C","readAt":"2026-10-31T13:03:45Z"}""").ShouldBeNull();

    [Theory]
    [InlineData("""[{"event_id":"a"}]""")]
    [InlineData("\"event_id\"")]
    [InlineData("42")]
    public void Data_that_is_not_an_object_has_no_id(string dataJson) => Read(dataJson).ShouldBeNull();

    [Theory]
    [InlineData("""{"event_id":12}""")]
    [InlineData("""{"event_id":null}""")]
    [InlineData("""{"event_id":""}""")]
    [InlineData("""{"event_id":"has space"}""")]
    [InlineData("""{"event_id":"غسيل"}""")]
    [InlineData("""{"Event_Id":"a-1"}""")]
    public void An_unusable_id_is_no_id(string dataJson) => Read(dataJson).ShouldBeNull();

    [Fact]
    public void An_id_longer_than_the_column_is_no_id()
    {
        Read($$"""{"event_id":"{{new string('a', 128)}}"}""").ShouldNotBeNull();
        Read($$"""{"event_id":"{{new string('a', 129)}}"}""").ShouldBeNull();
    }
}
