using System.Text;
using CarWash.Hub.Application.Features.Finance;
using CarWash.Hub.Web;
using CarWash.Hub.Web.Formatting;
using CarWash.Hub.Web.Reports;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace CarWash.Hub.UnitTests.Finance;

/// <summary>The CSV export and the chart's value axis.</summary>
public class FinanceExportTests
{
    private static readonly FinanceTotalsDto NoTotals = new(0, 0, 0, 0, 0);

    private static IStringLocalizer<DashboardResource> Localizer()
    {
        var localizer = Substitute.For<IStringLocalizer<DashboardResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>()));
        localizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(call => new LocalizedString(call.ArgAt<string>(0), call.ArgAt<string>(0)));
        return localizer;
    }

    private static FinanceReportDto Report(params FinanceDetailRowDto[] detail) => new(
        new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), FinanceGrain.Day, "JOD",
        NoTotals, new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 31), NoTotals,
        [], [], [], [], 0, detail);

    private static FinanceDetailRowDto Row(string station, string machine, decimal? price) => new(
        new DateOnly(2026, 11, 2), Guid.NewGuid(), station, Guid.NewGuid(), machine, "B0:CB:D8:D0:64:2C", "soap", "Soap", 3, price, (price ?? 0) * 3);

    private static string[] Lines(byte[] file)
    {
        file.Take(3).ShouldBe(Encoding.UTF8.GetPreamble(), "Excel needs the byte-order mark to read the Arabic");
        return Encoding.UTF8.GetString(file, 3, file.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void The_csv_has_a_header_and_one_line_per_detail_row_with_invariant_numbers()
    {
        var lines = Lines(FinanceCsv.Write(Report(Row("محطة الشميساني", "Bay 1", 0.5m)), Localizer()));

        lines.Length.ShouldBe(2);
        lines[1].ShouldBe("2026-11-02,محطة الشميساني,Bay 1,B0:CB:D8:D0:64:2C,Soap,3,0.500,1.500");
    }

    [Fact]
    public void An_unpriced_row_leaves_the_price_empty() =>
        Lines(FinanceCsv.Write(Report(Row("A", "B", null)), Localizer()))[1].ShouldEndWith(",3,,0.000");

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "'+1")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@sum", "'@sum")]
    [InlineData("Bay, north", "\"Bay, north\"")]
    public void Text_that_looks_like_a_formula_is_neutralised_and_commas_are_quoted(string name, string expected) =>
        Lines(FinanceCsv.Write(Report(Row(name, "M", 1m)), Localizer()))[1].ShouldStartWith($"2026-11-02,{expected},M,");

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(3, 4)]
    [InlineData(7, 8)]
    [InlineData(9, 10)]
    [InlineData(11, 20)]
    [InlineData(300, 400)]
    [InlineData(0.75, 0.8)]
    public void The_axis_tops_out_at_a_round_number(double max, double expected) =>
        ChartScale.NiceCeiling(max).ShouldBe(expected, 1e-9);

    [Theory]
    [InlineData(110, 100, "+10%")]
    [InlineData(50, 100, "−50%")]
    [InlineData(100, 100, "0%")]
    public void A_change_is_a_signed_whole_percentage(int current, int previous, string expected) =>
        DashboardFormat.Change(current, previous).ShouldBe(expected);

    [Fact]
    public void There_is_no_change_against_nothing() =>
        DashboardFormat.Change(5, 0).ShouldBeNull();
}
