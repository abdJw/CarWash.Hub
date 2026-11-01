using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Features.Processing;

namespace CarWash.Hub.Infrastructure.Processing;

/// <summary>
/// Records what the processing worker in this process is doing.
/// </summary>
/// <remarks>Layer: Infrastructure. Singleton; written by the worker, read by the status endpoint.</remarks>
public sealed class ProcessingMonitor : IProcessingMonitor
{
    private readonly Lock _lock = new();
    private bool _enabled;
    private bool _leaseHeld;
    private DateTimeOffset? _lastRunAt;
    private long _messagesProcessed;
    private string? _lastError;
    private DateTimeOffset? _lastErrorAt;

    /// <inheritdoc />
    public ProcessingWorkerSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new ProcessingWorkerSnapshot(_enabled, _leaseHeld, _lastRunAt, _messagesProcessed, _lastError, _lastErrorAt);
        }
    }

    /// <summary>Records whether workers run in this process.</summary>
    /// <param name="enabled">Whether they do.</param>
    public void SetEnabled(bool enabled)
    {
        lock (_lock)
        {
            _enabled = enabled;
        }
    }

    /// <summary>Records whether this process holds the worker lease.</summary>
    /// <param name="held">Whether it does.</param>
    public void SetLeaseHeld(bool held)
    {
        lock (_lock)
        {
            _leaseHeld = held;
        }
    }

    /// <summary>Records a finished batch.</summary>
    /// <param name="result">What it did.</param>
    /// <param name="at">When it finished.</param>
    public void RecordBatch(ProcessingBatchResult result, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (_lock)
        {
            _lastRunAt = at;
            _messagesProcessed += result.Messages;
        }
    }

    /// <summary>Records a failure of the worker loop.</summary>
    /// <param name="exception">The failure.</param>
    /// <param name="at">When it happened.</param>
    public void RecordError(Exception exception, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(exception);

        lock (_lock)
        {
            _lastError = $"{exception.GetType().Name}: {exception.Message}";
            _lastErrorAt = at;
        }
    }
}
