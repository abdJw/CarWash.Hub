using System.Text.Json;
using CarWash.Hub.Application.Common.Models;

namespace CarWash.Hub.Application.Features.Machines;

/// <summary>A machine known to the system.</summary>
/// <param name="Id">The internal identifier.</param>
/// <param name="MachineId">The identifier the machine sends, as first registered.</param>
/// <param name="DisplayName">An optional friendly name.</param>
/// <param name="GasStationId">The station it is installed at; null until assigned in Phase 2.</param>
/// <param name="IsActive">Whether the machine is in service.</param>
/// <param name="FirstSeenAt">When its first message arrived.</param>
/// <param name="LastSeenAt">When its latest message arrived; null if none was stored.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record MachineDto(
    Guid Id,
    string MachineId,
    string? DisplayName,
    Guid? GasStationId,
    bool IsActive,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastSeenAt);

/// <summary>A stored machine message.</summary>
/// <param name="Id">The message identifier.</param>
/// <param name="MachineId">The machine that sent it.</param>
/// <param name="ReceivedAt">When it arrived, in UTC.</param>
/// <param name="PayloadSizeBytes">The size of <paramref name="Payload"/> in UTF-8 bytes.</param>
/// <param name="ClientKeyId">The id of the configured key it was sent with - never the key.</param>
/// <param name="SourceIp">The sender's IP address.</param>
/// <param name="UserAgent">The sender's <c>User-Agent</c>.</param>
/// <param name="CorrelationId">The request's correlation id, to find it in the logs.</param>
/// <param name="Payload">The message's <c>data</c> as stored: a JSON string's text, or the raw JSON.</param>
/// <param name="PayloadJson">
/// The payload as JSON, when it is JSON, on the single-message and latest endpoints - so it can be
/// read without unescaping a string. Null otherwise; history lists carry only the text.
/// </param>
/// <remarks>Layer: Application.</remarks>
public sealed record MachineMessageDto(
    long Id,
    string MachineId,
    DateTimeOffset ReceivedAt,
    int PayloadSizeBytes,
    string ClientKeyId,
    string? SourceIp,
    string? UserAgent,
    string? CorrelationId,
    string Payload,
    JsonElement? PayloadJson);

/// <summary>Filters for the machine list.</summary>
/// <remarks>
/// Layer: Application. <see cref="PagedRequest.SearchTerm"/> matches the machine id or display name;
/// <see cref="PagedRequest.SortBy"/> accepts <c>machineId</c> (default), <c>firstSeenAt</c> and
/// <c>lastSeenAt</c>.
/// </remarks>
public sealed record MachineListQuery : PagedRequest
{
    /// <summary>Gets an optional filter on whether the machine is in service.</summary>
    public bool? IsActive { get; init; }
}

/// <summary>The window of a machine's message history, newest first.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed record MachineHistoryQuery : PageRequest
{
    /// <summary>Gets the earliest receive time included, inclusive.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Gets the latest receive time included, inclusive.</summary>
    public DateTimeOffset? To { get; init; }
}
