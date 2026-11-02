using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Features.Finance;
using CarWash.Hub.Application.Features.Pricing;
using CarWash.Hub.UnitTests.TestSupport;

namespace CarWash.Hub.UnitTests.Finance;

/// <summary>What a price and a finance report query must look like.</summary>
public class FinanceValidatorTests
{
    private static readonly SetStationPriceRequestValidator PriceValidator = new(new EchoLocalizer());
    private static readonly FinanceReportQueryValidator QueryValidator = new(new EchoLocalizer());

    private static readonly SetStationPriceRequest ValidPrice = new()
    {
        StationId = Guid.NewGuid(),
        Service = "soap",
        UnitPrice = 0.5m,
        EffectiveFrom = new DateOnly(2026, 11, 2),
    };

    private static IReadOnlyList<string> PriceErrors(SetStationPriceRequest request, string property) =>
        [.. PriceValidator.Validate(request).Errors.Where(error => error.PropertyName == property).Select(error => error.ErrorMessage)];

    [Theory]
    [InlineData("0")]
    [InlineData("0.5")]
    [InlineData("1.250")]
    [InlineData("999.999")]
    public void A_price_to_the_fils_passes(string price) =>
        PriceValidator.Validate(ValidPrice with { UnitPrice = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture) }).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("-0.001")]
    [InlineData("1000")]
    public void A_price_outside_the_range_is_refused(string price) =>
        PriceErrors(ValidPrice with { UnitPrice = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture) }, nameof(SetStationPriceRequest.UnitPrice))
            .ShouldContain(message => message.StartsWith(ResourceKeys.Validation.Range, StringComparison.Ordinal));

    [Fact]
    public void A_price_finer_than_a_fils_is_refused() =>
        PriceErrors(ValidPrice with { UnitPrice = 0.1234m }, nameof(SetStationPriceRequest.UnitPrice))
            .ShouldBe([$"{ResourceKeys.Validation.PriceDecimals}|{ResourceKeys.Fields.Price}"]);

    [Fact]
    public void A_price_needs_a_station_a_service_and_a_start_day()
    {
        var errors = PriceValidator.Validate(new SetStationPriceRequest()).Errors.Select(error => error.PropertyName).ToHashSet();

        errors.ShouldContain(nameof(SetStationPriceRequest.StationId));
        errors.ShouldContain(nameof(SetStationPriceRequest.Service));
        errors.ShouldContain(nameof(SetStationPriceRequest.EffectiveFrom));
    }

    [Fact]
    public void A_report_range_up_to_the_limit_passes()
    {
        var from = new DateOnly(2026, 1, 1);

        QueryValidator.Validate(new FinanceReportQuery { From = from, To = from.AddDays(FinanceReportQuery.MaxSpanDays) }).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_backwards_report_range_is_refused()
    {
        var result = QueryValidator.Validate(new FinanceReportQuery { From = new DateOnly(2026, 2, 1), To = new DateOnly(2026, 1, 1) });

        result.Errors.Select(error => error.ErrorMessage)
            .ShouldContain($"{ResourceKeys.Validation.DateRange}|{ResourceKeys.Fields.From}|{ResourceKeys.Fields.To}");
    }

    [Fact]
    public void A_report_range_over_the_limit_is_refused()
    {
        var from = new DateOnly(2026, 1, 1);
        var result = QueryValidator.Validate(new FinanceReportQuery { From = from, To = from.AddDays(FinanceReportQuery.MaxSpanDays + 1) });

        result.Errors.Select(error => error.ErrorMessage).ShouldContain(message => message.StartsWith(ResourceKeys.Validation.RangeTooLong, StringComparison.Ordinal));
    }

    [Fact]
    public void A_report_needs_both_ends()
    {
        var errors = QueryValidator.Validate(new FinanceReportQuery()).Errors.Select(error => error.PropertyName).ToHashSet();

        errors.ShouldContain(nameof(FinanceReportQuery.From));
        errors.ShouldContain(nameof(FinanceReportQuery.To));
    }

    [Fact]
    public void An_unknown_grain_is_refused() =>
        QueryValidator.Validate(new FinanceReportQuery { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 1, 2), Grain = (FinanceGrain)42 })
            .IsValid.ShouldBeFalse();
}
