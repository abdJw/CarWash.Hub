using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Common.Validation;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Dashboard;
using CarWash.Hub.Application.Features.Levels;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace CarWash.Hub.Application.Features.Refills;

/// <summary>One service in a refill: which, and to how many uses.</summary>
/// <param name="Service">The service code.</param>
/// <param name="Capacity">The uses the service must give after the refill.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record RefillLineDto(string Service, int Capacity);

/// <summary>A recorded refill, for the history list.</summary>
/// <param name="Id">The refill's key.</param>
/// <param name="RefilledAt">When it was done.</param>
/// <param name="RecordedAt">When it was recorded.</param>
/// <param name="RecordedBy">Who recorded it.</param>
/// <param name="Notes">The note.</param>
/// <param name="Items">The services refilled.</param>
/// <param name="IsVoided">Whether it was voided.</param>
/// <param name="VoidReason">Why.</param>
/// <param name="VoidedBy">Who voided it.</param>
/// <param name="VoidedAt">When.</param>
/// <param name="CanVoid">Whether the caller may void it now.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record RefillDto(
    Guid Id, DateTimeOffset RefilledAt, DateTimeOffset RecordedAt, string? RecordedBy, string? Notes, IReadOnlyList<RefillLineDto> Items,
    bool IsVoided, string? VoidReason, string? VoidedBy, DateTimeOffset? VoidedAt, bool CanVoid);

/// <summary>A tracked service as the refill form shows it.</summary>
/// <param name="Service">The service code.</param>
/// <param name="Level">Its level now.</param>
/// <param name="Capacity">The last refill's capacity.</param>
/// <param name="Used">Uses since that refill.</param>
/// <param name="PercentRemaining">The share left, 0 to 100; null if never refilled.</param>
/// <param name="SuggestedCapacity">The capacity to offer: the last refill's, else the service default.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record RefillFormServiceDto(string Service, ServiceLevel Level, int? Capacity, int Used, int? PercentRemaining, int? SuggestedCapacity);

/// <summary>What the refill form needs.</summary>
/// <param name="MachineId">The machine's key.</param>
/// <param name="Title">Its name, or MAC address.</param>
/// <param name="Code">Its MAC address.</param>
/// <param name="StationName">Its station, in the current language.</param>
/// <param name="Services">The tracked services, in display order.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record RefillFormDto(Guid MachineId, string Title, string Code, string? StationName, IReadOnlyList<RefillFormServiceDto> Services);

/// <summary>A refill to record.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed record RecordRefillRequest
{
    /// <summary>Gets the machine.</summary>
    public Guid MachineId { get; init; }

    /// <summary>Gets when the refill was done; now when null.</summary>
    public DateTimeOffset? RefilledAt { get; init; }

    /// <summary>Gets a note, such as the product or batch.</summary>
    public string? Notes { get; init; }

    /// <summary>Gets the services refilled, at least one.</summary>
    public IReadOnlyList<RefillLineDto> Items { get; init; } = [];
}

/// <summary>A refill to void.</summary>
/// <param name="RefillId">The refill.</param>
/// <param name="Reason">Why it is void.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record VoidRefillRequest(Guid RefillId, string? Reason);

/// <summary>Records, voids and lists refills.</summary>
/// <remarks>Layer: Application. Every call is limited to the machines the caller may see.</remarks>
public interface IRefillService
{
    /// <summary>Returns what the refill form of a machine needs.</summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The machine and its tracked services' levels.</returns>
    Task<RefillFormDto> GetFormAsync(Guid machineId, CancellationToken cancellationToken = default);

    /// <summary>Records a refill. The levels update within moments, through the worker.</summary>
    /// <param name="request">The refill.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The refill's key.</returns>
    Task<Guid> RecordAsync(RecordRefillRequest request, CancellationToken cancellationToken = default);

