using System.Text.Json;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.Processing;

/// <summary>
/// Parses the event firmware's message (ESP32 + A7670G, protocol baseline 2026-11-03):
/// <c>{"event":"payment_success","device_mac":"…","boot_id":"…","event_id":"…",…}</c>.
/// </summary>
/// <remarks>
/// Layer: Application. Pure.
/// <para>
/// <b>Exact.</b> Field names, event names and service codes are case-sensitive in this contract, so
/// they are matched exactly; <c>"Water"</c> is not a service.
/// </para>
/// <para>
/// <b>Strict on money, lenient elsewhere.</b> A <c>payment_success</c> with an invalid service, price
/// or paid amount fails the message, so no financial row is written from it. A bad
/// <c>duration_seconds</c>, <c>water_available</c> or <c>boot_id</c> is only dropped: the sale
/// still happened. Either way the HTTP answer was already 201 - the firmware stops posting on a 400,
/// so validation never happens at the door.
/// </para>
/// <para>
/// <c>uptime_ms</c> is elapsed device time, not a clock, so every event is timed by its arrival.
/// </para>
/// </remarks>
public static class Esp32DeviceEventParser
{
    /// <summary>A payment accepted and its service started - the only event that is a sale.</summary>
    public const string PaymentSuccess = "payment_success";

    /// <summary>The machine started and has connectivity.</summary>
    public const string DeviceBoot = "device_boot";

    /// <summary>Water availability, on change and about every 30 seconds.</summary>
    public const string WaterStatus = "water_status";

    /// <summary>The machine's answer to a settings revision.</summary>
    public const string SettingsAck = "settings_ack";

    /// <summary>The largest amount accepted, in qirsh (JOD 1,000): far above any price, so only a garbled number is refused.</summary>
    public const int MaxQirsh = 100_000;

    /// <summary>The longest run time kept, in seconds.</summary>
    public const int MaxDurationSeconds = 86_400;

    private const string EventProperty = "event";

    /// <summary>
    /// Parses the payload if it is an event message - a JSON object with a string <c>event</c>.
    /// </summary>
    /// <param name="payload">The stored <c>data</c> text.</param>
    /// <param name="result">The result, when it is an event message.</param>
    /// <returns>Whether the payload is an event message; if not, another format's parser should read it.</returns>
    public static bool TryParse(string payload, out MessageParseResult result)
    {
        ArgumentNullException.ThrowIfNull(payload);
        result = null!;

        var start = payload.AsSpan().TrimStart();
        if (start.IsEmpty || start[0] != '{')
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var data = document.RootElement;
            if (!data.TryGetProperty(EventProperty, out var name) || name.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            result = Parse(data, name.GetString()!);
            return true;
        }
    }

    private static MessageParseResult Parse(JsonElement data, string name)
    {
        var device = new DeviceReport(
            name,
            DeviceMac: data.TryGetProperty("device_mac", out var mac) && mac.ValueKind == JsonValueKind.String ? mac.GetString() : null,
            BootId: ReadBootId(data),
            WaterAvailable: ReadBool(data, "water_available"),
            IsBoot: name == DeviceBoot,
            Ack: null);

        return name switch
        {
            PaymentSuccess => ParsePayment(data, device),
            DeviceBoot => MessageParseResult.Report(device),
            WaterStatus => device.WaterAvailable is null
                ? MessageParseResult.Failed("water_status without a true or false water_available.") with { Device = device }
                : MessageParseResult.Report(device),
            SettingsAck => ParseAck(data, device),
            _ => MessageParseResult.Ignored($"Unknown event {Describe(name)}."),
        };
    }

    private static MessageParseResult ParsePayment(JsonElement data, DeviceReport device)
    {
        if (!data.TryGetProperty("service", out var service)
            || service.ValueKind != JsonValueKind.String
            || service.GetString() is not { } code
            || code.Length > ServiceDefinition.CodeMaxLength
            || !Esp32ServiceEventParser.IsServiceCode(code))
        {
            return Failure($"Invalid service {DescribeProperty(data, "service")}.");
        }

        if (ReadInt(data, "price_qirsh", 0, MaxQirsh) is not { } price)
        {
            return Failure($"price_qirsh must be a whole number from 0 to {MaxQirsh}; got {DescribeProperty(data, "price_qirsh")}.");
        }

        if (ReadInt(data, "paid_qirsh", 0, MaxQirsh) is not { } paid)
        {
            return Failure($"paid_qirsh must be a whole number from 0 to {MaxQirsh}; got {DescribeProperty(data, "paid_qirsh")}.");
        }

        if (paid < price)
        {
            return Failure($"paid_qirsh {paid} is less than price_qirsh {price}.");
        }

        var usage = new ParsedUsageEvent(
            code,
            DeviceTime: null,
            ItemIndex: 0,
            SaleAmount: price / 100m,
            PaidAmount: paid / 100m,
            DurationSeconds: ReadInt(data, "duration_seconds", 1, MaxDurationSeconds));

        return MessageParseResult.Usage([usage]) with { Device = device };

        MessageParseResult Failure(string reason) => MessageParseResult.Failed(reason) with { Device = device };
    }

    private static MessageParseResult ParseAck(JsonElement data, DeviceReport device)
    {
        if (!data.TryGetProperty("revision", out var revisionValue)
            || revisionValue.ValueKind != JsonValueKind.Number
            || !revisionValue.TryGetInt64(out var revision)
            || revision is < 0 or > uint.MaxValue)
        {
            return MessageParseResult.Failed(
                $"settings_ack without a revision from 0 to {uint.MaxValue}; got {DescribeProperty(data, "revision")}.") with { Device = device };
        }

        if (!data.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
        {
            return MessageParseResult.Failed("settings_ack without a status.") with { Device = device };
        }

        return MessageParseResult.Report(device with { Ack = new SettingsAckReport(revision, AckStatusOf(status.GetString()!)) });
    }

    /// <summary>Maps a <c>settings_ack.status</c> to its meaning; an unknown status is <see cref="DeviceAckStatus.Unknown"/>.</summary>
    /// <param name="status">The status as sent.</param>
    /// <returns>The status.</returns>
    public static DeviceAckStatus AckStatusOf(string status) => status switch
    {
        "saved" => DeviceAckStatus.Saved,
        "unchanged" => DeviceAckStatus.Unchanged,
        "busy" => DeviceAckStatus.Busy,
        "storage_error" => DeviceAckStatus.StorageError,
        "invalid_settings" => DeviceAckStatus.InvalidSettings,
        "invalid_command" => DeviceAckStatus.InvalidCommand,
        "invalid_json" => DeviceAckStatus.InvalidJson,
        _ => DeviceAckStatus.Unknown,
    };

    private static int? ReadInt(JsonElement data, string name, int min, int max) =>
        data.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
        && number >= min
        && number <= max
            ? number
            : null;

    private static bool? ReadBool(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static string? ReadBootId(JsonElement data)
    {
        if (!data.TryGetProperty("boot_id", out var value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { Length: > 0 and <= MachineState.BootIdMaxLength } id)
        {
            return null;
        }

        foreach (var character in id)
        {
            if (character is < '!' or > '~')
            {
                return null;
            }
        }

        return id;
    }

    private static string DescribeProperty(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) ? Describe(value.GetRawText()) : "nothing";

    // Quoted and clipped, so a huge or hostile value cannot bloat the stored error.
    private static string Describe(string text) =>
        text.Length <= 40 ? $"'{text}'" : $"'{text[..40]}…'";
}
