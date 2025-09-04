using System.Text.Json;
using AIChat.Orleans.Configuration;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Timers;

namespace AIChat.Orleans.Grains;

/// <summary>
/// User grain implementation providing user-centric operations.
/// Maintains user state, connections, and handles message routing.
/// </summary>
public sealed class UserGrain : Grain<UserGrainState>, IUserGrain
{
    private readonly ILogger<UserGrain> _logger;
    private readonly OrleansGrainConfiguration _configuration;
    private IGrainTimer? _cleanupTimer;
    private IGrainTimer? _metricsTimer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the UserGrain.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics</param>
    /// <param name="configuration">Configuration for Orleans grains</param>
    public UserGrain(
        ILogger<UserGrain> logger,
        IOptionsSnapshot<OrleansGrainConfiguration> configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value ?? new OrleansGrainConfiguration();
    }

    /// <inheritdoc />
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        // Initialize state if new grain
        if (string.IsNullOrEmpty(State.UserId))
        {
            State.UserId = this.GetPrimaryKeyString();
            State.ActivatedAt = DateTime.UtcNow;
            State.Metrics.ActivationCount = 1;
        }
        else
        {
            State.Metrics.ActivationCount++;
        }

        // Setup periodic timers if enabled
        if (_configuration.UserGrain.EnablePeriodicTimers)
        {
            // Setup periodic cleanup timer using Orleans 9.x API
            _cleanupTimer = this.RegisterGrainTimer(
                async _ => await CleanupStateAsync(null),
                new GrainTimerCreationOptions
                {
                    DueTime = TimeSpan.FromMinutes(_configuration.UserGrain.CleanupIntervalMinutes),
                    Period = TimeSpan.FromMinutes(_configuration.UserGrain.CleanupIntervalMinutes),
                    Interleave = true
                });

            // Setup metrics timer using Orleans 9.x API
            _metricsTimer = this.RegisterGrainTimer(
                async _ => await UpdateMetricsAsync(null),
                new GrainTimerCreationOptions
                {
                    DueTime = TimeSpan.FromMinutes(_configuration.UserGrain.MetricsUpdateIntervalMinutes),
                    Period = TimeSpan.FromMinutes(_configuration.UserGrain.MetricsUpdateIntervalMinutes),
                    Interleave = true
                });
        }

        await WriteStateAsync();

