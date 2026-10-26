using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Infrastructure.Localization;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CarWash.Hub.Api.OpenApi;

/// <summary>
/// Adds the <c>X-Lang</c> header parameter to every operation.
/// </summary>
/// <remarks>
/// Layer: Api. The header is read by middleware rather than bound by any action, so without
/// this filter the generated document would not mention it and a client reading the spec would
/// never discover that responses can be requested in Arabic.
/// </remarks>
public sealed class LanguageHeaderOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);

        operation.Parameters ??= [];

        // Idempotent: a document filter or a second pass must not add it twice.
        if (operation.Parameters.Any(parameter =>
                string.Equals(parameter.Name, HttpHeaderNames.Language, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var supported = LocalizationExtensions.SupportedCultures.Select(culture => culture.Name).ToList();

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = HttpHeaderNames.Language,
            In = ParameterLocation.Header,
            Required = false,
            Description =
                $"Response language. Takes precedence over Accept-Language. " +
                $"Supported: {string.Join(", ", supported)}. The neutral code (`ar`) is also accepted. " +
                $"Unsupported or malformed values fall back to {LocalizationExtensions.DefaultCulture}. " +
                "The culture actually used is returned in the Content-Language response header.",
            // Microsoft.OpenApi v2 models schema values as JsonNode rather than IOpenApiAny.
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [.. supported.Select(name => (JsonNode)JsonValue.Create(name))],
                Default = JsonValue.Create(LocalizationExtensions.DefaultCulture),
            },
        });
    }
}
