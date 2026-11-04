namespace CarWash.Hub.Application.Features.Finance;

/// <summary>How the finance report's timeline is bucketed.</summary>
/// <remarks>Layer: Application.</remarks>
public enum FinanceGrain
{
    /// <summary>Chosen from the range: days up to a month, weeks up to four months, months beyond.</summary>
    Auto = 0,

    /// <summary>One bucket per business day.</summary>
    Day = 1,

    /// <summary>One bucket per week, starting on the configured first day of the week.</summary>
    Week = 2,

    /// <summary>One bucket per calendar month.</summary>
    Month = 3,
}

/// <summary>What the finance report covers.</summary>
/// <remarks>
/// Layer: Application. A station user always gets their own station, whatever is asked for; the
/// read API key and a Super Admin choose freely, and no stations means all of them.
/// </remarks>
public sealed record FinanceReportQuery
{
    /// <summary>The longest range accepted, in days between the first and the last day.</summary>
    public const int MaxSpanDays = 366;

    /// <summary>Gets the first business day, inclusive.</summary>
    public DateOnly From { get; init; }

    /// <summary>Gets the last business day, inclusive.</summary>
    public DateOnly To { get; init; }

    /// <summary>Gets the stations; empty or null for every station.</summary>
    public IReadOnlyList<Guid>? StationIds { get; init; }

    /// <summary>Gets the service codes; empty or null for every service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Gets one machine to report on; null for every machine.</summary>
    public Guid? MachineId { get; init; }

    /// <summary>Gets how the timeline is bucketed.</summary>
    public FinanceGrain Grain { get; init; }

    /// <summary>Gets a value indicating whether to include one row per day, station, machine and service.</summary>
    public bool IncludeDetail { get; init; }
}

/// <summary>Totals over a period.</summary>
/// <param name="Uses">Every use, priced or not.</param>
/// <param name="Revenue">
/// The value of the priced uses, in dinars: the price each machine charged where it reports one,
/// otherwise the station's price list.
/// </param>
/// <param name="UnpricedUses">Uses with no price at all: the machine reported none and the station had none that day.</param>
/// <param name="Days">The days elapsed in the period, which the average is over.</param>
/// <param name="AveragePerDay">Revenue per elapsed day.</param>
/// <param name="CashCollected">
/// The cash customers inserted, in dinars, as reported by the machines that report it. It can exceed
/// the revenue (overpayment), and it covers reporting machines only.
/// </param>
/// <remarks>Layer: Application.</remarks>
public sealed record FinanceTotalsDto(long Uses, decimal Revenue, long UnpricedUses, int Days, decimal AveragePerDay, decimal CashCollected = 0);

/// <summary>One service's share of a bucket.</summary>
/// <param name="Service">The service code.</param>
/// <param name="Uses">Its uses.</param>
/// <param name="Revenue">Their value, in dinars.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record FinanceServiceAmountDto(string Service, long Uses, decimal Revenue);

/// <summary>One bucket of the timeline.</summary>
/// <param name="Start">Its first day.</param>
/// <param name="End">Its last day, inclusive; clipped to the report's range.</param>
/// <param name="Uses">Its uses.</param>
/// <param name="Revenue">Their value, in dinars.</param>
/// <param name="UnpricedUses">Its unpriced uses.</param>
/// <param name="Services">The split by service, in display order, services with no uses left out.</param>
/// <param name="CashCollected">The cash reported for it, in dinars.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record FinancePeriodDto(
    DateOnly Start, DateOnly End, long Uses, decimal Revenue, long UnpricedUses, IReadOnlyList<FinanceServiceAmountDto> Services, decimal CashCollected = 0);

/// <summary>One line of a breakdown: a station, a service, or a machine at a station.</summary>
/// <param name="Key">The station id, the service code, or <c>machineId:stationId</c>.</param>
/// <param name="Name">The name in the caller's language.</param>
/// <param name="Detail">Secondary text: a station's code, or the station a machine stood at.</param>
/// <param name="Uses">Its uses.</param>
/// <param name="Revenue">Their value, in dinars.</param>
/// <param name="UnpricedUses">Its unpriced uses.</param>
/// <param name="Share">Its share of the revenue, from 0 to 1 - of the uses when nothing is priced.</param>
/// <param name="CashCollected">The cash reported for it, in dinars.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record FinanceLineDto(
    string Key, string Name, string? Detail, long Uses, decimal Revenue, long UnpricedUses, decimal Share, decimal CashCollected = 0);

