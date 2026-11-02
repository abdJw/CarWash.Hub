using CarWash.Hub.Application.Features.Pricing;

namespace CarWash.Hub.UnitTests.Finance;

/// <summary>
/// The price of a use on a day: the newest price that had started by then, per station and
/// service - and none at all before the first.
/// </summary>
public class PriceBookTests
{
    private static readonly Guid StationA = Guid.NewGuid();
    private static readonly Guid StationB = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 11, 2);

    private static PricePoint Price(Guid station, string service, DateOnly from, decimal price) =>
        new(Guid.NewGuid(), station, service, from, price);

    [Fact]
    public void Before_the_first_price_a_use_is_unpriced()
    {
        var book = new PriceBook([Price(StationA, "soap", Day, 0.5m)]);

        book.PriceAt(StationA, "soap", Day.AddDays(-1)).ShouldBeNull();
        book.PriceAt(StationA, "water", Day).ShouldBeNull();
    }

    [Fact]
    public void A_price_applies_from_its_first_day_on()
    {
        var book = new PriceBook([Price(StationA, "soap", Day, 0.5m)]);

        book.PriceAt(StationA, "soap", Day).ShouldBe(0.5m);
        book.PriceAt(StationA, "soap", Day.AddDays(400)).ShouldBe(0.5m);
    }

    [Fact]
    public void A_newer_price_takes_over_without_changing_earlier_days()
    {
        var book = new PriceBook(
        [
            Price(StationA, "soap", Day.AddDays(10), 0.75m),
            Price(StationA, "soap", Day, 0.5m),
        ]);

        book.PriceAt(StationA, "soap", Day.AddDays(9)).ShouldBe(0.5m);
        book.PriceAt(StationA, "soap", Day.AddDays(10)).ShouldBe(0.75m);
    }

    [Fact]
    public void Stations_and_services_are_priced_separately()
    {
        var book = new PriceBook(
        [
            Price(StationA, "soap", Day, 0.5m),
            Price(StationB, "soap", Day, 0.6m),
            Price(StationA, "water", Day, 0.25m),
        ]);

        book.PriceAt(StationA, "soap", Day).ShouldBe(0.5m);
        book.PriceAt(StationB, "soap", Day).ShouldBe(0.6m);
        book.PriceAt(StationA, "water", Day).ShouldBe(0.25m);
        book.PriceAt(StationB, "water", Day).ShouldBeNull();
    }

    [Fact]
    public void Service_codes_match_whatever_their_case()
    {
        var book = new PriceBook([Price(StationA, "Soap", Day, 0.5m)]);

        book.PriceAt(StationA, "SOAP", Day).ShouldBe(0.5m);
    }

    [Fact]
    public void A_free_service_is_priced_at_zero_not_unpriced()
    {
        var book = new PriceBook([Price(StationA, "vacuum", Day, 0m)]);

        book.PriceAt(StationA, "vacuum", Day).ShouldBe(0m);
    }

    [Fact]
    public void The_next_price_is_the_first_one_scheduled_after_the_day()
    {
        var current = Price(StationA, "soap", Day, 0.5m);
        var next = Price(StationA, "soap", Day.AddDays(5), 0.6m);
        var later = Price(StationA, "soap", Day.AddDays(9), 0.7m);
        var book = new PriceBook([later, current, next]);

        book.EntryAt(StationA, "soap", Day).ShouldBe(current);
        book.NextAfter(StationA, "soap", Day).ShouldBe(next);
        book.NextAfter(StationA, "soap", Day.AddDays(9)).ShouldBeNull();
    }
}
