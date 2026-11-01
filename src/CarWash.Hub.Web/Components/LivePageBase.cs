using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Web.Circuits;
using CarWash.Hub.Web.Components.Admin;
using CarWash.Hub.Web.Components.Refills;
using CarWash.Hub.Web.Localization;
using CarWash.Hub.Web.Theming;
using CarWash.Hub.Web.Formatting;
using CarWash.Hub.Web.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace CarWash.Hub.Web.Components;

/// <summary>
/// A dashboard page that keeps itself current: it reloads when the machines it shows report, and
/// re-renders its relative times as they age.
/// </summary>
/// <remarks>
/// Layer: Web.
/// <para>
/// <b>Coalesced.</b> A busy fleet processes a batch every second or so. Events arriving within
/// <see cref="RefreshDelay"/> of each other cause one reload, not one each, and a reload keeps the
/// previous render on screen (dimmed by <see cref="IsRefreshing"/>) instead of flashing a skeleton.
/// </para>
/// <para>
/// <b>Safety net.</b> A missed event - a tab that reconnected, a process that recycled - is caught
/// up by a full reload every <see cref="FallbackRefreshInterval"/>.
/// </para>
/// <para>
/// Background work re-enters the renderer through <c>InvokeAsync</c>, and any failure is handed to
/// the error boundary rather than lost, so an exception in a timer can never kill the circuit.
/// </para>
/// </remarks>
public abstract class LivePageBase : ComponentBase, IAsyncDisposable
{
    /// <summary>How long events are coalesced before a reload.</summary>
    protected static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>How often relative times re-render.</summary>
    protected static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    /// <summary>How often the page reloads even without events.</summary>
    protected static readonly TimeSpan FallbackRefreshInterval = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan PulseDuration = TimeSpan.FromSeconds(2.5);

    private readonly CancellationTokenSource _disposed = new();
    private readonly Dictionary<Guid, DateTimeOffset> _pulses = [];
    private bool _refreshScheduled;
    private DateTimeOffset _lastFullRefresh;

    /// <summary>Gets the runner every query and command goes through.</summary>
    [Inject]
    protected DashboardOperationRunner Runner { get; private set; } = default!;

    /// <summary>Gets the circuit's live-event subscription.</summary>
    [Inject]
    protected LiveUpdates Live { get; private set; } = default!;

    /// <summary>Gets the dashboard's words.</summary>
    [Inject]
    protected IStringLocalizer<DashboardResource> L { get; private set; } = default!;

    /// <summary>Gets the number, time and duration formatter.</summary>
    [Inject]
    protected DashboardFormat Format { get; private set; } = default!;

    /// <summary>Gets the clock.</summary>
    [Inject]
    protected IDateTimeProvider Clock { get; private set; } = default!;

    /// <summary>Gets the dialog service.</summary>
    [Inject]
    protected IDialogService Dialogs { get; private set; } = default!;

    /// <summary>Gets when the data on screen was read, in UTC.</summary>
    protected DateTimeOffset? LastRefreshedAt { get; private set; }

    /// <summary>Gets a value indicating whether a reload is in progress.</summary>
    protected bool IsRefreshing { get; private set; }

    /// <summary>Gets a token cancelled when the page goes away.</summary>
    protected CancellationToken PageToken => _disposed.Token;

    /// <summary>Gets the current time, for relative times in the markup.</summary>
    protected DateTimeOffset Now => Clock.UtcNow;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Live.MachineActivity -= OnMachineActivityAsync;
        Live.AlertsChanged -= OnAlertsChangedAsync;
        await _disposed.CancelAsync();
        _disposed.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Returns whether a machine reported a use in the last moments, for a pulse animation.</summary>
    /// <param name="machineId">The machine.</param>
    /// <returns><see langword="true"/> while the pulse should show.</returns>
    protected bool IsPulsing(Guid machineId) =>
        _pulses.TryGetValue(machineId, out var at) && Now - at < PulseDuration;

    /// <summary>Opens the refill form for a machine; the page refreshes when the worker has applied it.</summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="title">The machine's name, for the dialog title.</param>
    /// <param name="services">The service names and icons.</param>
    /// <param name="preselect">A service to start selected.</param>
    /// <returns>A task that completes when the dialog closes.</returns>
    protected async Task OpenRefillAsync(Guid machineId, string title, ServiceLookup services, string? preselect = null)
    {
        var dialog = await Dialogs.ShowAsync<RefillDialog>(
            $"{L[DashboardText.RefillTitle]} · \u2068{title}\u2069",
            new DialogParameters<RefillDialog>
            {
                { refill => refill.MachineId, machineId },
                { refill => refill.Services, services },
                { refill => refill.Preselect, preselect },
            },
            AdminDialogs.Options);

        await dialog.Result;
    }

    /// <summary>Loads the page's data. Called on first render, on events and on the fallback timer.</summary>
    /// <param name="cancellationToken">Cancelled when the page goes away.</param>
    /// <returns>A task that completes when the data is loaded.</returns>
    protected abstract Task LoadAsync(CancellationToken cancellationToken);

    /// <summary>Returns whether an event concerns this page. All events do, by default.</summary>
    /// <param name="activity">The event.</param>
    /// <returns><see langword="true"/> to reload.</returns>
    protected virtual bool IsRelevant(MachineActivityEvent activity) => true;

    /// <summary>Returns whether a change to alerts concerns this page. None do, by default.</summary>
    /// <param name="alerts">The event.</param>
    /// <returns><see langword="true"/> to reload.</returns>
    protected virtual bool IsRelevant(AlertsChangedEvent alerts) => false;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        Live.MachineActivity += OnMachineActivityAsync;
        Live.AlertsChanged += OnAlertsChangedAsync;
        await RefreshAsync();
        _ = TickAsync();
    }

    /// <summary>Reloads the page's data now.</summary>
    /// <returns>A task that completes when it has.</returns>
    protected async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await LoadAsync(PageToken);
            LastRefreshedAt = Now;
            _lastFullRefresh = Now;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private Task OnMachineActivityAsync(MachineActivityEvent activity)
    {
        if (PageToken.IsCancellationRequested || !IsRelevant(activity))
        {
            return Task.CompletedTask;
        }

        return InvokeAsync(() =>
        {
            foreach (var machineId in activity.MachinesWithUsage)
            {
                _pulses[machineId] = Now;
            }

            ScheduleRefresh();
        });
    }

    private Task OnAlertsChangedAsync(AlertsChangedEvent alerts) =>
        PageToken.IsCancellationRequested || !IsRelevant(alerts) ? Task.CompletedTask : InvokeAsync(ScheduleRefresh);

    private void ScheduleRefresh()
    {
        if (!_refreshScheduled)
        {
            _refreshScheduled = true;
            _ = RefreshSoonAsync();
        }
    }

    private async Task RefreshSoonAsync()
    {
        try
        {
            await Task.Delay(RefreshDelay, PageToken);
            await InvokeAsync(async () =>
            {
                _refreshScheduled = false;
                await RefreshAsync();
                StateHasChanged();
            });
        }
        catch (OperationCanceledException)
        {
            // The page went away.
        }
        catch (Exception exception)
        {
            await DispatchExceptionAsync(exception);
        }
    }

    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(PageToken))
            {
                await InvokeAsync(async () =>
                {
                    if (Now - _lastFullRefresh >= FallbackRefreshInterval)
                    {
                        await RefreshAsync();
                    }

                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // The page went away.
        }
        catch (Exception exception)
        {
            await DispatchExceptionAsync(exception);
        }
    }
}
