namespace CarWash.Hub.Application.Abstractions.Services;

/// <summary>
/// Tells open dashboards that something they show has changed, so they refresh without polling.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// Published by the processing worker after its transaction commits - never before, or a dashboard
/// could read the old data in response to the news. In-process only: the dashboard runs in the
/// same host. Publishing never blocks and never throws; a dashboard that misses an event catches up
/// on its next refresh.
/// </para>
/// </remarks>
public interface IDashboardEventPublisher
{
    /// <summary>Publishes an event to every open dashboard.</summary>
    /// <param name="dashboardEvent">What changed.</param>
    void Publish(DashboardEvent dashboardEvent);
}

/// <summary>Something an open dashboard may need to show.</summary>
/// <param name="At">When it happened, in UTC.</param>
/// <remarks>Layer: Application.</remarks>
public abstract record DashboardEvent(DateTimeOffset At);

/// <summary>New messages from some machines have been processed: their state and usage changed.</summary>
/// <param name="At">When the batch committed, in UTC.</param>
/// <param name="MachineIds">The machines with new messages.</param>
/// <param name="MachinesWithUsage">The machines whose services were used, a subset of <paramref name="MachineIds"/>.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record MachineActivityEvent(
    DateTimeOffset At,
    IReadOnlyCollection<Guid> MachineIds,
    IReadOnlyCollection<Guid> MachinesWithUsage) : DashboardEvent(At);

/// <summary>Alerts on some machines were raised, resolved or acknowledged.</summary>
/// <param name="At">When the change committed, in UTC.</param>
/// <param name="MachineIds">The machines whose alerts changed.</param>
/// <remarks>
/// Layer: Application. Carries no alert content and no station: each dashboard reloads its alerts
/// through its own user's scope, so an event can neither leak another station's alert nor go stale
/// when a machine moves to another station.
/// </remarks>
public sealed record AlertsChangedEvent(DateTimeOffset At, IReadOnlyCollection<Guid> MachineIds) : DashboardEvent(At);

/// <summary>A publisher with no subscribers, for hosts without a dashboard.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class NullDashboardEventPublisher : IDashboardEventPublisher
{
    /// <inheritdoc />
    public void Publish(DashboardEvent dashboardEvent)
    {
    }
}
