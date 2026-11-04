using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Application.Features.DeviceSettings;
using CarWash.Hub.Application.Features.Pricing;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.IntegrationTests.Infrastructure;

namespace CarWash.Hub.IntegrationTests.Dashboard;

/// <summary>
/// A station's run times, which the machines are sent with its prices: suggested until saved, set by
/// a Super Admin only, and refused outright when a machine would refuse them.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class StationDurationTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly AuthenticatedUser Admin = DashboardDriver.User(UserRole.SuperAdmin);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(database.IsAvailable, database.SkipReason);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task<StationDurationsDto> GetAsync(AuthenticatedUser user, Guid stationId) =>
        database.SharedApi.AsUserAsync<IStationDurationService, StationDurationsDto>(user, service => service.GetAsync(stationId, Token));

    private Task<bool> SetAsync(AuthenticatedUser user, Guid stationId, string service, params int[] seconds) =>
        database.SharedApi.AsUserAsync<IStationDurationService, bool>(
            user,
            async durations =>
            {
                await durations.SetAsync(new SetStationDurationsRequest { StationId = stationId, Service = service, Seconds = seconds }, Token);
                return true;
            });

    [Fact]
    public async Task Unset_run_times_are_the_guide_suggestions_flagged_as_such()
    {
        var station = await database.CreateStationAsync();

        var durations = await GetAsync(Admin, station);

        durations.Services.Select(service => service.Service).ShouldBe(["water", "soap", "minute_water", "vacuum"]);
        durations.Services.ShouldAllBe(service => service.IsSuggested && service.SetAt == null);
        durations.Services.Single(service => service.Service == "soap").Seconds.ShouldBe([60, 90, 90]);
        durations.Services.Single(service => service.Service == "minute_water").Seconds.ShouldBe([60]);
    }

    [Fact]
    public async Task A_super_admin_sets_them_and_saving_again_edits_the_same_row()
    {
        var station = await database.CreateStationAsync();

        await SetAsync(Admin, station, "water", 120, 150, 200);
        await SetAsync(Admin, station, "water", 121, 151, 201);
        await SetAsync(Admin, station, "minute_water", 45);

        var durations = await GetAsync(Admin, station);
        var water = durations.Services.Single(service => service.Service == "water");
        water.IsSuggested.ShouldBeFalse();
        water.Seconds.ShouldBe([121, 151, 201]);
        durations.Services.Single(service => service.Service == "minute_water").Seconds.ShouldBe([45]);

        await using var context = database.CreateContext();
        context.Set<Domain.Entities.StationServiceDuration>().Count(row => row.GasStationId == station && row.ServiceCode == "water").ShouldBe(1);
    }

    [Fact]
    public async Task A_station_user_reads_their_own_but_cannot_set_them()
    {
        var station = await database.CreateStationAsync();
        var user = DashboardDriver.User(UserRole.StationUser, station);

        (await GetAsync(user, station)).Services.Count.ShouldBe(4);
        await Should.ThrowAsync<ForbiddenException>(() => SetAsync(user, station, "soap", 60, 90, 90));
        var other = await database.CreateStationAsync();
        await Should.ThrowAsync<NotFoundException>(() => GetAsync(user, other));
    }

    [Fact]
    public async Task Run_times_a_machine_would_refuse_are_refused()
    {
        var station = await database.CreateStationAsync();

        await Should.ThrowAsync<ValidationException>(() => SetAsync(Admin, station, "soap", 60, 6000, 90));
        await Should.ThrowAsync<ValidationException>(() => SetAsync(Admin, station, "minute_water", 60, 60, 60));
        await Should.ThrowAsync<ValidationException>(() => SetAsync(Admin, station, "wax", 60, 60, 60));
    }

    [Fact]
    public async Task A_machine_service_price_must_be_one_the_machine_accepts()
    {
        var station = await database.CreateStationAsync();

        await Should.ThrowAsync<ValidationException>(() => database.SharedApi.AsUserAsync<IStationPriceService, Guid>(
            Admin,
            prices => prices.SetAsync(
                new SetStationPriceRequest { StationId = station, Service = "vacuum", UnitPrice = 0.125m, EffectiveFrom = new DateOnly(2026, 11, 1) },
                Token)));
    }
}
