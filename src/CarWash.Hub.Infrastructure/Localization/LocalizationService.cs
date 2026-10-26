using System.Globalization;
using CarWash.Hub.Application;
using CarWash.Hub.Application.Abstractions.Services;
using Microsoft.Extensions.Localization;

namespace CarWash.Hub.Infrastructure.Localization;

/// <summary>
/// The default <see cref="ILocalizationService"/>, backed by the shared resources.
/// </summary>
/// <remarks>
/// Layer: Infrastructure. Scoped, matching the request whose culture it reports.
/// </remarks>
/// <param name="localizer">Resolves strings from the shared <c>.resx</c> files.</param>
public sealed class LocalizationService(IStringLocalizer<SharedResource> localizer) : ILocalizationService
{
    /// <inheritdoc />
    public string CurrentCulture => CultureInfo.CurrentUICulture.Name;

    /// <inheritdoc />
    /// <remarks>
    /// Compares the two-letter language rather than the full culture name, so <c>ar-SA</c>,
    /// <c>ar-EG</c> and a bare <c>ar</c> all count as Arabic.
    /// </remarks>
    public bool IsArabic =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("ar", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string GetString(string key) => localizer[key];

    /// <inheritdoc />
    public string GetString(string key, params object[] arguments) => localizer[key, arguments];

    /// <inheritdoc />
    public string Localize(string defaultValue, string? arabicValue) =>
        IsArabic && !string.IsNullOrWhiteSpace(arabicValue) ? arabicValue : defaultValue;
}
