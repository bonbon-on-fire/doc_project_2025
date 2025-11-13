using AIChat.Server.Services.SignalRBuffering.Models;

namespace AIChat.Server.Services.SignalRBuffering.Abstractions;

/// <summary>
/// <para>
/// Abstraction for SignalR message buffering operations.
/// Provides thread-safe, high-performance message queuing with overflow handling,
/// delivery confirmation, and comprehensive metrics collection.
/// </para>
/// <para>
/// This interface supports various buffering strategies and can be implemented
/// with different backing stores (in-memory, persistent, distributed).
/// </para>
/// </summary>
public interface ISignalRMessageBuffer
{
    /// <summary>
    /// Enqueues a message to the buffer for later delivery.
    /// The operation is thread-safe and supports overflow handling based on configured strategy.
    /// </summary>
    /// <param name="message">The SignalR message to enqueue</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>
    /// A BufferResult indicating whether the message was successfully enqueued,
    /// or if overflow handling was applied
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when buffer is disabled or unhealthy</exception>
    Task<BufferResult> EnqueueAsync(SignalRMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes buffered messages in batches and delivers them via the provided delivery service.
    /// This method is typically called by a background service at regular intervals.
    /// </summary>
    /// <param name="deliveryService">Service to handle actual message delivery</param>
    /// <param name="cancellationToken">Token to cancel the processing operation</param>
    /// <returns>
    /// A DeliveryResult containing information about the batch processing outcome,
    /// including success/failure counts and any errors encountered
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when deliveryService is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when buffer is disabled</exception>
    Task<DeliveryResult> ProcessBufferAsync(ISignalRDeliveryService deliveryService, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets comprehensive real-time metrics about buffer performance and health.
    /// Metrics include buffer utilization, throughput, success rates, and timing information.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current buffer metrics and performance data</returns>
    Task<BufferMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current health status of the buffer.
    /// Health checks include buffer capacity, error rates, connectivity, and operational status.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current buffer health information</returns>
    Task<BufferHealth> GetHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current number of messages in the buffer.
    /// This is a lightweight operation for quick buffer size checks.
    /// </summary>
    /// <returns>The number of messages currently queued in the buffer</returns>
    int GetCurrentSize();

    /// <summary>
    /// Gets the maximum capacity of the buffer.
    /// This value is typically configured at startup and doesn't change during runtime.
    /// </summary>
    /// <returns>The maximum number of messages the buffer can hold</returns>
    int GetMaxCapacity();

    /// <summary>
    /// Clears all messages from the buffer.
    /// This is typically used for emergency situations or when resetting the buffer state.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The number of messages that were cleared from the buffer</returns>
    Task<int> ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs cleanup operations such as removing expired delivery tracking data
    /// and optimizing internal data structures for performance.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the cleanup operation</returns>
    Task CleanupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a message is successfully enqueued to the buffer.
    /// Useful for monitoring and analytics purposes.
    /// </summary>
    event EventHandler<MessageEnqueuedEventArgs>? MessageEnqueued;

    /// <summary>
    /// Event raised when a message is successfully delivered to SignalR clients.
    /// Includes delivery timing and retry information.
    /// </summary>
    event EventHandler<MessageDeliveredEventArgs>? MessageDelivered;

    /// <summary>
    /// Event raised when a message delivery fails after all retry attempts.
    /// Includes failure reason and diagnostic information.
    /// </summary>
    event EventHandler<MessageFailedEventArgs>? MessageFailed;

    /// <summary>
    /// Event raised when buffer overflow occurs and messages are dropped.
    /// Includes information about the overflow strategy applied and messages affected.
    /// </summary>
    event EventHandler<BufferOverflowEventArgs>? BufferOverflow;

    /// <summary>
    /// Event raised when buffer health status changes.
    /// Useful for alerting and monitoring systems.
    /// </summary>
    event EventHandler<BufferHealthChangedEventArgs>? HealthStatusChanged;
}

/// <summary>
/// Event arguments for message enqueued events.
/// </summary>
public class MessageEnqueuedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the message that was enqueued.
    /// </summary>
    public SignalRMessage Message { get; }

    /// <summary>
    /// Gets the buffer size after the message was enqueued.
    /// </summary>
    public int BufferSize { get; }

    /// <summary>
    /// Gets the timestamp when the message was enqueued.
    /// </summary>
    public DateTime EnqueuedAt { get; }

    /// <summary>
    /// Initializes a new instance of the MessageEnqueuedEventArgs class.
    /// </summary>
    /// <param name="message">The enqueued message</param>
    /// <param name="bufferSize">The buffer size after enqueuing</param>
    public MessageEnqueuedEventArgs(SignalRMessage message, int bufferSize)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        BufferSize = bufferSize;
        EnqueuedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Event arguments for message delivered events.
/// </summary>
public class MessageDeliveredEventArgs : EventArgs
{
    /// <summary>
    /// Gets the message that was delivered.
    /// </summary>
    public SignalRMessage Message { get; }

    /// <summary>
    /// Gets the delivery result information.
    /// </summary>
    public DeliveryResult DeliveryResult { get; }

    /// <summary>
    /// Gets the time taken for delivery.
    /// </summary>
    public TimeSpan DeliveryTime { get; }

    /// <summary>
    /// Initializes a new instance of the MessageDeliveredEventArgs class.
    /// </summary>
    /// <param name="message">The delivered message</param>
    /// <param name="deliveryResult">The delivery result</param>
    /// <param name="deliveryTime">The time taken for delivery</param>
    public MessageDeliveredEventArgs(SignalRMessage message, DeliveryResult deliveryResult, TimeSpan deliveryTime)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        DeliveryResult = deliveryResult ?? throw new ArgumentNullException(nameof(deliveryResult));
        DeliveryTime = deliveryTime;
    }
}

/// <summary>
/// Event arguments for message failed events.
/// </summary>
public class MessageFailedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the message that failed delivery.
    /// </summary>
    public SignalRMessage Message { get; }

