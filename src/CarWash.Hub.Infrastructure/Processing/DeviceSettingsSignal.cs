using CarWash.Hub.Application.Abstractions.Services;

namespace CarWash.Hub.Infrastructure.Processing;

/// <summary>An in-process flag that the machines' settings need recomputing, which also wakes the worker.</summary>
/// <remarks>
/// Layer: Infrastructure. Any number of requests before the worker looks collapse into one: the
/// recompute covers every machine anyway.
/// </remarks>
/// <param name="trigger">Wakes the processing worker.</param>
public sealed class DeviceSettingsSignal(IMessageProcessingTrigger trigger) : IDeviceSettingsSignal
{
    private int _requested;

    /// <inheritdoc />
    public void Request()
    {
        Interlocked.Exchange(ref _requested, 1);
        trigger.Signal();
    }

    /// <inheritdoc />
    public bool TryConsume() => Interlocked.Exchange(ref _requested, 0) == 1;
}
