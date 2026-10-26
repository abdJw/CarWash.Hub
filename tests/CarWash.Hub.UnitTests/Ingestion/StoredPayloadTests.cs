using System.Text.Json;
using CarWash.Hub.Application.Features.Ingestion;

namespace CarWash.Hub.UnitTests.Ingestion;

public class StoredPayloadTests
{
    private static string StoredFor(string requestBody)
    {
        using var document = JsonDocument.Parse(requestBody);
        return StoredPayload.From(document.RootElement.GetProperty("data"));
    }

    [Fact]
    public void An_object_is_stored_as_its_raw_json_exactly_as_sent() =>
        StoredFor("""{"machineId":"CW-1","data":{ "status" : "Idle", "coins":12 }}""")
            .ShouldBe("""{ "status" : "Idle", "coins":12 }""");

    [Fact]
    public void An_array_is_stored_as_its_raw_json() =>
        StoredFor("""{"machineId":"CW-1","data":[1,2,3]}""").ShouldBe("[1,2,3]");

    [Fact]
    public void A_string_holding_json_is_stored_unescaped() =>
        StoredFor("""{"machineId":"CW-1","data":"{\"status\":\"Idle\"}"}""").ShouldBe("""{"status":"Idle"}""");

    [Fact]
    public void A_string_holding_text_is_stored_as_the_text() =>
        StoredFor("""{"machineId":"CW-1","data":"Idle,12,41.5"}""").ShouldBe("Idle,12,41.5");

    [Fact]
    public void Arabic_text_survives_unchanged() =>
        StoredFor("""{"machineId":"CW-1","data":{"note":"غسيل"}}""").ShouldBe("""{"note":"غسيل"}""");

    [Fact]
    public void A_number_is_stored_as_its_digits() =>
        StoredFor("""{"machineId":"CW-1","data":42.50}""").ShouldBe("42.50");

    [Fact]
    public void Size_is_counted_in_utf8_bytes() =>
        StoredPayload.SizeOf("غ1").ShouldBe(3);
}
