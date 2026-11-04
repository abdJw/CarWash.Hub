namespace CarWash.Hub.Application.Abstractions.Services;

/// <summary>
/// Tells the processing worker that the machines' desired settings may have changed - a price, a
/// run time, or which station a machine stands at - so it recomputes them now rather than at its
/// next periodic check.
/// </summary>
/// <remarks>
/// Layer: Application. A hint, like <see cref="IMessageProcessingTrigger"/>: the worker also
/// reconciles on a timer, which is what catches a dated price taking effect at midnight, so a lost
/// signal only costs latency.
/// </remarks>
public interface IDeviceSettingsSignal
{
    /// <summary>Asks for the settings to be recomputed. Never blocks and never throws.</summary>
    void Request();

    /// <summary>Takes a pending request, if there is one.</summary>
    /// <returns><see langword="true"/> if a recompute was requested since the last call.</returns>
    bool TryConsume();
}
