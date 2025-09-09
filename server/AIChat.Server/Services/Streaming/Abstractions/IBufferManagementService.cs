namespace AIChat.Server.Services.Streaming.Abstractions;

/// <summary>
/// Orchestrates buffer management, recovery, and replay operations for streaming.
/// </summary>
public interface IBufferManagementService
{
    /// <summary>
    /// Creates or gets a buffer for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="configuration">Buffer configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The stream buffer</returns>
    Task<IStreamBuffer> GetOrCreateBufferAsync(
        string streamId,
        BufferConfiguration? configuration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Buffers a message for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="message">The message to buffer</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if buffered successfully</returns>
    Task<bool> BufferMessageAsync(
        string streamId,
        BufferedStreamMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles disconnection for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="reason">Disconnection reason</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Disconnection handling result</returns>
    Task<DisconnectionResult> HandleDisconnectionAsync(
        string streamId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles reconnection for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="httpResponse">The HTTP response for replay</param>
    /// <param name="options">Reconnection options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Reconnection result</returns>
    Task<ReconnectionResult> HandleReconnectionAsync(
        string streamId,
        HttpResponse httpResponse,
        ReconnectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current status of a stream buffer.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <returns>Buffer status or null if not found</returns>
    Task<BufferStatus?> GetBufferStatusAsync(string streamId);

    /// <summary>
    /// Clears the buffer for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of messages cleared</returns>
    Task<int> ClearBufferAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a buffer replay for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="httpResponse">The HTTP response for replay</param>
    /// <param name="options">Replay options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Replay result</returns>
    Task<ReplayResult> ForceReplayAsync(
        string streamId,
        HttpResponse httpResponse,
        ReplayOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers buffers from persistent storage after service restart.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recovery result</returns>
    Task<ServiceRecoveryResult> RecoverFromPersistenceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics for all managed buffers.
    /// </summary>
    /// <returns>Overall buffer management statistics</returns>
    Task<BufferManagementStatistics> GetStatisticsAsync();

    /// <summary>
    /// Performs cleanup of expired buffers and persistence.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cleanup result</returns>
    Task<CleanupResult> PerformCleanupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Configures buffer settings for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="configuration">New configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if configured successfully</returns>
    Task<bool> ConfigureBufferAsync(
        string streamId,
        BufferConfiguration configuration,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of handling a disconnection.
/// </summary>
public record DisconnectionResult
{
    /// <summary>
    /// Gets whether the disconnection was handled successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the number of messages buffered.
    /// </summary>
    public required int MessagesBuffered { get; init; }

    /// <summary>
    /// Gets whether the buffer was persisted.
    /// </summary>
    public required bool BufferPersisted { get; init; }

    /// <summary>
    /// Gets the connection state after disconnection.
    /// </summary>
    public required ConnectionStatus ConnectionStatus { get; init; }

    /// <summary>
    /// Gets any error message.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Options for reconnection handling.
/// </summary>
public record ReconnectionOptions
{
    /// <summary>
    /// Gets whether to replay buffered messages.
    /// </summary>
    public bool ReplayBufferedMessages { get; init; } = true;

    /// <summary>
    /// Gets whether to load from persistence if available.
    /// </summary>
    public bool LoadFromPersistence { get; init; } = true;

    /// <summary>
    /// Gets the maximum age of messages to replay.
    /// </summary>
    public TimeSpan MaxMessageAge { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets custom replay options.
    /// </summary>
    public ReplayOptions? ReplayOptions { get; init; }
}

/// <summary>
/// Result of handling a reconnection.
/// </summary>
public record ReconnectionResult
{
    /// <summary>
    /// Gets whether the reconnection was handled successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the number of messages replayed.
    /// </summary>
    public required int MessagesReplayed { get; init; }

    /// <summary>
    /// Gets the number of messages loaded from persistence.
    /// </summary>
    public required int MessagesFromPersistence { get; init; }

    /// <summary>
    /// Gets the number of duplicates avoided.
    /// </summary>
    public required int DuplicatesAvoided { get; init; }

    /// <summary>
    /// Gets the connection state after reconnection.
    /// </summary>
    public required ConnectionStatus ConnectionStatus { get; init; }

    /// <summary>
    /// Gets the replay result if replay was performed.
    /// </summary>
    public ReplayResult? ReplayResult { get; init; }

    /// <summary>
    /// Gets any error message.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Status of a stream buffer.
/// </summary>
public record BufferStatus
{
    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets whether the buffer exists.
    /// </summary>
    public required bool Exists { get; init; }

    /// <summary>
    /// Gets the current message count.
    /// </summary>
    public required int MessageCount { get; init; }

    /// <summary>
    /// Gets the buffer size in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Gets the buffer configuration.
    /// </summary>
    public required BufferConfiguration Configuration { get; init; }

    /// <summary>
    /// Gets the connection state.
    /// </summary>
    public ConnectionState? ConnectionState { get; init; }

    /// <summary>
    /// Gets buffer statistics.
    /// </summary>
    public StreamBufferStatistics? Statistics { get; init; }

    /// <summary>
    /// Gets whether the buffer is persisted.
    /// </summary>
    public bool IsPersisted { get; init; }

    /// <summary>
    /// Gets the last activity timestamp.
    /// </summary>
    public DateTime? LastActivityAt { get; init; }
}

/// <summary>
/// Result of service recovery from persistence.
/// </summary>
public record ServiceRecoveryResult
{
    /// <summary>
    /// Gets whether recovery was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the number of buffers recovered.
    /// </summary>
    public required int BuffersRecovered { get; init; }

    /// <summary>
    /// Gets the total messages recovered.
    /// </summary>
    public required int TotalMessagesRecovered { get; init; }

    /// <summary>
    /// Gets the number of corrupted buffers.
    /// </summary>
    public required int CorruptedBuffers { get; init; }

    /// <summary>
    /// Gets the recovered stream identifiers.
    /// </summary>
    public required IReadOnlyList<string> RecoveredStreamIds { get; init; }

    /// <summary>
    /// Gets the recovery duration.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets any error messages.
    /// </summary>
    public List<string> Errors { get; init; } = [];
}

/// <summary>
/// Statistics for buffer management.
/// </summary>
public record BufferManagementStatistics
{
    /// <summary>
    /// Gets the total number of active buffers.
    /// </summary>
    public required int ActiveBuffers { get; init; }

    /// <summary>
    /// Gets the total messages across all buffers.
    /// </summary>
    public required long TotalMessages { get; init; }

    /// <summary>
    /// Gets the total size in bytes.
    /// </summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>
    /// Gets the number of connected streams.
    /// </summary>
    public required int ConnectedStreams { get; init; }

    /// <summary>
    /// Gets the number of disconnected streams.
    /// </summary>
    public required int DisconnectedStreams { get; init; }

    /// <summary>
    /// Gets the number of persisted buffers.
    /// </summary>
    public required int PersistedBuffers { get; init; }

    /// <summary>
    /// Gets the total replay operations.
    /// </summary>
    public required long TotalReplayOperations { get; init; }

    /// <summary>
    /// Gets the total messages replayed.
    /// </summary>
    public required long TotalMessagesReplayed { get; init; }

    /// <summary>
    /// Gets the total duplicates detected.
    /// </summary>
    public required long TotalDuplicatesDetected { get; init; }

    /// <summary>
    /// Gets persistence statistics if available.
    /// </summary>
    public PersistenceStatistics? PersistenceStatistics { get; init; }
}

/// <summary>
/// Result of cleanup operation.
/// </summary>
public record CleanupResult
{
    /// <summary>
    /// Gets whether cleanup was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the number of expired buffers removed.
    /// </summary>
    public required int ExpiredBuffersRemoved { get; init; }

    /// <summary>
    /// Gets the number of expired messages removed.
    /// </summary>
    public required int ExpiredMessagesRemoved { get; init; }

    /// <summary>
    /// Gets the number of persistence entries cleaned.
    /// </summary>
    public required int PersistenceEntriesCleaned { get; init; }

    /// <summary>
    /// Gets the space reclaimed in bytes.
    /// </summary>
    public required long SpaceReclaimedBytes { get; init; }

    /// <summary>
    /// Gets the cleanup duration.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets any error messages.
    /// </summary>
    public List<string> Errors { get; init; } = [];
}
