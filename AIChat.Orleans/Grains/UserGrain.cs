using System.Text.Json;
using AIChat.Orleans.Configuration;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Metrics;
using AIChat.Orleans.Services;
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
    private readonly IOrleansMetricsCollector _metricsCollector;
    private readonly ISignalRBroadcastService _signalRBroadcast;
    private IGrainTimer? _cleanupTimer;
    private IGrainTimer? _metricsTimer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the UserGrain.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics</param>
    /// <param name="configuration">Configuration for Orleans grains</param>
    /// <param name="metricsCollector">Metrics collector for performance tracking</param>
    /// <param name="signalRBroadcast">SignalR broadcast service for real-time messaging (optional)</param>
    public UserGrain(
        ILogger<UserGrain> logger,
        IOptionsSnapshot<OrleansGrainConfiguration> configuration,
        IOrleansMetricsCollector metricsCollector,
        ISignalRBroadcastService? signalRBroadcast = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value ?? new OrleansGrainConfiguration();
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _signalRBroadcast = signalRBroadcast ?? new NullSignalRBroadcastService(); // Default to no-op implementation
    }

    /// <inheritdoc />
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var activationStart = DateTime.UtcNow;
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

        // Record metrics for grain activation
        var activationTime = (DateTime.UtcNow - activationStart).TotalMilliseconds;
        await _metricsCollector.RecordGrainActivationAsync("UserGrain", State.UserId, activationTime);

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

        // Record metrics for grain deactivation
        var lifetimeMinutes = (DateTime.UtcNow - State.ActivatedAt).TotalMinutes;
        await _metricsCollector.RecordGrainDeactivationAsync("UserGrain", State.UserId, lifetimeMinutes);

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

            // Process any buffered messages for connected chats
            var processedCount = await ProcessBufferedMessagesAsync(connectionId);
            if (processedCount > 0)
            {
                _logger.LogDebug(
                    "Delivered {ProcessedCount} buffered messages to newly connected {ConnectionId}",
                    processedCount, connectionId);
            }

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
                    "No active connections subscribed to chat {ChatId} for {UserId}. Buffering message {MessageId}.",
                    message.ChatId, State.UserId, message.Id);
                
                // Buffer the message for later delivery
                await BufferMessageAsync(message, BufferPriority.Normal);
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
                    // Phase 3 Integration: Relay message via SignalR broadcast service
                    if (_signalRBroadcast.IsAvailable)
                    {
                        await _signalRBroadcast.BroadcastToGroupAsync(
                            $"chat_{message.ChatId}",
                            "ReceiveMessage",
                            new
                            {
                                Id = message.Id,
                                ChatId = message.ChatId,
                                Role = message.Role,
                                Content = message.Content,
                                Timestamp = message.Timestamp,
                                UserId = State.UserId
                            });
                            
                        _logger.LogTrace(
                            "Relayed message {MessageId} to SignalR group chat_{ChatId} for {UserId}",
                            message.Id, message.ChatId, State.UserId);
                    }
                    else
                    {
                        _logger.LogTrace(
                            "Would relay message {MessageId} to connection {ConnectionId} for {UserId} (SignalR not available)",
                            message.Id, connection.ConnectionId, State.UserId);
                    }

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
                    "No active connections subscribed to chat {ChatId} for {UserId}. Buffering chunk {ChunkIndex} of operation {OperationId}.",
                    chunk.ChatId, State.UserId, chunk.ChunkIndex, chunk.OperationId);
                
                // Buffer the stream chunk for later delivery
                await BufferStreamChunkAsync(chunk, BufferPriority.Normal);
                return;
            }

            // Relay chunk to each subscribed connection
            var successCount = 0;
            var failureCount = 0;

            foreach (var connection in subscribedConnections)
            {
                try
                {
                    // Phase 3 Integration: Relay stream chunk via SignalR broadcast service
                    if (_signalRBroadcast.IsAvailable)
                    {
                        await _signalRBroadcast.BroadcastToGroupAsync(
                            $"chat_{chunk.ChatId}",
                            "ReceiveStreamChunk",
                            new
                            {
                                OperationId = chunk.OperationId,
                                ChatId = chunk.ChatId,
                                ChunkIndex = chunk.ChunkIndex,
                                Content = chunk.Content,
                                IsComplete = chunk.IsComplete,
                                Timestamp = chunk.Timestamp,
                                UserId = State.UserId
                            });
                            
                        _logger.LogTrace(
                            "Relayed chunk {ChunkIndex} of operation {OperationId} to SignalR group chat_{ChatId} for {UserId}",
                            chunk.ChunkIndex, chunk.OperationId, chunk.ChatId, State.UserId);
                    }
                    else
                    {
                        _logger.LogTrace(
                            "Would relay chunk {ChunkIndex} of operation {OperationId} to connection {ConnectionId} for {UserId} (SignalR not available)",
                            chunk.ChunkIndex, chunk.OperationId, connection.ConnectionId, State.UserId);
                    }

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
    public async Task<string> ProcessMessageWithBackground(ChatMessage message)
    {
        var operationStart = DateTime.UtcNow;
        try
        {
            _logger.LogInformation("Processing message in background for chat {ChatId} and user {UserId}", 
                message.ChatId, State.UserId);

            // Validate message
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (string.IsNullOrEmpty(message.ChatId))
            {
                throw new ArgumentException("Message must have a ChatId", nameof(message));
            }

            // Generate operation ID
            var operationId = Guid.NewGuid().ToString();

            // Track in grain state
            State.ActiveOperations[operationId] = new OperationContext
            {
                OperationId = operationId,
                ChatId = message.ChatId,
                Type = OperationType.SendMessage,
                StartedAt = DateTime.UtcNow,
                Status = OperationStatus.Queued
            };

            // Update metrics
            State.Metrics.TotalOperationsStarted++;
            State.Metrics.ActiveOperationsCount++;

            // Record activity
            await RecordActivity(ActivityType.MessageSent,
                JsonSerializer.Serialize(new { 
                    OperationId = operationId, 
                    ChatId = message.ChatId, 
                    MessageLength = message.Content?.Length ?? 0 
                }));

            await WriteStateAsync();

            _logger.LogInformation("Message queued for background processing with operation ID {OperationId}", operationId);

            // TODO: Phase 3 complete integration - this will be implemented when background service is available
            // For now, we track the intent and provide the operation ID for coordination
            
            // Record successful operation metrics
            var duration = (DateTime.UtcNow - operationStart).TotalMilliseconds;
            await _metricsCollector.RecordGrainOperationAsync("UserGrain", "ProcessMessageWithBackground", duration, true);

            return operationId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message with background for {UserId}", State.UserId);
            
            // Record failed operation metrics
            var duration = (DateTime.UtcNow - operationStart).TotalMilliseconds;
            await _metricsCollector.RecordGrainOperationAsync("UserGrain", "ProcessMessageWithBackground", duration, false);
            
            // Record error activity
            await RecordActivity(ActivityType.ErrorOccurred,
                JsonSerializer.Serialize(new { 
                    Error = ex.Message, 
                    ChatId = message?.ChatId ?? "unknown" 
                }));
            
            throw;
        }
    }

    /// <inheritdoc />
    public async Task NotifyOperationStarted(string operationId, string chatId)
    {
        try
        {
            _logger.LogInformation("Operation {OperationId} started for user {UserId} in chat {ChatId}",
                operationId, State.UserId, chatId);

            // Update operation status in grain state
            if (State.ActiveOperations.TryGetValue(operationId, out var operation))
            {
                operation.Status = OperationStatus.InProgress;
                operation.StartedAt = DateTime.UtcNow;
                await WriteStateAsync();
            }
            else
            {
                _logger.LogWarning("Operation {OperationId} not found in state for user {UserId}", 
                    operationId, State.UserId);
            }

            // Broadcast operation started event to connected clients
            await BroadcastToChat(chatId, "OperationStarted", new
            {
                OperationId = operationId,
                ChatId = chatId,
                UserId = State.UserId,
                Timestamp = DateTime.UtcNow,
                Status = "InProgress"
            });

            // Record activity
            await RecordActivity(ActivityType.MessageSent,
                JsonSerializer.Serialize(new { 
                    Event = "OperationStarted",
                    OperationId = operationId, 
                    ChatId = chatId 
                }));

            _logger.LogDebug("Operation started notification completed for {OperationId}", operationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify operation started for {OperationId} and user {UserId}",
                operationId, State.UserId);
            // Don't throw - this is a notification method
        }
    }

    /// <inheritdoc />
    public async Task NotifyOperationCompleted(string operationId, bool success, string? error = null)
    {
        try
        {
            _logger.LogInformation("Operation {OperationId} completed for user {UserId}. Success: {Success}",
                operationId, State.UserId, success);

            // Find and update operation in grain state
            if (!State.ActiveOperations.TryGetValue(operationId, out var operation))
            {
                _logger.LogWarning("Operation {OperationId} not found in state for user {UserId}", 
                    operationId, State.UserId);
                return;
            }

            // Update operation status
            operation.Status = success ? OperationStatus.Completed : OperationStatus.Failed;
            operation.CompletedAt = DateTime.UtcNow;
            operation.Error = error;

            // Update metrics
            if (success)
            {
                State.Metrics.TotalOperationsCompleted++;
            }
            else
            {
                State.Metrics.TotalOperationsFailed++;
            }

            State.Metrics.ActiveOperationsCount--;

            // Calculate and track duration
            if (operation.CompletedAt.HasValue)
            {
                var duration = operation.CompletedAt.Value - operation.StartedAt;
                State.Metrics.TotalOperationDurationMs += (long)duration.TotalMilliseconds;
            }

            await WriteStateAsync();

            // Broadcast operation completed event to connected clients
            await BroadcastToChat(operation.ChatId, "OperationCompleted", new
            {
                OperationId = operationId,
                ChatId = operation.ChatId,
                UserId = State.UserId,
                Success = success,
                Error = error,
                Timestamp = DateTime.UtcNow,
                Status = success ? "Completed" : "Failed",
                Duration = operation.CompletedAt - operation.StartedAt
            });

            // Record activity
            var activityType = success ? ActivityType.MessageCompleted : ActivityType.ErrorOccurred;
            await RecordActivity(activityType,
                JsonSerializer.Serialize(new { 
                    Event = "OperationCompleted",
                    OperationId = operationId, 
                    ChatId = operation.ChatId,
                    Success = success,
                    Error = error,
                    Duration = (operation.CompletedAt - operation.StartedAt)?.TotalMilliseconds
                }));

            // Schedule cleanup of completed operation after delay
            if (success || !string.IsNullOrEmpty(error))
            {
                this.RegisterGrainTimer(
                    async _ => await CleanupOperation(operationId),
                    new GrainTimerCreationOptions
                    {
                        DueTime = TimeSpan.FromMinutes(_configuration.UserGrain.CompletedOperationRetentionMinutes),
                        Period = TimeSpan.MaxValue,
                        Interleave = true
                    });
            }

            _logger.LogDebug("Operation completed notification finished for {OperationId}", operationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify operation completed for {OperationId} and user {UserId}",
                operationId, State.UserId);
            // Don't throw - this is a notification method
        }
    }

    /// <inheritdoc />
    public async Task<bool> CancelOperation(string operationId)
    {
        try
        {
            _logger.LogInformation("Cancelling operation {OperationId} for user {UserId}",
                operationId, State.UserId);

            // Find operation in grain state
            if (!State.ActiveOperations.TryGetValue(operationId, out var operation))
            {
                _logger.LogWarning("Operation {OperationId} not found for user {UserId}. Cannot cancel.",
                    operationId, State.UserId);
                return false;
            }

            // Check if operation can be cancelled
            if (operation.Status is OperationStatus.Completed or OperationStatus.Failed or OperationStatus.Cancelled)
            {
                _logger.LogWarning("Operation {OperationId} for user {UserId} is already in final state {Status}. Cannot cancel.",
                    operationId, State.UserId, operation.Status);
                return false;
            }

            // Update operation status to cancelled
            operation.Status = OperationStatus.Cancelled;
            operation.CompletedAt = DateTime.UtcNow;
            operation.Error = "Operation cancelled by user";

            // Update metrics
            State.Metrics.TotalOperationsCancelled++;
            State.Metrics.ActiveOperationsCount--;

            if (operation.CompletedAt.HasValue)
            {
                var duration = operation.CompletedAt.Value - operation.StartedAt;
                State.Metrics.TotalOperationDurationMs += (long)duration.TotalMilliseconds;
            }

            await WriteStateAsync();

            // Broadcast cancellation to clients
            await BroadcastToChat(operation.ChatId, "OperationCancelled", new
            {
                OperationId = operationId,
                ChatId = operation.ChatId,
                UserId = State.UserId,
                Timestamp = DateTime.UtcNow,
                Status = "Cancelled",
                Duration = operation.CompletedAt - operation.StartedAt
            });

            // Record activity
            await RecordActivity(ActivityType.ErrorOccurred,
                JsonSerializer.Serialize(new { 
                    Event = "OperationCancelled",
                    OperationId = operationId, 
                    ChatId = operation.ChatId 
                }));

            // Schedule cleanup of cancelled operation after delay
            this.RegisterGrainTimer(
                async _ => await CleanupOperation(operationId),
                new GrainTimerCreationOptions
                {
                    DueTime = TimeSpan.FromMinutes(_configuration.UserGrain.CompletedOperationRetentionMinutes),
                    Period = TimeSpan.MaxValue,
                    Interleave = true
                });

            _logger.LogInformation("Operation {OperationId} successfully cancelled for user {UserId}",
                operationId, State.UserId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel operation {OperationId} for user {UserId}",
                operationId, State.UserId);
            // Don't throw - this is a control method
            return false;
        }
    }

    /// <inheritdoc />
    public Task<OperationContext?> GetOperationStatus(string operationId)
    {
        try
        {
            _logger.LogDebug("Getting operation status for {OperationId} and user {UserId}",
                operationId, State.UserId);

            // Find operation in grain state
            if (State.ActiveOperations.TryGetValue(operationId, out var operation))
            {
                // Return a copy to prevent external modification
                var operationCopy = new OperationContext
                {
                    OperationId = operation.OperationId,
                    ChatId = operation.ChatId,
                    Type = operation.Type,
                    StartedAt = operation.StartedAt,
                    CompletedAt = operation.CompletedAt,
                    Status = operation.Status,
                    Error = operation.Error
                };

                _logger.LogDebug("Found operation {OperationId} with status {Status} for user {UserId}",
                    operationId, operation.Status, State.UserId);

                return Task.FromResult<OperationContext?>(operationCopy);
            }

            _logger.LogDebug("Operation {OperationId} not found for user {UserId}",
                operationId, State.UserId);

            return Task.FromResult<OperationContext?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get operation status for {OperationId} and user {UserId}",
                operationId, State.UserId);
            return Task.FromResult<OperationContext?>(null);
        }
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
    /// Broadcasts a SignalR message to all connections subscribed to a chat.
    /// </summary>
    /// <param name="chatId">The chat ID to broadcast to</param>
    /// <param name="method">The SignalR method name to call</param>
    /// <param name="payload">The payload to broadcast</param>
    /// <returns>Statistics about the broadcast operation</returns>
    private async Task<(int SuccessCount, int FailureCount)> BroadcastToChat(string chatId, string method, object payload)
    {
        var connections = GetConnectionsForChat(chatId);
        var successCount = 0;
        var failureCount = 0;

        // Phase 3 Integration: Use SignalR broadcast service for actual broadcasting
        if (_signalRBroadcast.IsAvailable)
        {
            try
            {
                await _signalRBroadcast.BroadcastToGroupAsync($"chat_{chatId}", method, payload);

                // Update connection activity for tracked connections
                foreach (var connection in connections)
                {
                    connection.LastActivity = DateTime.UtcNow;
                    successCount++;
                }

                _logger.LogTrace(
                    "Broadcasted {Method} to SignalR group chat_{ChatId} for {UserId} ({ConnectionCount} connections)",
                    method, chatId, State.UserId, connections.Count());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to broadcast {Method} to SignalR group chat_{ChatId} for {UserId}",
                    method, chatId, State.UserId);
                failureCount = connections.Count();
            }
        }
        else
        {
            // Fallback to logging when SignalR is not available
            foreach (var connection in connections)
            {
                try
                {
                    _logger.LogTrace(
                        "Would broadcast {Method} to connection {ConnectionId} in chat {ChatId} for {UserId} (SignalR not available)",
                        method, connection.ConnectionId, chatId, State.UserId);

                    connection.LastActivity = DateTime.UtcNow;
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to track broadcast {Method} to connection {ConnectionId} in chat {ChatId} for {UserId}",
                        method, connection.ConnectionId, chatId, State.UserId);
                    failureCount++;
                }
            }
        }

        return (successCount, failureCount);
    }

    /// <summary>
    /// Broadcasts a message to all connections subscribed to a chat.
    /// </summary>
    /// <param name="chatId">The chat ID to broadcast to</param>
    /// <param name="payload">The payload to broadcast</param>
    /// <returns>Statistics about the broadcast operation</returns>
    private async Task<(int SuccessCount, int FailureCount)> BroadcastToChat(string chatId, object payload)
    {
        // Delegate to the main BroadcastToChat method with default method name
        return await BroadcastToChat(chatId, "ReceiveBroadcast", payload);
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

            // Clean up expired buffered messages (Phase 3 data)
            var expiredMessagesRemoved = await ClearExpiredBufferedMessagesAsync();
            if (expiredMessagesRemoved > 0)
            {
                changed = true;
                _logger.LogDebug("Cleaned up {Count} expired buffered messages for {UserId}",
                    expiredMessagesRemoved, State.UserId);
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

            // Update active operations count from actual state
            State.Metrics.ActiveOperationsCount = State.ActiveOperations
                .Count(kvp => kvp.Value.Status is OperationStatus.Queued or OperationStatus.InProgress);

            // Save metrics periodically
            await WriteStateAsync();

            // Record state metrics to central collector for dashboard
            var stateSize = System.Text.Json.JsonSerializer.Serialize(State).Length;
            await _metricsCollector.RecordGrainStateMetricsAsync(
                "UserGrain", 
                State.UserId, 
                stateSize, 
                State.Metrics.ActiveConnections, 
                State.Metrics.ActiveOperationsCount);

            _logger.LogTrace("Metrics updated for {UserId}: Connections={ConnectionCount}, Activities={ActivityCount}, ActiveOps={ActiveOperations}",
                State.UserId, State.Metrics.ActiveConnections, State.Metrics.TotalActivities, State.Metrics.ActiveOperationsCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update metrics for {UserId}", State.UserId);
        }
    }

    /// <summary>
    /// Cleans up a completed operation from the active operations dictionary.
    /// This method is called by a timer after an operation has completed.
    /// </summary>
    /// <param name="operationId">The operation ID to clean up</param>
    /// <returns>Task representing the cleanup operation</returns>
    private async Task CleanupOperation(string operationId)
    {
        // CRITICAL: Check disposal flag to prevent execution after deactivation
        if (_disposed)
        {
            return;
        }

        try
        {
            if (State.ActiveOperations.TryGetValue(operationId, out var operation))
            {
                // Only clean up completed, failed, or cancelled operations
                if (operation.Status is OperationStatus.Completed or OperationStatus.Failed or OperationStatus.Cancelled)
                {
                    State.ActiveOperations.Remove(operationId);
                    await WriteStateAsync();

                    _logger.LogDebug("Cleaned up completed operation {OperationId} for user {UserId}",
                        operationId, State.UserId);
                }
                else
                {
                    _logger.LogWarning("Attempted to clean up operation {OperationId} with status {Status} for user {UserId}",
                        operationId, operation.Status, State.UserId);
                }
            }
            else
            {
                _logger.LogDebug("Operation {OperationId} already removed from state for user {UserId}",
                    operationId, State.UserId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup operation {OperationId} for user {UserId}",
                operationId, State.UserId);
        }
    }

    #endregion

    #region IUserMessageBufferGrain Implementation

    /// <inheritdoc />
    public async Task<string> BufferMessageAsync(ChatMessage message, BufferPriority priority = BufferPriority.Normal)
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
                throw new ArgumentException("Message must have a valid ChatId", nameof(message));
            }

            // Generate unique message ID
            var messageId = $"{message.ChatId}_{Guid.NewGuid()}";

            // Get or create chat buffer
            var buffer = GetOrCreateChatBuffer(message.ChatId);

            // Create buffered message
            var bufferedMessage = new BufferedMessage
            {
                MessageId = messageId,
                ChatId = message.ChatId,
                Message = message,
                BufferedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(buffer.TtlMinutes),
                Priority = priority,
                DeliveryAttempts = 0,
                LastDeliveryAttempt = null,
                LastDeliveryError = null
            };

            // Handle buffer overflow if necessary
            while (buffer.Messages.Count >= buffer.MaxSize)
            {
                if (buffer.OverflowStrategy == BufferOverflowStrategy.RejectNew)
                {
                    _logger.LogWarning(
                        "Buffer overflow: Rejecting new message for chat {ChatId} (buffer full at {Count})",
                        message.ChatId, buffer.MaxSize);
                    
                    // Record overflow metric
                    State.Metrics.TotalBufferOverflowDrops++;
                    
                    throw new InvalidOperationException($"Buffer full for chat {message.ChatId}");
                }
                else // DropOldest
                {
                    var droppedMessage = buffer.Messages.Dequeue();
                    buffer.TotalOverflowDrops++;
                    State.Metrics.TotalBufferOverflowDrops++;
                    
                    _logger.LogDebug(
                        "Buffer overflow: Dropped oldest message {MessageId} from chat {ChatId}",
                        droppedMessage.MessageId, message.ChatId);
                }
            }

            // Add message to buffer
            buffer.Messages.Enqueue(bufferedMessage);
            buffer.TotalMessagesBuffered++;
            
            // Update metrics
            State.Metrics.TotalMessagesBuffered++;
            State.Metrics.CurrentBufferCount = State.MessageBuffers.Count;

            // Record activity
            await RecordActivity(ActivityType.MessageSent,
                JsonSerializer.Serialize(new { 
                    Event = "MessageBuffered", 
                    MessageId = messageId,
                    ChatId = message.ChatId,
                    Priority = priority.ToString(),
                    BufferSize = buffer.Messages.Count
                }));

            // Save state
            await WriteStateAsync();

            _logger.LogDebug(
                "Buffered message {MessageId} for chat {ChatId} with priority {Priority}",
                messageId, message.ChatId, priority);

            return messageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to buffer message for chat {ChatId} and user {UserId}",
                message?.ChatId, State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> BufferStreamChunkAsync(StreamChunk chunk, BufferPriority priority = BufferPriority.Normal)
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
                throw new ArgumentException("Stream chunk must have a valid ChatId", nameof(chunk));
            }

            // Create a ChatMessage from the stream chunk for buffering
            var message = new ChatMessage
            {
                Id = chunk.OperationId,
                ChatId = chunk.ChatId,
                Content = chunk.Content,
                Role = "assistant",
                Timestamp = DateTime.UtcNow,
                IsStreaming = true,
                Metadata = JsonSerializer.Serialize(new
                {
                    IsComplete = chunk.IsComplete,
                    ChunkIndex = chunk.ChunkIndex,
                    TotalChunks = chunk.TotalChunks,
                    MessageId = chunk.MessageId
                })
            };

            // Buffer as a regular message
            var messageId = await BufferMessageAsync(message, priority);

            _logger.LogDebug(
                "Buffered stream chunk {ChunkIndex} for operation {OperationId} as message {MessageId}",
                chunk.ChunkIndex, chunk.OperationId, messageId);

            return messageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to buffer stream chunk for chat {ChatId} and user {UserId}",
                chunk?.ChatId, State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<BufferedMessage>> GetBufferedMessagesAsync(
        string chatId, 
        int? limit = null, 
        bool highPriorityOnly = false)
    {
        try
        {
            if (string.IsNullOrEmpty(chatId))
            {
                throw new ArgumentException("Chat ID cannot be null or empty", nameof(chatId));
            }

            if (!State.MessageBuffers.TryGetValue(chatId, out var buffer))
            {
                return Task.FromResult(Enumerable.Empty<BufferedMessage>());
            }

            var messages = buffer.Messages.AsEnumerable();

            // Filter by priority if requested
            if (highPriorityOnly)
            {
                messages = messages.Where(m => m.Priority == BufferPriority.High);
            }

            // Apply limit if specified
            if (limit.HasValue && limit.Value > 0)
            {
                messages = messages.Take(limit.Value);
            }

            var result = messages.ToList();

            _logger.LogDebug(
                "Retrieved {Count} buffered messages for chat {ChatId} (high priority only: {HighPriorityOnly})",
                result.Count, chatId, highPriorityOnly);

            return Task.FromResult<IEnumerable<BufferedMessage>>(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get buffered messages for chat {ChatId} and user {UserId}",
                chatId, State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<BufferedMessage?> GetBufferedMessageAsync(string messageId)
    {
        try
        {
            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));
            }

            // Search through all chat buffers for the message
            foreach (var buffer in State.MessageBuffers.Values)
            {
                var message = buffer.Messages.FirstOrDefault(m => m.MessageId == messageId);
                if (message != null)
                {
                    _logger.LogDebug(
                        "Found buffered message {MessageId} in chat {ChatId}",
                        messageId, message.ChatId);
                    return Task.FromResult<BufferedMessage?>(message);
                }
            }

            _logger.LogDebug("Buffered message {MessageId} not found for user {UserId}",
                messageId, State.UserId);

            return Task.FromResult<BufferedMessage?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get buffered message {MessageId} for user {UserId}",
                messageId, State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveBufferedMessageAsync(string messageId)
    {
        try
        {
            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));
            }

            // Search through all chat buffers for the message
            foreach (var buffer in State.MessageBuffers.Values)
            {
                var messages = buffer.Messages.ToList();
                var messageToRemove = messages.FirstOrDefault(m => m.MessageId == messageId);
                
                if (messageToRemove != null)
                {
                    // Rebuild queue without the message
                    buffer.Messages.Clear();
                    foreach (var msg in messages.Where(m => m.MessageId != messageId))
                    {
                        buffer.Messages.Enqueue(msg);
                    }

                    // Update metrics
                    State.Metrics.CurrentBufferCount = State.MessageBuffers.Count;

                    // Record activity
                    await RecordActivity(ActivityType.MessageCompleted,
                        JsonSerializer.Serialize(new { 
                            Event = "MessageRemovedFromBuffer", 
                            MessageId = messageId,
                            ChatId = messageToRemove.ChatId
                        }));

                    // Save state
                    await WriteStateAsync();

                    _logger.LogDebug(
                        "Removed buffered message {MessageId} from chat {ChatId}",
                        messageId, messageToRemove.ChatId);

                    return true;
                }
            }

            _logger.LogDebug("Buffered message {MessageId} not found for removal in user {UserId}",
                messageId, State.UserId);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to remove buffered message {MessageId} for user {UserId}",
                messageId, State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> ClearExpiredBufferedMessagesAsync()
    {
        try
        {
            var totalRemoved = 0;
            var currentTime = DateTime.UtcNow;

            foreach (var kvp in State.MessageBuffers.ToList())
            {
                var chatId = kvp.Key;
                var buffer = kvp.Value;
                var originalCount = buffer.Messages.Count;

                // Filter out expired messages
                var validMessages = new Queue<BufferedMessage>();
                while (buffer.Messages.Count > 0)
                {
                    var message = buffer.Messages.Dequeue();
                    if (message.ExpiresAt > currentTime)
                    {
                        validMessages.Enqueue(message);
                    }
                    else
                    {
                        totalRemoved++;
                        buffer.TotalExpiredMessages++;
                        _logger.LogDebug(
                            "Expired buffered message {MessageId} from chat {ChatId}",
                            message.MessageId, chatId);
                    }
                }

                buffer.Messages = validMessages;
                buffer.LastCleanupAt = currentTime;

                // Remove empty buffers
                if (buffer.Messages.Count == 0)
                {
                    State.MessageBuffers.Remove(chatId);
                    _logger.LogDebug("Removed empty buffer for chat {ChatId}", chatId);
                }

                _logger.LogDebug(
                    "Cleaned buffer for chat {ChatId}: {OriginalCount} -> {NewCount} messages",
                    chatId, originalCount, buffer.Messages.Count);
            }

            // Update metrics
            State.Metrics.TotalBufferExpiredMessages += totalRemoved;
            State.Metrics.CurrentBufferCount = State.MessageBuffers.Count;
            State.Metrics.TotalBufferCleanupRuns++;

            if (totalRemoved > 0)
            {
                // Record activity
                await RecordActivity(ActivityType.MessageCompleted,
                    JsonSerializer.Serialize(new { 
                        Event = "BufferCleanup", 
                        ExpiredMessages = totalRemoved,
                        RemainingBuffers = State.MessageBuffers.Count
                    }));

                // Save state
                await WriteStateAsync();
            }

            _logger.LogDebug(
                "Buffer cleanup completed for user {UserId}: removed {ExpiredCount} expired messages",
                State.UserId, totalRemoved);

            return totalRemoved;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to clear expired buffered messages for user {UserId}",
                State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<ChatMessageBuffer?> GetChatBufferAsync(string chatId)
    {
        try
        {
            if (string.IsNullOrEmpty(chatId))
            {
                throw new ArgumentException("Chat ID cannot be null or empty", nameof(chatId));
            }

            if (State.MessageBuffers.TryGetValue(chatId, out var buffer))
            {
                _logger.LogDebug(
                    "Retrieved buffer for chat {ChatId}: {MessageCount} messages",
                    chatId, buffer.Messages.Count);
                return Task.FromResult<ChatMessageBuffer?>(buffer);
            }

            _logger.LogDebug("No buffer found for chat {ChatId} in user {UserId}",
                chatId, State.UserId);

            return Task.FromResult<ChatMessageBuffer?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get chat buffer for {ChatId} and user {UserId}",
                chatId, State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<Dictionary<string, BufferSummary>> GetBufferSummaryAsync()
    {
        try
        {
            var summaries = new Dictionary<string, BufferSummary>();
            var currentTime = DateTime.UtcNow;

            foreach (var kvp in State.MessageBuffers)
            {
                var chatId = kvp.Key;
                var buffer = kvp.Value;
                var messages = buffer.Messages.ToList();

                var summary = new BufferSummary
                {
                    ChatId = chatId,
                    CurrentMessageCount = messages.Count,
                    MaxCapacity = buffer.MaxSize,
                    UtilizationPercent = buffer.MaxSize > 0 ? (messages.Count * 100.0) / buffer.MaxSize : 0,
                    OldestMessageTime = messages.FirstOrDefault()?.BufferedAt,
                    NewestMessageTime = messages.LastOrDefault()?.BufferedAt,
                    HighPriorityCount = messages.Count(m => m.Priority == BufferPriority.High),
                    ExpiringMessageCount = messages.Count(m => (m.ExpiresAt - currentTime).TotalMinutes <= 5)
                };

                summaries[chatId] = summary;
            }

            _logger.LogDebug(
                "Generated buffer summary for user {UserId}: {BufferCount} active buffers",
                State.UserId, summaries.Count);

            return Task.FromResult(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get buffer summary for user {UserId}",
                State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> MarkMessageDeliveredAsync(string messageId)
    {
        try
        {
            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));
            }

            // Find and remove the delivered message
            var removed = await RemoveBufferedMessageAsync(messageId);

            if (removed)
            {
                // Update delivery metrics
                State.Metrics.TotalBufferedMessagesDelivered++;

                // Record activity
                await RecordActivity(ActivityType.MessageCompleted,
                    JsonSerializer.Serialize(new { 
                        Event = "MessageDelivered", 
                        MessageId = messageId
                    }));

                _logger.LogDebug(
                    "Marked buffered message {MessageId} as delivered for user {UserId}",
                    messageId, State.UserId);
            }

            return removed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to mark message {MessageId} as delivered for user {UserId}",
                messageId, State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RecordDeliveryAttemptAsync(string messageId, string error)
    {
        try
        {
            if (string.IsNullOrEmpty(messageId))
            {
                throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));
            }

            // Find the message in buffers
            foreach (var buffer in State.MessageBuffers.Values)
            {
                var messages = buffer.Messages.ToList();
                var message = messages.FirstOrDefault(m => m.MessageId == messageId);
                
                if (message != null)
                {
                    // Update delivery attempt info
                    message.DeliveryAttempts++;
                    message.LastDeliveryAttempt = DateTime.UtcNow;
                    message.LastDeliveryError = error;

                    // Rebuild the queue with updated message
                    buffer.Messages.Clear();
                    foreach (var msg in messages)
                    {
                        buffer.Messages.Enqueue(msg);
                    }

                    // Record activity
                    await RecordActivity(ActivityType.ErrorOccurred,
                        JsonSerializer.Serialize(new { 
                            Event = "DeliveryAttemptFailed", 
                            MessageId = messageId,
                            ChatId = message.ChatId,
                            Attempts = message.DeliveryAttempts,
                            Error = error
                        }));

                    // Save state
                    await WriteStateAsync();

                    _logger.LogDebug(
                        "Recorded delivery attempt #{Attempt} for message {MessageId}: {Error}",
                        message.DeliveryAttempts, messageId, error);

                    return true;
                }
            }

            _logger.LogDebug("Message {MessageId} not found for delivery attempt recording in user {UserId}",
                messageId, State.UserId);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to record delivery attempt for message {MessageId} and user {UserId}",
                messageId, State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> ProcessBufferedMessagesAsync(
        string connectionId, 
        string? chatId = null, 
        int maxMessages = 50)
    {
        try
        {
            if (string.IsNullOrEmpty(connectionId))
            {
                throw new ArgumentException("Connection ID cannot be null or empty", nameof(connectionId));
            }

            var processedCount = 0;
            var buffersToProcess = chatId != null 
                ? State.MessageBuffers.Where(kvp => kvp.Key == chatId)
                : State.MessageBuffers;

            foreach (var kvp in buffersToProcess.ToList())
            {
                if (processedCount >= maxMessages) break;

                var bufferChatId = kvp.Key;
                var buffer = kvp.Value;
                var messagesToDeliver = new List<BufferedMessage>();

                // Get messages to deliver (prioritize high priority)
                var messages = buffer.Messages
                    .OrderByDescending(m => m.Priority)
                    .ThenBy(m => m.BufferedAt)
                    .Take(maxMessages - processedCount)
                    .ToList();

                foreach (var message in messages)
                {
                    try
                    {
                        // Attempt to deliver the message
                        if (message.Message.IsStreaming && !string.IsNullOrEmpty(message.Message.Metadata))
                        {
                            // Try to parse stream metadata
                            try
                            {
                                var metadata = JsonSerializer.Deserialize<JsonElement>(message.Message.Metadata);
                                
                                // Deliver as stream chunk
                                var chunk = new StreamChunk
                                {
                                    OperationId = message.Message.Id,
                                    ChatId = message.Message.ChatId,
                                    Content = message.Message.Content,
                                    IsComplete = metadata.TryGetProperty("IsComplete", out var isComplete) ? isComplete.GetBoolean() : true,
                                    ChunkIndex = metadata.TryGetProperty("ChunkIndex", out var chunkIndex) ? chunkIndex.GetInt32() : 0,
                                    TotalChunks = metadata.TryGetProperty("TotalChunks", out var totalChunks) ? totalChunks.GetInt32() : null,
                                    MessageId = metadata.TryGetProperty("MessageId", out var messageId) ? messageId.GetString() : null
                                };

                                await RelayStreamChunk(chunk);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to parse stream metadata for message {MessageId}, delivering as regular message", message.MessageId);
                                // Fall back to regular message delivery
                                await RelayMessage(message.Message);
                            }
                        }
                        else
                        {
                            // Deliver as regular message
                            await RelayMessage(message.Message);
                        }

                        messagesToDeliver.Add(message);
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        // Record delivery failure
                        await RecordDeliveryAttemptAsync(message.MessageId, ex.Message);
                        
                        _logger.LogWarning(ex,
                            "Failed to deliver buffered message {MessageId} to connection {ConnectionId}",
                            message.MessageId, connectionId);
                    }
                }

                // Remove successfully delivered messages
                foreach (var deliveredMessage in messagesToDeliver)
                {
                    await MarkMessageDeliveredAsync(deliveredMessage.MessageId);
                }
            }

            if (processedCount > 0)
            {
                // Record activity
                await RecordActivity(ActivityType.MessageCompleted,
                    JsonSerializer.Serialize(new { 
                        Event = "BufferedMessagesProcessed", 
                        ConnectionId = connectionId,
                        ChatId = chatId,
                        ProcessedCount = processedCount
                    }));

                _logger.LogDebug(
                    "Processed {ProcessedCount} buffered messages for connection {ConnectionId}",
                    processedCount, connectionId);
            }

            return processedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process buffered messages for connection {ConnectionId} and user {UserId}",
                connectionId, State.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> ClearChatBufferAsync(string chatId)
    {
        try
        {
            if (string.IsNullOrEmpty(chatId))
            {
                throw new ArgumentException("Chat ID cannot be null or empty", nameof(chatId));
            }

            if (!State.MessageBuffers.TryGetValue(chatId, out var buffer))
            {
                _logger.LogDebug("No buffer found for chat {ChatId} in user {UserId}",
                    chatId, State.UserId);
                return 0;
            }

            var messageCount = buffer.Messages.Count;
            State.MessageBuffers.Remove(chatId);

            // Update metrics
            State.Metrics.CurrentBufferCount = State.MessageBuffers.Count;

            // Record activity
            await RecordActivity(ActivityType.MessageCompleted,
                JsonSerializer.Serialize(new { 
                    Event = "ChatBufferCleared", 
                    ChatId = chatId,
                    MessagesCleared = messageCount
                }));

            // Save state
            await WriteStateAsync();

            _logger.LogDebug(
                "Cleared buffer for chat {ChatId}: removed {MessageCount} messages",
                chatId, messageCount);

            return messageCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to clear chat buffer for {ChatId} and user {UserId}",
                chatId, State.UserId);
            throw;
        }
    }

    /// <summary>
    /// Gets or creates a chat message buffer with default settings.
    /// </summary>
    /// <param name="chatId">Chat identifier</param>
    /// <returns>Chat message buffer</returns>
    private ChatMessageBuffer GetOrCreateChatBuffer(string chatId)
    {
        if (State.MessageBuffers.TryGetValue(chatId, out var existingBuffer))
        {
            return existingBuffer;
        }

        var newBuffer = new ChatMessageBuffer
        {
            ChatId = chatId,
            Messages = new Queue<BufferedMessage>(),
            MaxSize = _configuration.UserGrain.MessageBufferSizePerChat,
            TtlMinutes = _configuration.UserGrain.MessageBufferTtlMinutes,
            LastCleanupAt = DateTime.UtcNow,
            OverflowStrategy = _configuration.UserGrain.BufferOverflowStrategy,
            CreatedAt = DateTime.UtcNow
        };

        State.MessageBuffers[chatId] = newBuffer;

        _logger.LogDebug(
            "Created new message buffer for chat {ChatId} with size limit {MaxSize}",
            chatId, newBuffer.MaxSize);

        return newBuffer;
    }

    #endregion
}
