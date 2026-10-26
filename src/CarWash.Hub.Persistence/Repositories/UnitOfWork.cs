using System.Collections.Concurrent;
using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Domain.Common;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CarWash.Hub.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>.
/// </summary>
/// <remarks>
/// Layer: Persistence.
/// <para>
/// Registered scoped, so every repository it exposes shares one <c>DbContext</c> and one change
/// tracker for the lifetime of a request. That is what makes a single
/// <see cref="SaveChangesAsync"/> commit work spanning several repositories atomically.
/// </para>
/// </remarks>
/// <param name="context">The database context.</param>
/// <param name="machines">The machine repository.</param>
public sealed class UnitOfWork(ApplicationDbContext context, IMachineRepository machines) : IUnitOfWork
{
    private readonly ConcurrentDictionary<Type, object> _adHocRepositories = new();

    /// <inheritdoc />
    public IMachineRepository Machines { get; } = machines;

    /// <inheritdoc />
    public IGenericRepository<MachineMessage, long> MachineMessages => Repository<MachineMessage, long>();

    /// <inheritdoc />
    public IGenericRepository<TEntity, TKey> Repository<TEntity, TKey>()
        where TEntity : BaseEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        // Cached per scope so repeated calls within one request share a repository instance.
        return (IGenericRepository<TEntity, TKey>)_adHocRepositories.GetOrAdd(
            typeof(TEntity),
            _ => new GenericRepository<TEntity, TKey>(context));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Translates the two expected database failures into the Application exception family, so
    /// they reach the client as a localized 409 and no layer above Persistence ever catches an EF
    /// Core type: a <c>rowversion</c> mismatch becomes <c>Concurrency_Conflict</c>, and a
    /// unique-index violation that slipped past a service's own check (a race between two
    /// requests) becomes <c>Error_DuplicateValue</c> rather than a 500.
    /// </remarks>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ResourceKeys.Errors.ConcurrencyConflict);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ConflictException(ResourceKeys.Errors.DuplicateValue);
        }
    }

    /// <inheritdoc />
    public void DiscardChanges() => context.ChangeTracker.Clear();

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The fix for a trap in cyberani_oms_api, whose <c>BeginTransactionAsync</c> opened the
    /// transaction directly on the database facade. With <c>EnableRetryOnFailure</c> configured,
    /// the next query then throws "the configured execution strategy does not support
    /// user-initiated transactions". The whole unit is therefore handed to the execution strategy,
    /// which can replay it from the start after a transient failure.
    /// </para>
    /// <para>
    /// Each attempt starts by clearing the change tracker, so entities staged by a failed attempt
    /// do not ride along - and be inserted twice - on the retry.
    /// </para>
    /// </remarks>
    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        return context.Database.CreateExecutionStrategy().ExecuteAsync(
            async token =>
            {
                context.ChangeTracker.Clear();

                var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                await using (transaction.ConfigureAwait(false))
                {
                    await work(token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    // 2601: duplicate key in a unique index. 2627: violation of a unique or primary key constraint.
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
