using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AIChat.Server.Models.WebSocket;
using AIChat.Server.Services.WebSocket;

namespace AIChat.Server.Handlers;

/// <summary>
/// Manages WebSocket heartbeat operations for maintaining connection health.
/// Provides automatic cleanup and proper resource disposal following the optimized patterns
/// established in architecture review feedback for Orleans integration.
/// </summary>
internal sealed class WebSocketHeartbeatManager : IDisposable
{
    private readonly WebSocketSessionInfo _sessionInfo;
    private readonly IWebSocketMessageRouter _messageRouter;
    private readonly ILogger _logger;
    private readonly TimeSpan _heartbeatInterval;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly CancellationToken _combinedToken;

    private Task? _heartbeatTask;
    private bool _disposed;

    public WebSocketHeartbeatManager(
        WebSocketSessionInfo sessionInfo,
        IWebSocketMessageRouter messageRouter,
        ILogger logger,
        TimeSpan heartbeatInterval,
        CancellationToken externalToken)
    {
        _sessionInfo = sessionInfo ?? throw new ArgumentNullException(nameof(sessionInfo));
        _messageRouter = messageRouter ?? throw new ArgumentNullException(nameof(messageRouter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _heartbeatInterval = heartbeatInterval;

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _combinedToken = _cancellationTokenSource.Token;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _heartbeatTask = ExecuteHeartbeatLoopAsync(_combinedToken);
    }

    private async Task ExecuteHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var periodicTimer = new PeriodicTimer(_heartbeatInterval);

            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    if (_sessionInfo.WebSocket.State == System.Net.WebSockets.WebSocketState.Open)
                    {
                        await _messageRouter.RouteHeartbeatAsync(_sessionInfo, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        _logger.LogDebug("Heartbeat stopping - WebSocket no longer open for session {SessionId}", _sessionInfo.SessionId);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Heartbeat error for session {SessionId}", _sessionInfo.SessionId);
                    // Continue heartbeat on error - don't break the loop for resilience
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Heartbeat cancelled for session {SessionId}", _sessionInfo.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal heartbeat error for session {SessionId}", _sessionInfo.SessionId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cancellationTokenSource.Cancel();

        if (_heartbeatTask != null)
        {
            try
            {
                _heartbeatTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }
        }

        _cancellationTokenSource.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// <para>
/// Implementation of IWebSocketHandler for managing WebSocket connections with Orleans integration.
/// Provides comprehensive WebSocket lifecycle management, message processing, and protocol negotiation
/// within the Orleans-based architecture following established patterns from ChatHub.
/// </para>
/// <para>
/// Features:
/// - Complete WebSocket connection lifecycle management
/// - Protocol negotiation with Orleans grain integration
/// - Message routing through dual-mode Orleans/direct service pattern
/// - Comprehensive error handling and recovery
/// - Distributed tracing and structured logging
/// - Health monitoring and metrics collection
/// </para>
/// </summary>
public class WebSocketHandler : IWebSocketHandler
{
    private readonly IWebSocketSessionManager _sessionManager;
    private readonly IWebSocketProtocolNegotiator _protocolNegotiator;
    private readonly IWebSocketMessageRouter _messageRouter;
    private readonly ILogger<WebSocketHandler> _logger;
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.WebSocketHandler");

    // Configuration constants
    private const int BufferSize = 4096;
    private const int MaxMessageSize = 1024 * 1024; // 1MB
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    // Cached JsonSerializerOptions for efficient serialization
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the WebSocketHandler.
    /// </summary>
    /// <param name="sessionManager">Session manager for connection tracking</param>
    /// <param name="protocolNegotiator">Protocol negotiator for WebSocket protocols</param>
    /// <param name="messageRouter">Message router for Orleans/direct service routing</param>
    /// <param name="logger">Logger instance</param>
    public WebSocketHandler(
        IWebSocketSessionManager sessionManager,
        IWebSocketProtocolNegotiator protocolNegotiator,
        IWebSocketMessageRouter messageRouter,
        ILogger<WebSocketHandler> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _protocolNegotiator = protocolNegotiator ?? throw new ArgumentNullException(nameof(protocolNegotiator));
        _messageRouter = messageRouter ?? throw new ArgumentNullException(nameof(messageRouter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task HandleWebSocketAsync(
        HttpContext context,
        WebSocket webSocket,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("HandleWebSocket");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(webSocket);

            var connectionId = context.Connection.Id ?? Guid.NewGuid().ToString();
            var userId = GetUserIdFromContext(context);

            _logger.LogInformation(
                "WebSocket connection established: Connection {ConnectionId}, User {UserId}",
                connectionId, userId);

            activity?.SetTag("connection.id", connectionId);
            activity?.SetTag("user.id", userId);

            // Perform protocol negotiation
            var requestedProtocols = GetRequestedProtocols(context);
            var negotiationResult = await NegotiateProtocolAsync(requestedProtocols, userId, cancellationToken);

            if (!negotiationResult.Success)
            {
                await CloseWebSocketWithErrorAsync(webSocket, WebSocketCloseStatus.ProtocolError,
                    negotiationResult.ErrorMessage, cancellationToken);
                return;
            }

            var protocol = negotiationResult.SelectedProtocol!.Name;

            // Create session
            var sessionInfo = await _sessionManager.CreateSessionAsync(
                webSocket, connectionId, userId, protocol,
                metadata: new Dictionary<string, object>
                {
                    ["remoteEndPoint"] = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    ["userAgent"] = context.Request.Headers.UserAgent.ToString(),
                    ["negotiatedCapabilities"] = negotiationResult.NegotiatedCapabilities
                },
                cancellationToken);

            activity?.SetTag("session.id", sessionInfo.SessionId);
            activity?.SetTag("protocol", protocol);

            stopwatch.Stop();
            _logger.LogInformation(
                "WebSocket session created: {SessionId} for user {UserId} with protocol {Protocol} in {ElapsedMs}ms",
                sessionInfo.SessionId, userId, protocol, stopwatch.ElapsedMilliseconds);

            // Start message processing
            await ProcessWebSocketMessagesAsync(webSocket, sessionInfo, cancellationToken);
        }
        catch (WebSocketException wsEx)
        {
            _logger.LogWarning(wsEx,
                "WebSocket error during connection handling (Connection: {ConnectionId}): {ErrorCode}",
                context.Connection.Id, wsEx.WebSocketErrorCode);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", "WebSocketException");
            activity?.SetTag("websocket.error.code", wsEx.WebSocketErrorCode.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error during WebSocket connection handling (Connection: {ConnectionId})",
                context.Connection.Id);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
        }
        finally
        {
            stopwatch.Stop();
            activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);
        }
    }

    /// <inheritdoc />
    public async Task ProcessWebSocketMessagesAsync(
        WebSocket webSocket,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("ProcessWebSocketMessages");
        using var heartbeatManager = CreateHeartbeatManager(sessionInfo, cancellationToken);

        try
        {
            ArgumentNullException.ThrowIfNull(webSocket);
            ArgumentNullException.ThrowIfNull(sessionInfo);

            _logger.LogDebug("Starting message processing for session {SessionId}", sessionInfo.SessionId);

            heartbeatManager.Start();
            await ProcessMessageLoopAsync(webSocket, sessionInfo, cancellationToken);

            _logger.LogDebug("Message processing completed for session {SessionId}", sessionInfo.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message processing failed for session {SessionId}", sessionInfo.SessionId);
            await HandleWebSocketErrorAsync(ex, sessionInfo, cancellationToken);
        }
        finally
        {
            await _sessionManager.RemoveSessionAsync(sessionInfo.SessionId, CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public async Task<ProtocolNegotiationResult> NegotiateProtocolAsync(
        IEnumerable<string> requestedProtocols,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("NegotiateProtocol");

        try
        {
            ArgumentNullException.ThrowIfNull(requestedProtocols);
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            _logger.LogDebug("Negotiating protocol for user {UserId} with protocols: {RequestedProtocols}",
                userId, string.Join(", ", requestedProtocols));

            var result = await _protocolNegotiator.NegotiateProtocolAsync(
                requestedProtocols, userId, cancellationToken: cancellationToken);

            activity?.SetTag("user.id", userId);
            activity?.SetTag("negotiation.success", result.Success);
            if (result.Success)
            {
                activity?.SetTag("selected.protocol", result.SelectedProtocol!.Name);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Protocol negotiation failed for user {UserId}", userId);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);

            return ProtocolNegotiationResult.CreateFailure($"Protocol negotiation error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task HandleIncomingMessageAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("HandleIncomingMessage");

        try
        {
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(sessionInfo);

            _logger.LogDebug(
                "Handling incoming message {MessageId} of type {MessageType} for session {SessionId}",
                message.MessageId, message.Type, sessionInfo.SessionId);

            activity?.SetTag("message.id", message.MessageId);
            activity?.SetTag("message.type", message.Type);
            activity?.SetTag("session.id", sessionInfo.SessionId);

            // Route message through the message router
            var routingResult = await _messageRouter.RouteMessageAsync(message, sessionInfo, cancellationToken);

            if (!routingResult.Success)
            {
                _logger.LogWarning(
                    "Message routing failed for message {MessageId}: {ErrorMessage}",
                    message.MessageId, routingResult.ErrorMessage);

                await SendErrorMessageAsync(sessionInfo.WebSocket, sessionInfo,
                    routingResult.ErrorMessage ?? "Message processing failed", cancellationToken);
            }
            else if (routingResult.ResponseMessage != null)
            {
                // Send response message if provided
                await SendMessageAsync(sessionInfo.WebSocket, routingResult.ResponseMessage, cancellationToken);
            }

            activity?.SetTag("routing.success", routingResult.Success);
            activity?.SetTag("routing.destination", routingResult.Destination.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error handling incoming message {MessageId} for session {SessionId}",
                message.MessageId, sessionInfo.SessionId);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);

            await HandleWebSocketErrorAsync(ex, sessionInfo, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task SendMessageAsync(
        WebSocket webSocket,
        WebSocketMessage message,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("SendMessage");

        try
        {
            ArgumentNullException.ThrowIfNull(webSocket);
            ArgumentNullException.ThrowIfNull(message);

            if (webSocket.State != WebSocketState.Open)
            {
                _logger.LogWarning("Cannot send message - WebSocket is not open (State: {State})", webSocket.State);
                return;
            }

            var json = JsonSerializer.Serialize(message, JsonOptions);

            var bytes = Encoding.UTF8.GetBytes(json);
            var buffer = new ArraySegment<byte>(bytes);

            await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);

            _logger.LogTrace("Sent message {MessageId} of type {MessageType} ({ByteCount} bytes)",
                message.MessageId, message.Type, bytes.Length);

            activity?.SetTag("message.id", message.MessageId);
            activity?.SetTag("message.type", message.Type);
            activity?.SetTag("message.size.bytes", bytes.Length);
            activity?.SetTag("operation.success", true);
        }
        catch (WebSocketException wsEx)
        {
            _logger.LogWarning(wsEx, "Failed to send WebSocket message: {ErrorCode}", wsEx.WebSocketErrorCode);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", "WebSocketException");
            activity?.SetTag("websocket.error.code", wsEx.WebSocketErrorCode.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message {MessageId}", message.MessageId);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
        }
    }

    /// <inheritdoc />
    public async Task HandleConnectionCloseAsync(
        WebSocketSessionInfo sessionInfo,
        WebSocketCloseStatus? closeStatus = null,
        string? statusDescription = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("HandleConnectionClose");

        try
        {
            ArgumentNullException.ThrowIfNull(sessionInfo);

            _logger.LogInformation(
                "Handling WebSocket connection close for session {SessionId}: {CloseStatus} - {StatusDescription}",
                sessionInfo.SessionId, closeStatus, statusDescription);

            // Update session status
            await _sessionManager.UpdateSessionStatusAsync(sessionInfo.SessionId,
                WebSocketConnectionStatus.Disconnected, cancellationToken);

            // Close WebSocket if still open
            if (sessionInfo.WebSocket.State == WebSocketState.Open)
            {
                await sessionInfo.WebSocket.CloseAsync(
                    closeStatus ?? WebSocketCloseStatus.NormalClosure,
                    statusDescription ?? "Connection closed",
                    cancellationToken);
            }

            // Remove session
            await _sessionManager.RemoveSessionAsync(sessionInfo.SessionId, cancellationToken);

            activity?.SetTag("session.id", sessionInfo.SessionId);
            activity?.SetTag("close.status", closeStatus?.ToString());
            activity?.SetTag("operation.success", true);

            _logger.LogDebug("Connection cleanup completed for session {SessionId}", sessionInfo.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during connection close handling for session {SessionId}", sessionInfo.SessionId);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
        }
    }

    /// <inheritdoc />
    public async Task HandleWebSocketErrorAsync(
        Exception exception,
        WebSocketSessionInfo? sessionInfo = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("HandleWebSocketError");

        try
        {
            ArgumentNullException.ThrowIfNull(exception);

            var sessionId = sessionInfo?.SessionId ?? "unknown";

            _logger.LogError(exception, "WebSocket error for session {SessionId}: {ErrorType}", sessionId, exception.GetType().Name);

            if (sessionInfo != null)
            {
                sessionInfo.Metrics.RecordError();

                // Try to send error message to client if connection is still open
                if (sessionInfo.WebSocket.State == WebSocketState.Open)
                {
                    await SendErrorMessageAsync(sessionInfo.WebSocket, sessionInfo, exception.Message, cancellationToken);
                }

                // Update session status
                await _sessionManager.UpdateSessionStatusAsync(sessionInfo.SessionId,
                    WebSocketConnectionStatus.Error, cancellationToken);
            }

            activity?.SetTag("session.id", sessionId);
            activity?.SetTag("error.type", exception.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebSocket error handling");
        }
    }

    /// <inheritdoc />
    public async Task<WebSocketHandlerHealthStatus> GetHealthStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionStats = await _sessionManager.GetStatisticsAsync(cancellationToken);
            var routerHealth = await _messageRouter.GetHealthStatusAsync(cancellationToken);

            var healthStatus = new WebSocketHandlerHealthStatus
            {
                IsHealthy = routerHealth.IsHealthy,
                ActiveConnections = sessionStats.TotalActiveSessions,
                IsOrleansHealthy = routerHealth.IsOrleansAvailable,
                Message = routerHealth.Message,
                Metrics = new Dictionary<string, object>
                {
                    ["totalSessions"] = sessionStats.TotalSessionsCreated,
                    ["sessionsByProtocol"] = sessionStats.SessionsByProtocol,
                    ["averageSessionDuration"] = sessionStats.AverageSessionDurationMinutes,
                    ["totalMessages"] = sessionStats.AggregateMetrics.TotalMessagesReceived + sessionStats.AggregateMetrics.TotalMessagesSent
                }
            };

            return healthStatus;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get WebSocket handler health status");

            return new WebSocketHandlerHealthStatus
            {
                IsHealthy = false,
                ActiveConnections = 0,
                IsOrleansHealthy = false,
                Message = $"Health check failed: {ex.Message}"
            };
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Creates a heartbeat manager for managing periodic heartbeat operations.
    /// </summary>
    /// <param name="sessionInfo">Session information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Heartbeat manager instance</returns>
    private WebSocketHeartbeatManager CreateHeartbeatManager(WebSocketSessionInfo sessionInfo, CancellationToken cancellationToken)
    {
        return new WebSocketHeartbeatManager(sessionInfo, _messageRouter, _logger, HeartbeatInterval, cancellationToken);
    }

    /// <summary>
    /// Processes the main message loop for WebSocket communications.
    /// Reduced complexity through focused message processing logic.
    /// </summary>
    /// <param name="webSocket">WebSocket instance</param>
    /// <param name="sessionInfo">Session information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task ProcessMessageLoopAsync(WebSocket webSocket, WebSocketSessionInfo sessionInfo, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        var messageBuffer = new List<byte>();

        while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await HandleCloseMessageAsync(sessionInfo, result, cancellationToken);
                    break;
                }

                if (!await ProcessMessageFrameAsync(webSocket, sessionInfo, buffer, result, messageBuffer, cancellationToken))
                {
                    break; // Break on error or size limit exceeded
                }
            }
            catch (WebSocketException wsEx) when (wsEx.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                _logger.LogInformation("WebSocket connection closed prematurely for session {SessionId}", sessionInfo.SessionId);
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Message processing cancelled for session {SessionId}", sessionInfo.SessionId);
                break;
            }
            catch (Exception ex)
            {
                await HandleMessageProcessingErrorAsync(webSocket, sessionInfo, ex, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Handles close message processing with proper logging and cleanup.
    /// </summary>
    /// <param name="sessionInfo">Session information</param>
    /// <param name="result">WebSocket receive result</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task HandleCloseMessageAsync(WebSocketSessionInfo sessionInfo, WebSocketReceiveResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "WebSocket close received for session {SessionId}: {CloseStatus} - {CloseDescription}",
            sessionInfo.SessionId, result.CloseStatus, result.CloseStatusDescription);

        await HandleConnectionCloseAsync(sessionInfo, result.CloseStatus, result.CloseStatusDescription, cancellationToken);
    }

    /// <summary>
    /// Processes a single message frame with size validation and assembly.
    /// Returns false if processing should stop (error or limit exceeded).
    /// </summary>
    /// <param name="webSocket">WebSocket instance</param>
    /// <param name="sessionInfo">Session information</param>
    /// <param name="buffer">Receive buffer</param>
    /// <param name="result">WebSocket receive result</param>
    /// <param name="messageBuffer">Message assembly buffer</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True to continue processing, false to stop</returns>
    private async Task<bool> ProcessMessageFrameAsync(
        WebSocket webSocket,
        WebSocketSessionInfo sessionInfo,
        byte[] buffer,
        WebSocketReceiveResult result,
        List<byte> messageBuffer,
        CancellationToken cancellationToken)
    {
        // Accumulate message bytes
        messageBuffer.AddRange(buffer.Take(result.Count));

        // Check for message size limit
        if (messageBuffer.Count > MaxMessageSize)
        {
            _logger.LogWarning(
                "Message size limit exceeded for session {SessionId}: {MessageSize} bytes",
                sessionInfo.SessionId, messageBuffer.Count);

            await CloseWebSocketWithErrorAsync(webSocket, WebSocketCloseStatus.MessageTooBig,
                "Message size limit exceeded", cancellationToken);
            return false;
        }

        // Process complete message
        if (result.EndOfMessage)
        {
            await ProcessCompleteMessageAsync(webSocket, sessionInfo, [.. messageBuffer], result.MessageType, cancellationToken);
            messageBuffer.Clear();
        }

        return true;
    }

    /// <summary>
    /// Handles message processing errors with proper error recording and client notification.
    /// </summary>
    /// <param name="webSocket">WebSocket instance</param>
    /// <param name="sessionInfo">Session information</param>
    /// <param name="ex">Exception that occurred</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task HandleMessageProcessingErrorAsync(
        WebSocket webSocket,
        WebSocketSessionInfo sessionInfo,
        Exception ex,
        CancellationToken cancellationToken)
    {
        _logger.LogError(ex, "Error processing message for session {SessionId}", sessionInfo.SessionId);
        sessionInfo.Metrics.RecordError();

        // Try to send error message to client
        await SendErrorMessageAsync(webSocket, sessionInfo, ex.Message, cancellationToken);
    }

    private static string GetUserIdFromContext(HttpContext context)
    {
        // Try to get user ID from various sources
        var userId = context.User?.Identity?.Name ??
                    context.Request.Query["userId"].FirstOrDefault() ??
                    context.Request.Headers["X-User-Id"].FirstOrDefault() ??
                    "anonymous";

        return userId;
    }

    private static IEnumerable<string> GetRequestedProtocols(HttpContext context)
    {
        var protocolHeader = context.Request.Headers.SecWebSocketProtocol.FirstOrDefault();
        if (string.IsNullOrEmpty(protocolHeader))
        {
            return ["generic-v1"]; // Default protocol
        }

        return protocolHeader.Split(',').Select(p => p.Trim());
    }

    private async Task ProcessCompleteMessageAsync(
        WebSocket webSocket,
        WebSocketSessionInfo sessionInfo,
        byte[] messageBytes,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        try
        {
            if (messageType == WebSocketMessageType.Text)
            {
                var json = Encoding.UTF8.GetString(messageBytes);
                var message = JsonSerializer.Deserialize<WebSocketMessage>(json, JsonOptions);

                if (message != null)
                {
                    sessionInfo.Metrics.RecordMessageReceived(messageBytes.Length);
                    await HandleIncomingMessageAsync(message, sessionInfo, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Failed to deserialize WebSocket message for session {SessionId}", sessionInfo.SessionId);
                }
            }
            else if (messageType == WebSocketMessageType.Binary)
            {
                // Handle binary messages (future enhancement)
                _logger.LogDebug("Received binary message for session {SessionId} ({ByteCount} bytes)",
                    sessionInfo.SessionId, messageBytes.Length);
            }
        }
        catch (JsonException jsonEx)
        {
            _logger.LogWarning(jsonEx, "Invalid JSON message received for session {SessionId}", sessionInfo.SessionId);
            await SendErrorMessageAsync(webSocket, sessionInfo, "Invalid message format", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing complete message for session {SessionId}", sessionInfo.SessionId);
            await SendErrorMessageAsync(webSocket, sessionInfo, "Message processing error", cancellationToken);
        }
    }

    private async Task SendErrorMessageAsync(
        WebSocket webSocket,
        WebSocketSessionInfo sessionInfo,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var errorMsg = new WebSocketMessage
            {
                Type = WebSocketMessageTypes.Error,
                SessionId = sessionInfo.SessionId,
                Payload = new ErrorPayload
                {
                    ErrorCode = "PROCESSING_ERROR",
                    Message = errorMessage,
                    Recoverable = true
                }
            };

            await SendMessageAsync(webSocket, errorMsg, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send error message to session {SessionId}", sessionInfo.SessionId);
        }
    }

    private async Task CloseWebSocketWithErrorAsync(
        WebSocket webSocket,
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(closeStatus, statusDescription, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close WebSocket with error status {CloseStatus}", closeStatus);
        }
    }

    #endregion
}