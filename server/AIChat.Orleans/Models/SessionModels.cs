using System.ComponentModel.DataAnnotations;
using Orleans;

namespace AIChat.Orleans.Contracts;

#region Enums

/// <summary>
/// Represents the possible states of a session.
/// </summary>
[Serializable]
[GenerateSerializer]
public enum SessionLifecycleState
{
    /// <summary>
    /// Session has been created but not yet connected.
    /// </summary>
    [Id(0)]
    Initialized,

    /// <summary>
    /// Session is currently connected and active.
    /// </summary>
    [Id(1)]
    Connected,

    /// <summary>
    /// Session is disconnected but can be reconnected.
    /// </summary>
    [Id(2)]
    Disconnected,

    /// <summary>
    /// Session is in the process of reconnecting.
    /// </summary>
    [Id(3)]
    Reconnecting,

    /// <summary>
    /// Session has been archived and cannot be reactivated.
    /// </summary>
    [Id(4)]
    Archived,

    /// <summary>
    /// Session is in an error state.
    /// </summary>
    [Id(5)]
    Error
}

/// <summary>
/// Represents valid state transitions for a session.
/// </summary>
[Serializable]
[GenerateSerializer]
public enum SessionStateTransition
{
    /// <summary>
    /// Initialize a new session.
    /// </summary>
    [Id(0)]
    Initialize,

    /// <summary>
    /// Connect an initialized or disconnected session.
    /// </summary>
    [Id(1)]
    Connect,

    /// <summary>
    /// Disconnect a connected session.
    /// </summary>
    [Id(2)]
    Disconnect,

    /// <summary>
    /// Start reconnection process.
    /// </summary>
    [Id(3)]
    StartReconnect,

    /// <summary>
    /// Complete reconnection process.
    /// </summary>
    [Id(4)]
    CompleteReconnect,

    /// <summary>
    /// Archive the session.
    /// </summary>
    [Id(5)]
    Archive,

    /// <summary>
    /// Mark session as errored.
    /// </summary>
    [Id(6)]
    MarkError,

    /// <summary>
    /// Recover from error state.
    /// </summary>
    [Id(7)]
    RecoverFromError
}

#endregion

#region Session State Models

/// <summary>
/// Request for initializing a new session.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.SessionInitRequest")]
public sealed class SessionInitRequest
{
    /// <summary>
    /// Unique identifier for the session.
    /// </summary>
    [Id(0)]
    [Required]
    public required string SessionId { get; set; }

    /// <summary>
    /// User ID associated with the session.
    /// </summary>
    [Id(1)]
    [Required]
    public required string UserId { get; set; }

    /// <summary>
    /// Client identifier (e.g., device ID, browser ID).
    /// </summary>
    [Id(2)]
    [Required]
    public required string ClientId { get; set; }

    /// <summary>
    /// Protocol type for the session (e.g., "SignalR", "SSE", "WebSocket").
    /// </summary>
    [Id(3)]
    [Required]
    [StringLength(50)]
    public required string ProtocolType { get; set; }

    /// <summary>
    /// Optional chat ID to associate with the session.
    /// </summary>
    [Id(4)]
    public string? ChatId { get; set; }

    /// <summary>
    /// Client IP address for tracking and security.
    /// </summary>
    [Id(5)]
    [StringLength(45)] // Max length for IPv6
    public string? ClientIpAddress { get; set; }

