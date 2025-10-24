using AIChat.Server.Configuration;
using AIChat.Server.Services.StateManagement.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services.StateManagement;

/// <summary>
/// Unit tests for MemoryStateCacheManager.
/// Tests in-memory caching functionality with metrics and pattern-based invalidation.
/// </summary>
public class MemoryStateCacheManagerTests : IDisposable
{
    private readonly MemoryCache _memoryCache;
    private readonly Mock<ILogger<MemoryStateCacheManager<TestEntity>>> _mockLogger;
    private readonly Mock<IOptions<MemoryStateCacheConfiguration>> _mockConfiguration;
    private readonly MemoryStateCacheManager<TestEntity> _cacheManager;

    public MemoryStateCacheManagerTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _mockLogger = new Mock<ILogger<MemoryStateCacheManager<TestEntity>>>();

        // Setup default configuration for tests
        var config = new MemoryStateCacheConfiguration
        {
            EnableDetailedLogging = true,
            EnableMetricsCollection = true,
            DefaultSlidingExpirationMinutes = 30,
            MemoryEstimationSampleSize = 10,
            FallbackMemoryEstimationBytes = 512L,
            ObjectOverheadFactor = 1.4,
            BaseObjectOverheadBytes = 64L,
            MaxDegreeOfParallelism = 0,
            EnableKeyTrackerCleanup = true
        };

        _mockConfiguration = new Mock<IOptions<MemoryStateCacheConfiguration>>();
        _mockConfiguration.Setup(x => x.Value).Returns(config);

