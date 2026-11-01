using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Features.Alerts;
using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Infrastructure.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Infrastructure.Workers;

/// <summary>
/// Runs <see cref="IMachineStateProcessor"/> continuously: as soon as ingestion signals, and on a
/// timer in case a signal was missed.
/// </summary>
/// <remarks>
/// Layer: Infrastructure.
/// <para>
/// <b>One process at a time.</b> The worker takes the <see cref="LeaseName"/> lease before doing
/// anything and checks it before every batch. During an IIS overlapped recycle the new process
/// waits until the old one lets go.
/// </para>
/// <para>
/// <b>Never stops the host.</b> An exception escaping <c>ExecuteAsync</c> would stop the whole
/// application by default, so every failure is logged, recorded for the status endpoint, and
/// retried after a growing pause (5 s up to a minute). The work is all in the database, so
/// nothing is lost while it waits.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Creates a scope - and a unit of work - per batch.</param>
/// <param name="trigger">The ingest wake-up signal.</param>
/// <param name="lease">Keeps the worker to one process.</param>
/// <param name="workerOptions">Whether workers run here.</param>
/// <param name="processingOptions">The sweep interval.</param>
/// <param name="alertOptions">How often alerts are re-checked, and the start-up grace.</param>
/// <param name="monitor">Records progress for the status endpoint.</param>
/// <param name="clock">Timestamps for the monitor.</param>
/// <param name="logger">Logger.</param>
public sealed class MachineStateWorker(
    IServiceScopeFactory scopeFactory,
    ChannelProcessingTrigger trigger,
    IWorkerLease lease,
    IOptionsMonitor<BackgroundWorkerOptions> workerOptions,
    IOptionsMonitor<ProcessingOptions> processingOptions,
    IOptionsMonitor<AlertOptions> alertOptions,
    ProcessingMonitor monitor,
    IDateTimeProvider clock,
    ILogger<MachineStateWorker> logger) : BackgroundService
{
    /// <summary>The name of the lease this worker holds.</summary>
    public const string LeaseName = "carwash-hub:machine-state";

    private static readonly TimeSpan LeaseRetryInterval = TimeSpan.FromSeconds(15);

    // Forecasts age even when nothing is used: "runs out in 6 hours" must become "in 5".
    private static readonly TimeSpan ForecastRefreshInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Read here rather than at registration: configuration added by a test host is only
        // complete once the host is built.
        if (!workerOptions.CurrentValue.Enabled)
        {
            logger.LogInformation("Background workers are disabled; machine messages are not processed in this process");
            return;
        }

        monitor.SetEnabled(true);

        // Yield first, so a slow first database call never delays application startup.
        await Task.Yield();

        var backoff = MinBackoff;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunWhileLeaseHeldAsync(stoppingToken).ConfigureAwait(false);
                backoff = MinBackoff;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                monitor.RecordError(exception, clock.UtcNow);
                logger.LogError(exception, "Message processing failed; retrying in {Delay}", backoff);

                try
                {
                    await Task.Delay(backoff, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }
            finally
            {
                monitor.SetLeaseHeld(false);
            }
        }
    }

    private async Task RunWhileLeaseHeldAsync(CancellationToken stoppingToken)
    {
        var handle = await lease.TryAcquireAsync(LeaseName, stoppingToken).ConfigureAwait(false);
        if (handle is null)
        {
            logger.LogDebug("Another process holds the processing lease; waiting");
            await Task.Delay(LeaseRetryInterval, stoppingToken).ConfigureAwait(false);
            return;
        }

        await using (handle.ConfigureAwait(false))
        {
            monitor.SetLeaseHeld(true);
            logger.LogInformation("Processing lease acquired; this process now processes machine messages");
            var nextForecastRefresh = DateTimeOffset.MinValue;
            var nextAlertCheck = DateTimeOffset.MinValue;

            // A machine is not called silent until messages that arrived while this process was not
            // processing have had time to be processed.
            var silenceAllowedFrom = clock.UtcNow + TimeSpan.FromMinutes(alertOptions.CurrentValue.StartupGraceMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await handle.IsHeldAsync(stoppingToken).ConfigureAwait(false))
                {
                    logger.LogWarning("Processing lease lost; re-acquiring");
                    return;
                }

                var result = await RunBatchAsync(stoppingToken).ConfigureAwait(false);
                if (result.HasMore)
                {
                    continue;
                }

                if (clock.UtcNow >= nextForecastRefresh)
                {
                    await RefreshForecastsAsync(stoppingToken).ConfigureAwait(false);
                    nextForecastRefresh = clock.UtcNow + ForecastRefreshInterval;
                }

                // Only between full batches, so a backlog is processed before anyone is called silent.
                if (clock.UtcNow >= nextAlertCheck)
                {
                    await CheckAlertsAsync(clock.UtcNow >= silenceAllowedFrom, stoppingToken).ConfigureAwait(false);
                    nextAlertCheck = clock.UtcNow + TimeSpan.FromSeconds(alertOptions.CurrentValue.CheckIntervalSeconds);
                }

                var interval = TimeSpan.FromSeconds(processingOptions.CurrentValue.SweepIntervalSeconds);
                await trigger.WaitAsync(interval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    // The processor's check logs and swallows its own failures, so a broken alert can never stop
    // message processing or give up the lease.
    private async Task CheckAlertsAsync(bool allowNoActivityRaise, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMachineStateProcessor>()
            .CheckAlertsAsync(allowNoActivityRaise, stoppingToken)
            .ConfigureAwait(false);
    }

    private async Task RefreshForecastsAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMachineStateProcessor>().RefreshForecastsAsync(stoppingToken).ConfigureAwait(false);
    }

    private async Task<ProcessingBatchResult> RunBatchAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IMachineStateProcessor>();

        var result = await processor.ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
        monitor.RecordBatch(result, clock.UtcNow);

        if (result.Failed > 0)
        {
            logger.LogWarning("{Failed} machine message(s) could not be processed", result.Failed);
        }

        return result;
    }
}
