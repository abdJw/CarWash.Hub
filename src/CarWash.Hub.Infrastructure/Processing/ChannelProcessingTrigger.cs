using System.Threading.Channels;
using CarWash.Hub.Application.Abstractions.Services;

namespace CarWash.Hub.Infrastructure.Processing;

/// <summary>
/// An in-process wake-up signal for the processing worker.
/// </summary>
/// <remarks>
/// Layer: Infrastructure.
/// <para>
/// A bounded channel of one, dropping writes when full: a thousand messages arriving while the
/// worker is busy leave exactly one signal behind, and the worker's next batch picks all of them up
/// from the database anyway. Ingestion therefore never waits on, allocates for, or fails because of
/// the worker.
/// </para>
/// </remarks>
public sealed class ChannelProcessingTrigger : IMessageProcessingTrigger
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
    });

    /// <inheritdoc />
    public void Signal() => _channel.Writer.TryWrite(true);

    /// <summary>Waits for a signal, or for the timeout to pass.</summary>
    /// <param name="timeout">The longest wait - the sweep interval.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns><see langword="true"/> when woken by a signal; <see langword="false"/> on timeout.</returns>
    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await _channel.Reader.ReadAsync(timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
