using System.Diagnostics;
using System.Globalization;
using AIChat.Orleans.Contracts;
using AIChat.Server.Services.StateManagement.Validation;

namespace AIChat.Server.Services.StateManagement.Implementations;

/// <summary>
/// Orleans-based implementation of IUserSessionStateManager.
/// Manages user session state persistence through UserGrain Orleans state.
/// Provides migration capabilities from ConnectionStateTracker to Orleans persistence.
/// </summary>
public class OrleansUserSessionStateManager : OrleansStateManagerBase<UserSessionState>, IUserSessionStateManager
{
    private readonly IStateValidator<UserSessionState> _validator;

    /// <inheritdoc />
    public override string Name => "OrleansUserSessionStateManager";

    /// <summary>
    /// Initializes a new instance of the OrleansUserSessionStateManager class.
    /// </summary>
    /// <param name="grainFactory">The Orleans grain factory</param>
    /// <param name="logger">The logger instance</param>
    /// <param name="cacheManager">The cache manager</param>
    /// <param name="validator">The session state validator</param>
    public OrleansUserSessionStateManager(
        IGrainFactory grainFactory,
        ILogger<OrleansUserSessionStateManager> logger,
        IStateCacheManager<UserSessionState> cacheManager,
        IStateValidator<UserSessionState> validator)
        : base(grainFactory, logger, cacheManager)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    #region IUserSessionStateManager Specialized Methods

    /// <inheritdoc />
    public async Task<StateResult<UserSessionState?>> GetActiveSessionAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.LogDebug("Getting active session for user {UserId}", userId);

            var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
            var userState = await userGrain.GetState();

            if (userState?.Sessions == null)
            {
                return StateResult<UserSessionState?>.FromError(
                    $"Failed to get user state or sessions for user {userId}");
            }

            // Find the most recently active non-archived session
            var activeSession = userState.Sessions.Values
                .Where(s => !s.IsArchived && s.CurrentState != SessionLifecycleState.Archived)
                .OrderByDescending(s => s.LastActivityAt)
                .FirstOrDefault();

            Metrics.RecordRead(stopwatch.ElapsedMilliseconds, success: true);

            return StateResult<UserSessionState?>.FromSuccess(activeSession);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get active session for user {UserId}", userId);
            Metrics.RecordRead(stopwatch.ElapsedMilliseconds, success: false);

            return StateResult<UserSessionState?>.FromError(
                $"Failed to get active session: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult<IReadOnlyList<UserSessionState>>> GetUserSessionsAsync(
        string userId,
        SessionLifecycleState? state = null,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.LogDebug("Getting sessions for user {UserId}, state={State}, includeArchived={IncludeArchived}",
                userId, state, includeArchived);

            var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
            var userState = await userGrain.GetState();

            if (userState?.Sessions == null)
            {
                return StateResult<IReadOnlyList<UserSessionState>>.FromError(
                    $"Failed to get user state or sessions for user {userId}");
            }

            var sessions = userState.Sessions.Values.AsEnumerable();

            // Apply filters
            if (!includeArchived)
            {
                sessions = sessions.Where(s => !s.IsArchived);
            }

            if (state.HasValue)
            {
                sessions = sessions.Where(s => s.CurrentState == state.Value);
            }

            var result = sessions.OrderByDescending(s => s.LastActivityAt).ToList();

            Metrics.RecordRead(stopwatch.ElapsedMilliseconds, success: true);

            return StateResult<IReadOnlyList<UserSessionState>>.FromSuccess(result);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get sessions for user {UserId}", userId);
            Metrics.RecordRead(stopwatch.ElapsedMilliseconds, success: false);

            return StateResult<IReadOnlyList<UserSessionState>>.FromError(
                $"Failed to get user sessions: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult<UserSessionState>> CreateSessionAsync(
        string userId,
        string streamId,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;

            var sessionState = new UserSessionState
            {
                SessionId = sessionId,
                StreamId = streamId,
                CurrentState = SessionLifecycleState.Initialized,
                CreatedAt = now,
                LastActivityAt = now,
                Metadata = metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? string.Empty) ?? []
            };

            // Validate the session state
            var validationResult = await _validator.ValidateAsync(sessionState, cancellationToken);
            if (!validationResult.IsValid)
            {
                return StateResult<UserSessionState>.FromError(
                    $"Session validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            // Store in UserGrain via IUserSessionGrain interface
            var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
            var createResult = await userGrain.CreateSessionAsync(sessionState, cancellationToken);

            if (!createResult.Success)
            {
                Logger.LogError("Failed to create session in UserGrain: {Error}", createResult.Error);
                Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
                return StateResult<UserSessionState>.FromError($"Failed to store session in grain: {createResult.Error}");
            }

            Logger.LogInformation("Created session {SessionId} for user {UserId} with stream {StreamId}",
                sessionId, userId, streamId);

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: true);

            return StateResult<UserSessionState>.FromSuccess(createResult.Data!);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create session for user {UserId} with stream {StreamId}", userId, streamId);
            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);

            return StateResult<UserSessionState>.FromError(
                $"Failed to create session: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> UpdateSessionActivityAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.LogDebug("Updating activity for session {SessionId} of user {UserId}", sessionId, userId);

            var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
            var updateResult = await userGrain.UpdateSessionActivityAsync(sessionId, cancellationToken: cancellationToken);

            if (!updateResult.Success)
            {
                Logger.LogError("Failed to update session activity in UserGrain: {Error}", updateResult.Error);
                Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
                return StateResult.FromError($"Failed to update session activity: {updateResult.Error}");
            }

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: true);

            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update activity for session {SessionId} of user {UserId}", sessionId, userId);
            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);

