using AIChat.Server.Services.StateManagement.Caching;
using Xunit;

namespace AIChat.Server.Tests.Services.StateManagement;

/// <summary>
/// Unit tests for NullStateCacheManager.
/// Tests the null object pattern implementation that provides no-op caching.
/// </summary>
public class NullStateCacheManagerTests
{
    private readonly NullStateCacheManager<TestEntity> _cacheManager;
    private static readonly string[] keys = ["key1", "key2"];

    public NullStateCacheManagerTests()
    {
        _cacheManager = new NullStateCacheManager<TestEntity>();
    }

    [Fact]
    public async Task GetFromCacheAsync_ShouldAlwaysReturnNull()
    {
        // Act
        var result = await _cacheManager.GetFromCacheAsync("any-key");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SetCacheAsync_ShouldCompleteSuccessfully()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test" };

        // Act & Assert - Should not throw
        await _cacheManager.SetCacheAsync("key", entity);
        await _cacheManager.SetCacheAsync("key", entity, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task RemoveFromCacheAsync_ShouldCompleteSuccessfully()
    {
        // Act & Assert - Should not throw
        await _cacheManager.RemoveFromCacheAsync("key");
    }

    [Fact]
    public async Task RemoveFromCacheAsync_WithMultipleKeys_ShouldCompleteSuccessfully()
    {
        // Arrange
        var keys = new[] { "key1", "key2", "key3" };

        // Act & Assert - Should not throw
        await _cacheManager.RemoveFromCacheAsync(keys);
    }

    [Fact]
    public async Task ExistsInCacheAsync_ShouldAlwaysReturnFalse()
    {
        // Act
        var exists = await _cacheManager.ExistsInCacheAsync("any-key");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task InvalidateAllAsync_ShouldCompleteSuccessfully()
    {
        // Act & Assert - Should not throw
        await _cacheManager.InvalidateAllAsync();
    }

    [Fact]
    public async Task InvalidateByPatternAsync_ShouldCompleteSuccessfully()
    {
        // Act & Assert - Should not throw
        await _cacheManager.InvalidateByPatternAsync(".*");
    }

    [Fact]
    public async Task GetCacheStatisticsAsync_ShouldReturnEmptyStatistics()
    {
        // Act
        var statistics = await _cacheManager.GetCacheStatisticsAsync();

        // Assert
        Assert.NotNull(statistics);
        Assert.Equal(0, statistics.HitCount);
        Assert.Equal(0, statistics.MissCount);
        Assert.Equal(0, statistics.TotalRequests);
        Assert.Equal(0, statistics.HitRatio);
        Assert.Equal(0, statistics.EntryCount);
        Assert.Equal(0, statistics.EstimatedMemoryUsage);

        Assert.NotNull(statistics.AdditionalMetrics);
        Assert.Equal("Null", statistics.AdditionalMetrics["CacheType"]);
        Assert.Equal("TestEntity", statistics.AdditionalMetrics["EntityType"]);
        Assert.Contains("No-operation cache implementation", statistics.AdditionalMetrics["Description"].ToString());
    }

    [Fact]
    public async Task AllOperations_WithCancellationToken_ShouldCompleteSuccessfully()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var entity = new TestEntity { Id = "1", Name = "Test" };

        // Act & Assert - All operations should complete successfully with cancellation token
        var result = await _cacheManager.GetFromCacheAsync("key", cts.Token);
        Assert.Null(result);

        await _cacheManager.SetCacheAsync("key", entity, TimeSpan.FromMinutes(5), cts.Token);
        await _cacheManager.RemoveFromCacheAsync("key", cts.Token);
        await _cacheManager.RemoveFromCacheAsync(keys, cts.Token);

        var exists = await _cacheManager.ExistsInCacheAsync("key", cts.Token);
        Assert.False(exists);

        await _cacheManager.InvalidateAllAsync(cts.Token);
        await _cacheManager.InvalidateByPatternAsync(".*", cts.Token);

        var stats = await _cacheManager.GetCacheStatisticsAsync(cts.Token);
        Assert.NotNull(stats);
    }

    [Fact]
    public async Task Operations_AfterMultipleCalls_ShouldRemainConsistent()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test" };

        // Act - Perform multiple operations
        await _cacheManager.SetCacheAsync("key1", entity);
        await _cacheManager.SetCacheAsync("key2", entity);

        var result1 = await _cacheManager.GetFromCacheAsync("key1");
        var result2 = await _cacheManager.GetFromCacheAsync("key2");

        var exists1 = await _cacheManager.ExistsInCacheAsync("key1");
        var exists2 = await _cacheManager.ExistsInCacheAsync("key2");

        await _cacheManager.RemoveFromCacheAsync("key1");

        var resultAfterRemove = await _cacheManager.GetFromCacheAsync("key1");
        var existsAfterRemove = await _cacheManager.ExistsInCacheAsync("key1");

        // Assert - All operations should maintain null object behavior
        Assert.Null(result1);
        Assert.Null(result2);
        Assert.False(exists1);
        Assert.False(exists2);
        Assert.Null(resultAfterRemove);
        Assert.False(existsAfterRemove);
    }

    /// <summary>
    /// Test entity for cache operations.
    /// </summary>
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
