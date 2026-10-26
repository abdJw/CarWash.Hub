using CarWash.Hub.Application.Abstractions.Services;

namespace CarWash.Hub.UnitTests.TestSupport;

/// <summary>
/// An <see cref="ILocalizationService"/> that returns keys instead of text, with arguments joined
/// by <c>|</c>, so assertions do not depend on the wording in the resource files.
/// </summary>
/// <param name="arabic">Whether the "request culture" is Arabic, for <see cref="Localize"/>.</param>
public sealed class EchoLocalizer(bool arabic = false) : ILocalizationService
{
    /// <inheritdoc />
    public string CurrentCulture => arabic ? "ar-SA" : "en-US";

    /// <inheritdoc />
    public bool IsArabic => arabic;

    /// <inheritdoc />
    public string GetString(string key) => key;

    /// <inheritdoc />
    public string GetString(string key, params object[] arguments) =>
        arguments.Length == 0 ? key : $"{key}|{string.Join("|", arguments)}";

    /// <inheritdoc />
    public string Localize(string defaultValue, string? arabicValue) =>
        arabic && !string.IsNullOrWhiteSpace(arabicValue) ? arabicValue : defaultValue;
}
