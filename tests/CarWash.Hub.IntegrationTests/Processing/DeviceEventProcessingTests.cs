using System.Net;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CarWash.Hub.IntegrationTests.Processing;

/// <summary>
/// The event firmware's messages become sales with their amounts, water and heartbeat state, and
/// failures that write nothing financial - as the integration guide's commissioning checks require.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DeviceEventProcessingTests(DatabaseFixture database) : IAsyncLifetime
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

    private async Task<long> PushAsync(string machineId, string dataJson)
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(machineId, dataJson)));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("data").GetProperty("messageId").GetInt64();
    }

    private async Task<(ProcessingStatus Status, string? Error)> OutcomeOfAsync(long messageId)
    {
        await using var context = database.CreateContext();
        var message = await context.MachineMessages.IgnoreQueryFilters()
            .SingleAsync(message => message.Id == messageId, TestContext.Current.CancellationToken);
        return (message.ProcessingStatus, message.ProcessingError);
    }

    private async Task<List<ServiceUsage>> UsagesOfAsync(string machineId)
    {
        await using var context = database.CreateContext();
        return await context.Set<ServiceUsage>().IgnoreQueryFilters()
            .Where(usage => usage.Machine.Code == machineId)
            .OrderBy(usage => usage.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<ServiceUsageDaily>> DaysOfAsync(string machineId)
    {
        await using var context = database.CreateContext();
        var id = await context.Machines.IgnoreQueryFilters()
            .Where(machine => machine.Code == machineId)
            .Select(machine => machine.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
        return await context.Set<ServiceUsageDaily>()
            .Where(day => day.MachineId == id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task<MachineState> StateOfAsync(string machineId)
    {
        await using var context = database.CreateContext();
        return await context.Set<MachineState>().IgnoreQueryFilters()
            .SingleAsync(state => state.Machine.Code == machineId, TestContext.Current.CancellationToken);
    }

    private static string NewMac()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return string.Join(':', bytes.Take(6).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task A_payment_is_a_sale_with_its_price_and_cash()
    {
        var mac = NewMac();
        var messageId = await PushAsync(mac, ProcessingDriver.Payment(mac, "soap", priceQirsh: 50, paidQirsh: 100, durationSeconds: 90));

        await database.SharedApi.ProcessAllPendingAsync();

        (await OutcomeOfAsync(messageId)).Status.ShouldBe(ProcessingStatus.Processed);
        var use = (await UsagesOfAsync(mac)).ShouldHaveSingleItem();
        use.ServiceCode.ShouldBe("soap");
        use.SaleAmount.ShouldBe(0.50m);
        use.PaidAmount.ShouldBe(1.00m);
        use.DurationSeconds.ShouldBe(90);
        use.OccurredAtSource.ShouldBe(OccurrenceTimeSource.Server);

        var day = (await DaysOfAsync(mac)).ShouldHaveSingleItem();
        day.UsageCount.ShouldBe(1);
        day.DevicePricedUses.ShouldBe(1);
        day.DeviceSalesAmount.ShouldBe(0.50m);
        day.CashCollectedAmount.ShouldBe(1.00m);
    }

    [Fact]
    public async Task Earlier_format_and_priced_uses_on_one_day_are_kept_apart()
    {
        var mac = NewMac();
        await PushAsync(mac, ProcessingDriver.ServiceEvent("water", DateTimeOffset.UtcNow.AddSeconds(-5), mac));
        await PushAsync(mac, ProcessingDriver.Payment(mac, "water", priceQirsh: 50, paidQirsh: 50));
        await PushAsync(mac, ProcessingDriver.Payment(mac, "water", priceQirsh: 75, paidQirsh: 100));

        await database.SharedApi.ProcessAllPendingAsync();

        var day = (await DaysOfAsync(mac)).ShouldHaveSingleItem();
        day.UsageCount.ShouldBe(3);
        day.DevicePricedUses.ShouldBe(2);
        day.DeviceSalesAmount.ShouldBe(1.25m);
        day.CashCollectedAmount.ShouldBe(1.50m);
    }

    [Fact]
    public async Task A_payment_naming_another_machine_fails_and_writes_nothing_financial()
    {
        var mac = NewMac();
        var messageId = await PushAsync(mac, ProcessingDriver.Payment(NewMac(), "water", 50, 50));

        await database.SharedApi.ProcessAllPendingAsync();

        var (status, error) = await OutcomeOfAsync(messageId);
        status.ShouldBe(ProcessingStatus.Failed);
        error!.ShouldContain("does not match");
        (await UsagesOfAsync(mac)).ShouldBeEmpty();
        (await DaysOfAsync(mac)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_malformed_payment_is_still_answered_201_but_writes_no_sale()
    {
        var mac = NewMac();
        var data = ProcessingDriver.DeviceEvent(mac, "payment_success", ProcessingDriver.Json("'service':'water','price_qirsh':50.5,'paid_qirsh':50"));
        var messageId = await PushAsync(mac, data);

        await database.SharedApi.ProcessAllPendingAsync();

        (await OutcomeOfAsync(messageId)).Status.ShouldBe(ProcessingStatus.Failed);
        (await UsagesOfAsync(mac)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_retried_payment_is_one_sale_even_after_processing()
    {
        var mac = NewMac();
        var data = ProcessingDriver.Payment(mac, "vacuum", 50, 50);
        await PushAsync(mac, data);
        await database.SharedApi.ProcessAllPendingAsync();

        await PushAsync(mac, data);
        await database.SharedApi.ProcessAllPendingAsync();

        (await UsagesOfAsync(mac)).Count.ShouldBe(1);
        (await DaysOfAsync(mac)).ShouldHaveSingleItem().DeviceSalesAmount.ShouldBe(0.50m);
    }

    [Fact]
    public async Task The_newest_water_report_wins_and_records_when_it_changed()
    {
        var mac = NewMac();
        await PushAsync(mac, ProcessingDriver.WaterStatus(mac, waterAvailable: true));
        var dry = await PushAsync(mac, ProcessingDriver.WaterStatus(mac, waterAvailable: false));
        var stillDry = await PushAsync(mac, ProcessingDriver.WaterStatus(mac, waterAvailable: false));

        await database.SharedApi.ProcessAllPendingAsync();

        var state = await StateOfAsync(mac);
        state.WaterAvailable.ShouldBe(false);
        state.WaterMessageId.ShouldBe(stillDry);
        state.LastHeartbeatAt.ShouldNotBeNull();

        await using var context = database.CreateContext();
        var dryAt = await context.MachineMessages.Where(message => message.Id == dry)
            .Select(message => message.ReceivedAt).SingleAsync(TestContext.Current.CancellationToken);
        state.WaterChangedAt.ShouldBe(dryAt);
        (await OutcomeOfAsync(dry)).Status.ShouldBe(ProcessingStatus.Processed);
    }

    [Fact]
    public async Task A_boot_records_its_id_and_is_not_a_sale()
    {
        var mac = NewMac();
        var messageId = await PushAsync(mac, ProcessingDriver.DeviceEvent(mac, "device_boot", ProcessingDriver.Json("'water_available':true")));

        await database.SharedApi.ProcessAllPendingAsync();

        (await OutcomeOfAsync(messageId)).Status.ShouldBe(ProcessingStatus.Processed);
        var state = await StateOfAsync(mac);
        state.LastBootId.ShouldBe("0123456789ABCDEF");
        state.LastBootAt.ShouldNotBeNull();
        state.WaterAvailable.ShouldBe(true);
        state.TotalUses.ShouldBe(0);
        (await UsagesOfAsync(mac)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_settings_acknowledgement_is_processed_and_is_not_a_sale()
    {
        var mac = NewMac();
        var messageId = await PushAsync(mac, ProcessingDriver.SettingsAck(mac, 1, "busy"));

        await database.SharedApi.ProcessAllPendingAsync();

        (await OutcomeOfAsync(messageId)).Status.ShouldBe(ProcessingStatus.Processed);
        (await UsagesOfAsync(mac)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_event_is_ignored()
    {
        var mac = NewMac();
        var messageId = await PushAsync(mac, ProcessingDriver.DeviceEvent(mac, "refund", string.Empty));

        await database.SharedApi.ProcessAllPendingAsync();

        (await OutcomeOfAsync(messageId)).Status.ShouldBe(ProcessingStatus.Ignored);
    }

    [Fact]
    public async Task Minute_water_is_a_configured_service()
    {
        var mac = NewMac();
        await PushAsync(mac, ProcessingDriver.Payment(mac, "minute_water", 25, 25, durationSeconds: 60));
        await database.SharedApi.ProcessAllPendingAsync();

        await using var context = database.CreateContext();
        var service = await context.ServiceDefinitions.IgnoreQueryFilters()
            .SingleAsync(definition => definition.Code == "minute_water", TestContext.Current.CancellationToken);
        service.IsConfigured.ShouldBeTrue();
        service.NameAr.ShouldBe("ماء بالدقيقة");
        (await UsagesOfAsync(mac)).ShouldHaveSingleItem().SaleAmount.ShouldBe(0.25m);
    }
}