        _logger.LogInformation(
            "UserGrain activated for {UserId}. Activation #{ActivationCount}",
            State.UserId,
            State.Metrics.ActivationCount);
    }

    /// <inheritdoc />
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "UserGrain deactivating for {UserId}. Reason: {Reason}",
            State.UserId,
            reason);

        // Cleanup timers with proper thread safety
        await DisposeTimersAsync();

        // Save final state if configured
        if (_configuration.Persistence.PersistOnDeactivation)
        {
            await PersistStateWithRetryAsync();
        }

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    /// <summary>
    /// Safely disposes timers with proper locking to prevent memory leaks.
    /// This method follows Orleans best practices for timer management:
    /// 1. Sets disposal flag to prevent new timer callbacks from executing
    /// 2. Disposes timers under lock to ensure thread safety
    /// 3. Nullifies timer references to allow GC collection
    /// 4. Waits briefly to allow any in-flight callbacks to complete
    /// </summary>
    private Task DisposeTimersAsync()
    {
        // Set disposal flag first to signal timer callbacks to exit early
        _disposed = true;

        // Dispose and nullify cleanup timer
        if (_cleanupTimer != null)
        {
            _cleanupTimer.Dispose();
            _cleanupTimer = null;
        }

        // Dispose and nullify metrics timer
        if (_metricsTimer != null)
        {
            _metricsTimer.Dispose();
            _metricsTimer = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Persists state with retry logic.
    /// </summary>
    private async Task PersistStateWithRetryAsync()
    {
        var retryCount = 0;
        var maxRetries = _configuration.Persistence.MaxPersistenceRetries;
        var retryDelay = TimeSpan.FromMilliseconds(_configuration.Persistence.PersistenceRetryDelayMilliseconds);

        while (retryCount < maxRetries)
        {
            try
            {
                await WriteStateAsync();
                return;
            }
            catch (Exception ex) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                _logger.LogWarning(ex,
                    "Failed to save state for {UserId} (attempt {Attempt}/{MaxAttempts}). Retrying...",
                    State.UserId, retryCount, maxRetries);

                await Task.Delay(retryDelay);
                retryDelay = TimeSpan.FromMilliseconds(Math.Min(retryDelay.TotalMilliseconds * 2, 5000));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to save state for {UserId} after {MaxAttempts} attempts",
                    State.UserId, maxRetries);
                throw;
            }
        }
    }

    #region Phase 1: Shadow Mode Operations

    /// <inheritdoc />
    public async Task RecordActivity(ActivityType type, string metadata)
    {
        try
        {
            var activity = new ActivityRecord
            {
                Type = type,
                Metadata = metadata,
                Timestamp = DateTime.UtcNow,
                CorrelationId = Guid.NewGuid().ToString()
            };

            State.RecentActivity.Enqueue(activity);

            // Maintain circular buffer with configurable size
            while (State.RecentActivity.Count > _configuration.UserGrain.MaxActivityBufferSize)
            {
                State.RecentActivity.Dequeue();
            }

            State.LastActivity = DateTime.UtcNow;
            State.Metrics.TotalActivities++;

            // Save state periodically based on configuration
            if (State.Metrics.TotalActivities % _configuration.Persistence.ActivityPersistenceInterval == 0)
            {
                await WriteStateAsync();
            }

            _logger.LogDebug(
                "Activity recorded for {UserId}: {ActivityType} - {Metadata}",
                State.UserId,
                type,
                metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to record activity for {UserId}. Type: {ActivityType}",
                State.UserId,
                type);

            // Don't throw in shadow mode
        }
    }

    /// <inheritdoc />
    public Task<UserGrainState> GetState()
    {
        _logger.LogDebug("State requested for {UserId}", State.UserId);
        return Task.FromResult(State);
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealth()
    {
        try
        {
            var warnings = new List<string>();

            // Check for stale connections
            var staleThreshold = TimeSpan.FromMinutes(_configuration.Connections.StaleConnectionThresholdMinutes);
            var staleConnections = State.Connections.Values
                .Where(c => DateTime.UtcNow - c.LastActivity > staleThreshold)
                .Count();

            if (staleConnections > 0)
            {
                warnings.Add($"{staleConnections} stale connections detected");
            }

            // Check memory pressure from activities
            var warningThreshold = (int)(_configuration.UserGrain.MaxActivityBufferSize * 0.8);
            if (State.RecentActivity.Count > warningThreshold)
            {
                warnings.Add("Activity queue near capacity");
            }

            // Check for old active operations
            var staleOperations = State.ActiveOperations.Values
                .Where(op => DateTime.UtcNow - op.StartedAt > TimeSpan.FromMinutes(10)
                           && op.Status == OperationStatus.InProgress)
                .Count();

            if (staleOperations > 0)
            {
                warnings.Add($"{staleOperations} stale operations detected");
            }

            var result = new HealthCheckResult
            {
                IsHealthy = warnings.Count == 0,
                GrainId = State.UserId,
                LastActivity = State.LastActivity,
                Metrics = State.Metrics,
                CheckedAt = DateTime.UtcNow,
                AdditionalInfo = $"Connections: {State.Connections.Count}, Chats: {State.ActiveChats.Count}",
                Warnings = warnings
            };

            _logger.LogDebug("Health check completed for {UserId}. Healthy: {IsHealthy}",
                State.UserId, result.IsHealthy);

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for {UserId}", State.UserId);

            return Task.FromResult(new HealthCheckResult
            {
                IsHealthy = false,
                GrainId = State.UserId,
                CheckedAt = DateTime.UtcNow,
                AdditionalInfo = $"Health check exception: {ex.Message}",
                Warnings = new List<string> { "Health check threw exception" }
            });
        }
    }

    #endregion

    #region Phase 2: Active Connection Management

    /// <inheritdoc />
    public async Task RegisterConnection(string connectionId, string clientId)
    {
        try
        {
            // Validate parameters
            if (string.IsNullOrEmpty(connectionId))
            {
                throw new ArgumentException("Connection ID cannot be null or empty", nameof(connectionId));
            }

            if (string.IsNullOrEmpty(clientId))
            {
                throw new ArgumentException("Client ID cannot be null or empty", nameof(clientId));
            }

            // Check if connection already exists
            if (State.Connections.ContainsKey(connectionId))
            {
                _logger.LogWarning(
                    "Connection {ConnectionId} already registered for {UserId}. Updating existing connection.",
                    connectionId, State.UserId);

                // Update existing connection's client ID and last activity
                State.Connections[connectionId].ClientId = clientId;
                State.Connections[connectionId].LastActivity = DateTime.UtcNow;
            }
            else
            {
                // Create new connection info
                var connectionInfo = new ConnectionInfo
                {
                    ConnectionId = connectionId,
                    ClientId = clientId,
                    ConnectedAt = DateTime.UtcNow,
                    LastActivity = DateTime.UtcNow,
                    SubscribedChatIds = new HashSet<string>()
                };

                // Add to connections dictionary
                State.Connections[connectionId] = connectionInfo;

                _logger.LogInformation(
                    "Connection {ConnectionId} registered for {UserId} from client {ClientId}",
                    connectionId, State.UserId, clientId);
            }

            // Update metrics
            State.Metrics.ActiveConnections = State.Connections.Count;
            State.LastActivity = DateTime.UtcNow;

            // Record activity
            await RecordActivity(ActivityType.Connected,
                JsonSerializer.Serialize(new { ConnectionId = connectionId, ClientId = clientId }));

            // Save state
            await WriteStateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to register connection {ConnectionId} for {UserId}",
                connectionId, State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UnregisterConnection(string connectionId)
    {
        try
        {
            // Validate parameters
            if (string.IsNullOrEmpty(connectionId))
            {
                throw new ArgumentException("Connection ID cannot be null or empty", nameof(connectionId));
            }

            // Check if connection exists
            if (!State.Connections.TryGetValue(connectionId, out var connectionInfo))
            {
                _logger.LogWarning(
                    "Connection {ConnectionId} not found for {UserId}. Ignoring unregistration.",
                    connectionId, State.UserId);
                return;
            }

            // Get list of subscribed chats before removing connection
            var subscribedChats = connectionInfo.SubscribedChatIds.ToList();

            // Remove connection from all subscribed chats
            foreach (var chatId in subscribedChats)
            {
                if (State.ActiveChats.TryGetValue(chatId, out var subscription))
                {
                    subscription.ConnectionCount--;

                    // Remove chat subscription if no more connections are subscribed
                    if (subscription.ConnectionCount <= 0)
                    {
                        State.ActiveChats.Remove(chatId);
                        _logger.LogDebug(
                            "Removed chat subscription {ChatId} for {UserId} (no remaining connections)",
                            chatId, State.UserId);
                    }
                }
            }

            // Remove connection from dictionary
            State.Connections.Remove(connectionId);

            // Update metrics
            State.Metrics.ActiveConnections = State.Connections.Count;
            State.LastActivity = DateTime.UtcNow;

            // Record activity
            await RecordActivity(ActivityType.Disconnected,
                JsonSerializer.Serialize(new { ConnectionId = connectionId, SubscribedChats = subscribedChats }));

            // Save state
            await WriteStateAsync();

            _logger.LogInformation(
                "Connection {ConnectionId} unregistered for {UserId}. Removed from {ChatCount} chats.",
                connectionId, State.UserId, subscribedChats.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to unregister connection {ConnectionId} for {UserId}",
                connectionId, State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SubscribeToChat(string connectionId, string chatId)
    {
        try
        {
            // Validate parameters
            if (string.IsNullOrEmpty(connectionId))
            {
                throw new ArgumentException("Connection ID cannot be null or empty", nameof(connectionId));
            }

            if (string.IsNullOrEmpty(chatId))
            {
                throw new ArgumentException("Chat ID cannot be null or empty", nameof(chatId));
            }

            // Check if connection exists
            if (!State.Connections.TryGetValue(connectionId, out var connectionInfo))
            {
                _logger.LogWarning(
                    "Connection {ConnectionId} not found for {UserId}. Cannot subscribe to chat {ChatId}.",
                    connectionId, State.UserId, chatId);
                throw new InvalidOperationException($"Connection {connectionId} not found");
            }

            // Add chat to connection's subscribed chats
            bool isNewSubscription = connectionInfo.SubscribedChatIds.Add(chatId);

            if (!isNewSubscription)
            {
                _logger.LogDebug(
                    "Connection {ConnectionId} already subscribed to chat {ChatId} for {UserId}",
                    connectionId, chatId, State.UserId);
                return;
            }

            // Update or create chat subscription
            if (State.ActiveChats.TryGetValue(chatId, out var subscription))
            {
                // Update existing subscription
                subscription.ConnectionCount++;
                subscription.State = SubscriptionState.Active;
            }
            else
            {
                // Create new subscription
                subscription = new ChatSubscription
                {
                    ChatId = chatId,
                    SubscribedAt = DateTime.UtcNow,
                    State = SubscriptionState.Active,
                    ConnectionCount = 1
                };
                State.ActiveChats[chatId] = subscription;
            }

            // Update connection activity
            connectionInfo.LastActivity = DateTime.UtcNow;
            State.LastActivity = DateTime.UtcNow;

            // Record activity
            await RecordActivity(ActivityType.ChatSubscribed,
                JsonSerializer.Serialize(new { ConnectionId = connectionId, ChatId = chatId }));

            // Save state
            await WriteStateAsync();

            _logger.LogInformation(
                "Connection {ConnectionId} subscribed to chat {ChatId} for {UserId}. Total connections in chat: {ConnectionCount}",
                connectionId, chatId, State.UserId, subscription.ConnectionCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to subscribe connection {ConnectionId} to chat {ChatId} for {UserId}",
                connectionId, chatId, State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UnsubscribeFromChat(string connectionId, string chatId)
    {
        try
        {
            // Validate parameters
            if (string.IsNullOrEmpty(connectionId))
            {
                throw new ArgumentException("Connection ID cannot be null or empty", nameof(connectionId));
            }

            if (string.IsNullOrEmpty(chatId))
            {
                throw new ArgumentException("Chat ID cannot be null or empty", nameof(chatId));
            }

            // Check if connection exists
            if (!State.Connections.TryGetValue(connectionId, out var connectionInfo))
            {
                _logger.LogWarning(
                    "Connection {ConnectionId} not found for {UserId}. Cannot unsubscribe from chat {ChatId}.",
                    connectionId, State.UserId, chatId);
                return;
            }

            // Remove chat from connection's subscribed chats
            bool wasSubscribed = connectionInfo.SubscribedChatIds.Remove(chatId);

            if (!wasSubscribed)
            {
                _logger.LogDebug(
                    "Connection {ConnectionId} was not subscribed to chat {ChatId} for {UserId}",
                    connectionId, chatId, State.UserId);
                return;
            }

            // Update chat subscription
            if (State.ActiveChats.TryGetValue(chatId, out var subscription))
            {
                subscription.ConnectionCount--;

                // Remove subscription if no more connections
                if (subscription.ConnectionCount <= 0)
                {
                    State.ActiveChats.Remove(chatId);
                    _logger.LogDebug(
                        "Removed chat subscription {ChatId} for {UserId} (no remaining connections)",
                        chatId, State.UserId);
                }
                else
                {
                    // Check if any connections are still active
                    var activeConnectionCount = State.Connections.Values
                        .Count(c => c.SubscribedChatIds.Contains(chatId));

                    if (activeConnectionCount == 0)
                    {
                        subscription.State = SubscriptionState.Inactive;
                    }
                }
            }

            // Update connection activity
            connectionInfo.LastActivity = DateTime.UtcNow;
            State.LastActivity = DateTime.UtcNow;

            // Record activity
            await RecordActivity(ActivityType.ChatUnsubscribed,
                JsonSerializer.Serialize(new { ConnectionId = connectionId, ChatId = chatId }));

            // Save state
            await WriteStateAsync();

            _logger.LogInformation(
                "Connection {ConnectionId} unsubscribed from chat {ChatId} for {UserId}",
                connectionId, chatId, State.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to unsubscribe connection {ConnectionId} from chat {ChatId} for {UserId}",
                connectionId, chatId, State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RelayMessage(ChatMessage message)
    {
        try
        {
            // Validate message
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (string.IsNullOrEmpty(message.ChatId))
            {
                throw new ArgumentException("Message must have a ChatId", nameof(message));
            }

            // Find all connections subscribed to this chat
            var subscribedConnections = GetConnectionsForChat(message.ChatId);

            if (!subscribedConnections.Any())
            {
                _logger.LogDebug(
                    "No active connections subscribed to chat {ChatId} for {UserId}. Message {MessageId} not relayed.",
                    message.ChatId, State.UserId, message.Id);
                return;
            }

            // Relay message to each subscribed connection
            var relayTasks = new List<Task>();
            var successCount = 0;
            var failureCount = 0;

            foreach (var connection in subscribedConnections)
            {
                try
                {
                    // TODO: In Phase 2 complete integration, this will call SignalR hub to send message
                    // For now, we just track the relay intent
                    await Task.Run(() =>
                    {
                        _logger.LogTrace(
                            "Would relay message {MessageId} to connection {ConnectionId} for {UserId}",
                            message.Id, connection.ConnectionId, State.UserId);
                    });

                    // Update connection activity
                    connection.LastActivity = DateTime.UtcNow;
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to relay message {MessageId} to connection {ConnectionId} for {UserId}",
                        message.Id, connection.ConnectionId, State.UserId);
                    failureCount++;
                }
            }

            // Update metrics
            State.Metrics.MessagesRelayed++;
            State.LastActivity = DateTime.UtcNow;

            // Save state periodically based on configuration
            if (State.Metrics.MessagesRelayed % _configuration.Persistence.MessagePersistenceInterval == 0)
            {
                await WriteStateAsync();
            }

            _logger.LogInformation(
                "Relayed message {MessageId} for chat {ChatId} to {SuccessCount} connections ({FailureCount} failures) for {UserId}",
                message.Id, message.ChatId, successCount, failureCount, State.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to relay message {MessageId} for {UserId}",
                message?.Id ?? "null", State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RelayStreamChunk(StreamChunk chunk)
    {
        try
        {
            // Validate chunk
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            if (string.IsNullOrEmpty(chunk.ChatId))
            {
                throw new ArgumentException("Chunk must have a ChatId", nameof(chunk));
            }

            if (string.IsNullOrEmpty(chunk.OperationId))
            {
                throw new ArgumentException("Chunk must have an OperationId", nameof(chunk));
            }

            // Find all connections subscribed to this chat
            var subscribedConnections = GetConnectionsForChat(chunk.ChatId);

            if (!subscribedConnections.Any())
            {
                _logger.LogDebug(
                    "No active connections subscribed to chat {ChatId} for {UserId}. Chunk {ChunkIndex} of operation {OperationId} not relayed.",
                    chunk.ChatId, State.UserId, chunk.ChunkIndex, chunk.OperationId);
                return;
            }

            // Relay chunk to each subscribed connection
            var successCount = 0;
            var failureCount = 0;

            foreach (var connection in subscribedConnections)
            {
                try
                {
                    // TODO: In Phase 2 complete integration, this will call SignalR hub to send chunk
                    // For now, we just track the relay intent
                    await Task.Run(() =>
                    {
                        _logger.LogTrace(
                            "Would relay chunk {ChunkIndex} of operation {OperationId} to connection {ConnectionId} for {UserId}",
                            chunk.ChunkIndex, chunk.OperationId, connection.ConnectionId, State.UserId);
                    });

                    // Update connection activity
                    connection.LastActivity = DateTime.UtcNow;
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to relay chunk {ChunkIndex} of operation {OperationId} to connection {ConnectionId} for {UserId}",
                        chunk.ChunkIndex, chunk.OperationId, connection.ConnectionId, State.UserId);
                    failureCount++;
                }
            }

            // Update last activity
            State.LastActivity = DateTime.UtcNow;

            // Log completion for final chunks
            if (chunk.IsComplete)
            {
                _logger.LogInformation(
                    "Completed relaying all chunks for operation {OperationId} in chat {ChatId} to {ConnectionCount} connections for {UserId}",
                    chunk.OperationId, chunk.ChatId, successCount, State.UserId);

                // Save state on completion
                await WriteStateAsync();
            }
            else if (chunk.ChunkIndex % _configuration.Persistence.StreamChunkPersistenceInterval == 0)
            {
                // Save state periodically for long streams
                await WriteStateAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to relay chunk for operation {OperationId} for {UserId}",
                chunk?.OperationId ?? "null", State.UserId);
            throw;
        }
    }

    #endregion

    #region Phase 3: Background Processing (Stubbed for Phase 1)

    /// <inheritdoc />
    public Task<string> ProcessMessageWithBackground(ChatMessage message)
    {
        _logger.LogDebug("ProcessMessageWithBackground called in Phase 1 (stubbed) for {UserId}: {MessageId}",
            State.UserId, message.Id);

        // Phase 3 implementation will go here
        var operationId = Guid.NewGuid().ToString();
        return Task.FromResult(operationId);
    }

    /// <inheritdoc />
    public Task NotifyOperationStarted(string operationId, string chatId)
    {
        _logger.LogDebug("NotifyOperationStarted called in Phase 1 (stubbed) for {UserId}: {OperationId}",
            State.UserId, operationId);

        // Phase 3 implementation will go here
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyOperationCompleted(string operationId, bool success, string? error = null)
    {
        _logger.LogDebug("NotifyOperationCompleted called in Phase 1 (stubbed) for {UserId}: {OperationId} Success: {Success}",
            State.UserId, operationId, success);

        // Phase 3 implementation will go here
        return Task.CompletedTask;
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Gets all active connections subscribed to a specific chat.
    /// </summary>
    /// <param name="chatId">The chat ID to filter connections for</param>
    /// <returns>List of active connections subscribed to the chat</returns>
    private IEnumerable<ConnectionInfo> GetConnectionsForChat(string chatId)
    {
        if (string.IsNullOrEmpty(chatId))
        {
            return Enumerable.Empty<ConnectionInfo>();
        }

        // Filter connections that are:
        // 1. Subscribed to this chat
        // 2. Still active (not stale)
        var staleThreshold = DateTime.UtcNow.AddMinutes(-_configuration.Connections.StaleConnectionThresholdMinutes);

        return State.Connections.Values
            .Where(c => c.SubscribedChatIds.Contains(chatId) &&
                       c.LastActivity > staleThreshold)
            .ToList();
    }

    /// <summary>
    /// Checks if a connection is active (not stale).
    /// </summary>
    /// <param name="connectionId">The connection ID to check</param>
    /// <returns>True if the connection is active, false otherwise</returns>
    private bool IsConnectionActive(string connectionId)
    {
        if (!State.Connections.TryGetValue(connectionId, out var connection))
        {
            return false;
        }

        // Consider connection stale after configured minutes of inactivity
        var staleThreshold = DateTime.UtcNow.AddMinutes(-_configuration.Connections.StaleConnectionThresholdMinutes);
        return connection.LastActivity > staleThreshold;
    }

    /// <summary>
    /// Gets all active connections for this user.
    /// </summary>
    /// <returns>List of active connections</returns>
    private IEnumerable<ConnectionInfo> GetActiveConnections()
    {
        var staleThreshold = DateTime.UtcNow.AddMinutes(-_configuration.Connections.StaleConnectionThresholdMinutes);
        return State.Connections.Values
            .Where(c => c.LastActivity > staleThreshold)
            .ToList();
    }

    /// <summary>
    /// Validates if a connection exists and is active.
    /// </summary>
    /// <param name="connectionId">The connection ID to validate</param>
    /// <returns>The connection info if valid, null otherwise</returns>
    private ConnectionInfo? ValidateConnection(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return null;
        }

        if (!State.Connections.TryGetValue(connectionId, out var connection))
        {
            return null;
        }

        // Check if connection is not stale
        var staleThreshold = DateTime.UtcNow.AddMinutes(-_configuration.Connections.StaleConnectionThresholdMinutes);
        if (connection.LastActivity <= staleThreshold)
        {
            _logger.LogWarning(
                "Connection {ConnectionId} for {UserId} is stale (last activity: {LastActivity})",
                connectionId, State.UserId, connection.LastActivity);
            return null;
        }

        return connection;
    }

    /// <summary>
    /// Broadcasts a message to all connections subscribed to a chat.
    /// </summary>
    /// <param name="chatId">The chat ID to broadcast to</param>
    /// <param name="payload">The payload to broadcast</param>
    /// <returns>Statistics about the broadcast operation</returns>
    private async Task<(int SuccessCount, int FailureCount)> BroadcastToChat(string chatId, object payload)
    {
        var connections = GetConnectionsForChat(chatId);
        var successCount = 0;
        var failureCount = 0;

        foreach (var connection in connections)
        {
            try
            {
                // TODO: In Phase 2 complete integration, this will call SignalR hub
                // For now, we just track the broadcast intent
                await Task.Run(() =>
                {
                    _logger.LogTrace(
                        "Would broadcast to connection {ConnectionId} in chat {ChatId} for {UserId}",
                        connection.ConnectionId, chatId, State.UserId);
                });

                connection.LastActivity = DateTime.UtcNow;
                successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to broadcast to connection {ConnectionId} in chat {ChatId} for {UserId}",
                    connection.ConnectionId, chatId, State.UserId);
                failureCount++;
            }
        }

        return (successCount, failureCount);
    }

    /// <summary>
    /// Handles connection recovery by transferring state from an old connection to a new one.
    /// </summary>
    /// <param name="oldConnectionId">The old connection ID being replaced</param>
    /// <param name="newConnectionId">The new connection ID</param>
    /// <param name="clientId">The client identifier</param>
    /// <returns>True if recovery was successful, false otherwise</returns>
    private async Task<bool> RecoverConnection(string oldConnectionId, string newConnectionId, string clientId)
    {
        try
        {
            // Check if old connection exists
            if (!State.Connections.TryGetValue(oldConnectionId, out var oldConnection))
            {
                _logger.LogWarning(
                    "Cannot recover connection {OldConnectionId} for {UserId}: connection not found",
                    oldConnectionId, State.UserId);
                return false;
            }

            // Check grace period for reconnection
            var gracePeriod = TimeSpan.FromMinutes(_configuration.Connections.ReconnectionGracePeriodMinutes);
            if (DateTime.UtcNow - oldConnection.LastActivity > gracePeriod)
            {
                _logger.LogWarning(
                    "Cannot recover connection {OldConnectionId} for {UserId}: grace period expired",
                    oldConnectionId, State.UserId);
                return false;
            }

            // Create new connection with transferred state
            var newConnection = new ConnectionInfo
            {
                ConnectionId = newConnectionId,
                ClientId = clientId,
                ConnectedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                SubscribedChatIds = new HashSet<string>(oldConnection.SubscribedChatIds)
            };

            // Remove old connection and add new one
            State.Connections.Remove(oldConnectionId);
            State.Connections[newConnectionId] = newConnection;

            // Record recovery activity
            await RecordActivity(ActivityType.Connected,
                JsonSerializer.Serialize(new
                {
                    Event = "ConnectionRecovered",
                    OldConnectionId = oldConnectionId,
                    NewConnectionId = newConnectionId,
                    ClientId = clientId,
                    RecoveredSubscriptions = oldConnection.SubscribedChatIds.Count
                }));

            // Save state
            await WriteStateAsync();

            _logger.LogInformation(
                "Successfully recovered connection {OldConnectionId} to {NewConnectionId} for {UserId} with {SubscriptionCount} subscriptions",
                oldConnectionId, newConnectionId, State.UserId, oldConnection.SubscribedChatIds.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to recover connection {OldConnectionId} to {NewConnectionId} for {UserId}",
                oldConnectionId, newConnectionId, State.UserId);
            return false;
        }
    }

    /// <summary>
    /// Periodic cleanup of old data to prevent memory leaks.
    /// This timer callback implements proper disposal checking to prevent execution after grain deactivation.
    /// The disposal check happens under lock and returns early if the grain is being deactivated.
    /// </summary>
    private async Task CleanupStateAsync(object? state)
    {
        // CRITICAL: Check disposal flag under lock to prevent execution after deactivation
        // This ensures timer callbacks don't execute during or after grain deactivation
        if (_disposed)
        {
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            var cleanupThreshold = TimeSpan.FromHours(_configuration.UserGrain.ActivityRetentionHours);
            var changed = false;

            // Clean up old activity records
            var oldActivitiesCount = State.RecentActivity.Count;
            var tempActivity = new Queue<ActivityRecord>();

            while (State.RecentActivity.Count > 0)
            {
                var activity = State.RecentActivity.Dequeue();
                if (now - activity.Timestamp <= cleanupThreshold)
                {
                    tempActivity.Enqueue(activity);
                }
            }

            State.RecentActivity = tempActivity;

            if (State.RecentActivity.Count != oldActivitiesCount)
            {
                changed = true;
                _logger.LogDebug("Cleaned up {Count} old activities for {UserId}",
                    oldActivitiesCount - State.RecentActivity.Count, State.UserId);
            }

            // Clean up completed operations (Phase 3 data)
            var completedOps = State.ActiveOperations
                .Where(kvp => kvp.Value.Status == OperationStatus.Completed
                           && kvp.Value.CompletedAt.HasValue
                           && now - kvp.Value.CompletedAt.Value > TimeSpan.FromMinutes(_configuration.UserGrain.CompletedOperationRetentionMinutes))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var opId in completedOps)
            {
                State.ActiveOperations.Remove(opId);
                changed = true;
            }

            if (completedOps.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} completed operations for {UserId}",
                    completedOps.Count, State.UserId);
            }

            // Save state if changes were made
            if (changed)
            {
                await WriteStateAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup state for {UserId}", State.UserId);
        }
    }

    /// <summary>
    /// Updates metrics periodically.
    /// This timer callback implements proper disposal checking to prevent execution after grain deactivation.
    /// The disposal check happens under lock and returns early if the grain is being deactivated.
    /// </summary>
    private async Task UpdateMetricsAsync(object? state)
    {
        // CRITICAL: Check disposal flag under lock to prevent execution after deactivation
        // This ensures timer callbacks don't execute during or after grain deactivation
        if (_disposed)
        {
            return;
        }

        try
        {
            // Update connection count
            State.Metrics.ActiveConnections = State.Connections.Count;

            // Save metrics periodically
            await WriteStateAsync();

            _logger.LogTrace("Metrics updated for {UserId}: Connections={ConnectionCount}, Activities={ActivityCount}",
                State.UserId, State.Metrics.ActiveConnections, State.Metrics.TotalActivities);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update metrics for {UserId}", State.UserId);
        }
    }

    #endregion
}
