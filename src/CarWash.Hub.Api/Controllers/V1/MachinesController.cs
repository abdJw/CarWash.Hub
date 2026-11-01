using Asp.Versioning;
using CarWash.Hub.Application.Common.Models;
using CarWash.Hub.Application.Features.Machines;
using CarWash.Hub.Application.Features.Usage;
using CarWash.Hub.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarWash.Hub.Api.Controllers.V1;

/// <summary>
/// Reads machines and their stored messages: current status and history.
/// </summary>
/// <remarks>
/// Layer: Api. Every action requires the <c>client-read-key</c> - never the key the machines carry.
/// <c>{machineId}</c> is the identifier the machine sends, matched case-insensitively.
/// </remarks>
/// <param name="machineService">Reads machines and messages.</param>
/// <param name="usageService">Reads the parsed usage history.</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/machines")]
[Authorize(Policy = AuthorizationPolicies.Read)]
[Produces("application/json")]
[Tags("Machines")]
public sealed class MachinesController(IMachineService machineService, IUsageService usageService) : ControllerBase
{
    /// <summary>Lists the machines that have sent at least one message.</summary>
    /// <param name="query">Search (machine id or name), sort (<c>machineId</c>, <c>firstSeenAt</c>, <c>lastSeenAt</c>) and page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of machines with their first- and last-seen times.</returns>
    /// <response code="200">The page.</response>
    /// <response code="401">The <c>client-read-key</c> header is missing or wrong.</response>
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<MachineDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<MachineDto>>> GetPaged(
        [FromQuery] MachineListQuery query,
        CancellationToken cancellationToken) =>
        Ok(await machineService.GetPagedAsync(query, cancellationToken).ConfigureAwait(false));

    /// <summary>Returns one machine.</summary>
    /// <param name="machineId">The identifier the machine sends.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The machine.</returns>
    /// <response code="200">The machine.</response>
    /// <response code="401">The <c>client-read-key</c> header is missing or wrong.</response>
    /// <response code="404">No machine with this id has sent a message.</response>
    [HttpGet("{machineId}")]
    [ProducesResponseType<ApiResponse<MachineDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MachineDto>> GetByMachineId(string machineId, CancellationToken cancellationToken) =>
        Ok(await machineService.GetByCodeAsync(machineId, cancellationToken).ConfigureAwait(false));

    /// <summary>Returns a machine's latest message - its current status.</summary>
    /// <param name="machineId">The identifier the machine sends.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The most recent message, with its raw payload and, for JSON, the payload as JSON.</returns>
    /// <response code="200">The latest message.</response>
    /// <response code="401">The <c>client-read-key</c> header is missing or wrong.</response>
    /// <response code="404">No such machine.</response>
    [HttpGet("{machineId}/messages/latest")]
    [ProducesResponseType<ApiResponse<MachineMessageDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MachineMessageDto>> GetLatestMessage(string machineId, CancellationToken cancellationToken) =>
        Ok(await machineService.GetLatestMessageAsync(machineId, cancellationToken).ConfigureAwait(false));

    /// <summary>Returns a machine's message history, newest first.</summary>
    /// <param name="machineId">The identifier the machine sends.</param>
    /// <param name="query">An optional receive-time window (<c>from</c>, <c>to</c>, inclusive, ISO 8601) and the page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of messages with their raw payloads.</returns>
    /// <response code="200">The page.</response>
    /// <response code="400">The window is backwards or a value does not parse.</response>
    /// <response code="401">The <c>client-read-key</c> header is missing or wrong.</response>
    /// <response code="404">No such machine.</response>
    [HttpGet("{machineId}/messages")]
    [ProducesResponseType<ApiResponse<PagedResult<MachineMessageDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<MachineMessageDto>>> GetHistory(
        string machineId,
        [FromQuery] MachineHistoryQuery query,
        CancellationToken cancellationToken) =>
        Ok(await machineService.GetHistoryAsync(machineId, query, cancellationToken).ConfigureAwait(false));

    /// <summary>Returns how a machine was used: uses per service, in total and per hour.</summary>
    /// <param name="machineId">The identifier the machine sends.</param>
    /// <param name="query">
    /// An optional window (<c>from</c>, <c>to</c>, ISO 8601): the last 24 hours by default, at most 31
    /// days. Counts are per whole UTC hour.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Totals per service, busiest first, and counts per hour, oldest first.</returns>
    /// <remarks>
    /// Usage is parsed from the stored messages by a background worker, normally within a second of
    /// arrival; <c>GET /api/v1/processing/status</c> shows any backlog.
    /// </remarks>
    /// <response code="200">The usage.</response>
    /// <response code="400">The window is backwards or longer than 31 days.</response>
    /// <response code="401">The <c>client-read-key</c> header is missing or wrong.</response>
    /// <response code="404">No such machine.</response>
    [HttpGet("{machineId}/usage")]
    [ProducesResponseType<ApiResponse<MachineUsageDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MachineUsageDto>> GetUsage(
        string machineId,
        [FromQuery] UsageQuery query,
        CancellationToken cancellationToken) =>
        Ok(await usageService.GetMachineUsageAsync(machineId, query, cancellationToken).ConfigureAwait(false));
}
