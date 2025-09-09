using System.ComponentModel.DataAnnotations;
using AIChat.Orleans.Contracts;

namespace AIChat.Orleans.Configuration;

/// <summary>
/// Configuration settings for Orleans grains.
/// </summary>
public class OrleansGrainConfiguration
{
    /// <summary>
    /// Configuration section name in appsettings.
    /// </summary>
    public const string SectionName = "OrleansGrains";

    /// <summary>
    /// Settings for UserGrain behavior.
    /// </summary>
    public UserGrainSettings UserGrain { get; set; } = new();

    /// <summary>
    /// Settings for connection management.
    /// </summary>
    public ConnectionSettings Connections { get; set; } = new();

    /// <summary>
    /// Settings for state persistence.
    /// </summary>
    public PersistenceSettings Persistence { get; set; } = new();

    /// <summary>
    /// Settings for streaming operations.
    /// </summary>
    public StreamingSettings Streaming { get; set; } = new();
}

/// <summary>
/// Configuration settings specific to UserGrain.
/// </summary>
public class UserGrainSettings
{
    /// <summary>
    /// Maximum number of activity records to maintain in memory.
    /// Default: 100
    /// </summary>
    [Range(10, 10000)]
    public int MaxActivityBufferSize { get; set; } = 100;

    /// <summary>
    /// Interval for cleanup timer in minutes.
    /// Default: 5 minutes
    /// </summary>
    [Range(1, 60)]
    public int CleanupIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Interval for metrics update timer in minutes.
    /// Default: 1 minute
    /// </summary>
    [Range(1, 10)]
    public int MetricsUpdateIntervalMinutes { get; set; } = 1;

    /// <summary>
    /// Time in hours to retain old activity records.
    /// Default: 1 hour
    /// </summary>
    [Range(0.5, 24)]
    public double ActivityRetentionHours { get; set; } = 1.0;

    /// <summary>
    /// Time in minutes to retain completed operations.
    /// Default: 30 minutes
    /// </summary>
    [Range(5, 120)]
    public int CompletedOperationRetentionMinutes { get; set; } = 30;

    /// <summary>
    /// Whether to enable periodic timers for cleanup and metrics.
    /// Default: true
    /// </summary>
    public bool EnablePeriodicTimers { get; set; } = true;

    /// <summary>
    /// Maximum number of messages to buffer per chat room.
    /// Default: 100
    /// </summary>
    [Range(10, 1000)]
    public int MessageBufferSizePerChat { get; set; } = 100;

    /// <summary>
    /// Time in minutes to retain buffered messages (TTL).
    /// Default: 60 minutes
    /// </summary>
    [Range(5, 1440)]
    public int MessageBufferTtlMinutes { get; set; } = 60;

    /// <summary>
    /// Interval in minutes for buffer cleanup operations.
    /// Default: 5 minutes
    /// </summary>
    [Range(1, 60)]
    public int BufferCleanupIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Strategy for handling buffer overflow conditions.
    /// Default: DropOldest
    /// </summary>
    public BufferOverflowStrategy BufferOverflowStrategy { get; set; } = BufferOverflowStrategy.DropOldest;

    /// <summary>
    /// Maximum delivery attempts per buffered message.
    /// Default: 3
    /// </summary>
    [Range(1, 10)]
    public int MaxBufferDeliveryAttempts { get; set; } = 3;
}

/// <summary>
/// Configuration settings for connection management.
/// </summary>
public class ConnectionSettings
{
    /// <summary>
    /// Time in minutes after which a connection is considered stale.
    /// Default: 30 minutes
    /// </summary>
    [Range(1, 120)]
    public int StaleConnectionThresholdMinutes { get; set; } = 30;

    /// <summary>
    /// Grace period in minutes for reconnection attempts.
    /// Default: 5 minutes
    /// </summary>
    [Range(1, 30)]
    public int ReconnectionGracePeriodMinutes { get; set; } = 5;

    /// <summary>
    /// Maximum number of connections per user.
    /// Default: 10
    /// </summary>
    [Range(1, 100)]
    public int MaxConnectionsPerUser { get; set; } = 10;
}

/// <summary>
/// Configuration settings for state persistence.
/// </summary>
public class PersistenceSettings
{
    /// <summary>
    /// Number of activities after which state is persisted.
    /// Default: 10
    /// </summary>
    [Range(1, 100)]
    public int ActivityPersistenceInterval { get; set; } = 10;

    /// <summary>
    /// Number of messages after which state is persisted.
    /// Default: 10
    /// </summary>
    [Range(1, 100)]
    public int MessagePersistenceInterval { get; set; } = 10;

    /// <summary>
    /// Number of stream chunks after which state is persisted for long streams.
    /// Default: 100
    /// </summary>
    [Range(10, 1000)]
    public int StreamChunkPersistenceInterval { get; set; } = 100;

    /// <summary>
    /// Whether to persist state on grain deactivation.
    /// Default: true
    /// </summary>
    public bool PersistOnDeactivation { get; set; } = true;

    /// <summary>
    /// Maximum retry attempts for state persistence failures.
    /// Default: 3
    /// </summary>
    [Range(1, 10)]
    public int MaxPersistenceRetries { get; set; } = 3;

    /// <summary>
    /// Delay in milliseconds between persistence retry attempts.
    /// Default: 100ms
    /// </summary>
    [Range(10, 5000)]
    public int PersistenceRetryDelayMilliseconds { get; set; } = 100;
}

/// <summary>
/// Configuration settings for streaming operations.
/// </summary>
public class StreamingSettings
{
    /// <summary>
    /// Maximum number of concurrent streams per user grain.
    /// Default: 3
    /// </summary>
    [Range(1, 10)]
    public int MaxConcurrentStreamsPerUser { get; set; } = 3;

    /// <summary>
    /// Timeout in minutes for idle streams.
    /// Default: 5 minutes
    /// </summary>
    [Range(1, 30)]
    public int StreamTimeoutMinutes { get; set; } = 5;

    /// <summary>
    /// Buffer size for stream channel.
    /// Default: 100
    /// </summary>
    [Range(10, 1000)]
    public int StreamChannelBufferSize { get; set; } = 100;

    /// <summary>
    /// Whether to persist partial stream state for recovery.
    /// Default: true
    /// </summary>
    public bool PersistPartialStreams { get; set; } = true;

    /// <summary>
    /// Interval in chunks to save partial stream state.
    /// Default: 50
    /// </summary>
    [Range(10, 500)]
    public int PartialStreamSaveInterval { get; set; } = 50;

    /// <summary>
    /// Maximum retry attempts for stream failures.
    /// Default: 3
    /// </summary>
    [Range(1, 10)]
    public int MaxStreamRetries { get; set; } = 3;

    /// <summary>
    /// Delay in milliseconds between stream retry attempts.
    /// Default: 1000ms
    /// </summary>
    [Range(100, 10000)]
    public int StreamRetryDelayMilliseconds { get; set; } = 1000;

    /// <summary>
    /// Whether to enable stream metrics collection.
    /// Default: true
    /// </summary>
    public bool EnableStreamMetrics { get; set; } = true;
}
