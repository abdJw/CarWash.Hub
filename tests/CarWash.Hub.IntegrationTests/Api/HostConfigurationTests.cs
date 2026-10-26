using System.Net;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.IntegrationTests.Infrastructure;

namespace CarWash.Hub.IntegrationTests.Api;

/// <summary>
/// Behaviour that depends on configuration: the rate limit, startup validation of the client keys,
/// and the API documentation per environment.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class HostConfigurationTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly List<ApiFactory> _factories = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(database.IsAvailable, database.SkipReason);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var factory in _factories)
        {
            await factory.DisposeAsync();
        }
    }

    private ApiFactory Factory(Dictionary<string, string?>? overrides = null, string environment = "Development", bool withDefaultKeys = true)
    {
        var factory = new ApiFactory(database, overrides, environment, withDefaultKeys);
        _factories.Add(factory);
        return factory;
    }

    [Fact]
    public async Task Pushing_past_the_rate_limit_is_refused_with_retry_after()
    {
        using var client = Factory(new()
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:IngestPermitLimit"] = "3",
            ["RateLimiting:IngestWindowSeconds"] = "60",
        }).CreateClient();
        var machineId = ApiRequests.NewMachineId();

        for (var i = 0; i < 3; i++)
        {
            await client.PushStatusAsync(machineId, $"S{i}");
        }

        using var refused = await client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(machineId, "{}")));

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.ShouldNotBeNull();
        (await refused.JsonAsync()).GetProperty("code").GetString().ShouldBe(ResourceKeys.Errors.TooManyRequests);
    }

    [Fact]
    public void Production_refuses_to_start_with_a_development_key()
    {
        var factory = Factory(
            new()
            {
                ["ClientKeys:IngestKeys:0:Id"] = "dev",
                ["ClientKeys:IngestKeys:0:Key"] = "dev-only-ingest-key-0123456789abcdef",
                ["ClientKeys:ReadKeys:0:Id"] = TestKeys.ReadId,
                ["ClientKeys:ReadKeys:0:Key"] = TestKeys.Read,
            },
            environment: "Production",
            withDefaultKeys: false);

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        failure.ToString().ShouldContain("development-only");
    }

    [Fact]
    public void Startup_fails_without_client_keys_and_says_which_settings_to_set()
    {
        var factory = Factory(environment: "Production", withDefaultKeys: false);

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        failure.ToString().ShouldContain("ClientKeys__IngestKeys__0__Key");
    }

    [Fact]
    public async Task Production_serves_the_api_but_not_the_documentation_by_default()
    {
        using var client = Factory(environment: "Production").CreateClient();

        // With a valid key, so the answer is about the documentation and not about authentication.
        using var swagger = await client.SendAndDisposeAsync(ApiRequests.Read("/swagger/v1/swagger.json"));
        using var health = await client.GetAsync(new Uri("/health", UriKind.Relative), Token);

        swagger.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_openapi_document_describes_both_keys_and_the_request_examples_without_needing_a_key()
    {
        using var client = database.SharedApi.CreateClient();

        using var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative), Token);
        using var scalar = await client.GetAsync(new Uri("/scalar/v1", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        scalar.StatusCode.ShouldBe(HttpStatusCode.OK);
        var document = await response.Content.ReadAsStringAsync(Token);
        document.ShouldContain("\"client-secret-key\"");
        document.ShouldContain("\"client-read-key\"");
        document.ShouldContain("IngestMessageRequest");
        document.ShouldContain("\"objectData\"");
        document.ShouldContain("\"stringData\"");
        document.ShouldNotContain("application/xml");
    }

    [Fact]
    public async Task An_unknown_route_reveals_nothing_without_a_key_and_is_404_with_one()
    {
        using var client = database.SharedApi.CreateClient();

        // Secure by default: the fallback policy covers paths with no endpoint too, so probing for
        // routes without a key learns nothing.
        using var anonymous = await client.GetAsync(new Uri("/api/v1/nothing-here", UriKind.Relative), Token);
        using var withKey = await client.SendAndDisposeAsync(ApiRequests.Read("/api/v1/nothing-here"));

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        withKey.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
