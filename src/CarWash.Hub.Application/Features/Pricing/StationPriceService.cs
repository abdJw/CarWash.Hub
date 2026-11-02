using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Common.Validation;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Admin;
using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Application.Features.Refills;
using CarWash.Hub.Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace CarWash.Hub.Application.Features.Pricing;

/// <summary>One dated price, as the price list shows it.</summary>
/// <param name="Id">The price row.</param>
/// <param name="UnitPrice">The price of one use, in dinars.</param>
/// <param name="EffectiveFrom">The first business day it applies to.</param>
/// <param name="Note">Why it was set, if anyone said.</param>
/// <param name="SetBy">Who set it last; null when unknown.</param>
/// <param name="SetAt">When it was set last.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record PriceEntryDto(Guid Id, decimal UnitPrice, DateOnly EffectiveFrom, string? Note, string? SetBy, DateTimeOffset SetAt);

/// <summary>A service's prices at one station.</summary>
/// <param name="Service">The service code.</param>
/// <param name="Current">The price today; null when uses are unpriced.</param>
/// <param name="Next">The next price scheduled to start, if any.</param>
/// <param name="History">Every live price, newest start first.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ServicePricesDto(string Service, PriceEntryDto? Current, PriceEntryDto? Next, IReadOnlyList<PriceEntryDto> History);

/// <summary>A station's price list.</summary>
/// <param name="StationId">The station.</param>
/// <param name="StationName">Its name in the caller's language.</param>
/// <param name="Today">Today's business date, which "current" is measured on.</param>
/// <param name="Services">Every active service, and any retired one that has prices, in display order.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record StationPricesDto(Guid StationId, string StationName, DateOnly Today, IReadOnlyList<ServicePricesDto> Services);

/// <summary>A service's price today.</summary>
/// <param name="Service">The service code.</param>
/// <param name="UnitPrice">The price of one use, in dinars.</param>
/// <param name="EffectiveFrom">Since when.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record CurrentPriceDto(string Service, decimal UnitPrice, DateOnly EffectiveFrom);

/// <summary>A station's prices today.</summary>
/// <param name="StationId">The station.</param>
/// <param name="Prices">Its priced services; a service missing here is unpriced.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record StationCurrentPricesDto(Guid StationId, IReadOnlyList<CurrentPriceDto> Prices);

/// <summary>A price to set for a service at a station, from a business day on.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed record SetStationPriceRequest
{
    /// <summary>Gets the station.</summary>
    public Guid StationId { get; init; }

    /// <summary>Gets the service code.</summary>
    public string Service { get; init; } = string.Empty;

    /// <summary>Gets the price of one use, in dinars, to the fils.</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>
    /// Gets the first business day the price applies to. A day that already has a price replaces
    /// it; a past day revalues the uses since then.
    /// </summary>
    public DateOnly EffectiveFrom { get; init; }

    /// <summary>Gets an optional note, such as why the price changed.</summary>
    public string? Note { get; init; }
}

/// <summary>Reads and sets the price of each service at each station.</summary>
/// <remarks>Layer: Application. Super Admins set prices; a station user may read its own station's.</remarks>
public interface IStationPriceService
{
    /// <summary>Returns a station's price list: current, upcoming and past prices per service.</summary>
    /// <param name="stationId">The station.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The price list.</returns>
    /// <exception cref="NotFoundException">No such station, or not the caller's.</exception>
    Task<StationPricesDto> GetAsync(Guid stationId, CancellationToken cancellationToken = default);

