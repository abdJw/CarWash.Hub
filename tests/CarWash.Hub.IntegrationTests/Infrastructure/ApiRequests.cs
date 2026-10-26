using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CarWash.Hub.Application.Common.Constants;

namespace CarWash.Hub.IntegrationTests.Infrastructure;

/// <summary>Builds requests the way a machine or a tester would send them.</summary>
public static class ApiRequests
{
    /// <summary>The ingestion endpoint.</summary>
    public const string IngestPath = "/api/v1/machine-messages";

    /// <summary>A fresh machine id, so tests never see each other's data in the shared database.</summary>
    /// <returns>An id such as <c>t-0f3a...</c>.</returns>
    public static string NewMachineId() => $"t-{Guid.NewGuid():N}";

    /// <summary>The request body for a message: <c>{"machineId": ..., "data": ...}</c>.</summary>
    /// <param name="machineId">The machine id.</param>
    /// <param name="dataJson">The <c>data</c> value as JSON text, for example <c>{"status":"Idle"}</c>.</param>
    /// <returns>The JSON body.</returns>
    public static string Body(string machineId, string dataJson) =>
        $$"""{"machineId":"{{machineId}}","data":{{dataJson}}}""";

    /// <summary>Builds a push, with the body bytes and Content-Type exactly as given.</summary>
    /// <param name="body">The body.</param>
    /// <param name="contentType">The full Content-Type header, or null to send none.</param>
    /// <param name="key">The <c>client-secret-key</c>, or null to send none.</param>
    /// <param name="headers">Extra headers.</param>
    /// <returns>The request.</returns>
    public static HttpRequestMessage Ingest(
        string body,
        string? contentType = "application/json",
        string? key = TestKeys.Ingest,
        IReadOnlyDictionary<string, string>? headers = null) =>
        Ingest(Encoding.UTF8.GetBytes(body), contentType, key, headers);

    /// <summary>Builds a push from raw bytes.</summary>
    /// <param name="body">The body bytes.</param>
    /// <param name="contentType">The full Content-Type header, or null to send none.</param>
    /// <param name="key">The <c>client-secret-key</c>, or null to send none.</param>
    /// <param name="headers">Extra headers.</param>
    /// <returns>The request.</returns>
    public static HttpRequestMessage Ingest(
        byte[] body,
        string? contentType,
        string? key = TestKeys.Ingest,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var content = new ByteArrayContent(body);
        if (contentType is not null)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, IngestPath) { Content = content };
        if (key is not null)
        {
            request.Headers.Add(HttpHeaderNames.ClientSecretKey, key);
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    /// <summary>Builds a read with the <c>client-read-key</c>.</summary>
    /// <param name="path">The path and query.</param>
    /// <param name="key">The read key, or null to send none.</param>
    /// <returns>The request.</returns>
    public static HttpRequestMessage Read(string path, string? key = TestKeys.Read)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (key is not null)
        {
            request.Headers.Add(HttpHeaderNames.ClientReadKey, key);
        }

        return request;
    }

    /// <summary>Reads a response body as JSON.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The root element.</returns>
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    /// <summary>Sends a request and returns the response.</summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The request, disposed after sending.</param>
    /// <returns>The response.</returns>
    public static async Task<HttpResponseMessage> SendAndDisposeAsync(this HttpClient client, HttpRequestMessage request)
    {
        using (request)
        {
            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Pushes a JSON status for a machine and returns the stored message id.</summary>
    /// <param name="client">The client.</param>
    /// <param name="machineId">The machine.</param>
    /// <param name="status">A status value, to tell messages apart.</param>
    /// <returns>The message id.</returns>
    public static async Task<long> PushStatusAsync(this HttpClient client, string machineId, string status)
    {
        using var response = await client.SendAndDisposeAsync(Ingest(Body(machineId, $$"""{"status":"{{status}}"}""")));
        response.EnsureSuccessStatusCode();
        return (await response.JsonAsync()).GetProperty("data").GetProperty("messageId").GetInt64();
    }
}
