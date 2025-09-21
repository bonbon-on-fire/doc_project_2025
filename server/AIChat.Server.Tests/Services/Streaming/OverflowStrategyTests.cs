using AIChat.Server.Configuration;
using AIChat.Server.Services.Streaming.Strategies;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services.Streaming;

public class OverflowStrategyTests
{
    private readonly Mock<ILogger> _loggerMock;
    private readonly OverflowStrategyConfiguration _configuration;

    public OverflowStrategyTests()
    {
        _loggerMock = new Mock<ILogger>();
        _configuration = new OverflowStrategyConfiguration
        {
            Primary = OverflowStrategy.Backpressure,
            Fallback = OverflowStrategy.DropOldest,
            DropThreshold = 95.0f,
            EnableMetrics = true,
            MaxDropBatchSize = 10
        };
    }

    [Fact]
    public async Task BackpressureStrategyAppliesDelay()
    {
        // Arrange
        var strategy = new BackpressureStrategy();
        var context = new OverflowContext
        {
            UtilizationPercentage = 90,
            CurrentSize = 90,
            Capacity = 100,
            PendingItems = 5,
            Configuration = _configuration,
            Logger = _loggerMock.Object
        };

        // Act
        var startTime = DateTime.UtcNow;
        var result = await strategy.HandleOverflowAsync(context);
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        Assert.True(result.Success);
        Assert.Equal(OverflowAction.BackpressureApplied, result.Action);
        Assert.NotNull(result.DelayApplied);
        Assert.True(result.DelayApplied.Value.TotalMilliseconds > 0);
        Assert.True(elapsed.TotalMilliseconds >= 50); // Should apply some delay
        Assert.True(result.ShouldRetry);
    }