    /// <summary>Returns today's prices of every station the caller may see that has any.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per priced station.</returns>
    Task<IReadOnlyList<StationCurrentPricesDto>> ListCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets a price from a day on, replacing one already set for that day.</summary>
    /// <param name="request">The price.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The price row.</returns>
    Task<Guid> SetAsync(SetStationPriceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes a price entered by mistake; the one before it applies again.</summary>
    /// <param name="priceId">The price row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when it is removed.</returns>
    Task DeleteAsync(Guid priceId, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IStationPriceService"/>.</summary>
/// <remarks>
/// Layer: Application. Prices are never edited in place except on the same day: a change is a new
/// dated row, so reports value each day at the price that applied then. Removal is a soft delete, so
/// who set and who removed a price stays on record.
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="currentUser">Must be a Super Admin to change prices.</param>
/// <param name="scopeProvider">The stations the caller may see.</param>
/// <param name="clock">Today.</param>
/// <param name="timeZone">The business day today is.</param>
/// <param name="validator">Validates a price.</param>
/// <param name="localizer">Messages and names.</param>
/// <param name="logger">Logger.</param>
public sealed class StationPriceService(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    IMachineAccessScopeProvider scopeProvider,
    IDateTimeProvider clock,
    IBusinessTimeZone timeZone,
    IValidator<SetStationPriceRequest> validator,
    ILocalizationService localizer,
    ILogger<StationPriceService> logger) : IStationPriceService
{
    /// <summary>How many years back a price may start - far enough to price the whole history.</summary>
    public const int MaxBackdateYears = 5;

    /// <summary>How many days ahead a price may be scheduled.</summary>
    public const int MaxDaysAhead = 366;

    /// <inheritdoc />
    public async Task<StationPricesDto> GetAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        var scope = scopeProvider.GetScope();

        // Not found rather than forbidden for another station: its existence is not revealed.
        var station = (scope.IsRestricted && scope.GasStationId != stationId
                          ? null
                          : await unitOfWork.Repository<GasStation, Guid>().GetByIdAsync(stationId, cancellationToken).ConfigureAwait(false))
                      ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Station), stationId);

        var today = timeZone.DateOf(clock.UtcNow);

        var prices = await unitOfWork.Repository<StationServicePrice, Guid>()
            .ListAsync(new LivePricesSpecification([stationId]), cancellationToken)
            .ConfigureAwait(false);

        var authorIds = prices.Select(price => price.ModifiedBy ?? price.CreatedBy).OfType<Guid>().Distinct().ToList();
        var authors = authorIds.Count == 0
            ? []
            : (await unitOfWork.Repository<User, Guid>()
                    .ListAsync(new UsersByIdsSpecification(authorIds), user => new { user.Id, user.DisplayName }, cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(user => user.Id, user => user.DisplayName);

        var catalog = await unitOfWork.Repository<ServiceDefinition, Guid>()
            .ListAsync(
                new AllServiceDefinitionsSpecification(),
                service => new { service.Code, service.IsActive, service.IsDeleted, service.SortOrder },
                cancellationToken)
            .ConfigureAwait(false);

        var book = new PriceBook(prices.Select(ToPoint));
        var priced = prices.Select(price => price.ServiceCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var services = catalog
            .Where(service => (service.IsActive && !service.IsDeleted) || priced.Contains(service.Code))
            .OrderBy(service => service.SortOrder)
            .ThenBy(service => service.Code, StringComparer.Ordinal)
            .Select(service =>
            {
                var history = prices
                    .Where(price => string.Equals(price.ServiceCode, service.Code, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(price => price.EffectiveFrom)
                    .Select(price => ToEntry(price, authors))
                    .ToList();

                return new ServicePricesDto(
                    service.Code,
                    Find(history, book.EntryAt(stationId, service.Code, today)),
                    Find(history, book.NextAfter(stationId, service.Code, today)),
                    history);
            })
            .ToList();

        return new StationPricesDto(station.Id, localizer.Localize(station.Name, station.NameAr), today, services);

        static PriceEntryDto? Find(List<PriceEntryDto> history, PricePoint? point) =>
            point is null ? null : history.Find(entry => entry.Id == point.Id);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StationCurrentPricesDto>> ListCurrentAsync(CancellationToken cancellationToken = default)
    {
        var scope = scopeProvider.GetScope();
        var today = timeZone.DateOf(clock.UtcNow);

        var prices = await unitOfWork.Repository<StationServicePrice, Guid>()
            .ListAsync(
                new LivePricesSpecification(scope.GasStationId is { } own ? [own] : null, until: today),
                price => new PricePoint(price.Id, price.GasStationId, price.ServiceCode, price.EffectiveFrom, price.UnitPrice),
                cancellationToken)
            .ConfigureAwait(false);

        var book = new PriceBook(prices);

        return
        [
            .. prices
                .GroupBy(price => price.StationId)
                .Select(station => new StationCurrentPricesDto(
                    station.Key,
                    [
                        .. station
                            .Select(price => price.Service)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Select(service => book.EntryAt(station.Key, service, today)!)
                            .Select(price => new CurrentPriceDto(price.Service, price.UnitPrice, price.EffectiveFrom)),
                    ])),
        ];
    }

    /// <inheritdoc />
    public async Task<Guid> SetAsync(SetStationPriceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AdminRules.EnsureSuperAdmin(currentUser);
        await validator.ValidateAndThrowLocalizedAsync(request, cancellationToken).ConfigureAwait(false);

        var today = timeZone.DateOf(clock.UtcNow);
        if (request.EffectiveFrom < today.AddYears(-MaxBackdateYears) || request.EffectiveFrom > today.AddDays(MaxDaysAhead))
        {
            throw new Exceptions.ValidationException(
                nameof(SetStationPriceRequest.EffectiveFrom),
                localizer.GetString(ResourceKeys.Validation.PriceDate, MaxBackdateYears, MaxDaysAhead));
        }

        _ = await unitOfWork.Repository<GasStation, Guid>().GetByIdAsync(request.StationId, cancellationToken).ConfigureAwait(false)
            ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Station), request.StationId);

        var code = request.Service.Trim().ToLowerInvariant();
        if (!await unitOfWork.Repository<ServiceDefinition, Guid>().ExistsAsync(service => service.Code == code, cancellationToken).ConfigureAwait(false))
        {
            throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Service), code);
        }

        var repository = unitOfWork.Repository<StationServicePrice, Guid>();
        var price = await repository
            .FirstOrDefaultAsync(new PriceOnDaySpecification(request.StationId, code, request.EffectiveFrom), cancellationToken)
            .ConfigureAwait(false);

        if (price is null)
        {
            price = new StationServicePrice
            {
                Id = Guid.CreateVersion7(),
                GasStationId = request.StationId,
                ServiceCode = code,
                EffectiveFrom = request.EffectiveFrom,
            };
            await repository.AddAsync(price, cancellationToken).ConfigureAwait(false);
        }

        price.UnitPrice = request.UnitPrice;
        price.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Price of {Service} at station {StationId} set to {UnitPrice} JOD from {EffectiveFrom} by {UserName}",
            code, request.StationId, request.UnitPrice, request.EffectiveFrom, currentUser.UserName);

        return price.Id;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid priceId, CancellationToken cancellationToken = default)
    {
        AdminRules.EnsureSuperAdmin(currentUser);

        var repository = unitOfWork.Repository<StationServicePrice, Guid>();
        var price = await repository.GetByIdAsync(priceId, cancellationToken).ConfigureAwait(false)
                    ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Price), priceId);

