namespace CarWash.Hub.Application.Abstractions.Services;

/// <summary>
/// Resolves localized text and picks the right variant of localized entity data.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// There are two kinds of localization in this application and they are solved differently:
/// </para>
/// <list type="bullet">
///   <item><description><b>Messages</b> - validation text, error text, status messages. Known at
///   compile time, so they live in <c>.resx</c> resources and are resolved by key.</description></item>
///   <item><description><b>Entity data</b> - gas station names. Entered at run time, so they cannot
///   live in a compiled resource file and are stored as parallel columns (<c>Name</c> /
///   <c>NameAr</c>) picked by the current culture.</description></item>
/// </list>
/// <para>
/// This abstraction covers both, so a caller never has to know which mechanism applies, and so
/// nothing in Application has to reach for <c>CultureInfo</c> directly.
/// </para>
/// </remarks>
public interface ILocalizationService
{
    /// <summary>Gets the culture resolved for the current request, for example <c>ar-SA</c>.</summary>
    string CurrentCulture { get; }

    /// <summary>Gets a value indicating whether the current culture is Arabic.</summary>
    bool IsArabic { get; }

    /// <summary>Resolves a message by resource key for the current culture.</summary>
    /// <param name="key">The resource key, from <c>ResourceKeys</c>.</param>
    /// <returns>The localized message, or the key itself when no translation exists.</returns>
    string GetString(string key);

    /// <summary>Resolves a message by resource key, substituting arguments.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="arguments">Values substituted into the message.</param>
    /// <returns>The localized, formatted message.</returns>
    string GetString(string key, params object[] arguments);

    /// <summary>
    /// Picks the variant of a localized entity value matching the current culture.
    /// </summary>
    /// <param name="defaultValue">The English value.</param>
    /// <param name="arabicValue">The Arabic value, which may be absent.</param>
    /// <returns>
    /// The Arabic value under an Arabic culture when it is present, otherwise the English one.
    /// </returns>
    /// <remarks>
    /// Falls back rather than returning an empty string: a station with no Arabic name should show
    /// its English name, not a blank.
    /// </remarks>
    string Localize(string defaultValue, string? arabicValue);
}
