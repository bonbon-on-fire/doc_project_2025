using AIChat.Server.Configuration;
using AIChat.Server.Services.Abstractions;
using AIChat.Server.Services.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services.Streaming;

public class AdaptiveBufferManagerTests
{
    private readonly Mock<ILogger<AdaptiveBufferManager>> _loggerMock;
    private readonly Mock<ITrendAnalyzer> _trendAnalyzerMock;
    private readonly Mock<ISystemTime> _systemTimeMock;
    private readonly Mock<ITimerFactory> _timerFactoryMock;
    private readonly Mock<AIChat.Server.Services.Abstractions.ITimer> _timerMock;
    private readonly StreamingConfiguration _configuration;
    private readonly IOptions<StreamingConfiguration> _options;
    private readonly DateTime _testTime = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public AdaptiveBufferManagerTests()
    {
        _loggerMock = new Mock<ILogger<AdaptiveBufferManager>>();
        _trendAnalyzerMock = new Mock<ITrendAnalyzer>();
        _systemTimeMock = new Mock<ISystemTime>();
        _timerFactoryMock = new Mock<ITimerFactory>();
        _timerMock = new Mock<AIChat.Server.Services.Abstractions.ITimer>();

        // Setup system time mock
        _systemTimeMock.Setup(x => x.UtcNow).Returns(_testTime);

        // Setup timer factory mock
        _timerFactoryMock.Setup(x => x.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>()))
            .Returns(_timerMock.Object);

