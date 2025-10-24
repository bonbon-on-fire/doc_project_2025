using System.Collections.Concurrent;
using System.Diagnostics;
using AIChat.Orleans.Contracts;
using AIChat.Server.Models.WebSocket;
using AIChat.Server.Services.Routing;

namespace AIChat.Server.Services.WebSocket;

/// <summary>
/// <para>
/// Implementation of IWebSocketMessageRouter for routing WebSocket messages through Orleans grains or direct services.
/// Implements the dual-mode routing pattern for seamless Orleans integration with fallback capabilities.
/// Provides comprehensive message routing, custom handler registration, and statistics collection.
/// </para>
/// <para>
/// Features:
/// - Dual-mode routing with Orleans grain integration
/// - Custom message handler registration and management
/// - Protocol-specific message routing
/// - Broadcasting capabilities for group messaging
/// - Comprehensive statistics and health monitoring
/// - Distributed tracing and structured logging
/// </para>
/// </summary>
public class WebSocketMessageRouter : IWebSocketMessageRouter
{
    private readonly IDualModeRouter _dualModeRouter;
    private readonly IWebSocketSessionManager _sessionManager;
    private readonly ILogger<WebSocketMessageRouter> _logger;
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.WebSocketMessageRouter");

    // Custom message handlers
    private readonly ConcurrentDictionary<string, Func<WebSocketMessage, WebSocketSessionInfo, CancellationToken, Task<MessageRoutingResult>>> _customHandlers = new();

    // Statistics tracking
    private long _totalMessages;
    private long _successfulMessages;
    private long _failedMessages;
    private long _orleansAttempts;
    private long _directServiceUses;
    private readonly Dictionary<MessageRoutingDestination, long> _messagesByDestination = [];
    private readonly Dictionary<string, long> _messagesByType = [];
    private readonly Dictionary<string, long> _messagesByProtocol = [];
    private readonly object _statisticsLock = new();

    /// <summary>
    /// Initializes a new instance of the WebSocketMessageRouter.
    /// </summary>
    /// <param name="dualModeRouter">Router for Orleans/direct service operations</param>
    /// <param name="sessionManager">Session manager for connection tracking</param>
    /// <param name="logger">Logger instance</param>
    public WebSocketMessageRouter(
        IDualModeRouter dualModeRouter,
        IWebSocketSessionManager sessionManager,
        ILogger<WebSocketMessageRouter> logger)
    {
        _dualModeRouter = dualModeRouter ?? throw new ArgumentNullException(nameof(dualModeRouter));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Register built-in message handlers
        RegisterBuiltInHandlers();
    }

    /// <inheritdoc />
    public async Task<MessageRoutingResult> RouteMessageAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("RouteMessage");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(sessionInfo);

            Interlocked.Increment(ref _totalMessages);

            // Update session activity
            await _sessionManager.UpdateActivityAsync(sessionInfo.SessionId, cancellationToken);

            // Update statistics
            lock (_statisticsLock)
            {
                _messagesByType.TryGetValue(message.Type, out var typeCount);
                _messagesByType[message.Type] = typeCount + 1;

                _messagesByProtocol.TryGetValue(sessionInfo.Protocol, out var protocolCount);
                _messagesByProtocol[sessionInfo.Protocol] = protocolCount + 1;
            }

            _logger.LogDebug(
                "Routing message {MessageId} of type {MessageType} for session {SessionId}",
                message.MessageId, message.Type, sessionInfo.SessionId);

            activity?.SetTag("message.id", message.MessageId);
            activity?.SetTag("message.type", message.Type);
            activity?.SetTag("session.id", sessionInfo.SessionId);
            activity?.SetTag("protocol", sessionInfo.Protocol);

            // Route based on message type
            var result = message.Type switch
            {
                WebSocketMessageTypes.Heartbeat => await RouteHeartbeatAsync(sessionInfo,
                    message.Payload as Dictionary<string, object>, cancellationToken),
                WebSocketMessageTypes.ProtocolNegotiation => await RouteProtocolNegotiationMessageAsync(message, sessionInfo, cancellationToken),
                WebSocketMessageTypes.ChatMessage => await RouteChatMessageAsync(message, sessionInfo, cancellationToken),
                WebSocketMessageTypes.Control => await RouteControlMessageAsync(message, sessionInfo, cancellationToken),
                _ => await RouteCustomMessageAsync(message, sessionInfo, cancellationToken)
            };

