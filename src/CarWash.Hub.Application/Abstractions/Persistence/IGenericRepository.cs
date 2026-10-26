using System.Linq.Expressions;
using CarWash.Hub.Application.Common.Models;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Domain.Common;

namespace CarWash.Hub.Application.Abstractions.Persistence;

/// <summary>
/// Data access for a single entity type. Every result is materialised.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <remarks>
/// Layer: Application.
/// <para>
/// Note what this interface does <em>not</em> expose: there is no <c>IQueryable</c> and no
/// <c>DbSet</c>. Callers describe queries with an <see cref="ISpecification{TEntity}"/> and
/// receive lists, pages or scalars. Persistence stays the only layer that knows EF Core
/// exists, which is what the architecture test in
/// <c>DependencyRuleTests.Application_has_no_dependency_on_EntityFrameworkCore</c> asserts.
/// </para>
/// <para>Every method takes a <see cref="CancellationToken"/>; there are no sync overloads.</para>
/// </remarks>
public interface IGenericRepository<TEntity>
    where TEntity : class
{
    /// <summary>Returns the single entity matching the specification, or null.</summary>
    /// <param name="specification">The query description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching entity, or <see langword="null"/>.</returns>
    Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);

    /// <summary>Returns the first entity matching a predicate, or null.</summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching entity, or <see langword="null"/>.</returns>
    Task<TEntity?> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Returns all entities matching the specification.</summary>
    /// <param name="specification">The query description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching entities.</returns>
    Task<IReadOnlyList<TEntity>> ListAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a page of entities matching the specification.
    /// </summary>
    /// <param name="specification">The query description, including its paging window.</param>
    /// <param name="pageNumber">The 1-based page number, echoed into the result.</param>
    /// <param name="pageSize">The page size, echoed into the result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of entities and the total count.</returns>
    Task<PagedResult<TEntity>> PagedListAsync(
        ISpecification<TEntity> specification,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a page of projections, selecting in the database rather than in memory.
    /// </summary>
    /// <typeparam name="TResult">The projected shape.</typeparam>
    /// <param name="specification">The query description.</param>
    /// <param name="selector">The projection applied server-side.</param>
    /// <param name="pageNumber">The 1-based page number.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of projected results.</returns>
    /// <remarks>
    /// Preferred over loading entities and mapping afterwards: the projection becomes the
    /// SELECT list, so unused columns are never read and no change-tracking entries are
    /// created for rows that are only being displayed.
    /// </remarks>
    Task<PagedResult<TResult>> PagedListAsync<TResult>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TResult>> selector,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Returns projections of all entities matching the specification.</summary>
    /// <typeparam name="TResult">The projected shape.</typeparam>
    /// <param name="specification">The query description.</param>
    /// <param name="selector">The projection applied server-side.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The projected results.</returns>
    Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TResult>> selector,
        CancellationToken cancellationToken = default);

    /// <summary>Determines whether any entity matches the predicate.</summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if a match exists.</returns>
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Counts the entities matching the specification.</summary>
    /// <param name="specification">The query description; paging is ignored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of matching rows.</returns>
    Task<int> CountAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);

    /// <summary>Sums a value over the entities matching the specification, in the database.</summary>
    /// <param name="specification">The filter; ordering and paging are ignored.</param>
    /// <param name="selector">The value summed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sum; 0 when nothing matches.</returns>
    Task<decimal> SumAsync(ISpecification<TEntity> specification, Expression<Func<TEntity, decimal>> selector, CancellationToken cancellationToken = default);

    /// <summary>Groups the entities matching the specification and aggregates each group, in the database.</summary>
    /// <typeparam name="TGroupKey">The grouping key.</typeparam>
    /// <typeparam name="TResult">The row per group.</typeparam>
    /// <param name="specification">The filter; ordering and paging are ignored.</param>
    /// <param name="keySelector">The grouping key.</param>
    /// <param name="resultSelector">The row of a group; keep to Count, Sum, Min and Max over simple members, so it translates to SQL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One row per group.</returns>
    Task<IReadOnlyList<TResult>> GroupAsync<TGroupKey, TResult>(
        ISpecification<TEntity> specification,
        Expression<Func<TEntity, TGroupKey>> keySelector,
        Expression<Func<TGroupKey, IEnumerable<TEntity>, TResult>> resultSelector,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new entity for insertion.</summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the entity is tracked.</returns>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Stages several new entities for insertion.</summary>
    /// <param name="entities">The entities to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the entities are tracked.</returns>
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>Stages an entity as modified.</summary>
    /// <param name="entity">The entity to update.</param>
    void Update(TEntity entity);

    /// <summary>
    /// Stages an entity for removal.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    /// <remarks>
    /// For an <see cref="ISoftDeletable"/> entity the interceptor converts this into a soft
    /// delete, so the row is flagged rather than removed. For anything else it is a real
    /// DELETE. The call site is the same either way, which keeps callers from having to
    /// know which kind of entity they hold.
    /// </remarks>
    void Remove(TEntity entity);

    /// <summary>Stages several entities for removal.</summary>
    /// <param name="entities">The entities to remove.</param>
    void RemoveRange(IEnumerable<TEntity> entities);
}

/// <summary>
/// Data access for an entity with a known key type.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <remarks>
/// Layer: Application. Split from the non-generic-key interface so that cross-cutting code
/// which does not care about key types can accept <see cref="IGenericRepository{TEntity}"/>,
/// while callers that look entities up by id keep full type safety.
/// </remarks>
public interface IGenericRepository<TEntity, in TKey> : IGenericRepository<TEntity>
    where TEntity : BaseEntity<TKey>
    where TKey : IEquatable<TKey>
{
    /// <summary>Returns the entity with the given key, or null.</summary>
    /// <param name="id">The primary key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entity, or <see langword="null"/> if not found or soft-deleted.</returns>
    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);

    /// <summary>Determines whether an entity with the given key exists.</summary>
    /// <param name="id">The primary key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if it exists and is not soft-deleted.</returns>
    Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default);
}
