using System.Globalization;
using CarWash.Hub.Application.Features.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.IntegrationTests.Infrastructure;

/// <summary>Drives message processing in tests, where the background worker is switched off.</summary>
public static class ProcessingDriver
{
    /// <summary>Runs the processor until no pending message is left, as the worker would.</summary>
    /// <param name="factory">The host.</param>
    /// <returns>The number of messages processed.</returns>
    public static async Task<int> ProcessAllPendingAsync(this ApiFactory factory)
    {
        var total = 0;

        // Bounded, so a processor bug that never drains the queue fails the test instead of hanging it.
        for (var batch = 0; batch < 10_000; batch++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IMachineStateProcessor>()
                .ProcessPendingAsync(TestContext.Current.CancellationToken);

            total += result.Messages;
            if (!result.HasMore)
            {
                return total;
            }
        }

        throw new InvalidOperationException("The processing queue did not drain.");
    }

    /// <summary>The <c>data</c> of a controller's service event, as the ESP32 firmware sends it.</summary>
    /// <param name="service">The service code.</param>
    /// <param name="readAt">The machine's reading time.</param>
    /// <param name="machineId">The MAC address the payload repeats.</param>
    /// <returns>The data JSON.</returns>
    public static string ServiceEvent(string service, DateTimeOffset readAt, string machineId = "B0:CB:D8:D0:64:2C") =>
        $$"""{"service":"{{service}}","macAddress":"{{machineId}}","readAt":"{{readAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}}"}""";

    /// <summary>The <c>data</c> of an event-firmware message, with the common fields filled in.</summary>
    /// <param name="machineId">The machine id, repeated as <c>device_mac</c>.</param>
    /// <param name="eventName">The event, for example <c>water_status</c>.</param>
    /// <param name="fields">The event's own fields, as JSON members without braces; empty for none.</param>
    /// <param name="eventId">The event id; a fresh one when null.</param>
    /// <returns>The data JSON.</returns>
    public static string DeviceEvent(string machineId, string eventName, string fields, string? eventId = null)
    {
        eventId ??= $"{machineId}-0123456789ABCDEF-{eventName}-{Guid.NewGuid():N}";
        var extra = fields.Length == 0 ? string.Empty : "," + fields;
        return $$"""{"event":"{{eventName}}","device_mac":"{{machineId}}","boot_id":"0123456789ABCDEF","event_id":"{{eventId}}"{{extra}}}""";
    }

    /// <summary>The <c>data</c> of a <c>payment_success</c>.</summary>
    /// <param name="machineId">The machine id.</param>
    /// <param name="service">The service code.</param>
    /// <param name="priceQirsh">The price, in qirsh.</param>
    /// <param name="paidQirsh">The cash inserted, in qirsh.</param>
    /// <param name="durationSeconds">The selected run time.</param>
    /// <returns>The data JSON.</returns>
    public static string Payment(string machineId, string service, int priceQirsh, int paidQirsh, int durationSeconds = 180) =>
        DeviceEvent(
            machineId,
            "payment_success",
            Json($"'service':'{service}','price_qirsh':{priceQirsh},'paid_qirsh':{paidQirsh},'duration_seconds':{durationSeconds},'uptime_ms':120000,'water_available':true"));

    /// <summary>The <c>data</c> of a <c>water_status</c>.</summary>
    /// <param name="machineId">The machine id.</param>
    /// <param name="waterAvailable">Whether water is available.</param>
    /// <returns>The data JSON.</returns>
    public static string WaterStatus(string machineId, bool waterAvailable) =>
        DeviceEvent(machineId, "water_status", Json($"'water_available':{(waterAvailable ? "true" : "false")},'uptime_ms':30000"));

    /// <summary>The <c>data</c> of a <c>settings_ack</c>.</summary>
    /// <param name="machineId">The machine id.</param>
    /// <param name="revision">The revision acknowledged.</param>
    /// <param name="status">The status, for example <c>saved</c>.</param>
    /// <returns>The data JSON.</returns>
    public static string SettingsAck(string machineId, long revision, string status) =>
        DeviceEvent(machineId, "settings_ack", Json($"'revision':{revision},'status':'{status}','water_available':true"));

    /// <summary>Turns single quotes into double quotes, so JSON fragments read cleanly in C#.</summary>
    /// <param name="text">The fragment, quoted with <c>'</c>.</param>
    /// <returns>The JSON.</returns>
    public static string Json(string text) => text.Replace('\'', '"');
}
