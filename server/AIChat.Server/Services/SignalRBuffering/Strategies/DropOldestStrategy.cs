using System.Diagnostics;
using AIChat.Server.Services.SignalRBuffering.Abstractions;
using AIChat.Server.Services.SignalRBuffering.Models;

namespace AIChat.Server.Services.SignalRBuffering.Strategies;

/// <summary>
/// Overflow strategy that removes the oldest messages from the buffer to make room for new ones.
/// This strategy prioritizes recent messages over older ones, which is suitable for real-time
/// applications where older messages become less relevant over time.
///
/// Characteristics:
/// - FIFO (First In, First Out) removal policy
/// - Always accepts new messages
/// - Preserves message ordering
/// - Low computational complexity (O(1) for single message removal)
/// - Best for time-sensitive real-time applications
/// </summary>
public class DropOldestStrategy : IBufferOverflowStrategy
{
    private readonly ILogger<DropOldestStrategy> _logger;

    /// <summary>
    /// Initializes a new instance of the DropOldestStrategy.
    /// </summary>
    /// <param name="logger">Logger for strategy operations</param>
    public DropOldestStrategy(ILogger<DropOldestStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string StrategyName => "DropOldest";

    /// <inheritdoc />
    public string Description =>
        "Removes the oldest messages from the buffer to make room for new messages. " +
        "Always accepts new messages, prioritizing recent data over historical data. " +
        "Ideal for real-time applications where message freshness is critical.";

    /// <inheritdoc />
    public bool CanHandle(int currentBufferSize, int maxCapacity, SignalRMessage? newMessage)
    {
        // This strategy can always handle overflow as long as there are messages to remove
        return currentBufferSize > 0;
    }

    /// <inheritdoc />
    public Task<OverflowResult> HandleOverflowAsync(
        IReadOnlyList<SignalRMessage> currentBuffer,
        SignalRMessage? newMessage,
        SignalRBufferConfiguration config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentBuffer);
        ArgumentNullException.ThrowIfNull(config);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // If buffer is empty, we can't remove anything
            if (currentBuffer.Count == 0)
            {
                _logger.LogWarning("DropOldest strategy called with empty buffer");
                return Task.FromResult(OverflowResult.Failed(
                    StrategyName,
                    "Cannot drop messages from empty buffer",
                    stopwatch.Elapsed));
            }

            // Calculate how many messages we need to remove
            var targetCapacity = config.MaxBufferSize;
            var currentSize = currentBuffer.Count;
            var spaceNeeded = newMessage != null ? 1 : 0;
            var messagesToRemove = Math.Max(0, (currentSize + spaceNeeded) - targetCapacity);

            if (messagesToRemove == 0)
            {
                // No overflow actually exists
                _logger.LogDebug("No overflow detected, buffer size: {CurrentSize}, capacity: {Capacity}",
                    currentSize, targetCapacity);

                stopwatch.Stop();
                return Task.FromResult(OverflowResult.CreateSuccess(
                    StrategyName,
                    [],
                    stopwatch.Elapsed,
                    new Dictionary<string, object>
                    {
                        ["ActualOverflow"] = false,
                        ["BufferSize"] = currentSize,
                        ["Capacity"] = targetCapacity
                    }));
            }

            // Remove the oldest messages (those at the beginning of the list)
            var messagesToRemoveList = currentBuffer.Take(messagesToRemove).ToList();

            // Log the removal operation
            _logger.LogInformation(
                "DropOldest strategy removing {MessageCount} oldest messages from buffer. " +
                "Buffer size: {CurrentSize}/{MaxCapacity}, New message: {HasNewMessage}",
                messagesToRemove, currentSize, targetCapacity, newMessage != null);

            // Log details about removed messages for debugging
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                foreach (var removedMessage in messagesToRemoveList)
                {
                    _logger.LogDebug(
                        "Dropping oldest message: ID={MessageId}, Method={MethodName}, " +
                        "Timestamp={Timestamp}, Age={Age:F2}s",
                        removedMessage.Id,
                        removedMessage.MethodName,
                        removedMessage.Timestamp,
                        (DateTime.UtcNow - removedMessage.Timestamp).TotalSeconds);
                }
            }

            stopwatch.Stop();

            return Task.FromResult(OverflowResult.CreateSuccess(
                StrategyName,
                messagesToRemoveList,
                stopwatch.Elapsed,
                new Dictionary<string, object>
                {
                    ["MessagesRemoved"] = messagesToRemove,
                    ["OldestMessageAge"] = messagesToRemoveList.Count > 0
                        ? (DateTime.UtcNow - messagesToRemoveList[0].Timestamp).TotalSeconds
                        : 0.0,
                    ["NewestRemovedMessageAge"] = messagesToRemoveList.Count > 0
                        ? (DateTime.UtcNow - messagesToRemoveList[^1].Timestamp).TotalSeconds
                        : 0.0,
                    ["BufferUtilization"] = (double)currentSize / targetCapacity * 100.0,
                    ["NewMessageAccepted"] = newMessage != null
                }));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex,
                "Error in DropOldest strategy while handling overflow. " +
                "Buffer size: {BufferSize}, New message: {HasNewMessage}",
                currentBuffer.Count, newMessage != null);

            return Task.FromResult(OverflowResult.Failed(
                StrategyName,
                $"Strategy execution failed: {ex.Message}",
                stopwatch.Elapsed,
                new Dictionary<string, object>
                {
                    ["ExceptionType"] = ex.GetType().Name,
                    ["BufferSize"] = currentBuffer.Count,
                    ["HasNewMessage"] = newMessage != null
                }));
        }
    }

    /// <inheritdoc />
    public Dictionary<string, object> GetStrategyMetadata()
    {
        return new Dictionary<string, object>
        {
            ["Name"] = StrategyName,
            ["Description"] = Description,
            ["RemovalPolicy"] = "FIFO",
            ["AcceptsNewMessages"] = true,
            ["PreservesOrdering"] = true,
            ["ComputationalComplexity"] = "O(n) where n is messages to remove",
            ["MemoryComplexity"] = "O(n) where n is messages to remove",
            ["BestFor"] = new[] { "Real-time applications", "Time-sensitive data", "Chat messages", "Notifications" },
            ["Characteristics"] = new[]
            {
                "Always accepts new messages",
                "Removes oldest messages first",
                "Maintains chronological order",
                "Low latency operation",
                "Suitable for streaming data"
            },
            ["ConfigurationOptions"] = new Dictionary<string, object>
            {
                ["RequiredSettings"] = Array.Empty<string>(),
                ["OptionalSettings"] = Array.Empty<string>(),
                ["DefaultBehavior"] = "Remove oldest messages until capacity allows new message"
            }
        };
    }
}