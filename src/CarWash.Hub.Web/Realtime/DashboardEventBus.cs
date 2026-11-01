using System.Collections.Concurrent;
using System.Threading.Channels;
using CarWash.Hub.Application.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace CarWash.Hub.Web.Realtime;

/// <summary>
/// Delivers dashboard events from the processing worker to every open dashboard in this process.
/// </summary>
/// <remarks>
/// Layer: Web.
/// <para>
/// <b>The worker never waits for a browser.</b> Each subscriber - one per open tab - has a bounded
/// queue of its own, drained by its own loop. Publishing is a non-blocking write into each queue; a
/// slow or stalled tab only drops its own oldest events, which is harmless because every event means
/// "refresh", and one refresh catches up on all of them.
/// </para>
/// <para>
/// In-process, because the dashboard and the worker share the host. A second instance would need a
/// backplane here; nothing else would change.
/// </para>
/// </remarks>
/// <param name="logger">Logs subscriber failures.</param>
public sealed class DashboardEventBus(ILogger<DashboardEventBus> logger) : IDashboardEventPublisher
{
    private const int QueueCapacity = 16;

    private readonly ConcurrentDictionary<Guid, Subscriber> _subscribers = new();

    /// <summary>Gets the number of open subscriptions.</summary>
    public int SubscriberCount => _subscribers.Count;

    /// <inheritdoc />
    public void Publish(DashboardEvent dashboardEvent)
    {
        ArgumentNullException.ThrowIfNull(dashboardEvent);

        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Post(dashboardEvent);
        }
    }

    /// <summary>Subscribes to every event published from now on.</summary>
    /// <param name="handler">Called for each event, one at a time, off the publisher's thread.</param>
    /// <returns>The subscription; dispose it to stop.</returns>
    public IDisposable Subscribe(Func<DashboardEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subscriber = new Subscriber(this, handler);
        _subscribers[subscriber.Id] = subscriber;
        subscriber.Start();
        return subscriber;
    }

    private sealed class Subscriber(DashboardEventBus bus, Func<DashboardEvent, Task> handler) : IDisposable
    {
        private readonly Channel<DashboardEvent> _queue = Channel.CreateBounded<DashboardEvent>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        private readonly CancellationTokenSource _stopping = new();

        public Guid Id { get; } = Guid.NewGuid();

        public void Post(DashboardEvent dashboardEvent) => _queue.Writer.TryWrite(dashboardEvent);

        public void Start() => _ = Task.Run(RunAsync);

        public void Dispose()
        {
            if (bus._subscribers.TryRemove(Id, out _))
            {
                _queue.Writer.TryComplete();
                _stopping.Cancel();
                _stopping.Dispose();
            }
        }

        private async Task RunAsync()
        {
            CancellationToken token;
            try
            {
                token = _stopping.Token;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                await foreach (var dashboardEvent in _queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    try
                    {
                        await handler(dashboardEvent).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // One tab failing must not stop its own updates, let alone anyone else's.
                        bus.LogHandlerFailed(exception);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Disposed: the tab closed.
            }
        }
    }

    private void LogHandlerFailed(Exception exception) =>
        logger.LogWarning(exception, "A dashboard failed to handle a live update");
}
