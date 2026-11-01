using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.Infrastructure.Auth;
using CarWash.Hub.Web.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.IntegrationTests.Infrastructure;

/// <summary>Signs in to the dashboard the way a browser does, and creates the users and stations tests need.</summary>
public static partial class DashboardDriver
{
    private static readonly PasswordHasherAdapter Hasher = new();

    /// <summary>A browser: keeps cookies, does not follow redirects, so a test sees each one.</summary>
    /// <param name="factory">The host.</param>
    /// <returns>The client.</returns>
    public static HttpClient Browser(this ApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>Creates a gas station with a unique code.</summary>
    /// <param name="database">The test database.</param>
    /// <returns>The station's key.</returns>
    public static async Task<Guid> CreateStationAsync(this DatabaseFixture database)
    {
        var id = Guid.CreateVersion7();
        await using var context = database.CreateContext();
        context.GasStations.Add(new GasStation
        {
            Id = id,
            Code = $"IT-{Guid.NewGuid():N}"[..20],
            Name = "Integration station",
            NameAr = "محطة الاختبار",
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    /// <summary>Creates a dashboard user with a unique name and a known password.</summary>
    /// <param name="database">The test database.</param>
    /// <param name="role">The role.</param>
    /// <param name="stationId">The station, for a station user.</param>
    /// <returns>The user's key, name and password.</returns>
    public static async Task<(Guid Id, string UserName, string Password)> CreateUserAsync(
        this DatabaseFixture database,
        UserRole role = UserRole.SuperAdmin,
        Guid? stationId = null)
    {
        var id = Guid.CreateVersion7();
        var userName = $"it-{Guid.NewGuid():N}"[..24];
        var password = $"pw-{Guid.NewGuid():N}";

        await using var context = database.CreateContext();
        context.Users.Add(new User
        {
            Id = id,
            UserName = userName,
            Email = $"{userName}@example.com",
            DisplayName = "Integration User",
            PasswordHash = Hasher.Hash(password),
            Role = role,
            GasStationId = stationId,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (id, userName, password);
    }

    /// <summary>Posts the sign-in form, antiforgery token included.</summary>
    /// <param name="browser">The browser.</param>
    /// <param name="userName">The user name.</param>
    /// <param name="password">The password.</param>
    /// <param name="returnUrl">Where to go afterwards.</param>
    /// <returns>The form's response: a redirect on success, the page again on failure.</returns>
    public static async Task<HttpResponseMessage> SignInAsync(this HttpClient browser, string userName, string password, string returnUrl = "/")
    {
        var token = await FormTokenAsync(browser, DashboardAuthentication.SignInPath);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = token,
            ["Input.UserName"] = userName,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "true",
        });

        return await browser.PostAsync(
            new Uri($"{DashboardAuthentication.SignInPath}?returnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative),
            form,
            TestContext.Current.CancellationToken);
    }

    /// <summary>Signs in and fails the test unless it worked.</summary>
    /// <param name="browser">The browser.</param>
    /// <param name="userName">The user name.</param>
    /// <param name="password">The password.</param>
    /// <returns>A task that completes when signed in.</returns>
    public static async Task SignInOrFailAsync(this HttpClient browser, string userName, string password)
    {
        using var response = await browser.SignInAsync(userName, password);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, "the sign-in form should redirect on success");
    }

    /// <summary>Reads a page's antiforgery token, as its form would post it.</summary>
    /// <param name="browser">The browser.</param>
    /// <param name="path">The page.</param>
    /// <returns>The token.</returns>
    public static async Task<string> FormTokenAsync(HttpClient browser, string path)
    {
        var page = await browser.GetStringAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
        var match = AntiforgeryToken().Match(page);
        match.Success.ShouldBeTrue($"{path} should render an antiforgery token");
        return WebUtility.HtmlDecode(match.Groups["token"].Value);
    }

    /// <summary>Resolves a service in a scope whose caller is the given dashboard user - the dashboard's own path.</summary>
    /// <typeparam name="TService">The service.</typeparam>
    /// <param name="factory">The host.</param>
    /// <param name="user">The caller.</param>
    /// <param name="call">What to do with the service.</param>
    /// <typeparam name="T">The result.</typeparam>
    /// <returns>The result.</returns>
    public static async Task<T> AsUserAsync<TService, T>(this ApiFactory factory, AuthenticatedUser user, Func<TService, Task<T>> call)
        where TService : notnull
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentPrincipalHolder>()
            .Set(DashboardAuthentication.CreatePrincipal(user), null, null, null);
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <summary>A dashboard user, as a session carries them.</summary>
    /// <param name="role">The role.</param>
    /// <param name="stationId">The station, for a station user.</param>
    /// <returns>The user.</returns>
    public static AuthenticatedUser User(UserRole role, Guid? stationId = null) =>
        new(Guid.NewGuid(), "it-user", "Integration User", role, stationId, Guid.NewGuid(), MustChangePassword: false);

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
