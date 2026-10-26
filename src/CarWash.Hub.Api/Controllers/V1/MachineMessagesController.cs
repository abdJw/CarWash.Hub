using Asp.Versioning;
using CarWash.Hub.Api.Filters;
using CarWash.Hub.Api.Options;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Common.Models;
using CarWash.Hub.Application.Features.Ingestion;
using CarWash.Hub.Application.Features.Machines;
using CarWash.Hub.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CarWash.Hub.Api.Controllers.V1;

/// <summary>
/// Receives machine status messages, and reads a stored one back.
/// </summary>
/// <remarks>Layer: Api.</remarks>
/// <param name="ingestionService">Stores messages.</param>
/// <param name="machineService">Reads stored messages.</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/machine-messages")]
[Produces("application/json")]
[Tags("Machine messages")]
public sealed class MachineMessagesController(
    IIngestionService ingestionService,
    IMachineService machineService) : ControllerBase
{
    /// <summary>
    /// The largest request the server reads at all; the data inside it is limited separately to
    /// 64 KB by the validator.
    /// </summary>
    public const int MaxRequestBytes = 1_048_576;

    /// <summary>Stores a machine's status message.</summary>
    /// <param name="request">The machine id and its data, in any JSON shape.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored message's identity.</returns>
    /// <remarks>
    /// <c>data</c> is stored as text, exactly as sent: a JSON string's own text, anything else as its
    /// raw JSON. A new <c>machineId</c> registers the machine.
    /// </remarks>
    /// <response code="201">Stored. <c>Location</c> points at the stored message.</response>
    /// <response code="400">machineId or data missing or invalid, or the body is not valid JSON.</response>
    /// <response code="401">The <c>client-secret-key</c> header is missing or wrong.</response>
    /// <response code="413">The request is larger than 1 MB.</response>
    /// <response code="415">The Content-Type is not <c>application/json</c>.</response>
    /// <response code="429">Too many messages from this address; retry after <c>Retry-After</c> seconds.</response>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Ingest)]
    [EnableRateLimiting(RateLimitingOptions.IngestPolicyName)]
    [RequestSizeLimit(MaxRequestBytes)]
    [Consumes("application/json")]
    [ResponseMessage(ResourceKeys.Messages.MessageStored)]
    [ProducesResponseType<ApiResponse<IngestMessageResult>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<IngestMessageResult>> Ingest(
        [FromBody] IngestMessageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await ingestionService.IngestAsync(request, cancellationToken).ConfigureAwait(false);

        return CreatedAtAction(nameof(GetById), new { id = result.MessageId, version = "1" }, result);
    }

    /// <summary>Returns one stored message.</summary>
    /// <param name="id">The message id returned when it was stored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message, with its stored data and, when it is JSON, the data as JSON.</returns>
    /// <response code="200">The message.</response>
    /// <response code="401">The <c>client-read-key</c> header is missing or wrong.</response>
    /// <response code="404">No message with this id.</response>
    [HttpGet("{id:long}")]
    [Authorize(Policy = AuthorizationPolicies.Read)]
    [ProducesResponseType<ApiResponse<MachineMessageDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MachineMessageDto>> GetById(long id, CancellationToken cancellationToken) =>
        Ok(await machineService.GetMessageByIdAsync(id, cancellationToken).ConfigureAwait(false));
}
