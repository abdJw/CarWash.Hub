using System.Text.Json.Nodes;
using CarWash.Hub.Application.Features.Ingestion;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CarWash.Hub.Api.OpenApi;

/// <summary>
/// Adds ready-to-send examples to the ingestion request body.
/// </summary>
/// <remarks>
/// Layer: Api.
/// <para>
/// <c>data</c> is a free-form JSON value, so the generated schema alone tells an engineer nothing
/// about what to send. The named examples show the two ways to send it - as JSON, or as a JSON
/// string - and Swagger UI's example picker puts each one click away in "Try it out".
/// </para>
/// <para>
/// Microsoft.OpenApi 2.x models example values as <see cref="JsonNode"/>.
/// </para>
/// </remarks>
public sealed class IngestRequestExamplesOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var takesIngestRequest = context.ApiDescription.ParameterDescriptions
            .Any(parameter => parameter.Type == typeof(IngestMessageRequest));

        if (!takesIngestRequest
            || operation.RequestBody?.Content is not { } content
            || !content.TryGetValue("application/json", out var json))
        {
            return;
        }

        json.Examples = new Dictionary<string, IOpenApiExample>
        {
            // The ESP32 event firmware's messages (protocol baseline 2026-11-03): a sale, a water
            // report and a settings acknowledgement. Illustrative ids, not a real machine.
            ["paymentSuccess"] = new OpenApiExample
            {
                Summary = "ESP32: payment accepted, service started",
                Value = JsonNode.Parse("""{"machineId":"AA:BB:CC:DD:EE:FF","data":{"event":"payment_success","device_mac":"AA:BB:CC:DD:EE:FF","boot_id":"0123456789ABCDEF","event_id":"AA:BB:CC:DD:EE:FF-0123456789ABCDEF-payment_success-1","service":"water","price_qirsh":50,"paid_qirsh":50,"duration_seconds":180,"uptime_ms":120000,"water_available":true}}"""),
            },
            ["waterStatus"] = new OpenApiExample
            {
                Summary = "ESP32: water status (also the heartbeat)",
                Value = JsonNode.Parse("""{"machineId":"AA:BB:CC:DD:EE:FF","data":{"event":"water_status","device_mac":"AA:BB:CC:DD:EE:FF","boot_id":"0123456789ABCDEF","event_id":"AA:BB:CC:DD:EE:FF-0123456789ABCDEF-water_status-2","water_available":true,"uptime_ms":150000}}"""),
            },
            ["settingsAck"] = new OpenApiExample
            {
                Summary = "ESP32: settings revision saved",
                Value = JsonNode.Parse("""{"machineId":"AA:BB:CC:DD:EE:FF","data":{"event":"settings_ack","device_mac":"AA:BB:CC:DD:EE:FF","boot_id":"0123456789ABCDEF","event_id":"AA:BB:CC:DD:EE:FF-0123456789ABCDEF-settings_ack-3","revision":1,"status":"saved","water_available":true}}"""),
            },
            ["objectData"] = new OpenApiExample
            {
                Summary = "data as a JSON object",
                Value = JsonNode.Parse("""{"machineId":"CW-RUH-0001","data":{"status":"Idle","coins":12,"temperatureC":41.5}}"""),
            },
            ["arrayData"] = new OpenApiExample
            {
                Summary = "data as a JSON array",
                Value = JsonNode.Parse("""{"machineId":"CW-RUH-0002","data":[{"t":"2026-10-26T10:00:00Z","state":"Washing"},{"t":"2026-10-26T10:05:00Z","state":"Idle"}]}"""),
            },
            ["stringData"] = new OpenApiExample
            {
                Summary = "data as a JSON string",
                Value = JsonNode.Parse("""{"machineId":"CW-RUH-0003","data":"{\"status\":\"Idle\",\"coins\":12}"}"""),
            },
        };
    }
}
