using CarWash.Hub.Application.Common.Specifications;
using Microsoft.EntityFrameworkCore;

namespace CarWash.Hub.Persistence.Specifications;

/// <summary>
/// Turns an <see cref="ISpecification{TEntity}"/> into an EF Core query.
/// </summary>
/// <remarks>
/// Layer: Persistence.
/// <para>
/// This is the only place a specification meets <c>IQueryable</c>, and the resulting
/// queryable never leaves this assembly - repositories materialise it before returning.
/// </para>
/// </remarks>
internal static class SpecificationEvaluator
{
    /// <summary>
    /// Applies a specification's filtering, includes, ordering and paging to a query.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="inputQuery">The query root.</param>
    /// <param name="specification">The specification to apply.</param>
    /// <param name="ignorePaging">
    /// When <see langword="true"/>, skip and take are not applied. Used by the count half of
    /// a paged query, which must count every matching row rather than the page.
    /// </param>
    /// <returns>The composed query.</returns>
    public static IQueryable<TEntity> GetQuery<TEntity>(
        IQueryable<TEntity> inputQuery,
        ISpecification<TEntity> specification,
        bool ignorePaging = false)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(inputQuery);
        ArgumentNullException.ThrowIfNull(specification);

        var query = inputQuery;

        // Must come first: a filter applied after IgnoreQueryFilters would still be subject
        // to the global soft-delete filter for the earlier operators.
        if (specification.IgnoreSoftDeleteFilter)
        {
            query = query.IgnoreQueryFilters();
        }

        if (specification.AsNoTracking)
        {
            query = query.AsNoTracking();
        }

        if (specification.Criteria is not null)
        {
            query = query.Where(specification.Criteria);
        }

        query = specification.Includes.Aggregate(query, (current, include) => current.Include(include));
        query = specification.IncludeStrings.Aggregate(query, (current, include) => current.Include(include));

        if (specification.OrderBy.Count > 0)
        {
            var first = specification.OrderBy[0];
            var ordered = first.Descending
                ? query.OrderByDescending(first.KeySelector)
                : query.OrderBy(first.KeySelector);

            for (var i = 1; i < specification.OrderBy.Count; i++)
            {
                var next = specification.OrderBy[i];
                ordered = next.Descending
                    ? ordered.ThenByDescending(next.KeySelector)
                    : ordered.ThenBy(next.KeySelector);
            }

            query = ordered;
        }

        // Split queries only pay off when several collection navigations are included -
        // otherwise a single join is cheaper than several round-trips. Ordering is applied
        // before this, which matters: EF Core requires a deterministic order to stitch a
        // split query's results back together correctly.
        if (specification.AsSplitQuery)
        {
            query = query.AsSplitQuery();
        }

        if (!ignorePaging && specification.IsPagingEnabled)
        {
            if (specification.Skip is { } skip)
            {
                query = query.Skip(skip);
            }

            if (specification.Take is { } take)
            {
                query = query.Take(take);
            }
        }

        return query;
    }
}
