using System.Net;
using System.Text.Json;
using CarWash.Hub.IntegrationTests.Infrastructure;

namespace CarWash.Hub.IntegrationTests.Api;

/// <summary>
/// The firmware retries a POST with the same <c>data.event_id</c>; the contract is that a repeated
/// event is answered 201 again - never 409 - and stored once.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DeviceEventDedupeTests(DatabaseFixture database) : IAsyncLifetime
{
    private HttpClient _client = null!;

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(database.IsAvailable, database.SkipReason);
        _client = database.SharedApi.CreateClient();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string WaterStatus(string eventId, bool water = true) =>
        $$"""{"event":"water_status","device_mac":"AA:BB:CC:DD:EE:FF","boot_id":"0123456789ABCDEF","event_id":"{{eventId}}","water_available":{{(water ? "true" : "false")}},"uptime_ms":30000}""";

    private async Task<JsonElement> PostAsync(string machineId, string data)
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(machineId, data)));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("data");
    }

    private async Task<int> StoredCountAsync(string machineId)
    {
        using var history = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machines/{machineId}/messages"));
        return (await history.JsonAsync()).GetProperty("data").GetProperty("totalCount").GetInt32();
    }

    [Fact]
    public async Task A_retried_event_is_answered_201_with_the_original_and_stored_once()
    {
        var machineId = ApiRequests.NewMachineId();
        var data = WaterStatus($"{machineId}-1");

        var first = await PostAsync(machineId, data);
        var retry = await PostAsync(machineId, data);

        first.GetProperty("duplicate").GetBoolean().ShouldBeFalse();
        retry.GetProperty("duplicate").GetBoolean().ShouldBeTrue();
        retry.GetProperty("messageId").GetInt64().ShouldBe(first.GetProperty("messageId").GetInt64());
        retry.GetProperty("receivedAt").GetDateTimeOffset().ShouldBe(first.GetProperty("receivedAt").GetDateTimeOffset());
        (await StoredCountAsync(machineId)).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_retries_of_one_event_store_one_row_and_all_succeed()
    {
        var machineId = ApiRequests.NewMachineId();
        var data = WaterStatus($"{machineId}-race");

        // Registers the machine first, so the race is on the event alone.
        await PostAsync(machineId, WaterStatus($"{machineId}-warmup"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(machineId, data)))));

        var ids = new HashSet<long>();
        foreach (var response in responses)
        {
            using (response)
            {
                response.StatusCode.ShouldBe(HttpStatusCode.Created);
                ids.Add((await response.JsonAsync()).GetProperty("data").GetProperty("messageId").GetInt64());
            }
        }

        ids.Count.ShouldBe(1);
        (await StoredCountAsync(machineId)).ShouldBe(2);
    }

    [Fact]
    public async Task The_same_event_id_from_two_machines_is_two_events()
    {
        var first = ApiRequests.NewMachineId();
        var second = ApiRequests.NewMachineId();
        var data = WaterStatus("shared-event-id-1");

        (await PostAsync(first, data)).GetProperty("duplicate").GetBoolean().ShouldBeFalse();
        (await PostAsync(second, data)).GetProperty("duplicate").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_reused_event_id_with_different_data_keeps_the_original()
    {
        var machineId = ApiRequests.NewMachineId();
        var eventId = $"{machineId}-1";

        var first = await PostAsync(machineId, WaterStatus(eventId, water: true));
        var collision = await PostAsync(machineId, WaterStatus(eventId, water: false));

        collision.GetProperty("duplicate").GetBoolean().ShouldBeTrue();
        collision.GetProperty("messageId").GetInt64().ShouldBe(first.GetProperty("messageId").GetInt64());
        (await StoredCountAsync(machineId)).ShouldBe(1);
    }

    [Fact]
    public async Task Messages_without_an_event_id_are_never_deduplicated()
    {
        var machineId = ApiRequests.NewMachineId();
        const string legacy = """{"service":"soap","macAddress":"B0:CB:D8:D0:64:2C","readAt":"2026-10-31T13:03:45Z"}""";

        await PostAsync(machineId, legacy);
        await PostAsync(machineId, legacy);

        (await StoredCountAsync(machineId)).ShouldBe(2);
    }
}
