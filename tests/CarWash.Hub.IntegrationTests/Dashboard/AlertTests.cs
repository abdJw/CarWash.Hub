using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Alerts;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Application.Features.Refills;
using CarWash.Hub.Application.Features.Stations;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.IntegrationTests.Dashboard;

/// <summary>
/// Alerts follow the machines: a service running down raises one escalating alert, a refill closes
/// it, a silent machine raises one no-activity alert outside its station's quiet hours, and each
/// station sees only its own. The evaluator is called with an explicit time where time must pass.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AlertTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly AuthenticatedUser _admin = DashboardDriver.User(UserRole.SuperAdmin);
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

    // A machine that has reported once, assigned to a new station (or none), with the station's quiet
    // hours as given - none by default, so silence counts around the clock.
    private async Task<(Guid Id, string Code, Guid? StationId)> MachineAsync(bool assigned = true, TimeOnly? quietStart = null, TimeOnly? quietEnd = null)
    {
        Guid? stationId = assigned ? await database.CreateStationAsync() : null;
        var code = ApiRequests.NewMachineId();
        await UseAsync(code, "water", Now.AddHours(-3));

        await using var context = database.CreateContext();
        var machine = await context.Machines.SingleAsync(m => m.Code == code, Token);
        machine.GasStationId = stationId;
        if (stationId is { } id)
        {
            var station = await context.GasStations.SingleAsync(s => s.Id == id, Token);
            (station.QuietHoursStart, station.QuietHoursEnd) = (quietStart, quietEnd);
        }

        await context.SaveChangesAsync(Token);
        return (machine.Id, code, stationId);
    }

    private async Task UseAsync(string code, string service, DateTimeOffset at, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            using var response = await _client.SendAndDisposeAsync(
                ApiRequests.Ingest(ApiRequests.Body(code, ProcessingDriver.ServiceEvent(service, at.AddSeconds(i)))));
            response.EnsureSuccessStatusCode();
        }

        await database.SharedApi.ProcessAllPendingAsync();
    }

    private async Task<Guid> RefillAsync(Guid machineId, string service, int capacity, DateTimeOffset at)
    {
        var id = await database.SharedApi.AsUserAsync<IRefillService, Guid>(_admin, refills => refills.RecordAsync(
            new RecordRefillRequest { MachineId = machineId, RefilledAt = at, Items = [new RefillLineDto(service, capacity)] }, Token));
        await database.SharedApi.ProcessAllPendingAsync();
        return id;
    }

    private async Task<AlertEvaluation> EvaluateAsync(Guid machineId, DateTimeOffset at, bool allowNoActivityRaise = true)
    {
        await using var scope = database.SharedApi.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAlertEvaluator>().EvaluateAsync([machineId], at, allowNoActivityRaise, Token);
    }

    private Task<IReadOnlyList<AlertDto>> AlertsAsync(Guid machineId, AuthenticatedUser? user = null) =>
        database.SharedApi.AsUserAsync<IAlertService, IReadOnlyList<AlertDto>>(
            user ?? _admin, alerts => alerts.ListAsync(new AlertQuery { MachineId = machineId, Take = 50 }, Token));

    private async Task SetMachineAsync(Guid machineId, Action<Domain.Entities.Machine> change)
    {
        await using var context = database.CreateContext();
        var machine = await context.Machines.SingleAsync(m => m.Id == machineId, Token);
        change(machine);
        await context.SaveChangesAsync(Token);
    }

    [Fact]
    public async Task A_service_running_down_escalates_one_alert_from_low_to_empty_to_used_after_empty()
    {
        var (machineId, code, _) = await MachineAsync();
        await RefillAsync(machineId, "soap", 10, Now.AddMinutes(-40));
        (await AlertsAsync(machineId)).ShouldBeEmpty();

        await UseAsync(code, "soap", Now.AddMinutes(-30), times: 8);    // 2 of 10 left
        var low = (await AlertsAsync(machineId)).ShouldHaveSingleItem();
        low.Type.ShouldBe(AlertType.Low);
        low.Severity.ShouldBe(AlertSeverity.Warning);
        low.Notify.ShouldBeTrue();
        (low.Remaining, low.Capacity).ShouldBe((2, 10));

        await UseAsync(code, "soap", Now.AddMinutes(-20), times: 2);    // empty
        await UseAsync(code, "soap", Now.AddMinutes(-10));              // and used again

        var alerts = await AlertsAsync(machineId);
        var open = alerts.Where(alert => alert.IsOpen).ShouldHaveSingleItem();
        open.Type.ShouldBe(AlertType.UsedAfterEmpty);
        open.Severity.ShouldBe(AlertSeverity.Critical);
        open.Notify.ShouldBeTrue();
        alerts.Where(alert => !alert.IsOpen).Select(alert => (alert.Type, alert.Resolution))
            .ShouldBe([(AlertType.Empty, AlertResolution.Escalated), (AlertType.Low, AlertResolution.Escalated)], ignoreOrder: true);
    }

    [Fact]
    public async Task A_refill_closes_the_alert_even_with_nothing_else_to_process()
    {
        var (machineId, code, _) = await MachineAsync();
        await RefillAsync(machineId, "soap", 5, Now.AddMinutes(-40));
        await UseAsync(code, "soap", Now.AddMinutes(-30), times: 5);
        (await AlertsAsync(machineId)).ShouldHaveSingleItem().Type.ShouldBe(AlertType.Empty);

        // The refill's rebuild is the only work left: the processor's no-pending path resolves it.
        await RefillAsync(machineId, "soap", 300, Now.AddMinutes(-1));

        var closed = (await AlertsAsync(machineId)).ShouldHaveSingleItem();
        closed.IsOpen.ShouldBeFalse();
        closed.Resolution.ShouldBe(AlertResolution.Refilled);
    }

    [Fact]
    public async Task A_backdated_refill_counts_as_a_refill()
    {
        var (machineId, code, _) = await MachineAsync();
        await RefillAsync(machineId, "vacuum", 4, Now.AddHours(-2));
        await UseAsync(code, "vacuum", Now.AddMinutes(-90), times: 4);
        (await AlertsAsync(machineId)).ShouldHaveSingleItem().Type.ShouldBe(AlertType.Empty);

        // Refilled an hour ago, recorded now: earlier than the alert, later than its refill.
        await RefillAsync(machineId, "vacuum", 100, Now.AddMinutes(-60));

        (await AlertsAsync(machineId)).ShouldHaveSingleItem().Resolution.ShouldBe(AlertResolution.Refilled);
    }

    [Fact]
    public async Task Voiding_the_refill_brings_the_alert_back()
    {
        var (machineId, code, _) = await MachineAsync();
        await RefillAsync(machineId, "soap", 3, Now.AddHours(-2));
        await UseAsync(code, "soap", Now.AddMinutes(-90), times: 3);
        var refill = await RefillAsync(machineId, "soap", 100, Now.AddMinutes(-30));
        (await AlertsAsync(machineId)).Where(alert => alert.IsOpen).ShouldBeEmpty();

        await database.SharedApi.AsUserAsync<IRefillService, bool>(_admin, async refills =>
        {
            await refills.VoidAsync(new VoidRefillRequest(refill, "Recorded on the wrong machine"), Token);
            return true;
        });
        await database.SharedApi.ProcessAllPendingAsync();

        var alerts = await AlertsAsync(machineId);
        alerts.Count(alert => alert.Type == AlertType.Empty).ShouldBe(2);
        alerts.Where(alert => alert.IsOpen).ShouldHaveSingleItem().Type.ShouldBe(AlertType.Empty);
    }

    [Fact]
    public async Task An_unassigned_machine_never_alerts()
    {
        var (machineId, code, _) = await MachineAsync(assigned: false);
        await RefillAsync(machineId, "soap", 2, Now.AddMinutes(-40));
        await UseAsync(code, "soap", Now.AddMinutes(-30), times: 3);

        (await EvaluateAsync(machineId, Now.AddHours(10))).Raised.ShouldBe(0);
        (await AlertsAsync(machineId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Taking_a_machine_out_of_service_closes_its_alerts()
    {
        var (machineId, code, _) = await MachineAsync();
        await RefillAsync(machineId, "soap", 2, Now.AddMinutes(-40));
        await UseAsync(code, "soap", Now.AddMinutes(-30), times: 2);

        await SetMachineAsync(machineId, machine => machine.IsActive = false);
        await EvaluateAsync(machineId, DateTimeOffset.UtcNow);

        var alert = (await AlertsAsync(machineId)).ShouldHaveSingleItem();
        alert.IsOpen.ShouldBeFalse();
        alert.Resolution.ShouldBe(AlertResolution.NotEligible);
    }

    [Fact]
    public async Task A_silent_machine_raises_one_no_activity_alert_and_its_next_message_closes_it()
    {
        var (machineId, code, _) = await MachineAsync();

        // Seven hours on, past the default six - but not while raising is held back after a restart.
        (await EvaluateAsync(machineId, Now.AddHours(7), allowNoActivityRaise: false)).Raised.ShouldBe(0);

        (await EvaluateAsync(machineId, Now.AddHours(7))).Raised.ShouldBe(1);
        (await EvaluateAsync(machineId, Now.AddHours(8))).Raised.ShouldBe(0);

        var alert = (await AlertsAsync(machineId)).ShouldHaveSingleItem();
        alert.Type.ShouldBe(AlertType.NoActivity);
        alert.Service.ShouldBeNull();
        alert.Notify.ShouldBeTrue();
        alert.LastSeenAt.ShouldNotBeNull();

        await UseAsync(code, "water", DateTimeOffset.UtcNow);

        (await AlertsAsync(machineId)).ShouldHaveSingleItem().Resolution.ShouldBe(AlertResolution.ActivityResumed);
    }

    [Fact]
    public async Task Quiet_hours_do_not_count_towards_silence()
    {
        // Quiet from an hour ago until three hours from now, in the station's local time; one hour of
        // silence is enough for this machine.
        var zone = database.SharedApi.Services.GetRequiredService<IBusinessTimeZone>();
        var local = TimeOnly.FromDateTime(zone.ToLocal(Now).DateTime);
        var (machineId, _, _) = await MachineAsync(quietStart: local.AddHours(-1), quietEnd: local.AddHours(3));
        await SetMachineAsync(machineId, machine => machine.NoActivityAfterHours = 1);

        (await EvaluateAsync(machineId, Now.AddHours(2.5))).Raised.ShouldBe(0);
        (await EvaluateAsync(machineId, Now.AddHours(4.5))).Raised.ShouldBe(1);
    }

    [Fact]
    public async Task Each_station_sees_and_acknowledges_only_its_own_alerts()
    {
        var (machineId, code, stationId) = await MachineAsync();
        await RefillAsync(machineId, "soap", 1, Now.AddMinutes(-40));
        await UseAsync(code, "soap", Now.AddMinutes(-30));

        var attendant = DashboardDriver.User(UserRole.StationUser, stationId);
        var stranger = DashboardDriver.User(UserRole.StationUser, await database.CreateStationAsync());

        var alert = (await AlertsAsync(machineId, attendant)).ShouldHaveSingleItem();
        (await AlertsAsync(machineId, stranger)).ShouldBeEmpty();

        var theirs = await database.SharedApi.AsUserAsync<IAlertService, AlertSummaryDto>(stranger, alerts => alerts.GetSummaryAsync(null, Token));
        theirs.Open.ShouldBe(0);

        await Should.ThrowAsync<NotFoundException>(() => database.SharedApi.AsUserAsync<IAlertService, bool>(stranger, async alerts =>
        {
            await alerts.AcknowledgeAsync(alert.Id, Token);
            return true;
        }));

        var before = await database.SharedApi.AsUserAsync<IAlertService, AlertSummaryDto>(attendant, alerts => alerts.GetSummaryAsync(alert.Id - 1, Token));
        (before.Open, before.Unacknowledged, before.UnacknowledgedCritical).ShouldBe((1, 1, 1));
        before.Fresh.ShouldHaveSingleItem().Id.ShouldBe(alert.Id);
        before.LatestId.ShouldBe(alert.Id);

        await database.SharedApi.AsUserAsync<IAlertService, bool>(attendant, async alerts =>
        {
            await alerts.AcknowledgeAsync(alert.Id, Token);
            return true;
        });

        var after = await database.SharedApi.AsUserAsync<IAlertService, AlertSummaryDto>(attendant, alerts => alerts.GetSummaryAsync(alert.Id, Token));
        (after.Open, after.Unacknowledged).ShouldBe((1, 0));
        after.Fresh.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_station_has_both_quiet_hours_or_neither()
    {
        static SaveStationRequest Station(TimeOnly? start, TimeOnly? end) => new()
        {
            Code = $"Q-{Guid.NewGuid():N}"[..20],
            Name = "Quiet hours",
            NameAr = "ساعات هادئة",
            QuietHoursStart = start,
            QuietHoursEnd = end,
        };

        foreach (var (start, end) in new (TimeOnly?, TimeOnly?)[] { (new(23, 0), null), (null, new(7, 0)), (new(7, 0), new(7, 0)) })
        {
            await Should.ThrowAsync<ValidationException>(() => database.SharedApi.AsUserAsync<IGasStationService, StationDto>(
                _admin, stations => stations.SaveAsync(Station(start, end), Token)));
        }

        var open24 = await database.SharedApi.AsUserAsync<IGasStationService, StationDto>(_admin, stations => stations.SaveAsync(Station(null, null), Token));
        (open24.QuietHoursStart, open24.QuietHoursEnd).ShouldBe((null, null));

        var night = await database.SharedApi.AsUserAsync<IGasStationService, StationDto>(_admin, stations => stations.SaveAsync(Station(new(22, 30), new(6, 0)), Token));
        (night.QuietHoursStart, night.QuietHoursEnd).ShouldBe((new TimeOnly(22, 30), new TimeOnly(6, 0)));
    }

    [Fact]
    public async Task An_acknowledged_alert_still_resolves()
    {
        var (machineId, code, _) = await MachineAsync();
        await RefillAsync(machineId, "soap", 1, Now.AddMinutes(-40));
        await UseAsync(code, "soap", Now.AddMinutes(-30));
        var alert = (await AlertsAsync(machineId)).ShouldHaveSingleItem();

        await database.SharedApi.AsUserAsync<IAlertService, bool>(_admin, async alerts =>
        {
            await alerts.AcknowledgeAsync(alert.Id, Token);
            return true;
        });
        (await EvaluateAsync(machineId, DateTimeOffset.UtcNow)).Raised.ShouldBe(0);

        await RefillAsync(machineId, "soap", 50, Now.AddMinutes(-1));

        var closed = (await AlertsAsync(machineId)).ShouldHaveSingleItem();
        closed.AcknowledgedAt.ShouldNotBeNull();
        closed.Resolution.ShouldBe(AlertResolution.Refilled);
    }
}
