namespace CarWash.Hub.Application.Common.Models;

/// <summary>
/// A materialised page of results plus the metadata a client needs to page through them.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items on this page.</param>
/// <param name="PageNumber">The 1-based page number.</param>
/// <param name="PageSize">The requested page size.</param>
/// <param name="TotalCount">Total matching rows across all pages.</param>
/// <remarks>
/// Layer: Application.
/// <para>
/// <c>Items</c> is an <see cref="IReadOnlyList{T}"/> of already-materialised results, never
/// an <c>IQueryable</c>. That is deliberate: it is what stops a lazily-composed query from
/// escaping Persistence and being executed - or failing - somewhere in a controller.
/// </para>
/// </remarks>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount)
{
    /// <summary>Gets the total number of pages.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>Gets a value indicating whether a previous page exists.</summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>Gets a value indicating whether a further page exists.</summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>Gets an empty page, used when a query is short-circuited.</summary>
    /// <param name="pageNumber">The 1-based page number.</param>
    /// <param name="pageSize">The requested page size.</param>
    /// <returns>An empty <see cref="PagedResult{T}"/>.</returns>
    public static PagedResult<T> Empty(int pageNumber, int pageSize) =>
        new([], pageNumber, pageSize, 0);
}