            return StateResult.FromError($"Failed to update session activity: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> RecordConnectionAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.LogDebug("Recording connection for session {SessionId} of user {UserId}", sessionId, userId);

            var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
            var connectResult = await userGrain.RecordSessionConnectionAsync(sessionId, cancellationToken);

            if (!connectResult.Success)
            {
                Logger.LogError("Failed to record session connection in UserGrain: {Error}", connectResult.Error);
                Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
                return StateResult.FromError($"Failed to record session connection: {connectResult.Error}");
            }

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: true);
            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to record connection for session {SessionId} of user {UserId}", sessionId, userId);
            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
            return StateResult.FromError($"Failed to record session connection: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> RecordDisconnectionAsync(
        string userId,
        string sessionId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.LogDebug("Recording disconnection for session {SessionId} of user {UserId} with reason: {Reason}",
                sessionId, userId, reason);

            var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
            var disconnectResult = await userGrain.RecordSessionDisconnectionAsync(sessionId, reason, cancellationToken);

            if (!disconnectResult.Success)
            {
                Logger.LogError("Failed to record session disconnection in UserGrain: {Error}", disconnectResult.Error);
                Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
                return StateResult.FromError($"Failed to record session disconnection: {disconnectResult.Error}");
            }

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: true);
            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to record disconnection for session {SessionId} of user {UserId}", sessionId, userId);
            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
            return StateResult.FromError($"Failed to record session disconnection: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> RecordReconnectionAttemptAsync(
        string userId,
        string sessionId,
        bool success,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.LogDebug("Recording reconnection attempt for session {SessionId} of user {UserId}, success: {Success}",
                sessionId, userId, success);

            var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
            var reconnectResult = await userGrain.RecordSessionReconnectionAttemptAsync(sessionId, success, cancellationToken);

            if (!reconnectResult.Success)
            {
                Logger.LogError("Failed to record session reconnection attempt in UserGrain: {Error}", reconnectResult.Error);
                Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
                return StateResult.FromError($"Failed to record session reconnection attempt: {reconnectResult.Error}");
            }

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: true);
            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to record reconnection attempt for session {SessionId} of user {UserId}", sessionId, userId);
            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
            return StateResult.FromError($"Failed to record session reconnection attempt: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> ArchiveSessionAsync(
        string userId,
        string sessionId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.LogDebug("Archiving session {SessionId} for user {UserId} with reason: {Reason}",
                sessionId, userId, reason);

            var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
            var archiveResult = await userGrain.ArchiveSessionAsync(sessionId, reason, cancellationToken);

            if (!archiveResult.Success)
            {
                Logger.LogError("Failed to archive session in UserGrain: {Error}", archiveResult.Error);
                Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
                return StateResult.FromError($"Failed to archive session: {archiveResult.Error}");
            }

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: true);
            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to archive session {SessionId} for user {UserId}", sessionId, userId);
            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
            return StateResult.FromError($"Failed to archive session: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult<UserSessionMetrics?>> GetSessionMetricsAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.LogDebug("Getting session metrics for session {SessionId} of user {UserId}", sessionId, userId);

            var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
            var metricsResult = await userGrain.GetSessionMetricsAsync(sessionId, cancellationToken);

            if (!metricsResult.Success)
            {
                Logger.LogError("Failed to get session metrics from UserGrain: {Error}", metricsResult.Error);
                Metrics.RecordRead(stopwatch.ElapsedMilliseconds, success: false);
                return StateResult<UserSessionMetrics?>.FromError($"Failed to get session metrics: {metricsResult.Error}");
            }

            Metrics.RecordRead(stopwatch.ElapsedMilliseconds, success: true);
            return StateResult<UserSessionMetrics?>.FromSuccess(metricsResult.Data);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get session metrics for session {SessionId} of user {UserId}", sessionId, userId);
            Metrics.RecordRead(stopwatch.ElapsedMilliseconds, success: false);
            return StateResult<UserSessionMetrics?>.FromError($"Failed to get session metrics: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult> UpdateSessionMetricsAsync(
        string userId,
        string sessionId,
        UserSessionMetrics metrics,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(metrics);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.LogDebug("Updating session metrics for session {SessionId} of user {UserId}", sessionId, userId);

            var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
            var updateResult = await userGrain.UpdateSessionMetricsAsync(sessionId, metrics, cancellationToken);

            if (!updateResult.Success)
            {
                Logger.LogError("Failed to update session metrics in UserGrain: {Error}", updateResult.Error);
                Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
                return StateResult.FromError($"Failed to update session metrics: {updateResult.Error}");
            }

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: true);
            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update session metrics for session {SessionId} of user {UserId}", sessionId, userId);
            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
            return StateResult.FromError($"Failed to update session metrics: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<StateResult<UserSessionState>> MigrateConnectionStateAsync(
        string userId,
        string streamId,
        object connectionStateData,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        ArgumentNullException.ThrowIfNull(connectionStateData);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.LogDebug("Migrating connection state data for user {UserId}, stream {StreamId}", userId, streamId);

            // Cast connectionStateData to expected type
            // Note: This assumes the caller passes the actual ConnectionStateData object
            // In a real implementation, you might need reflection or a known interface
            var connectionData = connectionStateData as dynamic;
            if (connectionData == null)
            {
                return StateResult<UserSessionState>.FromError("Invalid connection state data format");
            }

            // Map ConnectionStatus to SessionLifecycleState
            var sessionState = MapConnectionStatusToSessionState(connectionData.Status);

            // Create session state from connection data
            var sessionId = Guid.NewGuid().ToString();
            var migratedSession = new UserSessionState
            {
                SessionId = sessionId,
                StreamId = streamId,
                CurrentState = sessionState,
                CreatedAt = connectionData.ConnectedAt ?? DateTime.UtcNow,
                LastActivityAt = connectionData.LastActivityAt,
                ConnectedAt = connectionData.ConnectedAt,
                DisconnectedAt = connectionData.DisconnectedAt,
                ReconnectionAttempts = connectionData.ReconnectionAttempts ?? 0,
                SuccessfulReconnections = connectionData.SuccessfulReconnections ?? 0,
                TotalConnectionTime = connectionData.TotalConnectionTime ?? TimeSpan.Zero,
                TotalDisconnectionTime = connectionData.TotalDisconnectionTime ?? TimeSpan.Zero,
                TotalReconnectionTime = connectionData.TotalReconnectionTime ?? TimeSpan.Zero,
                LongestConnectionDuration = connectionData.LongestConnectionDuration ?? TimeSpan.Zero,
                LastDisconnectionReason = connectionData.LastDisconnectionReason,
                DisconnectionCount = connectionData.DisconnectionCount ?? 0,
                Metadata = new Dictionary<string, string>
                {
                    ["userId"] = userId,
                    ["migratedFrom"] = "ConnectionStateTracker",
                    ["migrationTimestamp"] = DateTime.UtcNow.ToString(CultureInfo.InvariantCulture),
                    ["originalStreamId"] = streamId
                }
            };

            // Validate the migrated session
            var validationResult = await _validator.ValidateAsync(migratedSession, cancellationToken);
            if (!validationResult.IsValid)
            {
                Logger.LogError("Migrated session validation failed: {Errors}",
                    string.Join(", ", validationResult.Errors));
                return StateResult<UserSessionState>.FromError(
                    $"Migration validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            // Store the migrated session in UserGrain
            var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
            var createResult = await userGrain.CreateSessionAsync(migratedSession, cancellationToken);

            if (!createResult.Success)
            {
                Logger.LogError("Failed to store migrated session in UserGrain: {Error}", createResult.Error);
                Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
                return StateResult<UserSessionState>.FromError($"Failed to store migrated session: {createResult.Error}");
            }

            // Create and store session metrics if connection data includes timing information
            if (connectionData.ReconnectionTimes != null)
            {
                var sessionMetrics = new UserSessionMetrics
                {
                    SessionId = sessionId,
                    ReconnectionTimes = new List<TimeSpan>(connectionData.ReconnectionTimes),
                    RecentFailureCount = connectionData.RecentFailureCount ?? 0,
                    ConnectionStartTime = connectionData.ConnectionStartTime ?? DateTime.UtcNow,
                    DisconnectionStartTime = connectionData.DisconnectionStartTime,
                    UptimePercentage = CalculateUptimePercentage(migratedSession),
                    LastCalculatedAt = DateTime.UtcNow
                };

                var metricsResult = await userGrain.UpdateSessionMetricsAsync(sessionId, sessionMetrics, cancellationToken);
                if (!metricsResult.Success)
                {
                    Logger.LogWarning("Failed to store session metrics during migration: {Error}", metricsResult.Error);
                    // Don't fail the migration if metrics storage fails
                }
            }

            Logger.LogInformation("Successfully migrated session data for user {UserId}, stream {StreamId}, session {SessionId}",
                userId, streamId, sessionId);

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: true);
            return StateResult<UserSessionState>.FromSuccess(createResult.Data!);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to migrate connection state for user {UserId}, stream {StreamId}", userId, streamId);
            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
            return StateResult<UserSessionState>.FromError($"Migration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Maps ConnectionStatus to SessionLifecycleState.
    /// </summary>
    private static SessionLifecycleState MapConnectionStatusToSessionState(dynamic status)
    {
        // Convert the status value to string for comparison
        var statusString = status?.ToString();
        return statusString switch
        {
            "Connected" => SessionLifecycleState.Connected,
            "Disconnected" => SessionLifecycleState.Disconnected,
            "Reconnecting" => SessionLifecycleState.Reconnecting,
            "Failed" => SessionLifecycleState.Error,
            "Unstable" => SessionLifecycleState.Reconnecting, // Map unstable to reconnecting
            "Unknown" => SessionLifecycleState.Initialized,   // Map unknown to initialized
            _ => SessionLifecycleState.Initialized
        };
    }

    /// <summary>
    /// Calculates uptime percentage for a session.
    /// </summary>
    private static double CalculateUptimePercentage(UserSessionState session)
    {
        var totalTime = session.TotalConnectionTime + session.TotalDisconnectionTime;
        if (totalTime == TimeSpan.Zero)
        {
            return 100.0;
        }

        return (session.TotalConnectionTime.TotalMilliseconds / totalTime.TotalMilliseconds) * 100.0;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Updates the state of a session.
    /// </summary>
    private async Task<StateResult> UpdateSessionStateAsync(
        string userId,
        string sessionId,
        SessionLifecycleState newState,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.LogDebug("Updating session {SessionId} state to {NewState} for user {UserId}",
                sessionId, newState, userId);

            var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
            StateResult updateResult;

            // Use specific grain methods based on the new state
            switch (newState)
            {
                case SessionLifecycleState.Connected:
                    var connectResult = await userGrain.RecordSessionConnectionAsync(sessionId, cancellationToken);
                    updateResult = MapToServerResult(connectResult);
                    break;
                case SessionLifecycleState.Disconnected:
                    var disconnectResult = await userGrain.RecordSessionDisconnectionAsync(sessionId, cancellationToken: cancellationToken);
                    updateResult = MapToServerResult(disconnectResult);
                    break;
                case SessionLifecycleState.Archived:
                    var archiveResult = await userGrain.ArchiveSessionAsync(sessionId, cancellationToken: cancellationToken);
                    updateResult = MapToServerResult(archiveResult);
                    break;
                default:
                    // For other states (Initialized, Reconnecting, Error), use the general state update method
                    var stateUpdateResult = await userGrain.UpdateSessionStateAsync(sessionId, newState, cancellationToken);
                    updateResult = MapToServerResult(stateUpdateResult);
                    break;
            }

            if (!updateResult.Success)
            {
                Logger.LogError("Failed to update session state in UserGrain: {Error}", updateResult.Error);
                Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);
                return StateResult.FromError($"Failed to update session state: {updateResult.Error}");
            }

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: true);

            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update session {SessionId} state for user {UserId}", sessionId, userId);
            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, success: false);

            return StateResult.FromError($"Failed to update session state: {ex.Message}");
        }
    }

    #endregion

    #region OrleansStateManagerBase Abstract Implementation

    /// <inheritdoc />
    protected override async Task<StateResult<UserSessionState>> GetFromGrainAsync(
        string id,
        CancellationToken cancellationToken)
    {
        // Session IDs are structured as "userId:sessionId"
        var parts = id.Split(':', 2);
        if (parts.Length != 2)
        {
            return StateResult<UserSessionState>.FromError($"Invalid session ID format: {id}");
        }

        var userId = parts[0];
        var sessionId = parts[1];

        var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
        var userState = await userGrain.GetState();

        if (userState?.Sessions == null)
        {
            return StateResult<UserSessionState>.FromError($"Failed to get user state or sessions for user {userId}");
        }

        if (userState.Sessions.TryGetValue(sessionId, out var session))
        {
            return StateResult<UserSessionState>.FromSuccess(session);
        }

        return StateResult<UserSessionState>.FromError($"Session {sessionId} not found for user {userId}");
    }

    /// <inheritdoc />
    protected override async Task<StateResult<IReadOnlyList<UserSessionState>>> GetMultipleFromGrainsAsync(
        IList<string> ids,
        CancellationToken cancellationToken)
    {
        // Group by user ID for efficient grain access
        var userGroups = ids
            .Select(id => id.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0])
            .ToList();

        var results = new List<UserSessionState>();

        foreach (var userGroup in userGroups)
        {
            var userId = userGroup.Key;
            var sessionIds = userGroup.Select(parts => parts[1]).ToList();

            var userGrain = GrainFactory.GetGrain<IUserGrain>(userId);
            var userState = await userGrain.GetState();

            if (userState?.Sessions != null)
            {
                foreach (var sessionId in sessionIds)
                {
                    if (userState.Sessions.TryGetValue(sessionId, out var session))
                    {
                        results.Add(session);
                    }
                }
            }
        }

        return StateResult<IReadOnlyList<UserSessionState>>.FromSuccess(results);
    }

