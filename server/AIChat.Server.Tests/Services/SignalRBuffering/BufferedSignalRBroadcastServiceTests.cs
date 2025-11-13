using AIChat.Orleans.Services;
using AIChat.Server.Services.SignalRBuffering.Abstractions;
using AIChat.Server.Services.SignalRBuffering.Implementations;
using AIChat.Server.Services.SignalRBuffering.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services.SignalRBuffering;

/// <summary>
/// Unit tests for BufferedSignalRBroadcastService to validate decorator functionality,
/// buffering behavior, fallback mechanisms, and metrics collection.
/// </summary>
public class BufferedSignalRBroadcastServiceTests : IDisposable
{
    private readonly Mock<ISignalRBroadcastService> _mockInnerService;
    private readonly Mock<ISignalRMessageBuffer> _mockMessageBuffer;
    private readonly Mock<ILogger<BufferedSignalRBroadcastService>> _mockLogger;
    private readonly SignalRBufferConfiguration _config;
    private readonly BufferedSignalRBroadcastService _service;

    public BufferedSignalRBroadcastServiceTests()
    {
        _mockInnerService = new Mock<ISignalRBroadcastService>();
        _mockMessageBuffer = new Mock<ISignalRMessageBuffer>();
        _mockLogger = new Mock<ILogger<BufferedSignalRBroadcastService>>();

        _config = new SignalRBufferConfiguration
        {
            Enabled = true,
            MaxBufferSize = 100,
            BatchSize = 10,
            ProcessingInterval = TimeSpan.FromMilliseconds(100),
            EnableDeliveryConfirmation = true,
            EnableMetrics = true
        };

        var configOptions = Options.Create(_config);

        // Setup default mock behaviors
        _mockInnerService.Setup(x => x.IsAvailable).Returns(true);

        _mockMessageBuffer
            .Setup(x => x.EnqueueAsync(It.IsAny<SignalRMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SignalRMessage msg, CancellationToken ct) =>
                BufferResult.CreateSuccess(1));

        _mockMessageBuffer
            .Setup(x => x.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BufferHealth
            {
                IsHealthy = true,
                Status = BufferHealthStatus.Healthy,
                Message = "Buffer is operating normally"
            });

        _mockMessageBuffer
            .Setup(x => x.GetMaxCapacity())
            .Returns(_config.MaxBufferSize);

        _service = new BufferedSignalRBroadcastService(
            _mockInnerService.Object,
            _mockMessageBuffer.Object,
            configOptions,
            _mockLogger.Object);
    }

