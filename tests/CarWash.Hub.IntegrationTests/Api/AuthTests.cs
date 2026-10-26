using System.Net;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.IntegrationTests.Infrastructure;

namespace CarWash.Hub.IntegrationTests.Api;

/// <summary>
/// The <c>client-secret-key</c> and <c>client-read-key</c> headers: each endpoint accepts exactly
/// its own key, a missing key is told apart from a wrong one, and nothing else is open.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuthTests(DatabaseFixture database) : IAsyncLifetime
{
    private HttpClient _client = null!;

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(database.IsAvailable, database.SkipReason);
        _client = database.SharedApi.CreateClient();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string StatusBody() => ApiRequests.Body(ApiRequests.NewMachineId(), """{"status":"Idle"}""");

    private static async Task ShouldBeUnauthorizedAsync(HttpResponseMessage response, string code)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.JsonAsync()).GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Pushing_without_the_header_is_refused_as_missing() =>
        await ShouldBeUnauthorizedAsync(
            await _client.SendAndDisposeAsync(ApiRequests.Ingest(StatusBody(), key: null)),
            ResourceKeys.Auth.ClientSecretKeyMissing);

    [Fact]
    public async Task Pushing_with_a_wrong_key_is_refused_as_invalid() =>
        await ShouldBeUnauthorizedAsync(
            await _client.SendAndDisposeAsync(ApiRequests.Ingest(StatusBody(), key: "not-the-key-not-the-key-not-the-key")),
            ResourceKeys.Auth.ClientSecretKeyInvalid);

    [Fact]
    public async Task The_read_key_cannot_push()
    {
        await ShouldBeUnauthorizedAsync(
            await _client.SendAndDisposeAsync(ApiRequests.Ingest(StatusBody(), key: TestKeys.Read)),
            ResourceKeys.Auth.ClientSecretKeyInvalid);

        // Sent in its own header it does not count either: the ingest endpoint reads only client-secret-key.
        var request = ApiRequests.Ingest(StatusBody(), key: null);
        request.Headers.Add(HttpHeaderNames.ClientReadKey, TestKeys.Read);
        await ShouldBeUnauthorizedAsync(await _client.SendAndDisposeAsync(request), ResourceKeys.Auth.ClientSecretKeyMissing);
    }

    [Fact]
    public async Task The_machine_key_cannot_read()
    {
        var request = ApiRequests.Read("/api/v1/machines", key: null);
        request.Headers.Add(HttpHeaderNames.ClientSecretKey, TestKeys.Ingest);

        await ShouldBeUnauthorizedAsync(await _client.SendAndDisposeAsync(request), ResourceKeys.Auth.ClientReadKeyMissing);
        await ShouldBeUnauthorizedAsync(
            await _client.SendAndDisposeAsync(ApiRequests.Read("/api/v1/machines", key: TestKeys.Ingest)),
            ResourceKeys.Auth.ClientReadKeyInvalid);
    }

    [Fact]
    public async Task The_rotation_key_is_accepted_and_recorded_on_the_message()
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(StatusBody(), key: TestKeys.IngestNext));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var stored = await _client.SendAndDisposeAsync(ApiRequests.Read(response.Headers.Location!.PathAndQuery));
        (await stored.JsonAsync()).GetProperty("data").GetProperty("clientKeyId").GetString().ShouldBe(TestKeys.IngestNextId);
    }

    [Theory]
    [InlineData("CLIENT-SECRET-KEY")]
    [InlineData("Client-Secret-Key")]
    public async Task The_header_name_is_case_insensitive(string headerName)
    {
        var request = ApiRequests.Ingest(StatusBody(), key: null);
        request.Headers.Add(headerName, TestKeys.Ingest);

        using var response = await _client.SendAndDisposeAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_key_is_never_accepted_from_the_query_string()
    {
        var request = ApiRequests.Ingest(StatusBody(), key: null);
        request.RequestUri = new Uri($"{ApiRequests.IngestPath}?client-secret-key={TestKeys.Ingest}", UriKind.Relative);

        await ShouldBeUnauthorizedAsync(await _client.SendAndDisposeAsync(request), ResourceKeys.Auth.ClientSecretKeyMissing);
    }

    [Fact]
    public async Task Health_probes_need_no_key()
    {
        using var live = await _client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        using var ready = await _client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