    /// <summary>Voids a refill recorded by mistake.</summary>
    /// <param name="request">The refill and the reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when saved.</returns>
    Task VoidAsync(VoidRefillRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists a machine's refills, latest first, voided ones included.</summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Up to the latest 50.</returns>
    Task<IReadOnlyList<RefillDto>> GetHistoryAsync(Guid machineId, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IRefillService"/>.</summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>The refill is a fact; the level is derived.</b> Recording saves the refill and a rebuild
/// request in one transaction and wakes the processing worker, which recomputes the machine's
/// levels from the refill and the usage history. The UI never writes a level, so it can never race
/// the worker counting uses.
/// </para>
/// <para>
/// <b>Who may void.</b> A Super Admin, any refill. A station user, only their own, within
/// <see cref="StationUserVoidWindow"/> of recording it - long enough to correct a slip, too short to
/// rewrite history.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="scopeProvider">The machines the caller may see.</param>
/// <param name="clock">The refill time when none is given.</param>
/// <param name="processingTrigger">Wakes the worker to recompute.</param>
/// <param name="validator">Validates the refill.</param>
/// <param name="localizer">Messages and names.</param>
/// <param name="logger">Logger.</param>
public sealed class RefillService(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    IMachineAccessScopeProvider scopeProvider,
    IDateTimeProvider clock,
    IMessageProcessingTrigger processingTrigger,
    IValidator<RecordRefillRequest> validator,
    ILocalizationService localizer,
    ILogger<RefillService> logger) : IRefillService
{
    /// <summary>How long a station user may void their own refill.</summary>
    public static readonly TimeSpan StationUserVoidWindow = TimeSpan.FromHours(24);

    /// <summary>How far back a refill may be dated.</summary>
    public const int MaxBackdateDays = 30;

    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);
    private const int HistoryLength = 50;

    /// <inheritdoc />
    public async Task<RefillFormDto> GetFormAsync(Guid machineId, CancellationToken cancellationToken = default)
    {
        var machine = await VisibleMachineAsync(machineId, cancellationToken).ConfigureAwait(false);
        var catalog = await TrackedServicesAsync(cancellationToken).ConfigureAwait(false);

        var states = (await unitOfWork.Repository<MachineServiceState, long>()
                .ListAsync(new MachineLevelsSpecification([machineId]), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(state => state.ServiceCode, StringComparer.OrdinalIgnoreCase);

        var services = catalog.Select(service =>
        {
            states.TryGetValue(service.Code, out var state);
            var used = state?.UsedSinceRefill ?? 0;
            return new RefillFormServiceDto(
                service.Code,
                state?.Level ?? ServiceLevel.NeverRefilled,
                state?.Capacity,
                used,
                ServiceLevelCalculator.PercentRemaining(state?.Capacity, used),
                state?.Capacity ?? service.DefaultCapacity);
        }).ToList();

        return new RefillFormDto(
            machine.Id,
            string.IsNullOrWhiteSpace(machine.DisplayName) ? machine.Code : machine.DisplayName,
            machine.Code,
            machine.StationName is null ? null : localizer.Localize(machine.StationName, machine.StationNameAr),
            services);
    }

    /// <inheritdoc />
    public async Task<Guid> RecordAsync(RecordRefillRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireUser();
        await validator.ValidateAndThrowLocalizedAsync(request, cancellationToken).ConfigureAwait(false);

        var machine = await VisibleMachineAsync(request.MachineId, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var refilledAt = request.RefilledAt?.ToUniversalTime() ?? now;
        if (refilledAt > now + FutureTolerance || refilledAt < now.AddDays(-MaxBackdateDays))
        {
            throw new Exceptions.ValidationException(nameof(RecordRefillRequest.RefilledAt), localizer.GetString(ResourceKeys.Validation.RefillTime, MaxBackdateDays));
        }

        var tracked = (await TrackedServicesAsync(cancellationToken).ConfigureAwait(false))
            .Select(service => service.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var untracked = request.Items.FirstOrDefault(item => !tracked.Contains(item.Service));
        if (untracked is not null)
        {
            throw new Exceptions.ValidationException(nameof(RecordRefillRequest.Items), localizer.GetString(ResourceKeys.Validation.RefillServiceNotTracked, untracked.Service));
        }

        var refill = new Refill
        {
            Id = Guid.CreateVersion7(),
            MachineId = machine.Id,
            RefilledAt = refilledAt > now ? now : refilledAt,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        };

        foreach (var item in request.Items)
        {
            refill.Items.Add(new RefillItem { Id = Guid.CreateVersion7(), ServiceCode = item.Service.Trim().ToLowerInvariant(), Capacity = item.Capacity });
        }

        await unitOfWork.Repository<Refill, Guid>().AddAsync(refill, cancellationToken).ConfigureAwait(false);
        await RequestRebuildAsync(machine.Id, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        processingTrigger.Signal();

        logger.LogInformation(
            "Refill {RefillId} of {Services} recorded on machine {MachineCode} by {UserName}",
            refill.Id, string.Join(", ", request.Items.Select(item => $"{item.Service}={item.Capacity}")), machine.Code, currentUser.UserName);

        return refill.Id;
    }

    /// <inheritdoc />
    public async Task VoidAsync(VoidRefillRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = RequireUser();

        var refill = await unitOfWork.Repository<Refill, Guid>()
                         .FirstOrDefaultAsync(new RefillForVoidSpecification(request.RefillId), cancellationToken)
                         .ConfigureAwait(false)
                     ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Refill), request.RefillId);

        // Not found rather than forbidden for another station's refill: its existence is not revealed.
        await VisibleMachineAsync(refill.MachineId, cancellationToken).ConfigureAwait(false);

        if (refill.IsDeleted)
        {
            throw new BusinessRuleException(ResourceKeys.Admin.RefillAlreadyVoided);
        }

        if (!CanVoid(refill.CreatedBy, refill.CreatedAt, userId))
        {
            throw new ForbiddenException(ResourceKeys.Admin.VoidNotAllowed);
        }

        refill.VoidReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()[..Math.Min(request.Reason.Trim().Length, Refill.VoidReasonMaxLength)];

        // A soft delete: the interceptor stamps who voided it and when.
        unitOfWork.Repository<Refill, Guid>().Remove(refill);
        await RequestRebuildAsync(refill.MachineId, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        processingTrigger.Signal();

        logger.LogInformation("Refill {RefillId} voided by {UserName}", refill.Id, currentUser.UserName);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RefillDto>> GetHistoryAsync(Guid machineId, CancellationToken cancellationToken = default)
    {
        await VisibleMachineAsync(machineId, cancellationToken).ConfigureAwait(false);
        var userId = currentUser.UserId;

        var refills = await unitOfWork.Repository<Refill, Guid>()
            .ListAsync(new MachineRefillHistorySpecification(machineId, HistoryLength), cancellationToken)
            .ConfigureAwait(false);

        var userIds = refills.SelectMany(refill => new[] { refill.CreatedBy, refill.DeletedBy }).OfType<Guid>().Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await unitOfWork.Repository<User, Guid>()
                    .ListAsync(new UsersByIdsSpecification(userIds), user => new { user.Id, user.DisplayName }, cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(user => user.Id, user => user.DisplayName);

        return
        [
            .. refills.Select(refill => new RefillDto(
                refill.Id,
                refill.RefilledAt,
                refill.CreatedAt,
                refill.CreatedBy is { } by && names.TryGetValue(by, out var recorder) ? recorder : null,
                refill.Notes,
                [.. refill.Items.OrderBy(item => item.ServiceCode, StringComparer.Ordinal).Select(item => new RefillLineDto(item.ServiceCode, item.Capacity))],
                refill.IsDeleted,
                refill.VoidReason,
                refill.DeletedBy is { } voider && names.TryGetValue(voider, out var voidedBy) ? voidedBy : null,
                refill.DeletedAt,
                !refill.IsDeleted && userId is { } me && CanVoid(refill.CreatedBy, refill.CreatedAt, me))),
        ];
    }

    private bool CanVoid(Guid? recordedBy, DateTimeOffset recordedAt, Guid userId) =>
        currentUser.Role == UserRole.SuperAdmin
        || (recordedBy == userId && clock.UtcNow - recordedAt <= StationUserVoidWindow);

    private Guid RequireUser() => currentUser.UserId ?? throw new ForbiddenException(ResourceKeys.Errors.Forbidden);

    private Task RequestRebuildAsync(Guid machineId, CancellationToken cancellationToken) =>
        unitOfWork.Repository<StateRebuildRequest, long>().AddAsync(
            new StateRebuildRequest { MachineId = machineId, Reason = RebuildReason.RefillChanged, RequestedAt = clock.UtcNow },
            cancellationToken);

    private async Task<List<ServiceDefinition>> TrackedServicesAsync(CancellationToken cancellationToken) =>
    [
        .. (await unitOfWork.Repository<ServiceDefinition, Guid>()
                .ListAsync(new ActiveServicesSpecification(), cancellationToken)
                .ConfigureAwait(false))
            .Where(service => service.TracksRefill),
    ];

    private async Task<VisibleMachine> VisibleMachineAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var machines = await unitOfWork.Machines
            .ListAsync(
                new DashboardMachinesSpecification(scopeProvider.GetScope(), machineId),
                machine => new VisibleMachine(
                    machine.Id,
                    machine.Code,
                    machine.DisplayName,
                    machine.GasStation != null ? machine.GasStation.Name : null,
                    machine.GasStation != null ? machine.GasStation.NameAr : null),
                cancellationToken)
            .ConfigureAwait(false);

        return machines.Count > 0
            ? machines[0]
            : throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Machine), machineId);
    }

    private sealed record VisibleMachine(Guid Id, string Code, string? DisplayName, string? StationName, string? StationNameAr);
}

/// <summary>Validates a refill.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class RecordRefillRequestValidator : AbstractValidator<RecordRefillRequest>
{
    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public RecordRefillRequestValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(request => request.Items)
            .NotEmpty().WithMessage(_ => localizer.GetString(ResourceKeys.Validation.RefillNoItems))
            .Must(items => items.Select(item => item.Service.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == items.Count)
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.RefillDuplicateService));

        RuleForEach(request => request.Items).ChildRules(item =>
            item.RuleFor(line => line.Capacity)
                .InclusiveBetween(1, RefillItem.MaxCapacity)
                .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.Range, localizer.GetString(ResourceKeys.Fields.Capacity), 1, RefillItem.MaxCapacity)));

        RuleFor(request => request.Notes).Optional(localizer, ResourceKeys.Fields.Notes, Refill.NotesMaxLength);
    }
}

/// <summary>The level rows of some machines.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class MachineLevelsSpecification : BaseSpecification<MachineServiceState>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineIds">The machines.</param>
    public MachineLevelsSpecification(IReadOnlyCollection<Guid> machineIds)
        : base(state => machineIds.Contains(state.MachineId))
    {
    }
}

