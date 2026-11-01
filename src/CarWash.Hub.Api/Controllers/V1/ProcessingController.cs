using Asp.Versioning;
using CarWash.Hub.Application.Common.Models;
using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarWash.Hub.Api.Controllers.V1;

/// <summary>
/// Reports on the background processing that turns stored messages into usage.
/// </summary>
/// <remarks>Layer: Api. Requires the <c>client-read-key</c>.</remarks>
/// <param name="statusService">Reads the processing backlog.</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/processing")]
[Authorize(Policy = AuthorizationPolicies.Read)]
[Produces("application/json")]
[Tags("Processing")]
public sealed class ProcessingController(IProcessingStatusService statusService) : ControllerBase
{
    /// <summary>Returns the processing backlog and the state of the worker.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Pending and failed message counts, the arrival time of the oldest pending message, and the
    /// worker in the process that answered: whether it is enabled, holds the lease, and when it last
    /// ran or failed.
    /// </returns>
    /// <response code="200">The status.</response>
    /// <response code="401">The <c>client-read-key</c> header is missing or wrong.</response>
    [HttpGet("status")]
    [ProducesResponseType<ApiResponse<ProcessingStatusDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProcessingStatusDto>> GetStatus(CancellationToken cancellationToken) =>
        Ok(await statusService.GetStatusAsync(cancellationToken).ConfigureAwait(false));
}