/// <summary>One day of one service on one machine at one station, with its price.</summary>
/// <param name="Date">The business day.</param>
/// <param name="StationId">The station.</param>
/// <param name="Station">Its name.</param>
/// <param name="MachineId">The machine.</param>
/// <param name="Machine">Its name.</param>
/// <param name="MachineCode">Its code (MAC address).</param>
/// <param name="Service">The service code.</param>
/// <param name="ServiceName">Its name.</param>
/// <param name="Uses">The uses.</param>
/// <param name="UnitPrice">The station's list price that day, applied to the uses the machine did not price; null when it had none.</param>
/// <param name="Revenue">Their value, in dinars.</param>
/// <param name="DevicePricedUses">How many of the uses the machine priced itself.</param>
/// <param name="CashCollected">The cash the machine reported for them, in dinars.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record FinanceDetailRowDto(
    DateOnly Date, Guid StationId, string Station, Guid MachineId, string Machine, string MachineCode,
    string Service, string ServiceName, long Uses, decimal? UnitPrice, decimal Revenue,
    long DevicePricedUses = 0, decimal CashCollected = 0);

/// <summary>The finance report.</summary>
/// <param name="From">The first day.</param>
/// <param name="To">The last day.</param>
/// <param name="Grain">The timeline's bucket size, resolved.</param>
/// <param name="Currency">The ISO currency code - always JOD.</param>
/// <param name="Totals">The range's totals.</param>
/// <param name="PreviousFrom">The first day of the comparison period: as many days as have elapsed, just before.</param>
/// <param name="PreviousTo">Its last day.</param>
/// <param name="Previous">Its totals.</param>
/// <param name="Periods">The timeline, every bucket of the range, empty ones included.</param>
/// <param name="Stations">One line per station, by revenue.</param>
/// <param name="Services">One line per service, by revenue.</param>
/// <param name="Machines">One line per machine and station, by revenue.</param>
/// <param name="UnassignedUses">Uses on machines that belonged to no station - counted only for a whole-fleet report.</param>
/// <param name="Detail">One row per day, station, machine and service; null unless asked for.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record FinanceReportDto(
    DateOnly From,
    DateOnly To,
    FinanceGrain Grain,
    string Currency,
    FinanceTotalsDto Totals,
    DateOnly PreviousFrom,
    DateOnly PreviousTo,
    FinanceTotalsDto Previous,
    IReadOnlyList<FinancePeriodDto> Periods,
    IReadOnlyList<FinanceLineDto> Stations,
    IReadOnlyList<FinanceLineDto> Services,
    IReadOnlyList<FinanceLineDto> Machines,
    long UnassignedUses,
    IReadOnlyList<FinanceDetailRowDto>? Detail);

/// <summary>A station the report can be filtered to.</summary>
/// <param name="Id">The station.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name in the caller's language.</param>
/// <param name="IsActive">Whether it is in service.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record FinanceStationOptionDto(Guid Id, string Code, string Name, bool IsActive);

/// <summary>A machine the report can be filtered to.</summary>
/// <param name="Id">The machine.</param>
/// <param name="Name">Its display name, or its code.</param>
/// <param name="Code">Its code (MAC address).</param>
/// <param name="StationId">The station it stands at now.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record FinanceMachineOptionDto(Guid Id, string Name, string Code, Guid? StationId);

/// <summary>What the finance report's filters offer the caller.</summary>
/// <param name="Today">Today's business date, for the date presets.</param>
/// <param name="FirstDayOfWeek">The day weeks start on.</param>
/// <param name="Currency">The ISO currency code.</param>
/// <param name="AllStations">Whether the caller may choose stations; false for a station user.</param>
/// <param name="Stations">The stations the caller may see.</param>
/// <param name="Services">The active service codes, in display order.</param>
/// <param name="Machines">The machines standing at those stations now.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record FinanceFiltersDto(
    DateOnly Today,
    DayOfWeek FirstDayOfWeek,
    string Currency,
    bool AllStations,
    IReadOnlyList<FinanceStationOptionDto> Stations,
    IReadOnlyList<string> Services,
    IReadOnlyList<FinanceMachineOptionDto> Machines);
