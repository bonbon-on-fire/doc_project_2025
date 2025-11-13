using System.Collections.Concurrent;
using System.Diagnostics;
using AIChat.Server.Models.WebSocket;
using NetWebSocket = System.Net.WebSockets.WebSocket;

namespace AIChat.Server.Services.WebSocket;

/// <summary>
/// <para>
/// Implementation of IWebSocketSessionManager for managing active WebSocket sessions.
/// Provides thread-safe session lifecycle management, connection tracking, and correlation
/// with Orleans grains for the WebSocket handler system.
/// </para>
/// <para>
/// Features:
/// - Thread-safe concurrent session management
/// - Comprehensive metrics and statistics collection
/// - Distributed tracing integration
/// - Health monitoring with detailed status reporting
/// - Automatic cleanup of stale sessions
/// </para>
/// </summary>
public class WebSocketSessionManager : IWebSocketSessionManager
{
    private readonly ILogger<WebSocketSessionManager> _logger;
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.WebSocketSessionManager");

    /// <summary>
    /// Thread-safe collections for session management
    /// </summary>
    private readonly ConcurrentDictionary<string, WebSocketSessionInfo> _sessionsBySessionId = new();
    private readonly ConcurrentDictionary<string, WebSocketSessionInfo> _sessionsByConnectionId = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _sessionsByUserId = new();
    // Note: Using ConcurrentDictionary<string, bool> as a concurrent set for session IDs

    /// <summary>
    /// Statistics tracking
    /// </summary>
    private long _totalSessionsCreated;
    private long _totalSessionsRemoved;
    private long _totalHeartbeats;
    private long _totalActivityUpdates;

