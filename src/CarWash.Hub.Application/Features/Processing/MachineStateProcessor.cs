using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Alerts;
using CarWash.Hub.Application.Features.Levels;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Application.Features.Processing;

/// <summary>
/// Turns pending machine messages into usage, hourly counts and machine state.
/// </summary>
/// <remarks>
/// Layer: Application. Called by the processing worker in a loop, one batch per call and one DI scope
/// per batch; tests call it directly instead of running the worker.
/// </remarks>
public interface IMachineStateProcessor
{
    /// <summary>Processes the oldest batch of pending messages.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the batch did, and whether more messages are waiting.</returns>
    /// <remarks>Pending level rebuilds - a refill recorded or voided - are applied first.</remarks>
    Task<ProcessingBatchResult> ProcessPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Recomputes every run-out forecast, which ages even when nothing is used.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the forecasts are saved.</returns>
    Task RefreshForecastsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-checks the alerts of every machine: a forecast coming due or a machine going silent happens
    /// with the passing of time, not with a message. Never throws; a failure is logged and the next
    /// check catches up.
    /// </summary>
    /// <param name="allowNoActivityRaise">Whether a silent machine may raise a no-activity alert now.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the check is done.</returns>
    Task CheckAlertsAsync(bool allowNoActivityRaise, CancellationToken cancellationToken = default);
}

/// <summary>What one processing batch did.</summary>
/// <param name="Processed">Messages that carried usage.</param>
/// <param name="Ignored">Messages with nothing to record.</param>
/// <param name="Failed">Messages that could not be processed.</param>
/// <param name="UsagesRecorded">Usage events stored.</param>
/// <param name="Duplicates">Usage events skipped as retries of ones already stored.</param>
/// <param name="HasMore">Whether the batch was full, so more messages may be waiting.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ProcessingBatchResult(int Processed, int Ignored, int Failed, int UsagesRecorded, int Duplicates, bool HasMore)
{
    /// <summary>Gets the result of a batch that found nothing to do.</summary>
    public static ProcessingBatchResult Empty { get; } = new(0, 0, 0, 0, 0, false);

    /// <summary>Gets the number of messages the batch handled.</summary>
    public int Messages => Processed + Ignored + Failed;
}

