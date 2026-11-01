using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Application.Features.Dashboard;
using CarWash.Hub.Application.Features.Refills;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CarWash.Hub.IntegrationTests.Dashboard;

/// <summary>
/// Refills drive the levels: each use counts against the latest standing refill of its service, a
/// backdated refill counts only what came after it, a void restores the refill before, and only the
/// right people may record or void at a station.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RefillTests(DatabaseFixture database) : IAsyncLifetime
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

    private async Task<(Guid Id, string Code)> MachineAsync(Guid? stationId = null)
    {
        var code = ApiRequests.NewMachineId();
        await UseAsync(code, "water", Now.AddHours(-2));

        await using var context = database.CreateContext();
        var machine = await context.Machines.SingleAsync(m => m.Code == code, Token);
        machine.GasStationId = stationId;
        await context.SaveChangesAsync(Token);
        return (machine.Id, code);
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

    private Task<Guid> RefillAsync(Guid machineId, string service, int capacity, DateTimeOffset at, AuthenticatedUser? by = null) =>
        database.SharedApi.AsUserAsync<IRefillService, Guid>(by ?? _admin, refills => refills.RecordAsync(
            new RecordRefillRequest { MachineId = machineId, RefilledAt = at, Items = [new RefillLineDto(service, capacity)] }, Token));

    private async Task<ServiceLevelDto> LevelAsync(Guid machineId, string service)
    {
        await database.SharedApi.ProcessAllPendingAsync();
        var detail = await database.SharedApi.AsUserAsync<IFleetDashboardService, MachineDetailDto>(
            _admin, dashboard => dashboard.GetMachineAsync(machineId, DashboardRange.Last24Hours, Token));
        return detail.Card.Levels.Single(level => level.Service == service);
    }

    [Fact]
    public async Task Uses_after_a_refill_move_the_level_from_ok_to_low_to_empty_to_overused()
    {
        var (machineId, code) = await MachineAsync();
        await RefillAsync(machineId, "soap", 10, Now.AddMinutes(-30));

        var fresh = await LevelAsync(machineId, "soap");
        fresh.Level.ShouldBe(ServiceLevel.Ok);
        fresh.Capacity.ShouldBe(10);
        fresh.Remaining.ShouldBe(10);
        fresh.PercentRemaining.ShouldBe(100);

        await UseAsync(code, "soap", Now.AddMinutes(-20), times: 8);
        var low = await LevelAsync(machineId, "soap");
        low.Level.ShouldBe(ServiceLevel.Low);
        low.Remaining.ShouldBe(2);

        await UseAsync(code, "soap", Now.AddMinutes(-10), times: 2);
        (await LevelAsync(machineId, "soap")).Level.ShouldBe(ServiceLevel.Empty);

        await UseAsync(code, "soap", Now.AddMinutes(-5));
        var overused = await LevelAsync(machineId, "soap");
        overused.Level.ShouldBe(ServiceLevel.Overused);
        overused.Remaining.ShouldBe(-1);
    }

    [Fact]
    public async Task A_backdated_refill_counts_only_the_uses_after_it()
    {
        var (machineId, code) = await MachineAsync();
        await UseAsync(code, "soap", Now.AddMinutes(-40), times: 3);
        await UseAsync(code, "soap", Now.AddMinutes(-10), times: 2);

        // Recorded now, but done 20 minutes ago: between the two groups of uses.
        await RefillAsync(machineId, "soap", 50, Now.AddMinutes(-20));

        var level = await LevelAsync(machineId, "soap");
        level.Used.ShouldBe(2);
        level.Remaining.ShouldBe(48);
    }

    [Fact]
    public async Task Voiding_a_refill_brings_back_the_one_before()
    {
        var (machineId, code) = await MachineAsync();
        await RefillAsync(machineId, "water", 10, Now.AddMinutes(-60));
        await UseAsync(code, "water", Now.AddMinutes(-50), times: 4);
        var mistake = await RefillAsync(machineId, "water", 500, Now.AddMinutes(-30));
        (await LevelAsync(machineId, "water")).Capacity.ShouldBe(500);

        await database.SharedApi.AsUserAsync<IRefillService, bool>(_admin, async refills =>
        {
            await refills.VoidAsync(new VoidRefillRequest(mistake, "Typed 500 instead of 50"), Token);
            return true;
        });

        var restored = await LevelAsync(machineId, "water");
        restored.Capacity.ShouldBe(10);
        restored.Used.ShouldBe(4);

        var history = await database.SharedApi.AsUserAsync<IRefillService, IReadOnlyList<RefillDto>>(_admin, refills => refills.GetHistoryAsync(machineId, Token));
        var voided = history.Single(refill => refill.Id == mistake);
        voided.IsVoided.ShouldBeTrue();
        voided.VoidReason.ShouldBe("Typed 500 instead of 50");
        voided.CanVoid.ShouldBeFalse();
    }

    [Fact]
    public async Task One_refill_can_cover_several_services()
    {
        var (machineId, _) = await MachineAsync();

        await database.SharedApi.AsUserAsync<IRefillService, Guid>(_admin, refills => refills.RecordAsync(
            new RecordRefillRequest { MachineId = machineId, Items = [new RefillLineDto("soap", 300), new RefillLineDto("water", 200)] }, Token));

        (await LevelAsync(machineId, "soap")).Capacity.ShouldBe(300);
        (await LevelAsync(machineId, "water")).Capacity.ShouldBe(200);
    }

    [Fact]
    public async Task The_refill_form_suggests_the_last_capacity()
    {
        var (machineId, _) = await MachineAsync();
        await RefillAsync(machineId, "vacuum", 120, Now.AddMinutes(-5));
        await database.SharedApi.ProcessAllPendingAsync();

        var form = await database.SharedApi.AsUserAsync<IRefillService, RefillFormDto>(_admin, refills => refills.GetFormAsync(machineId, Token));

        form.Services.Single(service => service.Service == "vacuum").SuggestedCapacity.ShouldBe(120);
    }

    [Fact]
    public async Task An_invalid_refill_is_refused_field_by_field()
    {
        var (machineId, _) = await MachineAsync();

        var empty = await Should.ThrowAsync<ValidationException>(() => database.SharedApi.AsUserAsync<IRefillService, Guid>(_admin,
            refills => refills.RecordAsync(new RecordRefillRequest { MachineId = machineId }, Token)));
        empty.Errors.Keys.ShouldContain(nameof(RecordRefillRequest.Items));

        var future = await Should.ThrowAsync<ValidationException>(() => RefillAsync(machineId, "soap", 10, Now.AddHours(2)));
        future.Errors.Keys.ShouldContain(nameof(RecordRefillRequest.RefilledAt));

        await Should.ThrowAsync<ValidationException>(() => RefillAsync(machineId, "soap", 0, Now));
    }

    [Fact]
    public async Task A_station_user_refills_their_own_machines_only()
    {
        var stationA = await database.CreateStationAsync();
        var stationB = await database.CreateStationAsync();
        var (ownMachine, _) = await MachineAsync(stationA);
        var (otherMachine, _) = await MachineAsync(stationB);
        var attendant = DashboardDriver.User(UserRole.StationUser, stationA);

        await RefillAsync(ownMachine, "soap", 100, Now, attendant);
        (await LevelAsync(ownMachine, "soap")).Capacity.ShouldBe(100);

        await Should.ThrowAsync<NotFoundException>(() => RefillAsync(otherMachine, "soap", 100, Now, attendant));
    }

    [Fact]
    public async Task A_station_user_may_void_their_own_refill_but_not_someone_elses()
    {
        var station = await database.CreateStationAsync();
        var (machineId, _) = await MachineAsync(station);
        var attendant = DashboardDriver.User(UserRole.StationUser, station);
        var colleague = DashboardDriver.User(UserRole.StationUser, station);

        var mine = await RefillAsync(machineId, "soap", 100, Now, attendant);
        var theirs = await RefillAsync(machineId, "water", 100, Now, colleague);

        await Should.ThrowAsync<ForbiddenException>(() => database.SharedApi.AsUserAsync<IRefillService, bool>(attendant, async refills =>
        {
            await refills.VoidAsync(new VoidRefillRequest(theirs, null), Token);
            return true;
        }));

        await database.SharedApi.AsUserAsync<IRefillService, bool>(attendant, async refills =>
        {
            await refills.VoidAsync(new VoidRefillRequest(mine, "wrong machine"), Token);
            return true;
        });

        var history = await database.SharedApi.AsUserAsync<IRefillService, IReadOnlyList<RefillDto>>(attendant, refills => refills.GetHistoryAsync(machineId, Token));
        history.Single(refill => refill.Id == mine).IsVoided.ShouldBeTrue();
        history.Single(refill => refill.Id == theirs).CanVoid.ShouldBeFalse();
    }
}