        repository.Remove(price);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Price of {Service} at station {StationId} from {EffectiveFrom} removed by {UserName}",
            price.ServiceCode, price.GasStationId, price.EffectiveFrom, currentUser.UserName);
    }

    private static PricePoint ToPoint(StationServicePrice price) =>
        new(price.Id, price.GasStationId, price.ServiceCode, price.EffectiveFrom, price.UnitPrice);

    private static PriceEntryDto ToEntry(StationServicePrice price, Dictionary<Guid, string> authors) =>
        new(
            price.Id,
            price.UnitPrice,
            price.EffectiveFrom,
            price.Note,
            (price.ModifiedBy ?? price.CreatedBy) is { } author ? authors.GetValueOrDefault(author) : null,
            price.ModifiedAt ?? price.CreatedAt);
}

/// <summary>Validates a price.</summary>
/// <remarks>Layer: Application. The date window needs today, so the service checks it.</remarks>
public sealed class SetStationPriceRequestValidator : AbstractValidator<SetStationPriceRequest>
{
    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public SetStationPriceRequestValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(request => request.StationId)
            .NotEmpty().WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Required, localizer.GetString(ResourceKeys.Fields.Station)));

        RuleFor(request => request.Service).Required(localizer, ResourceKeys.Fields.Service, ServiceDefinition.CodeMaxLength);

        RuleFor(request => request.UnitPrice)
            .InclusiveBetween(0m, StationServicePrice.MaxUnitPrice)
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Range, localizer.GetString(ResourceKeys.Fields.Price), 0, StationServicePrice.MaxUnitPrice))
            .PrecisionScale(9, 3, ignoreTrailingZeros: true)
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.PriceDecimals, localizer.GetString(ResourceKeys.Fields.Price)));

        RuleFor(request => request.EffectiveFrom)
            .NotEmpty().WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Required, localizer.GetString(ResourceKeys.Fields.EffectiveFrom)));

        RuleFor(request => request.Note).Optional(localizer, ResourceKeys.Fields.Notes, StationServicePrice.NoteMaxLength);
    }
}

/// <summary>The live prices of some stations - or all - optionally only those started by a day.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class LivePricesSpecification : BaseSpecification<StationServicePrice>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="stationIds">The stations; null for every station.</param>
    /// <param name="services">The service codes; null for every service.</param>
    /// <param name="until">The last start day included; null for scheduled prices too.</param>
    public LivePricesSpecification(IReadOnlyCollection<Guid>? stationIds, IReadOnlyCollection<string>? services = null, DateOnly? until = null)
    {
        if (stationIds is not null)
        {
            AddCriteria(price => stationIds.Contains(price.GasStationId));
        }

        if (services is not null)
        {
            AddCriteria(price => services.Contains(price.ServiceCode));
        }

        if (until is { } last)
        {
            AddCriteria(price => price.EffectiveFrom <= last);
        }
    }
}

/// <summary>The live price of a service at a station starting on a given day, tracked to be replaced.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class PriceOnDaySpecification : BaseSpecification<StationServicePrice>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="stationId">The station.</param>
    /// <param name="service">The service code.</param>
    /// <param name="day">The start day.</param>
    public PriceOnDaySpecification(Guid stationId, string service, DateOnly day)
        : base(price => price.GasStationId == stationId && price.ServiceCode == service && price.EffectiveFrom == day)
    {
        EnableTracking();
    }
}
