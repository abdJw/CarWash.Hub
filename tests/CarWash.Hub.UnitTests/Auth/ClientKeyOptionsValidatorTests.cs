using CarWash.Hub.Infrastructure.Auth;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace CarWash.Hub.UnitTests.Auth;

public class ClientKeyOptionsValidatorTests
{
    private const string IngestKey = "K7f2c9d4e1a8b3f6c0d5e2a9b4c7f1e8d3a6b0c5f";
    private const string ReadKey = "R3b8e1f6a9c2d5b0e7f4a1c8d3b6e9f2a5c0d7e4b";

    private static ClientKeyOptionsValidator Validator(string environmentName = "Production")
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return new ClientKeyOptionsValidator(environment);
    }

    private static ClientKeyOptions Options(string ingestKey = IngestKey, string readKey = ReadKey)
    {
        var options = new ClientKeyOptions();
        options.IngestKeys.Add(new ClientKeyEntry { Id = "2026-10", Key = ingestKey });
        options.ReadKeys.Add(new ClientKeyEntry { Id = "read-1", Key = readKey });
        return options;
    }

    private static IReadOnlyList<string> FailuresOf(ClientKeyOptions options, string environmentName = "Production") =>
        [.. Validator(environmentName).Validate(null, options).Failures ?? []];

    [Fact]
    public void A_proper_configuration_passes() =>
        Validator().Validate(null, Options()).Succeeded.ShouldBeTrue();

    [Fact]
    public void Two_ingest_keys_are_allowed_for_a_rotation()
    {
        var options = Options();
        options.IngestKeys.Add(new ClientKeyEntry { Id = "2026-11", Key = IngestKey.Replace('K', 'N') });

        Validator().Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Missing_keys_fail_naming_the_environment_variables_to_set()
    {
        var failures = FailuresOf(new ClientKeyOptions());

        failures.ShouldContain(failure => failure.Contains("ClientKeys__IngestKeys__0__Key", StringComparison.Ordinal));
        failures.ShouldContain(failure => failure.Contains("ClientKeys__ReadKeys__0__Key", StringComparison.Ordinal));
    }

    [Fact]
    public void Three_keys_in_one_list_fail()
    {
        var options = Options();
        options.IngestKeys.Add(new ClientKeyEntry { Id = "b", Key = IngestKey.Replace('K', 'B') });
        options.IngestKeys.Add(new ClientKeyEntry { Id = "c", Key = IngestKey.Replace('K', 'C') });

        FailuresOf(options).ShouldContain(failure => failure.Contains("at most 2", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("too-short-key")]
    [InlineData("a key with spaces in it that is otherwise long enough")]
    public void Weak_keys_fail(string key) =>
        FailuresOf(Options(ingestKey: key)).ShouldNotBeEmpty();

    [Fact]
    public void A_placeholder_fails() =>
        FailuresOf(Options(ingestKey: "changeme-changeme-changeme-changeme")).ShouldContain(f => f.Contains("placeholder", StringComparison.Ordinal));

    [Fact]
    public void The_same_key_in_both_lists_fails() =>
        FailuresOf(Options(readKey: IngestKey)).ShouldContain(f => f.Contains("both IngestKeys and ReadKeys", StringComparison.Ordinal));

    [Fact]
    public void Duplicate_ids_fail()
    {
        var options = Options();
        options.IngestKeys.Add(new ClientKeyEntry { Id = "2026-10", Key = IngestKey.Replace('K', 'N') });

        FailuresOf(options).ShouldContain(f => f.Contains("same Id", StringComparison.Ordinal));
    }

    [Fact]
    public void A_development_key_is_refused_outside_Development_and_accepted_in_it()
    {
        var options = Options(ingestKey: "dev-only-ingest-key-0123456789abcdef");

        FailuresOf(options, "Production").ShouldContain(f => f.Contains("development-only", StringComparison.Ordinal));
        FailuresOf(options, "Staging").ShouldNotBeEmpty();
        FailuresOf(options, "Development").ShouldBeEmpty();
    }

    [Fact]
    public void No_failure_message_ever_contains_a_key()
    {
        var options = Options(ingestKey: "dev-only-ingest-key-0123456789abcdef", readKey: "short");

        foreach (var failure in FailuresOf(options))
        {
            failure.ShouldNotContain("0123456789abcdef");
            failure.ShouldNotContain("short");
        }
    }
}
