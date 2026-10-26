using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CarWash.Hub.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API in-process against the test database.
/// </summary>
/// <remarks>
/// The whole application - every middleware in its real order, the real filters, the real
/// client-key handler - with only the configuration replaced. No service is substituted, so a
/// test through here proves the composed system.
/// </remarks>
/// <param name="database">The database fixture supplying the connection string.</param>
/// <param name="overrides">Settings that replace the defaults below, for a test that needs, say, rate limiting on.</param>
/// <param name="environment">The hosting environment; Development unless a test checks Production behaviour.</param>
/// <param name="withDefaultKeys">Whether to configure <see cref="TestKeys"/>; false for a test of key validation itself.</param>
public sealed class ApiFactory(
    DatabaseFixture database,
    IReadOnlyDictionary<string, string?>? overrides = null,
    string environment = "Development",
    bool withDefaultKeys = true) : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(environment);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = database.ConnectionString,
                ["Seed:ApplyMigrations"] = "true",

                // Off, so a burst of test requests is never mistaken for a misbehaving machine.
                ["RateLimiting:Enabled"] = "false",

                // Quiet: the rolling file sink would otherwise write into the source tree, and the
                // console sink would flood the test runner.
                ["Serilog:MinimumLevel:Default"] = "Warning",
                ["Serilog:MinimumLevel:Override:Microsoft"] = "Error",
                ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Error",
                ["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore.Database.Command"] = "Error",
                ["Serilog:MinimumLevel:Override:CarWash.Hub"] = "Error",
                ["Serilog:WriteTo:1:Name"] = "Console",
                ["RequestLogging:Enabled"] = "false",
            };

            if (withDefaultKeys)
            {
                // Index 0 replaces the dev-only keys of appsettings.Development.json; index 1 is the
                // rotation key.
                settings["ClientKeys:IngestKeys:0:Id"] = TestKeys.IngestId;
                settings["ClientKeys:IngestKeys:0:Key"] = TestKeys.Ingest;
                settings["ClientKeys:IngestKeys:1:Id"] = TestKeys.IngestNextId;
                settings["ClientKeys:IngestKeys:1:Key"] = TestKeys.IngestNext;
                settings["ClientKeys:ReadKeys:0:Id"] = TestKeys.ReadId;
                settings["ClientKeys:ReadKeys:0:Key"] = TestKeys.Read;
            }

            configuration.AddInMemoryCollection(settings);

            if (overrides is not null)
            {
                configuration.AddInMemoryCollection(overrides);
            }
        });
    }
}

/// <summary>The client keys the test host is configured with. Test values, never real ones.</summary>
public static class TestKeys
{
    /// <summary>The current machine key's id.</summary>
    public const string IngestId = "it-ingest";

    /// <summary>The current machine key.</summary>
    public const string Ingest = "it-ingest-key-4f1c9a7e2b8d3f6a0c5e";

    /// <summary>The rotation (next) machine key's id.</summary>
    public const string IngestNextId = "it-ingest-next";

    /// <summary>The rotation (next) machine key.</summary>
    public const string IngestNext = "it-ingest-next-key-8b2e6d1f9a4c7e3b";

    /// <summary>The read key's id.</summary>
    public const string ReadId = "it-read";

    /// <summary>The read key.</summary>
    public const string Read = "it-read-key-3e7a1c9f5b2d8e4a6c0f";
}
