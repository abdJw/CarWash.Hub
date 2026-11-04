using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Exceptions;
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
/// <para>
/// <b>Parsing happens elsewhere.</b> The message is stored as pending and the processing worker is
/// woken; the 201 never waits for parsing, and nothing downstream can make ingestion fail.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="currentUser">The caller's key id, address and correlation id.</param>
/// <param name="dateTime">The receive timestamp.</param>
/// <param name="processingTrigger">Wakes the processing worker.</param>
/// <param name="logger">Logger.</param>
public sealed class IngestionService(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    IDateTimeProvider dateTime,
    IMessageProcessingTrigger processingTrigger,
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
        var deviceEventId = DeviceEventId.TryRead(data);
        var machine = await unitOfWork.Machines.GetOrRegisterAsync(code, cancellationToken).ConfigureAwait(false);

        if (deviceEventId is not null
            && await FindOriginalAsync(machine.Id, deviceEventId, cancellationToken).ConfigureAwait(false) is { } earlier)
        {
            return Duplicate(earlier, machine, deviceEventId, payload);
        }

        var message = new MachineMessage
        {
            MachineId = machine.Id,
            ReceivedAt = dateTime.UtcNow,
            Payload = payload,
            PayloadSizeBytes = StoredPayload.SizeOf(payload),
            DeviceEventId = deviceEventId,
            ClientKeyId = currentUser.ClientKeyId ?? "unknown",
            SourceIp = Truncate(currentUser.IpAddress, ShortTextMaxLength),
            UserAgent = Truncate(currentUser.UserAgent, UserAgentMaxLength),
            CorrelationId = Truncate(currentUser.CorrelationId, ShortTextMaxLength),
        };

        await unitOfWork.MachineMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ConflictException) when (deviceEventId is not null)
        {
            // Lost the race to the original: the retry arrived while the first attempt was still
            // being stored. The unique index let exactly one row in; answer with that one. Never a
            // 409 - the firmware would treat it as a configuration error and stop posting.
            unitOfWork.DiscardChanges();

            var original = await FindOriginalAsync(machine.Id, deviceEventId, cancellationToken).ConfigureAwait(false)
                           ?? throw new InvalidOperationException(
                               $"Event '{deviceEventId}' of machine '{machine.Code}' violated the unique index but could not be read back.");

            return Duplicate(original, machine, deviceEventId, payload);
        }

        processingTrigger.Signal();

        logger.LogInformation(
            "Stored message {MessageId} for machine {MachineCode} ({PayloadSizeBytes} bytes, key {ClientKeyId})",
            message.Id, machine.Code, message.PayloadSizeBytes, message.ClientKeyId);

        return new IngestMessageResult(message.Id, machine.Code, message.ReceivedAt);
    }

    private async Task<OriginalMessage?> FindOriginalAsync(Guid machineId, string deviceEventId, CancellationToken cancellationToken)
    {
        var rows = await unitOfWork.MachineMessages.ListAsync(
                new MessageByDeviceEventSpecification(machineId, deviceEventId),
                message => new OriginalMessage(message.Id, message.ReceivedAt, message.Payload),
                cancellationToken)
            .ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0];
    }

    private IngestMessageResult Duplicate(OriginalMessage original, MachineRegistration machine, string deviceEventId, string payload)
    {
        if (string.Equals(original.Payload, payload, StringComparison.Ordinal))
        {
            logger.LogInformation(
                "Event {DeviceEventId} of machine {MachineCode} was already stored as message {MessageId}; not stored again",
                deviceEventId, machine.Code, original.Id);
        }
        else
        {
            // Same id, different content: not a retry but a collision - a firmware that reused an
            // event id, for example after a boot id repeated. The original stands; this one is lost,
            // which is why it is worth a warning.
            logger.LogWarning(
                "Event {DeviceEventId} of machine {MachineCode} repeats message {MessageId} with different data; the new data was not stored: {Payload}",
                deviceEventId, machine.Code, original.Id, payload);
        }

        return new IngestMessageResult(original.Id, machine.Code, original.ReceivedAt, Duplicate: true);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private sealed record OriginalMessage(long Id, DateTimeOffset ReceivedAt, string Payload);
}
