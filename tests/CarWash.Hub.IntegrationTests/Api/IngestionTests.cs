using System.Net;
using System.Text.Json;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Features.Ingestion;
using CarWash.Hub.IntegrationTests.Infrastructure;

namespace CarWash.Hub.IntegrationTests.Api;

/// <summary>
/// <c>POST /api/v1/machine-messages</c> with <c>{ "machineId": ..., "data": ... }</c>: data of any JSON
/// shape is stored as text against the right machine, and every refusal is a localized problem
/// document.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class IngestionTests(DatabaseFixture database) : IAsyncLifetime
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

    private async Task<JsonElement> ReadStoredAsync(Uri location)
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Read(location.PathAndQuery));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.JsonAsync()).GetProperty("data");
    }

    private static async Task<JsonElement> ShouldBeBadRequestAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        return await response.JsonAsync();
    }

    [Fact]
    public async Task Object_data_is_stored_as_its_raw_json_and_readable_at_its_location()
    {
        var machineId = ApiRequests.NewMachineId();
        const string data = """{"status":"Idle","coins":12,"temperatureC":41.5}""";

        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(machineId, data)));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();
        var envelope = await response.JsonAsync();
        envelope.GetProperty("success").GetBoolean().ShouldBeTrue();
        envelope.GetProperty("message").GetString().ShouldBe("Machine message stored.");
        var result = envelope.GetProperty("data");
        result.GetProperty("machineId").GetString().ShouldBe(machineId);

        var stored = await ReadStoredAsync(response.Headers.Location);
        stored.GetProperty("id").GetInt64().ShouldBe(result.GetProperty("messageId").GetInt64());
        stored.GetProperty("payload").GetString().ShouldBe(data);
        stored.GetProperty("payloadJson").GetProperty("coins").GetInt32().ShouldBe(12);
        stored.GetProperty("clientKeyId").GetString().ShouldBe(TestKeys.IngestId);
    }

    [Fact]
    public async Task String_data_holding_json_is_stored_unescaped()
    {
        var machineId = ApiRequests.NewMachineId();

        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(
            ApiRequests.Body(machineId, "\"{\\\"status\\\":\\\"Washing\\\"}\"")));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var stored = await ReadStoredAsync(response.Headers.Location!);
        stored.GetProperty("payload").GetString().ShouldBe("""{"status":"Washing"}""");
        stored.GetProperty("payloadJson").GetProperty("status").GetString().ShouldBe("Washing");
    }

    [Fact]
    public async Task String_data_holding_text_is_stored_as_text()
    {
        var machineId = ApiRequests.NewMachineId();

        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(machineId, "\"Idle,12,41.5\"")));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var stored = await ReadStoredAsync(response.Headers.Location!);
        stored.GetProperty("payload").GetString().ShouldBe("Idle,12,41.5");
        stored.GetProperty("payloadJson").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Array_data_is_stored()
    {
        var machineId = ApiRequests.NewMachineId();

        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(machineId, """[{"t":1},{"t":2}]""")));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadStoredAsync(response.Headers.Location!)).GetProperty("payloadJson").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task A_missing_machine_id_is_refused()
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest("""{"data":{"status":"Idle"}}"""));

        var problem = await ShouldBeBadRequestAsync(response);
        problem.GetProperty("code").GetString().ShouldBe(ResourceKeys.Errors.ValidationFailed);
        problem.GetProperty("errors").GetProperty("machineId")[0].GetString().ShouldBe("Machine id is required.");
        problem.GetProperty("correlationId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Errors_come_back_in_Arabic_when_asked()
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(
            """{"data":{"status":"Idle"}}""",
            headers: new Dictionary<string, string> { [HttpHeaderNames.Language] = "ar" }));

        var problem = await ShouldBeBadRequestAsync(response);
        response.Content.Headers.ContentLanguage.ShouldContain("ar-SA");
        problem.GetProperty("errors").GetProperty("machineId")[0].GetString().ShouldBe("معرّف الجهاز مطلوب.");
    }

    [Fact]
    public async Task A_malformed_machine_id_is_refused()
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body("CW/01", "{}")));

        var problem = await ShouldBeBadRequestAsync(response);
        problem.GetProperty("errors").GetProperty("machineId")[0].GetString()!.ShouldContain("'CW/01'");
    }

    [Theory]
    [InlineData("""{"machineId":"CW-1"}""")]
    [InlineData("""{"machineId":"CW-1","data":null}""")]
    [InlineData("""{"machineId":"CW-1","data":""}""")]
    public async Task Missing_data_is_refused(string body)
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(body));

        var problem = await ShouldBeBadRequestAsync(response);
        problem.GetProperty("errors").GetProperty("data")[0].GetString().ShouldBe("Data is required.");
    }

    [Fact]
    public async Task Data_over_64_KB_is_refused()
    {
        var body = ApiRequests.Body(ApiRequests.NewMachineId(), $"\"{new string('x', StoredPayload.MaxBytes + 1)}\"");

        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(body));

        var problem = await ShouldBeBadRequestAsync(response);
        problem.GetProperty("errors").GetProperty("data")[0].GetString().ShouldBe($"data must not exceed {StoredPayload.MaxBytes} bytes.");
    }

    [Theory]
    [InlineData("""{"machineId":"CW-1","data":{"status":}}""")]
    [InlineData("""{"machineId":1234,"data":{}}""")]
    [InlineData("not json at all")]
    public async Task A_body_that_is_not_the_expected_json_is_refused(string body)
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(body));

        var problem = await ShouldBeBadRequestAsync(response);
        problem.GetProperty("code").GetString().ShouldBe(ResourceKeys.Validation.MalformedRequest);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task A_non_json_content_type_is_unsupported(string contentType)
    {
        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body("CW-1", "{}"), contentType));

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Twenty_simultaneous_first_messages_register_one_machine_and_store_twenty_messages()
    {
        var machineId = ApiRequests.NewMachineId();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            _client.SendAndDisposeAsync(ApiRequests.Ingest(ApiRequests.Body(machineId, $$"""{"seq":{{i}}}""")))));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Created);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        using var list = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machines?searchTerm={machineId}"));
        (await list.JsonAsync()).GetProperty("data").GetProperty("totalCount").GetInt32().ShouldBe(1);

        using var history = await _client.SendAndDisposeAsync(ApiRequests.Read($"/api/v1/machines/{machineId}/messages"));
        (await history.JsonAsync()).GetProperty("data").GetProperty("totalCount").GetInt32().ShouldBe(20);
    }

    [Fact]
    public async Task Machine_ids_are_matched_ignoring_case_and_keep_their_first_spelling()
    {
        var machineId = ApiRequests.NewMachineId().ToUpperInvariant();
        await _client.PushStatusAsync(machineId, "Idle");

        using var response = await _client.SendAndDisposeAsync(ApiRequests.Ingest(
            ApiRequests.Body($"  {machineId.ToLowerInvariant()}  ", """{"status":"Washing"}""")));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.JsonAsync()).GetProperty("data").GetProperty("machineId").GetString().ShouldBe(machineId);
    }
}
