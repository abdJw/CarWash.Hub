using System.Globalization;
using System.Net;
using System.Text;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Application.Features.Finance;
using CarWash.Hub.Application.Features.Pricing;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.IntegrationTests.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace CarWash.Hub.IntegrationTests.Dashboard;

/// <summary>
/// Prices and the finance report, from real processed messages: each use valued at its station's
/// price on its business day, revenue kept by the station that earned it, and station users kept to
/// their own station - on the page, the read API and the export alike.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class FinanceTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly TimeZoneInfo Amman = TimeZoneInfo.FindSystemTimeZoneById("Asia/Amman");
    private static readonly AuthenticatedUser Admin = DashboardDriver.User(UserRole.SuperAdmin);

    private HttpClient _client = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // Today's business date in Amman; the days used below are all in the past week, which the
    // processor accepts as device time.
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
    public async Task Revenue_is_each_use_times_its_stations_price()
    {
        var station = await database.CreateStationAsync();
        var machine = await MachineAsync(station);
        await PriceAsync(station, "soap", 0.5m, Today.AddDays(-5));
        await PriceAsync(station, "water", 0.25m, Today.AddDays(-5));
        await UsesAsync(machine.Code, ("soap", At(-3, 10)), ("soap", At(-3, 11)), ("water", At(-3, 12)));

        var report = await ReportAsync(Admin, Today.AddDays(-6), Today.AddDays(-1), stations: [station]);

        report.Totals.Uses.ShouldBe(3);
        report.Totals.Revenue.ShouldBe(1.25m);
        report.Totals.UnpricedUses.ShouldBe(0);
        report.Currency.ShouldBe("JOD");
        report.Services.Single(line => line.Key == "soap").Revenue.ShouldBe(1m);
        report.Services.Single(line => line.Key == "water").Revenue.ShouldBe(0.25m);
        report.Stations.Single().Key.ShouldBe(station.ToString());
        report.Stations.Single().Share.ShouldBe(1m);
        report.Machines.Single().Uses.ShouldBe(3);
        report.Periods.Count.ShouldBe(6, "one bucket per day of a short range");
        report.Periods.Single(period => period.Start == Today.AddDays(-3)).Revenue.ShouldBe(1.25m);
    }

    [Fact]
    public async Task A_price_change_values_each_day_at_its_own_price()
    {
        var station = await database.CreateStationAsync();
        var machine = await MachineAsync(station);
        await PriceAsync(station, "soap", 0.5m, Today.AddDays(-6));
        await PriceAsync(station, "soap", 1m, Today.AddDays(-2));
        await UsesAsync(machine.Code, ("soap", At(-4, 12)), ("soap", At(-1, 12)));

        var report = await ReportAsync(Admin, Today.AddDays(-6), Today.AddDays(-1), stations: [station], grain: FinanceGrain.Day);

        report.Totals.Revenue.ShouldBe(1.5m);
        report.Periods.Single(period => period.Start == Today.AddDays(-4)).Revenue.ShouldBe(0.5m);
        report.Periods.Single(period => period.Start == Today.AddDays(-1)).Revenue.ShouldBe(1m);
    }

    [Fact]
    public async Task Uses_with_no_price_are_unpriced_until_a_backdated_price_values_them()
    {
        var station = await database.CreateStationAsync();
        var machine = await MachineAsync(station);
        await UsesAsync(machine.Code, ("vacuum", At(-3, 9)), ("vacuum", At(-3, 10)));

        var before = await ReportAsync(Admin, Today.AddDays(-4), Today, stations: [station]);
        before.Totals.Revenue.ShouldBe(0m);
        before.Totals.UnpricedUses.ShouldBe(2);
        before.Services.Single().UnpricedUses.ShouldBe(2);

        await PriceAsync(station, "vacuum", 0.75m, Today.AddDays(-30));

        var after = await ReportAsync(Admin, Today.AddDays(-4), Today, stations: [station]);
        after.Totals.Revenue.ShouldBe(1.5m);
        after.Totals.UnpricedUses.ShouldBe(0);
    }

    [Fact]
    public async Task A_late_evening_use_counts_for_that_day_in_Amman()
    {
        var station = await database.CreateStationAsync();
        var machine = await MachineAsync(station);
        await UsesAsync(machine.Code, ("soap", At(-2, 23, 30)));

        (await ReportAsync(Admin, Today.AddDays(-2), Today.AddDays(-2), stations: [station])).Totals.Uses.ShouldBe(1);
        (await ReportAsync(Admin, Today.AddDays(-1), Today.AddDays(-1), stations: [station])).Totals.Uses.ShouldBe(0);
    }

    [Fact]
    public async Task Revenue_stays_with_the_station_where_the_machine_earned_it()
    {
        var stationA = await database.CreateStationAsync();
        var stationB = await database.CreateStationAsync();
        var machine = await MachineAsync(stationA);
        await PriceAsync(stationA, "soap", 0.5m, Today.AddDays(-6));
        await PriceAsync(stationB, "soap", 0.8m, Today.AddDays(-6));

        await UsesAsync(machine.Code, ("soap", At(-3, 12)));
        await MoveAsync(machine.Id, stationB);
        await UsesAsync(machine.Code, ("soap", At(-2, 12)));

        var atA = await ReportAsync(Admin, Today.AddDays(-6), Today, stations: [stationA]);
        var atB = await ReportAsync(Admin, Today.AddDays(-6), Today, stations: [stationB]);
        var both = await ReportAsync(Admin, Today.AddDays(-6), Today, stations: [stationA, stationB]);

        atA.Totals.Revenue.ShouldBe(0.5m);
        atB.Totals.Revenue.ShouldBe(0.8m);
        both.Totals.Revenue.ShouldBe(1.3m);
        both.Machines.Count.ShouldBe(2, "a machine that moved shows once per station it earned at");
        both.Stations.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_station_user_sees_only_their_own_station_whatever_they_ask_for()
    {
        var stationA = await database.CreateStationAsync();
        var stationB = await database.CreateStationAsync();
        var machineA = await MachineAsync(stationA);
        var machineB = await MachineAsync(stationB);
        await UsesAsync(machineA.Code, ("soap", At(-2, 12)));
        await UsesAsync(machineB.Code, ("soap", At(-2, 12)), ("soap", At(-2, 13)));

        var userA = DashboardDriver.User(UserRole.StationUser, stationA);
        var asked = await ReportAsync(userA, Today.AddDays(-3), Today, stations: [stationB]);
        var all = await ReportAsync(userA, Today.AddDays(-3), Today);

        asked.Totals.Uses.ShouldBe(1);
        asked.Stations.Single().Key.ShouldBe(stationA.ToString());
        all.Totals.Uses.ShouldBe(1);
        all.UnassignedUses.ShouldBe(0);

        var filters = await database.SharedApi.AsUserAsync<IFinanceReportService, FinanceFiltersDto>(userA, service => service.GetFiltersAsync(Token));
        filters.AllStations.ShouldBeFalse();
        filters.Stations.Select(station => station.Id).ShouldBe([stationA]);
        filters.Machines.Select(machine => machine.Id).ShouldBe([machineA.Id]);
    }

    [Fact]
    public async Task Unassigned_uses_are_counted_apart_for_a_whole_fleet_report()
    {
        var machine = await MachineAsync(null);
        await UsesAsync(machine.Code, ("soap", At(-2, 12)), ("water", At(-2, 13)));

        var report = await ReportAsync(Admin, Today.AddDays(-3), Today, machine: machine.Id);

        report.UnassignedUses.ShouldBe(2);
        report.Totals.Uses.ShouldBe(0, "uses on no station earn no station anything");
    }

    [Fact]
    public async Task A_deleted_machine_still_reports_what_it_earned()
    {
        var station = await database.CreateStationAsync();
        var machine = await MachineAsync(station);
        await PriceAsync(station, "soap", 0.5m, Today.AddDays(-6));
        await UsesAsync(machine.Code, ("soap", At(-2, 12)));

        await using (var context = database.CreateContext())
        {
            var row = await context.Machines.SingleAsync(m => m.Id == machine.Id, Token);
            context.Machines.Remove(row);
            await context.SaveChangesAsync(Token);
        }

        var report = await ReportAsync(Admin, Today.AddDays(-3), Today, stations: [station]);

        report.Totals.Revenue.ShouldBe(0.5m);
        report.Machines.Single().Name.ShouldBe(machine.Code, "a deleted machine keeps its name in the report");
    }

    [Fact]
    public async Task Service_and_machine_filters_narrow_the_report()
    {
        var station = await database.CreateStationAsync();
        var first = await MachineAsync(station);
        var second = await MachineAsync(station);
        await UsesAsync(first.Code, ("soap", At(-2, 12)), ("water", At(-2, 13)));
        await UsesAsync(second.Code, ("water", At(-2, 14)));

        var water = await ReportAsync(Admin, Today.AddDays(-3), Today, stations: [station], services: ["WATER"]);
        var firstOnly = await ReportAsync(Admin, Today.AddDays(-3), Today, stations: [station], machine: first.Id);

        water.Totals.Uses.ShouldBe(2);
        water.Services.Select(line => line.Key).ShouldBe(["water"]);
        firstOnly.Totals.Uses.ShouldBe(2);
        firstOnly.Machines.Single().Key.ShouldStartWith(first.Id.ToString());
    }

    [Fact]
    public async Task The_previous_period_is_as_long_as_the_elapsed_part_of_the_range()
    {
        var station = await database.CreateStationAsync();
        var machine = await MachineAsync(station);
        await PriceAsync(station, "soap", 1m, Today.AddDays(-6));
        await UsesAsync(machine.Code, ("soap", At(-4, 12)), ("soap", At(-1, 12)), ("soap", At(-1, 13)));

        var report = await ReportAsync(Admin, Today.AddDays(-2), Today.AddDays(-1), stations: [station]);

        report.PreviousFrom.ShouldBe(Today.AddDays(-4));
        report.PreviousTo.ShouldBe(Today.AddDays(-3));
        report.Previous.Revenue.ShouldBe(1m);
        report.Totals.Revenue.ShouldBe(2m);
        report.Totals.Days.ShouldBe(2);
        report.Totals.AveragePerDay.ShouldBe(1m);
    }

    [Fact]
    public async Task Only_a_super_admin_sets_or_removes_prices()
    {
        var station = await database.CreateStationAsync();
        var priceId = await PriceAsync(station, "soap", 0.5m, Today);
        var stationUser = DashboardDriver.User(UserRole.StationUser, station);

        await Should.ThrowAsync<ForbiddenException>(() => database.SharedApi.AsUserAsync<IStationPriceService, Guid>(
            stationUser,
            service => service.SetAsync(new SetStationPriceRequest { StationId = station, Service = "soap", UnitPrice = 0.1m, EffectiveFrom = Today }, Token)));

        await Should.ThrowAsync<ForbiddenException>(() => database.SharedApi.AsUserAsync<IStationPriceService, bool>(
            stationUser,
            async service =>
            {
                await service.DeleteAsync(priceId, Token);
                return true;
            }));

        // Reading their own station's prices is fine; another station's is not found.
        var own = await database.SharedApi.AsUserAsync<IStationPriceService, StationPricesDto>(stationUser, service => service.GetAsync(station, Token));
        own.Services.Single(prices => prices.Service == "soap").Current!.UnitPrice.ShouldBe(0.5m);

        var other = await database.CreateStationAsync();
        await Should.ThrowAsync<NotFoundException>(() => database.SharedApi.AsUserAsync<IStationPriceService, StationPricesDto>(
            stationUser, service => service.GetAsync(other, Token)));
    }

    [Fact]
    public async Task The_same_day_replaces_a_price_and_removing_one_brings_back_the_one_before()
    {
        var station = await database.CreateStationAsync();
        var machine = await MachineAsync(station);
        await PriceAsync(station, "soap", 0.5m, Today.AddDays(-6));
        await PriceAsync(station, "soap", 0.7m, Today.AddDays(-3));
        var replaced = await PriceAsync(station, "soap", 0.8m, Today.AddDays(-3));
        await UsesAsync(machine.Code, ("soap", At(-1, 12)));

        (await ReportAsync(Admin, Today.AddDays(-1), Today.AddDays(-1), stations: [station])).Totals.Revenue.ShouldBe(0.8m);

        var prices = await database.SharedApi.AsUserAsync<IStationPriceService, StationPricesDto>(Admin, service => service.GetAsync(station, Token));
        var soap = prices.Services.Single(service => service.Service == "soap");
        soap.History.Select(entry => entry.UnitPrice).ShouldBe([0.8m, 0.5m]);
        soap.Current!.Id.ShouldBe(replaced);

        await database.SharedApi.AsUserAsync<IStationPriceService, bool>(Admin, async service =>
        {
            await service.DeleteAsync(replaced, Token);
            return true;
        });

        (await ReportAsync(Admin, Today.AddDays(-1), Today.AddDays(-1), stations: [station])).Totals.Revenue.ShouldBe(0.5m);

        // The removed day can be priced again.
        await PriceAsync(station, "soap", 0.9m, Today.AddDays(-3));
        (await ReportAsync(Admin, Today.AddDays(-1), Today.AddDays(-1), stations: [station])).Totals.Revenue.ShouldBe(0.9m);
    }

    [Fact]
    public async Task A_scheduled_price_shows_as_next_and_today_as_current()
    {
        var station = await database.CreateStationAsync();
        await PriceAsync(station, "water", 0.25m, Today.AddDays(-1));
        await PriceAsync(station, "water", 0.3m, Today.AddDays(10));

        var water = (await database.SharedApi.AsUserAsync<IStationPriceService, StationPricesDto>(Admin, service => service.GetAsync(station, Token)))
            .Services.Single(service => service.Service == "water");

        water.Current!.UnitPrice.ShouldBe(0.25m);
        water.Next!.UnitPrice.ShouldBe(0.3m);

        var current = await database.SharedApi.AsUserAsync<IStationPriceService, IReadOnlyList<StationCurrentPricesDto>>(
            Admin, service => service.ListCurrentAsync(Token));
        current.Single(entry => entry.StationId == station).Prices.Single().UnitPrice.ShouldBe(0.25m);
    }

    [Fact]
    public async Task A_price_too_far_back_or_finer_than_a_fils_is_refused()
    {
        var station = await database.CreateStationAsync();

        var tooOld = await Should.ThrowAsync<ValidationException>(() => PriceAsync(station, "soap", 0.5m, Today.AddYears(-10)));
        tooOld.Errors.Keys.ShouldContain(nameof(SetStationPriceRequest.EffectiveFrom));

        var tooFine = await Should.ThrowAsync<ValidationException>(() => PriceAsync(station, "soap", 0.1234m, Today));
        tooFine.Errors.Keys.ShouldContain(nameof(SetStationPriceRequest.UnitPrice));

        await Should.ThrowAsync<NotFoundException>(() => PriceAsync(station, "no-such-service", 0.5m, Today));
    }

    [Fact]
    public async Task The_read_api_returns_the_report_and_refuses_a_backwards_range()
    {
        var station = await database.CreateStationAsync();
        var machine = await MachineAsync(station);
        await PriceAsync(station, "soap", 0.5m, Today.AddDays(-6));
        await UsesAsync(machine.Code, ("soap", At(-2, 12)), ("soap", At(-2, 13)));

        using var response = await _client.SendAndDisposeAsync(
            ApiRequests.Read($"/api/v1/reports/finance?from={Iso(Today.AddDays(-3))}&to={Iso(Today)}&stationIds={station}&grain=Week"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = (await response.JsonAsync()).GetProperty("data");
        data.GetProperty("totals").GetProperty("revenue").GetDecimal().ShouldBe(1m);
        data.GetProperty("grain").GetString().ShouldBe("Week");
        data.GetProperty("currency").GetString().ShouldBe("JOD");

        using var backwards = await _client.SendAndDisposeAsync(
            ApiRequests.Read($"/api/v1/reports/finance?from={Iso(Today)}&to={Iso(Today.AddDays(-3))}"));
        backwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var anonymous = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/reports/finance?from={Iso(Today)}&to={Iso(Today)}", key: null));
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_export_downloads_a_workbook_and_a_csv_of_the_users_own_station()
    {
        var stationA = await database.CreateStationAsync();
        var stationB = await database.CreateStationAsync();
        var machineA = await MachineAsync(stationA);
        var machineB = await MachineAsync(stationB);
        await PriceAsync(stationA, "soap", 0.5m, Today.AddDays(-6));
        await UsesAsync(machineA.Code, ("soap", At(-2, 12)), ("water", At(-2, 13)));
        await UsesAsync(machineB.Code, ("soap", At(-2, 12)));

        var user = await database.CreateUserAsync(UserRole.StationUser, stationA);
        using var browser = database.SharedApi.Browser();
        await browser.SignInOrFailAsync(user.UserName, user.Password);

        // Asking for station B still exports station A: the scope wins over the address.
        var query = $"from={Iso(Today.AddDays(-3))}&to={Iso(Today)}&stationIds={stationB}";

        using var xlsx = await browser.GetAsync(new Uri($"/reports/finance/export?format=xlsx&{query}", UriKind.Relative), Token);
        xlsx.StatusCode.ShouldBe(HttpStatusCode.OK);
        xlsx.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        xlsx.Content.Headers.ContentDisposition!.FileName!.ShouldContain($"finance_{Iso(Today.AddDays(-3))}_{Iso(Today)}.xlsx");

        await using (var stream = await xlsx.Content.ReadAsStreamAsync(Token))
        using (var workbook = new XLWorkbook(stream))
        {
            workbook.Worksheets.Count.ShouldBe(6);
            var detail = workbook.Worksheets.Last();
            detail.RowsUsed().Count().ShouldBe(3, "a header and one row per service used at station A");
            detail.Column(4).CellsUsed().Skip(1).Select(cell => cell.GetString()).ShouldAllBe(code => code == machineA.Code);
        }

        using var csv = await browser.GetAsync(new Uri($"/reports/finance/export?format=csv&{query}", UriKind.Relative), Token);
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await csv.Content.ReadAsByteArrayAsync(Token);
        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(3);
        lines.Skip(1).ShouldAllBe(line => line.Contains(machineA.Code, StringComparison.Ordinal));
        lines.ShouldContain(line => line.EndsWith(",1,0.500,0.500,0,0.000", StringComparison.Ordinal));

        using var anonymous = await database.SharedApi.Browser().GetAsync(new Uri($"/reports/finance/export?{query}", UriKind.Relative), Token);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task A_machine_that_reports_its_price_is_valued_at_it_and_its_cash_is_reported()
    {
        var station = await database.CreateStationAsync();
        var machine = await MachineAsync(station);
        await PriceAsync(station, "soap", 1m, Today.AddDays(-5));
        await UsesAsync(machine.Code, ("soap", At(-1, 12)));
        await PaymentsAsync(machine.Code, ("soap", 50, 100), ("soap", 50, 50));

        var report = await ReportAsync(Admin, Today.AddDays(-2), Today, stations: [station]);

        report.Totals.Uses.ShouldBe(3);
        report.Totals.Revenue.ShouldBe(2m, "the earlier firmware's use at the list price, the two sales at 0.50 each");
        report.Totals.CashCollected.ShouldBe(1.5m);
        report.Totals.UnpricedUses.ShouldBe(0);
        report.Stations.Single().CashCollected.ShouldBe(1.5m);
        report.Periods.Single(period => period.Start == Today).CashCollected.ShouldBe(1.5m);

        // A later list price revalues only the use the machine did not price.
        await PriceAsync(station, "soap", 2m, Today.AddDays(-5));
        var revalued = await ReportAsync(Admin, Today.AddDays(-2), Today, stations: [station], detail: true);
        revalued.Totals.Revenue.ShouldBe(3m);
        var today = revalued.Detail!.Single(row => row.Date == Today);
        today.DevicePricedUses.ShouldBe(2);
        today.Revenue.ShouldBe(1m);
        today.CashCollected.ShouldBe(1.5m);
    }

    [Fact]
    public async Task A_use_the_machine_priced_is_never_unpriced()
    {
        var station = await database.CreateStationAsync();
        var machine = await MachineAsync(station);
        await PaymentsAsync(machine.Code, ("minute_water", 25, 25));

        var report = await ReportAsync(Admin, Today, Today, stations: [station]);

        report.Totals.Revenue.ShouldBe(0.25m);
        report.Totals.UnpricedUses.ShouldBe(0);
    }

    private async Task PaymentsAsync(string code, params (string Service, int PriceQirsh, int PaidQirsh)[] payments)
    {
        foreach (var (service, price, paid) in payments)
        {
            using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(code, ProcessingDriver.Payment(code, service, price, paid))));
            response.EnsureSuccessStatusCode();
        }

        await database.SharedApi.ProcessAllPendingAsync();
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // A time on a business day, counted from today, in Amman.
    private static DateTimeOffset At(int daysFromToday, int hour, int minute = 0)
    {
        var local = Today.AddDays(daysFromToday).ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Amman.GetUtcOffset(local));
    }

    // A machine standing at a station before it reports, so its uses are stamped with that station.
    private async Task<(Guid Id, string Code)> MachineAsync(Guid? stationId)
    {
        var machine = new Machine { Id = Guid.CreateVersion7(), Code = ApiRequests.NewMachineId(), GasStationId = stationId };
        await using var context = database.CreateContext();
        context.Machines.Add(machine);
        await context.SaveChangesAsync(Token);
        return (machine.Id, machine.Code);
    }

    private async Task MoveAsync(Guid machineId, Guid stationId)
    {
        await using var context = database.CreateContext();
        var machine = await context.Machines.SingleAsync(m => m.Id == machineId, Token);
        machine.GasStationId = stationId;
        await context.SaveChangesAsync(Token);
    }

    private async Task UsesAsync(string code, params (string Service, DateTimeOffset At)[] uses)
    {
        foreach (var (service, at) in uses)
        {
            using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(code, ProcessingDriver.ServiceEvent(service, at))));
            response.EnsureSuccessStatusCode();
        }

        await database.SharedApi.ProcessAllPendingAsync();
    }

    private Task<Guid> PriceAsync(Guid stationId, string service, decimal price, DateOnly from) =>
        database.SharedApi.AsUserAsync<IStationPriceService, Guid>(
            Admin,
            prices => prices.SetAsync(new SetStationPriceRequest { StationId = stationId, Service = service, UnitPrice = price, EffectiveFrom = from }, Token));

    private Task<FinanceReportDto> ReportAsync(
        AuthenticatedUser user,
        DateOnly from,
        DateOnly to,
        Guid[]? stations = null,
        string[]? services = null,
        Guid? machine = null,
        FinanceGrain grain = FinanceGrain.Auto,
        bool detail = false) =>
        database.SharedApi.AsUserAsync<IFinanceReportService, FinanceReportDto>(
            user,
            finance => finance.GetReportAsync(
                new FinanceReportQuery
                {
                    From = from, To = to, StationIds = stations, Services = services, MachineId = machine, Grain = grain, IncludeDetail = detail,
                },
                Token));
}
