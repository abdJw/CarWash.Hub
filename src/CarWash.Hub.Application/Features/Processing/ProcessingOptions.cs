using System.ComponentModel.DataAnnotations;

namespace CarWash.Hub.Application.Features.Processing;

/// <summary>
/// How stored messages are turned into usage.
/// </summary>
/// <remarks>Layer: Application. Bound from the <c>Processing</c> section and validated at startup.</remarks>
public sealed class ProcessingOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Processing";

    /// <summary>
    /// Gets or sets the most messages processed in one transaction. Small enough that a batch
    /// finishes within IIS's shutdown time limit, so a recycle never interrupts one for long.
    /// </summary>
    [Range(1, 1000)]
    public int BatchSize { get; set; } = 200;

    /// <summary>
    /// Gets or sets how often, in seconds, the worker looks for pending messages when nothing has
    /// woken it - the safety net behind the ingest signal.
    /// </summary>
    [Range(1, 3600)]
    public int SweepIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets how far in the past a machine's own reading time may be and still be trusted.
    /// Older readings - an unset clock reporting 1970, say - are replaced by the receive time.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "365.00:00:00")]
    public TimeSpan MaxPastSkew { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets how far ahead of the server a machine's reading time may be and still be
    /// trusted. Covers ordinary clock drift; anything further is replaced by the receive time.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan MaxFutureSkew { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets how much later an event with the same machine, service and reading time must
    /// arrive to count as a retry rather than a second use.
    /// </summary>
    /// <remarks>
    /// <c>readAt</c> has one-second resolution, so two real uses in the same second share it - but
    /// they arrive together. A retry arrives only after the controller's request timeout. Erring
    /// towards counting is deliberate: an extra count brings a refill alert slightly early, a
    /// missed one brings it after the tank has run dry.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00", "00:10:00")]
    public TimeSpan RetryMinDelay { get; set; } = TimeSpan.FromSeconds(2);
}