    /// <inheritdoc />
    protected override async Task<StateResult<PagedResult<UserSessionState>>> GetPagedFromGrainsAsync(
        StateQuery query,
        CancellationToken cancellationToken)
    {
        // For session paging, we need to specify a user ID in the query filters
        if (query.Filters?.ContainsKey("userId") != true)
        {
            return StateResult<PagedResult<UserSessionState>>.FromError("User ID filter is required for paged session queries");
        }

        var userId = query.Filters!["userId"]?.ToString();
        if (string.IsNullOrEmpty(userId))
        {
            return StateResult<PagedResult<UserSessionState>>.FromError("Invalid user ID provided");
        }

        var userSessions = await GetUserSessionsAsync(userId, cancellationToken: cancellationToken);

        if (!userSessions.Success)
        {
            return StateResult<PagedResult<UserSessionState>>.FromError(userSessions.Error ?? "Failed to retrieve user sessions");
        }

        var sessions = userSessions.Data ?? [];
        var skip = (query.Page - 1) * query.PageSize;
        var pagedSessions = sessions.Skip(skip).Take(query.PageSize).ToList();

        var pagedResult = new PagedResult<UserSessionState>
        {
            Items = pagedSessions,
            TotalCount = sessions.Count,
            Page = query.Page,
            PageSize = query.PageSize
        };

        return StateResult<PagedResult<UserSessionState>>.FromSuccess(pagedResult);
    }