    [Fact]
    public async Task BackpressureStrategyTracksStatistics()
    {
        // Arrange
        var strategy = new BackpressureStrategy();
        var context = new OverflowContext
        {
            UtilizationPercentage = 85,
            CurrentSize = 85,
            Capacity = 100,
            PendingItems = 3,
            Configuration = _configuration,
            Logger = _loggerMock.Object
        };

        // Act
        await strategy.HandleOverflowAsync(context);
        await strategy.HandleOverflowAsync(context);
        var stats = strategy.GetStatistics();

        // Assert
        Assert.Equal(2, stats.TotalOverflowEvents);
        Assert.Equal(2, stats.SuccessfulHandlings);
        Assert.Equal(0, stats.FailedHandlings);
        Assert.True(stats.TotalDelayApplied.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task DropOldestStrategyReportsDroppedItems()
    {
        // Arrange
        var strategy = new DropOldestStrategy();
        var droppableItems = new List<object> { "item1", "item2", "item3", "item4", "item5" };
        var context = new OverflowContext
        {
            UtilizationPercentage = 95,
            CurrentSize = 95,
            Capacity = 100,
            PendingItems = 5,
            DroppableItems = droppableItems,
            Configuration = _configuration,
            Logger = _loggerMock.Object
        };

        // Act
        var result = await strategy.HandleOverflowAsync(context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(OverflowAction.DroppedOldest, result.Action);
        Assert.True(result.ItemsAffected > 0);
        Assert.True(result.ShouldRetry);
        Assert.True(droppableItems.Count < 5); // Items should be removed
    }

    [Fact]
    public async Task DropNewestStrategyRejectsNewItems()
    {
        // Arrange
        var strategy = new DropNewestStrategy();
        var context = new OverflowContext
        {
            UtilizationPercentage = 98,
            CurrentSize = 98,
            Capacity = 100,
            PendingItems = 10,
            Configuration = _configuration,
            Logger = _loggerMock.Object
        };

        // Act
        var result = await strategy.HandleOverflowAsync(context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(OverflowAction.DroppedNewest, result.Action);
        Assert.Equal(10, result.ItemsAffected); // All pending items rejected
        Assert.False(result.ShouldRetry); // Don't retry - items are rejected
    }

    [Fact]
    public void OverflowStrategyFactoryCreatesCorrectStrategies()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<OverflowStrategyFactory>>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(x => x.GetService(typeof(ILogger<HybridStrategy>)))
            .Returns(new Mock<ILogger<HybridStrategy>>().Object);
        var factory = new OverflowStrategyFactory(loggerMock.Object, serviceProviderMock.Object);

        // Act
        var backpressure = factory.GetStrategy(OverflowStrategy.Backpressure);
        var dropOldest = factory.GetStrategy(OverflowStrategy.DropOldest);
        var dropNewest = factory.GetStrategy(OverflowStrategy.DropNewest);
        var hybrid = factory.GetStrategy(OverflowStrategy.Hybrid);

        // Assert
        Assert.Equal("Backpressure", backpressure.Name);
        Assert.Equal("DropOldest", dropOldest.Name);
        Assert.Equal("DropNewest", dropNewest.Name);
        Assert.Equal("Hybrid", hybrid.Name);
    }

    [Fact]
    public void OverflowStrategyFactoryGetAllStatisticsReturnsAllStrategies()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<OverflowStrategyFactory>>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(x => x.GetService(typeof(ILogger<HybridStrategy>)))
            .Returns(new Mock<ILogger<HybridStrategy>>().Object);
        var factory = new OverflowStrategyFactory(loggerMock.Object, serviceProviderMock.Object);

        // Act
        var allStats = factory.GetAllStatistics();

        // Assert
        Assert.Equal(4, allStats.Count);
        Assert.True(allStats.ContainsKey("Backpressure"));
        Assert.True(allStats.ContainsKey("DropOldest"));
        Assert.True(allStats.ContainsKey("DropNewest"));
        Assert.True(allStats.ContainsKey("Hybrid"));
    }

    [Fact]
    public async Task HybridStrategyUsesBackpressureUnderThreshold()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<OverflowStrategyFactory>>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(x => x.GetService(typeof(ILogger<HybridStrategy>)))
            .Returns(new Mock<ILogger<HybridStrategy>>().Object);
        var factory = new OverflowStrategyFactory(loggerMock.Object, serviceProviderMock.Object);
        var hybrid = factory.GetStrategy(OverflowStrategy.Hybrid);

        var context = new OverflowContext
        {
            UtilizationPercentage = 85, // Below drop threshold
            CurrentSize = 85,
            Capacity = 100,
            PendingItems = 5,
            Configuration = _configuration,
            Logger = _loggerMock.Object
        };

        // Act
        var startTime = DateTime.UtcNow;
        var result = await hybrid.HandleOverflowAsync(context);
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        Assert.True(result.Success);
        Assert.Equal(OverflowAction.HybridAction, result.Action);
        Assert.NotNull(result.DelayApplied); // Should apply delay (backpressure)
        Assert.True(elapsed.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task HybridStrategyDropsItemsOverThreshold()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<OverflowStrategyFactory>>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(x => x.GetService(typeof(ILogger<HybridStrategy>)))
            .Returns(new Mock<ILogger<HybridStrategy>>().Object);
        var factory = new OverflowStrategyFactory(loggerMock.Object, serviceProviderMock.Object);
        var hybrid = factory.GetStrategy(OverflowStrategy.Hybrid);

        var droppableItems = new List<object> { "item1", "item2", "item3" };
        var context = new OverflowContext
        {
            UtilizationPercentage = 96, // Above drop threshold
            CurrentSize = 96,
            Capacity = 100,
            PendingItems = 3,
            DroppableItems = droppableItems,
            Configuration = _configuration,
            Logger = _loggerMock.Object
        };

        // Act
        var result = await hybrid.HandleOverflowAsync(context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(OverflowAction.HybridAction, result.Action);
        Assert.True(result.ItemsAffected > 0); // Should drop items
        Assert.True(droppableItems.Count < 3); // Items should be removed
    }
}