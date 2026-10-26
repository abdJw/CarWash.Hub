using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Common.Models;

/// <summary>
/// The page window shared by every paged endpoint.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// Split from <see cref="PagedRequest"/> so an endpoint with a fixed order - a machine's history,
/// always newest first - does not advertise search and sort parameters it would ignore.
/// </para>
/// </remarks>
public record PageRequest
{
    /// <summary>The largest page a client may request.</summary>
    /// <remarks>
    /// Clamped rather than rejected. An unbounded page size is a denial-of-service vector, and
    /// returning 100 rows is friendlier to callers than a 400.
    /// </remarks>
    public const int MaxPageSize = 100;

    /// <summary>The page size used when the caller does not specify one.</summary>
    public const int DefaultPageSize = 20;

    private readonly int _pageNumber = 1;
    private readonly int _pageSize = DefaultPageSize;

    /// <summary>Gets the 1-based page number. Values below 1 are treated as 1.</summary>
    public int PageNumber
    {
        get => _pageNumber;
        init => _pageNumber = value < 1 ? 1 : value;
    }

    /// <summary>Gets the page size, clamped to <see cref="MaxPageSize"/>.</summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value,
        };
    }

    /// <summary>Gets the number of rows to skip for this page.</summary>
    public int Skip => (PageNumber - 1) * PageSize;
}

/// <summary>
/// Pagination, sorting and search parameters shared by list endpoints.
/// </summary>
/// <remarks>Layer: Application.</remarks>
public record PagedRequest : PageRequest
{
    /// <summary>Gets the free-text search term, matched against entity-specific fields.</summary>
    public string? SearchTerm { get; init; }

    /// <summary>
    /// Gets the property name to sort by.
    /// </summary>
    /// <remarks>
    /// Resolved against an explicit allow-list per entity, never passed through to a dynamic
    /// query. An unrecognised value falls back to the entity's default sort rather than erroring.
    /// </remarks>
    public string? SortBy { get; init; }

    /// <summary>Gets the sort direction. Defaults to ascending.</summary>
    public SortDirection SortDirection { get; init; } = SortDirection.Ascending;
}
