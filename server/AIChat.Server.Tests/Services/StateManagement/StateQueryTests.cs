using AIChat.Server.Services.StateManagement;
using Xunit;

namespace AIChat.Server.Tests.Services.StateManagement;

/// <summary>
/// Unit tests for StateQuery and PagedResult classes.
/// Tests query building, validation, and paging functionality.
/// </summary>
public class StateQueryTests
{
    #region StateQuery Tests

    [Fact]
    public void StateQuery_Default_ShouldHaveCorrectDefaults()
    {
        // Act
        var query = StateQuery.Default();

        // Assert
        Assert.Equal(1, query.Page);
        Assert.Equal(50, query.PageSize);
        Assert.Null(query.Filters);
        Assert.Null(query.SortBy);
        Assert.Equal(SortDirection.Ascending, query.SortDirection);
        Assert.Equal(0, query.Offset);
    }

    [Fact]
    public void StateQuery_Create_ShouldSetPageAndPageSize()
    {
        // Arrange
        const int page = 3;
        const int pageSize = 25;

        // Act
        var query = StateQuery.Create(page, pageSize);

        // Assert
        Assert.Equal(page, query.Page);
        Assert.Equal(pageSize, query.PageSize);
        Assert.Equal((page - 1) * pageSize, query.Offset);
    }

    [Fact]
    public void StateQuery_WithFilters_ShouldSetFiltersAndPaging()
    {
        // Arrange
        var filters = new Dictionary<string, object>
        {
            ["userId"] = "user123",
            ["status"] = "active"
        };

        // Act
        var query = StateQuery.WithFilters(filters, 2, 20);

        // Assert
        Assert.Equal(filters, query.Filters);
        Assert.Equal(2, query.Page);
        Assert.Equal(20, query.PageSize);
        Assert.Equal(20, query.Offset);
    }

    [Fact]
    public void StateQuery_WithSort_ShouldSetSortingAndPaging()
    {
        // Arrange
        const string sortBy = "CreatedAt";
        const SortDirection sortDirection = SortDirection.Descending;

        // Act
        var query = StateQuery.WithSort(sortBy, sortDirection, 1, 10);

        // Assert
        Assert.Equal(sortBy, query.SortBy);
        Assert.Equal(sortDirection, query.SortDirection);
        Assert.Equal(1, query.Page);
        Assert.Equal(10, query.PageSize);
    }

    [Fact]
    public void StateQuery_AddFilter_ShouldAddToExistingFilters()
    {
        // Arrange
        var existingFilters = new Dictionary<string, object> { ["existing"] = "value" };
        var query = new StateQuery { Filters = existingFilters };

        // Act
        var newQuery = query.AddFilter("new", "newValue");

        // Assert
        Assert.Equal(2, newQuery.Filters!.Count);
        Assert.Equal("value", newQuery.Filters["existing"]);
        Assert.Equal("newValue", newQuery.Filters["new"]);

        // Original query should be unchanged (immutable)
        _ = Assert.Single(query.Filters!);
    }

    [Fact]
    public void StateQuery_AddFilter_ShouldCreateFiltersIfNull()
    {
        // Arrange
        var query = new StateQuery();

        // Act
        var newQuery = query.AddFilter("key", "value");

        // Assert
        Assert.NotNull(newQuery.Filters);
        _ = Assert.Single(newQuery.Filters);
        Assert.Equal("value", newQuery.Filters["key"]);
    }

    [Fact]
    public void StateQuery_WithSort_ShouldUpdateSorting()
    {
        // Arrange
        var query = StateQuery.Create();

        // Act
        var newQuery = query.WithSort("Name", SortDirection.Descending);

        // Assert
        Assert.Equal("Name", newQuery.SortBy);
        Assert.Equal(SortDirection.Descending, newQuery.SortDirection);

        // Other properties should remain unchanged
        Assert.Equal(query.Page, newQuery.Page);
        Assert.Equal(query.PageSize, newQuery.PageSize);
    }

    [Fact]
    public void StateQuery_WithPaging_ShouldUpdatePaging()
    {
        // Arrange
        var query = StateQuery.Create();

        // Act
        var newQuery = query.WithPaging(5, 15);

        // Assert
        Assert.Equal(5, newQuery.Page);
        Assert.Equal(15, newQuery.PageSize);
        Assert.Equal(60, newQuery.Offset); // (5-1) * 15
    }

    [Fact]
    public void StateQuery_Offset_ShouldCalculateCorrectly()
    {
        // Arrange & Act & Assert
        var query1 = StateQuery.Create(1, 10);
        Assert.Equal(0, query1.Offset);

        var query2 = StateQuery.Create(2, 10);
        Assert.Equal(10, query2.Offset);

        var query3 = StateQuery.Create(3, 25);
        Assert.Equal(50, query3.Offset);
    }

