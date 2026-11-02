using System.Globalization;
using CarWash.Hub.Application.Features.Finance;
using CarWash.Hub.Web.Localization;
using ClosedXML.Excel;
using Microsoft.Extensions.Localization;

namespace CarWash.Hub.Web.Reports;

/// <summary>Writes the finance report as an Excel workbook.</summary>
/// <remarks>
/// Layer: Web. One sheet per view of the report - summary, timeline, stations, services, machines -
/// and one with every day, station, machine and service, filterable. Numbers are real numbers with
/// number formats, not text, so the workbook sums and pivots. Sheets read right to left in Arabic.
/// </remarks>
public static class FinanceWorkbook
{
    private const string MoneyFormat = "#,##0.000";
    private const string CountFormat = "#,##0";
    private const string ShareFormat = "0.0%";
    private const string DateFormat = "yyyy-mm-dd";

    /// <summary>Writes the workbook.</summary>
    /// <param name="report">The report, with its detail rows.</param>
    /// <param name="filters">The filters it was made with, in words.</param>
    /// <param name="localizer">The dashboard's words, in the caller's language.</param>
    /// <returns>The .xlsx file.</returns>
    public static byte[] Write(FinanceReportDto report, FinanceExportFilters filters, IStringLocalizer<DashboardResource> localizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(localizer);

        var rtl = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;
        using var workbook = new XLWorkbook();

        Summary(Sheet(workbook, localizer[DashboardText.ExportSheetSummary], rtl), report, filters, localizer);
        Periods(Sheet(workbook, localizer[DashboardText.ExportSheetPeriods], rtl), report, localizer);
        Lines(Sheet(workbook, localizer[DashboardText.ExportSheetStations], rtl), report.Stations, localizer[DashboardText.FieldStation], localizer[DashboardText.FieldCode], localizer);
        Lines(Sheet(workbook, localizer[DashboardText.ExportSheetServices], rtl), report.Services, localizer[DashboardText.FieldService], null, localizer);
        Lines(Sheet(workbook, localizer[DashboardText.ExportSheetMachines], rtl), report.Machines, localizer[DashboardText.FinanceMachine], localizer[DashboardText.FieldStation], localizer);
        Detail(Sheet(workbook, localizer[DashboardText.ExportSheetDetail], rtl), report.Detail ?? [], localizer);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static IXLWorksheet Sheet(XLWorkbook workbook, string name, bool rtl)
    {
        var sheet = workbook.AddWorksheet(name);
        sheet.RightToLeft = rtl;
        return sheet;
    }

    private static void Summary(IXLWorksheet sheet, FinanceReportDto report, FinanceExportFilters filters, IStringLocalizer<DashboardResource> localizer)
    {
        sheet.Cell(1, 1).Value = localizer[DashboardText.FinanceTitle].Value;
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);

        var row = 3;
        Fact(localizer[DashboardText.FinancePeriod], $"{Iso(report.From)} – {Iso(report.To)}");
        Fact(localizer[DashboardText.FinanceStations], filters.Stations);
        Fact(localizer[DashboardText.FinanceServices], filters.Services);
        Fact(localizer[DashboardText.FinanceMachine], filters.Machine);
        Fact(localizer[DashboardText.ExportCurrency], report.Currency);

        row++;
        Figure(localizer[DashboardText.FinanceKpiRevenue], report.Totals.Revenue, MoneyFormat);
        Figure(localizer[DashboardText.FinanceKpiUses], report.Totals.Uses, CountFormat);
        Figure(localizer[DashboardText.FinanceKpiAverage], report.Totals.AveragePerDay, MoneyFormat);
        Figure(localizer[DashboardText.FinanceKpiUnpriced], report.Totals.UnpricedUses, CountFormat);
        if (report.UnassignedUses > 0)
        {
            Figure(localizer[DashboardText.ExportUnassignedUses], report.UnassignedUses, CountFormat);
        }

        row++;
        Fact(localizer[DashboardText.ExportPreviousPeriod], $"{Iso(report.PreviousFrom)} – {Iso(report.PreviousTo)}");
        Figure(localizer[DashboardText.FinanceKpiRevenue], report.Previous.Revenue, MoneyFormat);
        Figure(localizer[DashboardText.FinanceKpiUses], report.Previous.Uses, CountFormat);
        if (report.Previous.Revenue > 0)
        {
            Figure(localizer[DashboardText.ExportRevenueChange], (report.Totals.Revenue - report.Previous.Revenue) / report.Previous.Revenue, ShareFormat);
        }

        sheet.Column(1).Style.Font.SetBold();
        sheet.Columns(1, 2).AdjustToContents();

        void Fact(string label, string value)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 2).Value = value;
            row++;
        }

        void Figure(string label, decimal value, string format)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 2).Value = value;
            sheet.Cell(row, 2).Style.NumberFormat.Format = format;
            row++;
        }
    }

    private static void Periods(IXLWorksheet sheet, FinanceReportDto report, IStringLocalizer<DashboardResource> localizer)
    {
        // One revenue column per service, in the order the report ranks them.
        var services = report.Services.Select(line => (line.Key, line.Name)).ToList();

        Header(
            sheet,
            [
                localizer[DashboardText.ExportPeriodStart],
                localizer[DashboardText.ExportPeriodEnd],
                localizer[DashboardText.FinanceColUses],
                Revenue(localizer),
                localizer[DashboardText.FinanceColUnpriced],
                .. services.Select(service => $"{service.Name} ({localizer[DashboardText.FinanceCurrency]})"),
            ]);

        var row = 2;
        foreach (var period in report.Periods)
        {
            Date(sheet.Cell(row, 1), period.Start);
            Date(sheet.Cell(row, 2), period.End);
            Count(sheet.Cell(row, 3), period.Uses);
            Money(sheet.Cell(row, 4), period.Revenue);
            Count(sheet.Cell(row, 5), period.UnpricedUses);

            for (var index = 0; index < services.Count; index++)
            {
                var amount = period.Services.FirstOrDefault(service => string.Equals(service.Service, services[index].Key, StringComparison.OrdinalIgnoreCase));
                Money(sheet.Cell(row, 6 + index), amount?.Revenue ?? 0);
            }

            row++;
        }

        Finish(sheet, row - 1, 5 + services.Count);
    }

    private static void Lines(
        IXLWorksheet sheet, IReadOnlyList<FinanceLineDto> lines, string nameHeader, string? detailHeader, IStringLocalizer<DashboardResource> localizer)
    {
        var column = 1;
        var nameColumn = column++;
        var detailColumn = detailHeader is null ? 0 : column++;
        var usesColumn = column++;
        var revenueColumn = column++;
        var shareColumn = column++;
        var unpricedColumn = column;

        Header(
            sheet,
            [
                nameHeader,
                .. detailHeader is null ? Array.Empty<string>() : [detailHeader],
                localizer[DashboardText.FinanceColUses],
                Revenue(localizer),
                localizer[DashboardText.FinanceColShare],
                localizer[DashboardText.FinanceColUnpriced],
            ]);

        var row = 2;
        foreach (var line in lines)
        {
            sheet.Cell(row, nameColumn).Value = line.Name;
            if (detailColumn > 0)
            {
                sheet.Cell(row, detailColumn).Value = line.Detail ?? string.Empty;
            }

            Count(sheet.Cell(row, usesColumn), line.Uses);
            Money(sheet.Cell(row, revenueColumn), line.Revenue);
            sheet.Cell(row, shareColumn).Value = line.Share;
            sheet.Cell(row, shareColumn).Style.NumberFormat.Format = ShareFormat;
            Count(sheet.Cell(row, unpricedColumn), line.UnpricedUses);
            row++;
        }

        Finish(sheet, row - 1, unpricedColumn);
    }

    private static void Detail(IXLWorksheet sheet, IReadOnlyList<FinanceDetailRowDto> rows, IStringLocalizer<DashboardResource> localizer)
    {
        Header(
            sheet,
            [
                localizer[DashboardText.ExportDate],
                localizer[DashboardText.FieldStation],
                localizer[DashboardText.FinanceMachine],
                localizer[DashboardText.ExportMachineCode],
                localizer[DashboardText.FieldService],
                localizer[DashboardText.FinanceColUses],
                $"{localizer[DashboardText.FieldPrice]} ({localizer[DashboardText.FinanceCurrency]})",
                Revenue(localizer),
            ]);

        var row = 2;
        foreach (var detail in rows)
        {
            Date(sheet.Cell(row, 1), detail.Date);
            sheet.Cell(row, 2).Value = detail.Station;
            sheet.Cell(row, 3).Value = detail.Machine;
            sheet.Cell(row, 4).Value = detail.MachineCode;
            sheet.Cell(row, 5).Value = detail.ServiceName;
            Count(sheet.Cell(row, 6), detail.Uses);
            if (detail.UnitPrice is { } price)
            {
                Money(sheet.Cell(row, 7), price);
            }

            Money(sheet.Cell(row, 8), detail.Revenue);
            row++;
        }

        // Fixed widths: measuring thousands of rows would make the download slow.
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, Math.Max(1, row - 1), 8).SetAutoFilter();
        foreach (var (column, width) in new[] { (1, 12), (2, 24), (3, 22), (4, 20), (5, 14), (6, 10), (7, 14), (8, 14) })
        {
            sheet.Column(column).Width = width;
        }
    }

    private static void Header(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            sheet.Cell(1, index + 1).Value = headers[index];
        }

        var range = sheet.Range(1, 1, 1, Math.Max(1, headers.Count));
        range.Style.Font.SetBold();
        range.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E1E0D9"));
    }

    private static void Finish(IXLWorksheet sheet, int lastRow, int lastColumn)
    {
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, Math.Max(1, lastRow), lastColumn).SetAutoFilter();
        sheet.Columns(1, lastColumn).AdjustToContents();
    }

    private static string Revenue(IStringLocalizer<DashboardResource> localizer) =>
        localizer[DashboardText.FinanceColRevenue, localizer[DashboardText.FinanceCurrency]];

    private static void Money(IXLCell cell, decimal value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = MoneyFormat;
    }

    private static void Count(IXLCell cell, long value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = CountFormat;
    }

    private static void Date(IXLCell cell, DateOnly date)
    {
        cell.Value = date.ToDateTime(TimeOnly.MinValue);
        cell.Style.DateFormat.Format = DateFormat;
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