        _configuration = new StreamingConfiguration
        {
            BufferSize = 100,
            AdaptiveBuffering = new AdaptiveBufferingConfiguration
            {
                Enabled = true,
                MinSize = 50,
                MaxSize = 500,
                ScaleUpThreshold = 80,
                ScaleDownThreshold = 30,
                WindowSizeMinutes = 5,
                ScaleFactor = 1.5f,
                ScaleCooldownSeconds = 1 // Short for testing
            }
        };
        _options = Options.Create(_configuration);
    }

    [Fact]
    public void ConstructorInitializesCorrectly()
    {
        // Act
        using var manager = new AdaptiveBufferManager(
            _loggerMock.Object,
            _options,
            _trendAnalyzerMock.Object,
            _systemTimeMock.Object,
            _timerFactoryMock.Object,
            100);

        // Assert
        Assert.Equal(100, manager.CurrentBufferSize);
        var stats = manager.GetStatistics();
        Assert.Equal(0, stats.TotalAdjustments);
        Assert.Equal(50, stats.MinSize);
        Assert.Equal(500, stats.MaxSize);
    }

    [Fact]
    public void ConstructorClampsInitialSizeToLimits()
    {
        // Act - Test with size too large
        using var managerLarge = new AdaptiveBufferManager(
            _loggerMock.Object,
            _options,
            _trendAnalyzerMock.Object,
            _systemTimeMock.Object,
            _timerFactoryMock.Object,
            1000);
        Assert.Equal(500, managerLarge.CurrentBufferSize); // Clamped to max

        // Act - Test with size too small
        using var managerSmall = new AdaptiveBufferManager(
            _loggerMock.Object,
            _options,
            _trendAnalyzerMock.Object,
            _systemTimeMock.Object,
            _timerFactoryMock.Object,
            10);
        Assert.Equal(50, managerSmall.CurrentBufferSize); // Clamped to min
    }

    [Fact]
    public void RecordUsageAddsDataPoints()
    {
        // Arrange
        using var manager = new AdaptiveBufferManager(
            _loggerMock.Object,
            _options,
            _trendAnalyzerMock.Object,
            _systemTimeMock.Object,
            _timerFactoryMock.Object,
            100);

        // Act
        manager.RecordUsage(50, 100);
        manager.RecordUsage(60, 100);
        manager.RecordUsage(70, 100);

        // Assert
        var stats = manager.GetStatistics();
        Assert.True(stats.DataPointsCount >= 3);
    }

    [Fact]
    public void ScaleUpTriggeredOnHighUtilization()
    {
        // Arrange
        _trendAnalyzerMock.Setup(x => x.CalculateTrend(It.IsAny<IEnumerable<UsageDataPoint>>()))
            .Returns(UsageTrend.Stable);
        _trendAnalyzerMock.Setup(x => x.GetScalingRecommendation(
                It.IsAny<float>(),
                It.IsAny<float>(),
                It.IsAny<UsageTrend>(),
                It.IsAny<float>(),
                It.IsAny<float>()))
            .Returns(new ScalingRecommendation
            {
                Action = ScalingAction.ScaleUp,
                ScaleFactor = 1.5f,
                Reason = "High utilization"
            });

        TimerCallback? capturedCallback = null;
        _timerFactoryMock.Setup(x => x.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>()))
            .Callback<TimerCallback, object?>((callback, state) => capturedCallback = callback)
            .Returns(_timerMock.Object);

        using var manager = new AdaptiveBufferManager(
            _loggerMock.Object,
            _options,
            _trendAnalyzerMock.Object,
            _systemTimeMock.Object,
            _timerFactoryMock.Object,
            100);

        var sizeChanged = false;
        var newSize = 0;

        manager.BufferSizeChanged += (sender, args) =>
        {
            sizeChanged = true;
            newSize = args.NewSize;
        };

        // Act - Record high utilization
        for (var i = 0; i < 15; i++)
        {
            manager.RecordUsage(85, 100); // 85% utilization
        }

        // Trigger the timer callback manually
        capturedCallback?.Invoke(null);

        // Assert
        Assert.True(sizeChanged, "Buffer size should have changed");
        Assert.True(newSize > 100, $"New size ({newSize}) should be greater than 100");
        Assert.Equal(150, newSize); // Should scale by factor of 1.5
    }

    [Fact]
    public void ScaleDownTriggeredOnLowUtilization()
    {
        // Arrange
        _trendAnalyzerMock.Setup(x => x.CalculateTrend(It.IsAny<IEnumerable<UsageDataPoint>>()))
            .Returns(UsageTrend.Stable);
        _trendAnalyzerMock.Setup(x => x.GetScalingRecommendation(
                It.IsAny<float>(),
                It.IsAny<float>(),
                It.IsAny<UsageTrend>(),
                It.IsAny<float>(),
                It.IsAny<float>()))
            .Returns(new ScalingRecommendation
            {
                Action = ScalingAction.ScaleDown,
                ScaleFactor = 1.5f,
                Reason = "Low utilization"
            });

        TimerCallback? capturedCallback = null;
        _timerFactoryMock.Setup(x => x.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>()))
            .Callback<TimerCallback, object?>((callback, state) => capturedCallback = callback)
            .Returns(_timerMock.Object);

        using var manager = new AdaptiveBufferManager(
            _loggerMock.Object,
            _options,
            _trendAnalyzerMock.Object,
            _systemTimeMock.Object,
            _timerFactoryMock.Object,
            200);

        var sizeChanged = false;
        var newSize = 0;

        manager.BufferSizeChanged += (sender, args) =>
        {
            sizeChanged = true;
            newSize = args.NewSize;
        };

        // Act - Record low utilization
        for (var i = 0; i < 15; i++)
        {
            manager.RecordUsage(40, 200); // 20% utilization
        }

        // Trigger the timer callback manually
        capturedCallback?.Invoke(null);

        // Assert
        Assert.True(sizeChanged, "Buffer size should have changed");
        Assert.True(newSize < 200, $"New size ({newSize}) should be less than 200");
    }

    [Fact]
    public void GetStatisticsReturnsCorrectData()
    {
        // Arrange
        using var manager = new AdaptiveBufferManager(
            _loggerMock.Object,
            _options,
            _trendAnalyzerMock.Object,
            _systemTimeMock.Object,
            _timerFactoryMock.Object,
            100);

        // Act
        for (var i = 0; i < 10; i++)
        {
            manager.RecordUsage(50 + i, 100);
        }

        var stats = manager.GetStatistics();

        // Assert
        Assert.Equal(100, stats.CurrentSize);
        Assert.Equal(50, stats.MinSize);
        Assert.Equal(500, stats.MaxSize);
        Assert.True(stats.AverageUtilization > 0);
        Assert.True(stats.DataPointsCount >= 10);
    }

    [Fact]
    public void DisposeCleansUpResources()
    {
        // Arrange
        var manager = new AdaptiveBufferManager(
            _loggerMock.Object,
            _options,
            _trendAnalyzerMock.Object,
            _systemTimeMock.Object,
            _timerFactoryMock.Object,
            100);

        // Act
        manager.Dispose();

        // Assert - Should not throw
        manager.Dispose(); // Double dispose should be safe

        // Verify logging occurred
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("disposed")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify timer was disposed
        _timerMock.Verify(x => x.Dispose(), Times.Once);
    }
}