using CarWash.Hub.Application.Features.Pricing;
using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.DeviceSettings;

/// <summary>Where a machine's settings stand, for the dashboard.</summary>
/// <remarks>Layer: Application.</remarks>
public enum DeviceSettingsStatus
{
    /// <summary>No settings have been issued yet, and none are due.</summary>
    NotConfigured = 0,

    /// <summary>The machine confirmed it runs the desired revision.</summary>
    Applied = 1,

    /// <summary>A revision was issued and the machine has not confirmed it yet.</summary>
    Pending = 2,

    /// <summary>The machine was busy and will apply the desired revision when idle.</summary>
    Busy = 3,

    /// <summary>The machine refused or could not store the desired revision.</summary>
    Failed = 4,

    /// <summary>
    /// The station's current values cannot be sent (a price or run time missing or invalid); the
    /// machine keeps what it last confirmed.
    /// </summary>
    Blocked = 5,
}

/// <summary>Builds a station's settings snapshot, applies acknowledgements, and says where a machine stands.</summary>
/// <remarks>Layer: Application. Pure.</remarks>
public static class DeviceSettingsRules
{
    /// <summary>Builds the settings a station's machines should run today.</summary>
    /// <param name="stationId">The station.</param>
    /// <param name="prices">The station's live prices.</param>
    /// <param name="today">Today's business date, which decides the price in effect.</param>
    /// <param name="durations">The station's run times.</param>
    /// <param name="issue">Why there is no complete snapshot, when there is not.</param>
    /// <param name="issueService">The service the issue is about.</param>
    /// <returns>Every configurable service's settings, or null when one is missing or invalid.</returns>
    public static IReadOnlyList<DeviceServiceSettings>? TryBuild(
        Guid stationId,
        PriceBook prices,
        DateOnly today,
        IReadOnlyCollection<StationServiceDuration> durations,
        out DeviceSettingsIssue issue,
        out string? issueService)
    {
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(durations);

        var services = new List<DeviceServiceSettings>(DeviceServices.All.Count);

        foreach (var service in DeviceServices.All)
        {
            issueService = service.Code;

            if (prices.PriceAt(stationId, service.Code, today) is not { } price)
            {
                issue = DeviceSettingsIssue.MissingPrice;
                return null;
            }

            if (DeviceServices.ToQirsh(price) is not { } qirsh)
            {
                issue = DeviceSettingsIssue.InvalidPrice;
                return null;
            }

            var seconds = durations
                .FirstOrDefault(row => row.GasStationId == stationId && string.Equals(row.ServiceCode, service.Code, StringComparison.OrdinalIgnoreCase))
                ?.Seconds();

            if (seconds is null || seconds.Count != service.Positions || !seconds.All(DeviceServices.IsDuration))
            {
                issue = DeviceSettingsIssue.MissingDurations;
                return null;
            }

            services.Add(new DeviceServiceSettings(service.Code, qirsh, seconds));
        }

        issue = DeviceSettingsIssue.None;
        issueService = null;
        return services;
    }

    /// <summary>Issues a new revision for changed values.</summary>
    /// <param name="settings">The machine's settings row.</param>
    /// <param name="servicesJson">The new <c>services</c> object.</param>
    /// <param name="now">When.</param>
    /// <returns><see langword="true"/> if a revision was issued; false when the values are unchanged or the revisions ran out.</returns>
    public static bool Publish(MachineDeviceSettings settings, string servicesJson, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.Equals(settings.ServicesJson, servicesJson, StringComparison.Ordinal))
        {
            return false;
        }

        // Above anything a machine has ever reported, so new values never share a revision with old ones.
        var next = Math.Max(settings.DesiredRevision, settings.MaxReportedRevision) + 1;
        if (next > MachineDeviceSettings.MaxRevision)
        {
            return false;
        }

        settings.DesiredRevision = next;
        settings.ServicesJson = servicesJson;
        settings.DesiredAt = now;
        return true;
    }

    /// <summary>Applies a machine's acknowledgement of a revision.</summary>
    /// <param name="settings">The machine's settings row.</param>
    /// <param name="ack">The acknowledgement.</param>
    /// <param name="messageId">The message it came in, which orders acknowledgements.</param>
    /// <param name="receivedAt">When that message arrived.</param>
    /// <remarks>
    /// Only <c>saved</c> or <c>unchanged</c> marks a revision applied, only one the server actually
    /// issued, and never one older than the applied revision - so an old reply arriving late cannot
    /// undo a newer confirmation. The latest acknowledgement shown follows message order.
    /// </remarks>
    public static void ApplyAck(MachineDeviceSettings settings, SettingsAckReport ack, long messageId, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ack);

        if (settings.LastAckMessageId is null || messageId > settings.LastAckMessageId)
        {
            settings.LastAckRevision = ack.Revision;
            settings.LastAckStatus = ack.Status;
            settings.LastAckAt = receivedAt;
            settings.LastAckMessageId = messageId;
        }

        settings.MaxReportedRevision = Math.Max(settings.MaxReportedRevision, ack.Revision);

        if (ack.Status is DeviceAckStatus.Saved or DeviceAckStatus.Unchanged
            && ack.Revision > settings.AppliedRevision
            && ack.Revision <= settings.DesiredRevision)
        {
            settings.AppliedRevision = ack.Revision;
            settings.AppliedAt = receivedAt;
        }
    }

    /// <summary>Says where a machine's settings stand.</summary>
    /// <param name="settings">The machine's settings row; null when it has none.</param>
    /// <returns>The status.</returns>
    /// <remarks>
    /// Blocked comes first: the machine may still run an applied revision, but the station's
    /// current values are not reaching it, and that needs someone's attention.
    /// </remarks>
    public static DeviceSettingsStatus StatusOf(MachineDeviceSettings? settings)
    {
        if (settings is null)
        {
            return DeviceSettingsStatus.NotConfigured;
        }

        if (settings.Issue != DeviceSettingsIssue.None)
        {
            return DeviceSettingsStatus.Blocked;
        }

        if (settings.DesiredRevision == 0)
        {
            return DeviceSettingsStatus.NotConfigured;
        }

        if (settings.AppliedRevision >= settings.DesiredRevision)
        {
            return DeviceSettingsStatus.Applied;
        }

        if (settings.LastAckRevision == settings.DesiredRevision)
        {
            switch (settings.LastAckStatus)
            {
                case DeviceAckStatus.Busy:
                    return DeviceSettingsStatus.Busy;
                case DeviceAckStatus.StorageError or DeviceAckStatus.InvalidSettings or DeviceAckStatus.InvalidCommand
                    or DeviceAckStatus.InvalidJson or DeviceAckStatus.Unknown:
                    return DeviceSettingsStatus.Failed;
            }
        }

        return DeviceSettingsStatus.Pending;
    }
}
