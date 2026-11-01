using System.Net;
using CarWash.Hub.IntegrationTests.Infrastructure;
using CarWash.Hub.Web.Auth;
using Microsoft.EntityFrameworkCore;

namespace CarWash.Hub.IntegrationTests.Dashboard;

/// <summary>
/// The dashboard shares the host with the machine API without weakening it: pages need a signed-in
/// user, the API still needs its keys, a cookie never opens the API, and each kind of response
/// carries its own security policy.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DashboardHostTests(DatabaseFixture database) : IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HttpClient _browser = null!;

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(database.IsAvailable, database.SkipReason);
        _browser = database.SharedApi.Browser();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _browser?.Dispose();
        return ValueTask.CompletedTask;
    }

    private Task<HttpResponseMessage> GetAsync(string path) => _browser.GetAsync(new Uri(path, UriKind.Relative), Token);

    [Theory]
    [InlineData("/", "%2F")]
    [InlineData("/machines", "%2Fmachines")]
    public async Task An_anonymous_page_request_is_sent_to_sign_in(string path, string returnUrl)
    {
        using var response = await GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldEndWith($"{DashboardAuthentication.SignInPath}?returnUrl={returnUrl}");
    }

    [Fact]
    public async Task An_anonymous_blazor_connection_is_refused()
    {
        using var response = await _browser.PostAsync(new Uri("/_blazor/negotiate?negotiateVersion=1", UriKind.Relative), null, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_content/MudBlazor/MudBlazor.min.css")]
    [InlineData("/_content/CarWash.Hub.Web/css/dashboard.css")]
    [InlineData("/_content/CarWash.Hub.Web/js/dashboard.js")]
    [InlineData("/favicon.svg")]
    public async Task Static_assets_are_served_anonymously(string path)
    {
        using var response = await GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{path} must load before anyone has signed in, or the sign-in page breaks");
    }

    [Fact]
    public async Task The_sign_in_page_opens_in_Arabic_with_the_dashboard_policy_and_no_caching()
    {
        using var response = await GetAsync(DashboardAuthentication.SignInPath);
        var html = await response.Content.ReadAsStringAsync(Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentLanguage.ShouldContain("ar-SA");
        html.ShouldContain("<html lang=\"ar-SA\" dir=\"rtl\">");
        html.ShouldContain("تسجيل الدخول", customMessage: "Arabic is written as text, not as &#x...; entities");

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.ShouldContain("script-src 'self';");
        policy.ShouldContain("frame-ancestors 'none'");
        policy.ShouldNotContain("'unsafe-eval'");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task The_api_keeps_its_english_default_and_its_strict_policy()
    {
        using var response = await _browser.SendAndDisposeAsync(ApiRequests.Read("/api/v1/machines"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentLanguage.ShouldContain("en-US");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldBe("default-src 'none'; frame-ancestors 'none'");
    }

    [Fact]
    public async Task Valid_credentials_sign_in_and_open_the_dashboard_and_its_connection()
    {
        var user = await database.CreateUserAsync();

        using var signIn = await _browser.SignInAsync(user.UserName, user.Password, "/machines");
        signIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        signIn.Headers.Location!.ToString().ShouldEndWith("/machines");

        using var page = await GetAsync("/machines");
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");

        using var negotiate = await _browser.PostAsync(new Uri("/_blazor/negotiate?negotiateVersion=1", UriKind.Relative), null, Token);
        negotiate.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_return_url_off_the_site_is_ignored()
    {
        var user = await database.CreateUserAsync();

        using var signIn = await _browser.SignInAsync(user.UserName, user.Password, "https://evil.example/");

        signIn.Headers.Location!.ToString().ShouldNotContain("evil");
    }

    [Fact]
    public async Task A_wrong_password_shows_an_error_and_signs_nobody_in()
    {
        var user = await database.CreateUserAsync();

        using var response = await _browser.SignInAsync(user.UserName, "wrong");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("role=\"alert\"");
        using var page = await GetAsync("/");
        page.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account_even_against_the_right_one()
    {
        var user = await database.CreateUserAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await _browser.SignInAsync(user.UserName, "wrong");
        }

        using var right = await _browser.SignInAsync(user.UserName, user.Password);
        right.StatusCode.ShouldBe(HttpStatusCode.OK, "a locked account stays on the sign-in page");

        await using var context = database.CreateContext();
        (await context.Users.SingleAsync(u => u.Id == user.Id, Token)).LockoutEndAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_dashboard_cookie_alone_cannot_call_the_api()
    {
        var user = await database.CreateUserAsync();
        await _browser.SignInOrFailAsync(user.UserName, user.Password);

        using var response = await GetAsync("/api/v1/machines");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var user = await database.CreateUserAsync();
        await _browser.SignInOrFailAsync(user.UserName, user.Password);

        var token = await DashboardDriver.FormTokenAsync(_browser, DashboardAuthentication.SignOutPath);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "logout",
            ["__RequestVerificationToken"] = token,
        });
        using var signOut = await _browser.PostAsync(new Uri(DashboardAuthentication.SignOutPath, UriKind.Relative), form, Token);
        signOut.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var page = await GetAsync("/");
        page.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task A_deactivated_user_loses_their_open_session()
    {
        var user = await database.CreateUserAsync();
        await _browser.SignInOrFailAsync(user.UserName, user.Password);

        await using (var context = database.CreateContext())
        {
            var stored = await context.Users.SingleAsync(u => u.Id == user.Id, Token);
            stored.IsActive = false;
            await context.SaveChangesAsync(Token);
        }

        using var page = await GetAsync("/");

        page.StatusCode.ShouldBe(HttpStatusCode.Redirect, "the session is re-checked against the stored user");
    }

    [Fact]
    public async Task A_form_post_without_the_antiforgery_token_is_refused()
    {
        var user = await database.CreateUserAsync();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["Input.UserName"] = user.UserName,
            ["Input.Password"] = user.Password,
        });

        using var response = await _browser.PostAsync(new Uri(DashboardAuthentication.SignInPath, UriKind.Relative), form, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_language_choice_is_remembered()
    {
        using var choose = await GetAsync($"{AccountEndpoints.CulturePath}?culture=en-US&returnUrl=%2Faccount%2Flogin");
        choose.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        choose.Headers.Location!.ToString().ShouldEndWith(DashboardAuthentication.SignInPath);

        using var page = await GetAsync(DashboardAuthentication.SignInPath);

        page.Content.Headers.ContentLanguage.ShouldContain("en-US");
    }

    [Fact]
    public async Task Sign_in_keys_are_kept_in_the_database_so_a_recycle_signs_nobody_out()
    {
        var user = await database.CreateUserAsync();
        await _browser.SignInOrFailAsync(user.UserName, user.Password);

        await using var context = database.CreateContext();
        var keys = await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM data_protection_keys").SingleAsync(Token);

        keys.ShouldBeGreaterThan(0);
    }
}
