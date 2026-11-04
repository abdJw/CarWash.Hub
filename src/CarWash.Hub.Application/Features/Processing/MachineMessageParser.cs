using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.Processing;

/// <summary>
/// Reads the usage events out of a stored message payload.
/// </summary>
/// <remarks>
/// Layer: Application. Pure: no I/O and no clock, so it is unit-tested on real payloads. One
/// implementation per known format; a firmware with a new format adds one, chosen by content.
/// </remarks>
public interface IMachineMessageParser
{
    /// <summary>Parses a payload.</summary>
    /// <param name="payload">The stored <c>data</c> text.</param>
    /// <returns>The events it carries, or why it carries none.</returns>
    MessageParseResult Parse(string payload);
}

/// <summary>What parsing made of a payload.</summary>
/// <remarks>Layer: Application.</remarks>
public enum MessageParseOutcome
{
    /// <summary>One or more usage events.</summary>
    Usage,

    /// <summary>A valid payload with nothing to record.</summary>
    Ignored,

    /// <summary>A payload that claims to report usage but cannot be read.</summary>
    Failed,

    /// <summary>
    /// A device report with no use in it - a boot, a water status or a settings acknowledgement -
    /// applied to the machine's state. Recorded as processed.
    /// </summary>
    Report,
}

/// <summary>The result of parsing one payload.</summary>
/// <param name="Outcome">What the payload turned out to be.</param>
/// <param name="Events">The usage events; empty unless <paramref name="Outcome"/> is <see cref="MessageParseOutcome.Usage"/>.</param>
/// <param name="Reason">Why nothing was recorded; null for usage.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record MessageParseResult(MessageParseOutcome Outcome, IReadOnlyList<ParsedUsageEvent> Events, string? Reason)
{
    /// <summary>
    /// Gets what the machine reported about itself, for the firmware that sends event messages;
    /// null for the earlier format, which reports uses only.
    /// </summary>
    public DeviceReport? Device { get; init; }

    /// <summary>A payload carrying usage.</summary>
    /// <param name="events">The events, at least one.</param>
    /// <returns>The result.</returns>
    public static MessageParseResult Usage(IReadOnlyList<ParsedUsageEvent> events) => new(MessageParseOutcome.Usage, events, null);

    /// <summary>A payload with nothing to record.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>The result.</returns>
    public static MessageParseResult Ignored(string reason) => new(MessageParseOutcome.Ignored, [], reason);

    /// <summary>A payload that could not be read.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>The result.</returns>
    public static MessageParseResult Failed(string reason) => new(MessageParseOutcome.Failed, [], reason);

    /// <summary>A device report with no use in it.</summary>
    /// <param name="device">What the machine reported.</param>
    /// <returns>The result.</returns>
    public static MessageParseResult Report(DeviceReport device) => new(MessageParseOutcome.Report, [], null) { Device = device };
}

/// <summary>One usage event read from a payload.</summary>
/// <param name="ServiceCode">The service, normalised to lower case.</param>
/// <param name="DeviceTime">The machine's reading time, in UTC, if it sent a readable one.</param>
/// <param name="ItemIndex">0 for a single event; its position for an event in an array.</param>
/// <param name="SaleAmount">The price the machine charged, in JOD; null when it does not report one.</param>
/// <param name="PaidAmount">The cash inserted, in JOD; null when not reported.</param>
/// <param name="DurationSeconds">The selected run time, in seconds; null when not reported.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ParsedUsageEvent(
    string ServiceCode,
    DateTimeOffset? DeviceTime,
    short ItemIndex,
    decimal? SaleAmount = null,
    decimal? PaidAmount = null,
    int? DurationSeconds = null);

/// <summary>What a machine running the event firmware reported about itself in one message.</summary>
/// <param name="Event">The event name, for example <c>water_status</c>.</param>
/// <param name="DeviceMac">The <c>device_mac</c> it claims; checked against the machine id by the processor.</param>
/// <param name="BootId">The id of the current start-up, if it sent a usable one.</param>
/// <param name="WaterAvailable">Whether water is available, if the message says.</param>
/// <param name="IsBoot">Whether this is the start-up report, <c>device_boot</c>.</param>
/// <param name="Ack">The settings acknowledgement, for <c>settings_ack</c>.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record DeviceReport(
    string Event,
    string? DeviceMac,
    string? BootId,
    bool? WaterAvailable,
    bool IsBoot,
    SettingsAckReport? Ack);

/// <summary>A machine's answer to a settings revision.</summary>
/// <param name="Revision">The revision it answers.</param>
/// <param name="Status">What it did with it.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record SettingsAckReport(long Revision, DeviceAckStatus Status);

/// <summary>
/// Chooses the parser by content: an object with a string <c>event</c> is the event firmware's
/// message; anything else is read as the earlier service-event format.
/// </summary>
/// <remarks>
/// Layer: Application. The event check comes first because a <c>payment_success</c> also carries a
/// <c>service</c> field, which the earlier parser would happily count - without its price.
/// </remarks>
/// <param name="legacy">The earlier format's parser.</param>
public sealed class MachineMessageParser(Esp32ServiceEventParser legacy) : IMachineMessageParser
{
    /// <inheritdoc />
    public MessageParseResult Parse(string payload) =>
        Esp32DeviceEventParser.TryParse(payload, out var result) ? result : legacy.Parse(payload);
}

