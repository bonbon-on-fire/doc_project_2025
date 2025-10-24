using System.ComponentModel.DataAnnotations;

namespace AIChat.Server.Services.StateManagement;

/// <summary>
/// Represents a query for state management operations with filtering, paging, and sorting.
/// </summary>
public record StateQuery
{
    /// <summary>
    /// Gets the page number (1-based).
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Page must be greater than 0")]
    public int Page { get; init; } = 1;

    /// <summary>
    /// Gets the page size.
    /// </summary>
    [Range(1, 1000, ErrorMessage = "PageSize must be between 1 and 1000")]
    public int PageSize { get; init; } = 50;

    /// <summary>
    /// Gets the filters to apply to the query.
    /// </summary>
    public Dictionary<string, object>? Filters { get; init; }

    /// <summary>
    /// Gets the property name to sort by.
    /// </summary>
    public string? SortBy { get; init; }

    /// <summary>
    /// Gets the sort direction.
    /// </summary>
    public SortDirection SortDirection { get; init; } = SortDirection.Ascending;

    /// <summary>
    /// Gets additional query options.
    /// </summary>
    public Dictionary<string, object>? Options { get; init; }

    /// <summary>
    /// Gets the zero-based offset for the query.
    /// </summary>
    public int Offset => (Page - 1) * PageSize;

    /// <summary>
    /// Creates a new query with default values.
    /// </summary>
    /// <returns>A new state query with default settings</returns>
    public static StateQuery Default() => new();

    /// <summary>
    /// Creates a new query with specified page and page size.
    /// </summary>
    /// <param name="page">The page number (1-based)</param>
    /// <param name="pageSize">The page size</param>
    /// <returns>A new state query</returns>
    public static StateQuery Create(int page = 1, int pageSize = 50)
    {
        return new StateQuery
        {
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Creates a new query with specified filters.
    /// </summary>
    /// <param name="filters">The filters to apply</param>
    /// <param name="page">The page number (1-based)</param>
    /// <param name="pageSize">The page size</param>
    /// <returns>A new state query with filters</returns>
    public static StateQuery WithFilters(Dictionary<string, object> filters, int page = 1, int pageSize = 50)
    {
        return new StateQuery
        {
            Filters = filters,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Creates a new query with specified sorting.
    /// </summary>
    /// <param name="sortBy">The property to sort by</param>
    /// <param name="sortDirection">The sort direction</param>
    /// <param name="page">The page number (1-based)</param>
    /// <param name="pageSize">The page size</param>
    /// <returns>A new state query with sorting</returns>
    public static StateQuery WithSort(string sortBy, SortDirection sortDirection = SortDirection.Ascending, int page = 1, int pageSize = 50)
    {
        return new StateQuery
        {
            SortBy = sortBy,
            SortDirection = sortDirection,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Adds a filter to the query.
    /// </summary>
    /// <param name="key">The filter key</param>
    /// <param name="value">The filter value</param>
    /// <returns>A new query with the additional filter</returns>
    public StateQuery AddFilter(string key, object value)
    {
        var filters = new Dictionary<string, object>(Filters ?? [])
        {
            [key] = value
        };

        return this with { Filters = filters };
    }

    /// <summary>
    /// Sets the sort parameters for the query.
    /// </summary>
    /// <param name="sortBy">The property to sort by</param>
    /// <param name="sortDirection">The sort direction</param>
    /// <returns>A new query with updated sorting</returns>
    public StateQuery WithSort(string sortBy, SortDirection sortDirection = SortDirection.Ascending)
    {
        return this with
        {
            SortBy = sortBy,
            SortDirection = sortDirection
        };
    }

    /// <summary>
    /// Sets the paging parameters for the query.
    /// </summary>
    /// <param name="page">The page number (1-based)</param>
    /// <param name="pageSize">The page size</param>
    /// <returns>A new query with updated paging</returns>
    public StateQuery WithPaging(int page, int pageSize)
    {
        return this with
        {
            Page = page,
            PageSize = pageSize
        };
    }
}

/// <summary>
/// Represents the result of a paged query.
/// </summary>
/// <typeparam name="T">The type of items in the page</typeparam>
public record PagedResult<T>
{
    /// <summary>
    /// Gets the items in the current page.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>
    /// Gets the total count of items across all pages.
    /// </summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// Gets the current page number (1-based).
    /// </summary>
    public required int Page { get; init; }

    /// <summary>
    /// Gets the page size.
    /// </summary>
    public required int PageSize { get; init; }

    /// <summary>
    /// Gets whether there is a next page available.
    /// </summary>
    public bool HasNextPage => Page * PageSize < TotalCount;

    /// <summary>
    /// Gets whether there is a previous page available.
    /// </summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>
    /// Gets the total number of pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>
    /// Gets the 1-based index of the first item in the current page.
    /// </summary>
    public int FirstItemIndex => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    /// <summary>
    /// Gets the 1-based index of the last item in the current page.
    /// </summary>
    public int LastItemIndex => Math.Min(Page * PageSize, TotalCount);

    /// <summary>
    /// Creates a paged result from a list of items and query information.
    /// </summary>
    /// <param name="items">The items in the current page</param>
    /// <param name="totalCount">The total count of items</param>
    /// <param name="query">The query that produced this result</param>
    /// <returns>A new paged result</returns>
    public static PagedResult<T> FromQuery(IReadOnlyList<T> items, int totalCount, StateQuery query)
    {
        return new PagedResult<T>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    /// <summary>
    /// Creates an empty paged result.
    /// </summary>
    /// <param name="query">The query information</param>
    /// <returns>An empty paged result</returns>
    public static PagedResult<T> Empty(StateQuery query)
    {
        return new PagedResult<T>
        {
            Items = [],
            TotalCount = 0,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }
}

/// <summary>
/// Represents the sort direction for queries.
/// </summary>
public enum SortDirection
{
    /// <summary>
    /// Sort in ascending order.
    /// </summary>
    Ascending = 0,

    /// <summary>
    /// Sort in descending order.
    /// </summary>
    Descending = 1
}