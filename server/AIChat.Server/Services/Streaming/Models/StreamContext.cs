using System.Threading.Channels;
using AIChat.Server.Services.Streaming.Abstractions;

namespace AIChat.Server.Services.Streaming.Models;

/// <summary>
/// Thread-safe context for managing stream state and metrics.
/// Uses immutable properties and thread-safe collections.
/// </summary>
public sealed class StreamContext
{
    private readonly Lock _lock = new();
    private StreamState _state;
    private CircuitState _circuitState;
    private long _messagesProcessed;
    private int _reconnectionAttempts;
    private int _failureCount;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private string? _lastError;
    private DateTime _lastActivityTime;
    private double _totalProcessingTimeMs;

    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    public string StreamId { get; }

    /// <summary>
    /// Gets the HTTP response associated with the stream.
    /// </summary>
    public HttpResponse HttpResponse { get; }

    /// <summary>
    /// Gets the message buffer channel.
    /// </summary>
    public Channel<BufferedMessage<string>> MessageBuffer { get; }

    /// <summary>
    /// Gets the partial messages collection.
    /// </summary>
    public IPartialMessageStore PartialMessages { get; }

    /// <summary>
    /// Gets the stream start time.
    /// </summary>
    public DateTime StartTime { get; }

    /// <summary>
    /// Gets or sets the stream state in a thread-safe manner.
    /// </summary>
    public StreamState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
        set
        {
            lock (_lock)
            {
                _state = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the circuit breaker state in a thread-safe manner.
    /// </summary>
    public CircuitState CircuitState
    {
        get
        {
            lock (_lock)
            {
                return _circuitState;
            }
        }
        set
        {
            lock (_lock)
            {
                _circuitState = value;
            }
        }
    }

    /// <summary>
    /// Gets the number of messages processed.
    /// </summary>
    public long MessagesProcessed => Interlocked.Read(ref _messagesProcessed);

    /// <summary>
    /// Increments the messages processed count.
    /// </summary>
    public void IncrementMessagesProcessed()
    {
        _ = Interlocked.Increment(ref _messagesProcessed);
    }

    /// <summary>
    /// Gets the number of reconnection attempts.
    /// </summary>
    public int ReconnectionAttempts => Interlocked.CompareExchange(ref _reconnectionAttempts, 0, 0);

    /// <summary>
    /// Increments the reconnection attempts.
    /// </summary>
    public void IncrementReconnectionAttempts()
    {
        _ = Interlocked.Increment(ref _reconnectionAttempts);
    }

    /// <summary>
    /// Gets the failure count.
    /// </summary>
    public int FailureCount => Interlocked.CompareExchange(ref _failureCount, 0, 0);

    /// <summary>
    /// Increments the failure count.
    /// </summary>
    public void IncrementFailureCount()
    {
        _ = Interlocked.Increment(ref _failureCount);
    }

    /// <summary>
    /// Gets or sets the consecutive failures in a thread-safe manner.
    /// </summary>
    public int ConsecutiveFailures
    {
        get
        {
            lock (_lock)
            {
                return _consecutiveFailures;
            }
        }
        set
        {
            lock (_lock)
            {
                _consecutiveFailures = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the consecutive successes in a thread-safe manner.
    /// </summary>
    public int ConsecutiveSuccesses
    {
        get
        {
            lock (_lock)
            {
                return _consecutiveSuccesses;
            }
        }
        set
        {
            lock (_lock)
            {
                _consecutiveSuccesses = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the last error in a thread-safe manner.
    /// </summary>
    public string? LastError
    {
        get
        {
            lock (_lock)
            {
                return _lastError;
            }
        }
        set
        {
            lock (_lock)
            {
                _lastError = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the last activity time in a thread-safe manner.
    /// </summary>
    public DateTime LastActivityTime
    {
        get
        {
            lock (_lock)
            {
                return _lastActivityTime;
            }
        }
        set
        {
            lock (_lock)
            {
                _lastActivityTime = value;
            }
        }
    }

    /// <summary>
    /// Gets the total processing time in milliseconds.
    /// </summary>
    public double TotalProcessingTimeMs
    {
        get
        {
            lock (_lock)
            {
                return _totalProcessingTimeMs;
            }
        }
    }

    /// <summary>
    /// Adds to the total processing time.
    /// </summary>
    /// <param name="milliseconds">Milliseconds to add</param>
    public void AddProcessingTime(double milliseconds)
    {
        lock (_lock)
        {
            _totalProcessingTimeMs += milliseconds;
        }
    }

    /// <summary>
    /// Initializes a new instance of the StreamContext class.
    /// </summary>
    public StreamContext(
        string streamId,
        HttpResponse httpResponse,
        Channel<BufferedMessage<string>> messageBuffer,
        IPartialMessageStore partialMessages,
        ISystemClock systemClock)
    {
        StreamId = streamId ?? throw new ArgumentNullException(nameof(streamId));
        HttpResponse = httpResponse ?? throw new ArgumentNullException(nameof(httpResponse));
        MessageBuffer = messageBuffer ?? throw new ArgumentNullException(nameof(messageBuffer));
        PartialMessages = partialMessages ?? throw new ArgumentNullException(nameof(partialMessages));

        StartTime = systemClock.UtcNow;
        _lastActivityTime = StartTime;
        _state = StreamState.Active;
        _circuitState = CircuitState.Closed;
    }

    /// <summary>
    /// Creates a snapshot of the current metrics.
    /// </summary>
    /// <returns>A metrics snapshot</returns>
    public StreamMetrics CreateMetricsSnapshot()
    {
        lock (_lock)
        {
            return new StreamMetrics
            {
                StreamId = StreamId,
                State = _state,
                CircuitState = _circuitState,
                MessagesProcessed = Interlocked.Read(ref _messagesProcessed),
                BufferedMessages = MessageBuffer.Reader.Count,
                ReconnectionAttempts = ReconnectionAttempts,
                FailureCount = FailureCount,
                LastError = _lastError,
                StartTime = StartTime,
                LastActivityTime = _lastActivityTime,
                TotalProcessingTimeMs = _totalProcessingTimeMs,
                PartialRecovery = PartialMessages.GetStatistics()
            };
        }
    }
}

/// <summary>
/// Interface for storing partial messages.
/// </summary>
public interface IPartialMessageStore
{
    /// <summary>
    /// Stores a partial message.
    /// </summary>
    void Store(int sequenceNumber, PartialMessage message);

    /// <summary>
    /// Retrieves a partial message.
    /// </summary>
    PartialMessage? Retrieve(int sequenceNumber);

    /// <summary>
    /// Removes a partial message.
    /// </summary>
    bool Remove(int sequenceNumber);

    /// <summary>
    /// Gets all partial messages ordered by sequence.
    /// </summary>
    IReadOnlyList<PartialMessage> GetAll();

    /// <summary>
    /// Clears all partial messages.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets statistics about stored partial messages.
    /// </summary>
    PartialRecoveryStats? GetStatistics();
}

/// <summary>
/// Represents a partial message for recovery.
/// </summary>
public record PartialMessage
{
    /// <summary>
    /// Gets the sequence number.
    /// </summary>
    public required int SequenceNumber { get; init; }

    /// <summary>
    /// Gets the message data.
    /// </summary>
    public required string Data { get; init; }

    /// <summary>
    /// Gets the timestamp.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Gets the size in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }
}