    #endregion StateQuery Tests

    #region PagedResult Tests

    [Fact]
    public void PagedResult_FromQuery_ShouldCreateCorrectResult()
    {
        // Arrange
        var items = new List<string> { "item1", "item2", "item3" };
        const int totalCount = 100;
        var query = StateQuery.Create(2, 10);

        // Act
        var result = PagedResult<string>.FromQuery(items, totalCount, query);

        // Assert
        Assert.Equal(items, result.Items);
        Assert.Equal(totalCount, result.TotalCount);
        Assert.Equal(query.Page, result.Page);
        Assert.Equal(query.PageSize, result.PageSize);
    }

    [Fact]
    public void PagedResult_Empty_ShouldCreateEmptyResult()
    {
        // Arrange
        var query = StateQuery.Create(1, 10);

        // Act
        var result = PagedResult<string>.Empty(query);

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(query.Page, result.Page);
        Assert.Equal(query.PageSize, result.PageSize);
    }

    [Fact]
    public void PagedResult_HasNextPage_ShouldReturnCorrectValue()
    {
        // Arrange
        var query = StateQuery.Create(2, 10);

        // Test case 1: Has next page
        var result1 = PagedResult<string>.FromQuery([], 25, query);
        Assert.True(result1.HasNextPage);

        // Test case 2: No next page
        var result2 = PagedResult<string>.FromQuery([], 20, query);
        Assert.False(result2.HasNextPage);

        // Test case 3: Exactly on boundary
        var result3 = PagedResult<string>.FromQuery([], 20, query);
        Assert.False(result3.HasNextPage);
    }

    [Fact]
    public void PagedResult_HasPreviousPage_ShouldReturnCorrectValue()
    {
        // Test case 1: First page - no previous
        var query1 = StateQuery.Create(1, 10);
        var result1 = PagedResult<string>.FromQuery([], 25, query1);
        Assert.False(result1.HasPreviousPage);

        // Test case 2: Second page - has previous
        var query2 = StateQuery.Create(2, 10);
        var result2 = PagedResult<string>.FromQuery([], 25, query2);
        Assert.True(result2.HasPreviousPage);
    }

    [Fact]
    public void PagedResult_TotalPages_ShouldCalculateCorrectly()
    {
        // Test case 1: Exact division
        var query1 = StateQuery.Create(1, 10);
        var result1 = PagedResult<string>.FromQuery([], 30, query1);
        Assert.Equal(3, result1.TotalPages);

        // Test case 2: With remainder
        var query2 = StateQuery.Create(1, 10);
        var result2 = PagedResult<string>.FromQuery([], 25, query2);
        Assert.Equal(3, result2.TotalPages);

        // Test case 3: Empty result
        var query3 = StateQuery.Create(1, 10);
        var result3 = PagedResult<string>.FromQuery([], 0, query3);
        Assert.Equal(0, result3.TotalPages);
    }

    [Fact]
    public void PagedResult_ItemIndexes_ShouldCalculateCorrectly()
    {
        // Test case 1: First page
        var query1 = StateQuery.Create(1, 10);
        var result1 = PagedResult<string>.FromQuery([], 25, query1);
        Assert.Equal(1, result1.FirstItemIndex);
        Assert.Equal(10, result1.LastItemIndex);

        // Test case 2: Second page
        var query2 = StateQuery.Create(2, 10);
        var result2 = PagedResult<string>.FromQuery([], 25, query2);
        Assert.Equal(11, result2.FirstItemIndex);
        Assert.Equal(20, result2.LastItemIndex);

        // Test case 3: Last page with fewer items
        var query3 = StateQuery.Create(3, 10);
        var result3 = PagedResult<string>.FromQuery([], 25, query3);
        Assert.Equal(21, result3.FirstItemIndex);
        Assert.Equal(25, result3.LastItemIndex);

        // Test case 4: Empty result
        var query4 = StateQuery.Create(1, 10);
        var result4 = PagedResult<string>.FromQuery([], 0, query4);
        Assert.Equal(0, result4.FirstItemIndex);
        Assert.Equal(0, result4.LastItemIndex);
    }

    #endregion PagedResult Tests

    #region SortDirection Tests

    [Theory]
    [InlineData(SortDirection.Ascending)]
    [InlineData(SortDirection.Descending)]
    public void SortDirection_ShouldHaveDefinedValues(SortDirection direction)
    {
        Assert.True(Enum.IsDefined(direction));
    }

    #endregion SortDirection Tests
}
