using System.Net.WebSockets;
using AIChat.Server.Models.WebSocket;

namespace AIChat.Server.Handlers;

/// <summary>
/// Interface for handling WebSocket connections with Orleans integration.
/// Provides the contract for WebSocket lifecycle management, message processing,
/// and protocol negotiation within the Orleans-based architecture.
/// </summary>
public interface IWebSocketHandler
{
    /// <summary>
    /// Handles an incoming WebSocket connection request.
    /// This is the main entry point for WebSocket processing.
    /// </summary>
    /// <param name="context">The HTTP context containing the WebSocket request</param>
    /// <param name="webSocket">The accepted WebSocket connection</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    Task HandleWebSocketAsync(
        HttpContext context,
        WebSocket webSocket,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes incoming WebSocket messages for a specific session.
    /// Runs the message processing loop for the duration of the connection.
    /// </summary>
    /// <param name="webSocket">The WebSocket connection</param>
    /// <param name="sessionInfo">Information about the WebSocket session</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    Task ProcessWebSocketMessagesAsync(
        WebSocket webSocket,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles protocol negotiation during WebSocket handshake.
    /// Determines the best protocol based on client preferences and server capabilities.
    /// </summary>
    /// <param name="requestedProtocols">Protocols requested by the client</param>
    /// <param name="userId">The user identifier for the connection</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of protocol negotiation</returns>
    Task<ProtocolNegotiationResult> NegotiateProtocolAsync(
        IEnumerable<string> requestedProtocols,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles an incoming WebSocket message.
    /// Routes the message through Orleans grains or direct services based on configuration.
    /// </summary>
    /// <param name="message">The WebSocket message to process</param>
    /// <param name="sessionInfo">Information about the session</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    Task HandleIncomingMessageAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to a specific WebSocket connection.
    /// </summary>
    /// <param name="webSocket">The WebSocket to send the message to</param>
    /// <param name="message">The message to send</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    Task SendMessageAsync(
        WebSocket webSocket,
        WebSocketMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles WebSocket connection closure.
    /// Performs cleanup operations and notifies Orleans grains of the disconnection.
    /// </summary>
    /// <param name="sessionInfo">Information about the session being closed</param>
    /// <param name="closeStatus">The WebSocket close status</param>
    /// <param name="statusDescription">Optional description of the close reason</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    Task HandleConnectionCloseAsync(
        WebSocketSessionInfo sessionInfo,
        WebSocketCloseStatus? closeStatus = null,
        string? statusDescription = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles WebSocket errors and exceptions.
    /// Provides centralized error handling and recovery logic.
    /// </summary>
    /// <param name="exception">The exception that occurred</param>
    /// <param name="sessionInfo">Information about the session where the error occurred</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    Task HandleWebSocketErrorAsync(
        Exception exception,
        WebSocketSessionInfo? sessionInfo = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the health status of the WebSocket handler.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Health status information</returns>
    Task<WebSocketHandlerHealthStatus> GetHealthStatusAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the health status of the WebSocket handler.
/// </summary>
public record WebSocketHandlerHealthStatus
{
    /// <summary>
    /// Gets whether the WebSocket handler is healthy overall.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Gets the number of active WebSocket connections.
    /// </summary>
    public int ActiveConnections { get; init; }

    /// <summary>
    /// Gets whether Orleans integration is available and healthy.
    /// </summary>
    public bool IsOrleansHealthy { get; init; }

    /// <summary>
    /// Gets any health status message or error details.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the timestamp of this health check.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets connection metrics summary.
    /// </summary>
    public Dictionary<string, object>? Metrics { get; init; }
}
