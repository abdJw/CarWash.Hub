using System.Text;
using System.Text.Json;

namespace CarWash.Hub.Application.Features.Ingestion;

/// <summary>
/// Turns a message's <c>data</c> into the text stored in <c>machine_messages.payload</c>.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>A JSON string is stored as its own text</b>, unescaped: a machine that sends
/// <c>"data": "{\"status\":\"Idle\"}"</c> is storing <c>{"status":"Idle"}</c>, and storing the
/// escaped, quoted form would make every later JSON query over the payloads fail. <b>Anything else
/// is stored as its raw JSON</b>, byte for byte as it appeared in the request.
/// </para>
/// </remarks>
public static class StoredPayload
{
    /// <summary>The largest stored payload accepted, in UTF-8 bytes.</summary>
    /// <remarks>
    /// A status message is a few hundred bytes; 64 KB leaves ample room while keeping a
    /// misbehaving device from filling the database.
    /// </remarks>
    public const int MaxBytes = 65_536;

    /// <summary>Returns the text to store for a message's data.</summary>
    /// <param name="data">The <c>data</c> value of the request.</param>
    /// <returns>The string's text for a JSON string; the raw JSON otherwise.</returns>
    public static string From(JsonElement data) =>
        data.ValueKind == JsonValueKind.String ? data.GetString() ?? string.Empty : data.GetRawText();

    /// <summary>Returns the size of a stored payload in UTF-8 bytes.</summary>
    /// <param name="payload">The stored text.</param>
    /// <returns>The byte count.</returns>
    public static int SizeOf(string payload) => Encoding.UTF8.GetByteCount(payload);
}
