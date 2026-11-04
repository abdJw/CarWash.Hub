using System.Text.Json.Serialization;
using Asp.Versioning;
using CarWash.Hub.Api.Options;
using CarWash.Hub.Application.Features.DeviceSettings;
using CarWash.Hub.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CarWash.Hub.Api.Controllers.V1;

/// <summary>
/// The settings each machine polls for: its station's prices and run times, by revision.
/// </summary>
/// <remarks>
/// Layer: Api. Called by the ESP32 firmware about every 15 seconds with the same
/// <c>client-secret-key</c> it posts its messages with. The machine applies a new revision when idle
/// and acknowledges it with a <c>settings_ack</c> message on <c>POST /api/v1/machine-messages</c>.
/// </remarks>
/// <param name="settings">Reads the settings.</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/settings")]
[Tags("Machine settings")]
public sealed class DeviceSettingsController(IDeviceSettingsService settings) : ControllerBase
{
    /// <summary>Returns the settings a machine should run.</summary>
    /// <param name="query">The machine asking: <c>?machineId=AA:BB:CC:DD:EE:FF</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The settings document, exactly as the firmware reads it.</returns>
    /// <remarks>
    /// The body is the document itself - no envelope - minified, with an exact
    /// <c>Content-Length</c>, <c>Cache-Control: no-store</c> and no compression, because the
    /// cellular modem reads it into a small fixed buffer. A machine with nothing to apply - unknown,
    /// unassigned, or its station not fully priced yet - gets <c>{"revision":0}</c>, still with 200.
    /// <code>{"revision":1,"command":"update_settings","services":{"water":{"price_qirsh":50,"durations_seconds":[180,180,210]},"soap":{"price_qirsh":50,"durations_seconds":[60,90,90]},"minute_water":{"price_qirsh":25,"duration_seconds":60},"vacuum":{"price_qirsh":50,"durations_seconds":[180,180,210]}}}</code>
    /// </remarks>
    /// <response code="200">The settings, or <c>{"revision":0}</c> when there are none.</response>
    /// <response code="400">machineId missing or malformed.</response>
    /// <response code="401">The <c>client-secret-key</c> header is missing or wrong.</response>
    /// <response code="429">Too many polls from this address; retry after <c>Retry-After</c> seconds.</response>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.Ingest)]
    [EnableRateLimiting(RateLimitingOptions.SettingsPolicyName)]
    [ProducesResponseType<DeviceSettingsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Get([FromQuery] DeviceSettingsQuery query, CancellationToken cancellationToken) =>
        new DeviceJsonResult(await settings.GetDocumentAsync(query, cancellationToken).ConfigureAwait(false));
}

/// <summary>
/// Writes pre-built JSON bytes as they are: status 200, <c>application/json</c>, an exact
/// <c>Content-Length</c> and <c>Cache-Control: no-store</c>.
/// </summary>
/// <remarks>
/// Layer: Api. Not an <see cref="ObjectResult"/>, so the response envelope filter leaves it alone,
/// and with its length set before the first byte, so IIS sends it whole instead of chunked - the
/// A7670G modem reads neither an envelope nor chunked transfer encoding.
/// </remarks>
/// <param name="body">The bytes to send.</param>
public sealed class DeviceJsonResult(ReadOnlyMemory<byte> body) : IActionResult
{
    /// <inheritdoc />
    public async Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "application/json";
        response.ContentLength = body.Length;
        response.Headers.CacheControl = "no-store";

        await response.Body.WriteAsync(body, context.HttpContext.RequestAborted).ConfigureAwait(false);
    }
}

/// <summary>The settings document, as the OpenAPI description shows it. Never serialized: the bytes are built by <c>DeviceSettingsDocument</c>.</summary>
/// <param name="Revision">The revision; 0 when there are no settings, and then nothing else is sent.</param>
/// <param name="Command">Always <c>update_settings</c>.</param>
/// <param name="Services">Every service, by code: <c>water</c>, <c>soap</c>, <c>minute_water</c>, <c>vacuum</c>.</param>
/// <remarks>Layer: Api.</remarks>
public sealed record DeviceSettingsResponse(
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("command")] string? Command,
    [property: JsonPropertyName("services")] IReadOnlyDictionary<string, DeviceServiceSettingsResponse>? Services);

/// <summary>One service in the settings document.</summary>
/// <param name="PriceQirsh">The price, in qirsh: 5 to 995, in steps of 5.</param>
/// <param name="DurationsSeconds">The run times of selector positions 1-3, in seconds; absent for minute water.</param>
/// <param name="DurationSeconds">Minute water's single run time, in seconds.</param>
/// <remarks>Layer: Api.</remarks>
public sealed record DeviceServiceSettingsResponse(
    [property: JsonPropertyName("price_qirsh")] int PriceQirsh,
    [property: JsonPropertyName("durations_seconds")] IReadOnlyList<int>? DurationsSeconds,
    [property: JsonPropertyName("duration_seconds")] int? DurationSeconds);
