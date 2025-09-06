using System.Text.Json;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Logging;

namespace AIChat.Orleans.Services;

/// <summary>
/// Service interface for managing SignalR connections and chat subscriptions.
/// Encapsulates business logic for Phase 2 connection management operations.
/// Follows the Single Responsibility Principle by focusing solely on connection lifecycle.
/// </summary>
public interface IConnectionManagementService
{
    /// <summary>
    /// Registers a new SignalR connection for a user.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="clientId">Client tab/browser identifier</param>
    /// <returns>Updated state and activity record</returns>
    Task<(UserGrainState UpdatedState, ActivityRecord ActivityRecord)> RegisterConnectionAsync(
        UserGrainState state,
        string connectionId,
        string clientId);

    /// <summary>
    /// Unregisters a SignalR connection and cleans up associated subscriptions.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <returns>Updated state, activity record, and list of affected chat IDs</returns>
    Task<(UserGrainState UpdatedState, ActivityRecord ActivityRecord, List<string> AffectedChatIds)> UnregisterConnectionAsync(
        UserGrainState state,
        string connectionId);

    /// <summary>
    /// Subscribes a connection to a specific chat room.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="chatId">Chat room identifier</param>
    /// <returns>Updated state and activity record</returns>
    Task<(UserGrainState UpdatedState, ActivityRecord ActivityRecord)> SubscribeToChatAsync(
        UserGrainState state,
        string connectionId,
        string chatId);

    /// <summary>
    /// Unsubscribes a connection from a specific chat room.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="chatId">Chat room identifier</param>
    /// <returns>Updated state and activity record</returns>
    Task<(UserGrainState UpdatedState, ActivityRecord ActivityRecord)> UnsubscribeFromChatAsync(
        UserGrainState state,
        string connectionId,
        string chatId);

    /// <summary>
    /// Gets all active connections for a specific chat room.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="chatId">Chat room identifier</param>
    /// <param name="staleThresholdMinutes">Threshold for considering connections stale</param>
    /// <returns>List of active connections</returns>
    Task<List<ConnectionInfo>> GetActiveConnectionsForChatAsync(
        UserGrainState state,
        string chatId,
        int staleThresholdMinutes);

    /// <summary>
    /// Gets all active connections for the user.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="staleThresholdMinutes">Threshold for considering connections stale</param>
    /// <returns>List of active connections</returns>
    Task<List<ConnectionInfo>> GetAllActiveConnectionsAsync(
        UserGrainState state,
        int staleThresholdMinutes);

    /// <summary>
    /// Validates connection parameters.
    /// </summary>
    /// <param name="connectionId">Connection identifier</param>
    /// <param name="clientId">Client identifier (optional)</param>
    /// <param name="chatId">Chat identifier (optional)</param>
    /// <returns>Validation result</returns>
    Task<(bool IsValid, string[] Errors, string[] Warnings)> ValidateConnectionParametersAsync(
        string connectionId,
        string? clientId = null,
        string? chatId = null);

    /// <summary>
    /// Performs connection recovery by transferring state from old to new connection.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="oldConnectionId">Old connection identifier</param>
    /// <param name="newConnectionId">New connection identifier</param>
    /// <param name="clientId">Client identifier</param>
    /// <param name="gracePeriodMinutes">Grace period for recovery</param>
    /// <returns>Updated state, success flag, and activity record</returns>
    Task<(UserGrainState UpdatedState, bool RecoverySuccessful, ActivityRecord ActivityRecord)> RecoverConnectionAsync(
        UserGrainState state,
        string oldConnectionId,
        string newConnectionId,
        string clientId,
        int gracePeriodMinutes);
}

/// <summary>
/// Default implementation of the connection management service.
/// </summary>
public class ConnectionManagementService : IConnectionManagementService
{
    private readonly Microsoft.Extensions.Logging.ILogger<ConnectionManagementService> _logger;

