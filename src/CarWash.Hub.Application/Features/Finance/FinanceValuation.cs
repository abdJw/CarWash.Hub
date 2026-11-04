namespace CarWash.Hub.Application.Features.Finance;

/// <summary>Values one day's uses of one service on one machine.</summary>
/// <remarks>
/// Layer: Application. Pure.
/// <para>
/// <b>The machine's own price comes first.</b> A machine running the event firmware reports the
/// price it charged with every sale, and that is what the customer paid for - whatever the price
/// list said that day, and even if the list is changed afterwards. Only the uses it did not price
/// (the earlier firmware's) are valued at the station's list price in effect that business day.
/// </para>
/// </remarks>
public static class FinanceValuation
{
    /// <summary>Returns the revenue of a day's uses.</summary>
    /// <param name="uses">All the uses.</param>
    /// <param name="devicePricedUses">How many of them the machine priced.</param>
    /// <param name="deviceSales">The sum of those prices, in dinars.</param>
    /// <param name="listPrice">The station's list price that day; null when it had none.</param>
    /// <returns>The revenue, in dinars.</returns>
    public static decimal Revenue(int uses, int devicePricedUses, decimal deviceSales, decimal? listPrice) =>
        deviceSales + ((listPrice ?? 0) * Remainder(uses, devicePricedUses));

    /// <summary>Returns how many of a day's uses have no price at all.</summary>
    /// <param name="uses">All the uses.</param>
    /// <param name="devicePricedUses">How many of them the machine priced.</param>
    /// <param name="listPrice">The station's list price that day; null when it had none.</param>
    /// <returns>The unpriced uses.</returns>
    public static long UnpricedUses(int uses, int devicePricedUses, decimal? listPrice) =>
        listPrice is null ? Remainder(uses, devicePricedUses) : 0;

    private static int Remainder(int uses, int devicePricedUses) => Math.Max(0, uses - devicePricedUses);
}