        _cacheManager = new MemoryStateCacheManager<TestEntity>(_memoryCache, _mockLogger.Object, _mockConfiguration.Object);
    }

    [Fact]
    public async Task GetFromCacheAsync_WhenItemExists_ShouldReturnItem()
    {
        // Arrange
        const string key = "test-key";
        var entity = new TestEntity { Id = "1", Name = "Test" };
        await _cacheManager.SetCacheAsync(key, entity);

        // Act
        var result = await _cacheManager.GetFromCacheAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(entity.Id, result.Id);
        Assert.Equal(entity.Name, result.Name);
    }

    [Fact]
    public async Task GetFromCacheAsync_WhenItemDoesNotExist_ShouldReturnNull()
    {
        // Arrange
        const string key = "non-existent-key";

        // Act
        var result = await _cacheManager.GetFromCacheAsync(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetFromCacheAsync_WithNullOrEmptyKey_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => _cacheManager.GetFromCacheAsync(null!));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => _cacheManager.GetFromCacheAsync(string.Empty));
    }

    [Fact]
    public async Task SetCacheAsync_ShouldStoreItem()
    {
        // Arrange
        const string key = "test-key";
        var entity = new TestEntity { Id = "1", Name = "Test" };

        // Act
        await _cacheManager.SetCacheAsync(key, entity);
        var result = await _cacheManager.GetFromCacheAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(entity.Id, result.Id);
    }

    [Fact]
    public async Task SetCacheAsync_WithExpiry_ShouldExpireItem()
    {
        // Arrange
        const string key = "test-key";
        var entity = new TestEntity { Id = "1", Name = "Test" };
        var expiry = TimeSpan.FromMilliseconds(50);

        // Act
        await _cacheManager.SetCacheAsync(key, entity, expiry);

        // Verify item is initially there
        var initialResult = await _cacheManager.GetFromCacheAsync(key);
        Assert.NotNull(initialResult);

        // Wait for expiry
        await Task.Delay(100);

        // Verify item has expired
        var expiredResult = await _cacheManager.GetFromCacheAsync(key);
        Assert.Null(expiredResult);
    }

    [Fact]
    public async Task SetCacheAsync_WithNullKey_ShouldThrowArgumentNullException()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test" };

        // Act & Assert
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => _cacheManager.SetCacheAsync(null!, entity));
    }

    [Fact]
    public async Task SetCacheAsync_WithNullValue_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => _cacheManager.SetCacheAsync("key", null!));
    }

    [Fact]
    public async Task RemoveFromCacheAsync_ShouldRemoveItem()
    {
        // Arrange
        const string key = "test-key";
        var entity = new TestEntity { Id = "1", Name = "Test" };
        await _cacheManager.SetCacheAsync(key, entity);

        // Act
        await _cacheManager.RemoveFromCacheAsync(key);

        // Assert
        var result = await _cacheManager.GetFromCacheAsync(key);
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveFromCacheAsync_WithMultipleKeys_ShouldRemoveAllItems()
    {
        // Arrange
        var keys = new[] { "key1", "key2", "key3" };
        var entity = new TestEntity { Id = "1", Name = "Test" };

        foreach (var key in keys)
        {
            await _cacheManager.SetCacheAsync(key, entity);
        }

        // Act
        await _cacheManager.RemoveFromCacheAsync(keys);

        // Assert
        foreach (var key in keys)
        {
            var result = await _cacheManager.GetFromCacheAsync(key);
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task ExistsInCacheAsync_WhenItemExists_ShouldReturnTrue()
    {
        // Arrange
        const string key = "test-key";
        var entity = new TestEntity { Id = "1", Name = "Test" };
        await _cacheManager.SetCacheAsync(key, entity);

        // Act
        var exists = await _cacheManager.ExistsInCacheAsync(key);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsInCacheAsync_WhenItemDoesNotExist_ShouldReturnFalse()
    {
        // Arrange
        const string key = "non-existent-key";

        // Act
        var exists = await _cacheManager.ExistsInCacheAsync(key);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task InvalidateAllAsync_ShouldRemoveAllItems()
    {
        // Arrange
        var keys = new[] { "key1", "key2", "key3" };
        var entity = new TestEntity { Id = "1", Name = "Test" };

        foreach (var key in keys)
        {
            await _cacheManager.SetCacheAsync(key, entity);
        }

        // Act
        await _cacheManager.InvalidateAllAsync();

        // Assert
        foreach (var key in keys)
        {
            var result = await _cacheManager.GetFromCacheAsync(key);
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task InvalidateByPatternAsync_ShouldRemoveMatchingItems()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test" };
        await _cacheManager.SetCacheAsync("user:123", entity);
        await _cacheManager.SetCacheAsync("user:456", entity);
        await _cacheManager.SetCacheAsync("chat:789", entity);

        // Act - Remove all user keys
        await _cacheManager.InvalidateByPatternAsync("user:.*");

        // Assert
        Assert.Null(await _cacheManager.GetFromCacheAsync("user:123"));
        Assert.Null(await _cacheManager.GetFromCacheAsync("user:456"));
        Assert.NotNull(await _cacheManager.GetFromCacheAsync("chat:789")); // Should remain
    }

    [Fact]
    public async Task GetCacheStatisticsAsync_ShouldReturnCorrectStatistics()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test" };

        // Add some items
        await _cacheManager.SetCacheAsync("key1", entity);
        await _cacheManager.SetCacheAsync("key2", entity);

        // Generate some hits and misses
        _ = await _cacheManager.GetFromCacheAsync("key1"); // Hit
        _ = await _cacheManager.GetFromCacheAsync("key2"); // Hit
        _ = await _cacheManager.GetFromCacheAsync("non-existent"); // Miss

        // Act
        var statistics = await _cacheManager.GetCacheStatisticsAsync();

        // Assert
        Assert.NotNull(statistics);
        Assert.Equal(2, statistics.HitCount);
        Assert.Equal(1, statistics.MissCount);
        Assert.Equal(3, statistics.TotalRequests);
        Assert.True(statistics.HitRatio > 0.5);
        Assert.Equal(2, statistics.EntryCount);
        Assert.True(statistics.EstimatedMemoryUsage > 0);

        Assert.NotNull(statistics.AdditionalMetrics);
        Assert.Equal("Memory", statistics.AdditionalMetrics["CacheType"]);
        Assert.Equal("TestEntity", statistics.AdditionalMetrics["EntityType"]);
    }

    [Fact]
    public async Task GetCacheStatisticsAsync_WithEmptyCache_ShouldReturnZeroStatistics()
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
    }

    [Fact]
    public void Dispose_ShouldNotThrowException()
    {
        // Act & Assert - Should not throw
        _cacheManager.Dispose();
        _cacheManager.Dispose(); // Should be safe to call multiple times
    }

    [Fact]
    public async Task Operations_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        _cacheManager.Dispose();

        // Act & Assert
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => _cacheManager.GetFromCacheAsync("key"));
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => _cacheManager.SetCacheAsync("key", new TestEntity()));
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => _cacheManager.RemoveFromCacheAsync("key"));
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => _cacheManager.ExistsInCacheAsync("key"));
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => _cacheManager.InvalidateAllAsync());
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => _cacheManager.InvalidateByPatternAsync(".*"));
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => _cacheManager.GetCacheStatisticsAsync());
    }

    public void Dispose()
    {
        _cacheManager?.Dispose();
        _memoryCache?.Dispose();
        GC.SuppressFinalize(this);
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