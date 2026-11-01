using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CarWash.Hub.Web;
using CarWash.Hub.Web.Localization;

namespace CarWash.Hub.UnitTests.Localization;

/// <summary>
/// The dashboard's own words resolve in English and Arabic: every <see cref="DashboardText"/> key is
/// in both <c>DashboardResource</c> files, with the same placeholders, and nothing else is.
/// </summary>
public partial class DashboardResourceCompletenessTests
{
    private static IReadOnlyDictionary<string, string> ValuesIn(string? cultureSuffix)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(DashboardResource).Assembly.Location)!);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.SkipWhen(directory is null, "Could not locate the repository root from the test output directory.");
        var fileName = cultureSuffix is null ? "DashboardResource.resx" : $"DashboardResource.{cultureSuffix}.resx";
        var path = Path.Combine(directory!.FullName, "src", "CarWash.Hub.Web", "Resources", fileName);

        return XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(element => element.Attribute("name")!.Value, element => element.Element("value")?.Value ?? string.Empty, StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> DeclaredKeys() =>
    [
        .. typeof(DashboardText)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false })
            .Select(field => (string)field.GetRawConstantValue()!),
    ];

    [GeneratedRegex(@"\{(?<index>\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static HashSet<string> Placeholders(string text) => [.. Placeholder().Matches(text).Select(match => match.Groups["index"].Value)];

    [Theory]
    [InlineData(null)]
    [InlineData("ar-SA")]
    public void Every_key_is_in_the_resource_file(string? culture)
    {
        var values = ValuesIn(culture);

        var missing = DeclaredKeys().Where(key => !values.ContainsKey(key)).ToList();

        missing.ShouldBeEmpty($"missing from DashboardResource{(culture is null ? string.Empty : "." + culture)}.resx: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ar-SA")]
    public void No_entry_is_stale_or_empty(string? culture)
    {
        var declared = DeclaredKeys().ToHashSet(StringComparer.Ordinal);
        var values = ValuesIn(culture);

        values.Keys.Where(key => !declared.Contains(key)).ShouldBeEmpty("entries no DashboardText constant refers to");
        values.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).ShouldBeEmpty("empty values");
    }

    [Fact]
    public void Keys_are_unique()
    {
        var keys = DeclaredKeys();

        keys.GroupBy(key => key, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ShouldBeEmpty();
    }

    [Fact]
    public void Arabic_uses_the_same_placeholders_as_English()
    {
        var english = ValuesIn(null);
        var arabic = ValuesIn("ar-SA");

        var mismatched = english
            .Where(pair => arabic.TryGetValue(pair.Key, out var translation) && !Placeholders(pair.Value).SetEquals(Placeholders(translation)))
            .Select(pair => pair.Key)
            .ToList();

        mismatched.ShouldBeEmpty("a translation that drops or adds a {n} would throw or show the wrong value");
    }
}
