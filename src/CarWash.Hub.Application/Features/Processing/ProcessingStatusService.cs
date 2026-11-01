using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.Processing;

/// <summary>Reports how far behind message processing is.</summary>
/// <remarks>Layer: Application.</remarks>
public interface IProcessingStatusService
{
    /// <summary>Returns the processing backlog and the worker's state.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status.</returns>
    Task<ProcessingStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>The processing backlog and the worker's state.</summary>
/// <param name="PendingCount">Messages waiting to be processed.</param>
/// <param name="FailedCount">Messages that could not be processed.</param>
/// <param name="OldestPendingReceivedAt">When the oldest waiting message arrived; null when none is waiting.</param>
/// <param name="Worker">The worker in the process that answered.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ProcessingStatusDto(
    int PendingCount,
    int FailedCount,
    DateTimeOffset? OldestPendingReceivedAt,
    ProcessingWorkerSnapshot Worker);

/// <summary>The default <see cref="IProcessingStatusService"/>.</summary>
/// <remarks>Layer: Application. Each count is a scan of a filtered index that holds only those rows.</remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="monitor">The worker's state in this process.</param>
public sealed class ProcessingStatusService(IUnitOfWork unitOfWork, IProcessingMonitor monitor) : IProcessingStatusService
{
    /// <inheritdoc />
    public async Task<ProcessingStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var messages = unitOfWork.MachineMessages;

        var pending = await messages
            .CountAsync(new MessagesByStatusSpecification(ProcessingStatus.Pending), cancellationToken)
            .ConfigureAwait(false);

        var failed = await messages
            .CountAsync(new MessagesByStatusSpecification(ProcessingStatus.Failed), cancellationToken)
            .ConfigureAwait(false);

        var oldest = pending == 0
            ? []
            : await messages
                .ListAsync(
                    new MessagesByStatusSpecification(ProcessingStatus.Pending, oldestFirst: true),
                    message => (DateTimeOffset?)message.ReceivedAt,
                    cancellationToken)
                .ConfigureAwait(false);

        return new ProcessingStatusDto(pending, failed, oldest.Count > 0 ? oldest[0] : null, monitor.GetSnapshot());
    }
}
