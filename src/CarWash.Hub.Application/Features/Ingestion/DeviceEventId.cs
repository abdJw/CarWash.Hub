using System.Text.Json;
using CarWash.Hub.Domain.Entities;

namespace CarWash.Hub.Application.Features.Ingestion;

/// <summary>
/// Reads the device's own event id, <c>data.event_id</c>, which makes a retried event recognisable.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// The ESP32 firmware retries a POST after a network failure or a 5xx, with the same
/// <c>event_id</c> each time - for example
/// <c>AA:BB:CC:DD:EE:FF-0123456789ABCDEF-payment_success-1</c>. The contract is that a repeated
/// event is answered 201 again but stored, and counted, once.
/// </para>
/// <para>
/// <b>Lenient by design.</b> Anything that is not a usable id - <c>data</c> that is not an object, a
/// missing or non-string <c>event_id</c>, one that is blank, too long, or not printable ASCII - is
/// simply no id: the message is stored and processed like the earlier firmware's, never rejected. A
/// 400 would stop the device posting until it is restarted.
/// </para>
/// </remarks>
public static class DeviceEventId
{
    /// <summary>The <c>data</c> property holding the id; matched exactly, as the firmware sends it.</summary>
    public const string PropertyName = "event_id";

    /// <summary>Returns the event id in a message's data, or null when there is no usable one.</summary>
    /// <param name="data">The <c>data</c> value of the request.</param>
    /// <returns>The id, or null.</returns>
    public static string? TryRead(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty(PropertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var id = value.GetString();
        if (string.IsNullOrEmpty(id) || id.Length > MachineMessage.DeviceEventIdMaxLength)
        {
            return null;
        }

        foreach (var character in id)
        {
            // Printable ASCII only: the column is varchar, and a space or control character is a
            // firmware bug, not an id worth deduplicating on.
            if (character is < '!' or > '~')
            {
                return null;
            }
        }

        return id;
    }
}
