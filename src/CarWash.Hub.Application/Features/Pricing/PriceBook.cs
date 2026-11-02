namespace CarWash.Hub.Application.Features.Pricing;

/// <summary>One dated price: from <paramref name="EffectiveFrom"/> on, one use costs <paramref name="UnitPrice"/>.</summary>
/// <param name="Id">The price row.</param>
/// <param name="StationId">The station.</param>
/// <param name="Service">The service code.</param>
/// <param name="EffectiveFrom">The first business day it applies to.</param>
/// <param name="UnitPrice">The price of one use, in dinars.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record PricePoint(Guid Id, Guid StationId, string Service, DateOnly EffectiveFrom, decimal UnitPrice);

/// <summary>
/// Answers "what did one use cost here on that day" for a set of dated prices.
/// </summary>
/// <remarks>
/// Layer: Application. Pure and in memory: a report loads the few price rows it needs once, then
/// values every daily count against them. The price on a day is the newest one that had started by
/// then; before a station's first price for a service, a use has no price at all - unpriced, which
/// is not the same as free.
/// </remarks>
public sealed class PriceBook
{
    private readonly Dictionary<(Guid StationId, string Service), PricePoint[]> _prices;

    /// <summary>Initializes a new instance of the <see cref="PriceBook"/> class.</summary>
    /// <param name="prices">The live (not deleted) prices, in any order.</param>
    public PriceBook(IEnumerable<PricePoint> prices)
    {
        ArgumentNullException.ThrowIfNull(prices);

        _prices = prices
            .GroupBy(price => (price.StationId, Normalize(price.Service)))
            .ToDictionary(group => group.Key, group => group.OrderBy(price => price.EffectiveFrom).ToArray());
    }

    /// <summary>Returns the price in effect on a day.</summary>
    /// <param name="stationId">The station.</param>
    /// <param name="service">The service code.</param>
    /// <param name="day">The business day.</param>
    /// <returns>The price row, or null when none had started by then.</returns>
    public PricePoint? EntryAt(Guid stationId, string service, DateOnly day)
    {
        if (!_prices.TryGetValue((stationId, Normalize(service)), out var prices))
        {
            return null;
        }

        for (var index = prices.Length - 1; index >= 0; index--)
        {
            if (prices[index].EffectiveFrom <= day)
            {
                return prices[index];
            }
        }

        return null;
    }

    /// <summary>Returns the price of one use on a day.</summary>
    /// <param name="stationId">The station.</param>
    /// <param name="service">The service code.</param>
    /// <param name="day">The business day.</param>
    /// <returns>The price, or null when the use is unpriced.</returns>
    public decimal? PriceAt(Guid stationId, string service, DateOnly day) => EntryAt(stationId, service, day)?.UnitPrice;

    /// <summary>Returns the first price scheduled to start after a day.</summary>
    /// <param name="stationId">The station.</param>
    /// <param name="service">The service code.</param>
    /// <param name="day">The business day.</param>
    /// <returns>The upcoming price row, or null.</returns>
    public PricePoint? NextAfter(Guid stationId, string service, DateOnly day) =>
        _prices.TryGetValue((stationId, Normalize(service)), out var prices)
            ? Array.Find(prices, price => price.EffectiveFrom > day)
            : null;

    private static string Normalize(string service) => service.Trim().ToLowerInvariant();
}
