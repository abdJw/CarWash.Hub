using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CarWash.Hub.Application;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Infrastructure.Localization;

namespace CarWash.Hub.UnitTests.Localization;

/// <summary>
/// Asserts that every resource key resolves in both supported cultures.
/// </summary>
/// <remarks>
/// <c>IStringLocalizer</c> fails <em>silently</em>: a missing translation returns the key itself,
/// so <c>Ingestion_MachineIdMissing</c> would be served to an engineer as though it were a
/// sentence. Comparing the constants against the resource files catches it at build time.
/// </remarks>
public partial class ResourceCompletenessTests
{
    private const string ArabicSuffix = "ar-SA";

    private static IReadOnlyDictionary<string, string> ValuesIn(string cultureSuffix)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(SharedResource).Assembly.Location)!);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.SkipWhen(directory is null, "Could not locate the repository root from the test output directory.");
        var fileName = string.IsNullOrEmpty(cultureSuffix) ? "SharedResource.resx" : $"SharedResource.{cultureSuffix}.resx";
        var path = Path.Combine(directory!.FullName, "src", "CarWash.Hub.Application", "Resources", fileName);

        return XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(element => element.Attribute("name")!.Value, element => element.Element("value")?.Value ?? string.Empty, StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> DeclaredKeys() =>
    [
        // Every nested class of ResourceKeys, so a new group is covered automatically.
        .. typeof(ResourceKeys)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(group => group.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field is { IsLiteral: true, IsInitOnly: false })
            .Select(field => (string)field.GetRawConstantValue()!),
    ];

    private static HashSet<string> Placeholders(string text) =>
        [.. Placeholder().Matches(text).Select(match => match.Groups["name"].Value)];

    [GeneratedRegex(@"\{(?<name>[A-Za-z0-9_]+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    [Fact]
    public void Every_declared_key_has_an_English_translation()
    {
        var english = ValuesIn(string.Empty);

        var missing = DeclaredKeys().Where(key => !english.ContainsKey(key)).ToList();

        missing.ShouldBeEmpty($"declared in ResourceKeys but absent from the English resources: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_declared_key_has_an_Arabic_translation()
    {
        var arabic = ValuesIn(ArabicSuffix);

        var missing = DeclaredKeys().Where(key => !arabic.ContainsKey(key)).ToList();

        missing.ShouldBeEmpty($"no Arabic translation, so an ar-SA caller would receive the raw key: {string.Join(", ", missing)}");
    }

    [Fact]
    public void The_two_resource_files_declare_the_same_keys()
    {
        var english = ValuesIn(string.Empty).Keys.ToHashSet(StringComparer.Ordinal);
        var arabic = ValuesIn(ArabicSuffix).Keys.ToHashSet(StringComparer.Ordinal);

        english.Except(arabic).ShouldBeEmpty("keys present in English but missing from Arabic");
        arabic.Except(english).ShouldBeEmpty("keys present in Arabic but missing from English");
    }

    [Fact]
    public void Every_resource_entry_is_declared_in_ResourceKeys()
    {
        // A stale entry nothing references any more is dead weight that drifts out of date.
        var declared = DeclaredKeys().ToHashSet(StringComparer.Ordinal);

        ValuesIn(string.Empty).Keys.Where(key => !declared.Contains(key)).ShouldBeEmpty();
    }

    [Fact]
    public void Declared_keys_are_unique()
    {
        var declared = DeclaredKeys();
        declared.Distinct(StringComparer.Ordinal).Count().ShouldBe(declared.Count);
    }

    [Fact]
    public void No_translation_is_empty()
    {
        foreach (var culture in new[] { string.Empty, ArabicSuffix })
        {
            var empty = ValuesIn(culture).Where(entry => string.IsNullOrWhiteSpace(entry.Value)).Select(entry => entry.Key).ToList();
            empty.ShouldBeEmpty($"blank entries in the '{culture}' resources: {string.Join(", ", empty)}");
        }
    }

    [Fact]
    public void Both_languages_fill_the_same_placeholders()
    {
        var english = ValuesIn(string.Empty);
        var arabic = ValuesIn(ArabicSuffix);

        var mismatched = english
            .Where(entry => arabic.TryGetValue(entry.Key, out var translation)
                            && !Placeholders(entry.Value).SetEquals(Placeholders(translation)))
            .Select(entry => entry.Key)
            .ToList();

        mismatched.ShouldBeEmpty($"the two languages fill different placeholders: {string.Join(", ", mismatched)}");
    }

    [Fact]
    public void Supported_cultures_are_en_US_and_ar_SA()
    {
        LocalizationExtensions.SupportedCultures.Select(culture => culture.Name).ShouldBe(["en-US", "ar-SA"]);
        LocalizationExtensions.DefaultCulture.ShouldBe("en-US");
    }

    [Fact]
    public void The_Arabic_culture_actually_resolves()
    {
        // Guards InvariantGlobalization: switched on, every culture would collapse to the invariant
        // one and Arabic lookup would silently return English.
        var arabic = new CultureInfo(LocalizationExtensions.ArabicCulture);

        arabic.Name.ShouldBe("ar-SA");
        arabic.TwoLetterISOLanguageName.ShouldBe("ar");
    }
}
