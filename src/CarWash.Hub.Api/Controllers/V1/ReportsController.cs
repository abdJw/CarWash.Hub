using Asp.Versioning;
using CarWash.Hub.Application.Common.Models;
using CarWash.Hub.Application.Features.Finance;
using CarWash.Hub.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarWash.Hub.Api.Controllers.V1;

/// <summary>
/// Reports built from the usage history: what the stations earned.
/// </summary>
/// <remarks>Layer: Api. Requires the <c>client-read-key</c>, which sees every station.</remarks>
/// <param name="financeService">Builds the finance report.</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reports")]
[Authorize(Policy = AuthorizationPolicies.Read)]
[Produces("application/json")]
[Tags("Reports")]
public sealed class ReportsController(IFinanceReportService financeService) : ControllerBase
{
    /// <summary>Returns the stations' revenue: uses valued at each station's price for each service.</summary>
    /// <param name="query">
    /// <c>from</c> and <c>to</c> (business days in Asia/Amman, <c>yyyy-MM-dd</c>, inclusive, at most
    /// 366 days apart); optional <c>stationIds</c> and <c>services</c> (repeat the parameter for
    /// several); optional <c>machineId</c> (the machine's key); <c>grain</c> (<c>Auto</c>, <c>Day</c>,
    /// <c>Week</c> or <c>Month</c>); <c>includeDetail</c> for one row per day, station, machine and
    /// service.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Totals and the same for the period just before, the timeline, and breakdowns by station,
    /// service and machine. Amounts are in Jordanian dinars. A use is valued at the price in effect
    /// at its station on its day; uses with no price yet are counted in <c>unpricedUses</c>.
    /// </returns>
    /// <response code="200">The report.</response>
    /// <response code="400">The range is missing, backwards or too long.</response>
    /// <response code="401">The <c>client-read-key</c> header is missing or wrong.</response>
    [HttpGet("finance")]
    [ProducesResponseType<ApiResponse<FinanceReportDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<FinanceReportDto>> GetFinance(
        [FromQuery] FinanceReportQuery query,
        CancellationToken cancellationToken) =>
        Ok(await financeService.GetReportAsync(query, cancellationToken).ConfigureAwait(false));
}
