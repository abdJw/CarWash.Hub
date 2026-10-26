using System.Globalization;
using CarWash.Hub.Application.Abstractions.Services;
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
    public static IReadOnlyList<CultureInfo> SupportedCultures { get; } =
    [
        new CultureInfo(DefaultCulture),
        new CultureInfo(ArabicCulture),
    ];

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

        services.Configure<RequestLocalizationOptions>(options =>
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
            options.RequestCultureProviders =
            [
                new CustomHeaderRequestCultureProvider(SupportedCultures),
                new QueryStringRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider(),
            ];
        });

        return services;
    }

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
