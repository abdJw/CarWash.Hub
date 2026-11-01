namespace CarWash.Hub.Infrastructure.Workers;

/// <summary>
/// Whether this process runs the background workers.
/// </summary>
/// <remarks>
/// Layer: Infrastructure. Bound from <c>BackgroundWorkers</c>. On by default. The integration tests
/// turn it off and drive the processor themselves, so a test sees exactly the batches it ran.
/// </remarks>
public sealed class BackgroundWorkerOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "BackgroundWorkers";

    /// <summary>Gets or sets a value indicating whether the background workers run in this process.</summary>
    public bool Enabled { get; set; } = true;
}
