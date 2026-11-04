using CarWash.Hub.Application.Features.Alerts;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.IntegrationTests.Dashboard;

/// <summary>
/// Alerts for the firmware that reports every 30 seconds: offline when it stops reporting, no
/// activity measured in sales rather than messages, and no water - while machines on the earlier
/// firmware keep the alerts they had.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class HeartbeatAlertTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly AuthenticatedUser Admin = DashboardDriver.User(UserRole.SuperAdmin);

    private HttpClient _client = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

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
    public async Task A_heartbeat_machine_that_stops_reporting_is_offline_and_its_next_report_closes_it()
    {
        var (machineId, mac, now) = await HeartbeatMachineAsync();

        (await EvaluateAsync(machineId, now.AddMinutes(2))).Raised.ShouldBe(0);
        (await EvaluateAsync(machineId, now.AddMinutes(4), allowRaise: false)).Raised.ShouldBe(0, "held back after a restart");
        (await EvaluateAsync(machineId, now.AddMinutes(4))).Raised.ShouldBe(1);

        var alert = (await AlertsAsync(machineId)).ShouldHaveSingleItem();
        alert.Type.ShouldBe(AlertType.Offline);
        alert.Severity.ShouldBe(AlertSeverity.Warning);
        alert.Notify.ShouldBeTrue();
        alert.LastSeenAt.ShouldNotBeNull();

        await PostAsync(mac, ProcessingDriver.WaterStatus(mac, waterAvailable: true));

        (await AlertsAsync(machineId)).ShouldHaveSingleItem().Resolution.ShouldBe(AlertResolution.ActivityResumed);
    }

    [Fact]
    public async Task An_offline_machine_is_not_also_called_quiet()
    {
        var (machineId, _, now) = await HeartbeatMachineAsync();

        await EvaluateAsync(machineId, now.AddHours(7));

        (await AlertsAsync(machineId)).ShouldHaveSingleItem().Type.ShouldBe(AlertType.Offline);
    }

    [Fact]
    public async Task A_machine_on_the_earlier_firmware_is_never_called_offline()
    {
        var mac = NewMac();
        await PostAsync(mac, ProcessingDriver.ServiceEvent("soap", DateTimeOffset.UtcNow.AddSeconds(-5), mac));
        var machineId = await AssignAsync(mac);

        (await EvaluateAsync(machineId, DateTimeOffset.UtcNow.AddMinutes(30))).Raised.ShouldBe(0);
        (await EvaluateAsync(machineId, DateTimeOffset.UtcNow.AddHours(7))).Raised.ShouldBe(1);
        (await AlertsAsync(machineId)).ShouldHaveSingleItem().Type.ShouldBe(AlertType.NoActivity);
    }

    [Fact]
    public async Task An_online_machine_with_no_sales_raises_no_activity_and_a_sale_closes_it()
    {
        var (machineId, mac, now) = await HeartbeatMachineAsync();

        // Still reporting seven hours on, but nothing sold.
        await StateAsync(machineId, state => (state.LastSeenAt, state.LastHeartbeatAt) = (now.AddHours(7), now.AddHours(7)));
        (await EvaluateAsync(machineId, now.AddHours(7))).Raised.ShouldBe(1);

        var alert = (await AlertsAsync(machineId)).ShouldHaveSingleItem();
        alert.Type.ShouldBe(AlertType.NoActivity);
        alert.LastSaleAt.ShouldNotBeNull("measured in sales, from when the machine appeared");

        await PostAsync(mac, ProcessingDriver.Payment(mac, "water", 50, 50));

        (await AlertsAsync(machineId)).ShouldHaveSingleItem().Resolution.ShouldBe(AlertResolution.ActivityResumed);
    }

    [Fact]
    public async Task No_water_is_a_critical_alert_after_a_debounce_and_water_closes_it()
    {
        var (machineId, mac, _) = await HeartbeatMachineAsync();
        await PostAsync(mac, ProcessingDriver.WaterStatus(mac, waterAvailable: false));
        var dryAt = DateTimeOffset.UtcNow;

        (await EvaluateAsync(machineId, dryAt.AddSeconds(20))).Raised.ShouldBe(0, "a blip is not an alert");
        (await EvaluateAsync(machineId, dryAt.AddMinutes(2))).Raised.ShouldBe(1);

        var alert = (await AlertsAsync(machineId)).ShouldHaveSingleItem();
        alert.Type.ShouldBe(AlertType.WaterUnavailable);
        alert.Severity.ShouldBe(AlertSeverity.Critical);
        alert.WaterUnavailableSince.ShouldNotBeNull();

        await PostAsync(mac, ProcessingDriver.WaterStatus(mac, waterAvailable: true));

        (await AlertsAsync(machineId)).ShouldHaveSingleItem().Resolution.ShouldBe(AlertResolution.WaterRestored);
    }

    [Fact]
    public async Task Machine_alerts_are_listed_as_machine_status_never_as_refills()
    {
        var (machineId, _, now) = await HeartbeatMachineAsync();
        await EvaluateAsync(machineId, now.AddMinutes(4));

        var machine = await database.SharedApi.AsUserAsync<IAlertService, IReadOnlyList<AlertDto>>(
            Admin, alerts => alerts.ListAsync(new AlertQuery { MachineId = machineId, Kind = AlertKind.NoActivity, Take = 50 }, Token));
        var refill = await database.SharedApi.AsUserAsync<IAlertService, IReadOnlyList<AlertDto>>(
            Admin, alerts => alerts.ListAsync(new AlertQuery { MachineId = machineId, Kind = AlertKind.Refill, Take = 50 }, Token));

        machine.ShouldHaveSingleItem().Type.ShouldBe(AlertType.Offline);
        refill.ShouldBeEmpty();
    }

    private static string NewMac() =>
        string.Join(':', Guid.NewGuid().ToByteArray().Take(6).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));

    // A machine on the event firmware that has just reported, at a station with no quiet hours.
    private async Task<(Guid Id, string Mac, DateTimeOffset Now)> HeartbeatMachineAsync()
    {
        var mac = NewMac();
        await PostAsync(mac, ProcessingDriver.WaterStatus(mac, waterAvailable: true));
        return (await AssignAsync(mac), mac, DateTimeOffset.UtcNow);
    }

    private async Task<Guid> AssignAsync(string mac)
    {
        var stationId = await database.CreateStationAsync();
        await using var context = database.CreateContext();
        var machine = await context.Machines.SingleAsync(m => m.Code == mac, Token);
        machine.GasStationId = stationId;
        await context.SaveChangesAsync(Token);
        return machine.Id;
    }

    private async Task StateAsync(Guid machineId, Action<MachineState> change)
    {
        await using var context = database.CreateContext();
        var state = await context.Set<MachineState>().SingleAsync(s => s.Id == machineId, Token);
        change(state);
        await context.SaveChangesAsync(Token);
    }

    private async Task PostAsync(string mac, string data)
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(mac, data)));
        response.EnsureSuccessStatusCode();
        await database.SharedApi.ProcessAllPendingAsync();
    }

    private async Task<AlertEvaluation> EvaluateAsync(Guid machineId, DateTimeOffset at, bool allowRaise = true)
    {
        await using var scope = database.SharedApi.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAlertEvaluator>().EvaluateAsync([machineId], at, allowRaise, Token);
    }

    private Task<IReadOnlyList<AlertDto>> AlertsAsync(Guid machineId) =>
        database.SharedApi.AsUserAsync<IAlertService, IReadOnlyList<AlertDto>>(
            Admin, alerts => alerts.ListAsync(new AlertQuery { MachineId = machineId, Take = 50 }, Token));
}
