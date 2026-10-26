using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CarWash.Hub.Application.Features.Ingestion;

/// <summary>Stores machine status messages.</summary>
/// <remarks>Layer: Application.</remarks>
public interface IIngestionService
{
    /// <summary>
    /// Registers the machine if it is new, and stores the message's data as text.
    /// </summary>
    /// <param name="request">The message, already validated by <see cref="IngestMessageRequestValidator"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored message's identity.</returns>
    Task<IngestMessageResult> IngestAsync(IngestMessageRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IIngestionService"/>.</summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>No explicit transaction.</b> Registering a new machine and storing the message are two saves.
/// If the second fails after the first succeeded, a machine exists with no message yet - harmless,
/// since the machine will retry and the registration is reused. Wrapping both in a transaction
/// would buy nothing and cost a round trip on every message.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="currentUser">The caller's key id, address and correlation id.</param>
/// <param name="dateTime">The receive timestamp.</param>
/// <param name="logger">Logger.</param>
public sealed class IngestionService(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    IDateTimeProvider dateTime,
    ILogger<IngestionService> logger) : IIngestionService
{
    private const int UserAgentMaxLength = 512;
    private const int ShortTextMaxLength = 64;

    /// <inheritdoc />
    public async Task<IngestMessageResult> IngestAsync(IngestMessageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator guarantees both; a caller that skipped it gets a clear failure, not a null.
        var code = request.MachineId?.Trim()
            ?? throw new ArgumentException("MachineId is required.", nameof(request));
        var data = request.Data
            ?? throw new ArgumentException("Data is required.", nameof(request));

        var payload = StoredPayload.From(data);
        var machine = await unitOfWork.Machines.GetOrRegisterAsync(code, cancellationToken).ConfigureAwait(false);

        var message = new MachineMessage
        {
            MachineId = machine.Id,
            ReceivedAt = dateTime.UtcNow,
            Payload = payload,
            PayloadSizeBytes = StoredPayload.SizeOf(payload),
            ClientKeyId = currentUser.ClientKeyId ?? "unknown",
            SourceIp = Truncate(currentUser.IpAddress, ShortTextMaxLength),
            UserAgent = Truncate(currentUser.UserAgent, UserAgentMaxLength),
            CorrelationId = Truncate(currentUser.CorrelationId, ShortTextMaxLength),
        };

        await unitOfWork.MachineMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Stored message {MessageId} for machine {MachineCode} ({PayloadSizeBytes} bytes, key {ClientKeyId})",
            message.Id, machine.Code, message.PayloadSizeBytes, message.ClientKeyId);

        return new IngestMessageResult(message.Id, machine.Code, message.ReceivedAt);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
