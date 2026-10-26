using System.Linq.Expressions;

namespace CarWash.Hub.Application.Common.Specifications;

/// <summary>
/// A declarative description of a query: what to filter, include, sort and page.
/// </summary>
/// <typeparam name="TEntity">The entity being queried.</typeparam>
/// <remarks>
/// Layer: Application.
/// <para>
/// This exists to satisfy the rule that no <c>IQueryable</c> leaves the Persistence layer.
/// The Application layer says <em>what</em> it wants as data; <c>SpecificationEvaluator</c>
/// inside Persistence turns that into an EF Core query and executes it. Consequences worth
/// knowing:
/// </para>
/// <list type="bullet">
///   <item><description>No caller can accidentally trigger a query after the DbContext is
///   disposed, because there is nothing lazy to hand out.</description></item>
///   <item><description>Specifications are plain objects, so they unit-test without a
///   database.</description></item>
///   <item><description>The cost is expressiveness: anything the evaluator does not
///   support has to be added to it, rather than written inline at the call site. That
///   trade is the point - it keeps query capability in one reviewable place.</description></item>
/// </list>
/// </remarks>
public interface ISpecification<TEntity>
    where TEntity : class
{
    /// <summary>Gets the filter predicate, or <see langword="null"/> for no filtering.</summary>
    Expression<Func<TEntity, bool>>? Criteria { get; }

    /// <summary>Gets the navigation properties to eager-load.</summary>
    IReadOnlyList<Expression<Func<TEntity, object?>>> Includes { get; }

    /// <summary>Gets nested navigation paths to eager-load, for chains Includes cannot express.</summary>
    IReadOnlyList<string> IncludeStrings { get; }

    /// <summary>Gets the ordering clauses, applied in sequence.</summary>
    IReadOnlyList<OrderExpression<TEntity>> OrderBy { get; }

    /// <summary>Gets the number of rows to skip, when paging is enabled.</summary>
    int? Skip { get; }

    /// <summary>Gets the number of rows to take, when paging is enabled.</summary>
    int? Take { get; }

    /// <summary>Gets a value indicating whether paging has been configured.</summary>
    bool IsPagingEnabled { get; }

    /// <summary>
    /// Gets a value indicating whether the query runs without change tracking.
    /// Defaults to <see langword="true"/>, since reads vastly outnumber writes.
    /// </summary>
    bool AsNoTracking { get; }

    /// <summary>
    /// Gets a value indicating whether to split a query with multiple collection includes
    /// into several round-trips, avoiding the row explosion of a single large join.
    /// </summary>
    bool AsSplitQuery { get; }

    /// <summary>
    /// Gets a value indicating whether the global soft-delete filter is bypassed.
    /// </summary>
    /// <remarks>
    /// This is the documented, deliberate way to read soft-deleted rows - an audit or
    /// restore screen, for instance. It is a property on the specification rather than a
    /// repository overload so that the intent is recorded in the object being passed
    /// around, and shows up in code review rather than hiding in a boolean argument.
    /// See README, "Bypassing the soft-delete filter".
    /// </remarks>
    bool IgnoreSoftDeleteFilter { get; }
}

/// <summary>
/// A single ordering clause.
/// </summary>
/// <typeparam name="TEntity">The entity being ordered.</typeparam>
/// <param name="KeySelector">The property to order by.</param>
/// <param name="Descending">Whether to order descending.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record OrderExpression<TEntity>(
    Expression<Func<TEntity, object?>> KeySelector,
    bool Descending);
