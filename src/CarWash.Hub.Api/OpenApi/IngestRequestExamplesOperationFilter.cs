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
