using System.Collections.Concurrent;
using AIChat.Server.Configuration;
using AIChat.Server.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Manages stream recovery with automatic reconnection and message replay.
/// </summary>
public sealed class StreamRecoveryManager : IStreamRecoveryManager
{
    private readonly ILogger<StreamRecoveryManager> _logger;
    private readonly RecoveryConfiguration _configuration;
    private readonly IMessageReplayService _replayService;
    private readonly ISystemTime _systemTime;
    private readonly ConcurrentDictionary<string, StreamRecoveryState> _recoveryStates;
    private readonly Services.Abstractions.ITimer _cleanupTimer;
    private readonly Lock _statsLock = new();

    private long _totalRecoveryAttempts;
    private long _successfulRecoveries;
    private long _failedRecoveries;
    private long _messagesReplayed;
    private bool _disposed;

    /// <summary>
    /// Event raised when recovery is initiated.
    /// </summary>
    public event EventHandler<RecoveryEventArgs>? RecoveryStarted;

    /// <summary>
    /// Event raised when recovery is completed.
    /// </summary>
    public event EventHandler<RecoveryEventArgs>? RecoveryCompleted;

    /// <summary>
    /// Event raised when recovery fails.
    /// </summary>
    public event EventHandler<RecoveryEventArgs>? RecoveryFailed;

    /// <summary>
    /// Initializes a new instance of the StreamRecoveryManager class.
    /// </summary>
    /// <param name="logger">Logger for diagnostics</param>
    /// <param name="configuration">Streaming configuration</param>
    /// <param name="replayService">Message replay service</param>
    /// <param name="systemTime">System time abstraction</param>
    /// <param name="timerFactory">Timer factory for creating timers</param>
    public StreamRecoveryManager(
        ILogger<StreamRecoveryManager> logger,
        IOptions<StreamingConfiguration> configuration,
        IMessageReplayService replayService,
        ISystemTime systemTime,
        ITimerFactory timerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value?.Recovery
            ?? throw new ArgumentNullException(nameof(configuration));
        _replayService = replayService ?? throw new ArgumentNullException(nameof(replayService));
        _systemTime = systemTime ?? throw new ArgumentNullException(nameof(systemTime));
        ArgumentNullException.ThrowIfNull(timerFactory);

        _recoveryStates = new ConcurrentDictionary<string, StreamRecoveryState>();

        // Start cleanup timer to remove old messages
        var cleanupInterval = TimeSpan.FromSeconds(
            Math.Max(
                StreamingConstants.Recovery.MinCleanupIntervalSeconds,
                _configuration.ReplayMaxAgeSeconds / StreamingConstants.Recovery.CleanupIntervalDivisor));

        _cleanupTimer = timerFactory.CreateTimer(CleanupOldMessages, null);
        _cleanupTimer.Start(cleanupInterval, cleanupInterval);

        _logger.LogInformation(
            "StreamRecoveryManager initialized. Max attempts: {MaxAttempts}, Replay buffer: {BufferSize}",
            _configuration.MaxReconnectAttempts,
            _configuration.ReplayBufferSize);
    }

    /// <summary>
    /// Initiates recovery for a stream.
    /// </summary>
    /// <param name="streamId">Stream identifier</param>
    /// <param name="reconnectFunc">Function to attempt reconnection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if recovery succeeded, false otherwise</returns>
    public async Task<bool> RecoverStreamAsync(
        string streamId,
        Func<CancellationToken, Task<bool>> reconnectFunc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(reconnectFunc);

        if (!_configuration.EnableAutoReconnect)
        {
            _logger.LogWarning("Auto-reconnect is disabled for stream {StreamId}", streamId);
            return false;
        }

        var state = _recoveryStates.GetOrAdd(streamId, _ => new StreamRecoveryState());
        state.LastAttemptTime = _systemTime.UtcNow;

        lock (_statsLock)
        {
            _totalRecoveryAttempts++;
        }

        RecoveryStarted?.Invoke(this, new RecoveryEventArgs
        {
            StreamId = streamId,
            Attempt = state.AttemptCount + 1,
            MaxAttempts = _configuration.MaxReconnectAttempts
        });

        _logger.LogInformation("Starting recovery for stream {StreamId}", streamId);

        var delay = _configuration.ReconnectDelayMs;
        var attempt = 0;

        while (attempt < _configuration.MaxReconnectAttempts && !cancellationToken.IsCancellationRequested)
        {
            attempt++;
            state.AttemptCount = attempt;

            try
            {
                _logger.LogInformation(
                    "Recovery attempt {Attempt}/{Max} for stream {StreamId}",
                    attempt,
                    _configuration.MaxReconnectAttempts,
                    streamId);

                // Wait with exponential backoff
                if (attempt > 1)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                // Attempt reconnection
                using var cts = new CancellationTokenSource(_configuration.RecoveryTimeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    cts.Token);

                if (await reconnectFunc(linkedCts.Token))
                {
                    // Success - replay messages if enabled
                    if (_configuration.EnableMessageReplay)
                    {
                        await ReplayMessagesAsync(streamId, cancellationToken);
                    }

                    state.IsRecovered = true;
                    state.RecoveryTime = _systemTime.UtcNow;

                    lock (_statsLock)
                    {
                        _successfulRecoveries++;
                    }

                    RecoveryCompleted?.Invoke(this, new RecoveryEventArgs
                    {
                        StreamId = streamId,
                        Attempt = attempt,
                        MaxAttempts = _configuration.MaxReconnectAttempts,
                        Success = true
                    });

                    _logger.LogInformation(
                        "Successfully recovered stream {StreamId} after {Attempts} attempts",
                        streamId,
                        attempt);

                    return true;
                }

                // Calculate next delay with exponential backoff
                delay = (int)Math.Min(
                    delay * _configuration.BackoffMultiplier,
                    _configuration.MaxReconnectDelayMs);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Recovery cancelled for stream {StreamId}", streamId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Recovery attempt {Attempt} failed for stream {StreamId}",
                    attempt,
                    streamId);
            }
        }

