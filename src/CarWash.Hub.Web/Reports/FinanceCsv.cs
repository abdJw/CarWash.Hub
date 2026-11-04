using System.Globalization;
using System.Text;
using CarWash.Hub.Application.Features.Finance;
using CarWash.Hub.Web.Localization;
using Microsoft.Extensions.Localization;

namespace CarWash.Hub.Web.Reports;

/// <summary>Writes the finance report's detail rows as CSV.</summary>
/// <remarks>
/// Layer: Web. UTF-8 with a byte-order mark, so Excel shows the Arabic names; dates in ISO form
/// and numbers in the invariant culture, so any tool reads them back the same. A text cell that
/// starts like a formula is prefixed with an apostrophe: station and machine names are typed by
/// people, and a spreadsheet must never run one.
/// </remarks>
public static class FinanceCsv
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];
    private static readonly char[] NeedsQuotes = [',', '"', '\r', '\n'];

    /// <summary>Writes the file.</summary>
    /// <param name="report">The report, with its detail rows.</param>
    /// <param name="localizer">The dashboard's words, for the header row.</param>
    /// <returns>The .csv file.</returns>
    public static byte[] Write(FinanceReportDto report, IStringLocalizer<DashboardResource> localizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(localizer);

        var csv = new StringBuilder();
        Row(
            csv,
            [
                localizer[DashboardText.ExportDate],
                localizer[DashboardText.FieldStation],
                localizer[DashboardText.FinanceMachine],
                localizer[DashboardText.ExportMachineCode],
                localizer[DashboardText.FieldService],
                localizer[DashboardText.FinanceColUses],
                $"{localizer[DashboardText.FieldPrice]} ({report.Currency})",
                $"{localizer[DashboardText.FinanceKpiRevenue]} ({report.Currency})",
                localizer[DashboardText.ExportDevicePricedUses],
                $"{localizer[DashboardText.FinanceKpiCash]} ({report.Currency})",
            ]);

        foreach (var detail in report.Detail ?? [])
        {
            Row(
                csv,
                [
                    detail.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Text(detail.Station),
                    Text(detail.Machine),
                    Text(detail.MachineCode),
                    Text(detail.ServiceName),
                    detail.Uses.ToString(CultureInfo.InvariantCulture),
                    detail.UnitPrice?.ToString("0.000", CultureInfo.InvariantCulture) ?? string.Empty,
                    detail.Revenue.ToString("0.000", CultureInfo.InvariantCulture),
                    detail.DevicePricedUses.ToString(CultureInfo.InvariantCulture),
                    detail.CashCollected.ToString("0.000", CultureInfo.InvariantCulture),
                ]);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static void Row(StringBuilder csv, IEnumerable<string> fields)
    {
        csv.AppendJoin(',', fields.Select(Quote));
        csv.Append("\r\n");
    }

    private static string Text(string value) =>
        value.Length > 0 && Array.IndexOf(FormulaStarts, value[0]) >= 0 ? "'" + value : value;

    private static string Quote(string field) =>
        field.IndexOfAny(NeedsQuotes) >= 0 ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : field;
}
