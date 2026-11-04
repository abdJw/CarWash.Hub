using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Common.Options;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Common.Validation;
using CarWash.Hub.Application.Features.Pricing;
using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Application.Features.Finance;

/// <summary>Reports what the stations earned: uses valued at each station's prices.</summary>
/// <remarks>
/// Layer: Application. Scoped like every read: a station user sees only their own station; the
/// read API key and a Super Admin see every station.
/// </remarks>
public interface IFinanceReportService
{
    /// <summary>Builds the finance report.</summary>
    /// <param name="query">The range and filters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Totals, the timeline and the breakdowns.</returns>
    Task<FinanceReportDto> GetReportAsync(FinanceReportQuery query, CancellationToken cancellationToken = default);

    /// <summary>Returns what the report's filters offer the caller.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stations, services, machines and today's date.</returns>
    Task<FinanceFiltersDto> GetFiltersAsync(CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IFinanceReportService"/>.</summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// Reads the daily rollup, never the raw uses: a year at thirty machines is some thirty thousand
/// rows. Each row is valued in memory at the price its station had for the service that day, so a
/// price change never touches what earlier days earned, and a use with no price is counted as
/// unpriced rather than free.
/// </para>
/// <para>
/// A use belongs to the station it was made at, which the worker stamped on it; a machine that
/// moved shows up once per station it earned at. Uses made while a machine was unassigned belong to
/// no station and are only counted, for a whole-fleet report.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="scopeProvider">The stations the caller may see.</param>
/// <param name="clock">Today, for elapsed days.</param>
/// <param name="timeZone">The business day today is.</param>
/// <param name="options">The first day of the week.</param>
/// <param name="validator">Validates the query.</param>
/// <param name="localizer">Names in the caller's language.</param>
public sealed class FinanceReportService(
    IUnitOfWork unitOfWork,
    IMachineAccessScopeProvider scopeProvider,
    IDateTimeProvider clock,
    IBusinessTimeZone timeZone,
    IOptions<DashboardOptions> options,
    IValidator<FinanceReportQuery> validator,
    ILocalizationService localizer) : IFinanceReportService
{
    /// <summary>The currency every price and amount is in.</summary>
    public const string Currency = "JOD";

    /// <inheritdoc />
    public async Task<FinanceReportDto> GetReportAsync(FinanceReportQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await validator.ValidateAndThrowLocalizedAsync(query, cancellationToken).ConfigureAwait(false);

        var scope = scopeProvider.GetScope();
        List<Guid>? stationIds = scope.GasStationId is { } own
            ? [own]
            : query.StationIds is { Count: > 0 } chosen ? [.. chosen.Distinct()] : null;
        var services = Normalize(query.Services);

        // The comparison period is as long as the part of the range that has happened: "this
        // month" on the 2nd is compared with the two days before it, not with a whole month.
        var today = timeZone.DateOf(clock.UtcNow);
        var lastElapsed = query.To < today ? query.To : today;
        var elapsedDays = Math.Max(0, lastElapsed.DayNumber - query.From.DayNumber + 1);
        var compareDays = elapsedDays > 0 ? elapsedDays : query.To.DayNumber - query.From.DayNumber + 1;
        var previousFrom = query.From.AddDays(-compareDays);
        var previousTo = query.From.AddDays(-1);

        var counts = await unitOfWork.Repository<ServiceUsageDaily, long>()
            .ListAsync(
                new FinanceDaysSpecification(previousFrom, query.To, stationIds, services, query.MachineId),
                row => new DayCount(
                    row.BusinessDate,
                    row.GasStationId!.Value,
                    row.MachineId,
                    row.ServiceCode,
                    row.UsageCount,
                    row.DevicePricedUses,
                    row.DeviceSalesAmount,
                    row.CashCollectedAmount),
                cancellationToken)
            .ConfigureAwait(false);

        var prices = await unitOfWork.Repository<StationServicePrice, Guid>()
            .ListAsync(
                new LivePricesSpecification(stationIds, services, until: query.To),
                price => new PricePoint(price.Id, price.GasStationId, price.ServiceCode, price.EffectiveFrom, price.UnitPrice),
                cancellationToken)
            .ConfigureAwait(false);

        var book = new PriceBook(prices);
        var valued = counts.Select(count => new ValuedCount(count, book.PriceAt(count.StationId, count.Service, count.Date))).ToList();
        var current = valued.Where(count => count.Date >= query.From).ToList();
        var previous = valued.Where(count => count.Date < query.From).ToList();

        var names = await NamesAsync(current, cancellationToken).ConfigureAwait(false);
        var totals = Totals(current, elapsedDays);
        var grain = FinancePeriods.Resolve(query.Grain, query.From, query.To);

        var periods = FinancePeriods.Buckets(query.From, query.To, grain, options.Value.FirstDayOfWeek)
            .Select(bucket =>
            {
                var inBucket = current.Where(count => count.Date >= bucket.Start && count.Date <= bucket.End).ToList();
                return new FinancePeriodDto(
                    bucket.Start,
                    bucket.End,
                    inBucket.Sum(count => (long)count.Uses),
                    inBucket.Sum(count => count.Revenue),
                    inBucket.Sum(count => count.UnpricedUses),
                    [
                        .. inBucket
                            .GroupBy(count => count.Service, StringComparer.OrdinalIgnoreCase)
                            .OrderBy(group => names.ServiceOrder(group.Key))
                            .Select(group => new FinanceServiceAmountDto(group.Key, group.Sum(count => (long)count.Uses), group.Sum(count => count.Revenue))),
                    ],
                    inBucket.Sum(count => count.Cash));
            })
            .ToList();

        var unassigned = scope.IsRestricted || stationIds is not null
            ? 0
            : (long)await unitOfWork.Repository<ServiceUsageDaily, long>()
                .SumAsync(new UnassignedDaysSpecification(query.From, query.To, services, query.MachineId), row => row.UsageCount, cancellationToken)
                .ConfigureAwait(false);

        return new FinanceReportDto(
            query.From,
            query.To,
            grain,
            Currency,
            totals,
            previousFrom,
            previousTo,
            Totals(previous, compareDays),
            periods,
            Lines(current, count => count.StationId, id => (id.ToString(), names.Station(id), names.StationCode(id)), totals),
            Lines(current, count => count.Service.ToLowerInvariant(), code => (code, names.Service(code), null), totals),
            Lines(
                current,
                count => (count.MachineId, count.StationId),
                key => ($"{key.MachineId}:{key.StationId}", names.Machine(key.MachineId), names.Station(key.StationId)),
                totals),
            unassigned,
            query.IncludeDetail ? Detail(current, names) : null);
    }

    /// <inheritdoc />
    public async Task<FinanceFiltersDto> GetFiltersAsync(CancellationToken cancellationToken = default)
    {
        var scope = scopeProvider.GetScope();

        var stations = await unitOfWork.Repository<GasStation, Guid>()
            .ListAsync(
                new FinanceStationsSpecification(scope.GasStationId),
                station => new { station.Id, station.Code, station.Name, station.NameAr, station.IsActive },
                cancellationToken)
            .ConfigureAwait(false);

        var machines = await unitOfWork.Repository<Machine, Guid>()
            .ListAsync(
                new FinanceMachinesSpecification(scope.GasStationId),
                machine => new { machine.Id, machine.Code, machine.DisplayName, machine.GasStationId },
                cancellationToken)
            .ConfigureAwait(false);

        var catalog = await unitOfWork.Repository<ServiceDefinition, Guid>()
            .ListAsync(
                new AllServiceDefinitionsSpecification(),
                service => new { service.Code, service.IsActive, service.IsDeleted, service.SortOrder },
                cancellationToken)
            .ConfigureAwait(false);

        return new FinanceFiltersDto(
            timeZone.DateOf(clock.UtcNow),
            options.Value.FirstDayOfWeek,
            Currency,
            !scope.IsRestricted,
            [.. stations.Select(station => new FinanceStationOptionDto(station.Id, station.Code, localizer.Localize(station.Name, station.NameAr), station.IsActive))],
            [
                .. catalog
                    .Where(service => service.IsActive && !service.IsDeleted)
                    .OrderBy(service => service.SortOrder)
                    .ThenBy(service => service.Code, StringComparer.Ordinal)
                    .Select(service => service.Code),
            ],
            [
                .. machines.Select(machine => new FinanceMachineOptionDto(
                    machine.Id,
                    string.IsNullOrWhiteSpace(machine.DisplayName) ? machine.Code : machine.DisplayName,
                    machine.Code,
                    machine.GasStationId)),
            ]);
    }

    private static List<string>? Normalize(IReadOnlyList<string>? services)
    {
        if (services is null)
        {
            return null;
        }

        List<string> codes = [.. services.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal)];
        return codes.Count == 0 ? null : codes;
    }

    private static FinanceTotalsDto Totals(List<ValuedCount> counts, int days)
    {
        var revenue = counts.Sum(count => count.Revenue);
        return new FinanceTotalsDto(
            counts.Sum(count => (long)count.Uses),
            revenue,
            counts.Sum(count => count.UnpricedUses),
            days,
            days == 0 ? 0 : Math.Round(revenue / days, 3, MidpointRounding.AwayFromZero),
            counts.Sum(count => count.Cash));
    }

    // Biggest earner first; while nothing is priced, the busiest.
    private static List<FinanceLineDto> Lines<TKey>(
        List<ValuedCount> counts,
        Func<ValuedCount, TKey> keyOf,
        Func<TKey, (string Key, string Name, string? Detail)> describe,
        FinanceTotalsDto totals)
        where TKey : notnull =>
    [
        .. counts
            .GroupBy(keyOf)
            .Select(group =>
            {
                var uses = group.Sum(count => (long)count.Uses);
                var revenue = group.Sum(count => count.Revenue);
                var (key, name, detail) = describe(group.Key);
                var share = totals.Revenue > 0 ? revenue / totals.Revenue : totals.Uses > 0 ? (decimal)uses / totals.Uses : 0;
                return new FinanceLineDto(
                    key, name, detail, uses, revenue, group.Sum(count => count.UnpricedUses), Math.Round(share, 4), group.Sum(count => count.Cash));
            })
            .OrderByDescending(line => line.Revenue)
            .ThenByDescending(line => line.Uses)
            .ThenBy(line => line.Name, StringComparer.CurrentCulture),
    ];

    private static List<FinanceDetailRowDto> Detail(List<ValuedCount> counts, Names names) =>
    [
        .. counts
            .OrderBy(count => count.Date)
            .ThenBy(count => names.Station(count.StationId), StringComparer.CurrentCulture)
            .ThenBy(count => names.Machine(count.MachineId), StringComparer.CurrentCulture)
            .ThenBy(count => names.ServiceOrder(count.Service))
            .Select(count => new FinanceDetailRowDto(
                count.Date,
                count.StationId,
                names.Station(count.StationId),
                count.MachineId,
                names.Machine(count.MachineId),
                names.MachineCode(count.MachineId),
                count.Service.ToLowerInvariant(),
                names.Service(count.Service),
                count.Uses,
                count.UnitPrice,
                count.Revenue,
                count.DevicePricedUses,
                count.Cash)),
    ];

    // Deleted stations and machines included: the money they earned is still reported, by name.
    private async Task<Names> NamesAsync(List<ValuedCount> counts, CancellationToken cancellationToken)
    {
        var stationIds = counts.Select(count => count.StationId).Distinct().ToList();
        var machineIds = counts.Select(count => count.MachineId).Distinct().ToList();

        var stations = stationIds.Count == 0
            ? []
            : await unitOfWork.Repository<GasStation, Guid>()
                .ListAsync(
                    new StationsByIdsSpecification(stationIds),
                    station => new { station.Id, station.Code, station.Name, station.NameAr },
                    cancellationToken)
                .ConfigureAwait(false);

        var machines = machineIds.Count == 0
            ? []
            : await unitOfWork.Repository<Machine, Guid>()
                .ListAsync(new MachinesByIdsSpecification(machineIds), machine => new { machine.Id, machine.Code, machine.DisplayName }, cancellationToken)
                .ConfigureAwait(false);

        var catalog = await unitOfWork.Repository<ServiceDefinition, Guid>()
            .ListAsync(
                new AllServiceDefinitionsSpecification(),
                service => new { service.Code, service.Name, service.NameAr, service.SortOrder },
                cancellationToken)
            .ConfigureAwait(false);

        return new Names(
            stations.ToDictionary(station => station.Id, station => (station.Code, Name: localizer.Localize(station.Name, station.NameAr))),
            machines.ToDictionary(machine => machine.Id, machine => (machine.Code, Name: string.IsNullOrWhiteSpace(machine.DisplayName) ? machine.Code : machine.DisplayName)),
            catalog.ToDictionary(
                service => service.Code,
                service => (Name: localizer.Localize(service.Name, service.NameAr), service.SortOrder),
                StringComparer.OrdinalIgnoreCase));
    }

    private sealed record DayCount(
        DateOnly Date, Guid StationId, Guid MachineId, string Service, int Uses, int DevicePricedUses, decimal DeviceSales, decimal Cash);

    private sealed class ValuedCount(DayCount count, decimal? unitPrice)
    {
        public DateOnly Date => count.Date;

        public Guid StationId => count.StationId;

        public Guid MachineId => count.MachineId;

        public string Service => count.Service;

        public int Uses => count.Uses;

        public decimal? UnitPrice => unitPrice;

        public int DevicePricedUses => count.DevicePricedUses;

        public decimal Cash => count.Cash;

        public decimal Revenue => FinanceValuation.Revenue(count.Uses, count.DevicePricedUses, count.DeviceSales, unitPrice);

        public long UnpricedUses => FinanceValuation.UnpricedUses(count.Uses, count.DevicePricedUses, unitPrice);
    }

    private sealed class Names(
        Dictionary<Guid, (string Code, string Name)> stations,
        Dictionary<Guid, (string Code, string Name)> machines,
        Dictionary<string, (string Name, int SortOrder)> services)
    {
        public string Station(Guid id) => stations.TryGetValue(id, out var station) ? station.Name : id.ToString();

        public string? StationCode(Guid id) => stations.TryGetValue(id, out var station) ? station.Code : null;

        public string Machine(Guid id) => machines.TryGetValue(id, out var machine) ? machine.Name : id.ToString();

        public string MachineCode(Guid id) => machines.TryGetValue(id, out var machine) ? machine.Code : string.Empty;

        public string Service(string code) => services.TryGetValue(code, out var service) ? service.Name : code;

        public int ServiceOrder(string code) => services.TryGetValue(code, out var service) ? service.SortOrder : int.MaxValue;
    }
}

/// <summary>Validates a finance report's range.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class FinanceReportQueryValidator : AbstractValidator<FinanceReportQuery>
{
    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public FinanceReportQueryValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(query => query.From)
            .NotEmpty().WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Required, localizer.GetString(ResourceKeys.Fields.From)));

        RuleFor(query => query.To)
            .NotEmpty().WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Required, localizer.GetString(ResourceKeys.Fields.To)));

        RuleFor(query => query.To)
            .Must((query, to) => query.From <= to)
            .WithMessage(_ => localizer.GetString(
                ResourceKeys.Validation.DateRange,
                localizer.GetString(ResourceKeys.Fields.From),
                localizer.GetString(ResourceKeys.Fields.To)))
            .Must((query, to) => to.DayNumber - query.From.DayNumber <= FinanceReportQuery.MaxSpanDays)
            .WithMessage(_ => localizer.GetString(
                ResourceKeys.Validation.RangeTooLong,
                localizer.GetString(ResourceKeys.Fields.From),
                localizer.GetString(ResourceKeys.Fields.To),
                FinanceReportQuery.MaxSpanDays))
            .When(query => query.From != default && query.To != default);

        RuleFor(query => query.Grain).IsInEnum()
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.MalformedRequest));
    }
}

