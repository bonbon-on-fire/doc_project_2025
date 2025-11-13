using AIChat.Server.Services.SignalRBuffering.Abstractions;
using AIChat.Server.Services.SignalRBuffering.Implementations;
using AIChat.Server.Services.SignalRBuffering.Models;
using AIChat.Server.Services.SignalRBuffering.Strategies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services.SignalRBuffering;

/// <summary>
/// Unit tests for InMemorySignalRMessageBuffer to validate core buffering functionality,
/// overflow handling, metrics collection, and delivery confirmation.
/// </summary>
public class InMemorySignalRMessageBufferTests : IDisposable
{
    private readonly Mock<ILogger<InMemorySignalRMessageBuffer>> _mockLogger;
    private readonly Mock<ILogger<DropOldestStrategy>> _mockStrategyLogger;
    private readonly Mock<ISignalRDeliveryService> _mockDeliveryService;
    private readonly SignalRBufferConfiguration _config;
    private readonly IBufferOverflowStrategy _overflowStrategy;
    private readonly InMemorySignalRMessageBuffer _buffer;

    public InMemorySignalRMessageBufferTests()
    {
        _mockLogger = new Mock<ILogger<InMemorySignalRMessageBuffer>>();
        _mockStrategyLogger = new Mock<ILogger<DropOldestStrategy>>();
        _mockDeliveryService = new Mock<ISignalRDeliveryService>();

        _config = new SignalRBufferConfiguration
        {
            Enabled = true,
            MaxBufferSize = 10,
            BatchSize = 5,
            ProcessingInterval = TimeSpan.FromMilliseconds(100),
            EnableDeliveryConfirmation = true,
            EnableMetrics = true,
            MaxDeadLetterQueueSize = 5, // Must not exceed MaxBufferSize
            DegradedThresholdPercent = 70,
            UnhealthyThresholdPercent = 85
        };

        _overflowStrategy = new DropOldestStrategy(_mockStrategyLogger.Object);
        var configOptions = Options.Create(_config);

        _buffer = new InMemorySignalRMessageBuffer(configOptions, _overflowStrategy, _mockLogger.Object);

        // Setup mock delivery service
        _mockDeliveryService
            .Setup(x => x.IsAvailable)
            .Returns(true);

        _mockDeliveryService
            .Setup(x => x.DeliverMessageAsync(It.IsAny<SignalRMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SignalRMessage msg, CancellationToken ct) =>
                DeliveryResult.CreateSuccess(msg.Id, TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public async Task EnqueueAsync_WithValidMessage_ShouldReturnSuccess()
    {
        // Arrange
        var message = CreateTestMessage();

        // Act
        var result = await _buffer.EnqueueAsync(message);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(BufferOperationResult.Enqueued, result.Result);
        Assert.Equal(1, result.CurrentBufferSize);
        Assert.Equal(1, _buffer.GetCurrentSize());
    }

    [Fact]
    public async Task EnqueueAsync_WithNullMessage_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _buffer.EnqueueAsync(null!));
    }

    [Fact]
    public async Task EnqueueAsync_WhenBufferingDisabled_ShouldReturnRejected()
    {
        // Arrange
        var disabledConfig = new SignalRBufferConfiguration { Enabled = false };
        var disabledConfigOptions = Options.Create(disabledConfig);
        using var disabledBuffer = new InMemorySignalRMessageBuffer(disabledConfigOptions, _overflowStrategy, _mockLogger.Object);
        var message = CreateTestMessage();

        // Act
        var result = await disabledBuffer.EnqueueAsync(message);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(BufferOperationResult.BufferUnavailable, result.Result);
    }

    [Fact]
    public async Task EnqueueAsync_WithDuplicateMessageId_ShouldReturnDuplicate()
    {
        // Arrange
        var message1 = CreateTestMessage("duplicate-id");
        var message2 = CreateTestMessage("duplicate-id");

        // Act
        var result1 = await _buffer.EnqueueAsync(message1);
        var result2 = await _buffer.EnqueueAsync(message2);

        // Assert
        Assert.True(result1.Success);
        Assert.False(result2.Success);
        Assert.Equal(BufferOperationResult.Duplicate, result2.Result);
    }

    [Fact]
    public async Task EnqueueAsync_WhenBufferFull_ShouldTriggerOverflow()
    {
        // Arrange - Fill buffer to capacity
        for (int i = 0; i < _config.MaxBufferSize; i++)
        {
            await _buffer.EnqueueAsync(CreateTestMessage($"msg-{i}"));
        }

        // Act - Add one more message to trigger overflow
        var overflowMessage = CreateTestMessage("overflow-msg");
        var result = await _buffer.EnqueueAsync(overflowMessage);

        // Assert - Should succeed with oldest message dropped
        Assert.True(result.Success);
        Assert.Equal(1, result.DroppedMessageCount);
        Assert.Equal(_config.MaxBufferSize, result.CurrentBufferSize);
    }

    [Fact]
    public async Task ProcessBufferAsync_WithValidDeliveryService_ShouldProcessMessages()
    {
        // Arrange
        var messages = new[]
        {
            CreateTestMessage("msg-1"),
            CreateTestMessage("msg-2"),
            CreateTestMessage("msg-3")
        };

        foreach (var msg in messages)
        {
            await _buffer.EnqueueAsync(msg);
        }

        // Act
        var result = await _buffer.ProcessBufferAsync(_mockDeliveryService.Object);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(DeliveryStatus.Delivered, result.Status);

        // Verify delivery service was called for each message
        _mockDeliveryService.Verify(
            x => x.DeliverMessageAsync(It.IsAny<SignalRMessage>(), It.IsAny<CancellationToken>()),
            Times.Exactly(messages.Length));
    }

    [Fact]
    public async Task ProcessBufferAsync_WithNullDeliveryService_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _buffer.ProcessBufferAsync(null!));
    }

    [Fact]
    public async Task ProcessBufferAsync_WithEmptyBuffer_ShouldReturnSuccess()
    {
        // Act
        var result = await _buffer.ProcessBufferAsync(_mockDeliveryService.Object);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Batch", result.MessageId);

        // Verify delivery service was not called
        _mockDeliveryService.Verify(
            x => x.DeliverMessageAsync(It.IsAny<SignalRMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetMetricsAsync_ShouldReturnAccurateMetrics()
    {
        // Arrange
        var message = CreateTestMessage();
        await _buffer.EnqueueAsync(message);

        // Act
        var metrics = await _buffer.GetMetricsAsync();

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.CurrentBufferSize);
        Assert.Equal(_config.MaxBufferSize, metrics.MaxBufferCapacity);
        Assert.Equal(1, metrics.TotalEnqueued);
        Assert.True(metrics.BufferUtilizationPercent > 0);
        Assert.Equal(BufferHealthStatus.Healthy, metrics.HealthStatus);
    }

    [Fact]
    public async Task GetHealthAsync_WithLowUtilization_ShouldReturnHealthy()
    {
        // Arrange - Add a few messages but stay well below capacity
        await _buffer.EnqueueAsync(CreateTestMessage("msg-1"));
        await _buffer.EnqueueAsync(CreateTestMessage("msg-2"));

        // Act
        var health = await _buffer.GetHealthAsync();

        // Assert
        Assert.NotNull(health);
        Assert.True(health.IsHealthy);
        Assert.Equal(BufferHealthStatus.Healthy, health.Status);
        Assert.Contains("normally", health.Message ?? "");
    }

    [Fact]
    public async Task GetCurrentSize_ShouldReturnCorrectSize()
    {
        // Arrange
        Assert.Equal(0, _buffer.GetCurrentSize());

        // Act & Assert - Add messages and verify size increases
        await _buffer.EnqueueAsync(CreateTestMessage("msg-1"));
        Assert.Equal(1, _buffer.GetCurrentSize());

        await _buffer.EnqueueAsync(CreateTestMessage("msg-2"));
        Assert.Equal(2, _buffer.GetCurrentSize());
    }

    [Fact]
    public void GetMaxCapacity_ShouldReturnConfiguredCapacity()
    {
        // Act & Assert
        Assert.Equal(_config.MaxBufferSize, _buffer.GetMaxCapacity());
    }

    [Fact]
    public async Task ClearAsync_ShouldRemoveAllMessages()
    {
        // Arrange
        await _buffer.EnqueueAsync(CreateTestMessage("msg-1"));
        await _buffer.EnqueueAsync(CreateTestMessage("msg-2"));
        await _buffer.EnqueueAsync(CreateTestMessage("msg-3"));
        Assert.Equal(3, _buffer.GetCurrentSize());

        // Act
        var clearedCount = await _buffer.ClearAsync();

        // Assert
        Assert.Equal(3, clearedCount);
        Assert.Equal(0, _buffer.GetCurrentSize());
    }

    [Fact]
    public async Task MessageEnqueued_Event_ShouldBeRaised()
    {
        // Arrange
        var eventRaised = false;
        MessageEnqueuedEventArgs? eventArgs = null;

        _buffer.MessageEnqueued += (sender, args) =>
        {
            eventRaised = true;
            eventArgs = args;
        };

        var message = CreateTestMessage();

        // Act
        await _buffer.EnqueueAsync(message);

        // Assert
        Assert.True(eventRaised);
        Assert.NotNull(eventArgs);
        Assert.Equal(message.Id, eventArgs.Message.Id);
        Assert.Equal(1, eventArgs.BufferSize);
    }

    [Fact]
    public async Task BufferOverflow_Event_ShouldBeRaisedWhenOverflowOccurs()
    {
        // Arrange
        var eventRaised = false;
        BufferOverflowEventArgs? eventArgs = null;

        _buffer.BufferOverflow += (sender, args) =>
        {
            eventRaised = true;
            eventArgs = args;
        };

        // Fill buffer to capacity
        for (int i = 0; i < _config.MaxBufferSize; i++)
        {
            await _buffer.EnqueueAsync(CreateTestMessage($"msg-{i}"));
        }

        // Act - Trigger overflow
        await _buffer.EnqueueAsync(CreateTestMessage("overflow-msg"));

        // Assert
        Assert.True(eventRaised);
        Assert.NotNull(eventArgs);
        Assert.Equal("DropOldest", eventArgs.OverflowStrategy);
        Assert.Equal(1, eventArgs.DroppedMessageCount);
    }

    [Fact]
    public async Task CleanupAsync_ShouldCompleteSuccessfully()
    {
        // Arrange
        await _buffer.EnqueueAsync(CreateTestMessage("msg-1"));

        // Act & Assert - Should not throw
        await _buffer.CleanupAsync();
    }

    /// <summary>
    /// Creates a test SignalR message with specified ID or random ID.
    /// </summary>
    /// <param name="id">Message ID (optional, will generate random if not provided)</param>
    /// <returns>A SignalRMessage for testing</returns>
    private static SignalRMessage CreateTestMessage(string? id = null)
    {
        return new SignalRMessage
        {
            Id = id ?? Guid.NewGuid().ToString(),
            GroupName = "test-group",
            MethodName = "TestMethod",
            Payload = new { Message = "Test payload", Timestamp = DateTime.UtcNow },
            Timestamp = DateTime.UtcNow,
            Priority = MessagePriority.Normal
        };
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
