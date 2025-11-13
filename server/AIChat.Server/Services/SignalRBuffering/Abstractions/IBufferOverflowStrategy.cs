using AIChat.Server.Services.SignalRBuffering.Models;

namespace AIChat.Server.Services.SignalRBuffering.Abstractions;

/// <summary>
/// Defines a strategy for handling buffer overflow conditions.
/// Different strategies provide different trade-offs between data loss,
/// performance, and system stability when buffer capacity is exceeded.
/// </summary>
public interface IBufferOverflowStrategy
{
    /// <summary>
    /// Gets the name of this overflow strategy.
    /// Used for configuration, logging, and metrics.
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// Gets a description of how this strategy handles overflow conditions.
    /// Useful for documentation and monitoring purposes.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Handles a buffer overflow condition when attempting to add a new message.
    /// The strategy determines which messages (if any) should be removed to make room,
    /// and whether the new message should be accepted or rejected.
    /// </summary>
    /// <param name="currentBuffer">
    /// A read-only view of the current buffer contents.
    /// Strategies can examine message priorities, timestamps, and metadata to make decisions.
    /// </param>
    /// <param name="newMessage">
    /// The new message that triggered the overflow condition.
    /// May be null if the overflow was detected during regular processing.
    /// </param>
    /// <param name="config">
    /// The current buffer configuration, including capacity limits and strategy settings.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>
    /// An OverflowResult describing the actions taken to handle the overflow,
    /// including which messages were removed and whether the new message was accepted.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
    /// <exception cref="InvalidOperationException">Thrown when strategy cannot handle the overflow</exception>
    Task<OverflowResult> HandleOverflowAsync(
        IReadOnlyList<SignalRMessage> currentBuffer,
        SignalRMessage? newMessage,
        SignalRBufferConfiguration config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if this strategy can handle the given overflow condition.
    /// Some strategies may not be applicable in certain scenarios (e.g., when buffer is empty).
    /// </summary>
    /// <param name="currentBufferSize">The current number of messages in the buffer</param>
    /// <param name="maxCapacity">The maximum buffer capacity</param>
    /// <param name="newMessage">The message that would trigger overflow (may be null)</param>
    /// <returns>True if this strategy can handle the condition, false otherwise</returns>
    bool CanHandle(int currentBufferSize, int maxCapacity, SignalRMessage? newMessage);

    /// <summary>
    /// Gets metadata about this strategy's behavior and configuration.
    /// Used for monitoring, analytics, and strategy selection.
    /// </summary>
    /// <returns>A dictionary containing strategy metadata</returns>
    Dictionary<string, object> GetStrategyMetadata();
}

/// <summary>
/// Represents the result of an overflow handling operation.
/// Provides detailed information about actions taken and their outcomes.
/// </summary>
public record OverflowResult
{
    /// <summary>
    /// Gets whether the overflow handling was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets whether the new message was accepted into the buffer.
    /// </summary>
    public bool NewMessageAccepted { get; init; }

    /// <summary>
    /// Gets the list of messages that were removed from the buffer.
    /// Empty if no messages were removed.
    /// </summary>
    public IReadOnlyList<SignalRMessage> RemovedMessages { get; init; } = [];

    /// <summary>
    /// Gets the number of messages removed to handle the overflow.
    /// </summary>
    public int RemovedCount => RemovedMessages.Count;

    /// <summary>
    /// Gets the strategy that was used to handle the overflow.
    /// </summary>
    public string StrategyUsed { get; init; } = string.Empty;

    /// <summary>
    /// Gets any error message if the overflow handling failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the time taken to handle the overflow.
    /// Used for performance monitoring and optimization.
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// Gets additional metadata about the overflow handling operation.
    /// Used for detailed analytics and troubleshooting.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Creates a successful overflow result where the new message was accepted.
    /// </summary>
    /// <param name="strategyUsed">The strategy that handled the overflow</param>
    /// <param name="removedMessages">Messages that were removed to make room</param>
    /// <param name="processingTime">Time taken to handle the overflow</param>
    /// <param name="metadata">Additional metadata</param>
    /// <returns>A successful OverflowResult with new message accepted</returns>
    public static OverflowResult CreateSuccess(
        string strategyUsed,
        IReadOnlyList<SignalRMessage> removedMessages,
        TimeSpan processingTime,
        Dictionary<string, object>? metadata = null)
        => new()
        {
            Success = true,
            NewMessageAccepted = true,
            RemovedMessages = removedMessages,
            StrategyUsed = strategyUsed,
            ProcessingTime = processingTime,
            Metadata = metadata ?? []
        };

    /// <summary>
    /// Creates a successful overflow result where the new message was rejected.
    /// </summary>
    /// <param name="strategyUsed">The strategy that handled the overflow</param>
    /// <param name="processingTime">Time taken to handle the overflow</param>
    /// <param name="metadata">Additional metadata</param>
    /// <returns>A successful OverflowResult with new message rejected</returns>
    public static OverflowResult Rejected(
        string strategyUsed,
        TimeSpan processingTime,
        Dictionary<string, object>? metadata = null)
        => new()
        {
            Success = true,
            NewMessageAccepted = false,
            RemovedMessages = [],
            StrategyUsed = strategyUsed,
            ProcessingTime = processingTime,
            Metadata = metadata ?? []
        };

    /// <summary>
    /// Creates a failed overflow result.
    /// </summary>
    /// <param name="strategyUsed">The strategy that attempted to handle the overflow</param>
    /// <param name="errorMessage">The error that occurred</param>
    /// <param name="processingTime">Time taken before the error occurred</param>
    /// <param name="metadata">Additional metadata</param>
    /// <returns>A failed OverflowResult</returns>
    public static OverflowResult Failed(
        string strategyUsed,
        string errorMessage,
        TimeSpan processingTime,
        Dictionary<string, object>? metadata = null)
        => new()
        {
            Success = false,
            NewMessageAccepted = false,
            RemovedMessages = [],
            StrategyUsed = strategyUsed,
            ErrorMessage = errorMessage,
            ProcessingTime = processingTime,
            Metadata = metadata ?? []
        };
}
