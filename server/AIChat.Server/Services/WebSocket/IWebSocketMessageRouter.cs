using AIChat.Server.Models.WebSocket;

namespace AIChat.Server.Services.WebSocket;

/// <summary>
/// Interface for routing WebSocket messages through Orleans grains or direct services.
/// Implements the dual-mode routing pattern for seamless Orleans integration with fallback capabilities.
/// </summary>
public interface IWebSocketMessageRouter
{
    /// <summary>
    /// Routes an incoming WebSocket message to the appropriate handler.
    /// Uses dual-mode routing to try Orleans grains first, then fallback to direct services.
    /// </summary>
    /// <param name="message">The WebSocket message to route</param>
    /// <param name="sessionInfo">Information about the session sending the message</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of message processing</returns>
    Task<MessageRoutingResult> RouteMessageAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Routes a message to a specific destination.
    /// Allows explicit control over routing destination.
    /// </summary>
    /// <param name="message">The WebSocket message to route</param>
    /// <param name="destination">The routing destination</param>
    /// <param name="sessionInfo">Information about the session</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of message processing</returns>
    Task<MessageRoutingResult> RouteToDestinationAsync(
        WebSocketMessage message,
        MessageRoutingDestination destination,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles protocol-specific message routing.
    /// Routes messages based on the negotiated protocol.
    /// </summary>
    /// <param name="message">The WebSocket message to route</param>
    /// <param name="protocol">The protocol being used</param>
    /// <param name="sessionInfo">Information about the session</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of message processing</returns>
    Task<MessageRoutingResult> RouteProtocolMessageAsync(
        WebSocketMessage message,
        string protocol,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts a message to multiple sessions.
    /// Supports group messaging and notifications.
    /// </summary>
    /// <param name="message">The message to broadcast</param>
    /// <param name="targetSessions">The sessions to send the message to</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Results of broadcasting to each session</returns>
    Task<List<MessageRoutingResult>> BroadcastMessageAsync(
        WebSocketMessage message,
        IEnumerable<string> targetSessions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles heartbeat message routing.
    /// Special handling for connection health maintenance.
    /// </summary>
    /// <param name="sessionInfo">Information about the session sending the heartbeat</param>
    /// <param name="heartbeatData">Optional heartbeat data</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of heartbeat processing</returns>
    Task<MessageRoutingResult> RouteHeartbeatAsync(
        WebSocketSessionInfo sessionInfo,
        Dictionary<string, object>? heartbeatData = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a custom message handler for specific message types.
    /// Allows extension of routing capabilities.
    /// </summary>
    /// <param name="messageType">The message type to handle</param>
    /// <param name="handler">The handler function</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the handler was registered successfully</returns>
    Task<bool> RegisterMessageHandlerAsync(
        string messageType,
        Func<WebSocketMessage, WebSocketSessionInfo, CancellationToken, Task<MessageRoutingResult>> handler,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters a custom message handler.
    /// </summary>
    /// <param name="messageType">The message type to unregister</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the handler was unregistered successfully</returns>
    Task<bool> UnregisterMessageHandlerAsync(
        string messageType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets routing statistics and metrics.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Message routing statistics</returns>
    Task<MessageRoutingStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if Orleans routing is currently available.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if Orleans routing is available and healthy</returns>
    Task<bool> IsOrleansRoutingAvailableAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the health status of the message router.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Router health status</returns>
    Task<MessageRouterHealthStatus> GetHealthStatusAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of message routing.
/// </summary>
public record MessageRoutingResult
{
    /// <summary>
    /// Gets whether the message was successfully routed and processed.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the routing destination that was used.
    /// </summary>
    public MessageRoutingDestination Destination { get; init; }

    /// <summary>
    /// Gets any response message from the handler.
    /// </summary>
    public WebSocketMessage? ResponseMessage { get; init; }

    /// <summary>
    /// Gets any error message if routing failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets additional routing metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// Gets the time taken to process the message in milliseconds.
    /// </summary>
    public double ProcessingTimeMs { get; init; }

    /// <summary>
    /// Gets whether Orleans routing was attempted.
    /// </summary>
    public bool OrleansAttempted { get; init; }

    /// <summary>
    /// Gets whether direct service routing was used.
    /// </summary>
    public bool DirectServiceUsed { get; init; }

    /// <summary>
    /// Creates a successful routing result.
    /// </summary>
    /// <param name="destination">The destination that processed the message</param>
    /// <param name="responseMessage">Optional response message</param>
    /// <param name="processingTimeMs">Time taken to process</param>
    /// <param name="orleansAttempted">Whether Orleans was attempted</param>
    /// <param name="directServiceUsed">Whether direct service was used</param>
    /// <returns>A successful message routing result</returns>
    public static MessageRoutingResult CreateSuccess(
        MessageRoutingDestination destination,
        WebSocketMessage? responseMessage = null,
        double processingTimeMs = 0,
        bool orleansAttempted = false,
        bool directServiceUsed = false)
    {
        return new MessageRoutingResult
        {
            Success = true,
            Destination = destination,
            ResponseMessage = responseMessage,
            ProcessingTimeMs = processingTimeMs,
            OrleansAttempted = orleansAttempted,
            DirectServiceUsed = directServiceUsed
        };
    }

    /// <summary>
    /// Creates a failed routing result.
    /// </summary>
    /// <param name="errorMessage">The error message</param>
    /// <param name="destination">The destination that was attempted</param>
    /// <param name="processingTimeMs">Time taken before failure</param>
    /// <param name="orleansAttempted">Whether Orleans was attempted</param>
    /// <param name="directServiceUsed">Whether direct service was attempted</param>
    /// <returns>A failed message routing result</returns>
    public static MessageRoutingResult CreateFailure(
        string errorMessage,
        MessageRoutingDestination destination = MessageRoutingDestination.Unknown,
        double processingTimeMs = 0,
        bool orleansAttempted = false,
        bool directServiceUsed = false)
    {
        return new MessageRoutingResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Destination = destination,
            ProcessingTimeMs = processingTimeMs,
            OrleansAttempted = orleansAttempted,
            DirectServiceUsed = directServiceUsed
        };
    }
}

/// <summary>
/// Represents possible message routing destinations.
/// </summary>
public enum MessageRoutingDestination
{
    /// <summary>
    /// Unknown or unspecified destination.
    /// </summary>
    Unknown,

    /// <summary>
    /// Orleans session grain.
    /// </summary>
    OrleansSessionGrain,

    /// <summary>
    /// Orleans chat grain.
    /// </summary>
    OrleansChatGrain,

    /// <summary>
    /// Direct chat service.
    /// </summary>
    DirectChatService,

    /// <summary>
    /// Custom message handler.
    /// </summary>
    CustomHandler,

    /// <summary>
    /// Broadcast to multiple destinations.
    /// </summary>
    Broadcast,

    /// <summary>
    /// Heartbeat handler.
    /// </summary>
    HeartbeatHandler,

    /// <summary>
    /// Protocol negotiation handler.
    /// </summary>
    ProtocolNegotiationHandler
}

/// <summary>
/// Represents statistics about message routing.
/// </summary>
public record MessageRoutingStatistics
{
    /// <summary>
    /// Gets the total number of messages routed.
    /// </summary>
    public long TotalMessages { get; init; }

    /// <summary>
    /// Gets the number of successfully routed messages.
    /// </summary>
    public long SuccessfulMessages { get; init; }

    /// <summary>
    /// Gets the number of failed message routings.
    /// </summary>
    public long FailedMessages { get; init; }

    /// <summary>
    /// Gets the count of messages by destination.
    /// </summary>
    public Dictionary<MessageRoutingDestination, long> MessagesByDestination { get; init; } = new();

    /// <summary>
    /// Gets the count of messages by type.
    /// </summary>
    public Dictionary<string, long> MessagesByType { get; init; } = new();

    /// <summary>
    /// Gets the count of messages by protocol.
    /// </summary>
    public Dictionary<string, long> MessagesByProtocol { get; init; } = new();

    /// <summary>
    /// Gets the average processing time in milliseconds.
    /// </summary>
    public double AverageProcessingTimeMs { get; init; }

    /// <summary>
    /// Gets the number of Orleans routing attempts.
    /// </summary>
    public long OrleansAttempts { get; init; }

    /// <summary>
    /// Gets the number of direct service routing uses.
    /// </summary>
    public long DirectServiceUses { get; init; }

    /// <summary>
    /// Gets the timestamp when statistics were collected.
    /// </summary>
    public DateTime CollectedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents the health status of the message router.
/// </summary>
public record MessageRouterHealthStatus
{
    /// <summary>
    /// Gets whether the message router is healthy overall.
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// Gets whether Orleans routing is available.
    /// </summary>
    public bool IsOrleansAvailable { get; init; }

    /// <summary>
    /// Gets whether direct service routing is available.
    /// </summary>
    public bool IsDirectServiceAvailable { get; init; }

    /// <summary>
    /// Gets the number of registered custom handlers.
    /// </summary>
    public int CustomHandlerCount { get; init; }

    /// <summary>
    /// Gets any health status message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the timestamp of this health check.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}