    /// <summary>
    /// User agent string for client identification.
    /// </summary>
    [Id(6)]
    [StringLength(500)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Initial metadata for the session.
    /// </summary>
    [Id(7)]
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Session timeout in seconds (0 for no timeout).
    /// </summary>
    [Id(8)]
    [Range(0, 86400)] // Max 24 hours
    public int TimeoutSeconds { get; set; } = 3600; // Default 1 hour

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    [Id(9)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents the complete state of a session.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.SessionState")]
public sealed class SessionState
{
    /// <summary>
    /// Unique identifier for the session.
    /// </summary>
    [Id(0)]
    public required string SessionId { get; set; }

    /// <summary>
    /// User ID associated with the session.
    /// </summary>
    [Id(1)]
    public required string UserId { get; set; }

    /// <summary>
    /// Client identifier.
    /// </summary>
    [Id(2)]
    public required string ClientId { get; set; }

    /// <summary>
    /// Current state of the session.
    /// </summary>
    [Id(3)]
    public SessionLifecycleState CurrentState { get; set; } = SessionLifecycleState.Initialized;

    /// <summary>
    /// Current connection status.
    /// </summary>
    [Id(4)]
    public required ConnectionStatus ConnectionStatus { get; set; }

    /// <summary>
    /// Current protocol information.
    /// </summary>
    [Id(5)]
    public required SessionProtocolInfo ProtocolInfo { get; set; }

    /// <summary>
    /// Associated chat ID if any.
    /// </summary>
    [Id(6)]
    public string? ChatId { get; set; }

    /// <summary>
    /// Session creation time.
    /// </summary>
    [Id(7)]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last activity timestamp.
    /// </summary>
    [Id(8)]
    public DateTime LastActivityAt { get; set; }

    /// <summary>
    /// Session metadata.
    /// </summary>
    [Id(9)]
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Indicates if the session is archived.
    /// </summary>
    [Id(10)]
    public bool IsArchived { get; set; }

    /// <summary>
    /// Archive timestamp if archived.
    /// </summary>
    [Id(11)]
    public DateTime? ArchivedAt { get; set; }

    /// <summary>
    /// Archive reason if archived.
    /// </summary>
    [Id(12)]
    public string? ArchiveReason { get; set; }

    /// <summary>
    /// Total connection time in seconds.
    /// </summary>
    [Id(13)]
    public long TotalConnectionTimeSeconds { get; set; }

    /// <summary>
    /// Number of reconnection attempts.
    /// </summary>
    [Id(14)]
    public int ReconnectionCount { get; set; }
}

/// <summary>
/// Represents a session event for history tracking.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.SessionEvent")]
public sealed class SessionEvent
{
    /// <summary>
    /// Event identifier.
    /// </summary>
    [Id(0)]
    public required string EventId { get; set; }

    /// <summary>
    /// Session identifier.
    /// </summary>
    [Id(1)]
    public required string SessionId { get; set; }

    /// <summary>
    /// Type of event (e.g., "Connected", "Disconnected", "Reconnected").
    /// </summary>
    [Id(2)]
    public required string EventType { get; set; }

    /// <summary>
    /// Event timestamp.
    /// </summary>
    [Id(3)]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Event details or payload.
    /// </summary>
    [Id(4)]
    public Dictionary<string, string> Details { get; set; } = [];

    /// <summary>
    /// Optional error message if event represents an error.
    /// </summary>
    [Id(5)]
    public string? ErrorMessage { get; set; }
}

#endregion

#region Connection Models

/// <summary>
/// Request for establishing a connection.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ConnectionRequest")]
public sealed class ConnectionRequest
{
    /// <summary>
    /// Authentication token.
    /// </summary>
    [Id(0)]
    [Required]
    public required string AuthToken { get; set; }

    /// <summary>
    /// Connection endpoint or URL.
    /// </summary>
    [Id(1)]
    [Required]
    [StringLength(500)]
    public required string Endpoint { get; set; }

    /// <summary>
    /// Protocol-specific options.
    /// </summary>
    [Id(2)]
    public Dictionary<string, string> ProtocolOptions { get; set; } = [];

    /// <summary>
    /// Requested keep-alive interval in seconds.
    /// </summary>
    [Id(3)]
    [Range(0, 300)]
    public int KeepAliveIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Client capabilities for negotiation.
    /// </summary>
    [Id(4)]
    public List<string> ClientCapabilities { get; set; } = [];

    /// <summary>
    /// Request timestamp.
    /// </summary>
    [Id(5)]
    public DateTime RequestTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents an established session connection.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.SessionConnection")]
public sealed class SessionConnection
{
    /// <summary>
    /// Connection identifier.
    /// </summary>
    [Id(0)]
    public required string ConnectionId { get; set; }

    /// <summary>
    /// Session identifier.
    /// </summary>
    [Id(1)]
    public required string SessionId { get; set; }

    /// <summary>
    /// Connection state.
    /// </summary>
    [Id(2)]
    public required string State { get; set; } // "Connected", "Disconnected", "Reconnecting"

    /// <summary>
    /// Connection establishment time.
    /// </summary>
    [Id(3)]
    public DateTime ConnectedAt { get; set; }

    /// <summary>
    /// Last activity time.
    /// </summary>
    [Id(4)]
    public DateTime LastActivityAt { get; set; }

    /// <summary>
    /// Connection endpoint.
    /// </summary>
    [Id(5)]
    public required string Endpoint { get; set; }

    /// <summary>
    /// Negotiated keep-alive interval.
    /// </summary>
    [Id(6)]
    public int KeepAliveIntervalSeconds { get; set; }

    /// <summary>
    /// Connection quality metrics.
    /// </summary>
    [Id(7)]
    public ConnectionQuality Quality { get; set; } = new ConnectionQuality();
}

/// <summary>
/// Connection quality metrics.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ConnectionQuality")]
public sealed class ConnectionQuality
{
    /// <summary>
    /// Average latency in milliseconds.
    /// </summary>
    [Id(0)]
    public double LatencyMs { get; set; }

    /// <summary>
    /// Packet loss percentage.
    /// </summary>
    [Id(1)]
    [Range(0, 100)]
    public double PacketLossPercent { get; set; }

    /// <summary>
    /// Connection stability score (0-100).
    /// </summary>
    [Id(2)]
    [Range(0, 100)]
    public int StabilityScore { get; set; } = 100;

    /// <summary>
    /// Last measurement time.
    /// </summary>
    [Id(3)]
    public DateTime LastMeasuredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Request for reconnecting a session.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ReconnectionRequest")]
public sealed class ReconnectionRequest
{
    /// <summary>
    /// Previous connection ID for state recovery.
    /// </summary>
    [Id(0)]
    [Required]
    public required string PreviousConnectionId { get; set; }

    /// <summary>
    /// Authentication token for reconnection.
    /// </summary>
    [Id(1)]
    [Required]
    public required string AuthToken { get; set; }

    /// <summary>
    /// Last known sequence number for message recovery.
    /// </summary>
    [Id(2)]
    public long? LastSequenceNumber { get; set; }

    /// <summary>
    /// Whether to restore previous state.
    /// </summary>
    [Id(3)]
    public bool RestoreState { get; set; } = true;

    /// <summary>
    /// Reconnection attempt number.
    /// </summary>
    [Id(4)]
    public int AttemptNumber { get; set; } = 1;

    /// <summary>
    /// Request timestamp.
    /// </summary>
    [Id(5)]
    public DateTime RequestTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a connection attempt for tracking.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ConnectionAttempt")]
public sealed class ConnectionAttempt
{
    /// <summary>
    /// Attempt identifier.
    /// </summary>
    [Id(0)]
    public required string AttemptId { get; set; }

    /// <summary>
    /// Session identifier.
    /// </summary>
    [Id(1)]
    public required string SessionId { get; set; }

    /// <summary>
    /// Attempt timestamp.
    /// </summary>
    [Id(2)]
    public DateTime AttemptTime { get; set; }

    /// <summary>
    /// Whether the attempt succeeded.
    /// </summary>
    [Id(3)]
    public bool Success { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    [Id(4)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Connection duration in milliseconds.
    /// </summary>
    [Id(5)]
    public long DurationMs { get; set; }

    /// <summary>
    /// Client IP address.
    /// </summary>
    [Id(6)]
    public string? ClientIpAddress { get; set; }
}

/// <summary>
/// Current connection status information.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ConnectionStatus")]
public sealed class ConnectionStatus
{
    /// <summary>
    /// Connection state.
    /// </summary>
    [Id(0)]
    public required string State { get; set; } // "Connected", "Disconnected", "Reconnecting", "Failed"

    /// <summary>
    /// Whether the connection is active.
    /// </summary>
    [Id(1)]
    public bool IsConnected { get; set; }

    /// <summary>
    /// Last connection time.
    /// </summary>
    [Id(2)]
    public DateTime? LastConnectedAt { get; set; }

    /// <summary>
    /// Last disconnection time.
    /// </summary>
    [Id(3)]
    public DateTime? LastDisconnectedAt { get; set; }

    /// <summary>
    /// Disconnection reason if applicable.
    /// </summary>
    [Id(4)]
    public string? DisconnectionReason { get; set; }

    /// <summary>
    /// Current connection ID if connected.
    /// </summary>
    [Id(5)]
    public string? CurrentConnectionId { get; set; }

    /// <summary>
    /// Connection uptime in seconds.
    /// </summary>
    [Id(6)]
    public long UptimeSeconds { get; set; }
}

/// <summary>
/// Connection configuration settings.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ConnectionConfiguration")]
public sealed class ConnectionConfiguration
{
    /// <summary>
    /// Timeout for connection establishment in seconds.
    /// </summary>
    [Id(0)]
    [Range(1, 300)]
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Keep-alive interval in seconds.
    /// </summary>
    [Id(1)]
    [Range(1, 300)]
    public int KeepAliveIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum reconnection attempts.
    /// </summary>
    [Id(2)]
    [Range(0, 100)]
    public int MaxReconnectionAttempts { get; set; } = 5;

    /// <summary>
    /// Reconnection delay in seconds.
    /// </summary>
    [Id(3)]
    [Range(0, 60)]
    public int ReconnectionDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Enable automatic reconnection.
    /// </summary>
    [Id(4)]
    public bool EnableAutoReconnect { get; set; } = true;

    /// <summary>
    /// Buffer size for messages.
    /// </summary>
    [Id(5)]
    [Range(1024, 10485760)] // 1KB to 10MB
    public int BufferSize { get; set; } = 65536; // 64KB default
}

/// <summary>
/// Heartbeat response information.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.HeartbeatResponse")]
public sealed class HeartbeatResponse
{
    /// <summary>
    /// Heartbeat acknowledgment time.
    /// </summary>
    [Id(0)]
    public DateTime AcknowledgedAt { get; set; }

    /// <summary>
    /// Round-trip latency in milliseconds.
    /// </summary>
    [Id(1)]
    public double LatencyMs { get; set; }

    /// <summary>
    /// Server timestamp for time synchronization.
    /// </summary>
    [Id(2)]
    public DateTime ServerTime { get; set; }

    /// <summary>
    /// Session uptime in seconds.
    /// </summary>
    [Id(3)]
    public long SessionUptimeSeconds { get; set; }
}

/// <summary>
/// Connection metrics and statistics.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ConnectionMetrics")]
public sealed class ConnectionMetrics
{
    /// <summary>
    /// Total bytes sent.
    /// </summary>
    [Id(0)]
    public long BytesSent { get; set; }

    /// <summary>
    /// Total bytes received.
    /// </summary>
    [Id(1)]
    public long BytesReceived { get; set; }

    /// <summary>
    /// Total messages sent.
    /// </summary>
    [Id(2)]
    public long MessagesSent { get; set; }

    /// <summary>
    /// Total messages received.
    /// </summary>
    [Id(3)]
    public long MessagesReceived { get; set; }

    /// <summary>
    /// Average latency in milliseconds.
    /// </summary>
    [Id(4)]
    public double AverageLatencyMs { get; set; }

    /// <summary>
    /// Maximum latency in milliseconds.
    /// </summary>
    [Id(5)]
    public double MaxLatencyMs { get; set; }

    /// <summary>
    /// Minimum latency in milliseconds.
    /// </summary>
    [Id(6)]
    public double MinLatencyMs { get; set; }

    /// <summary>
    /// Error count.
    /// </summary>
    [Id(7)]
    public long ErrorCount { get; set; }

    /// <summary>
    /// Reconnection count.
    /// </summary>
    [Id(8)]
    public int ReconnectionCount { get; set; }

    /// <summary>
    /// Metrics collection period.
    /// </summary>
    [Id(9)]
    public DateTime MetricsPeriodStart { get; set; }

    /// <summary>
    /// Metrics collection end time.
    /// </summary>
    [Id(10)]
    public DateTime MetricsPeriodEnd { get; set; }
}

#endregion

#region Protocol Models

/// <summary>
/// Protocol configuration settings.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ProtocolConfiguration")]
public sealed class ProtocolConfiguration
{
    /// <summary>
    /// Protocol type (e.g., "SignalR", "SSE", "WebSocket").
    /// </summary>
    [Id(0)]
    [Required]
    [StringLength(50)]
    public required string ProtocolType { get; set; }

    /// <summary>
    /// Protocol version.
    /// </summary>
    [Id(1)]
    [StringLength(20)]
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Protocol-specific settings.
    /// </summary>
    [Id(2)]
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>
    /// Supported features for this protocol.
    /// </summary>
    [Id(3)]
    public List<string> SupportedFeatures { get; set; } = [];

    /// <summary>
    /// Compression settings.
    /// </summary>
    [Id(4)]
    public CompressionSettings? Compression { get; set; }

    /// <summary>
    /// Encryption settings.
    /// </summary>
    [Id(5)]
    public EncryptionSettings? Encryption { get; set; }
}

/// <summary>
/// Compression settings for protocol.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.CompressionSettings")]
public sealed class CompressionSettings
{
    /// <summary>
    /// Enable compression.
    /// </summary>
    [Id(0)]
    public bool Enabled { get; set; }

    /// <summary>
    /// Compression algorithm.
    /// </summary>
    [Id(1)]
    [StringLength(50)]
    public string Algorithm { get; set; } = "gzip";

    /// <summary>
    /// Compression level (1-9).
    /// </summary>
    [Id(2)]
    [Range(1, 9)]
    public int Level { get; set; } = 5;
}

/// <summary>
/// Encryption settings for protocol.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.EncryptionSettings")]
public sealed class EncryptionSettings
{
    /// <summary>
    /// Enable encryption.
    /// </summary>
    [Id(0)]
    public bool Enabled { get; set; }

    /// <summary>
    /// Encryption algorithm.
    /// </summary>
    [Id(1)]
    [StringLength(50)]
    public string Algorithm { get; set; } = "AES256";

    /// <summary>
    /// Key exchange method.
    /// </summary>
    [Id(2)]
    [StringLength(50)]
    public string KeyExchange { get; set; } = "ECDH";
}

/// <summary>
/// Session protocol information.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.SessionProtocolInfo")]
public sealed class SessionProtocolInfo
{
    /// <summary>
    /// Current protocol type.
    /// </summary>
    [Id(0)]
    public required string ProtocolType { get; set; }

    /// <summary>
    /// Protocol version.
    /// </summary>
    [Id(1)]
    public required string Version { get; set; }

    /// <summary>
    /// Negotiated capabilities.
    /// </summary>
    [Id(2)]
    public List<string> NegotiatedCapabilities { get; set; } = [];

    /// <summary>
    /// Protocol state.
    /// </summary>
    [Id(3)]
    public required ProtocolState State { get; set; }

    /// <summary>
    /// Protocol configuration.
    /// </summary>
    [Id(4)]
    public required ProtocolConfiguration Configuration { get; set; }
}

/// <summary>
/// Protocol state information.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ProtocolState")]
public sealed class ProtocolState
{
    /// <summary>
    /// State name.
    /// </summary>
    [Id(0)]
    public required string StateName { get; set; }

    /// <summary>
    /// State data.
    /// </summary>
    [Id(1)]
    public Dictionary<string, string> StateData { get; set; } = [];

    /// <summary>
    /// Last state change time.
    /// </summary>
    [Id(2)]
    public DateTime LastStateChange { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the protocol is ready.
    /// </summary>
    [Id(3)]
    public bool IsReady { get; set; }
}

/// <summary>
/// Protocol switch request.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ProtocolSwitchRequest")]
public sealed class ProtocolSwitchRequest
{
    /// <summary>
    /// Target protocol type.
    /// </summary>
    [Id(0)]
    [Required]
    [StringLength(50)]
    public required string TargetProtocol { get; set; }

    /// <summary>
    /// Reason for switching.
    /// </summary>
    [Id(1)]
    [StringLength(500)]
    public string? Reason { get; set; }

    /// <summary>
    /// Protocol-specific options.
    /// </summary>
    [Id(2)]
    public Dictionary<string, string> Options { get; set; } = [];

    /// <summary>
    /// Whether to maintain state during switch.
    /// </summary>
    [Id(3)]
    public bool MaintainState { get; set; } = true;

    /// <summary>
    /// Request timestamp.
    /// </summary>
    [Id(4)]
    public DateTime RequestTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Protocol validation result.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ProtocolValidationResult")]
public sealed class ProtocolValidationResult
{
    /// <summary>
    /// Whether the protocol is valid.
    /// </summary>
    [Id(0)]
    public bool IsValid { get; set; }

    /// <summary>
    /// Whether the protocol is supported.
    /// </summary>
    [Id(1)]
    public bool IsSupported { get; set; }

    /// <summary>
    /// Whether the protocol is compatible with current session.
    /// </summary>
    [Id(2)]
    public bool IsCompatible { get; set; }

    /// <summary>
    /// Validation messages.
    /// </summary>
    [Id(3)]
    public List<string> ValidationMessages { get; set; } = [];

    /// <summary>
    /// Required features that are missing.
    /// </summary>
    [Id(4)]
    public List<string> MissingFeatures { get; set; } = [];
}

/// <summary>
/// Protocol capabilities for negotiation.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ProtocolCapabilities")]
public sealed class ProtocolCapabilities
{
    /// <summary>
    /// List of supported protocols.
    /// </summary>
    [Id(0)]
    public List<string> SupportedProtocols { get; set; } = [];

    /// <summary>
    /// List of supported features.
    /// </summary>
    [Id(1)]
    public List<string> SupportedFeatures { get; set; } = [];

    /// <summary>
    /// Maximum message size in bytes.
    /// </summary>
    [Id(2)]
    public long MaxMessageSize { get; set; } = 1048576; // 1MB default

    /// <summary>
    /// Supported compression algorithms.
    /// </summary>
    [Id(3)]
    public List<string> CompressionAlgorithms { get; set; } = [];

    /// <summary>
    /// Supported encryption algorithms.
    /// </summary>
    [Id(4)]
    public List<string> EncryptionAlgorithms { get; set; } = [];
}

/// <summary>
/// Protocol information.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ProtocolInfo")]
public sealed class ProtocolInfo
{
    /// <summary>
    /// Protocol name.
    /// </summary>
    [Id(0)]
    public required string Name { get; set; }

    /// <summary>
    /// Protocol version.
    /// </summary>
    [Id(1)]
    public required string Version { get; set; }

    /// <summary>
    /// Protocol description.
    /// </summary>
    [Id(2)]
    public string? Description { get; set; }

    /// <summary>
    /// Whether the protocol is available.
    /// </summary>
    [Id(3)]
    public bool IsAvailable { get; set; }

    /// <summary>
    /// Protocol configuration.
    /// </summary>
    [Id(4)]
    public ProtocolConfiguration? DefaultConfiguration { get; set; }
}

/// <summary>
/// Protocol message for handling.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ProtocolMessage")]
public sealed class ProtocolMessage
{
    /// <summary>
    /// Message identifier.
    /// </summary>
    [Id(0)]
    public required string MessageId { get; set; }

    /// <summary>
    /// Message type.
    /// </summary>
    [Id(1)]
    public required string MessageType { get; set; }

    /// <summary>
    /// Message payload.
    /// </summary>
    [Id(2)]
    public required byte[] Payload { get; set; }

    /// <summary>
    /// Message headers.
    /// </summary>
    [Id(3)]
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>
    /// Message timestamp.
    /// </summary>
    [Id(4)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Protocol response.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ProtocolResponse")]
public sealed class ProtocolResponse
{
    /// <summary>
    /// Response identifier.
    /// </summary>
    [Id(0)]
    public required string ResponseId { get; set; }

    /// <summary>
    /// Original message ID.
    /// </summary>
    [Id(1)]
    public required string MessageId { get; set; }

    /// <summary>
    /// Response status.
    /// </summary>
    [Id(2)]
    public required string Status { get; set; } // "Success", "Error", "Pending"

    /// <summary>
    /// Response payload.
    /// </summary>
    [Id(3)]
    public byte[]? Payload { get; set; }

    /// <summary>
    /// Error message if applicable.
    /// </summary>
    [Id(4)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Response timestamp.
    /// </summary>
    [Id(5)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

#endregion

#region Monitoring Models

/// <summary>
/// Session health check result.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.SessionHealthCheck")]
public sealed class SessionHealthCheck
{
    /// <summary>
    /// Health status.
    /// </summary>
    [Id(0)]
    public required string Status { get; set; } // "Healthy", "Degraded", "Unhealthy"

    /// <summary>
    /// Health check timestamp.
    /// </summary>
    [Id(1)]
    public DateTime CheckTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Individual health checks.
    /// </summary>
    [Id(2)]
    public List<HealthCheckItem> Checks { get; set; } = [];

    /// <summary>
    /// Overall health score (0-100).
    /// </summary>
    [Id(3)]
    [Range(0, 100)]
    public int HealthScore { get; set; }

    /// <summary>
    /// Health check message.
    /// </summary>
    [Id(4)]
    public string? Message { get; set; }
}

/// <summary>
/// Individual health check item.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.HealthCheckItem")]
public sealed class HealthCheckItem
{
    /// <summary>
    /// Check name.
    /// </summary>
    [Id(0)]
    public required string Name { get; set; }

    /// <summary>
    /// Check status.
    /// </summary>
    [Id(1)]
    public required string Status { get; set; }

    /// <summary>
    /// Check message.
    /// </summary>
    [Id(2)]
    public string? Message { get; set; }

    /// <summary>
    /// Check duration in milliseconds.
    /// </summary>
    [Id(3)]
    public long DurationMs { get; set; }
}

/// <summary>
/// Session metrics information.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.SessionMetrics")]
public sealed class SessionMetrics
{
    /// <summary>
    /// Session identifier.
    /// </summary>
    [Id(0)]
    public required string SessionId { get; set; }

    /// <summary>
    /// Connection metrics.
    /// </summary>
    [Id(1)]
    public required ConnectionMetrics ConnectionMetrics { get; set; }

    /// <summary>
    /// Resource usage.
    /// </summary>
    [Id(2)]
    public required ResourceUsage ResourceUsage { get; set; }

    /// <summary>
    /// Performance statistics.
    /// </summary>
    [Id(3)]
    public required PerformanceStats PerformanceStats { get; set; }

    /// <summary>
    /// Custom metrics.
    /// </summary>
    [Id(4)]
    public Dictionary<string, double> CustomMetrics { get; set; } = [];

    /// <summary>
    /// Metrics collection time.
    /// </summary>
    [Id(5)]
    public DateTime CollectionTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Performance statistics.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.PerformanceStats")]
public sealed class PerformanceStats
{
    /// <summary>
    /// Average response time in milliseconds.
    /// </summary>
    [Id(0)]
    public double AverageResponseTimeMs { get; set; }

    /// <summary>
    /// 95th percentile response time.
    /// </summary>
    [Id(1)]
    public double P95ResponseTimeMs { get; set; }

    /// <summary>
    /// 99th percentile response time.
    /// </summary>
    [Id(2)]
    public double P99ResponseTimeMs { get; set; }

    /// <summary>
    /// Throughput (messages per second).
    /// </summary>
    [Id(3)]
    public double ThroughputPerSecond { get; set; }

    /// <summary>
    /// Error rate percentage.
    /// </summary>
    [Id(4)]
    [Range(0, 100)]
    public double ErrorRatePercent { get; set; }

    /// <summary>
    /// Total request count.
    /// </summary>
    [Id(5)]
    public long TotalRequests { get; set; }

    /// <summary>
    /// Statistics period start.
    /// </summary>
    [Id(6)]
    public DateTime PeriodStart { get; set; }

    /// <summary>
    /// Statistics period end.
    /// </summary>
    [Id(7)]
    public DateTime PeriodEnd { get; set; }
}

/// <summary>
/// Diagnostic report for troubleshooting.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.DiagnosticReport")]
public sealed class DiagnosticReport
{
    /// <summary>
    /// Report identifier.
    /// </summary>
    [Id(0)]
    public required string ReportId { get; set; }

    /// <summary>
    /// Session identifier.
    /// </summary>
    [Id(1)]
    public required string SessionId { get; set; }

    /// <summary>
    /// Diagnostic level.
    /// </summary>
    [Id(2)]
    public required DiagnosticLevel Level { get; set; }

    /// <summary>
    /// Report generation time.
    /// </summary>
    [Id(3)]
    public DateTime GeneratedAt { get; set; }

    /// <summary>
    /// Session state snapshot.
    /// </summary>
    [Id(4)]
    public SessionState? StateSnapshot { get; set; }

    /// <summary>
    /// Recent events.
    /// </summary>
    [Id(5)]
    public List<SessionEvent> RecentEvents { get; set; } = [];

    /// <summary>
    /// Current metrics.
    /// </summary>
    [Id(6)]
    public SessionMetrics? Metrics { get; set; }

    /// <summary>
    /// Error logs.
    /// </summary>
    [Id(7)]
    public List<string> ErrorLogs { get; set; } = [];

    /// <summary>
    /// Configuration snapshot.
    /// </summary>
    [Id(8)]
    public Dictionary<string, string> Configuration { get; set; } = [];

    /// <summary>
    /// Additional diagnostic data.
    /// </summary>
    [Id(9)]
    public Dictionary<string, string> AdditionalData { get; set; } = [];
}

/// <summary>
/// Diagnostic level enumeration.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.DiagnosticLevel")]
public enum DiagnosticLevel
{
    /// <summary>
    /// Basic diagnostic information.
    /// </summary>
    [Id(0)]
    Basic = 0,

    /// <summary>
    /// Standard diagnostic information.
    /// </summary>
    [Id(1)]
    Standard = 1,

    /// <summary>
    /// Detailed diagnostic information.
    /// </summary>
    [Id(2)]
    Detailed = 2,

    /// <summary>
    /// Full diagnostic information including sensitive data.
    /// </summary>
    [Id(3)]
    Full = 3
}

/// <summary>
/// Alert configuration for monitoring.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.AlertConfiguration")]
public sealed class AlertConfiguration
{
    /// <summary>
    /// Alert name.
    /// </summary>
    [Id(0)]
    [Required]
    [StringLength(100)]
    public required string Name { get; set; }

    /// <summary>
    /// Alert condition expression.
    /// </summary>
    [Id(1)]
    [Required]
    public required string Condition { get; set; }

    /// <summary>
    /// Alert severity.
    /// </summary>
    [Id(2)]
    public required AlertSeverity Severity { get; set; }

    /// <summary>
    /// Alert threshold value.
    /// </summary>
    [Id(3)]
    public double Threshold { get; set; }

    /// <summary>
    /// Evaluation period in seconds.
    /// </summary>
    [Id(4)]
    [Range(1, 3600)]
    public int EvaluationPeriodSeconds { get; set; } = 60;

    /// <summary>
    /// Alert actions to take.
    /// </summary>
    [Id(5)]
    public List<string> Actions { get; set; } = [];

    /// <summary>
    /// Alert metadata.
    /// </summary>
    [Id(6)]
    public Dictionary<string, string> Metadata { get; set; } = [];
}

/// <summary>
/// Alert severity enumeration.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.AlertSeverity")]
public enum AlertSeverity
{
    /// <summary>
    /// Informational alert.
    /// </summary>
    [Id(0)]
    Info = 0,

    /// <summary>
    /// Warning alert.
    /// </summary>
    [Id(1)]
    Warning = 1,

    /// <summary>
    /// Error alert.
    /// </summary>
    [Id(2)]
    Error = 2,

    /// <summary>
    /// Critical alert.
    /// </summary>
    [Id(3)]
    Critical = 3
}

/// <summary>
/// Active session alert.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.SessionAlert")]
public sealed class SessionAlert
{
    /// <summary>
    /// Alert identifier.
    /// </summary>
    [Id(0)]
    public required string AlertId { get; set; }

    /// <summary>
    /// Alert name.
    /// </summary>
    [Id(1)]
    public required string Name { get; set; }

    /// <summary>
    /// Alert severity.
    /// </summary>
    [Id(2)]
    public required AlertSeverity Severity { get; set; }

    /// <summary>
    /// Alert message.
    /// </summary>
    [Id(3)]
    public required string Message { get; set; }

    /// <summary>
    /// Alert trigger time.
    /// </summary>
    [Id(4)]
    public DateTime TriggeredAt { get; set; }

    /// <summary>
    /// Whether the alert is acknowledged.
    /// </summary>
    [Id(5)]
    public bool IsAcknowledged { get; set; }

    /// <summary>
    /// Acknowledgment time.
    /// </summary>
    [Id(6)]
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>
    /// Who acknowledged the alert.
    /// </summary>
    [Id(7)]
    public string? AcknowledgedBy { get; set; }

    /// <summary>
    /// Alert details.
    /// </summary>
    [Id(8)]
    public Dictionary<string, string> Details { get; set; } = [];
}

/// <summary>
/// Performance trace data.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.TraceData")]
public sealed class TraceData
{
    /// <summary>
    /// Trace identifier.
    /// </summary>
    [Id(0)]
    public required string TraceId { get; set; }

    /// <summary>
    /// Trace name.
    /// </summary>
    [Id(1)]
    public required string TraceName { get; set; }

    /// <summary>
    /// Trace start time.
    /// </summary>
    [Id(2)]
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Trace end time.
    /// </summary>
    [Id(3)]
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Trace duration in milliseconds.
    /// </summary>
    [Id(4)]
    public long DurationMs { get; set; }

    /// <summary>
    /// Trace spans.
    /// </summary>
    [Id(5)]
    public List<TraceSpan> Spans { get; set; } = [];

    /// <summary>
    /// Trace metadata.
    /// </summary>
    [Id(6)]
    public Dictionary<string, string> Metadata { get; set; } = [];
}

/// <summary>
/// Individual trace span.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.TraceSpan")]
public sealed class TraceSpan
{
    /// <summary>
    /// Span name.
    /// </summary>
    [Id(0)]
    public required string Name { get; set; }

    /// <summary>
    /// Span start time.
    /// </summary>
    [Id(1)]
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Span duration in milliseconds.
    /// </summary>
    [Id(2)]
    public long DurationMs { get; set; }

    /// <summary>
    /// Span attributes.
    /// </summary>
    [Id(3)]
    public Dictionary<string, string> Attributes { get; set; } = [];
}

/// <summary>
/// Resource usage information.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ResourceUsage")]
public sealed class ResourceUsage
{
    /// <summary>
    /// Memory usage in bytes.
    /// </summary>
    [Id(0)]
    public long MemoryBytes { get; set; }

    /// <summary>
    /// CPU usage percentage.
    /// </summary>
    [Id(1)]
    [Range(0, 100)]
    public double CpuPercent { get; set; }

    /// <summary>
    /// Network bandwidth usage in bytes per second.
    /// </summary>
    [Id(2)]
    public long NetworkBandwidthBytesPerSecond { get; set; }

    /// <summary>
    /// Active thread count.
    /// </summary>
    [Id(3)]
    public int ThreadCount { get; set; }

    /// <summary>
    /// Handle count.
    /// </summary>
    [Id(4)]
    public int HandleCount { get; set; }

    /// <summary>
    /// Measurement timestamp.
    /// </summary>
    [Id(5)]
    public DateTime MeasuredAt { get; set; } = DateTime.UtcNow;
}

#endregion

#region Operation Results

/// <summary>
/// Represents the result of a session operation with detailed context.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.SessionOperationResult")]
public sealed class SessionOperationResult<T>
{
    /// <summary>
    /// Indicates whether the operation was successful.
    /// </summary>
    [Id(0)]
    public bool Success { get; set; }

    /// <summary>
    /// The result data if successful.
    /// </summary>
    [Id(1)]
    public T? Data { get; set; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    [Id(2)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error code if the operation failed.
    /// </summary>
    [Id(3)]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Timestamp when the operation was performed.
    /// </summary>
    [Id(4)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Duration of the operation.
    /// </summary>
    [Id(5)]
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Correlation ID for tracking the operation.
    /// </summary>
    [Id(6)]
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Additional context or metadata about the operation.
    /// </summary>
    [Id(7)]
    public Dictionary<string, string> Context { get; set; } = [];
}

/// <summary>
/// Request for transitioning session state.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.SessionStateTransitionRequest")]
public sealed class SessionStateTransitionRequest
{
    /// <summary>
    /// The transition to perform.
    /// </summary>
    [Id(0)]
    [Required]
    public SessionStateTransition Transition { get; set; }

    /// <summary>
    /// Optional reason for the transition.
    /// </summary>
    [Id(1)]
    [StringLength(500)]
    public string? Reason { get; set; }

    /// <summary>
    /// Optional metadata for the transition.
    /// </summary>
    [Id(2)]
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Timestamp of the request.
    /// </summary>
    [Id(3)]
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}

#endregion

#region Validation Helpers

/// <summary>
/// Provides validation methods for session-related operations.
/// </summary>
public static class SessionValidation
{
    /// <summary>
    /// Valid state transitions map.
    /// </summary>
    private static readonly Dictionary<SessionLifecycleState, HashSet<SessionLifecycleState>> ValidTransitions = new()
    {
        [SessionLifecycleState.Initialized] = [SessionLifecycleState.Connected, SessionLifecycleState.Archived, SessionLifecycleState.Error],
        [SessionLifecycleState.Connected] = [SessionLifecycleState.Disconnected, SessionLifecycleState.Archived, SessionLifecycleState.Error],
        [SessionLifecycleState.Disconnected] = [SessionLifecycleState.Connected, SessionLifecycleState.Reconnecting, SessionLifecycleState.Archived, SessionLifecycleState.Error],
        [SessionLifecycleState.Reconnecting] = [SessionLifecycleState.Connected, SessionLifecycleState.Disconnected, SessionLifecycleState.Error, SessionLifecycleState.Archived],
        [SessionLifecycleState.Error] = [SessionLifecycleState.Disconnected, SessionLifecycleState.Archived],
        [SessionLifecycleState.Archived] = [] // No transitions from Archived
    };

    /// <summary>
    /// Validates if a state transition is allowed.
    /// </summary>
    /// <param name="currentState">Current session state.</param>
    /// <param name="newState">Desired new state.</param>
    /// <returns>True if transition is valid, false otherwise.</returns>
    public static bool IsValidTransition(SessionLifecycleState currentState, SessionLifecycleState newState)
    {
        if (currentState == newState)
        {
            return true; // Same state is always valid
        }

        return ValidTransitions.TryGetValue(currentState, out var validStates) && validStates.Contains(newState);
    }

    /// <summary>
    /// Gets the new state for a given transition.
    /// </summary>
    /// <param name="currentState">Current session state.</param>
    /// <param name="transition">The transition to apply.</param>
    /// <returns>The new state, or null if transition is invalid.</returns>
    public static SessionLifecycleState? GetNewState(SessionLifecycleState currentState, SessionStateTransition transition)
    {
        return transition switch
        {
            SessionStateTransition.Initialize => SessionLifecycleState.Initialized,
            SessionStateTransition.Connect when IsValidTransition(currentState, SessionLifecycleState.Connected) => SessionLifecycleState.Connected,
            SessionStateTransition.Disconnect when IsValidTransition(currentState, SessionLifecycleState.Disconnected) => SessionLifecycleState.Disconnected,
            SessionStateTransition.StartReconnect when IsValidTransition(currentState, SessionLifecycleState.Reconnecting) => SessionLifecycleState.Reconnecting,
            SessionStateTransition.CompleteReconnect when currentState == SessionLifecycleState.Reconnecting => SessionLifecycleState.Connected,
            SessionStateTransition.Archive when IsValidTransition(currentState, SessionLifecycleState.Archived) => SessionLifecycleState.Archived,
            SessionStateTransition.MarkError when IsValidTransition(currentState, SessionLifecycleState.Error) => SessionLifecycleState.Error,
            SessionStateTransition.RecoverFromError when currentState == SessionLifecycleState.Error => SessionLifecycleState.Disconnected,
            _ => null
        };
    }

    /// <summary>
    /// Validates a session ID format.
    /// </summary>
    /// <param name="sessionId">The session ID to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    public static bool IsValidSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        // Session ID should be between 8 and 128 characters
        // and contain only alphanumeric characters, hyphens, and underscores
        if (sessionId.Length is < 8 or > 128)
        {
            return false;
        }

        return sessionId.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    /// <summary>
    /// Validates a protocol type.
    /// </summary>
    /// <param name="protocol">The protocol to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    public static bool IsValidProtocol(string? protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol))
        {
            return false;
        }

        var validProtocols = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SignalR", "SSE", "WebSocket", "HTTP", "GRPC"
        };

        return validProtocols.Contains(protocol);
    }

    /// <summary>
    /// Validates a connection request.
    /// </summary>
    /// <param name="request">The connection request to validate.</param>
    /// <param name="errors">Output list of validation errors.</param>
    /// <returns>True if valid, false otherwise.</returns>
    public static bool ValidateConnectionRequest(ConnectionRequest request, out List<string> errors)
    {
        errors = [];

        if (request == null)
        {
            errors.Add("Connection request cannot be null.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.AuthToken))
        {
            errors.Add("Authentication token is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Endpoint))
        {
            errors.Add("Connection endpoint is required.");
        }

        return errors.Count == 0;
    }
}

#endregion
