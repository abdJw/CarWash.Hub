using System.Globalization;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Features.DeviceSettings;
using CarWash.Hub.Application.Features.Pricing;
using CarWash.Hub.UnitTests.TestSupport;

namespace CarWash.Hub.UnitTests.DeviceSettings;

/// <summary>The firmware's limits on what a machine can be configured with.</summary>
public class DeviceServicesTests
{
    private static readonly SetStationPriceRequestValidator PriceValidator = new(new EchoLocalizer());
    private static readonly SetStationDurationsRequestValidator DurationsValidator = new(new EchoLocalizer());

    [Fact]
    public void The_four_services_are_listed_in_settings_order() =>
        DeviceServices.All.Select(service => service.Code).ShouldBe(["water", "soap", "minute_water", "vacuum"]);

    [Theory]
    [InlineData("0.05", 5)]
    [InlineData("0.25", 25)]
    [InlineData("0.500", 50)]
    [InlineData("9.95", 995)]
    public void A_price_in_five_qirsh_steps_is_sent_as_qirsh(string price, int qirsh) =>
        DeviceServices.ToQirsh(decimal.Parse(price, CultureInfo.InvariantCulture)).ShouldBe(qirsh);

    [Theory]
    [InlineData("0")]
    [InlineData("0.04")]
    [InlineData("0.07")]
    [InlineData("0.125")]
    [InlineData("9.96")]
    [InlineData("10")]
    public void A_price_the_machine_would_refuse_is_not_a_device_price(string price) =>
        DeviceServices.IsDevicePrice(decimal.Parse(price, CultureInfo.InvariantCulture)).ShouldBeFalse();

    [Fact]
    public void A_machine_service_price_must_be_one_the_machine_accepts()
    {
        var request = new SetStationPriceRequest { StationId = Guid.NewGuid(), Service = "Soap", UnitPrice = 0.125m, EffectiveFrom = new DateOnly(2026, 11, 4) };

        PriceValidator.Validate(request).Errors
            .ShouldContain(error => error.ErrorMessage == $"{ResourceKeys.Validation.DevicePrice}|soap");
        PriceValidator.Validate(request with { UnitPrice = 0.15m }).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Other_services_keep_the_fils_rule_only() =>
        PriceValidator.Validate(new SetStationPriceRequest { StationId = Guid.NewGuid(), Service = "wax", UnitPrice = 0.125m, EffectiveFrom = new DateOnly(2026, 11, 4) })
            .IsValid.ShouldBeTrue();

    [Fact]
    public void Three_position_services_need_three_run_times()
    {
        DurationsValidator.Validate(Durations("water", 180, 180, 210)).IsValid.ShouldBeTrue();
        DurationsValidator.Validate(Durations("water", 180)).Errors
            .ShouldContain(error => error.ErrorMessage == $"{ResourceKeys.Validation.DurationCount}|water|3");
    }

    [Fact]
    public void Minute_water_has_one_run_time()
    {
        DurationsValidator.Validate(Durations("minute_water", 60)).IsValid.ShouldBeTrue();
        DurationsValidator.Validate(Durations("minute_water", 60, 60, 60)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6000)]
    [InlineData(-1)]
    public void A_run_time_outside_the_firmware_limits_is_refused(int seconds) =>
        DurationsValidator.Validate(Durations("soap", 60, seconds, 90)).Errors
            .ShouldContain(error => error.ErrorMessage.StartsWith(ResourceKeys.Validation.DurationRange, StringComparison.Ordinal));

    [Fact]
    public void A_service_the_machines_do_not_have_has_no_run_times() =>
        DurationsValidator.Validate(Durations("wax", 60, 60, 60)).Errors
            .ShouldContain(error => error.ErrorMessage == $"{ResourceKeys.Validation.NotDeviceService}|wax");

    private static SetStationDurationsRequest Durations(string service, params int[] seconds) =>
        new() { StationId = Guid.NewGuid(), Service = service, Seconds = seconds };
}
