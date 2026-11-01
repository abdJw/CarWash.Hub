using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Dashboard;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CarWash.Hub.IntegrationTests.Dashboard;

/// <summary>
/// What the dashboard reads, through the same scope-per-operation path its pages use: real usage
/// from processed messages, and station isolation - a station user never sees another station's
/// machine, even by its id.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class FleetDashboardTests(DatabaseFixture database) : IAsyncLifetime
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

    // A machine that has reported, assigned to a station (or not).
    private async Task<Guid> MachineAsync(Guid? stationId, params string[] services)
    {
        var code = ApiRequests.NewMachineId();
        var offset = 0;
        foreach (var service in services.DefaultIfEmpty("soap"))
        {
            using var response = await _client.SendAndDisposeAsync(
                ApiRequests.Ingest(ApiRequests.Body(code, ProcessingDriver.ServiceEvent(service, Now.AddSeconds(-30 + offset++)))));
            response.EnsureSuccessStatusCode();
        }

        await database.SharedApi.ProcessAllPendingAsync();

        await using var context = database.CreateContext();
        var machine = await context.Machines.SingleAsync(m => m.Code == code, TestContext.Current.CancellationToken);
        machine.GasStationId = stationId;
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return machine.Id;
    }

    [Fact]
    public async Task A_machine_page_shows_its_processed_usage()
    {
        var machineId = await MachineAsync(null, "soap", "water", "soap");

        var detail = await database.SharedApi.AsUserAsync<IFleetDashboardService, MachineDetailDto>(
            DashboardDriver.User(UserRole.SuperAdmin),
            service => service.GetMachineAsync(machineId, DashboardRange.Last24Hours));

        detail.Usage.Total.ShouldBe(3);
        detail.Usage.Buckets.Count.ShouldBe(24);
        detail.Usage.Totals.Single(total => total.Service == "soap").Count.ShouldBe(2);
        detail.Card.UsesTodayTotal.ShouldBeInRange(0, 3, "uses straddling local midnight count for yesterday");
        detail.Card.TotalUses.ShouldBe(3);
        detail.Card.Activity.ShouldBe(MachineActivity.Busy);
        detail.RecentUses.Count.ShouldBe(3);
        detail.RecentMessages.Count.ShouldBe(3);
        detail.RecentMessages.ShouldAllBe(message => message.Status == ProcessingStatus.Processed);
    }

    [Theory]
    [InlineData(DashboardRange.Last7Days, 7)]
    [InlineData(DashboardRange.Last30Days, 30)]
    public async Task Daily_ranges_have_one_bucket_per_business_day(DashboardRange range, int days)
    {
        var overview = await database.SharedApi.AsUserAsync<IFleetDashboardService, FleetOverviewDto>(
            DashboardDriver.User(UserRole.SuperAdmin),
            service => service.GetOverviewAsync(range));

        overview.Usage.Buckets.Count.ShouldBe(days);
        overview.Usage.Buckets.Select(bucket => bucket.Start).ShouldBeInOrder();
        overview.Last12Hours.Count.ShouldBe(12);
    }

    [Fact]
    public async Task A_station_user_sees_only_their_own_stations_machines()
    {
        var stationA = await database.CreateStationAsync();
        var stationB = await database.CreateStationAsync();
        var machineA = await MachineAsync(stationA);
        var machineB = await MachineAsync(stationB);

        var seenByA = await database.SharedApi.AsUserAsync<IFleetDashboardService, IReadOnlyList<MachineCardDto>>(
            DashboardDriver.User(UserRole.StationUser, stationA),
            service => service.GetMachinesAsync());

        seenByA.Select(machine => machine.Id).ShouldBe([machineA]);
        seenByA[0].StationName.ShouldNotBeNull();

        var seenByAdmin = await database.SharedApi.AsUserAsync<IFleetDashboardService, IReadOnlyList<MachineCardDto>>(
            DashboardDriver.User(UserRole.SuperAdmin),
            service => service.GetMachinesAsync());

        seenByAdmin.Select(machine => machine.Id).ShouldContain(machineA);
        seenByAdmin.Select(machine => machine.Id).ShouldContain(machineB);
    }

    [Fact]
    public async Task A_station_user_gets_not_found_for_another_stations_machine()
    {
        var stationA = await database.CreateStationAsync();
        var stationB = await database.CreateStationAsync();
        var machineB = await MachineAsync(stationB);

        await Should.ThrowAsync<NotFoundException>(() => database.SharedApi.AsUserAsync<IFleetDashboardService, MachineDetailDto>(
            DashboardDriver.User(UserRole.StationUser, stationA),
            service => service.GetMachineAsync(machineB, DashboardRange.Last24Hours)));
    }

    [Fact]
    public async Task A_station_users_overview_counts_only_their_station()
    {
        var station = await database.CreateStationAsync();
        await MachineAsync(station, "soap", "vacuum");

        var overview = await database.SharedApi.AsUserAsync<IFleetDashboardService, FleetOverviewDto>(
            DashboardDriver.User(UserRole.StationUser, station),
            service => service.GetOverviewAsync(DashboardRange.Last24Hours));

        overview.MachineCount.ShouldBe(1);
        overview.UnassignedCount.ShouldBe(0);
        overview.Usage.Total.ShouldBe(2);
    }

    [Fact]
    public async Task The_catalog_lists_the_seeded_services_in_the_callers_language()
    {
        var services = await database.SharedApi.AsUserAsync<IFleetDashboardService, IReadOnlyList<ServiceInfoDto>>(
            DashboardDriver.User(UserRole.SuperAdmin),
            service => service.GetServicesAsync());

        services.Select(service => service.Code).ShouldContain("soap");
        services.Select(service => service.Code).ShouldContain("water");
        services.Select(service => service.Code).ShouldContain("vacuum");
    }
}
