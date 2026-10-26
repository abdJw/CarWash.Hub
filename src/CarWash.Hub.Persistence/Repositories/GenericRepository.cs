using System.Linq.Expressions;
using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Common.Models;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Domain.Common;
using CarWash.Hub.Persistence.Context;
using CarWash.Hub.Persistence.Specifications;
using Microsoft.EntityFrameworkCore;

namespace CarWash.Hub.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IGenericRepository{TEntity, TKey}"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <remarks>
/// Layer: Persistence.
/// <para>
/// Every read method ends in a materialising call - <c>ToListAsync</c>,
/// <c>FirstOrDefaultAsync</c>, <c>CountAsync</c>. No method returns a queryable, so the
/// composed query cannot escape and be executed after the context is disposed.
/// </para>
/// <para>
/// Entity-specific repositories derive from this class and add only what generic CRUD
/// cannot express; they inherit everything else.
/// </para>
/// </remarks>
/// <param name="context">The database context.</param>
public class GenericRepository<TEntity, TKey>(ApplicationDbContext context)
    : IGenericRepository<TEntity, TKey>
    where TEntity : BaseEntity<TKey>
    where TKey : IEquatable<TKey>
{
    /// <summary>Gets the database context.</summary>
    protected ApplicationDbContext Context { get; } = context;

    /// <summary>Gets the entity set.</summary>
    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(e => e.Id.Equals(id), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default) =>
        await Set.AnyAsync(e => e.Id.Equals(id), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<TEntity?> FirstOrDefaultAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default) =>
        await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<TEntity?> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TEntity>> ListAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default) =>
        await ApplySpecification(specification).ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TResult>> selector,
        CancellationToken cancellationToken = default) =>
        await ApplySpecification(specification).Select(selector).ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<PagedResult<TEntity>> PagedListAsync(
        ISpecification<TEntity> specification,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Two queries, not one. The count must ignore skip/take or it would report the page
        // size instead of the total, which is why the evaluator takes ignorePaging.
        var total = await ApplySpecification(specification, ignorePaging: true)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await ApplySpecification(specification)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new PagedResult<TEntity>(items, pageNumber, pageSize, total);
    }

    /// <inheritdoc />
    public virtual async Task<PagedResult<TResult>> PagedListAsync<TResult>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TResult>> selector,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var total = await ApplySpecification(specification, ignorePaging: true)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        // Select is applied to the queryable, so the projection becomes the SQL SELECT list:
        // unused columns are never read off the wire.
        var items = await ApplySpecification(specification)
            .Select(selector)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new PagedResult<TResult>(items, pageNumber, pageSize, total);
    }

    /// <inheritdoc />
    public virtual async Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        await Set.AnyAsync(predicate, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<int> CountAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default) =>
        await ApplySpecification(specification, ignorePaging: true)
            .CountAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<decimal> SumAsync(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, decimal>> selector,
        CancellationToken cancellationToken = default) =>
        await ApplySpecification(specification, ignorePaging: true)
            .SumAsync(selector, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TResult>> GroupAsync<TGroupKey, TResult>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TGroupKey>> keySelector,
        Expression<Func<TGroupKey, IEnumerable<TEntity>, TResult>> resultSelector,
        CancellationToken cancellationToken = default) =>
        await ApplySpecification(specification, ignorePaging: true)
            .GroupBy(keySelector, resultSelector)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        await Set.AddAsync(entity, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        await Set.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual void Update(TEntity entity) => Set.Update(entity);

    /// <inheritdoc />
    public virtual void Remove(TEntity entity) => Set.Remove(entity);

    /// <inheritdoc />
    public virtual void RemoveRange(IEnumerable<TEntity> entities) => Set.RemoveRange(entities);

    /// <summary>
    /// Composes the entity set with a specification.
    /// </summary>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="ignorePaging">Whether to omit skip and take.</param>
    /// <returns>The composed query, which must be materialised before it leaves this assembly.</returns>
    protected IQueryable<TEntity> ApplySpecification(ISpecification<TEntity> specification, bool ignorePaging = false) =>
        SpecificationEvaluator.GetQuery(Set.AsQueryable(), specification, ignorePaging);
}
