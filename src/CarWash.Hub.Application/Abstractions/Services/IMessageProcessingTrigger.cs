namespace CarWash.Hub.Application.Abstractions.Services;

/// <summary>
/// Wakes the processing worker when there is new work, so a stored message becomes usage within a
/// moment rather than at the next periodic sweep.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// A hint, not a queue: the queue is the set of pending messages in the database. A signal that
/// is lost - the worker busy, or not running in this process - costs nothing but latency, because
/// the worker also sweeps on a timer.
/// </para>
/// </remarks>
public interface IMessageProcessingTrigger
{
    /// <summary>Signals that new work may be waiting. Never blocks and never throws.</summary>
    void Signal();
}

/// <summary>What the processing worker has been doing, for the status endpoint and health checks.</summary>
/// <remarks>Layer: Application. Implemented in Infrastructure, next to the worker it observes.</remarks>
public interface IProcessingMonitor
{
    /// <summary>Returns the worker's state at this moment.</summary>
    /// <returns>The snapshot.</returns>
    ProcessingWorkerSnapshot GetSnapshot();
}

/// <summary>The processing worker's state at one moment.</summary>
/// <param name="Enabled">Whether background workers are enabled in this process.</param>
/// <param name="LeaseHeld">Whether this process holds the worker lease and so does the processing.</param>
/// <param name="LastRunAt">When the worker last finished a batch, in UTC.</param>
/// <param name="MessagesProcessed">How many messages this process has processed since it started.</param>
/// <param name="LastError">The last failure, if any.</param>
/// <param name="LastErrorAt">When the last failure happened, in UTC.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ProcessingWorkerSnapshot(
    bool Enabled,
    bool LeaseHeld,
    DateTimeOffset? LastRunAt,
    long MessagesProcessed,
    string? LastError,
    DateTimeOffset? LastErrorAt);