/// <summary>
/// Parses the ESP32 controller's service event: <c>{"service":"soap","macAddress":"…","readAt":"2026-10-31T13:03:45Z"}</c>.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>Tolerant where it is safe to be.</b>
/// </para>
/// <list type="bullet">
///   <item><description>Property names are matched case-insensitively and service codes are lower-cased, so <c>"Service":"SOAP"</c> counts as soap.</description></item>
///   <item><description>An array of events is accepted, so firmware that batches is already understood; elements without a <c>service</c> are skipped.</description></item>
///   <item><description><c>readAt</c> may be ISO 8601 or Unix seconds or milliseconds. A missing or unreadable one is not an error - the receive time is used instead.</description></item>
///   <item><description>Text that is not JSON, and JSON with no <c>service</c>, is <see cref="MessageParseOutcome.Ignored"/>: Phase 1 accepted any payload, and none of it is wrong, it just isn't usage.</description></item>
/// </list>
/// <para>
/// <b>Strict where it matters.</b> A <c>service</c> that is not a valid code fails the message, so
/// a firmware bug shows up as failed messages instead of as a new service called <c>"soap "</c>.
/// </para>
/// </remarks>
public sealed partial class Esp32ServiceEventParser : IMachineMessageParser
{
    /// <summary>The most events read from one array payload.</summary>
    public const int MaxEventsPerMessage = 1000;

    private const string ServiceProperty = "service";
    private const string ReadAtProperty = "readAt";

    // Unix timestamps above this are milliseconds: 1e11 seconds is the year 5138.
    private const double MillisecondsThreshold = 1e11;

    /// <inheritdoc />
    public MessageParseResult Parse(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var start = payload.AsSpan().TrimStart();
        if (start.IsEmpty || start[0] is not ('{' or '['))
        {
            return MessageParseResult.Ignored("Not a JSON object or array.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return MessageParseResult.Ignored("Not valid JSON.");
        }

        using (document)
        {
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? ParseArray(document.RootElement)
                : ParseSingle(document.RootElement);
        }
    }

    /// <summary>Whether a text is a valid service code as it stands - lower case, no padding.</summary>
    /// <param name="code">The text.</param>
    /// <returns><see langword="true"/> for a valid code.</returns>
    public static bool IsServiceCode(string code) => ServiceCodePattern().IsMatch(code);

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,31}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ServiceCodePattern();

    private static MessageParseResult ParseSingle(JsonElement element)
    {
        var item = ReadEvent(element, 0, out var error);
        if (error is not null)
        {
            return MessageParseResult.Failed(error);
        }

        return item is null
            ? MessageParseResult.Ignored("No service field.")
            : MessageParseResult.Usage([item]);
    }

    private static MessageParseResult ParseArray(JsonElement array)
    {
        var events = new List<ParsedUsageEvent>();
        var index = 0;

        foreach (var element in array.EnumerateArray())
        {
            if (index >= MaxEventsPerMessage)
            {
                return MessageParseResult.Failed($"More than {MaxEventsPerMessage} events in one message.");
            }

            var item = ReadEvent(element, (short)index, out var error);
            if (error is not null)
            {
                return MessageParseResult.Failed($"Event {index}: {error}");
            }

            if (item is not null)
            {
                events.Add(item);
            }

            index++;
        }

        return events.Count > 0
            ? MessageParseResult.Usage(events)
            : MessageParseResult.Ignored("No service field in any array element.");
    }

    // Null with no error: an element that simply is not a usage event.
    private static ParsedUsageEvent? ReadEvent(JsonElement element, short itemIndex, out string? error)
    {
        error = null;
        if (element.ValueKind != JsonValueKind.Object || !TryGetProperty(element, ServiceProperty, out var service))
        {
            return null;
        }

        var code = service.ValueKind == JsonValueKind.String
            ? service.GetString()!.Trim().ToLowerInvariant()
            : null;

        if (code is null || code.Length > ServiceDefinition.CodeMaxLength || !ServiceCodePattern().IsMatch(code))
        {
            error = $"Invalid service code {Describe(service)}.";
            return null;
        }

        var readAt = TryGetProperty(element, ReadAtProperty, out var readAtValue) ? ReadTime(readAtValue) : null;
        return new ParsedUsageEvent(code, readAt, itemIndex);
    }

    private static DateTimeOffset? ReadTime(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                // A time without an offset is taken as UTC, which is what the firmware sends.
                return DateTimeOffset.TryParse(
                    value.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed)
                    ? parsed.ToUniversalTime()
                    : null;

            case JsonValueKind.Number when value.TryGetDouble(out var number) && number > 0:
                try
                {
                    return number >= MillisecondsThreshold
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)number)
                        : DateTimeOffset.FromUnixTimeSeconds((long)number);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }

            default:
                return null;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    // Quoted and clipped, so a huge or hostile value cannot bloat the stored error.
    private static string Describe(JsonElement value)
    {
        var text = value.GetRawText();
        return text.Length <= 40 ? text : string.Concat(text.AsSpan(0, 40), "…");
    }
}
