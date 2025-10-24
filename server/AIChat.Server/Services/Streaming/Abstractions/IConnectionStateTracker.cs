namespace AIChat.Server.Services.Streaming.Abstractions;

/// <summary>
/// Tracks and manages connection state for streaming operations.
/// </summary>
public interface IConnectionStateTracker
{
    /// <summary>
    /// Gets the current connection state for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <returns>Connection state or null if not tracked</returns>
    Task<ConnectionState?> GetConnectionStateAsync(string streamId);

    /// <summary>
    /// Updates the connection state for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="state">The new connection state</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateConnectionStateAsync(
        string streamId,
        ConnectionState state,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Records a successful connection for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordConnectionAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a disconnection for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="reason">Optional disconnection reason</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordDisconnectionAsync(
        string streamId,
        string? reason = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Records a reconnection attempt for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="success">Whether the reconnection was successful</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordReconnectionAttemptAsync(
        string streamId,
        bool success,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks if a stream connection is healthy.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <returns>True if healthy, false otherwise</returns>
    Task<bool> IsConnectionHealthyAsync(string streamId);

    /// <summary>
    /// Gets connection metrics for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <returns>Connection metrics or null if not tracked</returns>
    Task<ConnectionMetrics?> GetConnectionMetricsAsync(string streamId);

    /// <summary>
    /// Removes tracking for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveTrackingAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all actively tracked stream identifiers.
    /// </summary>
    /// <returns>Collection of stream identifiers</returns>
    Task<IReadOnlyList<string>> GetTrackedStreamIdsAsync();

    /// <summary>
    /// Performs a health check on all tracked connections.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check results</returns>
    Task<ConnectionHealthReport> PerformHealthCheckAsync(
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Represents the state of a connection.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Streaming.Abstractions.ConnectionState")]
public record ConnectionState
{
    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    [Id(0)]
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets the current status of the connection.
    /// </summary>
    [Id(1)]
    public required ConnectionStatus Status { get; init; }

    /// <summary>
    /// Gets the timestamp when the connection was established.
    /// </summary>
    [Id(2)]
    public DateTime? ConnectedAt { get; init; }

    /// <summary>
    /// Gets the timestamp of the last disconnection.
    /// </summary>
    [Id(3)]
    public DateTime? DisconnectedAt { get; init; }

    /// <summary>
    /// Gets the timestamp of the last activity.
    /// </summary>
    [Id(4)]
    public required DateTime LastActivityAt { get; init; }

    /// <summary>
    /// Gets the number of reconnection attempts.
    /// </summary>
    [Id(5)]
    public required int ReconnectionAttempts { get; init; }

    /// <summary>
    /// Gets the number of successful reconnections.
    /// </summary>
    [Id(6)]
    public required int SuccessfulReconnections { get; init; }

    /// <summary>
    /// Gets the last disconnection reason if any.
    /// </summary>
    [Id(7)]
    public string? LastDisconnectionReason { get; init; }

    /// <summary>
    /// Gets whether the connection is considered stable.
    /// </summary>
    public bool IsStable =>
        Status == ConnectionStatus.Connected && LastActivityAt > DateTime.UtcNow.AddSeconds(-30);

    /// <summary>
    /// Gets the connection duration if connected.
    /// </summary>
    public TimeSpan? ConnectionDuration =>
        ConnectedAt.HasValue && Status == ConnectionStatus.Connected
            ? DateTime.UtcNow - ConnectedAt.Value
            : null;

    /// <summary>
    /// Gets the time since last activity.
    /// </summary>
    public TimeSpan TimeSinceLastActivity => DateTime.UtcNow - LastActivityAt;
}

/// <summary>
/// Defines the status of a connection.
/// </summary>
[GenerateSerializer]
public enum ConnectionStatus
{
    /// <summary>
    /// Connection is active and healthy.
    /// </summary>
    Connected = 0,

    /// <summary>
    /// Connection is disconnected.
    /// </summary>
    Disconnected = 1,

    /// <summary>
    /// Connection is attempting to reconnect.
    /// </summary>
    Reconnecting = 2,

    /// <summary>
    /// Connection is unstable but attempting to maintain.
    /// </summary>
    Unstable = 3,

    /// <summary>
    /// Connection has failed permanently.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// Connection status is unknown.
    /// </summary>
    Unknown = 5,
}

/// <summary>
/// Metrics for a connection.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Streaming.Abstractions.ConnectionMetrics")]
public record ConnectionMetrics
{
    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    [Id(0)]
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets the total connection time.
    /// </summary>
    [Id(1)]
    public required TimeSpan TotalConnectionTime { get; init; }

    /// <summary>
    /// Gets the total disconnection time.
    /// </summary>
    [Id(2)]
    public required TimeSpan TotalDisconnectionTime { get; init; }

    /// <summary>
    /// Gets the number of disconnections.
    /// </summary>
    [Id(3)]
    public required int DisconnectionCount { get; init; }

    /// <summary>
    /// Gets the number of reconnection attempts.
    /// </summary>
    [Id(4)]
    public required int ReconnectionAttempts { get; init; }

    /// <summary>
    /// Gets the number of successful reconnections.
    /// </summary>
    [Id(5)]
    public required int SuccessfulReconnections { get; init; }

    /// <summary>
    /// Gets the average reconnection time.
    /// </summary>
    [Id(6)]
    public TimeSpan AverageReconnectionTime { get; init; }

    /// <summary>
    /// Gets the connection uptime percentage.
    /// </summary>
    [Id(7)]
    public double UptimePercentage { get; init; }

    /// <summary>
    /// Gets the longest continuous connection duration.
    /// </summary>
    [Id(8)]
    public TimeSpan LongestConnectionDuration { get; init; }

    /// <summary>
    /// Gets the reconnection success rate.
    /// </summary>
    public double ReconnectionSuccessRate =>
        ReconnectionAttempts > 0 ? (double)SuccessfulReconnections / ReconnectionAttempts * 100 : 0;
}

/// <summary>
/// Health report for all tracked connections.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Streaming.Abstractions.ConnectionHealthReport")]
public record ConnectionHealthReport
{
    /// <summary>
    /// Gets the timestamp of the health check.
    /// </summary>
    [Id(0)]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the total number of tracked connections.
    /// </summary>
    [Id(1)]
    public required int TotalConnections { get; init; }

    /// <summary>
    /// Gets the number of healthy connections.
    /// </summary>
    [Id(2)]
    public required int HealthyConnections { get; init; }

    /// <summary>
    /// Gets the number of unstable connections.
    /// </summary>
    [Id(3)]
    public required int UnstableConnections { get; init; }

    /// <summary>
    /// Gets the number of disconnected connections.
    /// </summary>
    [Id(4)]
    public required int DisconnectedConnections { get; init; }

    /// <summary>
    /// Gets the number of failed connections.
    /// </summary>
    [Id(5)]
    public required int FailedConnections { get; init; }

    /// <summary>
    /// Gets individual connection states.
    /// </summary>
    [Id(6)]
    public required IReadOnlyList<ConnectionState> ConnectionStates { get; init; }

    /// <summary>
    /// Gets the overall health percentage.
    /// </summary>
    public double HealthPercentage =>
        TotalConnections > 0 ? (double)HealthyConnections / TotalConnections * 100 : 100;

    /// <summary>
    /// Gets whether the overall system is healthy.
    /// </summary>
    public bool IsHealthy => HealthPercentage >= 80 && FailedConnections == 0;
}
