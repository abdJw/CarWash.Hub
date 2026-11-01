using System.Net;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Application.Features.Catalog;
using CarWash.Hub.Application.Features.Dashboard;
using CarWash.Hub.Application.Features.Machines;
using CarWash.Hub.Application.Features.Stations;
using CarWash.Hub.Application.Features.Users;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.IntegrationTests.Infrastructure;
using CarWash.Hub.Web.Auth;
using Microsoft.EntityFrameworkCore;

namespace CarWash.Hub.IntegrationTests.Dashboard;

/// <summary>
/// The administration: only a Super Admin may use it, whatever page a station user finds; edits are
/// validated and refuse stale versions; assigning a machine is what makes it visible to a station;
/// and access changes and password resets end the sessions they should.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdministrationTests(DatabaseFixture database) : IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static AuthenticatedUser Admin { get; } = DashboardDriver.User(UserRole.SuperAdmin);

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(database.IsAvailable, database.SkipReason);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task<T> AsAdminAsync<TService, T>(Func<TService, Task<T>> call)
        where TService : notnull => database.SharedApi.AsUserAsync(Admin, call);

    private static string UniqueCode() => $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private async Task<Guid> NewMachineAsync()
    {
        using var client = database.SharedApi.CreateClient();
        var code = ApiRequests.NewMachineId();
        using var response = await client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(code, ProcessingDriver.ServiceEvent("soap", DateTimeOffset.UtcNow))));
        response.EnsureSuccessStatusCode();
        await database.SharedApi.ProcessAllPendingAsync();

        await using var context = database.CreateContext();
        return (await context.Machines.SingleAsync(m => m.Code == code, Token)).Id;
    }

    [Fact]
    public async Task A_station_user_is_refused_by_every_admin_service()
    {
        var stationUser = DashboardDriver.User(UserRole.StationUser, await database.CreateStationAsync());
        var factory = database.SharedApi;

        await Should.ThrowAsync<ForbiddenException>(() => factory.AsUserAsync<IGasStationService, IReadOnlyList<StationDto>>(stationUser, service => service.ListAsync(Token)));
        await Should.ThrowAsync<ForbiddenException>(() => factory.AsUserAsync<IUserService, IReadOnlyList<UserDto>>(stationUser, service => service.ListAsync(Token)));
        await Should.ThrowAsync<ForbiddenException>(() => factory.AsUserAsync<IServiceCatalogService, IReadOnlyList<ServiceDefinitionDto>>(stationUser, service => service.ListAsync(Token)));
        await Should.ThrowAsync<ForbiddenException>(() => factory.AsUserAsync<IMachineAdminService, IReadOnlyList<MachineSettingsDto>>(stationUser, service => service.ListUnassignedAsync(Token)));
    }

    [Fact]
    public async Task A_station_users_browser_cannot_open_the_admin_pages()
    {
        var station = await database.CreateStationAsync();
        var user = await database.CreateUserAsync(UserRole.StationUser, station);
        using var browser = database.SharedApi.Browser();
        await browser.SignInOrFailAsync(user.UserName, user.Password);

        using var response = await browser.GetAsync(new Uri("/admin/users", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain(DashboardAuthentication.AccessDeniedPath);
    }

    [Fact]
    public async Task A_station_is_created_and_its_code_normalised()
    {
        var code = UniqueCode().ToLowerInvariant();

        var saved = await AsAdminAsync<IGasStationService, StationDto>(service => service.SaveAsync(
            new SaveStationRequest { Code = code, Name = "Amman North", NameAr = "عمّان الشمالية", City = "Amman" }, Token));

        saved.Code.ShouldBe(code.ToUpperInvariant());
        saved.IsActive.ShouldBeTrue();
        saved.MachineCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_invalid_station_is_refused_field_by_field()
    {
        var exception = await Should.ThrowAsync<ValidationException>(() => AsAdminAsync<IGasStationService, StationDto>(
            service => service.SaveAsync(new SaveStationRequest { Code = "bad code!", Name = "", NameAr = "x" }, Token)));

        exception.Errors.Keys.ShouldBe([nameof(SaveStationRequest.Code), nameof(SaveStationRequest.Name)], ignoreOrder: true);
    }

    [Fact]
    public async Task A_duplicate_station_code_is_a_conflict()
    {
        var code = UniqueCode();
        await AsAdminAsync<IGasStationService, StationDto>(service => service.SaveAsync(new SaveStationRequest { Code = code, Name = "A", NameAr = "أ" }, Token));

        await Should.ThrowAsync<ConflictException>(() => AsAdminAsync<IGasStationService, StationDto>(
            service => service.SaveAsync(new SaveStationRequest { Code = code, Name = "B", NameAr = "ب" }, Token)));
    }

    [Fact]
    public async Task An_edit_of_a_station_someone_else_changed_is_a_conflict()
    {
        var created = await AsAdminAsync<IGasStationService, StationDto>(service => service.SaveAsync(
            new SaveStationRequest { Code = UniqueCode(), Name = "Before", NameAr = "قبل" }, Token));
        var edit = new SaveStationRequest { Id = created.Id, Code = created.Code, Name = "First edit", NameAr = "تعديل", RowVersion = created.RowVersion };

        await AsAdminAsync<IGasStationService, StationDto>(service => service.SaveAsync(edit, Token));

        await Should.ThrowAsync<ConflictException>(() => AsAdminAsync<IGasStationService, StationDto>(
            service => service.SaveAsync(edit with { Name = "Second edit from the old version" }, Token)));
    }

    [Fact]
    public async Task Assigning_a_new_machine_moves_it_from_the_inbox_to_its_station()
    {
        var machineId = await NewMachineAsync();
        var stationId = await database.CreateStationAsync();

        var inbox = await AsAdminAsync<IMachineAdminService, IReadOnlyList<MachineSettingsDto>>(service => service.ListUnassignedAsync(Token));
        var machine = inbox.Single(candidate => candidate.Id == machineId);

        await AsAdminAsync<IMachineAdminService, MachineSettingsDto>(service => service.UpdateAsync(
            new UpdateMachineRequest { Id = machineId, DisplayName = "Bay 1", GasStationId = stationId, IsActive = true, RowVersion = machine.RowVersion }, Token));

        (await AsAdminAsync<IMachineAdminService, IReadOnlyList<MachineSettingsDto>>(service => service.ListUnassignedAsync(Token)))
            .ShouldNotContain(candidate => candidate.Id == machineId);

        var seenByStation = await database.SharedApi.AsUserAsync<IFleetDashboardService, IReadOnlyList<MachineCardDto>>(
            DashboardDriver.User(UserRole.StationUser, stationId), service => service.GetMachinesAsync(Token));
        seenByStation.Single().Title.ShouldBe("Bay 1");
    }

    [Fact]
    public async Task A_machine_cannot_be_assigned_to_an_inactive_station()
    {
        var machineId = await NewMachineAsync();
        var inactive = await AsAdminAsync<IGasStationService, StationDto>(service => service.SaveAsync(
            new SaveStationRequest { Code = UniqueCode(), Name = "Closed", NameAr = "مغلقة", IsActive = false }, Token));
        var machine = await AsAdminAsync<IMachineAdminService, MachineSettingsDto>(service => service.GetAsync(machineId, Token));

        var exception = await Should.ThrowAsync<ValidationException>(() => AsAdminAsync<IMachineAdminService, MachineSettingsDto>(service => service.UpdateAsync(
            new UpdateMachineRequest { Id = machineId, GasStationId = inactive.Id, IsActive = true, RowVersion = machine.RowVersion }, Token)));

        exception.Errors.Keys.ShouldContain(nameof(UpdateMachineRequest.GasStationId));
    }

    [Fact]
    public async Task A_station_user_needs_a_station()
    {
        var exception = await Should.ThrowAsync<ValidationException>(() => AsAdminAsync<IUserService, UserDto>(service => service.SaveAsync(
            new SaveUserRequest { UserName = $"u{Guid.NewGuid():N}"[..20], DisplayName = "No Station", Email = "nostation@example.com", Role = UserRole.StationUser, Password = "longenough1" }, Token)));

        exception.Errors.Keys.ShouldContain(nameof(SaveUserRequest.GasStationId));
    }

    [Fact]
    public async Task A_new_user_signs_in_with_the_temporary_password_and_must_choose_their_own()
    {
        var station = await database.CreateStationAsync();
        var userName = $"u{Guid.NewGuid():N}"[..20];
        const string temporary = "temporary-pass-1";
        await AsAdminAsync<IUserService, UserDto>(service => service.SaveAsync(
            new SaveUserRequest { UserName = userName, DisplayName = "New Attendant", Email = $"{userName}@example.com", Role = UserRole.StationUser, GasStationId = station, Password = temporary }, Token));

        using var browser = database.SharedApi.Browser();
        using var signIn = await browser.SignInAsync(userName, temporary, "/machines");

        signIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        signIn.Headers.Location!.ToString().ShouldContain(DashboardAuthentication.ChangePasswordPath);

        // Choosing a new password re-issues the session without the flag.
        var token = await DashboardDriver.FormTokenAsync(browser, DashboardAuthentication.ChangePasswordPath);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "password",
            ["__RequestVerificationToken"] = token,
            ["Input.Current"] = temporary,
            ["Input.New"] = "my-own-password-2",
            ["Input.Confirm"] = "my-own-password-2",
        });
        using var change = await browser.PostAsync(new Uri(DashboardAuthentication.ChangePasswordPath, UriKind.Relative), form, Token);
        change.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var page = await browser.GetAsync(new Uri("/machines", UriKind.Relative), Token);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var context = database.CreateContext();
        (await context.Users.SingleAsync(u => u.UserName == userName, Token)).MustChangePassword.ShouldBeFalse();
    }

    [Fact]
    public async Task Resetting_a_password_ends_the_users_open_sessions()
    {
        var user = await database.CreateUserAsync(UserRole.SuperAdmin);
        using var browser = database.SharedApi.Browser();
        await browser.SignInOrFailAsync(user.UserName, user.Password);

        await AsAdminAsync<IUserService, bool>(async service =>
        {
            await service.ResetPasswordAsync(new ResetPasswordRequest(user.Id, "brand-new-temp-1"), Token);
            return true;
        });

        using var page = await browser.GetAsync(new Uri("/", UriKind.Relative), Token);
        page.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        page.Headers.Location!.ToString().ShouldContain(DashboardAuthentication.SignInPath);
    }

    [Fact]
    public async Task An_admin_cannot_deactivate_themselves()
    {
        var created = await database.CreateUserAsync(UserRole.SuperAdmin);
        var self = new AuthenticatedUser(created.Id, created.UserName, "Self", UserRole.SuperAdmin, null, Guid.NewGuid(), false);
        var me = (await database.SharedApi.AsUserAsync<IUserService, IReadOnlyList<UserDto>>(self, service => service.ListAsync(Token)))
            .Single(user => user.Id == created.Id);

        await Should.ThrowAsync<BusinessRuleException>(() => database.SharedApi.AsUserAsync<IUserService, UserDto>(self, service => service.SaveAsync(
            new SaveUserRequest
            {
                Id = me.Id, UserName = me.UserName, DisplayName = me.DisplayName, Email = me.Email,
                Role = UserRole.SuperAdmin, IsActive = false, RowVersion = me.RowVersion,
            },
            Token)));
    }

    [Fact]
    public async Task Reviewing_a_service_marks_it_configured_and_validates_its_threshold()
    {
        var soap = (await AsAdminAsync<IServiceCatalogService, IReadOnlyList<ServiceDefinitionDto>>(service => service.ListAsync(Token)))
            .Single(service => service.Code == "soap");

        await Should.ThrowAsync<ValidationException>(() => AsAdminAsync<IServiceCatalogService, ServiceDefinitionDto>(service => service.UpdateAsync(
            new UpdateServiceRequest { Id = soap.Id, Name = soap.Name, NameAr = soap.NameAr, LowThresholdPercent = 150, RowVersion = soap.RowVersion }, Token)));

        var saved = await AsAdminAsync<IServiceCatalogService, ServiceDefinitionDto>(service => service.UpdateAsync(
            new UpdateServiceRequest
            {
                Id = soap.Id, Name = soap.Name, NameAr = soap.NameAr, Icon = soap.Icon, TracksRefill = true, DefaultCapacity = 300,
                LowThresholdPercent = 25, SortOrder = soap.SortOrder, IsActive = true, RowVersion = soap.RowVersion,
            },
            Token));

        saved.DefaultCapacity.ShouldBe(300);
        saved.LowThresholdPercent.ShouldBe(25);
        saved.IsConfigured.ShouldBeTrue();
    }
}