/// <summary>The daily counts of stations in a range, filtered as the report asks.</summary>
/// <remarks>Layer: Application. Unassigned uses are left out: they belong to no station.</remarks>
public sealed class FinanceDaysSpecification : BaseSpecification<ServiceUsageDaily>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="stationIds">The stations; null for every station.</param>
    /// <param name="services">The service codes; null for every service.</param>
    /// <param name="machineId">One machine; null for every machine.</param>
    public FinanceDaysSpecification(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? stationIds, IReadOnlyCollection<string>? services, Guid? machineId)
        : base(row => row.GasStationId != null && row.BusinessDate >= from && row.BusinessDate <= to)
    {
        if (stationIds is not null)
        {
            AddCriteria(row => stationIds.Contains(row.GasStationId!.Value));
        }

        if (services is not null)
        {
            AddCriteria(row => services.Contains(row.ServiceCode));
        }

        if (machineId is { } machine)
        {
            AddCriteria(row => row.MachineId == machine);
        }
    }
}

/// <summary>The daily counts of unassigned machines in a range.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class UnassignedDaysSpecification : BaseSpecification<ServiceUsageDaily>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="services">The service codes; null for every service.</param>
    /// <param name="machineId">One machine; null for every machine.</param>
    public UnassignedDaysSpecification(DateOnly from, DateOnly to, IReadOnlyCollection<string>? services, Guid? machineId)
        : base(row => row.GasStationId == null && row.BusinessDate >= from && row.BusinessDate <= to)
    {
        if (services is not null)
        {
            AddCriteria(row => services.Contains(row.ServiceCode));
        }

        if (machineId is { } machine)
        {
            AddCriteria(row => row.MachineId == machine);
        }
    }
}

/// <summary>Some stations by key, deleted ones included.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class StationsByIdsSpecification : BaseSpecification<GasStation>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="ids">The stations.</param>
    public StationsByIdsSpecification(IReadOnlyCollection<Guid> ids)
        : base(station => ids.Contains(station.Id))
    {
        IncludeSoftDeleted();
    }
}

/// <summary>The stations a finance report can be filtered to: one, or all; active first.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class FinanceStationsSpecification : BaseSpecification<GasStation>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="only">The caller's own station; null for every station.</param>
    public FinanceStationsSpecification(Guid? only)
    {
        if (only is { } id)
        {
            AddCriteria(station => station.Id == id);
        }

        AddOrderBy(station => station.IsActive, descending: true);
        AddOrderBy(station => station.Code);
    }
}

/// <summary>The machines standing at one station, or at any.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class FinanceMachinesSpecification : BaseSpecification<Machine>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="only">The caller's own station; null for every station.</param>
    public FinanceMachinesSpecification(Guid? only)
        : base(machine => machine.GasStationId != null)
    {
        if (only is { } id)
        {
            AddCriteria(machine => machine.GasStationId == id);
        }

        AddOrderBy(machine => machine.DisplayName ?? machine.Code);
    }
}