    /// <summary>
    /// Gets the failure reason.
    /// </summary>
    public string FailureReason { get; }

    /// <summary>
    /// Gets the number of retry attempts made.
    /// </summary>
    public int RetryAttempts { get; }

    /// <summary>
    /// Gets whether the message was moved to the dead letter queue.
    /// </summary>
    public bool MovedToDeadLetterQueue { get; }

    /// <summary>
    /// Initializes a new instance of the MessageFailedEventArgs class.
    /// </summary>
    /// <param name="message">The failed message</param>
    /// <param name="failureReason">The reason for failure</param>
    /// <param name="retryAttempts">The number of retry attempts</param>
    /// <param name="movedToDeadLetterQueue">Whether moved to dead letter queue</param>
    public MessageFailedEventArgs(SignalRMessage message, string failureReason, int retryAttempts, bool movedToDeadLetterQueue)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        FailureReason = failureReason ?? throw new ArgumentNullException(nameof(failureReason));
        RetryAttempts = retryAttempts;
        MovedToDeadLetterQueue = movedToDeadLetterQueue;
    }
}

/// <summary>
/// Event arguments for buffer overflow events.
/// </summary>
public class BufferOverflowEventArgs : EventArgs
{
    /// <summary>
    /// Gets the strategy used to handle the overflow.
    /// </summary>
    public string OverflowStrategy { get; }

    /// <summary>
    /// Gets the number of messages dropped due to overflow.
    /// </summary>
    public int DroppedMessageCount { get; }

    /// <summary>
    /// Gets the current buffer size after overflow handling.
    /// </summary>
    public int CurrentBufferSize { get; }

    /// <summary>
    /// Gets the message that triggered the overflow (if applicable).
    /// </summary>
    public SignalRMessage? TriggeringMessage { get; }

    /// <summary>
    /// Initializes a new instance of the BufferOverflowEventArgs class.
    /// </summary>
    /// <param name="overflowStrategy">The overflow strategy used</param>
    /// <param name="droppedMessageCount">Number of messages dropped</param>
    /// <param name="currentBufferSize">Current buffer size</param>
    /// <param name="triggeringMessage">The message that triggered overflow</param>
    public BufferOverflowEventArgs(string overflowStrategy, int droppedMessageCount, int currentBufferSize, SignalRMessage? triggeringMessage = null)
    {
        OverflowStrategy = overflowStrategy ?? throw new ArgumentNullException(nameof(overflowStrategy));
        DroppedMessageCount = droppedMessageCount;
        CurrentBufferSize = currentBufferSize;
        TriggeringMessage = triggeringMessage;
    }
}

/// <summary>
/// Event arguments for buffer health status change events.
/// </summary>
public class BufferHealthChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the previous health status.
    /// </summary>
    public BufferHealthStatus PreviousStatus { get; }

    /// <summary>
    /// Gets the current health status.
    /// </summary>
    public BufferHealthStatus CurrentStatus { get; }

    /// <summary>
    /// Gets the health check details.
    /// </summary>
    public BufferHealth HealthDetails { get; }

    /// <summary>
    /// Initializes a new instance of the BufferHealthChangedEventArgs class.
    /// </summary>
    /// <param name="previousStatus">The previous health status</param>
    /// <param name="currentStatus">The current health status</param>
    /// <param name="healthDetails">The health details</param>
    public BufferHealthChangedEventArgs(BufferHealthStatus previousStatus, BufferHealthStatus currentStatus, BufferHealth healthDetails)
    {
        PreviousStatus = previousStatus;
        CurrentStatus = currentStatus;
        HealthDetails = healthDetails ?? throw new ArgumentNullException(nameof(healthDetails));
    }
}
