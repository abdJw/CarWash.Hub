using CarWash.Hub.Domain.Common;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// One status message pushed by a machine: the <c>data</c> it sent, stored exactly as received.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// <b>Raw by design.</b> The engineers have not fixed the content of <c>data</c> yet, so Phase 1
/// keeps it verbatim in <see cref="Payload"/>. Once the real format is known, Phase 2 parses these
/// rows into a structured <c>MachineStatusLog</c> - and can re-parse the whole history whenever the
/// parser changes, which is why nothing is ever thrown away here.
/// </para>
/// <para>
/// Append-only, with a database-generated <c>bigint</c> key: rows are always read newest-first per
/// machine, so a monotonic identity is the right clustering. Not soft-deletable and not
/// row-versioned - a message is never edited.
/// </para>
/// </remarks>
public class MachineMessage : BaseEntity<long>
{
    /// <summary>Gets or sets the machine that sent the message.</summary>
    public Guid MachineId { get; set; }

    /// <summary>Gets or sets the machine that sent the message.</summary>
    public Machine Machine { get; set; } = null!;

    /// <summary>Gets or sets when the API received the message, in UTC.</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>
    /// Gets or sets the message's <c>data</c> as text: a JSON string's own text, or the raw JSON of
    /// an object, array or other value.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Gets or sets the size of <see cref="Payload"/> in UTF-8 bytes.</summary>
    public int PayloadSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the id of the configured client key the request was sent with. Never the key
    /// itself. Shows, during a key rotation, which machines still send the old key.
    /// </summary>
    public string ClientKeyId { get; set; } = string.Empty;

    /// <summary>Gets or sets the client's IP address.</summary>
    public string? SourceIp { get; set; }

    /// <summary>Gets or sets the client's <c>User-Agent</c>, often the firmware name and version.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Gets or sets the request's correlation identifier, to find it in the logs.</summary>
    public string? CorrelationId { get; set; }
}
