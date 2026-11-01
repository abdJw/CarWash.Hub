using CarWash.Hub.Application.Abstractions.Services;

namespace CarWash.Hub.Web.Realtime;

/// <summary>
/// One open dashboard's subscription to live events.
/// </summary>
/// <remarks>
/// Layer: Web. Scoped to the circuit: created with the tab, disposed when it closes, so a closed
/// tab never keeps a subscription alive. Pages listen to <see cref="MachineActivity"/> and
/// <see cref="AlertsChanged"/> rather than subscribing to the bus themselves.
/// </remarks>
public sealed class LiveUpdates : IDisposable
{
    private readonly IDisposable _subscription;

    /// <summary>Subscribes this circuit to the bus.</summary>
    /// <param name="bus">The process-wide bus.</param>
    public LiveUpdates(DashboardEventBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        _subscription = bus.Subscribe(OnEventAsync);
    }

    /// <summary>Raised when machines in this process sent messages that have now been processed.</summary>
    public event Func<MachineActivityEvent, Task>? MachineActivity;

    /// <summary>
    /// Raised when alerts were raised, resolved or acknowledged. Carries no content: listeners reload
    /// through their own user's scope, which is what keeps another station's alerts out of view.
    /// </summary>
    public event Func<AlertsChangedEvent, Task>? AlertsChanged;

    /// <summary>Gets when the last event arrived, in UTC.</summary>
    public DateTimeOffset? LastEventAt { get; private set; }

    /// <inheritdoc />
    public void Dispose() => _subscription.Dispose();

    private async Task OnEventAsync(DashboardEvent dashboardEvent)
    {
        LastEventAt = dashboardEvent.At;

        switch (dashboardEvent)
        {
            case MachineActivityEvent activity:
                await RaiseAsync(MachineActivity, activity).ConfigureAwait(false);
                break;
            case AlertsChangedEvent alerts:
                await RaiseAsync(AlertsChanged, alerts).ConfigureAwait(false);
                break;
        }
    }

    private static async Task RaiseAsync<TEvent>(Func<TEvent, Task>? handlers, TEvent dashboardEvent)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<TEvent, Task>>())
        {
            await handler(dashboardEvent).ConfigureAwait(false);
        }
    }
}
