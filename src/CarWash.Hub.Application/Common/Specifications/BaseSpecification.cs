using System.Linq.Expressions;
using CarWash.Hub.Application.Common.Models;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Common.Specifications;

/// <summary>
/// Convenience base class for building an <see cref="ISpecification{TEntity}"/>.
/// </summary>
/// <typeparam name="TEntity">The entity being queried.</typeparam>
/// <remarks>
/// Layer: Application. Derive from this and call the protected builders from the
/// constructor; the resulting object is immutable from the caller's point of view.
/// </remarks>
public abstract class BaseSpecification<TEntity> : ISpecification<TEntity>
    where TEntity : class
{
    private readonly List<Expression<Func<TEntity, object?>>> _includes = [];
    private readonly List<string> _includeStrings = [];
    private readonly List<OrderExpression<TEntity>> _orderBy = [];

    /// <summary>Initialises a specification with no filter.</summary>
    protected BaseSpecification()
    {
    }

    /// <summary>Initialises a specification with a filter predicate.</summary>
    /// <param name="criteria">The filter predicate.</param>
    protected BaseSpecification(Expression<Func<TEntity, bool>> criteria) => Criteria = criteria;

    /// <inheritdoc />
    public Expression<Func<TEntity, bool>>? Criteria { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<Expression<Func<TEntity, object?>>> Includes => _includes;

    /// <inheritdoc />
    public IReadOnlyList<string> IncludeStrings => _includeStrings;

    /// <inheritdoc />
    public IReadOnlyList<OrderExpression<TEntity>> OrderBy => _orderBy;

    /// <inheritdoc />
    public int? Skip { get; private set; }

    /// <inheritdoc />
    public int? Take { get; private set; }

    /// <inheritdoc />
    public bool IsPagingEnabled { get; private set; }

    /// <inheritdoc />
    public bool AsNoTracking { get; private set; } = true;

    /// <inheritdoc />
    public bool AsSplitQuery { get; private set; }

    /// <inheritdoc />
    public bool IgnoreSoftDeleteFilter { get; private set; }

    /// <summary>Adds a filter predicate, combined with any existing one using AND.</summary>
    /// <param name="criteria">The predicate to add.</param>
    protected void AddCriteria(Expression<Func<TEntity, bool>> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        Criteria = Criteria is null ? criteria : Criteria.AndAlso(criteria);
    }

    /// <summary>Eager-loads a navigation property.</summary>
    /// <param name="include">The navigation to include.</param>
    protected void AddInclude(Expression<Func<TEntity, object?>> include) => _includes.Add(include);

    /// <summary>Eager-loads a nested navigation path, for example <c>"UserRoles.Role"</c>.</summary>
    /// <param name="includeString">The dotted navigation path.</param>
    protected void AddInclude(string includeString) => _includeStrings.Add(includeString);

    /// <summary>Appends an ordering clause.</summary>
    /// <param name="keySelector">The property to order by.</param>
    /// <param name="descending">Whether to order descending.</param>
    protected void AddOrderBy(Expression<Func<TEntity, object?>> keySelector, bool descending = false) =>
        _orderBy.Add(new OrderExpression<TEntity>(keySelector, descending));

    /// <summary>Appends an ordering clause using a <see cref="SortDirection"/>.</summary>
    /// <param name="keySelector">The property to order by.</param>
    /// <param name="direction">The sort direction.</param>
    protected void AddOrderBy(Expression<Func<TEntity, object?>> keySelector, SortDirection direction) =>
        AddOrderBy(keySelector, direction == SortDirection.Descending);

    /// <summary>Enables paging using an explicit skip and take.</summary>
    /// <param name="skip">Rows to skip.</param>
    /// <param name="take">Rows to take.</param>
    protected void ApplyPaging(int skip, int take)
    {
        Skip = skip;
        Take = take;
        IsPagingEnabled = true;
    }

    /// <summary>Enables paging from a <see cref="PageRequest"/>.</summary>
    /// <param name="request">The paging request, already clamped.</param>
    protected void ApplyPaging(PageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ApplyPaging(request.Skip, request.PageSize);
    }

    /// <summary>Enables change tracking for this query, needed when the results will be modified.</summary>
    protected void EnableTracking() => AsNoTracking = false;

    /// <summary>Splits a query with several collection includes into multiple round-trips.</summary>
    protected void EnableSplitQuery() => AsSplitQuery = true;

    /// <summary>
    /// Includes soft-deleted rows in the results.
    /// </summary>
    /// <remarks>
    /// The deliberate soft-delete bypass. Call it only where showing deleted rows is the
    /// actual requirement - restore screens, audit views, uniqueness checks that must
    /// consider deleted rows.
    /// </remarks>
    protected void IncludeSoftDeleted() => IgnoreSoftDeleteFilter = true;
}
