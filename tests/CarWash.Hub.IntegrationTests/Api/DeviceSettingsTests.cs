using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Application.Features.DeviceSettings;
using CarWash.Hub.Application.Features.Pricing;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.IntegrationTests.Api;

/// <summary>
/// <c>GET /api/v1/settings</c> and the revisions behind it, against the ESP32 integration guide: the
/// exact document and headers, a revision per change and never per poll, pending until the matching
/// acknowledgement, and the last complete snapshot kept when a station's values are incomplete.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DeviceSettingsTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string GuideExample =
        """{"revision":1,"command":"update_settings","services":{"water":{"price_qirsh":50,"durations_seconds":[180,180,210]},"soap":{"price_qirsh":50,"durations_seconds":[60,90,90]},"minute_water":{"price_qirsh":25,"duration_seconds":60},"vacuum":{"price_qirsh":50,"durations_seconds":[180,180,210]}}}""";

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
    public async Task An_unknown_machine_is_told_revision_zero_and_is_not_registered()
    {
        var mac = NewMac();

        var (body, response) = await PollAsync(mac);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"revision":0}""");

        await using var context = database.CreateContext();
        (await context.Machines.IgnoreQueryFilters().AnyAsync(machine => machine.Code == mac, Token)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_document_is_the_guide_example_with_the_headers_the_modem_needs()
    {
        var (_, mac) = await ConfiguredMachineAsync();
        await ReconcileAsync();

        using var request = ApiRequests.Settings(mac);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        using var response = await _client.SendAsync(request, Token);
        var bytes = await response.Content.ReadAsByteArrayAsync(Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Encoding.ASCII.GetString(bytes).ShouldBe(GuideExample);
        response.Content.Headers.ContentType!.ToString().ShouldBe("application/json");
        response.Content.Headers.ContentLength.ShouldBe(bytes.Length);
        response.Content.Headers.ContentEncoding.ShouldBeEmpty();
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.TransferEncodingChunked.ShouldNotBe(true);
    }

    [Fact]
    public async Task A_revision_is_issued_per_change_and_never_per_recompute()
    {
        var (station, mac) = await ConfiguredMachineAsync();
        await ReconcileAsync();
        await ReconcileAsync();
        (await RevisionAsync(mac)).ShouldBe(1);

        await PriceAsync(station, "soap", 0.75m, Today);
        await ReconcileAsync();
        (await RevisionAsync(mac)).ShouldBe(2);

        await DurationsAsync(station, "vacuum", 200, 200, 240);
        await ReconcileAsync();
        (await RevisionAsync(mac)).ShouldBe(3);

        // Saving the same values again is not a change.
        await DurationsAsync(station, "vacuum", 200, 200, 240);
        await PriceAsync(station, "soap", 0.75m, Today);
        await ReconcileAsync();
        (await RevisionAsync(mac)).ShouldBe(3);
        (await PollAsync(mac)).Body.ShouldContain("\"soap\":{\"price_qirsh\":75,");
    }

    [Fact]
    public async Task A_dated_price_reaches_the_machine_on_its_day()
    {
        var (station, mac) = await ConfiguredMachineAsync();
        await ReconcileAsync();
        await PriceAsync(station, "water", 0.6m, Today.AddDays(1));

        await ReconcileAsync();
        (await RevisionAsync(mac)).ShouldBe(1, "tomorrow's price is not today's");

        await ReconcileAsync(DateTimeOffset.UtcNow.AddDays(1));
        (await RevisionAsync(mac)).ShouldBe(2);
        (await PollAsync(mac)).Body.ShouldContain("\"water\":{\"price_qirsh\":60,");
    }

    [Fact]
    public async Task An_incomplete_station_keeps_sending_the_last_complete_snapshot()
    {
        var (station, mac) = await ConfiguredMachineAsync();
        await ReconcileAsync();

        var prices = await database.SharedApi.AsUserAsync<IStationPriceService, StationPricesDto>(Admin, service => service.GetAsync(station, Token));
        var soap = prices.Services.Single(service => service.Service == "soap").Current!;
        await database.SharedApi.AsUserAsync<IStationPriceService, bool>(Admin, async service =>
        {
            await service.DeleteAsync(soap.Id, Token);
            return true;
        });
        await ReconcileAsync();

        (await PollAsync(mac)).Body.ShouldBe(GuideExample);
        var row = await RowAsync(mac);
        row.Issue.ShouldBe(DeviceSettingsIssue.MissingPrice);
        row.IssueService.ShouldBe("soap");
        DeviceSettingsRules.StatusOf(row).ShouldBe(DeviceSettingsStatus.Blocked);
    }

    [Fact]
    public async Task A_station_without_run_times_has_no_settings_yet()
    {
        var station = await database.CreateStationAsync();
        await PriceAllAsync(station);
        var mac = await MachineAsync(station);

        await ReconcileAsync();

        (await PollAsync(mac)).Body.ShouldBe("""{"revision":0}""");
        (await RowAsync(mac)).Issue.ShouldBe(DeviceSettingsIssue.MissingDurations);
    }

    [Fact]
    public async Task Only_the_matching_acknowledgement_marks_the_revision_applied()
    {
        var (station, mac) = await ConfiguredMachineAsync();
        await ReconcileAsync();
        DeviceSettingsRules.StatusOf(await RowAsync(mac)).ShouldBe(DeviceSettingsStatus.Pending);

        await PostAsync(mac, ProcessingDriver.SettingsAck(mac, 1, "busy"));
        DeviceSettingsRules.StatusOf(await RowAsync(mac)).ShouldBe(DeviceSettingsStatus.Busy);

        await PostAsync(mac, ProcessingDriver.SettingsAck(mac, 1, "saved"));
        var applied = await RowAsync(mac);
        applied.AppliedRevision.ShouldBe(1);
        DeviceSettingsRules.StatusOf(applied).ShouldBe(DeviceSettingsStatus.Applied);

        // A new revision is pending again, and a late reply to the old one does not apply it.
        await PriceAsync(station, "vacuum", 0.55m, Today);
        await ReconcileAsync();
        await PostAsync(mac, ProcessingDriver.SettingsAck(mac, 1, "unchanged"));
        var pending = await RowAsync(mac);
        pending.DesiredRevision.ShouldBe(2);
        pending.AppliedRevision.ShouldBe(1);
        DeviceSettingsRules.StatusOf(pending).ShouldBe(DeviceSettingsStatus.Pending);

        // The snapshot survives application and a reboot: the machine asks again after starting.
        await PostAsync(mac, ProcessingDriver.SettingsAck(mac, 2, "saved"));
        await PostAsync(mac, ProcessingDriver.DeviceEvent(mac, "device_boot", ProcessingDriver.Json("'water_available':true")));
        (await RevisionAsync(mac)).ShouldBe(2);
        (await RowAsync(mac)).AppliedRevision.ShouldBe(2);
    }

    [Fact]
    public async Task A_revision_a_machine_reports_ahead_of_the_server_is_never_reused()
    {
        var (_, mac) = await ConfiguredMachineAsync();

        // The machine still runs revision 5 from before, say, a database restore.
        await PostAsync(mac, ProcessingDriver.SettingsAck(mac, 5, "unchanged"));
        await ReconcileAsync();

        (await RevisionAsync(mac)).ShouldBe(6);
    }

    [Fact]
    public async Task A_poll_without_a_machine_id_or_with_a_bad_key_is_refused()
    {
        using var missing = await _client.SendAndDisposeAsync(ApiRequests.Settings(null));
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var malformed = await _client.SendAndDisposeAsync(ApiRequests.Settings("AA/BB"));
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var wrongKey = await _client.SendAndDisposeAsync(ApiRequests.Settings(NewMac(), key: "not-the-key-0000000000000000000000"));
        wrongKey.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var readKey = await _client.SendAndDisposeAsync(ApiRequests.Settings(NewMac(), key: TestKeys.Read));
        readKey.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the dashboard's read key is not a machine key");
    }

    [Fact]
    public async Task The_machine_id_is_matched_ignoring_case()
    {
        var (_, mac) = await ConfiguredMachineAsync();
        await ReconcileAsync();

        (await PollAsync(mac.ToLowerInvariant())).Body.ShouldBe(GuideExample);
    }

    private static string NewMac() =>
        string.Join(':', Guid.NewGuid().ToByteArray().Take(6).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));

    private async Task<(string Body, HttpResponseMessage Response)> PollAsync(string mac)
    {
        var response = await _client.SendAndDisposeAsync(ApiRequests.Settings(mac));
        return (await response.Content.ReadAsStringAsync(Token), response);
    }

    private async Task<long> RevisionAsync(string mac)
    {
        var (body, _) = await PollAsync(mac);
        using var document = System.Text.Json.JsonDocument.Parse(body);
        return document.RootElement.GetProperty("revision").GetInt64();
    }

    private async Task<MachineDeviceSettings> RowAsync(string mac)
    {
        await using var context = database.CreateContext();
        var id = await context.Machines.IgnoreQueryFilters().Where(machine => machine.Code == mac).Select(machine => machine.Id).SingleAsync(Token);
        return await context.Set<MachineDeviceSettings>().SingleAsync(row => row.Id == id, Token);
    }

    private async Task ReconcileAsync(DateTimeOffset? at = null)
    {
        await using var scope = database.SharedApi.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDeviceSettingsReconciler>().ReconcileAsync(at ?? DateTimeOffset.UtcNow, Token);
    }

    private async Task PostAsync(string mac, string data)
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(mac, data)));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await database.SharedApi.ProcessAllPendingAsync();
    }

    // A machine at a station priced and timed exactly as the guide's example.
    private async Task<(Guid Station, string Mac)> ConfiguredMachineAsync()
    {
        var station = await database.CreateStationAsync();
        await PriceAllAsync(station);
        await DurationsAsync(station, "water", 180, 180, 210);
        await DurationsAsync(station, "soap", 60, 90, 90);
        await DurationsAsync(station, "minute_water", 60);
        await DurationsAsync(station, "vacuum", 180, 180, 210);
        return (station, await MachineAsync(station));
    }

    private async Task PriceAllAsync(Guid station)
    {
        await PriceAsync(station, "water", 0.5m, Today.AddDays(-1));
        await PriceAsync(station, "soap", 0.5m, Today.AddDays(-1));
        await PriceAsync(station, "minute_water", 0.25m, Today.AddDays(-1));
        await PriceAsync(station, "vacuum", 0.5m, Today.AddDays(-1));
    }

    private async Task<string> MachineAsync(Guid station)
    {
        var machine = new Machine { Id = Guid.CreateVersion7(), Code = NewMac(), GasStationId = station };
        await using var context = database.CreateContext();
        context.Machines.Add(machine);
        await context.SaveChangesAsync(Token);
        return machine.Code;
    }

    private Task<Guid> PriceAsync(Guid station, string service, decimal price, DateOnly from) =>
        database.SharedApi.AsUserAsync<IStationPriceService, Guid>(
            Admin,
            prices => prices.SetAsync(new SetStationPriceRequest { StationId = station, Service = service, UnitPrice = price, EffectiveFrom = from }, Token));

    private Task<bool> DurationsAsync(Guid station, string service, params int[] seconds) =>
        database.SharedApi.AsUserAsync<IStationDurationService, bool>(
            Admin,
            async durations =>
            {
                await durations.SetAsync(new SetStationDurationsRequest { StationId = station, Service = service, Seconds = seconds }, Token);
                return true;
            });
}
