using System.Globalization;
using CarWash.Hub.Application.Common.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace CarWash.Hub.Infrastructure.Localization;

/// <summary>
/// Resolves the request culture from the custom <c>X-Lang</c> header.
/// </summary>
/// <remarks>
/// Layer: Infrastructure.
/// <para>
/// <b>Why a custom header at all.</b> <c>Accept-Language</c> is set by the browser and reflects
/// the operating system's language, which is frequently not the language the user chose inside
/// the application. A dedicated header lets the client state that choice explicitly, and this
/// provider is registered ahead of the <c>Accept-Language</c> one so the explicit choice wins.
/// </para>
/// <para>
/// <b>Never throws.</b> The header is attacker-controlled: it may be absent, empty, absurdly
/// long, or a culture name that does not exist. Every one of those returns
/// <see langword="null"/>, which tells the framework to try the next provider and ultimately the
/// default culture. Throwing here would turn a malformed header into a 500 - and into a trivial
/// denial-of-service.
/// </para>
/// <para>
/// A recognised-but-unsupported culture (say <c>fr-FR</c>) also yields <see langword="null"/>
/// rather than being accepted and then silently falling back at lookup time, so the
/// <c>Content-Language</c> response header always names a culture the response is actually in.
/// </para>
/// </remarks>
public sealed class CustomHeaderRequestCultureProvider : RequestCultureProvider
{
    /// <summary>
    /// The longest header value considered. Anything longer is ignored outright.
    /// </summary>
    /// <remarks>
    /// The longest real culture name is well under this. The cap exists so that a megabyte-long
    /// header is rejected by a length check rather than by attempting to construct a
    /// <see cref="CultureInfo"/> from it.
    /// </remarks>
    private const int MaxHeaderLength = 32;

    /// <summary>Initialises the provider with the cultures it may return.</summary>
    /// <param name="supportedCultures">The cultures the application serves.</param>
    public CustomHeaderRequestCultureProvider(IReadOnlyList<CultureInfo> supportedCultures) =>
        SupportedCultureNames = [.. supportedCultures.Select(culture => culture.Name)];

    /// <summary>Gets the culture names this provider will accept.</summary>
    public IReadOnlyList<string> SupportedCultureNames { get; }

    /// <inheritdoc />
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var requested = httpContext.Request.Headers[HttpHeaderNames.Language].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(requested) || requested.Length > MaxHeaderLength)
        {
            return Task.FromResult<ProviderCultureResult?>(null);
        }

        requested = requested.Trim();

        // Exact match first: "ar-SA" as configured.
        var exact = SupportedCultureNames.FirstOrDefault(
            name => string.Equals(name, requested, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(exact, exact));
        }

        // Then the neutral language: "ar" resolves to "ar-SA". Clients commonly send the
        // two-letter code, and refusing it would push them onto the English default for no
        // reason a user could understand.
        var neutralMatch = SupportedCultureNames.FirstOrDefault(name =>
            name.StartsWith(requested + "-", StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                name.Split('-')[0], requested, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<ProviderCultureResult?>(
            neutralMatch is null ? null : new ProviderCultureResult(neutralMatch, neutralMatch));
    }
}