/// <summary>One refill, voided or not, tracked to be voided.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class RefillForVoidSpecification : BaseSpecification<Refill>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="refillId">The refill.</param>
    public RefillForVoidSpecification(Guid refillId)
        : base(refill => refill.Id == refillId)
    {
        IncludeSoftDeleted();
        EnableTracking();
    }
}

/// <summary>A machine's latest refills, voided ones included, with their items.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class MachineRefillHistorySpecification : BaseSpecification<Refill>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="take">How many.</param>
    public MachineRefillHistorySpecification(Guid machineId, int take)
        : base(refill => refill.MachineId == machineId)
    {
        IncludeSoftDeleted();
        AddInclude(refill => refill.Items);
        AddOrderBy(refill => refill.RefilledAt, SortDirection.Descending);
        AddOrderBy(refill => refill.CreatedAt, SortDirection.Descending);
        ApplyPaging(0, take);
    }
}

/// <summary>Some users by key.</summary>
/// <remarks>Layer: Application. Deleted users included, so history still names them.</remarks>
public sealed class UsersByIdsSpecification : BaseSpecification<User>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="ids">The users.</param>
    public UsersByIdsSpecification(IReadOnlyCollection<Guid> ids)
        : base(user => ids.Contains(user.Id))
    {
        IncludeSoftDeleted();
    }
}
