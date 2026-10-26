using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CarWash.Hub.Api.OpenApi;

/// <summary>
/// Marks each operation with the client key header it requires.
/// </summary>
/// <remarks>
/// Layer: Api.
/// <para>
/// Reads the same <c>[Authorize(Policy = ...)]</c> metadata the authorization middleware enforces,
/// so the documentation cannot drift from the enforcement. The security requirement is added per
/// operation, not globally: Swagger's Authorize button then sends <c>client-secret-key</c> only to
/// the ingestion endpoint and <c>client-read-key</c> only to the read endpoints.
/// </para>
/// </remarks>
public sealed class ScopeOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        var policies = metadata.OfType<IAuthorizeData>()
            .Select(data => data.Policy)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var policy in policies)
        {
            var (scheme, header) = policy switch
            {
                AuthorizationPolicies.Ingest => (OpenApiConfiguration.SecretKeySchemeName, HttpHeaderNames.ClientSecretKey),
                AuthorizationPolicies.Read => (OpenApiConfiguration.ReadKeySchemeName, HttpHeaderNames.ClientReadKey),
                _ => (null, null),
            };

            if (scheme is null)
            {
                continue;
            }

            var line = $"**Requires header:** `{header}`.";
            operation.Description = string.IsNullOrWhiteSpace(operation.Description)
                ? line
                : $"{operation.Description}\n\n{line}";

            // Microsoft.OpenApi v2: a requirement is keyed by a scheme REFERENCE, resolved against
            // the document's components at serialization time.
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(scheme, null, null)] = [],
            });
        }
    }
}
