using System.Collections.Concurrent;
using System.Diagnostics;
using AIChat.Server.Models.WebSocket;

namespace AIChat.Server.Services.WebSocket;

/// <summary>
/// Implementation of IWebSocketMessageRouter for routing WebSocket messages.
/// Provides basic message routing with Orleans integration support.
/// </summary>
public class WebSocketMessageRouter : IWebSocketMessageRouter
{
    private readonly ILogger<WebSocketMessageRouter> _logger;
    private readonly ConcurrentDictionary<string, Func<WebSocketMessage, WebSocketSessionInfo, CancellationToken, Task<MessageRoutingResult>>> _customHandlers = new();
    private readonly ConcurrentDictionary<MessageRoutingDestination, long> _routingCounts = new();
    private long _totalMessages;
    private long _successfulMessages;
    private long _failedMessages;

    public WebSocketMessageRouter(ILogger<WebSocketMessageRouter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MessageRoutingResult> RouteMessageAsync(
        WebSocketMessage message,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sessionInfo);

        Interlocked.Increment(ref _totalMessages);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Routing message {MessageId} of type {MessageType} for session {SessionId}",
                message.MessageId, message.Type, sessionInfo.SessionId);

            // Check for custom handler first
            if (_customHandlers.TryGetValue(message.Type, out var customHandler))
            {
                var result = await customHandler(message, sessionInfo, cancellationToken);
                stopwatch.Stop();

                if (result.Success)
                {
                    Interlocked.Increment(ref _successfulMessages);
                    _routingCounts.AddOrUpdate(MessageRoutingDestination.CustomHandler, 1, (_, count) => count + 1);
                }
                else
                {
                    Interlocked.Increment(ref _failedMessages);
                }

                return result with { ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds };
            }

            // Default routing logic - basic acknowledgment for now
            stopwatch.Stop();
            Interlocked.Increment(ref _successfulMessages);
            _routingCounts.AddOrUpdate(MessageRoutingDestination.DirectChatService, 1, (_, count) => count + 1);

            _logger.LogDebug("Message {MessageId} routed successfully using default handler", message.MessageId);

            return MessageRoutingResult.CreateSuccess(
                MessageRoutingDestination.DirectChatService,
                processingTimeMs: stopwatch.Elapsed.TotalMilliseconds,
                directServiceUsed: true);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Interlocked.Increment(ref _failedMessages);

            _logger.LogError(ex, "Error routing message {MessageId} for session {SessionId}",
                message.MessageId, sessionInfo.SessionId);

            return MessageRoutingResult.CreateFailure(
                $"Routing error: {ex.Message}",
                processingTimeMs: stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    public Task<MessageRoutingResult> RouteToDestinationAsync(
        WebSocketMessage message,
        MessageRoutingDestination destination,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sessionInfo);

        _logger.LogDebug("Routing message {MessageId} to explicit destination {Destination}",
            message.MessageId, destination);

        // For now, just acknowledge the routing
        Interlocked.Increment(ref _successfulMessages);
        _routingCounts.AddOrUpdate(destination, 1, (_, count) => count + 1);

        return Task.FromResult(MessageRoutingResult.CreateSuccess(destination));
    }

    public Task<MessageRoutingResult> RouteProtocolMessageAsync(
        WebSocketMessage message,
        string protocol,
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        ArgumentNullException.ThrowIfNull(sessionInfo);

        _logger.LogDebug("Routing protocol message {MessageId} for protocol {Protocol}",
            message.MessageId, protocol);

        // Route based on protocol
        return RouteMessageAsync(message, sessionInfo, cancellationToken);
    }

    public async Task<List<MessageRoutingResult>> BroadcastMessageAsync(
        WebSocketMessage message,
        IEnumerable<string> targetSessions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(targetSessions);

        var sessions = targetSessions.ToList();
        _logger.LogDebug("Broadcasting message {MessageId} to {SessionCount} sessions",
            message.MessageId, sessions.Count);

        var results = new List<MessageRoutingResult>();

        foreach (var sessionId in sessions)
        {
            // For now, just return success for each target
            // In a real implementation, this would actually send to each session
            results.Add(MessageRoutingResult.CreateSuccess(
                MessageRoutingDestination.Broadcast));
        }

        _routingCounts.AddOrUpdate(MessageRoutingDestination.Broadcast, sessions.Count, (_, count) => count + sessions.Count);
        return results;
    }

    public Task<MessageRoutingResult> RouteHeartbeatAsync(
        WebSocketSessionInfo sessionInfo,
        Dictionary<string, object>? heartbeatData = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionInfo);

        _logger.LogTrace("Routing heartbeat for session {SessionId}", sessionInfo.SessionId);

        _routingCounts.AddOrUpdate(MessageRoutingDestination.HeartbeatHandler, 1, (_, count) => count + 1);

        return Task.FromResult(MessageRoutingResult.CreateSuccess(
            MessageRoutingDestination.HeartbeatHandler));
    }

    public Task<bool> RegisterMessageHandlerAsync(
        string messageType,
        Func<WebSocketMessage, WebSocketSessionInfo, CancellationToken, Task<MessageRoutingResult>> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentNullException.ThrowIfNull(handler);

        var added = _customHandlers.TryAdd(messageType, handler);

        if (added)
        {
            _logger.LogInformation("Registered custom handler for message type: {MessageType}", messageType);
        }
        else
        {
            _logger.LogWarning("Handler already registered for message type: {MessageType}", messageType);
        }

        return Task.FromResult(added);
    }

    public Task<bool> UnregisterMessageHandlerAsync(
        string messageType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);

        var removed = _customHandlers.TryRemove(messageType, out _);

        if (removed)
        {
            _logger.LogInformation("Unregistered handler for message type: {MessageType}", messageType);
        }

        return Task.FromResult(removed);
    }

    public Task<MessageRoutingStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var stats = new MessageRoutingStatistics
        {
            TotalMessages = Interlocked.Read(ref _totalMessages),
            SuccessfulMessages = Interlocked.Read(ref _successfulMessages),
            FailedMessages = Interlocked.Read(ref _failedMessages),
            MessagesByDestination = new Dictionary<MessageRoutingDestination, long>(_routingCounts),
            MessagesByType = [], // Would track message types in real implementation
            MessagesByProtocol = [], // Would track protocols in real implementation
            AverageProcessingTimeMs = 0, // Would calculate average in real implementation
            OrleansAttempts = 0, // Would track Orleans attempts in real implementation
            DirectServiceUses = Interlocked.Read(ref _successfulMessages),
            CollectedAt = DateTime.UtcNow
        };

        return Task.FromResult(stats);
    }

    public Task<bool> IsOrleansRoutingAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        // For now, Orleans routing is not implemented
        // In a real implementation, this would check Orleans health
        return Task.FromResult(false);
    }

    public Task<MessageRouterHealthStatus> GetHealthStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var healthStatus = new MessageRouterHealthStatus
        {
            IsHealthy = true, // Basic health check
            IsOrleansAvailable = false, // Orleans not implemented yet
            IsDirectServiceAvailable = true,
            CustomHandlerCount = _customHandlers.Count,
            Message = "WebSocket message router operational",
            Timestamp = DateTime.UtcNow
        };

        return Task.FromResult(healthStatus);
    }
}