/// <summary>The default <see cref="IMachineStateProcessor"/>.</summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>The single writer.</b> Only this class writes usage, the hourly rollup and machine state, and
/// the worker lease keeps it to one process at a time, so a counter is a plain read-and-increment
/// with no race. A batch is one transaction: its usage rows, its counters and its messages' new
/// status commit together or not at all, so an interrupted batch is simply processed again.
/// </para>
/// <para>
/// <b>Idempotent.</b> A message is only processed while pending; its events are unique by
/// <c>(message, index)</c>; and an event a machine re-sent after a timeout - same service, same
/// <c>readAt</c>, arriving at least <see cref="ProcessingOptions.RetryMinDelay"/> later - is
/// recognised and counted once.
/// </para>
/// <para>
/// <b>Poison messages.</b> If a whole batch fails, its messages are retried one by one, and only the
/// one that still fails is marked failed. One bad payload never blocks the queue.
/// </para>
/// <para>
/// <b>Open dashboards are told after commit.</b> Each batch that changed anything publishes one
/// <see cref="MachineActivityEvent"/> naming the machines involved, never before its transaction has
/// committed, so a dashboard refreshing in response always reads the new state.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="parser">Reads usage events out of payloads.</param>
/// <param name="clock">The processing timestamp.</param>
/// <param name="options">Batch size and clock-skew limits.</param>
/// <param name="events">Tells open dashboards what changed.</param>
/// <param name="levels">Keeps each service's refill level current.</param>
/// <param name="alerts">Raises and resolves alerts from the new state.</param>
/// <param name="logger">Logger.</param>
public sealed class MachineStateProcessor(
    IUnitOfWork unitOfWork,
    IMachineMessageParser parser,
    IDateTimeProvider clock,
    IOptions<ProcessingOptions> options,
    IDashboardEventPublisher events,
    IServiceLevelUpdater levels,
    IAlertEvaluator alerts,
    ILogger<MachineStateProcessor> logger) : IMachineStateProcessor
{
    private readonly ProcessingOptions _options = options.Value;

    // The machines the committed transactions of the current batch touched, for the dashboard event.
    private readonly HashSet<Guid> _touchedMachines = [];
    private readonly HashSet<Guid> _usedMachines = [];

    /// <inheritdoc />
    public async Task<ProcessingBatchResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        _touchedMachines.Clear();
        _usedMachines.Clear();

        // Refills recorded or voided since the last batch: recompute those machines first, so the
        // batch below counts its uses against the right refill.
        _touchedMachines.UnionWith(await levels.ApplyRebuildRequestsAsync(clock.UtcNow, cancellationToken).ConfigureAwait(false));

        var pending = await unitOfWork.MachineMessages
            .ListAsync(
                new PendingMessagesSpecification(_options.BatchSize),
                message => new PendingMessage(message.Id, message.MachineId, message.ReceivedAt, message.Payload),
                cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            // Often a refill was just recorded: its machine's alerts resolve now, not at the next check.
            await EvaluateAlertsAsync(_touchedMachines, allowNoActivityRaise: false, cancellationToken).ConfigureAwait(false);
            PublishActivity();
            return ProcessingBatchResult.Empty;
        }

        // Parsing is pure, so it happens once, outside the transaction that may be retried.
        var items = pending.Select(message => new ParsedMessage(message, parser.Parse(message.Payload))).ToList();

        await RegisterUnknownServicesAsync(items, cancellationToken).ConfigureAwait(false);

        ProcessingBatchResult result;
        try
        {
            result = await ApplyAsync(items, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "A batch of {Count} message(s) failed; retrying them one by one", items.Count);
            unitOfWork.DiscardChanges();
            result = await ApplyOneByOneAsync(items, cancellationToken).ConfigureAwait(false);
        }

        logger.LogDebug(
            "Processed {Messages} message(s): {Usages} use(s), {Duplicates} duplicate(s), {Ignored} ignored, {Failed} failed",
            result.Messages, result.UsagesRecorded, result.Duplicates, result.Ignored, result.Failed);

        // After the commit, in its own save: an alert problem must never fail or replay the batch.
        // Machines that just reported are not silent, so no no-activity alert can be due here.
        await EvaluateAlertsAsync(_touchedMachines, allowNoActivityRaise: false, cancellationToken).ConfigureAwait(false);
        PublishActivity();
        return result with { HasMore = pending.Count == _options.BatchSize };
    }

    /// <inheritdoc />
    public async Task RefreshForecastsAsync(CancellationToken cancellationToken = default)
    {
        var changed = await levels.RefreshForecastsAsync(clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (changed.Count > 0)
        {
            events.Publish(new MachineActivityEvent(clock.UtcNow, changed, []));
        }
    }

    /// <inheritdoc />
    public Task CheckAlertsAsync(bool allowNoActivityRaise, CancellationToken cancellationToken = default) =>
        EvaluateAlertsAsync(null, allowNoActivityRaise, cancellationToken);

    private async Task EvaluateAlertsAsync(IReadOnlyCollection<Guid>? machineIds, bool allowNoActivityRaise, CancellationToken cancellationToken)
    {
        if (machineIds is { Count: 0 })
        {
            return;
        }

        try
        {
            await alerts.EvaluateAsync(machineIds is null ? null : [.. machineIds], clock.UtcNow, allowNoActivityRaise, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            unitOfWork.DiscardChanges();
            logger.LogWarning(exception, "Evaluating alerts failed; the next check will catch up");
        }
    }

    private void PublishActivity()
    {
        if (_touchedMachines.Count > 0)
        {
            events.Publish(new MachineActivityEvent(clock.UtcNow, [.. _touchedMachines], [.. _usedMachines]));
        }
    }

    // A service code no one has configured is added to the catalog before the usage that names it,
    // in its own save: the usage rows reference the catalog by code.
    private async Task RegisterUnknownServicesAsync(IReadOnlyList<ParsedMessage> items, CancellationToken cancellationToken)
    {
        var codes = items
            .SelectMany(item => item.Result.Events)
            .Select(usage => usage.ServiceCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codes.Count == 0)
        {
            return;
        }

        var catalog = unitOfWork.Repository<ServiceDefinition, Guid>();
        var known = await catalog
            .ListAsync(new AllServiceDefinitionsSpecification(), service => service.Code, cancellationToken)
            .ConfigureAwait(false);

        var unknown = codes.Except(known, StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count == 0)
        {
            return;
        }

        foreach (var code in unknown)
        {
            await catalog.AddAsync(
                new ServiceDefinition
                {
                    Id = Guid.CreateVersion7(),
                    Code = code,
                    Name = code,
                    NameAr = code,
                    TracksRefill = false,
                    IsConfigured = false,
                    SortOrder = 1000,
                },
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Registered new service code(s) {Codes} from machine messages", string.Join(", ", unknown));
        }
        catch (ConflictException)
        {
            // Registered concurrently (the startup seed, or a second process during a recycle).
            // Either way the code now exists, which is all the usage rows need.
        }
        finally
        {
            unitOfWork.DiscardChanges();
        }
    }

    private async Task<ProcessingBatchResult> ApplyOneByOneAsync(IReadOnlyList<ParsedMessage> items, CancellationToken cancellationToken)
    {
        var total = ProcessingBatchResult.Empty;

        foreach (var item in items)
        {
            ProcessingBatchResult single;
            try
            {
                single = await ApplyAsync([item], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Message {MessageId} could not be processed and is marked failed", item.Message.Id);
                unitOfWork.DiscardChanges();
                await MarkFailedAsync(item.Message.Id, exception.Message, cancellationToken).ConfigureAwait(false);
                single = ProcessingBatchResult.Empty with { Failed = 1 };
            }

            total = new ProcessingBatchResult(
                total.Processed + single.Processed,
                total.Ignored + single.Ignored,
                total.Failed + single.Failed,
                total.UsagesRecorded + single.UsagesRecorded,
                total.Duplicates + single.Duplicates,
                HasMore: false);
        }

        return total;
    }

    private async Task MarkFailedAsync(long messageId, string error, CancellationToken cancellationToken)
    {
        var messages = await unitOfWork.MachineMessages
            .ListAsync(new MessagesByIdsSpecification([messageId]), cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in messages)
        {
            message.ProcessingStatus = ProcessingStatus.Failed;
            message.ProcessedAt = clock.UtcNow;
            message.ProcessingError = Truncate(error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        unitOfWork.DiscardChanges();
    }

    private async Task<ProcessingBatchResult> ApplyAsync(IReadOnlyList<ParsedMessage> items, CancellationToken cancellationToken)
    {
        var result = ProcessingBatchResult.Empty;
        var touched = new HashSet<Guid>();
        var used = new HashSet<Guid>();

        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // Everything is re-read inside the transaction: this delegate replays from the start
                // after a transient failure, with an empty change tracker - and empty sets, so a
                // failed attempt's machines are not reported.
                touched.Clear();
                used.Clear();
                result = await ApplyInTransactionAsync(items, touched, used, token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        unitOfWork.DiscardChanges();
        _touchedMachines.UnionWith(touched);
        _usedMachines.UnionWith(used);
        return result;
    }

    private async Task<ProcessingBatchResult> ApplyInTransactionAsync(
        IReadOnlyList<ParsedMessage> items,
        HashSet<Guid> touched,
        HashSet<Guid> used,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var messages = (await unitOfWork.MachineMessages
                .ListAsync(new MessagesByIdsSpecification([.. items.Select(item => item.Message.Id)]), cancellationToken)
                .ConfigureAwait(false))
            .Where(message => message.ProcessingStatus == ProcessingStatus.Pending)
            .ToDictionary(message => message.Id);

        var live = items.Where(item => messages.ContainsKey(item.Message.Id)).ToList();
        if (live.Count == 0)
        {
            return ProcessingBatchResult.Empty;
        }

        var candidates = live
            .Where(item => item.Result.Outcome == MessageParseOutcome.Usage)
            .SelectMany(item => item.Result.Events.Select(usage => ToUsage(item.Message, usage)))
            .ToList();

        var (accepted, duplicates) = await DeduplicateAsync(candidates, cancellationToken).ConfigureAwait(false);

        await unitOfWork.Repository<ServiceUsage, long>().AddRangeAsync(accepted, cancellationToken).ConfigureAwait(false);
        await IncrementHourlyAsync(accepted, cancellationToken).ConfigureAwait(false);
        await UpdateMachineStatesAsync(live, accepted, now, cancellationToken).ConfigureAwait(false);
        await levels.ApplyUsageAsync(accepted, now, cancellationToken).ConfigureAwait(false);

        var processed = 0;
        var ignored = 0;
        var failed = 0;

        foreach (var item in live)
        {
            var message = messages[item.Message.Id];
            message.ProcessedAt = now;
            message.ProcessingError = Truncate(item.Result.Reason);
            message.ProcessingStatus = item.Result.Outcome switch
            {
                MessageParseOutcome.Usage => ProcessingStatus.Processed,
                MessageParseOutcome.Ignored => ProcessingStatus.Ignored,
                _ => ProcessingStatus.Failed,
            };

            switch (message.ProcessingStatus)
            {
                case ProcessingStatus.Processed:
                    processed++;
                    break;
                case ProcessingStatus.Ignored:
                    ignored++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        touched.UnionWith(live.Select(item => item.Message.MachineId));
        used.UnionWith(accepted.Select(usage => usage.MachineId));

        return new ProcessingBatchResult(processed, ignored, failed, accepted.Count, duplicates, HasMore: false);
    }

    private ServiceUsage ToUsage(PendingMessage message, ParsedUsageEvent usage)
    {
        var (occurredAt, source) = OccurrenceTimeResolver.Resolve(
            usage.DeviceTime, message.ReceivedAt, _options.MaxPastSkew, _options.MaxFutureSkew);

        return new ServiceUsage
        {
            MachineId = message.MachineId,
            ServiceCode = usage.ServiceCode,
            OccurredAt = occurredAt,
            OccurredAtSource = source,
            ReceivedAt = message.ReceivedAt,
            MachineMessageId = message.Id,
            ItemIndex = usage.ItemIndex,
        };
    }

    // A retry after a lost response re-sends the same event - same machine, service and readAt - but
    // only after the controller's timeout. Two real uses in the same second share a readAt too, and
    // arrive together; they are both kept. Only device times are compared: two uses with the same
    // server receive time are always real.
    private async Task<(List<ServiceUsage> Accepted, int Duplicates)> DeduplicateAsync(
        List<ServiceUsage> candidates,
        CancellationToken cancellationToken)
    {
        var deviceTimed = candidates.Where(usage => usage.OccurredAtSource == OccurrenceTimeSource.Device).ToList();
        var arrivals = new Dictionary<UsageKey, List<DateTimeOffset>>();

        if (deviceTimed.Count > 0)
        {
            var existing = await unitOfWork.Repository<ServiceUsage, long>()
                .ListAsync(
                    new DeviceTimedUsagesSpecification(
                        [.. deviceTimed.Select(usage => usage.MachineId).Distinct()],
                        deviceTimed.Min(usage => usage.OccurredAt),
                        deviceTimed.Max(usage => usage.OccurredAt)),
                    usage => new { usage.MachineId, usage.ServiceCode, usage.OccurredAt, usage.ReceivedAt },
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var usage in existing)
            {
                ArrivalsOf(new UsageKey(usage.MachineId, usage.ServiceCode, usage.OccurredAt)).Add(usage.ReceivedAt);
            }
        }

        var accepted = new List<ServiceUsage>(candidates.Count);
        var duplicates = 0;

        foreach (var usage in candidates)
        {
            if (usage.OccurredAtSource == OccurrenceTimeSource.Device)
            {
                var earlier = ArrivalsOf(new UsageKey(usage.MachineId, usage.ServiceCode, usage.OccurredAt));
                if (earlier.Exists(receivedAt => (usage.ReceivedAt - receivedAt).Duration() >= _options.RetryMinDelay))
                {
                    duplicates++;
                    continue;
                }

                earlier.Add(usage.ReceivedAt);
            }

            accepted.Add(usage);
        }

        return (accepted, duplicates);

        List<DateTimeOffset> ArrivalsOf(UsageKey key)
        {
            if (!arrivals.TryGetValue(key, out var list))
            {
                list = [];
                arrivals[key] = list;
            }

            return list;
        }
    }

    private async Task IncrementHourlyAsync(List<ServiceUsage> accepted, CancellationToken cancellationToken)
    {
        if (accepted.Count == 0)
        {
            return;
        }

        var buckets = accepted
            .GroupBy(usage => new HourKey(usage.MachineId, OccurrenceTimeResolver.HourStart(usage.OccurredAt), usage.ServiceCode))
            .ToDictionary(group => group.Key, group => group.Count());

        var repository = unitOfWork.Repository<ServiceUsageHourly, long>();
        var rows = await repository
            .ListAsync(
                new HourlyUsageRowsSpecification(
                    [.. buckets.Keys.Select(key => key.MachineId).Distinct()],
                    buckets.Keys.Min(key => key.HourStart),
                    buckets.Keys.Max(key => key.HourStart)),
                cancellationToken)
            .ConfigureAwait(false);

        var existing = rows.ToDictionary(row => new HourKey(row.MachineId, row.HourStart, row.ServiceCode));

        foreach (var (key, count) in buckets)
        {
            if (existing.TryGetValue(key, out var row))
            {
                row.UsageCount += count;
            }
            else
            {
                await repository.AddAsync(
                    new ServiceUsageHourly
                    {
                        MachineId = key.MachineId,
                        HourStart = key.HourStart,
                        ServiceCode = key.ServiceCode,
                        UsageCount = count,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task UpdateMachineStatesAsync(
        List<ParsedMessage> live,
        List<ServiceUsage> accepted,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var machineIds = live.Select(item => item.Message.MachineId).Distinct().ToList();
        var repository = unitOfWork.Repository<MachineState, Guid>();

        var states = (await repository
                .ListAsync(new MachineStatesSpecification(machineIds), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(state => state.Id);

        foreach (var machineId in machineIds)
        {
            var messages = live.Where(item => item.Message.MachineId == machineId).Select(item => item.Message).ToList();
            var newest = messages.MaxBy(message => message.Id)!;
            var uses = accepted.Where(usage => usage.MachineId == machineId).ToList();

            if (!states.TryGetValue(machineId, out var state))
            {
                state = new MachineState { Id = machineId, LastSeenAt = newest.ReceivedAt, LastMessageId = newest.Id };
                await repository.AddAsync(state, cancellationToken).ConfigureAwait(false);
            }

            // Messages can be processed out of arrival order (a backfill, a retried batch), so each
            // value only ever moves forward.
            if (newest.ReceivedAt > state.LastSeenAt)
            {
                state.LastSeenAt = newest.ReceivedAt;
            }

            state.LastMessageId = Math.Max(state.LastMessageId, newest.Id);

            if (uses.MaxBy(usage => usage.OccurredAt) is { } latestUse
                && (state.LastUsageAt is null || latestUse.OccurredAt > state.LastUsageAt))
            {
                state.LastUsageAt = latestUse.OccurredAt;
                state.LastServiceCode = latestUse.ServiceCode;
            }

            state.TotalUses += uses.Count;
            state.UpdatedAt = now;
        }
    }

    private static string? Truncate(string? value) =>
        value is null || value.Length <= MachineMessage.ProcessingErrorMaxLength
            ? value
            : value[..MachineMessage.ProcessingErrorMaxLength];

    private sealed record PendingMessage(long Id, Guid MachineId, DateTimeOffset ReceivedAt, string Payload);

    private sealed record ParsedMessage(PendingMessage Message, MessageParseResult Result);

    private readonly record struct UsageKey(Guid MachineId, string ServiceCode, DateTimeOffset OccurredAt)
    {
        public bool Equals(UsageKey other) =>
            MachineId == other.MachineId
            && string.Equals(ServiceCode, other.ServiceCode, StringComparison.OrdinalIgnoreCase)
            && OccurredAt.UtcTicks == other.OccurredAt.UtcTicks;

        public override int GetHashCode() =>
            HashCode.Combine(MachineId, StringComparer.OrdinalIgnoreCase.GetHashCode(ServiceCode), OccurredAt.UtcTicks);
    }

    private readonly record struct HourKey(Guid MachineId, DateTimeOffset HourStart, string ServiceCode)
    {
        public bool Equals(HourKey other) =>
            MachineId == other.MachineId
            && HourStart.UtcTicks == other.HourStart.UtcTicks
            && string.Equals(ServiceCode, other.ServiceCode, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() =>
            HashCode.Combine(MachineId, HourStart.UtcTicks, StringComparer.OrdinalIgnoreCase.GetHashCode(ServiceCode));
    }
}
