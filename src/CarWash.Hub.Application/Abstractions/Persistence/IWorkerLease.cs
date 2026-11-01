namespace CarWash.Hub.Application.Abstractions.Persistence;

/// <summary>
/// Grants one process at a time the right to run a background worker.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>Why it exists on a single-instance host.</b> IIS recycles an application pool by starting
/// the new process before the old one stops, and a deploy does the same, so for a few seconds two
/// copies of every worker run against one database. The processing worker is the single writer of
/// derived state; a lease held in the database keeps it single across processes, too. The lease is
/// released automatically if its process dies.
/// </para>
/// </remarks>
public interface IWorkerLease
{
    /// <summary>Tries to take the named lease without waiting.</summary>
    /// <param name="name">The lease name, one per worker.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The held lease, to dispose when done; or null when another process holds it.</returns>
    Task<IWorkerLeaseHandle?> TryAcquireAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>A held worker lease. Disposing it releases the lease.</summary>
/// <remarks>Layer: Application.</remarks>
public interface IWorkerLeaseHandle : IAsyncDisposable
{
    /// <summary>Gets the lease name.</summary>
    string Name { get; }

    /// <summary>
    /// Checks that the lease is still held - a dropped database connection loses it - so a worker
    /// can stop before writing anything without it.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> while the lease is held.</returns>
    Task<bool> IsHeldAsync(CancellationToken cancellationToken = default);
}
