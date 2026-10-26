using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Infrastructure.Auth;

/// <summary>
/// The shared client keys: the one every machine sends, and the one the read endpoints require.
/// </summary>
/// <remarks>
/// Layer: Infrastructure. Bound from the <c>ClientKeys</c> configuration section.
/// <para>
/// <b>Configuration, not the database.</b> Every machine carries the same
/// <c>client-secret-key</c>, so there is nothing per-machine to manage - one secret, supplied like
/// any other: user-secrets in development, environment variables
/// (<c>ClientKeys__IngestKeys__0__Key</c>) everywhere else. Nothing here is ever committed.
/// </para>
/// <para>
/// <b>Up to two keys per list, for rotation without downtime.</b> Machines cannot all be updated
/// at once, so a new key is added as the second entry, rolled out machine by machine - each
/// stored message records which key id it arrived with - and the old entry removed once no
/// message uses it.
/// </para>
/// </remarks>
public sealed class ClientKeyOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "ClientKeys";

    /// <summary>The most keys one list may hold: the current one and its replacement.</summary>
    public const int MaxKeysPerList = 2;

    /// <summary>The shortest key accepted - 32 characters of a random key is well over 128 bits.</summary>
    public const int MinKeyLength = 32;

    /// <summary>The longest key accepted.</summary>
    public const int MaxKeyLength = 256;

    /// <summary>The longest key id accepted; it is stored on every message.</summary>
    public const int MaxIdLength = 32;

    /// <summary>
    /// Keys starting with this are for local development only, and are refused in any other
    /// environment - so a copy of <c>appsettings.Development.json</c> cannot open production.
    /// </summary>
    public const string DevelopmentKeyPrefix = "dev-only-";

    /// <summary>Gets the keys accepted in <c>client-secret-key</c>: the ones machines send to push messages.</summary>
    public IList<ClientKeyEntry> IngestKeys { get; } = [];

    /// <summary>Gets the keys accepted in <c>client-read-key</c>: for the read endpoints only.</summary>
    public IList<ClientKeyEntry> ReadKeys { get; } = [];
}

/// <summary>One configured key.</summary>
/// <remarks>Layer: Infrastructure.</remarks>
public sealed class ClientKeyEntry
{
    /// <summary>
    /// Gets or sets a short, non-secret name for the key, such as <c>2026-10</c>. Stored on every
    /// message and written to the logs in place of the key itself.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the secret value. Never logged, never stored.</summary>
    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// Refuses to start with missing, weak, duplicated or development keys.
/// </summary>
/// <remarks>
/// Layer: Infrastructure. Runs at startup (<c>ValidateOnStart</c>), so a misconfigured deployment
/// fails immediately with a message naming the setting to fix - rather than accepting every
/// request, or none. Messages never include a key's value.
/// </remarks>
/// <param name="environment">Decides whether development keys are allowed.</param>
public sealed partial class ClientKeyOptionsValidator(IHostEnvironment environment) : IValidateOptions<ClientKeyOptions>
{
    private static readonly string[] Placeholders = ["changeme", "change-me", "change_me", "replace", "your-key", "yourkey", "example", "placeholder", "<", ">"];

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ClientKeyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        ValidateList(nameof(ClientKeyOptions.IngestKeys), options.IngestKeys, failures);
        ValidateList(nameof(ClientKeyOptions.ReadKeys), options.ReadKeys, failures);

        // A key that both pushes and reads would undo the point of having two: a key pulled out of
        // a machine must not open the read endpoints.
        var shared = options.IngestKeys.Select(entry => entry.Key)
            .Intersect(options.ReadKeys.Select(entry => entry.Key), StringComparer.Ordinal)
            .Any(key => !string.IsNullOrEmpty(key));
        if (shared)
        {
            failures.Add(
                $"{ClientKeyOptions.SectionName}: the same key appears in both IngestKeys and ReadKeys. " +
                "The read key must differ from the key the machines carry.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateList(string listName, IList<ClientKeyEntry> entries, List<string> failures)
    {
        var path = $"{ClientKeyOptions.SectionName}:{listName}";

        if (entries.Count == 0)
        {
            failures.Add(
                $"{path} is empty. Set {ClientKeyOptions.SectionName}__{listName}__0__Id and " +
                $"{ClientKeyOptions.SectionName}__{listName}__0__Key (user-secrets or environment variables).");
            return;
        }

        if (entries.Count > ClientKeyOptions.MaxKeysPerList)
        {
            failures.Add(
                $"{path} has {entries.Count} keys; at most {ClientKeyOptions.MaxKeysPerList} may be active " +
                "at once (the current key and its replacement during a rotation).");
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var entryPath = $"{path}:{index}";

            if (string.IsNullOrWhiteSpace(entry.Id) || entry.Id.Length > ClientKeyOptions.MaxIdLength || !ValidId().IsMatch(entry.Id))
            {
                failures.Add(
                    $"{entryPath}:Id must be 1 to {ClientKeyOptions.MaxIdLength} letters, digits, '.', '_' or '-'. " +
                    "It names the key in logs and on stored messages.");
            }

            var key = entry.Key ?? string.Empty;
            if (key.Length is < ClientKeyOptions.MinKeyLength or > ClientKeyOptions.MaxKeyLength
                || key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                failures.Add(
                    $"{entryPath}:Key must be {ClientKeyOptions.MinKeyLength} to {ClientKeyOptions.MaxKeyLength} " +
                    "characters with no spaces. Generate one with a cryptographic random generator (see README).");
            }
            else if (Placeholders.Any(placeholder => key.Contains(placeholder, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"{entryPath}:Key is a placeholder value. Replace it with a randomly generated key.");
            }
            else if (key.StartsWith(ClientKeyOptions.DevelopmentKeyPrefix, StringComparison.OrdinalIgnoreCase)
                     && !environment.IsDevelopment())
            {
                failures.Add(
                    $"{entryPath}:Key is a development-only key ('{ClientKeyOptions.DevelopmentKeyPrefix}...') and is " +
                    $"refused in the {environment.EnvironmentName} environment.");
            }
        }

        if (entries.Select(entry => entry.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
        {
            failures.Add($"{path} has two keys with the same Id; each key needs its own Id.");
        }

        if (entries.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count() != entries.Count)
        {
            failures.Add($"{path} lists the same key twice.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidId();
}