    [Fact]
    public void IsAvailable_WhenBufferingEnabledAndBufferHealthy_ShouldReturnTrue()
    {
        // Arrange
        _config.Enabled = true;
        _mockInnerService.Setup(x => x.IsAvailable).Returns(true);

        // Act
        var result = _service.IsAvailable;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsAvailable_WhenBufferingDisabled_ShouldReturnInnerServiceAvailability()
    {
        // Arrange
        _config.Enabled = false;
        _mockInnerService.Setup(x => x.IsAvailable).Returns(true);

        // Act
        var result = _service.IsAvailable;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsAvailable_WhenBufferUnhealthyButInnerServiceAvailable_ShouldReturnTrue()
    {
        // Arrange
        _mockMessageBuffer
            .Setup(x => x.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BufferHealth
            {
                IsHealthy = false,
                Status = BufferHealthStatus.Unhealthy,
                Message = "Buffer is unhealthy"
            });

        _mockInnerService.Setup(x => x.IsAvailable).Returns(true);

        // Act
        var result = _service.IsAvailable;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task BroadcastToGroupAsync_WithValidMessage_WhenBufferingEnabled_ShouldEnqueueMessage()
    {
        // Arrange
        const string groupName = "test-group";
        const string methodName = "TestMethod";
        var payload = new { Message = "Test message" };

        // Act
        await _service.BroadcastToGroupAsync(groupName, methodName, payload);

        // Assert
        _mockMessageBuffer.Verify(
            x => x.EnqueueAsync(It.Is<SignalRMessage>(msg =>
                msg.GroupName == groupName &&
                msg.MethodName == methodName &&
                msg.Payload.Equals(payload)), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify inner service was NOT called directly
        _mockInnerService.Verify(
            x => x.BroadcastToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()),
            Times.Never);
    }

    [Fact]
    public async Task BroadcastToGroupAsync_WithValidMessage_WhenBufferingDisabled_ShouldCallInnerService()
    {
        // Arrange
        _config.Enabled = false;
        const string groupName = "test-group";
        const string methodName = "TestMethod";
        var payload = new { Message = "Test message" };

        // Act
        await _service.BroadcastToGroupAsync(groupName, methodName, payload);

        // Assert
        _mockInnerService.Verify(
            x => x.BroadcastToGroupAsync(groupName, methodName, payload),
            Times.Once);

        // Verify buffer was NOT used
        _mockMessageBuffer.Verify(
            x => x.EnqueueAsync(It.IsAny<SignalRMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BroadcastToGroupAsync_WithNullGroupName_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.BroadcastToGroupAsync(null!, "TestMethod", new { }));
    }

    [Fact]
    public async Task BroadcastToGroupAsync_WithNullMethodName_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.BroadcastToGroupAsync("test-group", null!, new { }));
    }

    [Fact]
    public async Task BroadcastToGroupAsync_WithNullPayload_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.BroadcastToGroupAsync("test-group", "TestMethod", null!));
    }

    [Fact]
    public async Task BroadcastToGroupAsync_WhenBufferEnqueueFails_ShouldFallbackToDirectBroadcast()
    {
        // Arrange
        const string groupName = "test-group";
        const string methodName = "TestMethod";
        var payload = new { Message = "Test message" };

        _mockMessageBuffer
            .Setup(x => x.EnqueueAsync(It.IsAny<SignalRMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BufferResult.Rejected(BufferOperationResult.Overflow, 100, "Buffer is full"));

        // Act
        await _service.BroadcastToGroupAsync(groupName, methodName, payload);

        // Assert
        // Should attempt buffering first
        _mockMessageBuffer.Verify(
            x => x.EnqueueAsync(It.IsAny<SignalRMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Should fallback to inner service
        _mockInnerService.Verify(
            x => x.BroadcastToGroupAsync(groupName, methodName, payload),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastToGroupAsync_WhenBufferUnhealthy_ShouldCallInnerServiceDirectly()
    {
        // Arrange
        const string groupName = "test-group";
        const string methodName = "TestMethod";
        var payload = new { Message = "Test message" };

        _mockMessageBuffer
            .Setup(x => x.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BufferHealth
            {
                IsHealthy = false,
                Status = BufferHealthStatus.Unhealthy,
                Message = "Buffer is unhealthy"
            });

        // Act
        await _service.BroadcastToGroupAsync(groupName, methodName, payload);

        // Assert
        _mockInnerService.Verify(
            x => x.BroadcastToGroupAsync(groupName, methodName, payload),
            Times.Once);

        // Buffer should not be used when unhealthy
        _mockMessageBuffer.Verify(
            x => x.EnqueueAsync(It.IsAny<SignalRMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BroadcastToGroupAsync_WhenInnerServiceUnavailable_ShouldForceBuffering()
    {
        // Arrange
        const string groupName = "test-group";
        const string methodName = "TestMethod";
        var payload = new { Message = "Test message" };

        _mockInnerService.Setup(x => x.IsAvailable).Returns(false);

        // Act
        await _service.BroadcastToGroupAsync(groupName, methodName, payload);

        // Assert
        _mockMessageBuffer.Verify(
            x => x.EnqueueAsync(It.IsAny<SignalRMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Inner service should not be called when unavailable
        _mockInnerService.Verify(
            x => x.BroadcastToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()),
            Times.Never);
    }

    [Fact]
    public async Task BroadcastToGroupAsync_WithErrorMethodName_ShouldSetHighPriority()
    {
        // Arrange
        const string groupName = "test-group";
        const string methodName = "OnError";
        var payload = new { Message = "Error occurred" };

        SignalRMessage? capturedMessage = null;
        _mockMessageBuffer
            .Setup(x => x.EnqueueAsync(It.IsAny<SignalRMessage>(), It.IsAny<CancellationToken>()))
            .Callback<SignalRMessage, CancellationToken>((msg, ct) => capturedMessage = msg)
            .ReturnsAsync(BufferResult.CreateSuccess(1));

        // Act
        await _service.BroadcastToGroupAsync(groupName, methodName, payload);

        // Assert
        Assert.NotNull(capturedMessage);
        Assert.Equal(MessagePriority.High, capturedMessage.Priority);
    }

    [Fact]
    public async Task BroadcastToGroupAsync_WithSystemMethodName_ShouldSetCriticalPriority()
    {
        // Arrange
        const string groupName = "test-group";
        const string methodName = "OnSystemNotification";
        var payload = new { Message = "System notification" };

        SignalRMessage? capturedMessage = null;
        _mockMessageBuffer
            .Setup(x => x.EnqueueAsync(It.IsAny<SignalRMessage>(), It.IsAny<CancellationToken>()))
            .Callback<SignalRMessage, CancellationToken>((msg, ct) => capturedMessage = msg)
            .ReturnsAsync(BufferResult.CreateSuccess(1));

        // Act
        await _service.BroadcastToGroupAsync(groupName, methodName, payload);

        // Assert
        Assert.NotNull(capturedMessage);
        Assert.Equal(MessagePriority.Critical, capturedMessage.Priority);
    }

    [Fact]
    public async Task BroadcastToGroupAsync_WithRegularMethodName_ShouldSetNormalPriority()
    {
        // Arrange
        const string groupName = "test-group";
        const string methodName = "OnMessageReceived";
        var payload = new { Message = "Regular message" };

        SignalRMessage? capturedMessage = null;
        _mockMessageBuffer
            .Setup(x => x.EnqueueAsync(It.IsAny<SignalRMessage>(), It.IsAny<CancellationToken>()))
            .Callback<SignalRMessage, CancellationToken>((msg, ct) => capturedMessage = msg)
            .ReturnsAsync(BufferResult.CreateSuccess(1));

        // Act
        await _service.BroadcastToGroupAsync(groupName, methodName, payload);

        // Assert
        Assert.NotNull(capturedMessage);
        Assert.Equal(MessagePriority.Normal, capturedMessage.Priority);
    }

    [Fact]
    public async Task GetMetricsAsync_ShouldReturnComprehensiveMetrics()
    {
        // Arrange
        var bufferMetrics = new BufferMetrics
        {
            CurrentBufferSize = 5,
            MaxBufferCapacity = 100,
            BufferUtilizationPercent = 5.0,
            TotalEnqueued = 10,
            TotalDelivered = 8,
            TotalDropped = 1,
            TotalFailed = 1,
            DeliverySuccessRate = 80.0,
            MessagesPerSecond = 2.5
        };

        var bufferHealth = new BufferHealth
        {
            IsHealthy = true,
            Status = BufferHealthStatus.Healthy,
            Message = "Buffer is operating normally"
        };

        _mockMessageBuffer
            .Setup(x => x.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(bufferMetrics);

        _mockMessageBuffer
            .Setup(x => x.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(bufferHealth);

        // Simulate some broadcast requests
        await _service.BroadcastToGroupAsync("group1", "Method1", new { });
        await _service.BroadcastToGroupAsync("group2", "Method2", new { });

        // Act
        var metrics = await _service.GetMetricsAsync();

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(2, metrics.TotalBroadcastRequests);
        Assert.Equal(2, metrics.BufferedRequests);
        Assert.Equal(0, metrics.DirectRequests);
        Assert.Equal(0, metrics.FailedRequests);
        Assert.Equal(100.0, metrics.BufferingSuccessRate);
        Assert.Equal(100.0, metrics.OverallSuccessRate);
        Assert.Equal(5.0, metrics.BufferUtilization);
        Assert.Equal(BufferHealthStatus.Healthy, metrics.BufferHealth);
        Assert.True(metrics.IsBufferingEnabled);
        Assert.True(metrics.InnerServiceAvailable);
        Assert.True(metrics.ServiceAvailable);
    }

    [Fact]
    public async Task GetMetricsAsync_WithMixedBroadcastTypes_ShouldTrackCorrectly()
    {
        // Arrange - Setup mock buffer metrics for this specific test
        var bufferMetrics = new BufferMetrics
        {
            CurrentBufferSize = 1,
            MaxBufferCapacity = 100,
            BufferUtilizationPercent = 1.0,
            TotalEnqueued = 1,
            TotalDelivered = 0,
            TotalDropped = 0,
            TotalFailed = 0,
            DeliverySuccessRate = 0.0,
            MessagesPerSecond = 0.5
        };

        var bufferHealth = new BufferHealth
        {
            IsHealthy = true,
            Status = BufferHealthStatus.Healthy,
            Message = "Buffer is operating normally"
        };

        _mockMessageBuffer
            .Setup(x => x.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(bufferMetrics);

        _mockMessageBuffer
            .Setup(x => x.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(bufferHealth);

        // Disable buffering for some calls to test direct routing
        _config.Enabled = false;

        // Act - Make some direct calls
        await _service.BroadcastToGroupAsync("group1", "Method1", new { });
        await _service.BroadcastToGroupAsync("group2", "Method2", new { });

        // Enable buffering for buffered calls
        _config.Enabled = true;
        await _service.BroadcastToGroupAsync("group3", "Method3", new { });

        var metrics = await _service.GetMetricsAsync();

        // Assert
        Assert.Equal(3, metrics.TotalBroadcastRequests);
        Assert.Equal(1, metrics.BufferedRequests);
        Assert.Equal(2, metrics.DirectRequests);
        Assert.Equal(0, metrics.FailedRequests);
    }

    [Fact]
    public void Constructor_WithNullInnerService_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BufferedSignalRBroadcastService(
            null!,
            _mockMessageBuffer.Object,
            Options.Create(_config),
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullMessageBuffer_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BufferedSignalRBroadcastService(
            _mockInnerService.Object,
            null!,
            Options.Create(_config),
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullConfig_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BufferedSignalRBroadcastService(
            _mockInnerService.Object,
            _mockMessageBuffer.Object,
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BufferedSignalRBroadcastService(
            _mockInnerService.Object,
            _mockMessageBuffer.Object,
            Options.Create(_config),
            null!));
    }

    [Fact]
    public void Dispose_ShouldCompleteCleanly()
    {
        // Act & Assert - Should not throw
        _service.Dispose();

        // Subsequent calls should also not throw
        _service.Dispose();
    }

    [Fact]
    public async Task BroadcastToGroupAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        _service.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _service.BroadcastToGroupAsync("group", "method", new { }));
    }

    [Fact]
    public async Task GetMetricsAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        _service.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(_service.GetMetricsAsync);
    }

    [Fact]
    public void IsAvailable_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        _service.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => _service.IsAvailable);
    }

    public void Dispose()
    {
        _service?.Dispose();
        GC.SuppressFinalize(this);
    }
}
