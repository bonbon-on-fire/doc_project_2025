using System.Collections.Concurrent;
using AIChat.Server.Services.Streaming.Abstractions;

namespace AIChat.Server.Services.Streaming.Implementations;

/// <summary>
/// Tracks and manages connection state for streaming operations.
/// </summary>
public class ConnectionStateTracker : IConnectionStateTracker, IDisposable
{
    private readonly ILogger<ConnectionStateTracker> _logger;
    private readonly ConcurrentDictionary<string, ConnectionStateData> _connectionStates;
    private readonly Timer _healthCheckTimer;
    private readonly TimeSpan _healthCheckInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _inactivityThreshold = TimeSpan.FromSeconds(30);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the ConnectionStateTracker class.
    /// </summary>
    public ConnectionStateTracker(ILogger<ConnectionStateTracker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionStates = new ConcurrentDictionary<string, ConnectionStateData>();

        // Start health check timer
        _healthCheckTimer = new Timer(
            async _ => await PerformHealthCheckAsync(),
            null,
            _healthCheckInterval,
            _healthCheckInterval
        );

        _logger.LogInformation(
            "ConnectionStateTracker initialized with health check interval: {Interval}",
            _healthCheckInterval
        );
    }

    /// <inheritdoc />
    public async Task<ConnectionState?> GetConnectionStateAsync(string streamId)
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        if (_connectionStates.TryGetValue(streamId, out var data))
        {
            return await Task.FromResult(data.ToConnectionState());
        }
        else
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task UpdateConnectionStateAsync(
        string streamId,
        ConnectionState state,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        ArgumentNullException.ThrowIfNull(state);

        var data = ConnectionStateData.FromConnectionState(state);
        _ = _connectionStates.AddOrUpdate(streamId, data, (key, existing) => data);

        _logger.LogDebug(
            "Updated connection state for {StreamId}: Status={Status}, ReconnectionAttempts={Attempts}",
            streamId,
            state.Status,
            state.ReconnectionAttempts
        );

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RecordConnectionAsync(
        string streamId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        var now = DateTime.UtcNow;
        var data = _connectionStates.AddOrUpdate(
            streamId,
            key => new ConnectionStateData
            {
                StreamId = key,
                Status = ConnectionStatus.Connected,
                ConnectedAt = now,
                LastActivityAt = now,
                ConnectionStartTime = now,
            },
            (key, existing) =>
            {
                existing.Status = ConnectionStatus.Connected;
                existing.ConnectedAt = now;
                existing.LastActivityAt = now;
                existing.DisconnectedAt = null;
                if (existing.ConnectionStartTime == default)
                {
                    existing.ConnectionStartTime = now;
                }

                return existing;
            }
        );

        _logger.LogInformation("Connection established for stream {StreamId}", streamId);
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RecordDisconnectionAsync(
        string streamId,
        string? reason = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        var now = DateTime.UtcNow;
        var data = _connectionStates.AddOrUpdate(
            streamId,
            key => new ConnectionStateData
            {
                StreamId = key,
                Status = ConnectionStatus.Disconnected,
                DisconnectedAt = now,
                LastActivityAt = now,
                LastDisconnectionReason = reason,
                DisconnectionCount = 1,
            },
            (key, existing) =>
            {
                // Update connection time tracking
                if (existing.ConnectedAt.HasValue)
                {
                    var connectionDuration = now - existing.ConnectedAt.Value;
                    existing.TotalConnectionTime = existing.TotalConnectionTime.Add(
                        connectionDuration
                    );

                    if (connectionDuration > existing.LongestConnectionDuration)
                    {
                        existing.LongestConnectionDuration = connectionDuration;
                    }
                }

                existing.Status = ConnectionStatus.Disconnected;
                existing.DisconnectedAt = now;
                existing.LastActivityAt = now;
                existing.LastDisconnectionReason = reason;
                existing.DisconnectionCount++;
                existing.DisconnectionStartTime = now;
                return existing;
            }
        );

        _logger.LogWarning(
            "Disconnection recorded for stream {StreamId}. Reason: {Reason}, Total disconnections: {Count}",
            streamId,
            reason ?? "Unknown",
            data.DisconnectionCount
        );

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RecordReconnectionAttemptAsync(
        string streamId,
        bool success,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        var now = DateTime.UtcNow;
        var data = _connectionStates.AddOrUpdate(
            streamId,
            key => new ConnectionStateData
            {
                StreamId = key,
                Status = success ? ConnectionStatus.Connected : ConnectionStatus.Reconnecting,
                LastActivityAt = now,
                ReconnectionAttempts = 1,
                SuccessfulReconnections = success ? 1 : 0,
            },
            (key, existing) =>
            {
                existing.ReconnectionAttempts++;
                existing.LastActivityAt = now;

                if (success)
                {
                    existing.SuccessfulReconnections++;
                    existing.Status = ConnectionStatus.Connected;
                    existing.ConnectedAt = now;

                    // Track reconnection time
                    if (existing.DisconnectionStartTime.HasValue)
                    {
                        var reconnectionTime = now - existing.DisconnectionStartTime.Value;
                        existing.TotalReconnectionTime = existing.TotalReconnectionTime.Add(
                            reconnectionTime
                        );
                        existing.ReconnectionTimes.Add(reconnectionTime);
                        existing.DisconnectionStartTime = null;
                    }

                    // Update disconnection time tracking
                    if (existing.DisconnectedAt.HasValue)
                    {
                        var disconnectionDuration = now - existing.DisconnectedAt.Value;
                        existing.TotalDisconnectionTime = existing.TotalDisconnectionTime.Add(
                            disconnectionDuration
                        );
                    }
                }
                else
                {
                    existing.Status = ConnectionStatus.Reconnecting;
                }

                return existing;
            }
        );

        _logger.LogInformation(
            "Reconnection attempt {Result} for stream {StreamId}. Total attempts: {Attempts}, Successful: {Successful}",
            success ? "succeeded" : "failed",
            streamId,
            data.ReconnectionAttempts,
            data.SuccessfulReconnections
        );

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> IsConnectionHealthyAsync(string streamId)
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        if (!_connectionStates.TryGetValue(streamId, out var data))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var timeSinceLastActivity = now - data.LastActivityAt;

        // Connection is healthy if:
        // 1. Status is Connected
        // 2. Last activity was within the threshold
        // 3. Not too many recent failures
        var isHealthy =
            data.Status == ConnectionStatus.Connected
            && timeSinceLastActivity < _inactivityThreshold
            && data.RecentFailureCount < 3;

        return await Task.FromResult(isHealthy);
    }

    /// <inheritdoc />
    public async Task<ConnectionMetrics?> GetConnectionMetricsAsync(string streamId)
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        if (!_connectionStates.TryGetValue(streamId, out var data))
        {
            return null;
        }

        var now = DateTime.UtcNow;

        // Calculate current session times
        var currentConnectionTime =
            data.Status == ConnectionStatus.Connected && data.ConnectedAt.HasValue
                ? now - data.ConnectedAt.Value
                : TimeSpan.Zero;

        var currentDisconnectionTime =
            data.Status == ConnectionStatus.Disconnected && data.DisconnectedAt.HasValue
                ? now - data.DisconnectedAt.Value
                : TimeSpan.Zero;

        var totalConnectionTime = data.TotalConnectionTime.Add(currentConnectionTime);
        var totalDisconnectionTime = data.TotalDisconnectionTime.Add(currentDisconnectionTime);
        var totalTime = totalConnectionTime.Add(totalDisconnectionTime);

        var metrics = new ConnectionMetrics
        {
            StreamId = streamId,
            TotalConnectionTime = totalConnectionTime,
            TotalDisconnectionTime = totalDisconnectionTime,
            DisconnectionCount = data.DisconnectionCount,
            ReconnectionAttempts = data.ReconnectionAttempts,
            SuccessfulReconnections = data.SuccessfulReconnections,
            AverageReconnectionTime =
                data.ReconnectionTimes.Count != 0
                    ? TimeSpan.FromMilliseconds(
                        data.ReconnectionTimes.Average(t => t.TotalMilliseconds)
                    )
                    : TimeSpan.Zero,
            UptimePercentage =
                totalTime.TotalSeconds > 0
                    ? totalConnectionTime.TotalSeconds / totalTime.TotalSeconds * 100
                    : 100,
            LongestConnectionDuration = data.LongestConnectionDuration,
        };

        return await Task.FromResult(metrics);
    }

    /// <inheritdoc />
    public async Task RemoveTrackingAsync(
        string streamId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        if (_connectionStates.TryRemove(streamId, out var removed))
        {
            _logger.LogInformation(
                "Removed tracking for stream {StreamId}. Final status: {Status}",
                streamId,
                removed.Status
            );
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetTrackedStreamIdsAsync()
    {
        return await Task.FromResult(_connectionStates.Keys.ToList());
    }

    /// <inheritdoc />
    public async Task<ConnectionHealthReport> PerformHealthCheckAsync(
        CancellationToken cancellationToken = default
    )
    {
        var now = DateTime.UtcNow;
        var states = new List<ConnectionState>();
        var healthyCount = 0;
        var unstableCount = 0;
        var disconnectedCount = 0;
        var failedCount = 0;

        foreach (var kvp in _connectionStates)
        {
            var data = kvp.Value;
            var state = data.ToConnectionState();
            states.Add(state);

            // Update status based on activity
            var timeSinceLastActivity = now - data.LastActivityAt;
            if (
                data.Status == ConnectionStatus.Connected
                && timeSinceLastActivity > _inactivityThreshold
            )
            {
                // Mark as unstable if no recent activity
                data.Status = ConnectionStatus.Unstable;
                _logger.LogWarning(
                    "Stream {StreamId} marked as unstable due to inactivity: {Duration}",
                    kvp.Key,
                    timeSinceLastActivity
                );
            }

            // Count by status
            switch (data.Status)
            {
                case ConnectionStatus.Connected:
                    if (timeSinceLastActivity < _inactivityThreshold)
                    {
                        healthyCount++;
                    }
                    else
                    {
                        unstableCount++;
                    }

                    break;
                case ConnectionStatus.Unstable:
                case ConnectionStatus.Reconnecting:
                    unstableCount++;
                    break;
                case ConnectionStatus.Disconnected:
                    disconnectedCount++;
                    break;
                case ConnectionStatus.Failed:
                    failedCount++;
                    break;
                default:
                    // No action needed for unknown or other states
                    break;
            }
        }

        var report = new ConnectionHealthReport
        {
            Timestamp = now,
            TotalConnections = states.Count,
            HealthyConnections = healthyCount,
            UnstableConnections = unstableCount,
            DisconnectedConnections = disconnectedCount,
            FailedConnections = failedCount,
            ConnectionStates = states,
        };

        if (!report.IsHealthy)
        {
            _logger.LogWarning(
                "Connection health check: {Healthy}/{Total} healthy, {Unstable} unstable, {Disconnected} disconnected, {Failed} failed",
                healthyCount,
                states.Count,
                unstableCount,
                disconnectedCount,
                failedCount
            );
        }

        return await Task.FromResult(report);
    }

    /// <summary>
    /// Disposes the tracker resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the tracker resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _healthCheckTimer?.Dispose();
            }
            _disposed = true;
        }
    }

    /// <summary>
    /// Internal data structure for connection state tracking.
    /// </summary>
    private sealed class ConnectionStateData
    {
        public string StreamId { get; set; } = string.Empty;
        public ConnectionStatus Status { get; set; } = ConnectionStatus.Unknown;
        public DateTime? ConnectedAt { get; set; }
        public DateTime? DisconnectedAt { get; set; }
        public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
        public int ReconnectionAttempts { get; set; }
        public int SuccessfulReconnections { get; set; }
        public string? LastDisconnectionReason { get; set; }
        public int DisconnectionCount { get; set; }
        public int RecentFailureCount { get; set; }

        /// <summary>
        /// Time tracking
        /// </summary>
        public DateTime ConnectionStartTime { get; set; }
        public DateTime? DisconnectionStartTime { get; set; }
        public TimeSpan TotalConnectionTime { get; set; } = TimeSpan.Zero;
        public TimeSpan TotalDisconnectionTime { get; set; } = TimeSpan.Zero;
        public TimeSpan TotalReconnectionTime { get; set; } = TimeSpan.Zero;
        public TimeSpan LongestConnectionDuration { get; set; } = TimeSpan.Zero;
        public List<TimeSpan> ReconnectionTimes { get; set; } = [];

        public ConnectionState ToConnectionState()
        {
            return new ConnectionState
            {
                StreamId = StreamId,
                Status = Status,
                ConnectedAt = ConnectedAt,
                DisconnectedAt = DisconnectedAt,
                LastActivityAt = LastActivityAt,
                ReconnectionAttempts = ReconnectionAttempts,
                SuccessfulReconnections = SuccessfulReconnections,
                LastDisconnectionReason = LastDisconnectionReason,
            };
        }

        public static ConnectionStateData FromConnectionState(ConnectionState state)
        {
            return new ConnectionStateData
            {
                StreamId = state.StreamId,
                Status = state.Status,
                ConnectedAt = state.ConnectedAt,
                DisconnectedAt = state.DisconnectedAt,
                LastActivityAt = state.LastActivityAt,
                ReconnectionAttempts = state.ReconnectionAttempts,
                SuccessfulReconnections = state.SuccessfulReconnections,
                LastDisconnectionReason = state.LastDisconnectionReason,
            };
        }
    }
}
