using System.Globalization;
using System.Net;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.IntegrationTests.Infrastructure;

namespace CarWash.Hub.IntegrationTests.Api;

/// <summary>
/// The read endpoints: a machine's latest status, its history, the machine list and one message.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ReadEndpointsTests(DatabaseFixture database) : IAsyncLifetime
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

    [Fact]
    public async Task Latest_returns_the_most_recent_message()
    {
        var machineId = ApiRequests.NewMachineId();
        await _client.PushStatusAsync(machineId, "Idle");
        await _client.PushStatusAsync(machineId, "Washing");
        var lastId = await _client.PushStatusAsync(machineId, "Drying");

        using var response = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machines/{machineId}/messages/latest"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var latest = (await response.JsonAsync()).GetProperty("data");
        latest.GetProperty("id").GetInt64().ShouldBe(lastId);
        latest.GetProperty("payloadJson").GetProperty("status").GetString().ShouldBe("Drying");
    }

    [Fact]
    public async Task History_is_newest_first_and_paged()
    {
        var machineId = ApiRequests.NewMachineId();
        var ids = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await _client.PushStatusAsync(machineId, $"S{i}"));
        }

        using var response = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machines/{machineId}/messages?pageNumber=1&pageSize=2"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = (await response.JsonAsync()).GetProperty("data");
        page.GetProperty("totalCount").GetInt32().ShouldBe(5);
        page.GetProperty("totalPages").GetInt32().ShouldBe(3);
        page.GetProperty("hasNextPage").GetBoolean().ShouldBeTrue();
        var items = page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt64()).ToList();
        items.ShouldBe([ids[4], ids[3]]);

        // Raw payloads only in lists; the parsed JSON is on the single-message endpoints.
        page.GetProperty("items")[0].GetProperty("payload").GetString()!.ShouldContain("S4");
    }

    [Fact]
    public async Task History_can_be_limited_to_a_time_window()
    {
        var machineId = ApiRequests.NewMachineId();
        await _client.PushStatusAsync(machineId, "Idle");

        var past = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O", CultureInfo.InvariantCulture));
        using var before = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machines/{machineId}/messages?to={past}"));
        using var since = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machines/{machineId}/messages?from={past}"));

        (await before.JsonAsync()).GetProperty("data").GetProperty("totalCount").GetInt32().ShouldBe(0);
        (await since.JsonAsync()).GetProperty("data").GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_backwards_window_is_refused()
    {
        var machineId = ApiRequests.NewMachineId();
        await _client.PushStatusAsync(machineId, "Idle");

        using var response = await _client.SendAndDisposeAsync(ApiRequests.Read(
            $"/api/v1/machines/{machineId}/messages?from=2026-10-02T00:00:00Z&to=2026-10-01T00:00:00Z"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").GetProperty("From").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task A_machine_is_found_ignoring_case_and_shows_when_it_was_seen()
    {
        var machineId = ApiRequests.NewMachineId();
        await _client.PushStatusAsync(machineId, "Idle");

        using var response = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machines/{machineId.ToUpperInvariant()}"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var machine = (await response.JsonAsync()).GetProperty("data");
        machine.GetProperty("machineId").GetString().ShouldBe(machineId);
        machine.GetProperty("gasStationId").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        machine.GetProperty("lastSeenAt").GetDateTimeOffset().ShouldBeGreaterThanOrEqualTo(machine.GetProperty("firstSeenAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task The_machine_list_finds_a_machine_by_search_term()
    {
        var machineId = ApiRequests.NewMachineId();
        await _client.PushStatusAsync(machineId, "Idle");

        using var response = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machines?searchTerm={machineId}&sortBy=lastSeenAt&sortDirection=Descending"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = (await response.JsonAsync()).GetProperty("data");
        page.GetProperty("totalCount").GetInt32().ShouldBe(1);
        page.GetProperty("items")[0].GetProperty("machineId").GetString().ShouldBe(machineId);
    }

    [Fact]
    public async Task An_unknown_machine_is_404()
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machines/{ApiRequests.NewMachineId()}/messages/latest"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.JsonAsync()).GetProperty("code").GetString().ShouldBe(ResourceKeys.Errors.NotFound);
    }

    [Fact]
    public async Task An_unknown_message_is_404()
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machine-messages/{long.MaxValue}"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
