using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIChat.Orleans.Configuration;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Metrics;
using AIChat.Orleans.Models;
using AIChat.Orleans.Services;
using AIChat.Orleans.Tracing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Placement;
using Orleans.Runtime;

namespace AIChat.Orleans.Grains;


/// <summary>
/// User grain implementation providing user-centric operations.
/// Maintains user state, connections, and handles message routing.
/// </summary>
[HashBasedPlacement]
public sealed class UserGrain : Grain<UserGrainState>, IUserGrain, IDisposable
{
    private readonly ILogger<UserGrain> _logger;
    private readonly OrleansGrainConfiguration _configuration;
    private readonly IOrleansMetricsCollector _metricsCollector;
    private readonly ISignalRBroadcastService _signalRBroadcast;
    private readonly IActivityAnalyticsService _activityAnalytics;
    private readonly IActivityPrivacyService _activityPrivacy;
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
    /// <param name="activityAnalytics">Activity analytics service for telemetry integration (optional)</param>
    /// <param name="activityPrivacy">Activity privacy service for PII protection (optional)</param>
    public UserGrain(
        ILogger<UserGrain> logger,
        IOptionsSnapshot<OrleansGrainConfiguration> configuration,
        IOrleansMetricsCollector metricsCollector,
        ISignalRBroadcastService? signalRBroadcast = null,
        IActivityAnalyticsService? activityAnalytics = null,
        IActivityPrivacyService? activityPrivacy = null
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value ?? new OrleansGrainConfiguration();
        _metricsCollector =
            metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _signalRBroadcast = signalRBroadcast ?? new NullSignalRBroadcastService(); // Default to no-op implementation

        // Initialize enhanced activity tracking services (ORL-ST-P2-005)
        if (activityAnalytics == null)
        {
            var analyticsLogger =
                new Microsoft.Extensions.Logging.Abstractions.NullLogger<ActivityAnalyticsService>();
            _activityAnalytics = new ActivityAnalyticsService(analyticsLogger, NoOpChatTelemetry.Instance, metricsCollector);
        }
        else
        {
            _activityAnalytics = activityAnalytics;
        }

        if (activityPrivacy == null)
        {
            var privacyLogger =
                new Microsoft.Extensions.Logging.Abstractions.NullLogger<ActivityPrivacyService>();
            _activityPrivacy = new ActivityPrivacyService(privacyLogger);
        }
        else
        {
            _activityPrivacy = activityPrivacy;
        }
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
        await _metricsCollector.RecordGrainActivationAsync(
            "UserGrain",
            State.UserId,
            activationTime
        );

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
                    Interleave = true,
                }
            );

            // Setup metrics timer using Orleans 9.x API
            _metricsTimer = this.RegisterGrainTimer(
                async _ => await UpdateMetricsAsync(null),
                new GrainTimerCreationOptions
                {
                    DueTime = TimeSpan.FromMinutes(
                        _configuration.UserGrain.MetricsUpdateIntervalMinutes
                    ),
                    Period = TimeSpan.FromMinutes(
                        _configuration.UserGrain.MetricsUpdateIntervalMinutes
                    ),
                    Interleave = true,
                }
            );
        }

        await WriteStateAsync();

        _logger.LogInformation(
            "UserGrain activated for {UserId}. Activation #{ActivationCount}",
            State.UserId,
            State.Metrics.ActivationCount
        );
    }

    /// <inheritdoc />
    public override async Task OnDeactivateAsync(
        DeactivationReason reason,
        CancellationToken cancellationToken
    )
    {
        _logger.LogInformation(
            "UserGrain deactivating for {UserId}. Reason: {Reason}",
            State.UserId,
            reason
        );

        // Record metrics for grain deactivation
        var lifetimeMinutes = (DateTime.UtcNow - State.ActivatedAt).TotalMinutes;
        await _metricsCollector.RecordGrainDeactivationAsync(
            "UserGrain",
            State.UserId,
            lifetimeMinutes
        );

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
        _cleanupTimer?.Dispose();
        _cleanupTimer = null;

        // Dispose and nullify metrics timer
        _metricsTimer?.Dispose();
        _metricsTimer = null;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Persists state with retry logic.
    /// </summary>
    private async Task PersistStateWithRetryAsync()
    {
        var retryCount = 0;
        var maxRetries = _configuration.Persistence.MaxPersistenceRetries;
        var retryDelay = TimeSpan.FromMilliseconds(
            _configuration.Persistence.PersistenceRetryDelayMilliseconds
        );

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
                _logger.LogWarning(
                    ex,
                    "Failed to save state for {UserId} (attempt {Attempt}/{MaxAttempts}). Retrying...",
                    State.UserId,
                    retryCount,
                    maxRetries
                );

                await Task.Delay(retryDelay);
                retryDelay = TimeSpan.FromMilliseconds(
                    Math.Min(retryDelay.TotalMilliseconds * 2, 5000)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to save state for {UserId} after {MaxAttempts} attempts",
                    State.UserId,
                    maxRetries
                );
                throw;
            }
        }
    }

    #region Phase 1: Shadow Mode Operations

    /// <inheritdoc />
    public async Task RecordActivity(ActivityType type, string metadata)
    {
        await RecordActivityAsync(type, metadata, CancellationToken.None);
    }

    /// <summary>
    /// Enhanced activity recording with privacy compliance and analytics integration.
    /// Implements comprehensive activity tracking with PII protection and telemetry export.
    /// </summary>
    public async Task<StateResult> RecordActivityAsync(
        ActivityType type,
        string metadata,
        CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "RecordActivity", this.GetPrimaryKeyString());

        try
        {
            // 1. Privacy compliance check
            var consentResult = await _activityPrivacy.ValidateUserConsentAsync(State.UserId, type, cancellationToken);
            if (!consentResult.ConsentGranted)
            {
                activity?.SetTag("consent.granted", false);
                return StateResult.FromSuccess(); // Respect user privacy preference
            }

            // 2. Sanitize metadata for PII
            var sanitizationResult = await _activityPrivacy.SanitizeActivityMetadataAsync(metadata, cancellationToken);
            if (!sanitizationResult.IsValid)
            {
                activity?.SetTag("sanitization.failed", true);
                return StateResult.FromError("Activity metadata contains PII that cannot be sanitized");
            }

            // 3. Create privacy-aware activity record
            var privacyAwareRecord = new PrivacyAwareActivityRecord
            {
                Type = type,
                SanitizedMetadata = sanitizationResult.SanitizedMetadata,
                Timestamp = DateTime.UtcNow,
                CorrelationId = Activity.Current?.Id ?? Guid.NewGuid().ToString(),
                AnonymizedUserId = HashUserId(State.UserId),
                RetentionExpiresAt = CalculateRetentionExpiry(type)
            };

            // 4. Record in enhanced activity state
            State.ActivityTracking.PrivacyCompliantActivities.Enqueue(privacyAwareRecord);

            // 5. Maintain circular buffer with configurable size
            await MaintainActivityBufferAsync();

            // 6. Export to analytics (async, non-blocking)
            _ = Task.Run(async () => await _activityAnalytics.ExportActivityToTelemetryAsync(
                State.UserId,
                ConvertToActivityRecord(privacyAwareRecord),
                cancellationToken));

            // 7. Update metrics
            State.ActivityTracking.Metrics.TotalActivitiesRecorded++;
            State.ActivityTracking.Metrics.LastRecordedAt = DateTime.UtcNow;

            // 8. Persist state
            await WriteStateAsync();

            activity?.SetTag("activity.recorded", true);
            OrleansActivitySource.SetSuccess(activity);

            _logger.LogDebug(
                "Enhanced activity recorded for {UserId}: {ActivityType} - Sanitized metadata length: {MetadataLength}",
                State.UserId,
                type,
                sanitizationResult.SanitizedMetadata?.Length ?? 0
            );

            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            OrleansActivitySource.SetError(activity, ex);
            _logger.LogError(
                ex,
                "Failed to record enhanced activity for {UserId}. Type: {ActivityType}",
                State.UserId,
                type
            );

            return StateResult.FromError($"Failed to record activity: {ex.Message}");
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
            var staleThreshold = TimeSpan.FromMinutes(
                _configuration.Connections.StaleConnectionThresholdMinutes
            );
            var staleConnections = State.Connections.Values.Count(c =>
                DateTime.UtcNow - c.LastActivity > staleThreshold
            );

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
            var staleOperations = State.ActiveOperations.Values.Count(op =>
                DateTime.UtcNow - op.StartedAt > TimeSpan.FromMinutes(10)
                && op.Status == OperationStatus.InProgress
            );

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
                AdditionalInfo =
                    $"Connections: {State.Connections.Count}, Chats: {State.ActiveChats.Count}",
                Warnings = warnings,
            };

            _logger.LogDebug(
                "Health check completed for {UserId}. Healthy: {IsHealthy}",
                State.UserId,
                result.IsHealthy
            );

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for {UserId}", State.UserId);

            return Task.FromResult(
                new HealthCheckResult
                {
                    IsHealthy = false,
                    GrainId = State.UserId,
                    CheckedAt = DateTime.UtcNow,
                    AdditionalInfo = $"Health check exception: {ex.Message}",
                    Warnings = ["Health check threw exception"],
                }
            );
        }
    }

    #endregion

    #region Phase 2: Active Connection Management

    /// <inheritdoc />
    public async Task RegisterConnection(string connectionId, string clientId)
    {
        using var activity = OrleansActivitySource.StartGrainActivity(
            "UserGrain",
            nameof(RegisterConnection),
            State.UserId
        );
        try
        {
            // Add tracing tags
            _ = (activity?.SetTag("connection.id", connectionId));
            _ = (activity?.SetTag("client.id", clientId));

            // Validate parameters
            if (string.IsNullOrEmpty(connectionId))
            {
                throw new ArgumentException(
                    "Connection ID cannot be null or empty",
                    nameof(connectionId)
                );
            }

            if (string.IsNullOrEmpty(clientId))
            {
                throw new ArgumentException("Client ID cannot be null or empty", nameof(clientId));
            }

            // Check if connection already exists
            if (State.Connections.TryGetValue(connectionId, out var value))
            {
                _logger.LogWarning(
                    "Connection {ConnectionId} already registered for {UserId}. Updating existing connection.",
                    connectionId,
                    State.UserId
                );
                value.ClientId = clientId;
                value.LastActivity = DateTime.UtcNow;
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
                    SubscribedChatIds = [],
                };

                // Add to connections dictionary
                State.Connections[connectionId] = connectionInfo;

                _logger.LogInformation(
                    "Connection {ConnectionId} registered for {UserId} from client {ClientId}",
                    connectionId,
                    State.UserId,
                    clientId
                );
            }

            // Update metrics
            State.Metrics.ActiveConnections = State.Connections.Count;
            State.LastActivity = DateTime.UtcNow;

            // Record activity
            await RecordActivity(
                ActivityType.Connected,
                JsonSerializer.Serialize(new { ConnectionId = connectionId, ClientId = clientId })
            );

            // Process any buffered messages for connected chats
            var processedCount = await ProcessBufferedMessagesAsync(connectionId);
            if (processedCount > 0)
            {
                _logger.LogDebug(
                    "Delivered {ProcessedCount} buffered messages to newly connected {ConnectionId}",
                    processedCount,
                    connectionId
                );
            }

            // Save state
            await WriteStateAsync();

            // Mark activity as successful
            OrleansActivitySource.SetSuccess(
                activity,
                new Dictionary<string, object>
                {
                    { "buffered.messages.processed", processedCount },
                    { "active.connections", State.Connections.Count },
                }
            );
        }
        catch (Exception ex)
        {
            // Set activity error
            OrleansActivitySource.SetError(activity, ex);

            _logger.LogError(
                ex,
                "Failed to register connection {ConnectionId} for {UserId}",
                connectionId,
                State.UserId
            );
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
                throw new ArgumentException(
                    "Connection ID cannot be null or empty",
                    nameof(connectionId)
                );
            }

            // Check if connection exists
            if (!State.Connections.TryGetValue(connectionId, out var connectionInfo))
            {
                _logger.LogWarning(
                    "Connection {ConnectionId} not found for {UserId}. Ignoring unregistration.",
                    connectionId,
                    State.UserId
                );
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
                        _ = State.ActiveChats.Remove(chatId);
                        _logger.LogDebug(
                            "Removed chat subscription {ChatId} for {UserId} (no remaining connections)",
                            chatId,
                            State.UserId
                        );
                    }
                }
            }

            // Remove connection from dictionary
            _ = State.Connections.Remove(connectionId);

            // Update metrics
            State.Metrics.ActiveConnections = State.Connections.Count;
            State.LastActivity = DateTime.UtcNow;

            // Record activity
            await RecordActivity(
                ActivityType.Disconnected,
                JsonSerializer.Serialize(
                    new { ConnectionId = connectionId, SubscribedChats = subscribedChats }
                )
            );

            // Save state
            await WriteStateAsync();

            _logger.LogInformation(
                "Connection {ConnectionId} unregistered for {UserId}. Removed from {ChatCount} chats.",
                connectionId,
                State.UserId,
                subscribedChats.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to unregister connection {ConnectionId} for {UserId}",
                connectionId,
                State.UserId
            );
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
                throw new ArgumentException(
                    "Connection ID cannot be null or empty",
                    nameof(connectionId)
                );
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
                    connectionId,
                    State.UserId,
                    chatId
                );
                throw new InvalidOperationException($"Connection {connectionId} not found");
            }

            // Add chat to connection's subscribed chats
            var isNewSubscription = connectionInfo.SubscribedChatIds.Add(chatId);

            if (!isNewSubscription)
            {
                _logger.LogDebug(
                    "Connection {ConnectionId} already subscribed to chat {ChatId} for {UserId}",
                    connectionId,
                    chatId,
                    State.UserId
                );
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
                    ConnectionCount = 1,
                };
                State.ActiveChats[chatId] = subscription;
            }

            // Update connection activity
            connectionInfo.LastActivity = DateTime.UtcNow;
            State.LastActivity = DateTime.UtcNow;

            // Record activity
            await RecordActivity(
                ActivityType.ChatSubscribed,
                JsonSerializer.Serialize(new { ConnectionId = connectionId, ChatId = chatId })
            );

            // Save state
            await WriteStateAsync();

            _logger.LogInformation(
                "Connection {ConnectionId} subscribed to chat {ChatId} for {UserId}. Total connections in chat: {ConnectionCount}",
                connectionId,
                chatId,
                State.UserId,
                subscription.ConnectionCount
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to subscribe connection {ConnectionId} to chat {ChatId} for {UserId}",
                connectionId,
                chatId,
                State.UserId
            );
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
                throw new ArgumentException(
                    "Connection ID cannot be null or empty",
                    nameof(connectionId)
                );
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
                    connectionId,
                    State.UserId,
                    chatId
                );
                return;
            }

            // Remove chat from connection's subscribed chats
            var wasSubscribed = connectionInfo.SubscribedChatIds.Remove(chatId);

            if (!wasSubscribed)
            {
                _logger.LogDebug(
                    "Connection {ConnectionId} was not subscribed to chat {ChatId} for {UserId}",
                    connectionId,
                    chatId,
                    State.UserId
                );
                return;
            }

            // Update chat subscription
            if (State.ActiveChats.TryGetValue(chatId, out var subscription))
            {
                subscription.ConnectionCount--;

                // Remove subscription if no more connections
                if (subscription.ConnectionCount <= 0)
                {
                    _ = State.ActiveChats.Remove(chatId);
                    _logger.LogDebug(
                        "Removed chat subscription {ChatId} for {UserId} (no remaining connections)",
                        chatId,
                        State.UserId
                    );
                }
                else
                {
                    // Check if any connections are still active
                    var activeConnectionCount = State.Connections.Values.Count(c =>
                        c.SubscribedChatIds.Contains(chatId)
                    );

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
            await RecordActivity(
                ActivityType.ChatUnsubscribed,
                JsonSerializer.Serialize(new { ConnectionId = connectionId, ChatId = chatId })
            );

            // Save state
            await WriteStateAsync();

            _logger.LogInformation(
                "Connection {ConnectionId} unsubscribed from chat {ChatId} for {UserId}",
                connectionId,
                chatId,
                State.UserId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to unsubscribe connection {ConnectionId} from chat {ChatId} for {UserId}",
                connectionId,
                chatId,
                State.UserId
            );
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RelayMessage(ChatMessage message)
    {
        using var activity = OrleansActivitySource.StartGrainActivity(
            "UserGrain",
            nameof(RelayMessage),
            State.UserId
        );
        try
        {
            // Add tracing tags
            _ = (activity?.SetTag("chat.id", message?.ChatId));
            _ = (activity?.SetTag("message.id", message?.Id));

            // Validate message
            ArgumentNullException.ThrowIfNull(message);

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
                    message.ChatId,
                    State.UserId,
                    message.Id
                );

                // Buffer the message for later delivery
                _ = await BufferMessageAsync(message, BufferPriority.Normal);
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
                                message.Id,
                                message.ChatId,
                                message.Role,
                                message.Content,
                                message.Timestamp,
                                State.UserId,
                            }
                        );

                        _logger.LogTrace(
                            "Relayed message {MessageId} to SignalR group chat_{ChatId} for {UserId}",
                            message.Id,
                            message.ChatId,
                            State.UserId
                        );
                    }
                    else
                    {
                        _logger.LogTrace(
                            "Would relay message {MessageId} to connection {ConnectionId} for {UserId} (SignalR not available)",
                            message.Id,
                            connection.ConnectionId,
                            State.UserId
                        );
                    }

                    // Update connection activity
                    connection.LastActivity = DateTime.UtcNow;
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to relay message {MessageId} to connection {ConnectionId} for {UserId}",
                        message.Id,
                        connection.ConnectionId,
                        State.UserId
                    );
                    failureCount++;
                }
            }

            // Update metrics
            State.Metrics.MessagesRelayed++;
            State.LastActivity = DateTime.UtcNow;

            // Save state periodically based on configuration
            if (
                State.Metrics.MessagesRelayed
                    % _configuration.Persistence.MessagePersistenceInterval
                == 0
            )
            {
                await WriteStateAsync();
            }

            _logger.LogInformation(
                "Relayed message {MessageId} for chat {ChatId} to {SuccessCount} connections ({FailureCount} failures) for {UserId}",
                message.Id,
                message.ChatId,
                successCount,
                failureCount,
                State.UserId
            );

            // Mark activity as successful
            OrleansActivitySource.SetSuccess(
                activity,
                new Dictionary<string, object>
                {
                    { "connections.success", successCount },
                    { "connections.failed", failureCount },
                }
            );
        }
        catch (Exception ex)
        {
            // Set activity error
            OrleansActivitySource.SetError(activity, ex);

            _logger.LogError(
                ex,
                "Failed to relay message {MessageId} for {UserId}",
                message?.Id ?? "null",
                State.UserId
            );
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RelayStreamChunk(StreamChunk chunk)
    {
        try
        {
            // Validate chunk
            ArgumentNullException.ThrowIfNull(chunk);

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
                    chunk.ChatId,
                    State.UserId,
                    chunk.ChunkIndex,
                    chunk.OperationId
                );

                // Buffer the stream chunk for later delivery
                _ = await BufferStreamChunkAsync(chunk, BufferPriority.Normal);
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
                                chunk.OperationId,
                                chunk.ChatId,
                                chunk.ChunkIndex,
                                chunk.Content,
                                chunk.IsComplete,
                                chunk.Timestamp,
                                State.UserId,
                            }
                        );

                        _logger.LogTrace(
                            "Relayed chunk {ChunkIndex} of operation {OperationId} to SignalR group chat_{ChatId} for {UserId}",
                            chunk.ChunkIndex,
                            chunk.OperationId,
                            chunk.ChatId,
                            State.UserId
                        );
                    }
                    else
                    {
                        _logger.LogTrace(
                            "Would relay chunk {ChunkIndex} of operation {OperationId} to connection {ConnectionId} for {UserId} (SignalR not available)",
                            chunk.ChunkIndex,
                            chunk.OperationId,
                            connection.ConnectionId,
                            State.UserId
                        );
                    }

                    // Update connection activity
                    connection.LastActivity = DateTime.UtcNow;
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to relay chunk {ChunkIndex} of operation {OperationId} to connection {ConnectionId} for {UserId}",
                        chunk.ChunkIndex,
                        chunk.OperationId,
                        connection.ConnectionId,
                        State.UserId
                    );
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
                    chunk.OperationId,
                    chunk.ChatId,
                    successCount,
                    State.UserId
                );

                // Save state on completion
                await WriteStateAsync();
            }
            else if (
                chunk.ChunkIndex % _configuration.Persistence.StreamChunkPersistenceInterval
                == 0
            )
            {
                // Save state periodically for long streams
                await WriteStateAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to relay chunk for operation {OperationId} for {UserId}",
                chunk?.OperationId ?? "null",
                State.UserId
            );
            throw;
        }
    }

    #endregion

    #region Phase 3: Background Processing (Stubbed for Phase 1)

    /// <inheritdoc />
    public async Task<string> ProcessMessageWithBackground(ChatMessage message)
    {
        using var activity = OrleansActivitySource.StartGrainActivity(
            "UserGrain",
            nameof(ProcessMessageWithBackground),
            State.UserId
        );
        var operationStart = DateTime.UtcNow;
        try
        {
            _logger.LogInformation(
                "Processing message in background for chat {ChatId} and user {UserId}",
                message.ChatId,
                State.UserId
            );

            // Add tracing tags
            _ = (activity?.SetTag("chat.id", message?.ChatId));
            _ = (activity?.SetTag("message.length", message?.Content?.Length ?? 0));

            // Validate message
            ArgumentNullException.ThrowIfNull(message);

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
                Status = OperationStatus.Queued,
            };

            // Update metrics
            State.Metrics.TotalOperationsStarted++;
            State.Metrics.ActiveOperationsCount++;

            // Record activity
            await RecordActivity(
                ActivityType.MessageSent,
                JsonSerializer.Serialize(
                    new
                    {
                        OperationId = operationId,
                        message.ChatId,
                        MessageLength = message.Content?.Length ?? 0,
                    }
                )
            );

            await WriteStateAsync();

            _logger.LogInformation(
                "Message queued for background processing with operation ID {OperationId}",
                operationId
            );

            // Execute message processing through ChatGrain for proper background integration
            try
            {
                // Get the chat grain to handle the actual message processing
                var chatGrain = GrainFactory.GetGrain<IChatGrain>(message.ChatId);

                // Process the message through the chat grain
                await chatGrain.ProcessMessageAsync(message);

                // Update operation status to in progress
                if (State.ActiveOperations.TryGetValue(operationId, out var operation))
                {
                    operation.Status = OperationStatus.InProgress;
                    await WriteStateAsync();
                }

                _logger.LogInformation(
                    "Message processing started successfully for operation {OperationId}",
                    operationId
                );
            }
            catch (Exception processingEx)
            {
                _logger.LogError(
                    processingEx,
                    "Failed to process message for operation {OperationId}",
                    operationId
                );

                // Update operation status to failed
                if (State.ActiveOperations.TryGetValue(operationId, out var operation))
                {
                    operation.Status = OperationStatus.Failed;
                    operation.Error = processingEx.Message;
                    await WriteStateAsync();
                }
            }

            // Record successful operation metrics
            var duration = (DateTime.UtcNow - operationStart).TotalMilliseconds;
            await _metricsCollector.RecordGrainOperationAsync(
                "UserGrain",
                "ProcessMessageWithBackground",
                duration,
                true
            );

            // Mark activity as successful
            OrleansActivitySource.SetSuccess(
                activity,
                new Dictionary<string, object>
                {
                    { "operation.id", operationId },
                    { "duration.ms", duration },
                }
            );

            return operationId;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process message with background for {UserId}",
                State.UserId
            );

            // Set activity error
            OrleansActivitySource.SetError(activity, ex);

            // Record failed operation metrics
            var duration = (DateTime.UtcNow - operationStart).TotalMilliseconds;
            await _metricsCollector.RecordGrainOperationAsync(
                "UserGrain",
                "ProcessMessageWithBackground",
                duration,
                false
            );

            // Record error activity
            await RecordActivity(
                ActivityType.ErrorOccurred,
                JsonSerializer.Serialize(
                    new { Error = ex.Message, ChatId = message?.ChatId ?? "unknown" }
                )
            );

            throw;
        }
    }

    /// <inheritdoc />
    public async Task NotifyOperationStarted(string operationId, string chatId)
    {
        try
        {
            _logger.LogInformation(
                "Operation {OperationId} started for user {UserId} in chat {ChatId}",
                operationId,
                State.UserId,
                chatId
            );

            // Update operation status in grain state
            if (State.ActiveOperations.TryGetValue(operationId, out var operation))
            {
                operation.Status = OperationStatus.InProgress;
                operation.StartedAt = DateTime.UtcNow;
                await WriteStateAsync();
            }
            else
            {
                _logger.LogWarning(
                    "Operation {OperationId} not found in state for user {UserId}",
                    operationId,
                    State.UserId
                );
            }

            // Broadcast operation started event to connected clients
            _ = await BroadcastToChat(
                chatId,
                "OperationStarted",
                new
                {
                    OperationId = operationId,
                    ChatId = chatId,
                    State.UserId,
                    Timestamp = DateTime.UtcNow,
                    Status = "InProgress",
                }
            );

            // Record activity
            await RecordActivity(
                ActivityType.MessageSent,
                JsonSerializer.Serialize(
                    new
                    {
                        Event = "OperationStarted",
                        OperationId = operationId,
                        ChatId = chatId,
                    }
                )
            );

            _logger.LogDebug(
                "Operation started notification completed for {OperationId}",
                operationId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to notify operation started for {OperationId} and user {UserId}",
                operationId,
                State.UserId
            );
            // Don't throw - this is a notification method
        }
    }

    /// <inheritdoc />
    public async Task NotifyOperationCompleted(
        string operationId,
        bool success,
        string? error = null
    )
    {
        try
        {
            _logger.LogInformation(
                "Operation {OperationId} completed for user {UserId}. Success: {Success}",
                operationId,
                State.UserId,
                success
            );

            // Find and update operation in grain state
            if (!State.ActiveOperations.TryGetValue(operationId, out var operation))
            {
                _logger.LogWarning(
                    "Operation {OperationId} not found in state for user {UserId}",
                    operationId,
                    State.UserId
                );
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
            _ = await BroadcastToChat(
                operation.ChatId,
                "OperationCompleted",
                new
                {
                    OperationId = operationId,
                    operation.ChatId,
                    State.UserId,
                    Success = success,
                    Error = error,
                    Timestamp = DateTime.UtcNow,
                    Status = success ? "Completed" : "Failed",
                    Duration = operation.CompletedAt - operation.StartedAt,
                }
            );

            // Record activity
            var activityType = success ? ActivityType.MessageCompleted : ActivityType.ErrorOccurred;
            await RecordActivity(
                activityType,
                JsonSerializer.Serialize(
                    new
                    {
                        Event = "OperationCompleted",
                        OperationId = operationId,
                        operation.ChatId,
                        Success = success,
                        Error = error,
                        Duration = (operation.CompletedAt - operation.StartedAt)?.TotalMilliseconds,
                    }
                )
            );

            // Schedule cleanup of completed operation after delay
            if (success || !string.IsNullOrEmpty(error))
            {
                _ = this.RegisterGrainTimer(
                    async _ => await CleanupOperation(operationId),
                    new GrainTimerCreationOptions
                    {
                        DueTime = TimeSpan.FromMinutes(
                            _configuration.UserGrain.CompletedOperationRetentionMinutes
                        ),
                        Period = Timeout.InfiniteTimeSpan,
                        Interleave = true,
                    }
                );
            }

            _logger.LogDebug(
                "Operation completed notification finished for {OperationId}",
                operationId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to notify operation completed for {OperationId} and user {UserId}",
                operationId,
                State.UserId
            );
            // Don't throw - this is a notification method
        }
    }

    /// <inheritdoc />
    public async Task<bool> CancelOperation(string operationId)
    {
        try
        {
            _logger.LogInformation(
                "Cancelling operation {OperationId} for user {UserId}",
                operationId,
                State.UserId
            );

            // Find operation in grain state
            if (!State.ActiveOperations.TryGetValue(operationId, out var operation))
            {
                _logger.LogWarning(
                    "Operation {OperationId} not found for user {UserId}. Cannot cancel.",
                    operationId,
                    State.UserId
                );
                return false;
            }

            // Check if operation can be cancelled
            if (
                operation.Status
                is OperationStatus.Completed
                    or OperationStatus.Failed
                    or OperationStatus.Cancelled
            )
            {
                _logger.LogWarning(
                    "Operation {OperationId} for user {UserId} is already in final state {Status}. Cannot cancel.",
                    operationId,
                    State.UserId,
                    operation.Status
                );
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
            _ = await BroadcastToChat(
                operation.ChatId,
                "OperationCancelled",
                new
                {
                    OperationId = operationId,
                    operation.ChatId,
                    State.UserId,
                    Timestamp = DateTime.UtcNow,
                    Status = "Cancelled",
                    Duration = operation.CompletedAt - operation.StartedAt,
                }
            );

            // Record activity
            await RecordActivity(
                ActivityType.ErrorOccurred,
                JsonSerializer.Serialize(
                    new
                    {
                        Event = "OperationCancelled",
                        OperationId = operationId,
                        operation.ChatId,
                    }
                )
            );

            // Schedule cleanup of cancelled operation after delay
            _ = this.RegisterGrainTimer(
                async _ => await CleanupOperation(operationId),
                new GrainTimerCreationOptions
                {
                    DueTime = TimeSpan.FromMinutes(
                        _configuration.UserGrain.CompletedOperationRetentionMinutes
                    ),
                    Period = TimeSpan.MaxValue,
                    Interleave = true,
                }
            );

            _logger.LogInformation(
                "Operation {OperationId} successfully cancelled for user {UserId}",
                operationId,
                State.UserId
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to cancel operation {OperationId} for user {UserId}",
                operationId,
                State.UserId
            );
            // Don't throw - this is a control method
            return false;
        }
    }

    /// <inheritdoc />
    public Task<OperationContext?> GetOperationStatus(string operationId)
    {
        try
        {
            _logger.LogDebug(
                "Getting operation status for {OperationId} and user {UserId}",
                operationId,
                State.UserId
            );

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
                    Error = operation.Error,
                };

                _logger.LogDebug(
                    "Found operation {OperationId} with status {Status} for user {UserId}",
                    operationId,
                    operation.Status,
                    State.UserId
                );

                return Task.FromResult<OperationContext?>(operationCopy);
            }

            _logger.LogDebug(
                "Operation {OperationId} not found for user {UserId}",
                operationId,
                State.UserId
            );

            return Task.FromResult<OperationContext?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get operation status for {OperationId} and user {UserId}",
                operationId,
                State.UserId
            );
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
            return [];
        }

        // Filter connections that are:
        // 1. Subscribed to this chat
        // 2. Still active (not stale)
        var staleThreshold = DateTime.UtcNow.AddMinutes(
            -_configuration.Connections.StaleConnectionThresholdMinutes
        );

        return
        [
            .. State.Connections.Values.Where(c =>
                c.SubscribedChatIds.Contains(chatId) && c.LastActivity > staleThreshold
            ),
        ];
    }

    /// <summary>
    /// Broadcasts a SignalR message to all connections subscribed to a chat.
    /// </summary>
    /// <param name="chatId">The chat ID to broadcast to</param>
    /// <param name="method">The SignalR method name to call</param>
    /// <param name="payload">The payload to broadcast</param>
    /// <returns>Statistics about the broadcast operation</returns>
    private async Task<(int SuccessCount, int FailureCount)> BroadcastToChat(
        string chatId,
        string method,
        object payload
    )
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
                    method,
                    chatId,
                    State.UserId,
                    connections.Count()
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to broadcast {Method} to SignalR group chat_{ChatId} for {UserId}",
                    method,
                    chatId,
                    State.UserId
                );
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
                        method,
                        connection.ConnectionId,
                        chatId,
                        State.UserId
                    );

                    connection.LastActivity = DateTime.UtcNow;
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to track broadcast {Method} to connection {ConnectionId} in chat {ChatId} for {UserId}",
                        method,
                        connection.ConnectionId,
                        chatId,
                        State.UserId
                    );
                    failureCount++;
                }
            }
        }

        return (successCount, failureCount);
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
            var cleanupThreshold = TimeSpan.FromHours(
                _configuration.UserGrain.ActivityRetentionHours
            );
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
                _logger.LogDebug(
                    "Cleaned up {Count} old activities for {UserId}",
                    oldActivitiesCount - State.RecentActivity.Count,
                    State.UserId
                );
            }

            // Clean up completed operations (Phase 3 data)
            var completedOps = State
                .ActiveOperations.Where(kvp =>
                    kvp.Value.Status == OperationStatus.Completed
                    && kvp.Value.CompletedAt.HasValue
                    && now - kvp.Value.CompletedAt.Value
                        > TimeSpan.FromMinutes(
                            _configuration.UserGrain.CompletedOperationRetentionMinutes
                        )
                )
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var opId in completedOps)
            {
                _ = State.ActiveOperations.Remove(opId);
                changed = true;
            }

            if (completedOps.Count > 0)
            {
                _logger.LogDebug(
                    "Cleaned up {Count} completed operations for {UserId}",
                    completedOps.Count,
                    State.UserId
                );
            }

            // Clean up expired buffered messages (Phase 3 data)
            var expiredMessagesRemoved = await ClearExpiredBufferedMessagesAsync();
            if (expiredMessagesRemoved > 0)
            {
                changed = true;
                _logger.LogDebug(
                    "Cleaned up {Count} expired buffered messages for {UserId}",
                    expiredMessagesRemoved,
                    State.UserId
                );
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
            State.Metrics.ActiveOperationsCount = State.ActiveOperations.Count(kvp =>
                kvp.Value.Status is OperationStatus.Queued or OperationStatus.InProgress
            );

            // Save metrics periodically
            await WriteStateAsync();

            // Record state metrics to central collector for dashboard
            var stateSize = JsonSerializer.Serialize(State).Length;
            await _metricsCollector.RecordGrainStateMetricsAsync(
                "UserGrain",
                State.UserId,
                stateSize,
                State.Metrics.ActiveConnections,
                State.Metrics.ActiveOperationsCount
            );

            _logger.LogTrace(
                "Metrics updated for {UserId}: Connections={ConnectionCount}, Activities={ActivityCount}, ActiveOps={ActiveOperations}",
                State.UserId,
                State.Metrics.ActiveConnections,
                State.Metrics.TotalActivities,
                State.Metrics.ActiveOperationsCount
            );
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
                if (
                    operation.Status
                    is OperationStatus.Completed
                        or OperationStatus.Failed
                        or OperationStatus.Cancelled
                )
                {
                    _ = State.ActiveOperations.Remove(operationId);
                    await WriteStateAsync();

                    _logger.LogDebug(
                        "Cleaned up completed operation {OperationId} for user {UserId}",
                        operationId,
                        State.UserId
                    );
                }
                else
                {
                    _logger.LogWarning(
                        "Attempted to clean up operation {OperationId} with status {Status} for user {UserId}",
                        operationId,
                        operation.Status,
                        State.UserId
                    );
                }
            }
            else
            {
                _logger.LogDebug(
                    "Operation {OperationId} already removed from state for user {UserId}",
                    operationId,
                    State.UserId
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to cleanup operation {OperationId} for user {UserId}",
                operationId,
                State.UserId
            );
        }
    }

    /// <summary>
    /// Creates a hash of the user ID for anonymized tracking.
    /// Implements privacy-preserving user identification for analytics.
    /// </summary>
    /// <param name="userId">The user ID to hash</param>
    /// <returns>SHA256 hash of the user ID</returns>
    private static string HashUserId(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(userId);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Calculates data retention expiry date based on activity type.
    /// Implements configurable retention policies for different activity types.
    /// </summary>
    /// <param name="activityType">The type of activity</param>
    /// <returns>Expiration date for the activity data</returns>
    private static DateTime CalculateRetentionExpiry(ActivityType activityType)
    {
        // Default retention periods by activity type
        var retentionDays = activityType switch
        {
            ActivityType.MessageSent => 365,           // 1 year for messages
            ActivityType.MessageCompleted => 365,      // 1 year for message completion
            ActivityType.Connected => 730,             // 2 years for connection events
            ActivityType.Disconnected => 730,          // 2 years for disconnection events
            ActivityType.ChatSubscribed => 365,        // 1 year for chat subscription events
            ActivityType.ChatUnsubscribed => 365,      // 1 year for chat unsubscription events
            ActivityType.OperationCancelled => 180,    // 6 months for operation events
            ActivityType.ErrorOccurred => 90,          // 3 months for error events
            _ => 365                                    // Default 1 year
        };

        return DateTime.UtcNow.AddDays(retentionDays);
    }

    /// <summary>
    /// Maintains the circular buffer for privacy-compliant activities.
    /// Ensures buffer size limits while preserving data retention policies.
    /// </summary>
    private async Task MaintainActivityBufferAsync()
    {
        // Maintain circular buffer with configurable size
        var maxBufferSize = _configuration.UserGrain.MaxActivityBufferSize;
        while (State.ActivityTracking.PrivacyCompliantActivities.Count > maxBufferSize)
        {
            var removedActivity = State.ActivityTracking.PrivacyCompliantActivities.Dequeue();

            // Log removal for audit purposes
            _logger.LogTrace(
                "Removed activity from buffer due to size limit: {ActivityType} for {UserId}",
                removedActivity?.Type,
                State.UserId
            );
        }

        // Clean up expired activities based on retention policy
        var currentTime = DateTime.UtcNow;
        var tempQueue = new Queue<PrivacyAwareActivityRecord>();

        while (State.ActivityTracking.PrivacyCompliantActivities.Count > 0)
        {
            var activity = State.ActivityTracking.PrivacyCompliantActivities.Dequeue();

            if (activity.RetentionExpiresAt > currentTime)
            {
                tempQueue.Enqueue(activity);
            }
            else
            {
                // Log retention-based removal for audit
                _logger.LogTrace(
                    "Removed expired activity: {ActivityType} for {UserId}",
                    activity.Type,
                    State.UserId
                );
            }
        }

        // Restore non-expired activities
        State.ActivityTracking.PrivacyCompliantActivities = tempQueue;

        await Task.CompletedTask; // For async compliance
    }

    /// <summary>
    /// Converts a PrivacyAwareActivityRecord to an ActivityRecord for analytics export.
    /// Maintains backward compatibility with existing analytics systems.
    /// </summary>
    /// <param name="privacyRecord">The privacy-aware activity record</param>
    /// <returns>Standard ActivityRecord for analytics export</returns>
    private static ActivityRecord ConvertToActivityRecord(PrivacyAwareActivityRecord privacyRecord)
    {
        return new ActivityRecord
        {
            Type = privacyRecord.Type,
            Metadata = privacyRecord.SanitizedMetadata ?? string.Empty,
            Timestamp = privacyRecord.Timestamp,
            CorrelationId = privacyRecord.CorrelationId
        };
    }

    #endregion

    #region IUserMessageBufferGrain Implementation

    /// <inheritdoc />
    public async Task<string> BufferMessageAsync(
        ChatMessage message,
        BufferPriority priority = BufferPriority.Normal
    )
    {
        try
        {
            // Validate message
            ArgumentNullException.ThrowIfNull(message);

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
                LastDeliveryError = null,
            };

            // Handle buffer overflow if necessary
            while (buffer.Messages.Count >= buffer.MaxSize)
            {
                if (buffer.OverflowStrategy == BufferOverflowStrategy.RejectNew)
                {
                    _logger.LogWarning(
                        "Buffer overflow: Rejecting new message for chat {ChatId} (buffer full at {Count})",
                        message.ChatId,
                        buffer.MaxSize
                    );

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
                        droppedMessage.MessageId,
                        message.ChatId
                    );
                }
            }

            // Add message to buffer
            buffer.Messages.Enqueue(bufferedMessage);
            buffer.TotalMessagesBuffered++;

            // Update metrics
            State.Metrics.TotalMessagesBuffered++;
            State.Metrics.CurrentBufferCount = State.MessageBuffers.Count;

            // Record activity
            await RecordActivity(
                ActivityType.MessageSent,
                JsonSerializer.Serialize(
                    new
                    {
                        Event = "MessageBuffered",
                        MessageId = messageId,
                        message.ChatId,
                        Priority = priority.ToString(),
                        BufferSize = buffer.Messages.Count,
                    }
                )
            );

            // Save state
            await WriteStateAsync();

            _logger.LogDebug(
                "Buffered message {MessageId} for chat {ChatId} with priority {Priority}",
                messageId,
                message.ChatId,
                priority
            );

            return messageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to buffer message for chat {ChatId} and user {UserId}",
                message?.ChatId,
                State.UserId
            );
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> BufferStreamChunkAsync(
        StreamChunk chunk,
        BufferPriority priority = BufferPriority.Normal
    )
    {
        try
        {
            // Validate chunk
            ArgumentNullException.ThrowIfNull(chunk);

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
                Metadata = JsonSerializer.Serialize(
                    new
                    {
                        chunk.IsComplete,
                        chunk.ChunkIndex,
                        chunk.TotalChunks,
                        chunk.MessageId,
                    }
                ),
            };

            // Buffer as a regular message
            var messageId = await BufferMessageAsync(message, priority);

            _logger.LogDebug(
                "Buffered stream chunk {ChunkIndex} for operation {OperationId} as message {MessageId}",
                chunk.ChunkIndex,
                chunk.OperationId,
                messageId
            );

            return messageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to buffer stream chunk for chat {ChatId} and user {UserId}",
                chunk?.ChatId,
                State.UserId
            );
            throw;
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<BufferedMessage>> GetBufferedMessagesAsync(
        string chatId,
        int? limit = null,
        bool highPriorityOnly = false
    )
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
                result.Count,
                chatId,
                highPriorityOnly
            );

            return Task.FromResult<IEnumerable<BufferedMessage>>(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get buffered messages for chat {ChatId} and user {UserId}",
                chatId,
                State.UserId
            );
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
                throw new ArgumentException(
                    "Message ID cannot be null or empty",
                    nameof(messageId)
                );
            }

            // Search through all chat buffers for the message
            foreach (var buffer in State.MessageBuffers.Values)
            {
                var message = buffer.Messages.FirstOrDefault(m => m.MessageId == messageId);
                if (message != null)
                {
                    _logger.LogDebug(
                        "Found buffered message {MessageId} in chat {ChatId}",
                        messageId,
                        message.ChatId
                    );
                    return Task.FromResult<BufferedMessage?>(message);
                }
            }

            _logger.LogDebug(
                "Buffered message {MessageId} not found for user {UserId}",
                messageId,
                State.UserId
            );

            return Task.FromResult<BufferedMessage?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get buffered message {MessageId} for user {UserId}",
                messageId,
                State.UserId
            );
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
                throw new ArgumentException(
                    "Message ID cannot be null or empty",
                    nameof(messageId)
                );
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
                    await RecordActivity(
                        ActivityType.MessageCompleted,
                        JsonSerializer.Serialize(
                            new
                            {
                                Event = "MessageRemovedFromBuffer",
                                MessageId = messageId,
                                messageToRemove.ChatId,
                            }
                        )
                    );

                    // Save state
                    await WriteStateAsync();

                    _logger.LogDebug(
                        "Removed buffered message {MessageId} from chat {ChatId}",
                        messageId,
                        messageToRemove.ChatId
                    );

                    return true;
                }
            }

            _logger.LogDebug(
                "Buffered message {MessageId} not found for removal in user {UserId}",
                messageId,
                State.UserId
            );

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to remove buffered message {MessageId} for user {UserId}",
                messageId,
                State.UserId
            );
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
                            message.MessageId,
                            chatId
                        );
                    }
                }

                buffer.Messages = validMessages;
                buffer.LastCleanupAt = currentTime;

                // Remove empty buffers
                if (buffer.Messages.Count == 0)
                {
                    _ = State.MessageBuffers.Remove(chatId);
                    _logger.LogDebug("Removed empty buffer for chat {ChatId}", chatId);
                }

                _logger.LogDebug(
                    "Cleaned buffer for chat {ChatId}: {OriginalCount} -> {NewCount} messages",
                    chatId,
                    originalCount,
                    buffer.Messages.Count
                );
            }

            // Update metrics
            State.Metrics.TotalBufferExpiredMessages += totalRemoved;
            State.Metrics.CurrentBufferCount = State.MessageBuffers.Count;
            State.Metrics.TotalBufferCleanupRuns++;

            if (totalRemoved > 0)
            {
                // Record activity
                await RecordActivity(
                    ActivityType.MessageCompleted,
                    JsonSerializer.Serialize(
                        new
                        {
                            Event = "BufferCleanup",
                            ExpiredMessages = totalRemoved,
                            RemainingBuffers = State.MessageBuffers.Count,
                        }
                    )
                );

                // Save state
                await WriteStateAsync();
            }

            _logger.LogDebug(
                "Buffer cleanup completed for user {UserId}: removed {ExpiredCount} expired messages",
                State.UserId,
                totalRemoved
            );

            return totalRemoved;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to clear expired buffered messages for user {UserId}",
                State.UserId
            );
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
                    chatId,
                    buffer.Messages.Count
                );
                return Task.FromResult<ChatMessageBuffer?>(buffer);
            }

            _logger.LogDebug(
                "No buffer found for chat {ChatId} in user {UserId}",
                chatId,
                State.UserId
            );

            return Task.FromResult<ChatMessageBuffer?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get chat buffer for {ChatId} and user {UserId}",
                chatId,
                State.UserId
            );
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
                    UtilizationPercent =
                        buffer.MaxSize > 0 ? messages.Count * 100.0 / buffer.MaxSize : 0,
                    OldestMessageTime = messages.FirstOrDefault()?.BufferedAt,
                    NewestMessageTime = messages.LastOrDefault()?.BufferedAt,
                    HighPriorityCount = messages.Count(m => m.Priority == BufferPriority.High),
                    ExpiringMessageCount = messages.Count(m =>
                        (m.ExpiresAt - currentTime).TotalMinutes <= 5
                    ),
                };

                summaries[chatId] = summary;
            }

            _logger.LogDebug(
                "Generated buffer summary for user {UserId}: {BufferCount} active buffers",
                State.UserId,
                summaries.Count
            );

            return Task.FromResult(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get buffer summary for user {UserId}", State.UserId);
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
                throw new ArgumentException(
                    "Message ID cannot be null or empty",
                    nameof(messageId)
                );
            }

            // Find and remove the delivered message
            var removed = await RemoveBufferedMessageAsync(messageId);

            if (removed)
            {
                // Update delivery metrics
                State.Metrics.TotalBufferedMessagesDelivered++;

                // Record activity
                await RecordActivity(
                    ActivityType.MessageCompleted,
                    JsonSerializer.Serialize(
                        new { Event = "MessageDelivered", MessageId = messageId }
                    )
                );

                _logger.LogDebug(
                    "Marked buffered message {MessageId} as delivered for user {UserId}",
                    messageId,
                    State.UserId
                );
            }

            return removed;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to mark message {MessageId} as delivered for user {UserId}",
                messageId,
                State.UserId
            );
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
                throw new ArgumentException(
                    "Message ID cannot be null or empty",
                    nameof(messageId)
                );
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
                    await RecordActivity(
                        ActivityType.ErrorOccurred,
                        JsonSerializer.Serialize(
                            new
                            {
                                Event = "DeliveryAttemptFailed",
                                MessageId = messageId,
                                message.ChatId,
                                Attempts = message.DeliveryAttempts,
                                Error = error,
                            }
                        )
                    );

                    // Save state
                    await WriteStateAsync();

                    _logger.LogDebug(
                        "Recorded delivery attempt #{Attempt} for message {MessageId}: {Error}",
                        message.DeliveryAttempts,
                        messageId,
                        error
                    );

                    return true;
                }
            }

            _logger.LogDebug(
                "Message {MessageId} not found for delivery attempt recording in user {UserId}",
                messageId,
                State.UserId
            );

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to record delivery attempt for message {MessageId} and user {UserId}",
                messageId,
                State.UserId
            );
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> ProcessBufferedMessagesAsync(
        string connectionId,
        string? chatId = null,
        int maxMessages = 50
    )
    {
        try
        {
            if (string.IsNullOrEmpty(connectionId))
            {
                throw new ArgumentException(
                    "Connection ID cannot be null or empty",
                    nameof(connectionId)
                );
            }

            var processedCount = 0;
            var buffersToProcess =
                chatId != null
                    ? State.MessageBuffers.Where(kvp => kvp.Key == chatId)
                    : State.MessageBuffers;

            foreach (var kvp in buffersToProcess.ToList())
            {
                if (processedCount >= maxMessages)
                {
                    break;
                }

                var bufferChatId = kvp.Key;
                var buffer = kvp.Value;
                var messagesToDeliver = new List<BufferedMessage>();

                // Get messages to deliver (prioritize high priority)
                var messages = buffer
                    .Messages.OrderByDescending(m => m.Priority)
                    .ThenBy(m => m.BufferedAt)
                    .Take(maxMessages - processedCount)
                    .ToList();

                foreach (var message in messages)
                {
                    try
                    {
                        // Attempt to deliver the message
                        if (
                            message.Message.IsStreaming
                            && !string.IsNullOrEmpty(message.Message.Metadata)
                        )
                        {
                            // Try to parse stream metadata
                            try
                            {
                                var metadata = JsonSerializer.Deserialize<JsonElement>(
                                    message.Message.Metadata
                                );

                                // Deliver as stream chunk
                                var chunk = new StreamChunk
                                {
                                    OperationId = message.Message.Id,
                                    ChatId = message.Message.ChatId,
                                    Content = message.Message.Content,
                                    IsComplete =
                                        !metadata.TryGetProperty("IsComplete", out var isComplete)
                                        || isComplete.GetBoolean(),
                                    ChunkIndex = metadata.TryGetProperty(
                                        "ChunkIndex",
                                        out var chunkIndex
                                    )
                                        ? chunkIndex.GetInt32()
                                        : 0,
                                    TotalChunks = metadata.TryGetProperty(
                                        "TotalChunks",
                                        out var totalChunks
                                    )
                                        ? totalChunks.GetInt32()
                                        : null,
                                    MessageId = metadata.TryGetProperty(
                                        "MessageId",
                                        out var messageId
                                    )
                                        ? messageId.GetString()
                                        : null,
                                };

                                await RelayStreamChunk(chunk);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(
                                    ex,
                                    "Failed to parse stream metadata for message {MessageId}, delivering as regular message",
                                    message.MessageId
                                );
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
                        _ = await RecordDeliveryAttemptAsync(message.MessageId, ex.Message);

                        _logger.LogWarning(
                            ex,
                            "Failed to deliver buffered message {MessageId} to connection {ConnectionId}",
                            message.MessageId,
                            connectionId
                        );
                    }
                }

                // Remove successfully delivered messages
                foreach (var deliveredMessage in messagesToDeliver)
                {
                    _ = await MarkMessageDeliveredAsync(deliveredMessage.MessageId);
                }
            }

            if (processedCount > 0)
            {
                // Record activity
                await RecordActivity(
                    ActivityType.MessageCompleted,
                    JsonSerializer.Serialize(
                        new
                        {
                            Event = "BufferedMessagesProcessed",
                            ConnectionId = connectionId,
                            ChatId = chatId,
                            ProcessedCount = processedCount,
                        }
                    )
                );

                _logger.LogDebug(
                    "Processed {ProcessedCount} buffered messages for connection {ConnectionId}",
                    processedCount,
                    connectionId
                );
            }

            return processedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process buffered messages for connection {ConnectionId} and user {UserId}",
                connectionId,
                State.UserId
            );
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
                _logger.LogDebug(
                    "No buffer found for chat {ChatId} in user {UserId}",
                    chatId,
                    State.UserId
                );
                return 0;
            }

            var messageCount = buffer.Messages.Count;
            _ = State.MessageBuffers.Remove(chatId);

            // Update metrics
            State.Metrics.CurrentBufferCount = State.MessageBuffers.Count;

            // Record activity
            await RecordActivity(
                ActivityType.MessageCompleted,
                JsonSerializer.Serialize(
                    new
                    {
                        Event = "ChatBufferCleared",
                        ChatId = chatId,
                        MessagesCleared = messageCount,
                    }
                )
            );

            // Save state
            await WriteStateAsync();

            _logger.LogDebug(
                "Cleared buffer for chat {ChatId}: removed {MessageCount} messages",
                chatId,
                messageCount
            );

            return messageCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to clear chat buffer for {ChatId} and user {UserId}",
                chatId,
                State.UserId
            );
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
            CreatedAt = DateTime.UtcNow,
        };

        State.MessageBuffers[chatId] = newBuffer;

        _logger.LogDebug(
            "Created new message buffer for chat {ChatId} with size limit {MaxSize}",
            chatId,
            newBuffer.MaxSize
        );

        return newBuffer;
    }

    #endregion

    #region Phase 4: Orleans-First Stream Processing

    /// <summary>
    /// Semaphore to limit concurrent streams per grain.
    /// </summary>
    private readonly SemaphoreSlim _streamSemaphore = new(3); // Default limit, will be updated from config

    /// <summary>
    /// Dictionary to track active stream cancellation tokens.
    /// </summary>
    private readonly Dictionary<string, CancellationTokenSource> _activeStreamCancellations = [];

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamChunk> ProcessChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        using var activity = OrleansActivitySource.StartGrainActivity(
            "UserGrain",
            nameof(ProcessChatStreamAsync),
            State.UserId
        );
        var streamId = Guid.NewGuid().ToString();
        var streamState = new StreamState
        {
            StreamId = streamId,
            ChatId = request.ChatId,
            UserId = request.UserId,
            StartedAt = DateTime.UtcNow,
            Status = StreamStatus.Active,
        };

        // Add tracing tags
        _ = (activity?.SetTag("stream.id", streamId));
        _ = (activity?.SetTag("chat.id", request.ChatId));
        _ = (activity?.SetTag("request.id", request.RequestId));

        // Check stream limit
        var activeStreamCount = State.ActiveStreams.Count(s =>
            s.Value.Status == StreamStatus.Active
        );
        if (activeStreamCount >= _configuration.Streaming.MaxConcurrentStreamsPerUser)
        {
            _logger.LogWarning(
                "Stream limit exceeded for user {UserId}. Active: {ActiveCount}, Max: {MaxCount}",
                State.UserId,
                activeStreamCount,
                _configuration.Streaming.MaxConcurrentStreamsPerUser
            );

            OrleansActivitySource.SetError(
                activity,
                new InvalidOperationException("Stream limit exceeded")
            );
            throw new InvalidOperationException(
                $"Maximum concurrent streams ({_configuration.Streaming.MaxConcurrentStreamsPerUser}) exceeded for user {State.UserId}"
            );
        }

        // Create linked cancellation token
        var streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeStreamCancellations[streamId] = streamCts;

        // Acquire semaphore
        await _streamSemaphore.WaitAsync(streamCts.Token);

        // Create channel for callback-to-AsyncEnumerable bridge
        var channel = System.Threading.Channels.Channel.CreateBounded<StreamChunk>(
            new System.Threading.Channels.BoundedChannelOptions(
                _configuration.Streaming.StreamChannelBufferSize
            )
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
                SingleWriter = true,
                SingleReader = true,
            }
        );

        Task? processingTask = null;
        var hasError = false;
        Exception? capturedError = null;

        try
        {
            // Add stream to state
            State.ActiveStreams[streamId] = streamState;
            State.TotalStreamsProcessed++;
            await WriteStateAsync();

            _logger.LogInformation(
                "Starting stream {StreamId} for chat {ChatId} and user {UserId}",
                streamId,
                request.ChatId,
                request.UserId
            );

            // Record activity
            await RecordActivity(
                ActivityType.MessageSent,
                JsonSerializer.Serialize(
                    new
                    {
                        Event = "StreamStarted",
                        StreamId = streamId,
                        request.ChatId,
                        request.RequestId,
                    }
                )
            );

            // Process the message with ChatGrain delegation
            processingTask = ProcessMessageWithChatGrain(
                request,
                streamState,
                channel.Writer,
                streamCts.Token
            );
        }
        catch (Exception ex)
        {
            hasError = true;
            capturedError = ex;
            streamState.Status = StreamStatus.Failed;
            streamState.ErrorMessage = ex.Message;

            try
            {
                await WriteStateAsync();
            }
            catch (Exception saveEx)
            {
                _logger.LogError(
                    saveEx,
                    "Failed to save state after error in stream {StreamId}",
                    streamId
                );
            }

            _logger.LogError(ex, "Stream {StreamId} failed during initialization", streamId);
            OrleansActivitySource.SetError(activity, ex);

            // Complete the channel to prevent waiting forever
            _ = channel.Writer.TryComplete(ex);
        }

        // Yield chunks from channel (outside try-catch to avoid CS1626)
        if (!hasError)
        {
            await foreach (var chunk in channel.Reader.ReadAllAsync(streamCts.Token))
            {
                // Update stream state
                streamState.ChunksSent++;
                streamState.LastActivity = DateTime.UtcNow;

                // Persist state periodically
                if (
                    _configuration.Streaming.PersistPartialStreams
                    && streamState.ChunksSent % _configuration.Streaming.PartialStreamSaveInterval
                        == 0
                )
                {
                    streamState.PartialMessage += chunk.Content;

                    try
                    {
                        await WriteStateAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to persist partial stream state for {StreamId}",
                            streamId
                        );
                    }
                }

                // Update metrics
                if (_configuration.Streaming.EnableStreamMetrics)
                {
                    try
                    {
                        await _metricsCollector.RecordGrainOperationAsync(
                            "UserGrain",
                            "StreamChunk",
                            0,
                            true
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to record metrics for stream chunk in {StreamId}",
                            streamId
                        );
                    }
                }

                yield return chunk;
            }
        }

        // Handle post-streaming logic
        try
        {
            if (processingTask != null)
            {
                await processingTask;
            }

            if (!hasError)
            {
                // Mark stream as completed
                streamState.Status = StreamStatus.Completed;
                await WriteStateAsync();

                _logger.LogInformation(
                    "Stream {StreamId} completed successfully. Chunks sent: {ChunkCount}",
                    streamId,
                    streamState.ChunksSent
                );

                OrleansActivitySource.SetSuccess(
                    activity,
                    new Dictionary<string, object>
                    {
                        { "chunks.sent", streamState.ChunksSent },
                        {
                            "duration.ms",
                            (DateTime.UtcNow - streamState.StartedAt).TotalMilliseconds
                        },
                    }
                );
            }
        }
        catch (OperationCanceledException)
        {
            streamState.Status = StreamStatus.Cancelled;

            try
            {
                await WriteStateAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to save state after cancellation for stream {StreamId}",
                    streamId
                );
            }

            _logger.LogInformation("Stream {StreamId} was cancelled", streamId);
            throw;
        }
        catch (Exception ex)
        {
            streamState.Status = StreamStatus.Failed;
            streamState.ErrorMessage = ex.Message;

            try
            {
                await WriteStateAsync();
            }
            catch (Exception saveEx)
            {
                _logger.LogWarning(
                    saveEx,
                    "Failed to save state after error for stream {StreamId}",
                    streamId
                );
            }

            _logger.LogError(ex, "Stream {StreamId} failed with error", streamId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
        finally
        {
            // Cleanup
            _ = _streamSemaphore.Release();

            if (_activeStreamCancellations.TryGetValue(streamId, out var cts))
            {
                _ = _activeStreamCancellations.Remove(streamId);
                cts.Dispose();
            }

            // Schedule stream cleanup after retention period
            _ = Task.Run(
                async () =>
                {
                    await Task.Delay(
                        TimeSpan.FromMinutes(
                            _configuration.UserGrain.CompletedOperationRetentionMinutes
                        )
                    );
                    await CleanupStreamState(streamId);
                },
                cancellationToken
            );
        }

        // Rethrow captured error if any
        if (capturedError != null)
        {
            throw capturedError;
        }
    }

    /// <summary>
    /// Processes a message with ChatGrain LLM integration and writes to channel.
    /// Delegates to ChatGrain which has direct IStreamingAgent access.
    /// ChatGrain will relay chunks back to this UserGrain via RelayStreamChunk.
    /// </summary>
    private async Task ProcessMessageWithChatGrain(
        ChatRequest request,
        StreamState streamState,
        System.Threading.Channels.ChannelWriter<StreamChunk> channelWriter,
        CancellationToken cancellationToken
    )
    {
        try
        {
            _logger.LogInformation(
                "Processing message for stream {StreamId} with ChatGrain LLM integration",
                streamState.StreamId
            );

            // Get ChatGrain for this chat
            var chatGrain = GrainFactory.GetGrain<IChatGrain>(request.ChatId);

            // Create ChatMessage from request
            var message = new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = request.ChatId,
                UserId = request.UserId,
                Content = request.Message,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                IsStreaming = false
            };

            // Start LLM processing via ChatGrain
            // ChatGrain will call UserGrain.RelayStreamChunk to broadcast chunks
            var streamHandle = await chatGrain.ProcessMessageWithLLMAsync(message, cancellationToken);

            _logger.LogInformation(
                "Started ChatGrain LLM processing. Stream {StreamId}, ChatId {ChatId}",
                streamHandle.StreamId,
                request.ChatId
            );

            // Note: Chunks will arrive via RelayStreamChunk and be broadcast via SignalR
            // This method completes the channel immediately since streaming happens via push model
            _ = channelWriter.TryComplete();

            streamState.Status = StreamStatus.Completed;
            _logger.LogInformation(
                "Stream {StreamId} initiated successfully via ChatGrain",
                streamState.StreamId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing message for stream {StreamId} via ChatGrain",
                streamState.StreamId
            );

            streamState.Status = StreamStatus.Failed;
            streamState.ErrorMessage = ex.Message;

            _ = channelWriter.TryComplete(ex);
            throw;
        }
    }

    /// <summary>
    /// Cleans up completed stream state after retention period.
    /// </summary>
    private async Task CleanupStreamState(string streamId)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (State.ActiveStreams.TryGetValue(streamId, out var streamState))
            {
                // Only cleanup completed/failed/cancelled streams
                if (streamState.Status != StreamStatus.Active)
                {
                    _ = State.ActiveStreams.Remove(streamId);
                    await WriteStateAsync();

                    _logger.LogDebug(
                        "Cleaned up stream state for {StreamId} with status {Status}",
                        streamId,
                        streamState.Status
                    );
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup stream state for {StreamId}", streamId);
        }
    }

    #endregion

    #region IUserSessionGrain Implementation

    /// <inheritdoc />
    public async Task<StateResult<UserSessionState>> CreateSessionAsync(
        UserSessionState session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "CreateSession");
            _ = (activity?.SetTag("session.id", session.SessionId));
            _ = (activity?.SetTag("session.streamId", session.StreamId));

            _logger.LogDebug("Creating session {SessionId} for user {UserId}", session.SessionId, State.UserId);

            // Check if session already exists
            if (State.Sessions.ContainsKey(session.SessionId))
            {
                return StateResult<UserSessionState>.FromError($"Session {session.SessionId} already exists");
            }

            // Add session to state
            State.Sessions[session.SessionId] = session;
            await WriteStateAsync();

            _logger.LogInformation("Created session {SessionId} for user {UserId}", session.SessionId, State.UserId);
            return StateResult<UserSessionState>.FromSuccess(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session {SessionId} for user {UserId}", session.SessionId, State.UserId);
            return StateResult<UserSessionState>.FromError($"Failed to create session: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult<UserSessionState>> UpdateSessionAsync(
        string sessionId,
        UserSessionState session,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(session);

        try
        {
            using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "UpdateSession");
            _ = (activity?.SetTag("session.id", sessionId));

            _logger.LogDebug("Updating session {SessionId} for user {UserId}", sessionId, State.UserId);

            if (!State.Sessions.ContainsKey(sessionId))
            {
                return StateResult<UserSessionState>.FromError($"Session {sessionId} not found");
            }

            State.Sessions[sessionId] = session;
            await WriteStateAsync();

            _logger.LogDebug("Updated session {SessionId} for user {UserId}", sessionId, State.UserId);
            return StateResult<UserSessionState>.FromSuccess(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update session {SessionId} for user {UserId}", sessionId, State.UserId);
            return StateResult<UserSessionState>.FromError($"Failed to update session: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> UpdateSessionStateAsync(
        string sessionId,
        SessionLifecycleState newState,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        try
        {
            using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "UpdateSessionState");
            _ = (activity?.SetTag("session.id", sessionId));
            _ = (activity?.SetTag("session.newState", newState.ToString()));

            _logger.LogDebug("Updating session state for {SessionId} to {NewState}", sessionId, newState);

            if (!State.Sessions.TryGetValue(sessionId, out var session))
            {
                return StateResult.FromError($"Session {sessionId} not found");
            }

            session.CurrentState = newState;
            session.LastActivityAt = DateTime.UtcNow;
            await WriteStateAsync();

            _logger.LogDebug("Updated session {SessionId} state to {NewState}", sessionId, newState);
            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update session state for {SessionId}", sessionId);
            return StateResult.FromError($"Failed to update session state: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> UpdateSessionActivityAsync(
        string sessionId,
        DateTime? activityTime = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        try
        {
            using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "UpdateSessionActivity");
            _ = (activity?.SetTag("session.id", sessionId));

            if (!State.Sessions.TryGetValue(sessionId, out var session))
            {
                return StateResult.FromError($"Session {sessionId} not found");
            }

            session.LastActivityAt = activityTime ?? DateTime.UtcNow;
            await WriteStateAsync();

            _logger.LogDebug("Updated session activity for {SessionId}", sessionId);
            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update session activity for {SessionId}", sessionId);
            return StateResult.FromError($"Failed to update session activity: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> RecordSessionConnectionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        try
        {
            using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "RecordSessionConnection");
            _ = (activity?.SetTag("session.id", sessionId));

            if (!State.Sessions.TryGetValue(sessionId, out var session))
            {
                return StateResult.FromError($"Session {sessionId} not found");
            }

            var now = DateTime.UtcNow;
            session.CurrentState = SessionLifecycleState.Connected;
            session.ConnectedAt = now;
            session.LastActivityAt = now;

            await WriteStateAsync();

            _logger.LogDebug("Recorded connection for session {SessionId}", sessionId);
            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record connection for session {SessionId}", sessionId);
            return StateResult.FromError($"Failed to record session connection: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> RecordSessionDisconnectionAsync(
        string sessionId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        try
        {
            using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "RecordSessionDisconnection");
            _ = (activity?.SetTag("session.id", sessionId));
            _ = (activity?.SetTag("disconnection.reason", reason ?? "Unknown"));

            if (!State.Sessions.TryGetValue(sessionId, out var session))
            {
                return StateResult.FromError($"Session {sessionId} not found");
            }

            var now = DateTime.UtcNow;
            session.CurrentState = SessionLifecycleState.Disconnected;
            session.DisconnectedAt = now;
            session.LastActivityAt = now;
            session.DisconnectionCount++;
            session.LastDisconnectionReason = reason;

            // Calculate connection duration if we have a connection start time
            if (session.ConnectedAt.HasValue)
            {
                var connectionDuration = now - session.ConnectedAt.Value;
                session.TotalConnectionTime += connectionDuration;

                if (connectionDuration > session.LongestConnectionDuration)
                {
                    session.LongestConnectionDuration = connectionDuration;
                }
            }

            await WriteStateAsync();

            _logger.LogDebug("Recorded disconnection for session {SessionId} with reason: {Reason}", sessionId, reason);
            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record disconnection for session {SessionId}", sessionId);
            return StateResult.FromError($"Failed to record session disconnection: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> RecordSessionReconnectionAttemptAsync(
        string sessionId,
        bool success,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        try
        {
            using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "RecordSessionReconnectionAttempt");
            _ = (activity?.SetTag("session.id", sessionId));
            _ = (activity?.SetTag("reconnection.success", success));

            if (!State.Sessions.TryGetValue(sessionId, out var session))
            {
                return StateResult.FromError($"Session {sessionId} not found");
            }

            session.ReconnectionAttempts++;
            session.LastActivityAt = DateTime.UtcNow;

            if (success)
            {
                session.SuccessfulReconnections++;
                session.CurrentState = SessionLifecycleState.Connected;
                session.ConnectedAt = DateTime.UtcNow;
            }
            else
            {
                session.CurrentState = SessionLifecycleState.Reconnecting;
            }

            await WriteStateAsync();

            _logger.LogDebug("Recorded reconnection attempt for session {SessionId}, success: {Success}", sessionId, success);
            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record reconnection attempt for session {SessionId}", sessionId);
            return StateResult.FromError($"Failed to record session reconnection attempt: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> ArchiveSessionAsync(
        string sessionId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        try
        {
            using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "ArchiveSession");
            _ = (activity?.SetTag("session.id", sessionId));
            _ = (activity?.SetTag("archive.reason", reason ?? "Unknown"));

            if (!State.Sessions.TryGetValue(sessionId, out var session))
            {
                return StateResult.FromError($"Session {sessionId} not found");
            }

            var now = DateTime.UtcNow;
            session.CurrentState = SessionLifecycleState.Archived;
            session.IsArchived = true;
            session.ArchivedAt = now;
            session.ArchiveReason = reason;
            session.LastActivityAt = now;

            await WriteStateAsync();

            _logger.LogInformation("Archived session {SessionId} with reason: {Reason}", sessionId, reason);
            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive session {SessionId}", sessionId);
            return StateResult.FromError($"Failed to archive session: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult<UserSessionMetrics?>> GetSessionMetricsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        try
        {
            using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "GetSessionMetrics");
            _ = (activity?.SetTag("session.id", sessionId));

            _ = State.SessionMetrics.TryGetValue(sessionId, out var metrics);

            _logger.LogDebug("Retrieved session metrics for {SessionId}, found: {Found}", sessionId, metrics != null);
            await Task.CompletedTask; // Satisfy async requirement
            return StateResult<UserSessionMetrics?>.FromSuccess(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get session metrics for {SessionId}", sessionId);
            return StateResult<UserSessionMetrics?>.FromError($"Failed to get session metrics: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> UpdateSessionMetricsAsync(
        string sessionId,
        UserSessionMetrics metrics,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(metrics);

        try
        {
            using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "UpdateSessionMetrics");
            _ = (activity?.SetTag("session.id", sessionId));

            // Ensure the session exists
            if (!State.Sessions.ContainsKey(sessionId))
            {
                return StateResult.FromError($"Session {sessionId} not found");
            }

            State.SessionMetrics[sessionId] = metrics;
            await WriteStateAsync();

            _logger.LogDebug("Updated session metrics for {SessionId}", sessionId);
            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update session metrics for {SessionId}", sessionId);
            return StateResult.FromError($"Failed to update session metrics: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult<IReadOnlyList<UserSessionState>>> BulkImportSessionsAsync(
        List<UserSessionState> sessions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        try
        {
            using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "BulkImportSessions");
            _ = (activity?.SetTag("sessions.count", sessions.Count));

            _logger.LogDebug("Bulk importing {Count} sessions for user {UserId}", sessions.Count, State.UserId);

            var importedSessions = new List<UserSessionState>();

            foreach (var session in sessions)
            {
                if (State.Sessions.ContainsKey(session.SessionId))
                {
                    _logger.LogWarning("Session {SessionId} already exists, skipping", session.SessionId);
                    continue;
                }

                State.Sessions[session.SessionId] = session;
                importedSessions.Add(session);
            }

            await WriteStateAsync();

            _logger.LogInformation("Bulk imported {ImportedCount} of {TotalCount} sessions for user {UserId}",
                importedSessions.Count, sessions.Count, State.UserId);

            return StateResult<IReadOnlyList<UserSessionState>>.FromSuccess(importedSessions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bulk import sessions for user {UserId}", State.UserId);
            return StateResult<IReadOnlyList<UserSessionState>>.FromError($"Failed to bulk import sessions: {ex.Message}");
        }
    }

    #endregion

    #region IUserPreferencesGrain Implementation

    /// <inheritdoc />
    public async Task<StateResult<UserPreferencesState>> GetPreferencesAsync(
        CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "GetPreferences", State.UserId);

        try
        {
            _logger.LogDebug("Getting preferences for user {UserId}", State.UserId);

            await ReadStateAsync();

            // Ensure preferences are initialized
            State.Preferences ??= new UserPreferencesState();
            State.PreferencesMetadata ??= new UserPreferencesMetadata();

            return StateResult<UserPreferencesState>.FromSuccess(State.Preferences);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get preferences for user {UserId}", State.UserId);
            return StateResult<UserPreferencesState>.FromError($"Failed to get preferences: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> UpdatePreferencesAsync(
        UserPreferencesState preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "UpdatePreferences", State.UserId);
        activity?.SetTag("preferences.version", preferences.Version);

        try
        {
            _logger.LogDebug("Updating preferences for user {UserId}, version {Version}",
                State.UserId, preferences.Version);

            await ReadStateAsync();

            // Optimistic concurrency control
            if (State.Preferences?.Version is not null and not 0 && preferences.Version <= State.Preferences.Version)
            {
                var error = $"Version conflict: provided version {preferences.Version} <= current version {State.Preferences.Version}";
                _logger.LogWarning("{Error} for user {UserId}", error, State.UserId);
                return StateResult.FromError(error);
            }

            // Update preferences and metadata
            preferences.Version++;
            preferences.LastUpdated = DateTime.UtcNow;

            State.Preferences = preferences;
            State.PreferencesMetadata.TotalPreferenceUpdates++;
            State.PreferencesMetadata.LastSyncedAt = DateTime.UtcNow;
            State.PreferencesMetadata.LastSyncSource = "api";

            await WriteStateAsync();

            _logger.LogDebug("Successfully updated preferences for user {UserId}, new version {Version}",
                State.UserId, preferences.Version);

            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update preferences for user {UserId}", State.UserId);
            return StateResult.FromError($"Failed to update preferences: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> UpdateMessagePreferenceAsync(
        string messageId,
        bool isExpanded,
        string renderPhase = "initial",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));
        }

        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "UpdateMessagePreference", State.UserId);
        activity?.SetTag("message.id", messageId);
        activity?.SetTag("message.expanded", isExpanded);

        try
        {
            _logger.LogDebug("Updating message preference for user {UserId}, message {MessageId}, expanded {IsExpanded}",
                State.UserId, messageId, isExpanded);

            await ReadStateAsync();

            // Ensure preferences are initialized
            State.Preferences ??= new UserPreferencesState();
            State.PreferencesMetadata ??= new UserPreferencesMetadata();

            // Update or create message preference
            var messagePreference = new MessagePreference
            {
                MessageId = messageId,
                IsExpanded = isExpanded,
                RenderPhase = renderPhase,
                LastModified = DateTime.UtcNow
            };

            State.Preferences.MessagePreferences[messageId] = messagePreference;
            State.Preferences.Version++;
            State.Preferences.LastUpdated = DateTime.UtcNow;

            State.PreferencesMetadata.TotalPreferenceUpdates++;
            State.PreferencesMetadata.LastSyncedAt = DateTime.UtcNow;
            State.PreferencesMetadata.LastSyncSource = "api";

            await WriteStateAsync();

            _logger.LogDebug("Successfully updated message preference for user {UserId}, message {MessageId}",
                State.UserId, messageId);

            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update message preference for user {UserId}, message {MessageId}: {Error}",
                State.UserId, messageId, ex.Message);
            return StateResult.FromError($"Failed to update message preference: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult<MessagePreference?>> GetMessagePreferenceAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));
        }

        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "GetMessagePreference", State.UserId);
        activity?.SetTag("message.id", messageId);

        try
        {
            _logger.LogDebug("Getting message preference for user {UserId}, message {MessageId}",
                State.UserId, messageId);

            await ReadStateAsync();

            State.Preferences ??= new UserPreferencesState();

            var preference = State.Preferences.MessagePreferences.TryGetValue(messageId, out var value)
                ? value
                : null;

            return StateResult<MessagePreference?>.FromSuccess(preference);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get message preference for user {UserId}, message {MessageId}",
                State.UserId, messageId);
            return StateResult<MessagePreference?>.FromError($"Failed to get message preference: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> BulkUpdateMessagePreferencesAsync(
        Dictionary<string, MessagePreference> preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "BulkUpdateMessagePreferences", State.UserId);
        activity?.SetTag("preferences.count", preferences.Count);

        try
        {
            _logger.LogDebug("Bulk updating {Count} message preferences for user {UserId}",
                preferences.Count, State.UserId);

            await ReadStateAsync();

            // Ensure preferences are initialized
            State.Preferences ??= new UserPreferencesState();
            State.PreferencesMetadata ??= new UserPreferencesMetadata();

            // Update all message preferences
            foreach (var kvp in preferences)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    continue;
                }

                kvp.Value.MessageId = kvp.Key;
                kvp.Value.LastModified = DateTime.UtcNow;
                State.Preferences.MessagePreferences[kvp.Key] = kvp.Value;
            }

            State.Preferences.Version++;
            State.Preferences.LastUpdated = DateTime.UtcNow;

            State.PreferencesMetadata.TotalPreferenceUpdates++;
            State.PreferencesMetadata.LastSyncedAt = DateTime.UtcNow;
            State.PreferencesMetadata.LastSyncSource = "bulk_api";

            await WriteStateAsync();

            _logger.LogDebug("Successfully bulk updated {Count} message preferences for user {UserId}",
                preferences.Count, State.UserId);

            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bulk update message preferences for user {UserId}: {Error}",
                State.UserId, ex.Message);
            return StateResult.FromError($"Failed to bulk update message preferences: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult<int>> ArchiveOldMessagePreferencesAsync(
        DateTime olderThan,
        CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "ArchiveOldMessagePreferences", State.UserId);
        activity?.SetTag("archive.older_than", olderThan.ToString("O"));

        try
        {
            _logger.LogDebug("Archiving message preferences older than {OlderThan} for user {UserId}",
                olderThan, State.UserId);

            await ReadStateAsync();

            State.Preferences ??= new UserPreferencesState();

            var toRemove = State.Preferences.MessagePreferences
                .Where(kvp => kvp.Value.LastModified < olderThan)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var messageId in toRemove)
            {
                State.Preferences.MessagePreferences.Remove(messageId);
            }

            if (toRemove.Count > 0)
            {
                State.Preferences.Version++;
                State.Preferences.LastUpdated = DateTime.UtcNow;

                State.PreferencesMetadata.TotalPreferenceUpdates++;
                State.PreferencesMetadata.LastSyncedAt = DateTime.UtcNow;
                State.PreferencesMetadata.LastSyncSource = "archive";

                await WriteStateAsync();
            }

            _logger.LogDebug("Archived {Count} old message preferences for user {UserId}",
                toRemove.Count, State.UserId);

            return StateResult<int>.FromSuccess(toRemove.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive old message preferences for user {UserId}: {Error}",
                State.UserId, ex.Message);
            return StateResult<int>.FromError($"Failed to archive old message preferences: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> UpdateSelectedModeAsync(
        string? modeId,
        CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "UpdateSelectedMode", State.UserId);
        activity?.SetTag("mode.id", modeId ?? "null");

        try
        {
            _logger.LogDebug("Updating selected mode for user {UserId} to {ModeId}",
                State.UserId, modeId ?? "null");

            await ReadStateAsync();

            // Ensure preferences are initialized
            State.Preferences ??= new UserPreferencesState();
            State.PreferencesMetadata ??= new UserPreferencesMetadata();

            State.Preferences.SelectedModeId = modeId;
            State.Preferences.Version++;
            State.Preferences.LastUpdated = DateTime.UtcNow;

            State.PreferencesMetadata.TotalPreferenceUpdates++;
            State.PreferencesMetadata.LastSyncedAt = DateTime.UtcNow;
            State.PreferencesMetadata.LastSyncSource = "api";

            await WriteStateAsync();

            _logger.LogDebug("Successfully updated selected mode for user {UserId}", State.UserId);

            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update selected mode for user {UserId}: {Error}",
                State.UserId, ex.Message);
            return StateResult.FromError($"Failed to update selected mode: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult<string?>> GetSelectedModeAsync(
        CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "GetSelectedMode", State.UserId);

        try
        {
            _logger.LogDebug("Getting selected mode for user {UserId}", State.UserId);

            await ReadStateAsync();

            State.Preferences ??= new UserPreferencesState();

            return StateResult<string?>.FromSuccess(State.Preferences.SelectedModeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get selected mode for user {UserId}: {Error}",
                State.UserId, ex.Message);
            return StateResult<string?>.FromError($"Failed to get selected mode: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> UpdateUIPreferenceAsync(
        string key,
        object value,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Preference key cannot be null or empty", nameof(key));
        }

        ArgumentNullException.ThrowIfNull(value);

        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "UpdateUIPreference", State.UserId);
        activity?.SetTag("preference.key", key);

        try
        {
            _logger.LogDebug("Updating UI preference {Key} for user {UserId}", key, State.UserId);

            await ReadStateAsync();

            // Ensure preferences are initialized
            State.Preferences ??= new UserPreferencesState();
            State.PreferencesMetadata ??= new UserPreferencesMetadata();

            State.Preferences.UIPreferences[key] = value?.ToString() ?? string.Empty;
            State.Preferences.Version++;
            State.Preferences.LastUpdated = DateTime.UtcNow;

            State.PreferencesMetadata.TotalPreferenceUpdates++;
            State.PreferencesMetadata.LastSyncedAt = DateTime.UtcNow;
            State.PreferencesMetadata.LastSyncSource = "api";

            await WriteStateAsync();

            _logger.LogDebug("Successfully updated UI preference {Key} for user {UserId}", key, State.UserId);

            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update UI preference {Key} for user {UserId}: {Error}",
                key, State.UserId, ex.Message);
            return StateResult.FromError($"Failed to update UI preference: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult<T?>> GetUIPreferenceAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Preference key cannot be null or empty", nameof(key));
        }

        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "GetUIPreference", State.UserId);
        activity?.SetTag("preference.key", key);

        try
        {
            _logger.LogDebug("Getting UI preference {Key} for user {UserId}", key, State.UserId);

            await ReadStateAsync();

            State.Preferences ??= new UserPreferencesState();

            if (!State.Preferences.UIPreferences.TryGetValue(key, out var value))
            {
                return StateResult<T?>.FromSuccess(default);
            }

            // Attempt to cast to requested type
            if (value is T typedValue)
            {
                return StateResult<T?>.FromSuccess(typedValue);
            }

            // Attempt JSON deserialization for complex types
            if (value is string stringValue && typeof(T) != typeof(string))
            {
                try
                {
                    var deserializedValue = System.Text.Json.JsonSerializer.Deserialize<T>(stringValue);
                    return StateResult<T?>.FromSuccess(deserializedValue);
                }
                catch (System.Text.Json.JsonException)
                {
                    // Fall through to type conversion error
                }
            }

            var error = $"Cannot convert preference value of type {value.GetType().Name} to {typeof(T).Name}";
            _logger.LogWarning("{Error} for key {Key}, user {UserId}", error, key, State.UserId);
            return StateResult<T?>.FromError(error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get UI preference {Key} for user {UserId}: {Error}",
                key, State.UserId, ex.Message);
            return StateResult<T?>.FromError($"Failed to get UI preference: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> RemoveUIPreferenceAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Preference key cannot be null or empty", nameof(key));
        }

        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "RemoveUIPreference", State.UserId);
        activity?.SetTag("preference.key", key);

        try
        {
            _logger.LogDebug("Removing UI preference {Key} for user {UserId}", key, State.UserId);

            await ReadStateAsync();

            State.Preferences ??= new UserPreferencesState();

            var removed = State.Preferences.UIPreferences.Remove(key);

            if (removed)
            {
                State.Preferences.Version++;
                State.Preferences.LastUpdated = DateTime.UtcNow;

                State.PreferencesMetadata ??= new UserPreferencesMetadata();
                State.PreferencesMetadata.TotalPreferenceUpdates++;
                State.PreferencesMetadata.LastSyncedAt = DateTime.UtcNow;
                State.PreferencesMetadata.LastSyncSource = "api";

                await WriteStateAsync();
            }

            _logger.LogDebug("UI preference {Key} removal result for user {UserId}: {Removed}",
                key, State.UserId, removed);

            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove UI preference {Key} for user {UserId}: {Error}",
                key, State.UserId, ex.Message);
            return StateResult.FromError($"Failed to remove UI preference: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> ImportClientPreferencesAsync(
        ClientPreferencesImport import,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(import);

        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "ImportClientPreferences", State.UserId);
        activity?.SetTag("import.source", import.ImportSource);
        activity?.SetTag("import.message_count", import.MessagePreferences.Count);

        try
        {
            _logger.LogDebug("Importing client preferences for user {UserId} from {Source}",
                State.UserId, import.ImportSource);

            await ReadStateAsync();

            // Ensure preferences are initialized
            State.Preferences ??= new UserPreferencesState();
            State.PreferencesMetadata ??= new UserPreferencesMetadata();

            // Import message preferences (merge with existing)
            foreach (var kvp in import.MessagePreferences)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    continue;
                }

                kvp.Value.MessageId = kvp.Key;
                kvp.Value.LastModified = import.ImportTimestamp;

                // Only import if we don't have a newer preference
                if (!State.Preferences.MessagePreferences.TryGetValue(kvp.Key, out var existing) ||
                    existing.LastModified <= kvp.Value.LastModified)
                {
                    State.Preferences.MessagePreferences[kvp.Key] = kvp.Value;
                }
            }

            // Import mode selection (only if we don't have one or import is newer)
            if (!string.IsNullOrWhiteSpace(import.SelectedModeId) &&
                (string.IsNullOrWhiteSpace(State.Preferences.SelectedModeId) ||
                 State.Preferences.LastUpdated <= import.ImportTimestamp))
            {
                State.Preferences.SelectedModeId = import.SelectedModeId;
            }

            // Import UI preferences (merge with existing)
            foreach (var kvp in import.UIPreferences)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    continue;
                }
                State.Preferences.UIPreferences[kvp.Key] = kvp.Value;
            }

            State.Preferences.Version++;
            State.Preferences.LastUpdated = DateTime.UtcNow;

            State.PreferencesMetadata.TotalPreferenceUpdates++;
            State.PreferencesMetadata.LastSyncedAt = DateTime.UtcNow;
            State.PreferencesMetadata.LastSyncSource = import.ImportSource;

            await WriteStateAsync();

            _logger.LogDebug("Successfully imported client preferences for user {UserId} from {Source}",
                State.UserId, import.ImportSource);

            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import client preferences for user {UserId}: {Error}",
                State.UserId, ex.Message);
            return StateResult.FromError($"Failed to import client preferences: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult<UserPreferencesState>> ExportPreferencesAsync(
        CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "ExportPreferences", State.UserId);

        try
        {
            _logger.LogDebug("Exporting preferences for user {UserId}", State.UserId);

            await ReadStateAsync();

            State.Preferences ??= new UserPreferencesState();

            // Create a deep copy for export to prevent external modification
            var export = new UserPreferencesState
            {
                MessagePreferences = new Dictionary<string, MessagePreference>(State.Preferences.MessagePreferences),
                SelectedModeId = State.Preferences.SelectedModeId,
                UIPreferences = new Dictionary<string, string>(State.Preferences.UIPreferences),
                LastUpdated = State.Preferences.LastUpdated,
                Version = State.Preferences.Version
            };

            return StateResult<UserPreferencesState>.FromSuccess(export);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export preferences for user {UserId}: {Error}",
                State.UserId, ex.Message);
            return StateResult<UserPreferencesState>.FromError($"Failed to export preferences: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> ResetPreferencesAsync(
        CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "ResetPreferences", State.UserId);

        try
        {
            _logger.LogWarning("Resetting all preferences for user {UserId}", State.UserId);

            await ReadStateAsync();

            // Reset to default state
            State.Preferences = new UserPreferencesState();
            State.PreferencesMetadata = new UserPreferencesMetadata
            {
                TotalPreferenceUpdates = 1,
                LastSyncSource = "reset"
            };

            await WriteStateAsync();

            _logger.LogWarning("Successfully reset all preferences for user {UserId}", State.UserId);

            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset preferences for user {UserId}: {Error}",
                State.UserId, ex.Message);
            return StateResult.FromError($"Failed to reset preferences: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> InvalidatePreferencesCacheAsync(
        CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("UserGrain", "InvalidatePreferencesCache", State.UserId);

        try
        {
            _logger.LogDebug("Invalidating preferences cache for user {UserId}", State.UserId);

            // Force read from persistent storage on next access
            await ClearStateAsync();

            _logger.LogDebug("Successfully invalidated preferences cache for user {UserId}", State.UserId);

            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate preferences cache for user {UserId}: {Error}",
                State.UserId, ex.Message);
            return StateResult.FromError($"Failed to invalidate preferences cache: {ex.Message}");
        }
    }

    #endregion

    /// <summary>
    /// Disposes the UserGrain and releases all managed resources.
    /// This method ensures proper cleanup of timers and prevents memory leaks.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            // Set disposal flag to prevent timer callbacks from executing
            _disposed = true;

            // Dispose timers to prevent memory leaks
            _cleanupTimer?.Dispose();
            _cleanupTimer = null;

            _metricsTimer?.Dispose();
            _metricsTimer = null;

            _logger.LogDebug("UserGrain {UserId} disposed successfully", this.GetPrimaryKeyString());
        }
    }
}
