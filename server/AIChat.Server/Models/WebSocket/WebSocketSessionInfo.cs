using NetWebSocket = System.Net.WebSockets.WebSocket;

namespace AIChat.Server.Models.WebSocket;

/// <summary>
/// Represents information about an active WebSocket session and its associated Orleans grain.
/// This class tracks the connection state and provides correlation between WebSocket connections
/// and Orleans session grains.
/// </summary>
public class WebSocketSessionInfo
{
    /// <summary>
    /// Gets or sets the unique session identifier.
    /// This correlates to the Orleans ISessionGrain key.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the unique connection identifier.
    /// Used for tracking the specific WebSocket connection instance.
    /// </summary>
    public required string ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets the active WebSocket instance.
    /// </summary>
    public required NetWebSocket WebSocket { get; set; }

    /// <summary>
    /// Gets or sets the negotiated protocol for this session.
    /// </summary>
    public required string Protocol { get; set; }

    /// <summary>
    /// Gets or sets the user identifier associated with this session.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Gets or sets when the connection was established.
    /// </summary>
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the timestamp of the last heartbeat received.
    /// Used for connection health monitoring.
    /// </summary>
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the timestamp of the last activity (message sent/received).
    /// </summary>
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the current connection status.
    /// </summary>
    public WebSocketConnectionStatus Status { get; set; } = WebSocketConnectionStatus.Connected;

    /// <summary>
    /// Gets or sets connection-specific metadata.
    /// Can include client information, feature flags, etc.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = [];

    /// <summary>
    /// Gets or sets connection metrics for monitoring.
    /// </summary>
    public WebSocketConnectionMetrics Metrics { get; set; } = new();
}

/// <summary>
/// Represents the current status of a WebSocket connection.
/// </summary>
public enum WebSocketConnectionStatus
{
    /// <summary>
    /// Connection is being established.
    /// </summary>
    Connecting = 0,

    /// <summary>
    /// Connection is active and healthy.
    /// </summary>
    Connected = 1,

    /// <summary>
    /// Connection is experiencing issues but still active.
    /// </summary>
    Degraded = 2,

    /// <summary>
    /// Connection is being gracefully closed.
    /// </summary>
    Disconnecting = 3,

    /// <summary>
    /// Connection has been closed.
    /// </summary>
    Disconnected = 4,

    /// <summary>
    /// Connection encountered an error.
    /// </summary>
    Error = 5
}

/// <summary>
/// Represents metrics and statistics for a WebSocket connection.
/// </summary>
public class WebSocketConnectionMetrics
{
    /// <summary>
    /// Gets or sets the total number of messages sent.
    /// </summary>
    public long MessagesSent { get; set; }

    /// <summary>
    /// Gets or sets the total number of messages received.
    /// </summary>
    public long MessagesReceived { get; set; }

    /// <summary>
    /// Gets or sets the total bytes sent.
    /// </summary>
    public long BytesSent { get; set; }

    /// <summary>
    /// Gets or sets the total bytes received.
    /// </summary>
    public long BytesReceived { get; set; }

    /// <summary>
    /// Gets or sets the number of errors encountered.
    /// </summary>
    public long ErrorCount { get; set; }

    /// <summary>
    /// Gets or sets the average latency for round-trip messages in milliseconds.
    /// </summary>
    public double AverageLatencyMs { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when metrics were last updated.
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Updates the metrics with a new sent message.
    /// </summary>
    /// <param name="messageSize">Size of the message in bytes</param>
    public void RecordMessageSent(int messageSize)
    {
        MessagesSent++;
        BytesSent += messageSize;
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the metrics with a new received message.
    /// </summary>
    /// <param name="messageSize">Size of the message in bytes</param>
    public void RecordMessageReceived(int messageSize)
    {
        MessagesReceived++;
        BytesReceived += messageSize;
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Records an error occurrence.
    /// </summary>
    public void RecordError()
    {
        ErrorCount++;
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the average latency with a new measurement.
    /// </summary>
    /// <param name="latencyMs">The latency measurement in milliseconds</param>
    public void UpdateLatency(double latencyMs)
    {
        // Simple moving average calculation
        var totalMessages = MessagesSent + MessagesReceived;
        if (totalMessages > 1)
        {
            AverageLatencyMs = ((AverageLatencyMs * (totalMessages - 1)) + latencyMs) / totalMessages;
        }
        else
        {
            AverageLatencyMs = latencyMs;
        }
        LastUpdated = DateTime.UtcNow;
    }
}