            // Update timing and success statistics
            stopwatch.Stop();
            result = result with { ProcessingTimeMs = stopwatch.ElapsedMilliseconds };

            if (result.Success)
            {
                Interlocked.Increment(ref _successfulMessages);
                UpdateDestinationStatistics(result.Destination, result.OrleansAttempted, result.DirectServiceUsed);

                _logger.LogDebug(
                    "Successfully routed message {MessageId} to {Destination} in {ElapsedMs}ms",
                    message.MessageId, result.Destination, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                Interlocked.Increment(ref _failedMessages);

                _logger.LogWarning(
                    "Failed to route message {MessageId}: {ErrorMessage} (Duration: {ElapsedMs}ms)",
                    message.MessageId, result.ErrorMessage, stopwatch.ElapsedMilliseconds);
            }

            activity?.SetTag("operation.success", result.Success);
            activity?.SetTag("destination", result.Destination.ToString());
            activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedMessages);
            stopwatch.Stop();

            _logger.LogError(ex,
                "Message routing failed for message {MessageId} in session {SessionId} after {ElapsedMs}ms",
                message.MessageId, sessionInfo.SessionId, stopwatch.ElapsedMilliseconds);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);

            return MessageRoutingResult.CreateFailure(
                $"Message routing error: {ex.Message}",
                MessageRoutingDestination.Unknown,
                stopwatch.ElapsedMilliseconds);
        }
    }

    /// <inheritdoc />
    public async Task<MessageRoutingResult> RouteToDestinationAsync(
        WebSocketMessage message,
        MessageRoutingDestination destination,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("RouteToDestination");

        try
        {
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(sessionInfo);

            _logger.LogDebug(
                "Routing message {MessageId} to specific destination {Destination}",
                message.MessageId, destination);

            activity?.SetTag("message.id", message.MessageId);
            activity?.SetTag("destination", destination.ToString());
            activity?.SetTag("session.id", sessionInfo.SessionId);

            return destination switch
            {
                MessageRoutingDestination.OrleansSessionGrain => await RouteToOrleansSessionGrainAsync(message, sessionInfo, cancellationToken),
                MessageRoutingDestination.OrleansChatGrain => await RouteToOrleansChatGrainAsync(message, sessionInfo, cancellationToken),
                MessageRoutingDestination.DirectChatService => await RouteToDirectChatServiceAsync(message, sessionInfo, cancellationToken),
                MessageRoutingDestination.HeartbeatHandler => await RouteHeartbeatAsync(sessionInfo, null, cancellationToken),
                MessageRoutingDestination.ProtocolNegotiationHandler => await RouteProtocolNegotiationMessageAsync(message, sessionInfo, cancellationToken),
                _ => MessageRoutingResult.CreateFailure($"Unknown destination: {destination}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to route message {MessageId} to destination {Destination}",
                message.MessageId, destination);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);

            return MessageRoutingResult.CreateFailure(
                $"Destination routing error: {ex.Message}",
                destination);
        }
    }

    /// <inheritdoc />
    public async Task<MessageRoutingResult> RouteProtocolMessageAsync(
        WebSocketMessage message,
        string protocol,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(message);
            ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
            ArgumentNullException.ThrowIfNull(sessionInfo);

            _logger.LogDebug(
                "Routing protocol-specific message {MessageId} for protocol {Protocol}",
                message.MessageId, protocol);

            // Route based on protocol type
            return protocol.ToLowerInvariant() switch
            {
                "chat-v1" => await RouteChatMessageAsync(message, sessionInfo, cancellationToken),
                "notifications-v1" => await RouteNotificationMessageAsync(message, sessionInfo, cancellationToken),
                "file-transfer-v1" => await RouteFileTransferMessageAsync(message, sessionInfo, cancellationToken),
                "generic-v1" => await RouteGenericMessageAsync(message, sessionInfo, cancellationToken),
                _ => await RouteCustomMessageAsync(message, sessionInfo, cancellationToken)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to route protocol message {MessageId} for protocol {Protocol}",
                message.MessageId, protocol);

            return MessageRoutingResult.CreateFailure(
                $"Protocol routing error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<List<MessageRoutingResult>> BroadcastMessageAsync(
        WebSocketMessage message,
        IEnumerable<string> targetSessions,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("BroadcastMessage");

        try
        {
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(targetSessions);

            var sessionList = targetSessions.ToList();
            var results = new List<MessageRoutingResult>();

            _logger.LogInformation(
                "Broadcasting message {MessageId} to {SessionCount} sessions",
                message.MessageId, sessionList.Count);

            activity?.SetTag("message.id", message.MessageId);
            activity?.SetTag("target.session.count", sessionList.Count);

            // Process sessions in parallel for better performance
            var tasks = sessionList.Select<string, Task<MessageRoutingResult>>(async sessionId =>
            {
                try
                {
                    var sessionInfo = await _sessionManager.GetSessionAsync(sessionId, cancellationToken);
                    if (sessionInfo == null)
                    {
                        return MessageRoutingResult.CreateFailure(
                            $"Session {sessionId} not found",
                            MessageRoutingDestination.Unknown);
                    }

                    return await RouteMessageAsync(message, sessionInfo, cancellationToken);
                }
                catch (Exception ex)
                {
                    return MessageRoutingResult.CreateFailure(
                        $"Broadcast error for session {sessionId}: {ex.Message}",
                        MessageRoutingDestination.Unknown);
                }
            });

            results.AddRange(await Task.WhenAll(tasks));

            var successCount = results.Count(r => r.Success);
            _logger.LogInformation(
                "Broadcast completed for message {MessageId}: {SuccessCount}/{TotalCount} successful",
                message.MessageId, successCount, results.Count);

            activity?.SetTag("successful.sends", successCount);
            activity?.SetTag("total.sends", results.Count);
            activity?.SetTag("operation.success", successCount > 0);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Broadcast failed for message {MessageId}", message.MessageId);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);

            return
            [
                MessageRoutingResult.CreateFailure($"Broadcast error: {ex.Message}", MessageRoutingDestination.Broadcast)
            ];
        }
    }

    /// <inheritdoc />
    public async Task<MessageRoutingResult> RouteHeartbeatAsync(
        WebSocketSessionInfo sessionInfo,
        Dictionary<string, object>? heartbeatData = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(sessionInfo);

            _logger.LogTrace("Processing heartbeat for session {SessionId}", sessionInfo.SessionId);

            // Update session heartbeat
            await _sessionManager.RecordHeartbeatAsync(sessionInfo.SessionId, cancellationToken);

            // Route heartbeat through Orleans grain for session health tracking
            var result = await _dualModeRouter.ExecuteAsync(
                orleansOperation: async grain => await ProcessOrleansHeartbeatAsync(grain, sessionInfo, heartbeatData, cancellationToken),
                directOperation: async service => await ProcessDirectHeartbeatAsync(sessionInfo, heartbeatData, cancellationToken),
                operationName: "ProcessHeartbeat",
                cancellationToken: cancellationToken);

            return MessageRoutingResult.CreateSuccess(
                MessageRoutingDestination.HeartbeatHandler,
                result.ResponseMessage,
                0,
                result.OrleansAttempted,
                result.DirectServiceUsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat processing failed for session {SessionId}", sessionInfo.SessionId);
            return MessageRoutingResult.CreateFailure($"Heartbeat error: {ex.Message}", MessageRoutingDestination.HeartbeatHandler);
        }
    }

    /// <inheritdoc />
    public async Task<bool> RegisterMessageHandlerAsync(
        string messageType,
        Func<WebSocketMessage, WebSocketSessionInfo, CancellationToken, Task<MessageRoutingResult>> handler,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
            ArgumentNullException.ThrowIfNull(handler);

            var registered = _customHandlers.TryAdd(messageType, handler);

            if (registered)
            {
                _logger.LogInformation("Registered custom message handler for type {MessageType}", messageType);
            }
            else
            {
                _logger.LogWarning("Message handler for type {MessageType} already exists", messageType);
            }

            return await Task.FromResult(registered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register message handler for type {MessageType}", messageType);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UnregisterMessageHandlerAsync(
        string messageType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(messageType);

            var removed = _customHandlers.TryRemove(messageType, out _);

            if (removed)
            {
                _logger.LogInformation("Unregistered custom message handler for type {MessageType}", messageType);
            }
            else
            {
                _logger.LogWarning("No message handler found for type {MessageType}", messageType);
            }

            return await Task.FromResult(removed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister message handler for type {MessageType}", messageType);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<MessageRoutingStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Suppress CS1998
        lock (_statisticsLock)
        {
            var statistics = new MessageRoutingStatistics
            {
                TotalMessages = Interlocked.Read(ref _totalMessages),
                SuccessfulMessages = Interlocked.Read(ref _successfulMessages),
                FailedMessages = Interlocked.Read(ref _failedMessages),
                MessagesByDestination = new Dictionary<MessageRoutingDestination, long>(_messagesByDestination),
                MessagesByType = new Dictionary<string, long>(_messagesByType),
                MessagesByProtocol = new Dictionary<string, long>(_messagesByProtocol),
                AverageProcessingTimeMs = 0, // TODO: Implement timing tracking
                OrleansAttempts = Interlocked.Read(ref _orleansAttempts),
                DirectServiceUses = Interlocked.Read(ref _directServiceUses)
            };

            return statistics;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsOrleansRoutingAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dualModeRouter.IsOrleansEnabledAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MessageRouterHealthStatus> GetHealthStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isOrleansAvailable = await IsOrleansRoutingAvailableAsync(cancellationToken);
            var customHandlerCount = _customHandlers.Count;

            var status = new MessageRouterHealthStatus
            {
                IsHealthy = true, // Consider healthy if we can respond
                IsOrleansAvailable = isOrleansAvailable,
                IsDirectServiceAvailable = true, // Direct service is always available
                CustomHandlerCount = customHandlerCount,
                Message = isOrleansAvailable ? "All routing modes available" : "Orleans unavailable, using direct service only"
            };

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get message router health status");

            return new MessageRouterHealthStatus
            {
                IsHealthy = false,
                IsOrleansAvailable = false,
                IsDirectServiceAvailable = false,
                Message = $"Health check failed: {ex.Message}"
            };
        }
    }

    #region Private Helper Methods

    private void RegisterBuiltInHandlers()
    {
        // Built-in handlers are implemented as direct routing logic in RouteMessageAsync
        // Custom handlers can be registered via RegisterMessageHandlerAsync
        _logger.LogDebug("Built-in message handlers registered");
    }

    private async Task<MessageRoutingResult> RouteChatMessageAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken)
    {
        return await _dualModeRouter.ExecuteAsync(
            orleansOperation: async grain => await RouteToOrleansChatGrainAsync(message, sessionInfo, cancellationToken),
            directOperation: async service => await RouteToDirectChatServiceAsync(message, sessionInfo, cancellationToken),
            operationName: "RouteChatMessage",
            cancellationToken: cancellationToken);
    }

    private Task<MessageRoutingResult> RouteProtocolNegotiationMessageAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken)
    {
        // Protocol negotiation messages are handled internally
        _logger.LogDebug("Processing protocol negotiation message for session {SessionId}", sessionInfo.SessionId);

        return Task.FromResult(MessageRoutingResult.CreateSuccess(
            MessageRoutingDestination.ProtocolNegotiationHandler,
            null,
            0,
            false,
            true));
    }

    private Task<MessageRoutingResult> RouteControlMessageAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken)
    {
        // Handle control messages (disconnect, status updates, etc.)
        _logger.LogDebug("Processing control message for session {SessionId}", sessionInfo.SessionId);

        return Task.FromResult(MessageRoutingResult.CreateSuccess(
            MessageRoutingDestination.CustomHandler,
            null,
            0,
            false,
            true));
    }

    private async Task<MessageRoutingResult> RouteCustomMessageAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken)
    {
        if (_customHandlers.TryGetValue(message.Type, out var handler))
        {
            return await handler(message, sessionInfo, cancellationToken);
        }

        _logger.LogWarning("No handler found for message type {MessageType}", message.Type);
        return MessageRoutingResult.CreateFailure(
            $"No handler registered for message type: {message.Type}",
            MessageRoutingDestination.Unknown);
    }

    private async Task<MessageRoutingResult> RouteNotificationMessageAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken)
    {
        // Route notification messages - could be Orleans or direct
        return await RouteGenericMessageAsync(message, sessionInfo, cancellationToken);
    }

    private async Task<MessageRoutingResult> RouteFileTransferMessageAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken)
    {
        // Route file transfer messages - likely direct service
        return await RouteToDirectChatServiceAsync(message, sessionInfo, cancellationToken);
    }

    private async Task<MessageRoutingResult> RouteGenericMessageAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken)
    {
        // Generic routing with dual-mode support
        return await _dualModeRouter.ExecuteAsync(
            orleansOperation: async grain => await RouteToOrleansSessionGrainAsync(message, sessionInfo, cancellationToken),
            directOperation: async service => await RouteToDirectChatServiceAsync(message, sessionInfo, cancellationToken),
            operationName: "RouteGenericMessage",
            cancellationToken: cancellationToken);
    }

    private Task<MessageRoutingResult> RouteToOrleansSessionGrainAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            Interlocked.Increment(ref _orleansAttempts);

            // Future: Route to Orleans ISessionProtocolGrain.HandleProtocolMessageAsync
            _logger.LogDebug("Routing message {MessageId} to Orleans session grain", message.MessageId);

            return Task.FromResult(MessageRoutingResult.CreateSuccess(
                MessageRoutingDestination.OrleansSessionGrain,
                null,
                0,
                true,
                false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route message to Orleans session grain");
            throw;
        }
    }

    private Task<MessageRoutingResult> RouteToOrleansChatGrainAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            Interlocked.Increment(ref _orleansAttempts);

            // Future: Route to Orleans IChatGrain for chat-specific messages
            _logger.LogDebug("Routing message {MessageId} to Orleans chat grain", message.MessageId);

            return Task.FromResult(MessageRoutingResult.CreateSuccess(
                MessageRoutingDestination.OrleansChatGrain,
                null,
                0,
                true,
                false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route message to Orleans chat grain");
            throw;
        }
    }

    private Task<MessageRoutingResult> RouteToDirectChatServiceAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            Interlocked.Increment(ref _directServiceUses);

            // Route to direct chat service for processing
            _logger.LogDebug("Routing message {MessageId} to direct chat service", message.MessageId);

            return Task.FromResult(MessageRoutingResult.CreateSuccess(
                MessageRoutingDestination.DirectChatService,
                null,
                0,
                false,
                true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route message to direct chat service");
            throw;
        }
    }

    private Task<HeartbeatResult> ProcessOrleansHeartbeatAsync(
        IChatGrain grain,
        WebSocketSessionInfo sessionInfo,
        Dictionary<string, object>? heartbeatData,
        CancellationToken cancellationToken)
    {
        // Future: Call ISessionConnectionGrain.HeartbeatAsync
        _logger.LogTrace("Processing heartbeat via Orleans for session {SessionId}", sessionInfo.SessionId);

        return Task.FromResult(new HeartbeatResult
        {
            OrleansAttempted = true,
            DirectServiceUsed = false,
            ResponseMessage = null
        });
    }

    private Task<HeartbeatResult> ProcessDirectHeartbeatAsync(
        WebSocketSessionInfo sessionInfo,
        Dictionary<string, object>? heartbeatData,
        CancellationToken cancellationToken)
    {
        // Direct heartbeat processing
        _logger.LogTrace("Processing heartbeat directly for session {SessionId}", sessionInfo.SessionId);

        return Task.FromResult(new HeartbeatResult
        {
            OrleansAttempted = false,
            DirectServiceUsed = true,
            ResponseMessage = new WebSocketMessage
            {
                Type = WebSocketMessageTypes.Acknowledgment,
                SessionId = sessionInfo.SessionId,
                Payload = new { Type = "heartbeat-ack", Timestamp = DateTime.UtcNow }
            }
        });
    }

    private void UpdateDestinationStatistics(MessageRoutingDestination destination, bool orleansAttempted, bool directServiceUsed)
    {
        lock (_statisticsLock)
        {
            _messagesByDestination.TryGetValue(destination, out var count);
            _messagesByDestination[destination] = count + 1;
        }

        if (orleansAttempted)
        {
            Interlocked.Increment(ref _orleansAttempts);
        }

        if (directServiceUsed)
        {
            Interlocked.Increment(ref _directServiceUses);
        }
    }

    #endregion

    #region Helper Classes

    private sealed record HeartbeatResult
    {
        public bool OrleansAttempted { get; init; }
        public bool DirectServiceUsed { get; init; }
        public WebSocketMessage? ResponseMessage { get; init; }
    }

    #endregion
}