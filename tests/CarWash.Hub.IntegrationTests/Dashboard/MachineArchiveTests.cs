using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Alerts;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Application.Features.Dashboard;
using CarWash.Hub.Application.Features.Finance;
using CarWash.Hub.Application.Features.Machines;
using CarWash.Hub.Application.Features.Pricing;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.IntegrationTests.Dashboard;

/// <summary>
/// Deleting a machine archives it - a soft delete that keeps its history and revenue - and
/// restoring it puts it back where it was.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MachineArchiveTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly TimeZoneInfo Amman = TimeZoneInfo.FindSystemTimeZoneById("Asia/Amman");
    private static readonly AuthenticatedUser Admin = DashboardDriver.User(UserRole.SuperAdmin);

    private HttpClient _client = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Amman).DateTime);

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
    public async Task A_deleted_machine_leaves_every_list_but_waits_in_the_archive()
    {
        var (machineId, code, _) = await MachineAsync();

        await DeleteAsync(machineId);

        (await CardsAsync()).ShouldNotContain(card => card.Id == machineId);
        await Should.ThrowAsync<NotFoundException>(() => AdminAsync(service => service.GetAsync(machineId, Token)));

        var archived = (await ArchiveAsync()).Single(machine => machine.Id == machineId);
        archived.Code.ShouldBe(code);
        archived.StationName.ShouldNotBeNull();
        archived.DeletedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
        archived.StillReporting.ShouldBeFalse();

        await using var context = database.CreateContext();
        var row = await context.Machines.IgnoreQueryFilters().SingleAsync(machine => machine.Id == machineId, Token);
        row.IsDeleted.ShouldBeTrue("a soft delete: the row is kept");
        row.DeletedBy.ShouldBe(Admin.Id);
    }

    [Fact]
    public async Task The_money_a_deleted_machine_earned_stays_in_the_reports()
    {
        var (machineId, code, stationId) = await MachineAsync();
        await database.SharedApi.AsUserAsync<IStationPriceService, Guid>(Admin, prices => prices.SetAsync(
            new SetStationPriceRequest { StationId = stationId!.Value, Service = "soap", UnitPrice = 0.5m, EffectiveFrom = Today.AddDays(-3) }, Token));
        await UseAsync(code, "soap");

        await DeleteAsync(machineId);

        var report = await database.SharedApi.AsUserAsync<IFinanceReportService, FinanceReportDto>(
            Admin, finance => finance.GetReportAsync(new FinanceReportQuery { From = Today.AddDays(-1), To = Today, StationIds = [stationId!.Value] }, Token));
        report.Totals.Revenue.ShouldBe(0.5m);
    }

    [Fact]
    public async Task A_deleted_machine_that_keeps_reporting_is_still_recorded_and_flagged()
    {
        var (machineId, code, _) = await MachineAsync();
        await DeleteAsync(machineId);

        await UseAsync(code, "water");

        (await ArchiveAsync()).Single(machine => machine.Id == machineId).StillReporting.ShouldBeTrue();
        await using var context = database.CreateContext();
        (await context.MachineMessages.IgnoreQueryFilters().CountAsync(message => message.MachineId == machineId, Token)).ShouldBe(2);
    }

    [Fact]
    public async Task Restoring_puts_the_machine_back_at_its_station()
    {
        var (machineId, _, stationId) = await MachineAsync();
        await DeleteAsync(machineId);
        var archived = (await ArchiveAsync()).Single(machine => machine.Id == machineId);

        var restored = await AdminAsync(service => service.RestoreAsync(new MachineArchiveRequest { Id = machineId, RowVersion = archived.RowVersion }, Token));

        restored.GasStationId.ShouldBe(stationId);
        (await CardsAsync()).ShouldContain(card => card.Id == machineId);
        (await ArchiveAsync()).ShouldNotContain(machine => machine.Id == machineId);

        await using var context = database.CreateContext();
        var row = await context.Machines.IgnoreQueryFilters().SingleAsync(machine => machine.Id == machineId, Token);
        (row.IsDeleted, row.DeletedAt, row.DeletedBy).ShouldBe((false, (DateTimeOffset?)null, (Guid?)null));
    }

    [Fact]
    public async Task A_machine_whose_station_was_deleted_meanwhile_is_restored_to_the_inbox()
    {
        var (machineId, _, stationId) = await MachineAsync();
        await DeleteAsync(machineId);
        await using (var context = database.CreateContext())
        {
            var station = await context.GasStations.SingleAsync(s => s.Id == stationId, Token);
            context.GasStations.Remove(station);
            await context.SaveChangesAsync(Token);
        }

        var archived = (await ArchiveAsync()).Single(machine => machine.Id == machineId);
        archived.StationName.ShouldBeNull();

        var restored = await AdminAsync(service => service.RestoreAsync(new MachineArchiveRequest { Id = machineId, RowVersion = archived.RowVersion }, Token));

        restored.GasStationId.ShouldBeNull();
        (await AdminAsync(service => service.ListUnassignedAsync(Token))).ShouldContain(machine => machine.Id == machineId);
    }

    [Fact]
    public async Task Deleting_or_restoring_twice_finds_nothing_and_a_stale_version_is_a_conflict()
    {
        var (machineId, _, _) = await MachineAsync();
        var settings = await AdminAsync(service => service.GetAsync(machineId, Token));

        await Should.ThrowAsync<ConflictException>(() => AdminAsync(async service =>
        {
            await service.DeleteAsync(new MachineArchiveRequest { Id = machineId, RowVersion = "AAAAAAAAAAA=" }, Token);
            return true;
        }));

        await DeleteAsync(machineId);
        await Should.ThrowAsync<NotFoundException>(() => AdminAsync(async service =>
        {
            await service.DeleteAsync(new MachineArchiveRequest { Id = machineId, RowVersion = settings.RowVersion }, Token);
            return true;
        }));

        var archived = (await ArchiveAsync()).Single(machine => machine.Id == machineId);
        await AdminAsync(service => service.RestoreAsync(new MachineArchiveRequest { Id = machineId, RowVersion = archived.RowVersion }, Token));
        await Should.ThrowAsync<NotFoundException>(() =>
            AdminAsync(service => service.RestoreAsync(new MachineArchiveRequest { Id = machineId, RowVersion = archived.RowVersion }, Token)));
    }

    [Fact]
    public async Task Only_a_super_admin_deletes_lists_or_restores()
    {
        var (machineId, _, stationId) = await MachineAsync();
        var stationUser = DashboardDriver.User(UserRole.StationUser, stationId);

        await Should.ThrowAsync<ForbiddenException>(() => database.SharedApi.AsUserAsync<IMachineAdminService, bool>(stationUser, async service =>
        {
            await service.DeleteAsync(new MachineArchiveRequest { Id = machineId }, Token);
            return true;
        }));
        await Should.ThrowAsync<ForbiddenException>(() =>
            database.SharedApi.AsUserAsync<IMachineAdminService, IReadOnlyList<ArchivedMachineDto>>(stationUser, service => service.ListArchivedAsync(Token)));
        await Should.ThrowAsync<ForbiddenException>(() =>
            database.SharedApi.AsUserAsync<IMachineAdminService, MachineSettingsDto>(stationUser, service => service.RestoreAsync(new MachineArchiveRequest { Id = machineId }, Token)));
    }

    [Fact]
    public async Task A_deleted_machines_open_alerts_close_as_no_longer_eligible()
    {
        var (machineId, _, _) = await MachineAsync();
        await EvaluateAsync(machineId, DateTimeOffset.UtcNow.AddHours(30));

        await DeleteAsync(machineId);
        await EvaluateAsync(machineId, DateTimeOffset.UtcNow.AddHours(30));

        await using var context = database.CreateContext();
        var alert = await context.Set<Alert>().IgnoreQueryFilters().SingleAsync(a => a.MachineId == machineId, Token);
        alert.Resolution.ShouldBe(AlertResolution.NotEligible);
    }

    // A machine that has reported once, at a new station.
    private async Task<(Guid Id, string Code, Guid? StationId)> MachineAsync()
    {
        var stationId = await database.CreateStationAsync();
        var code = ApiRequests.NewMachineId();
        await UseAsync(code, "water");

        await using var context = database.CreateContext();
        var machine = await context.Machines.SingleAsync(m => m.Code == code, Token);
        machine.GasStationId = stationId;
        await context.SaveChangesAsync(Token);
        return (machine.Id, code, stationId);
    }

    private async Task UseAsync(string code, string service)
    {
        using var response = await _client.SendAndDisposeAsync(
            ApiRequests.Ingest(ApiRequests.Body(code, ProcessingDriver.ServiceEvent(service, DateTimeOffset.UtcNow.AddSeconds(-5)))));
        response.EnsureSuccessStatusCode();
        await database.SharedApi.ProcessAllPendingAsync();
    }

    private async Task DeleteAsync(Guid machineId)
    {
        var settings = await AdminAsync(service => service.GetAsync(machineId, Token));
        await AdminAsync(async service =>
        {
            await service.DeleteAsync(new MachineArchiveRequest { Id = machineId, RowVersion = settings.RowVersion }, Token);
            return true;
        });
    }

    private Task<T> AdminAsync<T>(Func<IMachineAdminService, Task<T>> call) =>
        database.SharedApi.AsUserAsync(Admin, call);

    private Task<IReadOnlyList<ArchivedMachineDto>> ArchiveAsync() => AdminAsync(service => service.ListArchivedAsync(Token));

    private Task<IReadOnlyList<MachineCardDto>> CardsAsync() =>
        database.SharedApi.AsUserAsync<IFleetDashboardService, IReadOnlyList<MachineCardDto>>(Admin, service => service.GetMachinesAsync(Token));

    private async Task EvaluateAsync(Guid machineId, DateTimeOffset at)
    {
        await using var scope = database.SharedApi.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAlertEvaluator>().EvaluateAsync([machineId], at, allowNoActivityRaise: true, Token);
    }
}
