using System.Collections.Concurrent;
using AIChat.Orleans.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.Orleans.Metrics.Storage;

/// <summary>
/// In-memory implementation of SSE metrics storage.
/// Thread-safe implementation suitable for high-throughput operations.
/// </summary>
public class InMemorySseMetricsStorage : ISseMetricsStorage
{
    private readonly ILogger<InMemorySseMetricsStorage> _logger;
    private readonly SseMetricsOptions _options;

    // Thread-safe collections for metrics storage
    private readonly ConcurrentDictionary<string, SseConnectionMetrics> _activeConnections = new();
    private readonly ConcurrentQueue<SseStreamEvent> _recentStreamEvents = new();
    private readonly ConcurrentQueue<SseChunkMetrics> _recentChunks = new();
    private readonly ConcurrentQueue<SseBufferEvent> _bufferEvents = new();
    private readonly ConcurrentQueue<SseFailureEvent> _failureEvents = new();
    private readonly ConcurrentDictionary<string, StreamSessionMetrics> _streamSessions = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemorySseMetricsStorage"/> class.
    /// </summary>
    public InMemorySseMetricsStorage(
        ILogger<InMemorySseMetricsStorage> logger,
        IOptions<SseMetricsOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public Task StoreConnectionAsync(string connectionId, SseConnectionMetrics connection)
    {
        _activeConnections[connectionId] = connection;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SseConnectionMetrics?> RemoveConnectionAsync(string connectionId)
    {
        _activeConnections.TryRemove(connectionId, out var connection);
        return Task.FromResult(connection);
    }

    /// <inheritdoc />
    public Task<IEnumerable<KeyValuePair<string, SseConnectionMetrics>>> GetActiveConnectionsAsync()
    {
        return Task.FromResult(_activeConnections.AsEnumerable());
    }

    /// <inheritdoc />
    public Task StoreStreamEventAsync(SseStreamEvent streamEvent)
    {
        _recentStreamEvents.Enqueue(streamEvent);
        TrimQueue(_recentStreamEvents);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IEnumerable<SseStreamEvent>> GetStreamEventsAsync(DateTime since)
    {
        var events = _recentStreamEvents.Where(e => e.Timestamp > since);
        return Task.FromResult(events);
    }

    /// <inheritdoc />
    public Task StoreChunkMetricsAsync(SseChunkMetrics chunk)
    {
        _recentChunks.Enqueue(chunk);
        TrimQueue(_recentChunks);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IEnumerable<SseChunkMetrics>> GetChunkMetricsAsync(DateTime since)
    {
        var chunks = _recentChunks.Where(c => c.Timestamp > since);
        return Task.FromResult(chunks);
    }

    /// <inheritdoc />
    public Task StoreBufferEventAsync(SseBufferEvent bufferEvent)
    {
        _bufferEvents.Enqueue(bufferEvent);
        TrimQueue(_bufferEvents);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IEnumerable<SseBufferEvent>> GetBufferEventsAsync(DateTime since)
    {
        var events = _bufferEvents.Where(b => b.Timestamp > since);
        return Task.FromResult(events);
    }

    /// <inheritdoc />
    public Task StoreFailureEventAsync(SseFailureEvent failure)
    {
        _failureEvents.Enqueue(failure);
        TrimQueue(_failureEvents);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IEnumerable<SseFailureEvent>> GetFailureEventsAsync(DateTime since)
    {
        var failures = _failureEvents.Where(f => f.Timestamp > since);
        return Task.FromResult(failures);
    }

    /// <inheritdoc />
    public Task StoreStreamSessionAsync(string sessionKey, StreamSessionMetrics session)
    {
        _streamSessions[sessionKey] = session;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IEnumerable<KeyValuePair<string, StreamSessionMetrics>>> GetStreamSessionsAsync(DateTime since)
    {
        var sessions = _streamSessions.Where(s => s.Value.CompletedAt > since);
        return Task.FromResult(sessions);
    }

    /// <inheritdoc />
    public Task<int> CleanupOldDataAsync(DateTime cutoffTime, CancellationToken cancellationToken)
    {
        var itemsRemoved = 0;

        try
        {
            // Clean old connections
            var oldConnections = _activeConnections
                .Where(c => !c.Value.IsActive && c.Value.ClosedAt < cutoffTime)
                .Select(c => c.Key)
                .ToList();

            foreach (var key in oldConnections)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (_activeConnections.TryRemove(key, out _))
                {
                    itemsRemoved++;
                }
            }

            // Clean old sessions
            var oldSessions = _streamSessions
                .Where(s => s.Value.CompletedAt < cutoffTime)
                .Select(s => s.Key)
                .ToList();

            foreach (var key in oldSessions)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (_streamSessions.TryRemove(key, out _))
                {
                    itemsRemoved++;
                }
            }

            // Clean old queued events
            itemsRemoved += CleanQueue(_recentStreamEvents, cutoffTime, e => e.Timestamp);
            itemsRemoved += CleanQueue(_recentChunks, cutoffTime, c => c.Timestamp);
            itemsRemoved += CleanQueue(_bufferEvents, cutoffTime, b => b.Timestamp);
            itemsRemoved += CleanQueue(_failureEvents, cutoffTime, f => f.Timestamp);

            _logger.LogDebug("Cleaned up {Count} old metrics items", itemsRemoved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during metrics cleanup");
        }

        return Task.FromResult(itemsRemoved);
    }

    /// <inheritdoc />
    public Task<StorageStatistics> GetStatisticsAsync()
    {
        var stats = new StorageStatistics
        {
            ActiveConnectionCount = _activeConnections.Count,
            StreamEventCount = _recentStreamEvents.Count,
            ChunkMetricsCount = _recentChunks.Count,
            BufferEventCount = _bufferEvents.Count,
            FailureEventCount = _failureEvents.Count,
            StreamSessionCount = _streamSessions.Count,
            EstimatedMemoryUsage = EstimateMemoryUsage()
        };

        return Task.FromResult(stats);
    }

    #region Private Helper Methods

    private void TrimQueue<T>(ConcurrentQueue<T> queue)
    {
        while (queue.Count > _options.MaxRecentEvents)
        {
            _ = queue.TryDequeue(out _);
        }
    }

    private static int CleanQueue<T>(ConcurrentQueue<T> queue, DateTime cutoffTime, Func<T, DateTime> getTimestamp)
    {
        var itemsRemoved = 0;
        var tempQueue = new ConcurrentQueue<T>();

        while (queue.TryDequeue(out var item))
        {
            if (getTimestamp(item) > cutoffTime)
            {
                tempQueue.Enqueue(item);
            }
            else
            {
                itemsRemoved++;
            }
        }

        while (tempQueue.TryDequeue(out var item))
        {
            queue.Enqueue(item);
        }

        return itemsRemoved;
    }

    private long EstimateMemoryUsage()
    {
        // Rough estimation of memory usage
        const int avgObjectSize = 100; // bytes
        var totalObjects = _activeConnections.Count +
                          _recentStreamEvents.Count +
                          _recentChunks.Count +
                          _bufferEvents.Count +
                          _failureEvents.Count +
                          _streamSessions.Count;

        return totalObjects * avgObjectSize;
    }

    #endregion
}