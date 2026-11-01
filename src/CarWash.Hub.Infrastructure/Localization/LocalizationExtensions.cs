using System.Globalization;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Infrastructure.Localization;

/// <summary>
/// Configures request localization.
/// </summary>
/// <remarks>Layer: Infrastructure.</remarks>
public static class LocalizationExtensions
{
    /// <summary>The culture used when nothing else is requested or the request is unreadable.</summary>
    public const string DefaultCulture = "en-US";

    /// <summary>The Arabic culture this application serves.</summary>
    public const string ArabicCulture = "ar-SA";

    /// <summary>Gets the cultures the application supports, in preference order.</summary>
    /// <remarks>
    /// The Arabic culture is <c>ar-SA</c> with the Gregorian calendar. .NET's <c>ar-SA</c> defaults to
    /// the Um Al Qura (Hijri) calendar, which would put every date in the dashboard - and every
    /// date picker - in Hijri years. The request localization middleware hands out these very
    /// instances, so the calendar choice reaches every request.
    /// </remarks>
    public static IReadOnlyList<CultureInfo> SupportedCultures { get; } =
    [
        new CultureInfo(DefaultCulture),
        CreateArabicCulture(),
    ];

    /// <summary>The name of the cookie that remembers a dashboard user's language.</summary>
    public static string CultureCookieName => CookieRequestCultureProvider.DefaultCookieName;

    /// <summary>
    /// Registers request-localization options and the culture resolution service.
    /// </summary>
    /// <remarks>
    /// Named AddConfiguredLocalization rather than AddRequestLocalization deliberately: the
    /// framework already defines an extension with that exact name and signature on
    /// IServiceCollection, and whichever won the overload resolution would silently decide
    /// whether these options were applied at all. A distinct name removes the ambiguity.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddConfiguredLocalization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ILocalizationService, LocalizationService>();

        services.AddOptions<RequestLocalizationOptions>().Configure<IOptions<DashboardOptions>>((options, dashboard) =>
        {
            options.DefaultRequestCulture = new RequestCulture(DefaultCulture, DefaultCulture);
            options.SupportedCultures = [.. SupportedCultures];
            options.SupportedUICultures = [.. SupportedCultures];

            // Emits Content-Language on every response, naming the culture actually used. Without
            // it a client has no way to tell whether its requested language was honoured or
            // quietly replaced by the default.
            options.ApplyCurrentCultureToResponseHeaders = true;

            // ORDER IS THE MECHANISM. Providers are consulted in sequence and the first to
            // return a result wins, so putting the X-Lang provider first is precisely what makes
            // the custom header beat Accept-Language. Reordering these silently inverts the
            // documented behaviour.
            //   1. X-Lang - an API client's explicit choice. Browsers never send it.
            //   2. The culture cookie - a dashboard user's explicit choice.
            //   3. ?culture=
            //   4. The dashboard's default language, for dashboard pages only.
            //   5. Accept-Language - so an API client without X-Lang behaves as it always has.
            options.RequestCultureProviders =
            [
                new CustomHeaderRequestCultureProvider(SupportedCultures),
                new CookieRequestCultureProvider(),
                new QueryStringRequestCultureProvider(),
                new DashboardDefaultCultureProvider(ResolveSupportedName(dashboard.Value.DefaultCulture)),
                new AcceptLanguageHeaderRequestCultureProvider(),
            ];
        });

        return services;
    }

    private static CultureInfo CreateArabicCulture()
    {
        var culture = new CultureInfo(ArabicCulture);
        var gregorian = culture.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault();
        if (gregorian is not null)
        {
            culture.DateTimeFormat.Calendar = gregorian;
        }

        return culture;
    }

    // An unsupported name would make the provider's result be discarded; fall back to English.
    private static string ResolveSupportedName(string? name) =>
        SupportedCultures.FirstOrDefault(culture => string.Equals(culture.Name, name, StringComparison.OrdinalIgnoreCase))?.Name
        ?? DefaultCulture;

    /// <summary>
    /// Adds the request-localization middleware using the registered options.
    /// </summary>
    /// <param name="app">The application pipeline.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    /// <remarks>
    /// Placed ABOVE the exception handler in the pipeline, deliberately.
    /// <c>CultureInfo.CurrentUICulture</c> flows through an <c>AsyncLocal</c>, and a value set
    /// downstream is not visible to a handler further up the call stack. Ordering localization
    /// below the exception handler would leave every error response in English no matter what
    /// the client asked for - the culture would be set, but only after the handler that needed
    /// it had already been passed.
    /// <para>
    /// The cost of going first is that this middleware runs outside the exception handler. That
    /// is acceptable only because the culture provider is written never to throw; see
    /// <see cref="CustomHeaderRequestCultureProvider"/>.
    /// </para>
    /// </remarks>
    public static IApplicationBuilder UseConfiguredRequestLocalization(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices
            .GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

        return app.UseRequestLocalization(options);
    }
}
