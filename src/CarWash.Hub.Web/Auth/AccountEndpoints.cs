using CarWash.Hub.Infrastructure.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Routing;

namespace CarWash.Hub.Web.Auth;

/// <summary>The account endpoints that are not pages.</summary>
/// <remarks>
/// Layer: Web. Each one states its own authorization: the host's fallback policy demands a client
/// key, so an endpoint that forgot to would answer 401 to every browser.
/// </remarks>
public static class AccountEndpoints
{
    /// <summary>The language switch.</summary>
    public const string CulturePath = "/account/culture";

    /// <summary>Maps the endpoints.</summary>
    /// <param name="endpoints">The route builder.</param>
    public static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Stores the chosen language in the culture cookie and goes back. A plain link, not a form:
        // changing the language is harmless if forged, and it must work from the sign-in page too.
        endpoints.MapGet(CulturePath, (HttpContext context, string? culture, string? returnUrl) =>
            {
                var supported = LocalizationExtensions.SupportedCultures
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, culture, StringComparison.OrdinalIgnoreCase));

                if (supported is not null)
                {
                    context.Response.Cookies.Append(
                        LocalizationExtensions.CultureCookieName,
                        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(supported.Name)),
                        new CookieOptions
                        {
                            Expires = DateTimeOffset.UtcNow.AddYears(1),
                            IsEssential = true,
                            HttpOnly = true,
                            SameSite = SameSiteMode.Lax,
                            Secure = context.Request.IsHttps,
                            Path = "/",
                        });
                }

                return Results.LocalRedirect(SafeReturnUrl(returnUrl));
            })
            .AllowAnonymous()
            .ExcludeFromDescription();

        // Browsers ask for /favicon.ico whatever the page says; send them to the real icon.
        endpoints.MapGet("/favicon.ico", () => Results.Redirect("/favicon.svg", permanent: true))
            .AllowAnonymous()
            .ExcludeFromDescription();
    }

    // The language switch may return to the sign-in page itself, unlike a sign-in.
    private static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is not null && returnUrl.StartsWith(DashboardAuthentication.SignInPath, StringComparison.OrdinalIgnoreCase)
            && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : DashboardAuthentication.SafeReturnUrl(returnUrl);
}
