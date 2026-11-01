using System.Security.Claims;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Domain.Constants;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.Infrastructure.Auth;
using CarWash.Hub.Infrastructure.Services;
using CarWash.Hub.Web.Auth;
using Microsoft.AspNetCore.Http;

namespace CarWash.Hub.UnitTests.Auth;

/// <summary>
/// Who the caller is, and which machines they see: a station user only their station's, failing
/// closed; everyone else all. A dashboard operation's user wins over the HTTP request's.
/// </summary>
public class CallerScopeTests
{
    private static readonly Guid StationId = Guid.NewGuid();

    private static ClaimsPrincipal Principal(UserRole role, Guid? stationId) =>
        DashboardAuthentication.CreatePrincipal(new AuthenticatedUser(
            Guid.NewGuid(), "user", "User", role, stationId, Guid.NewGuid(), MustChangePassword: false));

    private static CurrentUserService CurrentUser(ClaimsPrincipal? requestUser = null, ClaimsPrincipal? operationUser = null)
    {
        var accessor = new HttpContextAccessor();
        if (requestUser is not null)
        {
            accessor.HttpContext = new DefaultHttpContext { User = requestUser };
        }

        var holder = new CurrentPrincipalHolder();
        if (operationUser is not null)
        {
            holder.Set(operationUser, "10.0.0.9", "Edge", "op-1");
        }

        return new CurrentUserService(accessor, holder);
    }

    [Fact]
    public void A_station_user_sees_only_their_station()
    {
        var scope = new UserMachineAccessScopeProvider(CurrentUser(Principal(UserRole.StationUser, StationId))).GetScope();

        scope.ShouldBe(MachineAccessScope.Station(StationId));
    }

    [Fact]
    public void A_station_user_without_a_station_sees_nothing()
    {
        var scope = new UserMachineAccessScopeProvider(CurrentUser(Principal(UserRole.StationUser, null))).GetScope();

        scope.IsRestricted.ShouldBeTrue();
        scope.GasStationId.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void A_super_admin_sees_every_station() =>
        new UserMachineAccessScopeProvider(CurrentUser(Principal(UserRole.SuperAdmin, null))).GetScope().ShouldBe(MachineAccessScope.AllStations);

    [Fact]
    public void A_client_key_or_no_caller_sees_every_station()
    {
        var keyHolder = new ClaimsPrincipal(new ClaimsIdentity([new Claim(CustomClaimTypes.KeyId, "r1"), new Claim(CustomClaimTypes.Scope, "read")], "ClientReadKey"));

        new UserMachineAccessScopeProvider(CurrentUser(keyHolder)).GetScope().ShouldBe(MachineAccessScope.AllStations);
        new UserMachineAccessScopeProvider(CurrentUser()).GetScope().ShouldBe(MachineAccessScope.AllStations);
    }

    [Fact]
    public void The_dashboard_operation_user_wins_over_the_request_user()
    {
        var operationUser = Principal(UserRole.StationUser, StationId);
        var current = CurrentUser(requestUser: Principal(UserRole.SuperAdmin, null), operationUser: operationUser);

        current.Role.ShouldBe(UserRole.StationUser);
        current.GasStationId.ShouldBe(StationId);
        current.UserId.ShouldBe(Guid.Parse(operationUser.FindFirstValue(CustomClaimTypes.UserId)!));
        current.IpAddress.ShouldBe("10.0.0.9");
        current.CorrelationId.ShouldBe("op-1");
    }

    [Fact]
    public void A_client_key_caller_has_no_user_or_role()
    {
        var keyHolder = new ClaimsPrincipal(new ClaimsIdentity([new Claim(CustomClaimTypes.KeyId, "k1")], "ClientSecretKey"));
        var current = CurrentUser(keyHolder);

        current.UserId.ShouldBeNull();
        current.Role.ShouldBeNull();
        current.ClientKeyId.ShouldBe("k1");
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/machines", "/machines")]
    [InlineData("/machines/1?x=2", "/machines/1?x=2")]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("/account/logout", "/")]
    public void Only_local_dashboard_paths_are_returned_to(string? requested, string expected) =>
        DashboardAuthentication.SafeReturnUrl(requested).ShouldBe(expected);
}