        lock (_statsLock)
        {
            _failedRecoveries++;
        }

        RecoveryFailed?.Invoke(this, new RecoveryEventArgs
        {
            StreamId = streamId,
            Attempt = attempt,
            MaxAttempts = _configuration.MaxReconnectAttempts,
            Success = false
        });

        _logger.LogError(
            "Failed to recover stream {StreamId} after {Attempts} attempts",
            streamId,
            _configuration.MaxReconnectAttempts);

        return false;
    }

    /// <inheritdoc />
    public void AddToReplayBuffer(string streamId, string message, long? sequenceNumber = null)
    {
        if (!_configuration.EnableMessageReplay || _disposed)
        {
            return;
        }

        var replayMessage = new ReplayMessage
        {
            StreamId = streamId,
            Message = message,
            Timestamp = _systemTime.UtcNow,
            SequenceNumber = sequenceNumber ?? 0
        };

        _replayService.AddMessage(replayMessage);
    }

    /// <inheritdoc />
    public void CreateCheckpoint(string streamId, long sequenceNumber)
    {
        if (!_configuration.EnableCheckpoints || _disposed)
        {
            return;
        }

        if (_recoveryStates.TryGetValue(streamId, out var state))
        {
            state.LastCheckpoint = sequenceNumber;
            state.CheckpointTime = _systemTime.UtcNow;

            _logger.LogDebug(
                "Created checkpoint for stream {StreamId} at sequence {Sequence}",
                streamId,
                sequenceNumber);
        }
    }

    /// <inheritdoc />
    public RecoveryStatistics GetStatistics()
    {
        lock (_statsLock)
        {
            var activeRecoveries = _recoveryStates.Count(s => !s.Value.IsRecovered);
            var successRate = _totalRecoveryAttempts > 0
                ? (double)_successfulRecoveries / _totalRecoveryAttempts * 100
                : 0;

            return new RecoveryStatistics
            {
                TotalAttempts = _totalRecoveryAttempts,
                SuccessfulRecoveries = _successfulRecoveries,
                FailedRecoveries = _failedRecoveries,
                MessagesReplayed = _messagesReplayed,
                ActiveRecoveries = activeRecoveries,
                ReplayBufferSize = _replayService.BufferSize,
                SuccessRate = successRate
            };
        }
    }

    private async Task ReplayMessagesAsync(string streamId, CancellationToken cancellationToken)
    {
        var messages = _replayService.GetMessagesForReplay(streamId, _configuration.ReplayMaxAgeSeconds);
        var messageList = messages.ToList();

        if (messageList.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Replaying {Count} messages for stream {StreamId}",
            messageList.Count,
            streamId);

        foreach (var message in messageList)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // In a real implementation, we would send these messages through the stream
            // For now, we just track the replay
            lock (_statsLock)
            {
                _messagesReplayed++;
            }

            await Task.Delay(StreamingConstants.Recovery.ReplayMessageDelayMs, cancellationToken);
        }
    }

    private void CleanupOldMessages(object? state)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            // Clean up old messages from replay service
            var itemsRemoved = _replayService.CleanupOldMessages(_configuration.ReplayMaxAgeSeconds);

            // Clean up old recovery states
            var staleStates = _recoveryStates
                .Where(kvp => kvp.Value.IsRecovered &&
                             kvp.Value.RecoveryTime < _systemTime.UtcNow.AddHours(-StreamingConstants.Recovery.RecoveredStateRetentionHours))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleStates)
            {
                _ = _recoveryStates.TryRemove(key, out _);
            }

            if (itemsRemoved > 0 || staleStates.Count > 0)
            {
                _logger.LogDebug(
                    "Cleanup completed. Removed {Messages} old messages and {States} stale states",
                    itemsRemoved,
                    staleStates.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cleanupTimer?.Dispose();
        _recoveryStates.Clear();

        var stats = GetStatistics();
        _logger.LogInformation(
            "StreamRecoveryManager disposed. Total attempts: {Total}, Success rate: {Rate:F1}%",
            stats.TotalAttempts,
            stats.SuccessRate);
    }
}

/// <summary>
/// Internal state for stream recovery tracking.
/// </summary>
internal sealed class StreamRecoveryState
{
    public int AttemptCount { get; set; }
    public DateTime LastAttemptTime { get; set; }
    public bool IsRecovered { get; set; }
    public DateTime RecoveryTime { get; set; }
    public long LastCheckpoint { get; set; }
    public DateTime CheckpointTime { get; set; }
}

/// <summary>
/// Event arguments for recovery events.
/// </summary>
public sealed class RecoveryEventArgs : EventArgs
{
    public required string StreamId { get; init; }
    public int Attempt { get; init; }
    public int MaxAttempts { get; init; }
    public bool Success { get; init; }
}

/// <summary>
/// Statistics for stream recovery operations.
/// </summary>
public sealed class RecoveryStatistics
{
    public long TotalAttempts { get; init; }
    public long SuccessfulRecoveries { get; init; }
    public long FailedRecoveries { get; init; }
    public long MessagesReplayed { get; init; }
    public int ActiveRecoveries { get; init; }
    public int ReplayBufferSize { get; init; }
    public double SuccessRate { get; init; }
}