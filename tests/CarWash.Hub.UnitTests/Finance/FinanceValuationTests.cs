using CarWash.Hub.Application.Features.Finance;

namespace CarWash.Hub.UnitTests.Finance;

public class FinanceValuationTests
{
    [Fact]
    public void The_earlier_firmware_is_valued_at_the_list_price() =>
        FinanceValuation.Revenue(uses: 3, devicePricedUses: 0, deviceSales: 0, listPrice: 0.5m).ShouldBe(1.5m);

    [Fact]
    public void Uses_the_machine_priced_keep_its_price_whatever_the_list_says() =>
        FinanceValuation.Revenue(uses: 2, devicePricedUses: 2, deviceSales: 0.75m, listPrice: 9m).ShouldBe(0.75m);

    [Fact]
    public void A_mixed_day_values_only_the_rest_at_the_list_price() =>
        FinanceValuation.Revenue(uses: 3, devicePricedUses: 2, deviceSales: 1m, listPrice: 0.25m).ShouldBe(1.25m);

    [Fact]
    public void Only_uses_the_machine_did_not_price_can_be_unpriced()
    {
        FinanceValuation.UnpricedUses(uses: 3, devicePricedUses: 2, listPrice: null).ShouldBe(1);
        FinanceValuation.UnpricedUses(uses: 2, devicePricedUses: 2, listPrice: null).ShouldBe(0);
        FinanceValuation.UnpricedUses(uses: 3, devicePricedUses: 0, listPrice: 0.5m).ShouldBe(0);
        FinanceValuation.Revenue(uses: 3, devicePricedUses: 2, deviceSales: 1m, listPrice: null).ShouldBe(1m);
    }
}
