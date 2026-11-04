using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Application.Features.Dashboard;
using CarWash.Hub.Application.Features.DeviceSettings;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CarWash.Hub.IntegrationTests.Dashboard;

/// <summary>
/// What the dashboard shows of a machine on the event firmware: online or offline, no sales, its
/// water, and its settings - sent, confirmed and where they stand.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DeviceDashboardTests(DatabaseFixture database) : IAsyncLifetime
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
    public async Task A_reporting_machine_shows_its_water_and_goes_offline_when_it_stops()
    {
        var (machineId, mac) = await HeartbeatMachineAsync(water: false);

        var card = await CardAsync(machineId);
        card.ReportsHeartbeat.ShouldBeTrue();
        card.Activity.ShouldBe(MachineActivity.Idle);
        card.WaterAvailable.ShouldBe(false);
        card.WaterIsStale.ShouldBeFalse();

        await StateAsync(machineId, state => state.LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-10));

        var offline = await CardAsync(machineId);
        offline.Activity.ShouldBe(MachineActivity.Offline);
        offline.WaterIsStale.ShouldBeTrue("a water report from an offline machine may no longer hold");
        (await OverviewAsync()).OfflineCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task An_online_machine_that_has_not_sold_for_hours_shows_no_sales()
    {
        var (machineId, _) = await HeartbeatMachineAsync(water: true);
        await StateAsync(machineId, state => state.LastUsageAt = DateTimeOffset.UtcNow.AddHours(-8));

        (await CardAsync(machineId)).Activity.ShouldBe(MachineActivity.NoSales);
    }

    [Fact]
    public async Task The_machine_page_shows_its_settings_and_last_start_up()
    {
        var (machineId, mac) = await HeartbeatMachineAsync(water: true);
        await PostAsync(mac, ProcessingDriver.DeviceEvent(mac, "device_boot", ProcessingDriver.Json("'water_available':true")));
        await using (var context = database.CreateContext())
        {
            context.Set<MachineDeviceSettings>().Add(new MachineDeviceSettings
            {
                Id = machineId,
                DesiredRevision = 2,
                ServicesJson = """{"water":{"price_qirsh":50,"durations_seconds":[180,180,210]},"minute_water":{"price_qirsh":25,"duration_seconds":60}}""",
                AppliedRevision = 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync(Token);
        }

        var detail = await database.SharedApi.AsUserAsync<IFleetDashboardService, MachineDetailDto>(
            Admin, service => service.GetMachineAsync(machineId, DashboardRange.Last24Hours, Token));

        var settings = detail.DeviceSettings.ShouldNotBeNull();
        settings.Status.ShouldBe(DeviceSettingsStatus.Pending);
        settings.DesiredRevision.ShouldBe(2);
        settings.AppliedRevision.ShouldBe(1);
        settings.Services.Select(service => service.Service).ShouldBe(["water", "minute_water"]);
        settings.Services[1].Seconds.ShouldBe([60]);
        settings.LastBootAt.ShouldNotBeNull();
        settings.LastHeartbeatAt.ShouldNotBeNull();
        detail.Card.SettingsStatus.ShouldBe(DeviceSettingsStatus.Pending);
    }

    [Fact]
    public async Task A_machine_on_the_earlier_firmware_has_no_settings_card()
    {
        var mac = NewMac();
        await PostAsync(mac, ProcessingDriver.ServiceEvent("soap", DateTimeOffset.UtcNow.AddSeconds(-5), mac));
        var machineId = await AssignAsync(mac);

        // Even with a settings row from the reconciler: this firmware never polls, so "pending" would never clear.
        await using (var context = database.CreateContext())
        {
            context.Set<MachineDeviceSettings>().Add(new MachineDeviceSettings { Id = machineId, DesiredRevision = 1, ServicesJson = "{}", UpdatedAt = DateTimeOffset.UtcNow });
            await context.SaveChangesAsync(Token);
        }

        var detail = await database.SharedApi.AsUserAsync<IFleetDashboardService, MachineDetailDto>(
            Admin, service => service.GetMachineAsync(machineId, DashboardRange.Last24Hours, Token));

        detail.DeviceSettings.ShouldBeNull();
        detail.Card.SettingsStatus.ShouldBe(DeviceSettingsStatus.NotConfigured);
        detail.Card.ReportsHeartbeat.ShouldBeFalse();
        detail.Card.WaterAvailable.ShouldBeNull();
    }

    private static string NewMac() =>
        string.Join(':', Guid.NewGuid().ToByteArray().Take(6).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));

    private async Task<(Guid Id, string Mac)> HeartbeatMachineAsync(bool water)
    {
        var mac = NewMac();
        await PostAsync(mac, ProcessingDriver.WaterStatus(mac, water));
        return (await AssignAsync(mac), mac);
    }

    private async Task<Guid> AssignAsync(string mac)
    {
        var stationId = await database.CreateStationAsync();
        await using var context = database.CreateContext();
        var machine = await context.Machines.SingleAsync(m => m.Code == mac, Token);
        machine.GasStationId = stationId;

        // Open around the clock, so silence counts the same whatever time the test runs.
        var station = await context.GasStations.SingleAsync(s => s.Id == stationId, Token);
        (station.QuietHoursStart, station.QuietHoursEnd) = (null, null);
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

    private async Task<MachineCardDto> CardAsync(Guid machineId) =>
        (await database.SharedApi.AsUserAsync<IFleetDashboardService, IReadOnlyList<MachineCardDto>>(
            Admin, service => service.GetMachinesAsync(Token))).Single(card => card.Id == machineId);

    private Task<FleetOverviewDto> OverviewAsync() =>
        database.SharedApi.AsUserAsync<IFleetDashboardService, FleetOverviewDto>(
            Admin, service => service.GetOverviewAsync(DashboardRange.Last24Hours, Token));
}
