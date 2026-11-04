using CarWash.Hub.Domain.Common;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// One status message pushed by a machine: the <c>data</c> it sent, stored exactly as received.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// <b>Raw by design.</b> <see cref="Payload"/> keeps <c>data</c> verbatim. The processing worker
/// parses each row into <see cref="ServiceUsage"/> events and records the outcome in
/// <see cref="ProcessingStatus"/>; because the raw text is never thrown away, a parser fix can
/// re-parse the whole history by resetting that status to pending.
/// </para>
/// <para>
/// Append-only, with a database-generated <c>bigint</c> key: rows are always read newest-first per
/// machine, so a monotonic identity is the right clustering. Not soft-deletable and not
/// row-versioned. The payload is never edited; only the processing columns are written, and only by
/// the worker.
/// </para>
/// </remarks>
public class MachineMessage : BaseEntity<long>
{
    /// <summary>The longest processing error kept.</summary>
    public const int ProcessingErrorMaxLength = 500;

    /// <summary>The longest device event id kept; a longer one is stored without it, so is never deduplicated.</summary>
    public const int DeviceEventIdMaxLength = 128;

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
    /// Gets or sets the device's own id for the event, <c>data.event_id</c>, when it sent one. Unique
    /// per machine: a retried event carries the same id and is answered with the original message
    /// instead of being stored again. Null for the earlier firmware format, which has no such id.
    /// </summary>
    public string? DeviceEventId { get; set; }

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

    /// <summary>Gets or sets where the message is in the parsing pipeline.</summary>
    public ProcessingStatus ProcessingStatus { get; set; }

    /// <summary>Gets or sets when the worker last processed the message, in UTC; null while pending.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Gets or sets why processing failed or the message was ignored; null otherwise.</summary>
    public string? ProcessingError { get; set; }
}
