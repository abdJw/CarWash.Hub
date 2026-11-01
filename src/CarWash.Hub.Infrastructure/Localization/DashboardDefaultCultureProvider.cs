using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace CarWash.Hub.Infrastructure.Localization;

/// <summary>
/// Opens the dashboard in its configured default language, while the API keeps answering in English.
/// </summary>
/// <remarks>
/// Layer: Infrastructure.
/// <para>
/// Consulted after the explicit choices (<c>X-Lang</c>, the culture cookie, <c>?culture=</c>) and
/// before <c>Accept-Language</c>. The owner chose Arabic for the dashboard regardless of the
/// browser's language, so a dashboard page without a cookie opens in Arabic; an API path falls
/// through to <c>Accept-Language</c> and then to English, exactly as before the dashboard existed.
/// </para>
/// </remarks>
/// <param name="culture">The dashboard's default culture name.</param>
public sealed class DashboardDefaultCultureProvider(string culture) : RequestCultureProvider
{
    private static readonly PathString[] NonDashboardPaths = ["/api", "/health", "/swagger", "/scalar"];

    /// <inheritdoc />
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        foreach (var prefix in NonDashboardPaths)
        {
            if (httpContext.Request.Path.StartsWithSegments(prefix))
            {
                return Task.FromResult<ProviderCultureResult?>(null);
            }
        }

        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture, culture));
    }
}
