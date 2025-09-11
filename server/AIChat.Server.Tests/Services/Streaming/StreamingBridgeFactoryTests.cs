using AIChat.Server.Configuration;
using AIChat.Server.Services.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services.Streaming;

public class StreamingBridgeFactoryTests
{
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<ILogger<StreamingBridge>> _loggerMock;
    private readonly StreamingConfiguration _defaultConfiguration;
    private readonly IOptions<StreamingConfiguration> _configurationOptions;
    private readonly StreamingBridgeFactory _factory;

    public StreamingBridgeFactoryTests()
    {
        _loggerMock = new Mock<ILogger<StreamingBridge>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _ = _loggerFactoryMock
            .Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(_loggerMock.Object);

        _defaultConfiguration = new StreamingConfiguration
        {
            BufferSize = 100,
            BackpressureThreshold = 80.0f,
            WriteTimeoutMs = 30000,
            BackpressureDelayMs = 100,
            EnableAdaptiveBackpressure = true,
            EnableTelemetry = true,
        };

        _configurationOptions = Options.Create(_defaultConfiguration);
        _factory = new StreamingBridgeFactory(_loggerFactoryMock.Object, _configurationOptions);
    }

    [Fact]
    public void CreateBridgeReturnsNewInstance()
    {
        // Act
        var bridge = _factory.CreateBridge();

        // Assert
        Assert.NotNull(bridge);
        _ = Assert.IsType<StreamingBridge>(bridge);
    }

    [Fact]
    public void CreateBridgeWithDefaultConfigurationUsesDefaultValues()
    {
        // Act
        var bridge = _factory.CreateBridge();
        var stats = bridge.GetBufferStatistics();

        // Assert
        Assert.Equal(100, stats.Capacity); // Default buffer size
    }

    [Fact]
    public void CreateBridgeWithCustomConfigurationAppliesCustomValues()
    {
        // Arrange
        var customBufferSize = 200;

        // Act
        var bridge = _factory.CreateBridge(config => config.BufferSize = customBufferSize);
        var stats = bridge.GetBufferStatistics();

        // Assert
        Assert.Equal(customBufferSize, stats.Capacity);
    }

    [Fact]
    public void CreateBridgeMultipleInstancesReturnsUniqueInstances()
    {
        // Act
        var bridge1 = _factory.CreateBridge();
        var bridge2 = _factory.CreateBridge();

        // Assert
        Assert.NotNull(bridge1);
        Assert.NotNull(bridge2);
        Assert.NotSame(bridge1, bridge2);
    }

    [Fact]
    public void CreateBridgeWithCustomConfigurationDoesNotAffectDefault()
    {
        // Act
        var customBridge = _factory.CreateBridge(config => config.BufferSize = 500);
        var defaultBridge = _factory.CreateBridge();

        var customStats = customBridge.GetBufferStatistics();
        var defaultStats = defaultBridge.GetBufferStatistics();

        // Assert
        Assert.Equal(500, customStats.Capacity);
        Assert.Equal(100, defaultStats.Capacity); // Should still be default
    }

    [Fact]
    public void CreateBridgeWithNullConfigureActionThrowsException()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() => _factory.CreateBridge(null!));
    }

    [Fact]
    public void ConstructorWithNullLoggerFactoryThrowsException()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(
            () => new StreamingBridgeFactory(null!, _configurationOptions)
        );
    }

    [Fact]
    public void ConstructorWithNullConfigurationThrowsException()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(
            () => new StreamingBridgeFactory(_loggerFactoryMock.Object, null!)
        );
    }

    [Fact]
    public void CreateBridgeWithCompleteCustomConfigurationAppliesAllSettings()
    {
        // Act
        var bridge = _factory.CreateBridge(config =>
        {
            config.BufferSize = 250;
            config.BackpressureThreshold = 75.0f;
            config.WriteTimeoutMs = 15000;
            config.BackpressureDelayMs = 200;
            config.EnableAdaptiveBackpressure = false;
            config.EnableTelemetry = false;
            config.MaxChunkSize = 16384;
            config.FlushIntervalMs = 200;
            config.EnableAutoRetry = false;
            config.MaxRetryAttempts = 5;
        });

        // Assert
        Assert.NotNull(bridge);
        var stats = bridge.GetBufferStatistics();
        Assert.Equal(250, stats.Capacity);
    }

    [Fact]
    public async Task CreateBridgeDisposesCorrectly()
    {
        // Arrange
        var bridge = _factory.CreateBridge();

        // Act & Assert
        await bridge.DisposeAsync();

        // Should not throw
        await bridge.DisposeAsync(); // Double dispose should be safe
    }
}
