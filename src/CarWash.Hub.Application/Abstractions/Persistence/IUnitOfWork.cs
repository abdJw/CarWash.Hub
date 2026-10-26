using CarWash.Hub.Domain.Common;
using CarWash.Hub.Domain.Entities;

namespace CarWash.Hub.Application.Abstractions.Persistence;

/// <summary>
/// Groups repositories over one change-tracking scope and commits them together.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// All repositories exposed here share a single <c>DbContext</c>, so one
/// <see cref="SaveChangesAsync"/> commits every change across them in one transaction.
/// </para>
/// <para>
/// The named properties are convenience for the entities Phase 1 works with. New entities do
/// <em>not</em> need a property added here - call <see cref="Repository{TEntity, TKey}"/>
/// instead, which keeps this interface from growing a line per entity forever.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Gets the machine repository.</summary>
    IMachineRepository Machines { get; }

    /// <summary>Gets the machine message repository.</summary>
    IGenericRepository<MachineMessage, long> MachineMessages { get; }

    /// <summary>
    /// Returns a generic repository for any entity, created on demand and cached per scope.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The primary key type.</typeparam>
    /// <returns>A repository over <typeparamref name="TEntity"/>.</returns>
    /// <remarks>
    /// A new entity gets working CRUD, paging, projection and soft delete with no new interface,
    /// no new registration and no edit to this file.
    /// </remarks>
    IGenericRepository<TEntity, TKey> Repository<TEntity, TKey>()
        where TEntity : BaseEntity<TKey>
        where TKey : IEquatable<TKey>;

    /// <summary>
    /// Persists all staged changes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written.</returns>
    /// <remarks>
    /// Audit stamping happens inside this call, via an interceptor, so no caller has to remember
    /// to trigger it. A <c>rowversion</c> mismatch surfaces as a <c>ConflictException</c> with
    /// <c>Concurrency_Conflict</c>, and a unique-index violation as one with
    /// <c>Error_DuplicateValue</c>.
    /// </remarks>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets every pending change and every tracked entity, as if the unit of work were new.
    /// </summary>
    /// <remarks>
    /// After a save fails - a unique violation lost to a concurrent request, say - its changes
    /// must not ride along with the next save in the same scope.
    /// </remarks>
    void DiscardChanges();

    /// <summary>
    /// Runs work that spans several saves inside one transaction, retried as a whole on a
    /// transient database failure.
    /// </summary>
    /// <param name="work">The work. It may run more than once, so it must re-read the state it depends on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the transaction has committed.</returns>
    /// <remarks>
    /// <para>
    /// Only needed when one operation must span several <see cref="SaveChangesAsync"/> calls. A
    /// single save is already atomic and needs no explicit transaction.
    /// </para>
    /// <para>
    /// This replaces a plain <c>BeginTransactionAsync</c>, which cannot work here: the context is
    /// configured with <c>EnableRetryOnFailure</c>, and SQL Server's retrying execution strategy
    /// rejects a user-initiated transaction unless the whole unit is handed to the strategy, so
    /// that a retry can replay it from the start. Each attempt starts with a clean change tracker.
    /// </para>
    /// </remarks>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);
}
