using System.Net;
using System.Text.Json;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CarWash.Hub.IntegrationTests.Processing;

/// <summary>
/// Stored messages become usage: the ESP32 service events are parsed into per-service counts, a
/// retried event is counted once, and anything else is recorded as ignored or failed - never lost.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProcessingTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

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

    private async Task<long> PushAsync(string machineId, string dataJson)
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(machineId, dataJson)));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("data").GetProperty("messageId").GetInt64();
    }

    private async Task<Dictionary<string, int>> UsageTotalsAsync(string machineId)
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machines/{machineId}/usage"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = (await response.JsonAsync()).GetProperty("data");

        return data.GetProperty("totals").EnumerateArray()
            .ToDictionary(total => total.GetProperty("service").GetString()!, total => total.GetProperty("count").GetInt32());
    }

    private async Task<ProcessingStatus> StatusOfAsync(long messageId)
    {
        await using var context = database.CreateContext();
        return await context.MachineMessages.IgnoreQueryFilters()
            .Where(message => message.Id == messageId)
            .Select(message => message.ProcessingStatus)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Pushed_service_events_become_usage_counted_per_service()
    {
        var machineId = ApiRequests.NewMachineId();
        await PushAsync(machineId, ProcessingDriver.ServiceEvent("soap", Now.AddSeconds(-30)));
        await PushAsync(machineId, ProcessingDriver.ServiceEvent("water", Now.AddSeconds(-20)));
        await PushAsync(machineId, ProcessingDriver.ServiceEvent("soap", Now.AddSeconds(-10)));

        await database.SharedApi.ProcessAllPendingAsync();

        var totals = await UsageTotalsAsync(machineId);
        totals.ShouldBe(new Dictionary<string, int> { ["soap"] = 2, ["water"] = 1 }, ignoreOrder: true);
    }

    [Fact]
    public async Task Usage_is_also_reported_per_hour()
    {
        var machineId = ApiRequests.NewMachineId();
        await PushAsync(machineId, ProcessingDriver.ServiceEvent("vacuum", Now.AddHours(-2)));
        await PushAsync(machineId, ProcessingDriver.ServiceEvent("vacuum", Now.AddMinutes(-1)));

        await database.SharedApi.ProcessAllPendingAsync();

        using var response = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machines/{machineId}/usage"));
        var hourly = (await response.JsonAsync()).GetProperty("data").GetProperty("hourly").EnumerateArray().ToList();

        hourly.Count.ShouldBe(2);
        hourly.ShouldAllBe(hour => hour.GetProperty("count").GetInt32() == 1);
        hourly[0].GetProperty("hourStart").GetDateTimeOffset().ShouldBeLessThan(hourly[1].GetProperty("hourStart").GetDateTimeOffset());
    }

    // Longer than the test host's Processing:RetryMinDelay, as a controller's request timeout is.
    private static Task WaitLikeARetryAsync() => Task.Delay(TimeSpan.FromMilliseconds(800), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_retried_event_with_the_same_reading_time_is_counted_once()
    {
        var machineId = ApiRequests.NewMachineId();
        var data = ProcessingDriver.ServiceEvent("soap", Now.AddSeconds(-5));
        await PushAsync(machineId, data);
        await WaitLikeARetryAsync();
        var retry = await PushAsync(machineId, data);

        await database.SharedApi.ProcessAllPendingAsync();

        (await UsageTotalsAsync(machineId))["soap"].ShouldBe(1);
        (await StatusOfAsync(retry)).ShouldBe(ProcessingStatus.Processed);
    }

    [Fact]
    public async Task A_retry_processed_in_a_later_batch_is_still_counted_once()
    {
        var machineId = ApiRequests.NewMachineId();
        var data = ProcessingDriver.ServiceEvent("water", Now.AddSeconds(-5));
        await PushAsync(machineId, data);
        await database.SharedApi.ProcessAllPendingAsync();

        await WaitLikeARetryAsync();
        await PushAsync(machineId, data);
        await database.SharedApi.ProcessAllPendingAsync();

        (await UsageTotalsAsync(machineId))["water"].ShouldBe(1);
    }

    [Fact]
    public async Task Two_uses_in_the_same_second_arriving_together_are_both_counted()
    {
        var machineId = ApiRequests.NewMachineId();
        var data = ProcessingDriver.ServiceEvent("soap", Now.AddSeconds(-2));
        await PushAsync(machineId, data);
        await PushAsync(machineId, data);

        await database.SharedApi.ProcessAllPendingAsync();

        (await UsageTotalsAsync(machineId))["soap"].ShouldBe(2);
    }

    [Fact]
    public async Task Each_event_in_an_array_is_counted()
    {
        var machineId = ApiRequests.NewMachineId();
        await PushAsync(machineId, $"[{ProcessingDriver.ServiceEvent("soap", Now.AddSeconds(-9))},{ProcessingDriver.ServiceEvent("vacuum", Now.AddSeconds(-8))}]");

        await database.SharedApi.ProcessAllPendingAsync();

        (await UsageTotalsAsync(machineId)).ShouldBe(new Dictionary<string, int> { ["soap"] = 1, ["vacuum"] = 1 }, ignoreOrder: true);
    }

    [Fact]
    public async Task A_status_only_message_is_ignored_and_records_no_usage()
    {
        var machineId = ApiRequests.NewMachineId();
        var messageId = await PushAsync(machineId, """{"status":"Idle","coins":12}""");

        await database.SharedApi.ProcessAllPendingAsync();

        (await StatusOfAsync(messageId)).ShouldBe(ProcessingStatus.Ignored);
        (await UsageTotalsAsync(machineId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_invalid_service_code_fails_the_message_with_the_reason()
    {
        var machineId = ApiRequests.NewMachineId();
        var messageId = await PushAsync(machineId, """{"service":"Wax Pro!"}""");

        await database.SharedApi.ProcessAllPendingAsync();

        await using var context = database.CreateContext();
        var message = await context.MachineMessages.IgnoreQueryFilters()
            .SingleAsync(m => m.Id == messageId, TestContext.Current.CancellationToken);
        message.ProcessingStatus.ShouldBe(ProcessingStatus.Failed);
        message.ProcessingError.ShouldNotBeNull().ShouldContain("Wax Pro!");
        message.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task An_unknown_service_is_added_to_the_catalog_unconfigured_and_counted()
    {
        var machineId = ApiRequests.NewMachineId();
        var code = $"wax-{Guid.NewGuid():N}"[..16];
        await PushAsync(machineId, ProcessingDriver.ServiceEvent(code, Now.AddSeconds(-3)));

        await database.SharedApi.ProcessAllPendingAsync();

        (await UsageTotalsAsync(machineId))[code].ShouldBe(1);
        await using var context = database.CreateContext();
        var service = await context.ServiceDefinitions.SingleAsync(s => s.Code == code, TestContext.Current.CancellationToken);
        service.IsConfigured.ShouldBeFalse();
        service.TracksRefill.ShouldBeFalse();
    }

    [Fact]
    public async Task The_seeded_services_are_tracked_for_refills()
    {
        await using var context = database.CreateContext();

        var seeded = await context.ServiceDefinitions
            .Where(s => s.Code == "soap" || s.Code == "water" || s.Code == "vacuum")
            .ToListAsync(TestContext.Current.CancellationToken);

        seeded.Count.ShouldBe(3);
        seeded.ShouldAllBe(s => s.TracksRefill && s.IsConfigured);
    }

    [Fact]
    public async Task Machine_state_records_last_seen_last_use_and_total_uses()
    {
        var machineId = ApiRequests.NewMachineId();
        var lastUse = Now.AddSeconds(-4);
        await PushAsync(machineId, ProcessingDriver.ServiceEvent("soap", Now.AddSeconds(-40)));
        await PushAsync(machineId, ProcessingDriver.ServiceEvent("water", lastUse));
        var statusMessage = await PushAsync(machineId, """{"status":"Idle"}""");

        await database.SharedApi.ProcessAllPendingAsync();

        await using var context = database.CreateContext();
        var state = await context.MachineStates
            .SingleAsync(s => s.Machine.Code == machineId, TestContext.Current.CancellationToken);
        state.TotalUses.ShouldBe(2);
        state.LastServiceCode.ShouldBe("water");
        state.LastUsageAt!.Value.ToUnixTimeSeconds().ShouldBe(lastUse.ToUnixTimeSeconds());
        state.LastMessageId.ShouldBe(statusMessage);
    }

    [Fact]
    public async Task An_unset_device_clock_falls_back_to_the_receive_time()
    {
        var machineId = ApiRequests.NewMachineId();
        await PushAsync(machineId, ProcessingDriver.ServiceEvent("soap", DateTimeOffset.UnixEpoch));

        await database.SharedApi.ProcessAllPendingAsync();

        await using var context = database.CreateContext();
        var usage = await context.ServiceUsages
            .SingleAsync(u => u.Machine.Code == machineId, TestContext.Current.CancellationToken);
        usage.OccurredAtSource.ShouldBe(OccurrenceTimeSource.Server);
        usage.OccurredAt.ShouldBe(usage.ReceivedAt);
    }

    [Fact]
    public async Task The_status_endpoint_reports_the_backlog_until_it_is_processed()
    {
        var machineId = ApiRequests.NewMachineId();
        await PushAsync(machineId, ProcessingDriver.ServiceEvent("soap", Now));

        var before = await StatusAsync();
        before.GetProperty("pendingCount").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        before.GetProperty("oldestPendingReceivedAt").ValueKind.ShouldBe(JsonValueKind.String);
        before.GetProperty("worker").GetProperty("enabled").GetBoolean().ShouldBeFalse();

        await database.SharedApi.ProcessAllPendingAsync();

        var after = await StatusAsync();
        after.GetProperty("pendingCount").GetInt32().ShouldBe(0);
        after.GetProperty("oldestPendingReceivedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        async Task<JsonElement> StatusAsync()
        {
            using var response = await _client.SendAndDisposeAsync(ApiRequests.Read("/api/v1/processing/status"));
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return (await response.JsonAsync()).GetProperty("data");
        }
    }

    [Fact]
    public async Task The_status_endpoint_requires_the_read_key()
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Read("/api/v1/processing/status", key: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_usage_window_longer_than_31_days_is_refused()
    {
        var machineId = ApiRequests.NewMachineId();
        await PushAsync(machineId, ProcessingDriver.ServiceEvent("soap", Now));

        using var response = await _client.SendAndDisposeAsync(ApiRequests.Read(
            $"/api/v1/machines/{machineId}/usage?from=2026-01-01T00:00:00Z&to=2026-03-01T00:00:00Z"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").GetProperty("To")[0].GetString()!.ShouldContain("31");
    }

    [Fact]
    public async Task Usage_of_an_unknown_machine_is_not_found()
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machines/{ApiRequests.NewMachineId()}/usage"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
