using System.Text.Json;

namespace CarWash.Hub.Application.Features.Ingestion;

/// <summary>A machine's status message.</summary>
/// <param name="MachineId">
/// The machine's identifier: 1 to 100 letters, digits, <c>.</c>, <c>_</c>, <c>:</c> or <c>-</c>,
/// starting with a letter or digit. Matched case-insensitively; a new id registers the machine.
/// </param>
/// <param name="Data">
/// The machine's status, in any JSON shape - an object, an array, or a JSON string. Stored as text,
/// exactly as sent: a string's own text, anything else as its raw JSON.
/// </param>
/// <remarks>
/// Layer: Application. Both members are nullable so that a missing field is reported by
/// <see cref="IngestMessageRequestValidator"/> as a localized "required" message rather than as a
/// deserialization failure.
/// </remarks>
public sealed record IngestMessageRequest(string? MachineId, JsonElement? Data);

/// <summary>The outcome of storing a machine message.</summary>
/// <param name="MessageId">The stored message's identifier; read it back from <c>GET /api/v1/machine-messages/{id}</c>.</param>
/// <param name="MachineId">The machine it was stored against, spelled as first registered.</param>
/// <param name="ReceivedAt">When the API received it, in UTC.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record IngestMessageResult(long MessageId, string MachineId, DateTimeOffset ReceivedAt);