    /// <summary>
    /// Initializes a new instance of the WebSocketSessionManager.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public WebSocketSessionManager(ILogger<WebSocketSessionManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<WebSocketSessionInfo> CreateSessionAsync(
        NetWebSocket webSocket,
        string connectionId,
        string userId,
        string protocol,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("CreateSession");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate inputs
            ArgumentNullException.ThrowIfNull(webSocket);
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);
            ArgumentException.ThrowIfNullOrWhiteSpace(protocol);

            var sessionId = Guid.NewGuid().ToString();
            var sessionInfo = new WebSocketSessionInfo
            {
                SessionId = sessionId,
                ConnectionId = connectionId,
                WebSocket = webSocket,
                Protocol = protocol,
                UserId = userId,
                ConnectedAt = DateTime.UtcNow,
                LastHeartbeat = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                Status = WebSocketConnectionStatus.Connected,
                Metadata = metadata ?? [],
                Metrics = new WebSocketConnectionMetrics()
            };

            // Add to session collections
            _sessionsBySessionId.TryAdd(sessionId, sessionInfo);
            _sessionsByConnectionId.TryAdd(connectionId, sessionInfo);

            // Add to user sessions mapping (thread-safe without locks)
            var userSessions = _sessionsByUserId.GetOrAdd(userId, _ => new ConcurrentDictionary<string, bool>());
            userSessions.TryAdd(sessionId, true);

            Interlocked.Increment(ref _totalSessionsCreated);
            stopwatch.Stop();

            _logger.LogInformation(
                "Created WebSocket session {SessionId} for user {UserId} with protocol {Protocol} (Connection: {ConnectionId}) in {ElapsedMs}ms",
                sessionId, userId, protocol, connectionId, stopwatch.ElapsedMilliseconds);

            activity?.SetTag("session.id", sessionId);
            activity?.SetTag("user.id", userId);
            activity?.SetTag("protocol", protocol);
            activity?.SetTag("connection.id", connectionId);
            activity?.SetTag("operation.success", true);
            activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);

            return Task.FromResult(sessionInfo);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Failed to create WebSocket session for user {UserId} with protocol {Protocol} (Connection: {ConnectionId}) after {ElapsedMs}ms",
                userId, protocol, connectionId, stopwatch.ElapsedMilliseconds);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<WebSocketSessionInfo?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        _sessionsBySessionId.TryGetValue(sessionId, out var sessionInfo);
        return Task.FromResult(sessionInfo);
    }

    /// <inheritdoc />
    public Task<WebSocketSessionInfo?> GetSessionByConnectionIdAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        _sessionsByConnectionId.TryGetValue(connectionId, out var sessionInfo);
        return Task.FromResult(sessionInfo);
    }

    /// <inheritdoc />
    public Task<List<WebSocketSessionInfo>> GetUserSessionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_sessionsByUserId.TryGetValue(userId, out var sessionIds))
        {
            return Task.FromResult(new List<WebSocketSessionInfo>());
        }

        var sessions = new List<WebSocketSessionInfo>();
        foreach (var sessionId in sessionIds.Keys)
        {
            if (_sessionsBySessionId.TryGetValue(sessionId, out var sessionInfo))
            {
                sessions.Add(sessionInfo);
            }
        }

        return Task.FromResult(sessions);
    }

    /// <inheritdoc />
    public Task UpdateSessionAsync(
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionInfo);

        using var activity = ActivitySource.StartActivity("UpdateSession");

        try
        {
            if (_sessionsBySessionId.TryGetValue(sessionInfo.SessionId, out var existingSession))
            {
                // Update the session in all collections
                _sessionsBySessionId.TryUpdate(sessionInfo.SessionId, sessionInfo, existingSession);
                _sessionsByConnectionId.TryUpdate(sessionInfo.ConnectionId, sessionInfo, existingSession);

                _logger.LogDebug("Updated WebSocket session {SessionId}", sessionInfo.SessionId);
                activity?.SetTag("session.id", sessionInfo.SessionId);
                activity?.SetTag("operation.success", true);
            }
            else
            {
                _logger.LogWarning("Attempted to update non-existent session {SessionId}", sessionInfo.SessionId);
                activity?.SetTag("operation.success", false);
                activity?.SetTag("error.reason", "session_not_found");
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update session {SessionId}", sessionInfo.SessionId);
            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            throw;
        }
    }

    /// <inheritdoc />
    public Task RecordHeartbeatAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_sessionsBySessionId.TryGetValue(sessionId, out var sessionInfo))
        {
            sessionInfo.LastHeartbeat = DateTime.UtcNow;
            sessionInfo.LastActivity = DateTime.UtcNow;

            Interlocked.Increment(ref _totalHeartbeats);

            _logger.LogTrace("Recorded heartbeat for session {SessionId}", sessionId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateActivityAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_sessionsBySessionId.TryGetValue(sessionId, out var sessionInfo))
        {
            sessionInfo.LastActivity = DateTime.UtcNow;
            Interlocked.Increment(ref _totalActivityUpdates);

            _logger.LogTrace("Updated activity for session {SessionId}", sessionId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RemoveSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        using var activity = ActivitySource.StartActivity("RemoveSession");

        try
        {
            if (!_sessionsBySessionId.TryRemove(sessionId, out var sessionInfo))
            {
                _logger.LogDebug("Session {SessionId} not found for removal", sessionId);
                activity?.SetTag("operation.success", false);
                activity?.SetTag("error.reason", "session_not_found");
                return Task.FromResult(false);
            }

            // Remove from connection mapping
            _sessionsByConnectionId.TryRemove(sessionInfo.ConnectionId, out _);

            // Remove from user sessions mapping (thread-safe without locks)
            if (_sessionsByUserId.TryGetValue(sessionInfo.UserId, out var userSessions))
            {
                userSessions.TryRemove(sessionId, out _);

                // Clean up empty user entries
                if (userSessions.IsEmpty)
                {
                    // Try to remove the user entry if it's still empty
                    // Note: There's a small race condition here but it's benign - worst case
                    // the empty dictionary stays until the next cleanup
                    _sessionsByUserId.TryRemove(sessionInfo.UserId, out _);
                }
            }

            Interlocked.Increment(ref _totalSessionsRemoved);

            _logger.LogInformation(
                "Removed WebSocket session {SessionId} for user {UserId} (Connection: {ConnectionId})",
                sessionId, sessionInfo.UserId, sessionInfo.ConnectionId);

            activity?.SetTag("session.id", sessionId);
            activity?.SetTag("user.id", sessionInfo.UserId);
            activity?.SetTag("operation.success", true);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove session {SessionId}", sessionId);
            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<List<WebSocketSessionInfo>> GetAllSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_sessionsBySessionId.Values.ToList());
    }

    /// <inheritdoc />
    public Task<List<WebSocketSessionInfo>> GetSessionsByProtocolAsync(
        string protocol,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);

        var sessions = _sessionsBySessionId.Values
            .Where(s => string.Equals(s.Protocol, protocol, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult(sessions);
    }

    /// <inheritdoc />
    public async Task<int> CleanupStaleSessionsAsync(
        TimeSpan inactivityThreshold,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("CleanupStaleSessions");
        var cutoffTime = DateTime.UtcNow - inactivityThreshold;
        var removedCount = 0;

        try
        {
            var staleSessions = _sessionsBySessionId.Values
                .Where(s => s.LastActivity < cutoffTime || s.Status == WebSocketConnectionStatus.Disconnected)
                .ToList();

            foreach (var session in staleSessions)
            {
                if (await RemoveSessionAsync(session.SessionId, cancellationToken))
                {
                    removedCount++;
                }
            }

            _logger.LogInformation(
                "Cleaned up {RemovedCount} stale WebSocket sessions (threshold: {ThresholdMinutes} minutes)",
                removedCount, inactivityThreshold.TotalMinutes);

            activity?.SetTag("removed.count", removedCount);
            activity?.SetTag("threshold.minutes", inactivityThreshold.TotalMinutes);
            activity?.SetTag("operation.success", true);

            return removedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup stale sessions");
            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<WebSocketSessionStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var allSessions = _sessionsBySessionId.Values.ToList();

        var sessionsByProtocol = allSessions
            .GroupBy(s => s.Protocol)
            .ToDictionary(g => g.Key, g => g.Count());

        var sessionsByStatus = allSessions
            .GroupBy(s => s.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        var averageSessionDuration = allSessions
            .Where(s => s.Status == WebSocketConnectionStatus.Connected)
            .Select(s => (DateTime.UtcNow - s.ConnectedAt).TotalMinutes)
            .DefaultIfEmpty(0)
            .Average();

        var aggregateMetrics = new WebSocketAggregateMetrics
        {
            TotalMessagesSent = allSessions.Sum(s => s.Metrics.MessagesSent),
            TotalMessagesReceived = allSessions.Sum(s => s.Metrics.MessagesReceived),
            TotalBytesSent = allSessions.Sum(s => s.Metrics.BytesSent),
            TotalBytesReceived = allSessions.Sum(s => s.Metrics.BytesReceived),
            TotalErrors = allSessions.Sum(s => s.Metrics.ErrorCount),
            AverageLatencyMs = allSessions
                .Where(s => s.Metrics.AverageLatencyMs > 0)
                .Select(s => s.Metrics.AverageLatencyMs)
                .DefaultIfEmpty(0)
                .Average()
        };

        var statistics = new WebSocketSessionStatistics
        {
            TotalActiveSessions = allSessions.Count,
            SessionsByProtocol = sessionsByProtocol,
            SessionsByStatus = sessionsByStatus,
            TotalSessionsCreated = Interlocked.Read(ref _totalSessionsCreated),
            TotalSessionsRemoved = Interlocked.Read(ref _totalSessionsRemoved),
            AverageSessionDurationMinutes = averageSessionDuration,
            AggregateMetrics = aggregateMetrics
        };

        return Task.FromResult(statistics);
    }

    /// <inheritdoc />
    public Task<bool> IsSessionActiveAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_sessionsBySessionId.TryGetValue(sessionId, out var sessionInfo))
        {
            return Task.FromResult(
                sessionInfo.Status == WebSocketConnectionStatus.Connected ||
                sessionInfo.Status == WebSocketConnectionStatus.Degraded);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task UpdateSessionStatusAsync(
        string sessionId,
        WebSocketConnectionStatus status,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_sessionsBySessionId.TryGetValue(sessionId, out var sessionInfo))
        {
            var previousStatus = sessionInfo.Status;
            sessionInfo.Status = status;

            _logger.LogDebug(
                "Updated session {SessionId} status from {PreviousStatus} to {NewStatus}",
                sessionId, previousStatus, status);
        }

        return Task.CompletedTask;
    }
}
