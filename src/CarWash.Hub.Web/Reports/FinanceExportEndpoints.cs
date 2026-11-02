using System.Globalization;
using CarWash.Hub.Application.Features.Finance;
using CarWash.Hub.Web.Auth;
using CarWash.Hub.Web.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using AppValidationException = CarWash.Hub.Application.Exceptions.ValidationException;

namespace CarWash.Hub.Web.Reports;

/// <summary>What the finance export is asked for: the report's filters and a file format.</summary>
/// <remarks>Layer: Web. Bound from the query string, with the API's parameter names.</remarks>
public sealed class FinanceExportRequest
{
    /// <summary>Gets or sets the file format: <c>xlsx</c> (the default) or <c>csv</c>.</summary>
    public string? Format { get; set; }

    /// <summary>Gets or sets the first business day.</summary>
    public DateOnly From { get; set; }

    /// <summary>Gets or sets the last business day.</summary>
    public DateOnly To { get; set; }

    /// <summary>Gets or sets the stations; none for every station.</summary>
    public Guid[]? StationIds { get; set; }

    /// <summary>Gets or sets the service codes; none for every service.</summary>
    public string[]? Services { get; set; }

    /// <summary>Gets or sets one machine; null for every machine.</summary>
    public Guid? MachineId { get; set; }

    /// <summary>Gets or sets the timeline's bucket size.</summary>
    public FinanceGrain? Grain { get; set; }

    /// <summary>Gets a value indicating whether a CSV file is asked for.</summary>
    public bool IsCsv => string.Equals(Format, "csv", StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds the report query, with the detail rows the files carry.</summary>
    /// <returns>The query.</returns>
    public FinanceReportQuery ToQuery() => new()
    {
        From = From,
        To = To,
        StationIds = StationIds,
        Services = Services,
        MachineId = MachineId,
        Grain = Grain ?? FinanceGrain.Auto,
        IncludeDetail = true,
    };
}

/// <summary>The finance report as a download: an Excel workbook or a CSV file.</summary>
/// <remarks>
/// Layer: Web. A plain GET behind the dashboard cookie, so the page links to it with
/// <c>&lt;a download&gt;</c> - no script, and the content-security policy stays as it is. The
/// signed-in user's scope applies exactly as on the page: a station user gets their own station.
/// </remarks>
public static class FinanceExportEndpoints
{
    /// <summary>The export's path.</summary>
    public const string Path = "/reports/finance/export";

    /// <summary>Maps the export.</summary>
    /// <param name="endpoints">The endpoint builder.</param>
    public static void MapFinanceExport(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(Path, ExportAsync)
            .RequireAuthorization(DashboardPolicies.Authenticated)
            .ExcludeFromDescription();
    }

    private static async Task<IResult> ExportAsync(
        [AsParameters] FinanceExportRequest request,
        IFinanceReportService finance,
        IStringLocalizer<DashboardResource> localizer,
        CancellationToken cancellationToken)
    {
        FinanceReportDto report;
        FinanceFiltersDto filters;
        try
        {
            report = await finance.GetReportAsync(request.ToQuery(), cancellationToken).ConfigureAwait(false);
            filters = await finance.GetFiltersAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AppValidationException exception)
        {
            // Only a hand-made address gets here: the page never builds an invalid range.
            return Results.ValidationProblem(exception.Errors.ToDictionary(error => error.Key, error => error.Value));
        }

        var name = string.Create(CultureInfo.InvariantCulture, $"finance_{report.From:yyyy-MM-dd}_{report.To:yyyy-MM-dd}");

        return request.IsCsv
            ? Results.File(FinanceCsv.Write(report, localizer), "text/csv; charset=utf-8", name + ".csv")
            : Results.File(
                FinanceWorkbook.Write(report, FinanceExportFilters.Describe(report, filters, request, localizer), localizer),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                name + ".xlsx");
    }
}

/// <summary>The filters a file was made with, in words, for its summary sheet.</summary>
/// <param name="Stations">The stations, or "all stations".</param>
/// <param name="Services">The services, or "all services".</param>
/// <param name="Machine">The machine, or "all machines".</param>
/// <remarks>Layer: Web.</remarks>
public sealed record FinanceExportFilters(string Stations, string Services, string Machine)
{
    /// <summary>Puts the filters into words.</summary>
    /// <param name="report">The report.</param>
    /// <param name="filters">The names the caller may see.</param>
    /// <param name="request">The filters asked for.</param>
    /// <param name="localizer">The dashboard's words.</param>
    /// <returns>The filters, in words.</returns>
    public static FinanceExportFilters Describe(
        FinanceReportDto report, FinanceFiltersDto filters, FinanceExportRequest request, IStringLocalizer<DashboardResource> localizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(localizer);

        // A station user is always reported on their own station, whatever the address says.
        var stations = !filters.AllStations
            ? string.Join(", ", filters.Stations.Select(station => station.Name))
            : request.StationIds is { Length: > 0 } ids
                ? string.Join(", ", filters.Stations.Where(station => ids.Contains(station.Id)).Select(station => station.Name))
                : localizer[DashboardText.FinanceAllStations];

        var services = request.Services is { Length: > 0 } codes
            ? string.Join(", ", codes.Select(code => report.Services.FirstOrDefault(line => string.Equals(line.Key, code, StringComparison.OrdinalIgnoreCase))?.Name ?? code))
            : localizer[DashboardText.FinanceAllServices];

        var machine = request.MachineId is { } machineId
            ? filters.Machines.FirstOrDefault(option => option.Id == machineId)?.Name ?? machineId.ToString()
            : localizer[DashboardText.FinanceAllMachines];

        return new FinanceExportFilters(stations, services, machine);
    }
}