    /// <inheritdoc />
    protected override async Task<StateResult<bool>> ExistsInGrainAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var getResult = await GetFromGrainAsync(id, cancellationToken);
        return StateResult<bool>.FromSuccess(getResult.Success);
    }

    /// <inheritdoc />
    protected override async Task<StateResult<long>> CountInGrainsAsync(
        StateQuery? query,
        CancellationToken cancellationToken)
    {
        if (query?.Filters?.ContainsKey("userId") != true)
        {
            return StateResult<long>.FromError("User ID filter is required for session count queries");
        }

        var userId = query.Filters["userId"]?.ToString();
        if (string.IsNullOrEmpty(userId))
        {
            return StateResult<long>.FromError("Invalid user ID provided");
        }

        var userSessions = await GetUserSessionsAsync(userId, cancellationToken: cancellationToken);

        if (!userSessions.Success)
        {
            return StateResult<long>.FromError(userSessions.Error ?? "Failed to retrieve user sessions for count");
        }

        return StateResult<long>.FromSuccess(userSessions.Data?.Count ?? 0);
    }

    /// <inheritdoc />
    protected override async Task<StateResult<UserSessionState>> CreateInGrainAsync(
        UserSessionState entity,
        CancellationToken cancellationToken)
    {
        // Extract user ID from session metadata or require it to be set
        if (!entity.Metadata.TryGetValue("userId", out var userIdObj) || userIdObj is not string userId)
        {
            return StateResult<UserSessionState>.FromError("User ID is required in session metadata for creation");
        }

        return await CreateSessionAsync(userId, entity.StreamId, entity.Metadata?.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value), cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task<StateResult<UserSessionState>> UpdateInGrainAsync(
        string id,
        UserSessionState entity,
        CancellationToken cancellationToken)
    {
        var parts = id.Split(':', 2);
        if (parts.Length != 2)
        {
            return StateResult<UserSessionState>.FromError($"Invalid session ID format: {id}");
        }

        var userId = parts[0];
        var sessionId = parts[1];

        // TODO: Implement full session update in UserGrain
        // For now, just update activity
        var updateResult = await UpdateSessionActivityAsync(userId, sessionId, cancellationToken);

        if (updateResult.Success)
        {
            return StateResult<UserSessionState>.FromSuccess(entity);
        }

        return StateResult<UserSessionState>.FromError(updateResult.Error ?? "Failed to update user session");
    }

    /// <inheritdoc />
    protected override async Task<StateResult<UserSessionState>> PatchInGrainAsync(
        string id,
        Dictionary<string, object> updates,
        CancellationToken cancellationToken)
    {
        // TODO: Implement partial update logic
        await Task.CompletedTask;
        return StateResult<UserSessionState>.FromError("Patch operations not yet implemented for sessions");
    }

    /// <inheritdoc />
    protected override async Task<StateResult> DeleteInGrainAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var parts = id.Split(':', 2);
        if (parts.Length != 2)
        {
            return StateResult.FromError($"Invalid session ID format: {id}");
        }

        var userId = parts[0];
        var sessionId = parts[1];

        // Archive instead of delete
        return await ArchiveSessionAsync(userId, sessionId, "Deleted via state manager", cancellationToken);
    }

    /// <inheritdoc />
    protected override string? GetEntityId(UserSessionState entity)
    {
        // Return composite ID: "userId:sessionId"
        if (entity.Metadata.TryGetValue("userId", out var userIdObj) && userIdObj is string userId)
        {
            return $"{userId}:{entity.SessionId}";
        }

        return null;
    }

    #endregion

    #region Mapping Helper Methods

    /// <summary>
    /// Maps an Orleans StateResult to a Server StateResult.
    /// </summary>
    /// <param name="orleansResult">The Orleans StateResult to map</param>
    /// <returns>The corresponding Server StateResult</returns>
    private static StateResult MapToServerResult(AIChat.Orleans.Models.StateResult orleansResult)
    {
        return orleansResult.Success
            ? StateResult.FromSuccess()
            : StateResult.FromError(orleansResult.Error ?? "Unknown error");
    }

    /// <summary>
    /// Maps an Orleans StateResult{T} to a Server StateResult{T}.
    /// </summary>
    /// <typeparam name="T">The data type</typeparam>
    /// <param name="orleansResult">The Orleans StateResult{T} to map</param>
    /// <returns>The corresponding Server StateResult{T}</returns>
    private static StateResult<T> MapToServerResult<T>(AIChat.Orleans.Models.StateResult<T> orleansResult)
    {
        return orleansResult.Success
            ? StateResult<T>.FromSuccess(orleansResult.Data!)
            : StateResult<T>.FromError(orleansResult.Error ?? "Unknown error");
    }

    #endregion
}