using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Domain.Constants;
using CarWash.Hub.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.Web.Auth;

/// <summary>The dashboard's sign-in scheme and the names other code refers to.</summary>
/// <remarks>Layer: Web.</remarks>
public static class DashboardAuthentication
{
    /// <summary>The cookie authentication scheme; the host's default scheme.</summary>
    public const string Scheme = "DashboardCookie";

    /// <summary>The rate-limit policy on the sign-in page.</summary>
    public const string SignInRateLimitPolicy = "dashboard-sign-in";

    /// <summary>The sign-in page.</summary>
    public const string SignInPath = "/account/login";

    /// <summary>The sign-out page.</summary>
    public const string SignOutPath = "/account/logout";

    /// <summary>The page shown when a signed-in user may not open a page.</summary>
    public const string AccessDeniedPath = "/account/access-denied";

    /// <summary>The page where a user changes their own password.</summary>
    public const string ChangePasswordPath = "/account/password";

    private const string ValidatedAtItem = ".carwash.validated-at";

    /// <summary>Builds the principal a session cookie carries.</summary>
    /// <param name="user">The signed-in user.</param>
    /// <returns>The principal.</returns>
    public static ClaimsPrincipal CreatePrincipal(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var claims = new List<Claim>
        {
            new(CustomClaimTypes.UserId, user.Id.ToString()),
            new(CustomClaimTypes.UserName, user.UserName),
            new(CustomClaimTypes.DisplayName, user.DisplayName),
            new(CustomClaimTypes.Role, user.Role.ToString()),
            new(CustomClaimTypes.SecurityStamp, user.SecurityStamp.ToString()),
        };

        if (user.GasStationId is { } stationId)
        {
            claims.Add(new Claim(CustomClaimTypes.GasStationId, stationId.ToString()));
        }

        if (user.MustChangePassword)
        {
            claims.Add(new Claim(CustomClaimTypes.MustChangePassword, "true"));
        }

        // The short claim names are declared as the name and role types, so IsInRole and
        // Identity.Name work against them.
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, CustomClaimTypes.UserName, CustomClaimTypes.Role));
    }

    /// <summary>Returns a return URL that stays on this site, or the overview.</summary>
    /// <param name="returnUrl">The requested URL.</param>
    /// <returns>A local path, never one back into the account pages.</returns>
    public static string SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || !returnUrl.StartsWith('/')
            || returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            || returnUrl.StartsWith("/account/", StringComparison.OrdinalIgnoreCase))
        {
            return "/";
        }

        return returnUrl;
    }

    /// <summary>Configures the cookie handler.</summary>
    /// <param name="options">The handler options.</param>
    /// <param name="settings">Lifetimes and revalidation.</param>
    /// <param name="isDevelopment">Whether plain-HTTP localhost must work.</param>
    internal static void ConfigureCookie(CookieAuthenticationOptions options, DashboardCookieOptions settings, bool isDevelopment)
    {
        // __Host- binds the cookie to this exact origin over HTTPS, so no sub-domain can set or read
        // it. It requires Secure, which a developer's http://localhost cannot satisfy.
        options.Cookie.Name = isDevelopment ? "CarWashHub.Session" : "__Host-CarWashHub";
        options.Cookie.HttpOnly = true;
        options.Cookie.Path = "/";
        options.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

        // Lax, not Strict: a link from an alert e-mail, a push notification or a QR sticker is a
        // cross-site navigation, and Strict would arrive signed out.
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.LoginPath = SignInPath;
        options.LogoutPath = SignOutPath;
        options.AccessDeniedPath = AccessDeniedPath;
        options.ReturnUrlParameter = "returnUrl";
        options.ExpireTimeSpan = TimeSpan.FromHours(settings.ExpireHours);
        options.SlidingExpiration = true;

        options.Events.OnRedirectToLogin = context => RedirectOrStatus(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => RedirectOrStatus(context, StatusCodes.Status403Forbidden);
        options.Events.OnValidatePrincipal = context => ValidatePrincipalAsync(context, TimeSpan.FromMinutes(settings.RevalidationMinutes));
    }

    // A browser navigating to a page is sent to the sign-in page; anything else - the Blazor
    // connection, a form post - gets the status code, which it can act on, instead of a 302 to HTML.
    private static Task RedirectOrStatus(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        var request = context.Request;
        if (HttpMethods.IsGet(request.Method) && !request.Path.StartsWithSegments("/_blazor"))
        {
            context.Response.Redirect(context.RedirectUri);
        }
        else
        {
            context.Response.StatusCode = statusCode;
        }

        return Task.CompletedTask;
    }

    // At most every few minutes, the session is checked against the user as stored: deactivated,
    // deleted, locked out or with a changed security stamp (a password reset) ends it. A change of
    // name, role or station is picked up into the cookie.
    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context, TimeSpan interval)
    {
        var properties = context.Properties;
        var now = DateTimeOffset.UtcNow;

        if (properties.Items.TryGetValue(ValidatedAtItem, out var stamp)
            && DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var validatedAt)
            && now - validatedAt < interval)
        {
            return;
        }

        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue(CustomClaimTypes.UserId), out var userId)
            || !Guid.TryParse(principal?.FindFirstValue(CustomClaimTypes.SecurityStamp), out var securityStamp))
        {
            await RejectAsync(context).ConfigureAwait(false);
            return;
        }

        await using var scope = context.HttpContext.RequestServices.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<ISignInService>()
            .ValidateSessionAsync(userId, securityStamp, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (user is null)
        {
            await RejectAsync(context).ConfigureAwait(false);
            return;
        }

        context.ReplacePrincipal(CreatePrincipal(user));
        properties.Items[ValidatedAtItem] = now.ToString("O", CultureInfo.InvariantCulture);
        context.ShouldRenew = true;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(Scheme).ConfigureAwait(false);
    }
}

/// <summary>The dashboard's authorization policies.</summary>
/// <remarks>
/// Layer: Web. Both authenticate with the dashboard cookie only, so neither can be satisfied by a
/// client key - just as the API's policies can never be satisfied by the cookie.
/// </remarks>
public static class DashboardPolicies
{
    /// <summary>Any signed-in dashboard user.</summary>
    public const string Authenticated = "Dashboard";

    /// <summary>A signed-in Super Admin.</summary>
    public const string SuperAdmin = "DashboardSuperAdmin";

    /// <summary>The role names, as the cookie's role claim carries them.</summary>
    internal static readonly string[] AllRoles = [nameof(UserRole.SuperAdmin), nameof(UserRole.StationUser)];
}

/// <summary>How long dashboard sessions last and how often they are re-checked.</summary>
/// <remarks>
/// Layer: Web. Bound from <c>DashboardAuth</c>, the same section as the Application layer's lockout
/// rules.
/// </remarks>
public sealed class DashboardCookieOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "DashboardAuth";

    /// <summary>Gets or sets how long an ordinary session lasts without activity, in hours.</summary>
    [Range(1, 168)]
    public int ExpireHours { get; set; } = 12;

    /// <summary>Gets or sets how long "keep me signed in" lasts, in days.</summary>
    [Range(1, 90)]
    public int RememberMeDays { get; set; } = 14;

    /// <summary>Gets or sets how often a session is checked against the stored user, in minutes.</summary>
    [Range(1, 60)]
    public int RevalidationMinutes { get; set; } = 5;
}