    /// <summary>
    /// Initializes a new instance of the ConnectionManagementService.
    /// </summary>
    /// <param name="logger">Logger for service operations</param>
    public ConnectionManagementService(Microsoft.Extensions.Logging.ILogger<ConnectionManagementService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<(UserGrainState UpdatedState, ActivityRecord ActivityRecord)> RegisterConnectionAsync(
        UserGrainState state,
        string connectionId,
        string clientId)
    {
        try
        {
            _logger.LogInformation(
                "Registering connection {ConnectionId} for user {UserId} from client {ClientId}",
                connectionId, state.UserId, clientId);

            var now = DateTime.UtcNow;
            var isUpdate = state.Connections.ContainsKey(connectionId);

            if (isUpdate)
            {
                // Update existing connection
                var existingConnection = state.Connections[connectionId];
                existingConnection.ClientId = clientId;
                existingConnection.LastActivity = now;

                _logger.LogWarning(
                    "Updated existing connection {ConnectionId} for user {UserId}",
                    connectionId, state.UserId);
            }
            else
            {
                // Create new connection
                var connectionInfo = new ConnectionInfo
                {
                    ConnectionId = connectionId,
                    ClientId = clientId,
                    ConnectedAt = now,
                    LastActivity = now,
                    SubscribedChatIds = new HashSet<string>()
                };

                state.Connections[connectionId] = connectionInfo;

                _logger.LogInformation(
                    "Created new connection {ConnectionId} for user {UserId}",
                    connectionId, state.UserId);
            }

            // Update metrics
            state.Metrics.ActiveConnections = state.Connections.Count;
            state.LastActivity = now;

            // Create activity record
            var activityRecord = new ActivityRecord
            {
                Type = ActivityType.Connected,
                Metadata = JsonSerializer.Serialize(new
                {
                    ConnectionId = connectionId,
                    ClientId = clientId,
                    IsUpdate = isUpdate
                }),
                Timestamp = now,
                CorrelationId = Guid.NewGuid().ToString()
            };

            return Task.FromResult((state, activityRecord));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to register connection {ConnectionId} for user {UserId}",
                connectionId, state.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<(UserGrainState UpdatedState, ActivityRecord ActivityRecord, List<string> AffectedChatIds)> UnregisterConnectionAsync(
        UserGrainState state,
        string connectionId)
    {
        try
        {
            _logger.LogInformation(
                "Unregistering connection {ConnectionId} for user {UserId}",
                connectionId, state.UserId);

            if (!state.Connections.TryGetValue(connectionId, out var connectionInfo))
            {
                _logger.LogWarning(
                    "Connection {ConnectionId} not found for user {UserId}",
                    connectionId, state.UserId);

                var warningActivity = new ActivityRecord
                {
                    Type = ActivityType.Disconnected,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        ConnectionId = connectionId,
                        Status = "NotFound"
                    }),
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = Guid.NewGuid().ToString()
                };

                return Task.FromResult((state, warningActivity, new List<string>()));
            }

            var now = DateTime.UtcNow;
            var affectedChatIds = connectionInfo.SubscribedChatIds.ToList();

            // Remove connection from all subscribed chats
            foreach (var chatId in affectedChatIds)
            {
                if (state.ActiveChats.TryGetValue(chatId, out var subscription))
                {
                    subscription.ConnectionCount--;

                    if (subscription.ConnectionCount <= 0)
                    {
                        state.ActiveChats.Remove(chatId);
                        _logger.LogDebug(
                            "Removed chat subscription {ChatId} for user {UserId} (no remaining connections)",
                            chatId, state.UserId);
                    }
                    else
                    {
                        // Check if any other connections are still subscribed
                        var hasOtherSubscribers = state.Connections.Values
                            .Any(c => c.ConnectionId != connectionId && c.SubscribedChatIds.Contains(chatId));

                        if (!hasOtherSubscribers)
                        {
                            subscription.State = SubscriptionState.Inactive;
                        }
                    }
                }
            }

            // Remove the connection
            state.Connections.Remove(connectionId);

            // Update metrics
            state.Metrics.ActiveConnections = state.Connections.Count;
            state.LastActivity = now;

            // Create activity record
            var activityRecord = new ActivityRecord
            {
                Type = ActivityType.Disconnected,
                Metadata = JsonSerializer.Serialize(new
                {
                    ConnectionId = connectionId,
                    ClientId = connectionInfo.ClientId,
                    SubscribedChats = affectedChatIds,
                    ConnectedDuration = (now - connectionInfo.ConnectedAt).TotalMinutes
                }),
                Timestamp = now,
                CorrelationId = Guid.NewGuid().ToString()
            };

            _logger.LogInformation(
                "Unregistered connection {ConnectionId} for user {UserId}. Removed from {ChatCount} chats",
                connectionId, state.UserId, affectedChatIds.Count);

            return Task.FromResult((state, activityRecord, affectedChatIds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to unregister connection {ConnectionId} for user {UserId}",
                connectionId, state.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<(UserGrainState UpdatedState, ActivityRecord ActivityRecord)> SubscribeToChatAsync(
        UserGrainState state,
        string connectionId,
        string chatId)
    {
        try
        {
            _logger.LogInformation(
                "Subscribing connection {ConnectionId} to chat {ChatId} for user {UserId}",
                connectionId, chatId, state.UserId);

            if (!state.Connections.TryGetValue(connectionId, out var connectionInfo))
            {
                throw new InvalidOperationException($"Connection {connectionId} not found for user {state.UserId}");
            }

            var now = DateTime.UtcNow;
            var wasAlreadySubscribed = !connectionInfo.SubscribedChatIds.Add(chatId);

            if (wasAlreadySubscribed)
            {
                _logger.LogDebug(
                    "Connection {ConnectionId} already subscribed to chat {ChatId} for user {UserId}",
                    connectionId, chatId, state.UserId);
            }
            else
            {
                // Update or create chat subscription
                if (state.ActiveChats.TryGetValue(chatId, out var subscription))
                {
                    subscription.ConnectionCount++;
                    subscription.State = SubscriptionState.Active;
                }
                else
                {
                    subscription = new ChatSubscription
                    {
                        ChatId = chatId,
                        SubscribedAt = now,
                        State = SubscriptionState.Active,
                        ConnectionCount = 1
                    };
                    state.ActiveChats[chatId] = subscription;
                }

                _logger.LogInformation(
                    "Subscribed connection {ConnectionId} to chat {ChatId} for user {UserId}. Total connections: {ConnectionCount}",
                    connectionId, chatId, state.UserId, subscription.ConnectionCount);
            }

            // Update activity timestamps
            connectionInfo.LastActivity = now;
            state.LastActivity = now;

            // Create activity record
            var activityRecord = new ActivityRecord
            {
                Type = ActivityType.ChatSubscribed,
                Metadata = JsonSerializer.Serialize(new
                {
                    ConnectionId = connectionId,
                    ChatId = chatId,
                    WasAlreadySubscribed = wasAlreadySubscribed,
                    TotalConnectionsInChat = state.ActiveChats.TryGetValue(chatId, out var sub) ? sub.ConnectionCount : 0
                }),
                Timestamp = now,
                CorrelationId = Guid.NewGuid().ToString()
            };

            return Task.FromResult((state, activityRecord));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to subscribe connection {ConnectionId} to chat {ChatId} for user {UserId}",
                connectionId, chatId, state.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<(UserGrainState UpdatedState, ActivityRecord ActivityRecord)> UnsubscribeFromChatAsync(
        UserGrainState state,
        string connectionId,
        string chatId)
    {
        try
        {
            _logger.LogInformation(
                "Unsubscribing connection {ConnectionId} from chat {ChatId} for user {UserId}",
                connectionId, chatId, state.UserId);

            if (!state.Connections.TryGetValue(connectionId, out var connectionInfo))
            {
                _logger.LogWarning(
                    "Connection {ConnectionId} not found for user {UserId}",
                    connectionId, state.UserId);

                var warningActivity = new ActivityRecord
                {
                    Type = ActivityType.ChatUnsubscribed,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        ConnectionId = connectionId,
                        ChatId = chatId,
                        Status = "ConnectionNotFound"
                    }),
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = Guid.NewGuid().ToString()
                };

                return Task.FromResult((state, warningActivity));
            }

            var now = DateTime.UtcNow;
            var wasSubscribed = connectionInfo.SubscribedChatIds.Remove(chatId);

            if (!wasSubscribed)
            {
                _logger.LogDebug(
                    "Connection {ConnectionId} was not subscribed to chat {ChatId} for user {UserId}",
                    connectionId, chatId, state.UserId);
            }
            else
            {
                // Update chat subscription
                if (state.ActiveChats.TryGetValue(chatId, out var subscription))
                {
                    subscription.ConnectionCount--;

                    if (subscription.ConnectionCount <= 0)
                    {
                        state.ActiveChats.Remove(chatId);
                        _logger.LogDebug(
                            "Removed chat subscription {ChatId} for user {UserId} (no remaining connections)",
                            chatId, state.UserId);
                    }
                    else
                    {
                        // Check if subscription should be marked inactive
                        var hasActiveSubscribers = state.Connections.Values
                            .Any(c => c.SubscribedChatIds.Contains(chatId));

                        if (!hasActiveSubscribers)
                        {
                            subscription.State = SubscriptionState.Inactive;
                        }
                    }
                }
            }

            // Update activity timestamps
            connectionInfo.LastActivity = now;
            state.LastActivity = now;

            // Create activity record
            var activityRecord = new ActivityRecord
            {
                Type = ActivityType.ChatUnsubscribed,
                Metadata = JsonSerializer.Serialize(new
                {
                    ConnectionId = connectionId,
                    ChatId = chatId,
                    WasSubscribed = wasSubscribed,
                    RemainingConnectionsInChat = state.ActiveChats.TryGetValue(chatId, out var sub) ? sub.ConnectionCount : 0
                }),
                Timestamp = now,
                CorrelationId = Guid.NewGuid().ToString()
            };

            return Task.FromResult((state, activityRecord));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to unsubscribe connection {ConnectionId} from chat {ChatId} for user {UserId}",
                connectionId, chatId, state.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<List<ConnectionInfo>> GetActiveConnectionsForChatAsync(
        UserGrainState state,
        string chatId,
        int staleThresholdMinutes)
    {
        if (string.IsNullOrEmpty(chatId))
        {
            return Task.FromResult(new List<ConnectionInfo>());
        }

        var staleThreshold = DateTime.UtcNow.AddMinutes(-staleThresholdMinutes);

        return Task.FromResult(state.Connections.Values
            .Where(c => c.SubscribedChatIds.Contains(chatId) && c.LastActivity > staleThreshold)
            .ToList());
    }

    /// <inheritdoc />
    public Task<List<ConnectionInfo>> GetAllActiveConnectionsAsync(
        UserGrainState state,
        int staleThresholdMinutes)
    {
        var staleThreshold = DateTime.UtcNow.AddMinutes(-staleThresholdMinutes);

        return Task.FromResult(state.Connections.Values
            .Where(c => c.LastActivity > staleThreshold)
            .ToList());
    }

    /// <inheritdoc />
    public Task<(bool IsValid, string[] Errors, string[] Warnings)> ValidateConnectionParametersAsync(
        string connectionId,
        string? clientId = null,
        string? chatId = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            // Validate connection ID
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                errors.Add("Connection ID cannot be null or empty");
            }

            // Validate client ID if provided
            if (clientId != null && string.IsNullOrWhiteSpace(clientId))
            {
                errors.Add("Client ID cannot be empty if provided");
            }

            // Validate chat ID if provided
            if (chatId != null && string.IsNullOrWhiteSpace(chatId))
            {
                errors.Add("Chat ID cannot be empty if provided");
            }

            return Task.FromResult((errors.Count == 0, errors.ToArray(), warnings.ToArray()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate connection parameters");
            return Task.FromResult((false, new[] { $"Validation failed: {ex.Message}" }, Array.Empty<string>()));
        }
    }

    /// <inheritdoc />
    public Task<(UserGrainState UpdatedState, bool RecoverySuccessful, ActivityRecord ActivityRecord)> RecoverConnectionAsync(
        UserGrainState state,
        string oldConnectionId,
        string newConnectionId,
        string clientId,
        int gracePeriodMinutes)
    {
        try
        {
            _logger.LogInformation(
                "Attempting to recover connection {OldConnectionId} to {NewConnectionId} for user {UserId}",
                oldConnectionId, newConnectionId, state.UserId);

            if (!state.Connections.TryGetValue(oldConnectionId, out var oldConnection))
            {
                var failureActivity = new ActivityRecord
                {
                    Type = ActivityType.Connected,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        Event = "ConnectionRecoveryFailed",
                        Reason = "OldConnectionNotFound",
                        OldConnectionId = oldConnectionId,
                        NewConnectionId = newConnectionId,
                        ClientId = clientId
                    }),
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = Guid.NewGuid().ToString()
                };

                return Task.FromResult((state, false, failureActivity));
            }

            // Check grace period
            var gracePeriod = TimeSpan.FromMinutes(gracePeriodMinutes);
            if (DateTime.UtcNow - oldConnection.LastActivity > gracePeriod)
            {
                var expiredActivity = new ActivityRecord
                {
                    Type = ActivityType.Connected,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        Event = "ConnectionRecoveryFailed",
                        Reason = "GracePeriodExpired",
                        OldConnectionId = oldConnectionId,
                        NewConnectionId = newConnectionId,
                        ClientId = clientId,
                        GracePeriodMinutes = gracePeriodMinutes
                    }),
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = Guid.NewGuid().ToString()
                };

                return Task.FromResult((state, false, expiredActivity));
            }

            // Perform recovery
            var now = DateTime.UtcNow;
            var newConnection = new ConnectionInfo
            {
                ConnectionId = newConnectionId,
                ClientId = clientId,
                ConnectedAt = now,
                LastActivity = now,
                SubscribedChatIds = new HashSet<string>(oldConnection.SubscribedChatIds)
            };

            // Remove old connection and add new one
            state.Connections.Remove(oldConnectionId);
            state.Connections[newConnectionId] = newConnection;

            // Update metrics (connection count should remain the same)
            state.LastActivity = now;

            var successActivity = new ActivityRecord
            {
                Type = ActivityType.Connected,
                Metadata = JsonSerializer.Serialize(new
                {
                    Event = "ConnectionRecovered",
                    OldConnectionId = oldConnectionId,
                    NewConnectionId = newConnectionId,
                    ClientId = clientId,
                    RecoveredSubscriptions = oldConnection.SubscribedChatIds.Count
                }),
                Timestamp = now,
                CorrelationId = Guid.NewGuid().ToString()
            };

            _logger.LogInformation(
                "Successfully recovered connection {OldConnectionId} to {NewConnectionId} for user {UserId}",
                oldConnectionId, newConnectionId, state.UserId);

            return Task.FromResult((state, true, successActivity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to recover connection {OldConnectionId} to {NewConnectionId} for user {UserId}",
                oldConnectionId, newConnectionId, state.UserId);

            var errorActivity = new ActivityRecord
            {
                Type = ActivityType.ErrorOccurred,
                Metadata = JsonSerializer.Serialize(new
                {
                    Event = "ConnectionRecoveryFailed",
                    Reason = "Exception",
                    Error = ex.Message,
                    OldConnectionId = oldConnectionId,
                    NewConnectionId = newConnectionId,
                    ClientId = clientId
                }),
                Timestamp = DateTime.UtcNow,
                CorrelationId = Guid.NewGuid().ToString()
            };

            return Task.FromResult((state, false, errorActivity));
        }
    }
}