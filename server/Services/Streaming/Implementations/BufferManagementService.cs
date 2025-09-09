using System.Collections.Concurrent;
using AIChat.Server.Services.Streaming.Abstractions;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services.Streaming.Implementations;

/// <summary>
/// Orchestrates buffer management, recovery, and replay operations for streaming.
/// Implements as a hosted service for lifecycle management.
/// </summary>
public sealed class BufferManagementService : IBufferManagementService, IHostedService, IDisposable
{
    private readonly ILogger<BufferManagementService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConnectionStateTracker _connectionTracker;
    private readonly IBufferReplayService _replayService;
    private readonly IPersistentBufferStore _persistentStore;
    private readonly BufferManagementOptions _options;
    private readonly ConcurrentDictionary<string, IStreamBuffer> _buffers;
    private readonly ConcurrentDictionary<string, BufferConfiguration> _configurations;
    private readonly SemaphoreSlim _recoveryLock;
    private Timer? _cleanupTimer;
    private bool _disposed;

    // Metrics
    private long _totalReplayOperations;
    private long _totalMessagesReplayed;
    private long _totalDuplicatesDetected;

    /// <summary>
    /// Initializes a new instance of the BufferManagementService class.
    /// </summary>
    public BufferManagementService(
        ILogger<BufferManagementService> logger,
        ILoggerFactory loggerFactory,
        IConnectionStateTracker connectionTracker,
        IBufferReplayService replayService,
        IPersistentBufferStore persistentStore,
        IOptions<BufferManagementOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _connectionTracker = connectionTracker ?? throw new ArgumentNullException(nameof(connectionTracker));
        _replayService = replayService ?? throw new ArgumentNullException(nameof(replayService));
        _persistentStore = persistentStore ?? throw new ArgumentNullException(nameof(persistentStore));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        _buffers = new ConcurrentDictionary<string, IStreamBuffer>();
        _configurations = new ConcurrentDictionary<string, BufferConfiguration>();
        _recoveryLock = new SemaphoreSlim(1, 1);
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting buffer management service");

        // Recover from persistence if enabled
        if (_options.EnablePersistence)
        {
            var recoveryResult = await RecoverFromPersistenceAsync(cancellationToken).ConfigureAwait(false);
            if (recoveryResult.Success)
            {
                _logger.LogInformation(
                    "Recovered {BufferCount} buffers with {MessageCount} total messages",
                    recoveryResult.BuffersRecovered, recoveryResult.TotalMessagesRecovered);
            }
        }

        // Start cleanup timer
        if (_options.EnableAutomaticCleanup)
        {
            _cleanupTimer = new Timer(
                async _ => await PerformCleanupAsync(CancellationToken.None).ConfigureAwait(false),
                null,
                TimeSpan.FromMinutes(1),
                _options.CleanupInterval);
        }

        _logger.LogInformation("Buffer management service started successfully");
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping buffer management service");

        // Stop cleanup timer
        if (_cleanupTimer != null)
        {
            await _cleanupTimer.DisposeAsync().ConfigureAwait(false);
            _cleanupTimer = null;
        }

        // Persist all active buffers if enabled
        if (_options.EnablePersistence)
        {
            await PersistAllBuffersAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Buffer management service stopped");
    }

    /// <inheritdoc/>
    public async Task<IStreamBuffer> GetOrCreateBufferAsync(
        string streamId,
        BufferConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        // Try to get existing buffer
        if (_buffers.TryGetValue(streamId, out var existingBuffer))
        {
            return existingBuffer;
        }

        // Create new buffer with configuration
        var config = configuration ?? GetDefaultConfiguration();
        _configurations[streamId] = config;

        // Create a new InMemoryStreamBuffer instance for this stream
        var bufferLogger = _loggerFactory.CreateLogger<InMemoryStreamBuffer>();
        var newBuffer = new InMemoryStreamBuffer(streamId, config, bufferLogger);

        if (_buffers.TryAdd(streamId, newBuffer))
        {
            _logger.LogInformation("Created new buffer for stream {StreamId}", streamId);

            // Initialize connection tracking
            await _connectionTracker.RecordConnectionAsync(streamId, cancellationToken).ConfigureAwait(false);

            return newBuffer;
        }

        // Another thread created it, return that one
        return _buffers[streamId];
    }

    /// <inheritdoc/>
    public async Task<bool> BufferMessageAsync(
        string streamId,
        BufferedStreamMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(message);

        var buffer = await GetOrCreateBufferAsync(streamId, null, cancellationToken).ConfigureAwait(false);
        var result = await buffer.AddMessageAsync(message, cancellationToken).ConfigureAwait(false);

        if (result)
        {
            // Update connection state by recording a new connection
            await _connectionTracker.RecordConnectionAsync(streamId, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<DisconnectionResult> HandleDisconnectionAsync(
        string streamId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        try
        {
            // Update connection state
            await _connectionTracker.RecordDisconnectionAsync(streamId, reason, cancellationToken).ConfigureAwait(false);

            // Get buffer
            if (!_buffers.TryGetValue(streamId, out var buffer))
            {
                return new DisconnectionResult
                {
                    Success = false,
                    MessagesBuffered = 0,
                    BufferPersisted = false,
                    ConnectionStatus = ConnectionStatus.Unknown,
                    ErrorMessage = "Buffer not found"
                };
            }

            // Get buffer statistics
            var stats = await buffer.GetStatisticsAsync().ConfigureAwait(false);
            var messagesBuffered = stats.MessageCount;

            // Persist buffer if enabled and has messages
            var bufferPersisted = false;
            if (_options.EnablePersistence && messagesBuffered > 0)
            {
                var messages = await buffer.GetMessagesAsync(cancellationToken).ConfigureAwait(false);
                bufferPersisted = await _persistentStore.PersistBufferAsync(
                    streamId, messages, null, cancellationToken).ConfigureAwait(false);
            }

            // Get connection status
            var connectionState = await _connectionTracker.GetConnectionStateAsync(streamId).ConfigureAwait(false);

            _logger.LogInformation(
                "Handled disconnection for stream {StreamId}. Messages buffered: {MessageCount}, Persisted: {Persisted}",
                streamId, messagesBuffered, bufferPersisted);

            return new DisconnectionResult
            {
                Success = true,
                MessagesBuffered = messagesBuffered,
                BufferPersisted = bufferPersisted,
                ConnectionStatus = connectionState?.Status ?? ConnectionStatus.Disconnected
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle disconnection for stream {StreamId}", streamId);
            return new DisconnectionResult
            {
                Success = false,
                MessagesBuffered = 0,
                BufferPersisted = false,
                ConnectionStatus = ConnectionStatus.Unknown,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc/>
    public async Task<ReconnectionResult> HandleReconnectionAsync(
        string streamId,
        HttpResponse httpResponse,
        ReconnectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(httpResponse);

        options ??= new ReconnectionOptions();

        try
        {
            // Update connection state
            await _connectionTracker.RecordReconnectionAttemptAsync(streamId, true, cancellationToken).ConfigureAwait(false);

            var messagesReplayed = 0;
            var messagesFromPersistence = 0;
            var duplicatesAvoided = 0;
            ReplayResult? replayResult = null;

            // Load from persistence if enabled
            if (options.LoadFromPersistence && _options.EnablePersistence)
            {
                var persistedBuffer = await _persistentStore.LoadBufferAsync(streamId, cancellationToken).ConfigureAwait(false);
                if (persistedBuffer != null && !persistedBuffer.IsCorrupted)
                {
                    messagesFromPersistence = persistedBuffer.Messages.Count;

                    // Add messages back to buffer
                    var buffer = await GetOrCreateBufferAsync(streamId, null, cancellationToken).ConfigureAwait(false);
                    foreach (var message in persistedBuffer.Messages)
                    {
                        _ = await buffer.AddMessageAsync(message, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            // Replay buffered messages if requested
            if (options.ReplayBufferedMessages)
            {
                var buffer = await GetOrCreateBufferAsync(streamId, null, cancellationToken).ConfigureAwait(false);
                var stats = await buffer.GetStatisticsAsync().ConfigureAwait(false);

                if (stats.MessageCount > 0)
                {
                    var messages = await buffer.GetMessagesAsync(cancellationToken).ConfigureAwait(false);

                    // Filter by age if specified
                    var cutoffTime = DateTime.UtcNow - options.MaxMessageAge;
                    var validMessages = messages.Where(m => m.Timestamp >= cutoffTime).ToList();

                    // Replay messages
                    replayResult = await _replayService.ReplayMessagesAsync(
                        streamId, validMessages, httpResponse, options.ReplayOptions, cancellationToken).ConfigureAwait(false);

                    messagesReplayed = replayResult.MessagesReplayed;
                    duplicatesAvoided = replayResult.DuplicatesSkipped;

                    // Update metrics
                    _ = Interlocked.Increment(ref _totalReplayOperations);
                    _ = Interlocked.Add(ref _totalMessagesReplayed, messagesReplayed);
                    _ = Interlocked.Add(ref _totalDuplicatesDetected, duplicatesAvoided);
                }
            }

            // Get connection status
            var connectionState = await _connectionTracker.GetConnectionStateAsync(streamId).ConfigureAwait(false);

            _logger.LogInformation(
                "Handled reconnection for stream {StreamId}. Replayed: {Replayed}, From persistence: {FromPersistence}, Duplicates avoided: {Duplicates}",
                streamId, messagesReplayed, messagesFromPersistence, duplicatesAvoided);

            return new ReconnectionResult
            {
                Success = true,
                MessagesReplayed = messagesReplayed,
                MessagesFromPersistence = messagesFromPersistence,
                DuplicatesAvoided = duplicatesAvoided,
                ConnectionStatus = connectionState?.Status ?? ConnectionStatus.Connected,
                ReplayResult = replayResult
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle reconnection for stream {StreamId}", streamId);
            return new ReconnectionResult
            {
                Success = false,
                MessagesReplayed = 0,
                MessagesFromPersistence = 0,
                DuplicatesAvoided = 0,
                ConnectionStatus = ConnectionStatus.Unknown,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc/>
    public async Task<BufferStatus?> GetBufferStatusAsync(string streamId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        if (!_buffers.TryGetValue(streamId, out var buffer))
        {
            return null;
        }

        var stats = await buffer.GetStatisticsAsync().ConfigureAwait(false);
        var connectionState = await _connectionTracker.GetConnectionStateAsync(streamId).ConfigureAwait(false);
        var configuration = _configurations.GetValueOrDefault(streamId) ?? GetDefaultConfiguration();

        // Check if persisted
        var isPersisted = false;
        if (_options.EnablePersistence)
        {
            var metadata = await _persistentStore.GetBufferMetadataAsync(streamId).ConfigureAwait(false);
            isPersisted = metadata != null;
        }

        return new BufferStatus
        {
            StreamId = streamId,
            Exists = true,
            MessageCount = stats.MessageCount,
            SizeBytes = stats.TotalSizeBytes,
            Configuration = configuration,
            ConnectionState = connectionState,
            Statistics = stats,
            IsPersisted = isPersisted,
            LastActivityAt = connectionState?.LastActivityAt
        };
    }

    /// <inheritdoc/>
    public async Task<int> ClearBufferAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        if (!_buffers.TryGetValue(streamId, out var buffer))
        {
            return 0;
        }

        var stats = await buffer.GetStatisticsAsync().ConfigureAwait(false);
        var messageCount = stats.MessageCount;

        _ = await buffer.ClearAsync(cancellationToken).ConfigureAwait(false);

        // Also clear from persistence if enabled
        if (_options.EnablePersistence)
        {
            _ = await _persistentStore.DeleteBufferAsync(streamId, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Cleared {MessageCount} messages from buffer for stream {StreamId}", messageCount, streamId);
        return messageCount;
    }

    /// <inheritdoc/>
    public async Task<ReplayResult> ForceReplayAsync(
        string streamId,
        HttpResponse httpResponse,
        ReplayOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(httpResponse);

        if (!_buffers.TryGetValue(streamId, out var buffer))
        {
            return new ReplayResult
            {
                Success = false,
                MessagesReplayed = 0,
                DuplicatesSkipped = 0,
                MessagesFailed = 0,
                PartialMessagesMerged = 0,
                BytesReplayed = 0,
                Duration = TimeSpan.Zero,
                ErrorMessage = "Buffer not found"
            };
        }

        var stats = await buffer.GetStatisticsAsync().ConfigureAwait(false);
        if (stats.MessageCount == 0)
        {
            return new ReplayResult
            {
                Success = true,
                MessagesReplayed = 0,
                DuplicatesSkipped = 0,
                MessagesFailed = 0,
                PartialMessagesMerged = 0,
                BytesReplayed = 0,
                Duration = TimeSpan.Zero
            };
        }

        var messages = await buffer.GetMessagesAsync(cancellationToken).ConfigureAwait(false);
        var result = await _replayService.ReplayMessagesAsync(
            streamId, messages, httpResponse, options, cancellationToken).ConfigureAwait(false);

        // Update metrics
        _ = Interlocked.Increment(ref _totalReplayOperations);
        _ = Interlocked.Add(ref _totalMessagesReplayed, result.MessagesReplayed);
        _ = Interlocked.Add(ref _totalDuplicatesDetected, result.DuplicatesSkipped);

        _logger.LogInformation(
            "Force replayed {MessageCount} messages for stream {StreamId}",
            result.MessagesReplayed, streamId);

        return result;
    }

    /// <inheritdoc/>
    public async Task<ServiceRecoveryResult> RecoverFromPersistenceAsync(CancellationToken cancellationToken = default)
    {
        await _recoveryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var startTime = DateTime.UtcNow;
            var streamIds = await _persistentStore.ListPersistedBuffersAsync(cancellationToken).ConfigureAwait(false);

            if (streamIds.Count == 0)
            {
                return new ServiceRecoveryResult
                {
                    Success = true,
                    BuffersRecovered = 0,
                    TotalMessagesRecovered = 0,
                    CorruptedBuffers = 0,
                    RecoveredStreamIds = [],
                    Duration = DateTime.UtcNow - startTime
                };
            }

            var buffersRecovered = 0;
            var totalMessages = 0;
            var corruptedBuffers = 0;
            var recoveredStreamIds = new List<string>();
            var errors = new List<string>();

            foreach (var streamId in streamIds)
            {
                try
                {
                    var persistedBuffer = await _persistentStore.LoadBufferAsync(streamId, cancellationToken).ConfigureAwait(false);
                    if (persistedBuffer != null)
                    {
                        if (persistedBuffer.IsCorrupted)
                        {
                            corruptedBuffers++;
                            errors.Add($"Stream {streamId}: {persistedBuffer.CorruptionDetails}");
                        }
                        else
                        {
                            var buffer = await GetOrCreateBufferAsync(streamId, null, cancellationToken).ConfigureAwait(false);
                            foreach (var message in persistedBuffer.Messages)
                            {
                                _ = await buffer.AddMessageAsync(message, cancellationToken).ConfigureAwait(false);
                            }

                            buffersRecovered++;
                            totalMessages += persistedBuffer.Messages.Count;
                            recoveredStreamIds.Add(streamId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to recover buffer for stream {StreamId}", streamId);
                    errors.Add($"Stream {streamId}: {ex.Message}");
                }
            }

            return new ServiceRecoveryResult
            {
                Success = true,
                BuffersRecovered = buffersRecovered,
                TotalMessagesRecovered = totalMessages,
                CorruptedBuffers = corruptedBuffers,
                RecoveredStreamIds = recoveredStreamIds,
                Duration = DateTime.UtcNow - startTime,
                Errors = errors
            };
        }
        finally
        {
            _ = _recoveryLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<BufferManagementStatistics> GetStatisticsAsync()
    {
        var activeBuffers = _buffers.Count;
        var totalMessages = 0L;
        var totalSizeBytes = 0L;
        var connectedStreams = 0;
        var disconnectedStreams = 0;

        foreach (var kvp in _buffers)
        {
            var stats = await kvp.Value.GetStatisticsAsync().ConfigureAwait(false);
            totalMessages += stats.MessageCount;
            totalSizeBytes += stats.TotalSizeBytes;

            var connectionState = await _connectionTracker.GetConnectionStateAsync(kvp.Key).ConfigureAwait(false);
            if (connectionState?.Status == ConnectionStatus.Connected)
            {
                connectedStreams++;
            }
            else
            {
                disconnectedStreams++;
            }
        }

        // Get persistence statistics
        PersistenceStatistics? persistenceStats = null;
        var persistedBuffers = 0;
        if (_options.EnablePersistence)
        {
            persistenceStats = await _persistentStore.GetStatisticsAsync().ConfigureAwait(false);
            persistedBuffers = persistenceStats.TotalBuffers;
        }

        return new BufferManagementStatistics
        {
            ActiveBuffers = activeBuffers,
            TotalMessages = totalMessages,
            TotalSizeBytes = totalSizeBytes,
            ConnectedStreams = connectedStreams,
            DisconnectedStreams = disconnectedStreams,
            PersistedBuffers = persistedBuffers,
            TotalReplayOperations = _totalReplayOperations,
            TotalMessagesReplayed = _totalMessagesReplayed,
            TotalDuplicatesDetected = _totalDuplicatesDetected,
            PersistenceStatistics = persistenceStats
        };
    }

    /// <inheritdoc/>
    public async Task<CleanupResult> PerformCleanupAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var expiredBuffersRemoved = 0;
        var expiredMessagesRemoved = 0;
        var persistenceEntriesCleaned = 0;
        var spaceReclaimed = 0L;
        var errors = new List<string>();

        try
        {
            // Clean up expired buffers
            var cutoffTime = DateTime.UtcNow - _options.BufferRetentionPeriod;
            var buffersToRemove = new List<string>();

            foreach (var kvp in _buffers)
            {
                var connectionState = await _connectionTracker.GetConnectionStateAsync(kvp.Key).ConfigureAwait(false);
                if (connectionState?.LastActivityAt < cutoffTime)
                {
                    buffersToRemove.Add(kvp.Key);
                }
            }

            foreach (var streamId in buffersToRemove)
            {
                if (_buffers.TryRemove(streamId, out var buffer))
                {
                    var stats = await buffer.GetStatisticsAsync().ConfigureAwait(false);
                    expiredMessagesRemoved += stats.MessageCount;
                    spaceReclaimed += stats.TotalSizeBytes;
                    expiredBuffersRemoved++;

                    _ = await buffer.ClearAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            // Clean up expired messages within active buffers
            foreach (var kvp in _buffers)
            {
                var removed = await kvp.Value.RemoveExpiredMessagesAsync(cancellationToken).ConfigureAwait(false);
                expiredMessagesRemoved += removed;
            }

            // Clean up persistence if enabled
            if (_options.EnablePersistence)
            {
                persistenceEntriesCleaned = await _persistentStore.CleanupExpiredBuffersAsync(
                    _options.BufferRetentionPeriod, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Cleanup completed. Buffers removed: {BuffersRemoved}, Messages removed: {MessagesRemoved}, Space reclaimed: {SpaceReclaimed} bytes",
                expiredBuffersRemoved, expiredMessagesRemoved, spaceReclaimed);

            return new CleanupResult
            {
                Success = true,
                ExpiredBuffersRemoved = expiredBuffersRemoved,
                ExpiredMessagesRemoved = expiredMessagesRemoved,
                PersistenceEntriesCleaned = persistenceEntriesCleaned,
                SpaceReclaimedBytes = spaceReclaimed,
                Duration = DateTime.UtcNow - startTime,
                Errors = errors
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup operation failed");
            errors.Add(ex.Message);

            return new CleanupResult
            {
                Success = false,
                ExpiredBuffersRemoved = expiredBuffersRemoved,
                ExpiredMessagesRemoved = expiredMessagesRemoved,
                PersistenceEntriesCleaned = persistenceEntriesCleaned,
                SpaceReclaimedBytes = spaceReclaimed,
                Duration = DateTime.UtcNow - startTime,
                Errors = errors
            };
        }
    }

    /// <inheritdoc/>
    public Task<bool> ConfigureBufferAsync(
        string streamId,
        BufferConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(configuration);

        _configurations[streamId] = configuration;

        // If buffer exists, update its configuration
        if (_buffers.TryGetValue(streamId, out _))
        {
            // Note: Current implementation doesn't support dynamic configuration updates
            // This would need to be added to IStreamBuffer interface
            _logger.LogInformation("Updated configuration for stream {StreamId}", streamId);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// Persists all active buffers to storage.
    /// </summary>
    private async Task PersistAllBuffersAsync(CancellationToken cancellationToken)
    {
        foreach (var kvp in _buffers)
        {
            try
            {
                var stats = await kvp.Value.GetStatisticsAsync().ConfigureAwait(false);
                if (stats.MessageCount > 0)
                {
                    var messages = await kvp.Value.GetMessagesAsync(cancellationToken).ConfigureAwait(false);
                    _ = await _persistentStore.PersistBufferAsync(kvp.Key, messages, null, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist buffer for stream {StreamId}", kvp.Key);
            }
        }
    }

    /// <summary>
    /// Gets the default buffer configuration.
    /// </summary>
    private BufferConfiguration GetDefaultConfiguration()
    {
        return new BufferConfiguration
        {
            MaxSize = _options.DefaultMaxMessages,
            MessageTTL = _options.DefaultMessageTtl,
            OverflowStrategy = _options.DefaultOverflowStrategy,
            EnablePersistence = _options.EnablePersistence
        };
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cleanupTimer?.Dispose();
        _recoveryLock?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Configuration options for BufferManagementService.
/// </summary>
public sealed class BufferManagementOptions
{
    /// <summary>
    /// Gets or sets whether to enable persistence.
    /// </summary>
    public bool EnablePersistence { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable automatic cleanup.
    /// </summary>
    public bool EnableAutomaticCleanup { get; set; } = true;

    /// <summary>
    /// Gets or sets the cleanup interval.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the buffer retention period.
    /// </summary>
    public TimeSpan BufferRetentionPeriod { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the default maximum messages per buffer.
    /// </summary>
    public int DefaultMaxMessages { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the default maximum size in bytes per buffer.
    /// </summary>
    public long DefaultMaxSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// Gets or sets the default message TTL.
    /// </summary>
    public TimeSpan DefaultMessageTtl { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets or sets the default overflow strategy.
    /// </summary>
    public OverflowStrategy DefaultOverflowStrategy { get; set; } = OverflowStrategy.DropOldest;
}
