using System.Text.Json;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;

namespace AIChat.Orleans.Grains;

/// <summary>
/// User grain implementation providing user-centric operations.
/// Maintains user state, connections, and handles message routing.
/// </summary>
public sealed class UserGrain : Grain<UserGrainState>, IUserGrain
{
    private readonly ILogger<UserGrain> _logger;
    private IDisposable? _cleanupTimer;
    private IDisposable? _metricsTimer;

    /// <summary>
    /// Initializes a new instance of the UserGrain.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics</param>
    public UserGrain(ILogger<UserGrain> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        // Setup periodic cleanup timer (every 5 minutes)
        _cleanupTimer = this.RegisterGrainTimer(
            (_) => CleanupStateAsync(null),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));

        // Setup metrics timer (every minute)
        _metricsTimer = this.RegisterGrainTimer(
            (_) => UpdateMetricsAsync(null),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));

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

        // Cleanup timers
        _cleanupTimer?.Dispose();
        _metricsTimer?.Dispose();

        // Save final state
        try
        {
            await WriteStateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state during deactivation for {UserId}", State.UserId);
        }

        await base.OnDeactivateAsync(reason, cancellationToken);
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

            // Maintain circular buffer of max 100 activities
            while (State.RecentActivity.Count > 100)
            {
                State.RecentActivity.Dequeue();
            }

            State.LastActivity = DateTime.UtcNow;
            State.Metrics.TotalActivities++;

            // Save state periodically (every 10 activities)
            if (State.Metrics.TotalActivities % 10 == 0)
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
            var staleConnections = State.Connections.Values
                .Where(c => DateTime.UtcNow - c.LastActivity > TimeSpan.FromMinutes(30))
                .Count();

            if (staleConnections > 0)
            {
                warnings.Add($"{staleConnections} stale connections detected");
            }

            // Check memory pressure from activities
            if (State.RecentActivity.Count > 80)
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

    #region Phase 2: Active Connection Management (Stubbed for Phase 1)

    /// <inheritdoc />
    public Task RegisterConnection(string connectionId, string clientId)
    {
        _logger.LogDebug("RegisterConnection called in Phase 1 (stubbed) for {UserId}: {ConnectionId}", 
            State.UserId, connectionId);
        
        // Phase 2 implementation will go here
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnregisterConnection(string connectionId)
    {
        _logger.LogDebug("UnregisterConnection called in Phase 1 (stubbed) for {UserId}: {ConnectionId}", 
            State.UserId, connectionId);
        
        // Phase 2 implementation will go here
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SubscribeToChat(string connectionId, string chatId)
    {
        _logger.LogDebug("SubscribeToChat called in Phase 1 (stubbed) for {UserId}: {ConnectionId} -> {ChatId}", 
            State.UserId, connectionId, chatId);
        
        // Phase 2 implementation will go here
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnsubscribeFromChat(string connectionId, string chatId)
    {
        _logger.LogDebug("UnsubscribeFromChat called in Phase 1 (stubbed) for {UserId}: {ConnectionId} -> {ChatId}", 
            State.UserId, connectionId, chatId);
        
        // Phase 2 implementation will go here
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RelayMessage(ChatMessage message)
    {
        _logger.LogDebug("RelayMessage called in Phase 1 (stubbed) for {UserId}: {MessageId}", 
            State.UserId, message.Id);
        
        // Phase 2 implementation will go here
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RelayStreamChunk(StreamChunk chunk)
    {
        _logger.LogDebug("RelayStreamChunk called in Phase 1 (stubbed) for {UserId}: {OperationId}", 
            State.UserId, chunk.OperationId);
        
        // Phase 2 implementation will go here
        return Task.CompletedTask;
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
    /// Periodic cleanup of old data to prevent memory leaks.
    /// </summary>
    private async Task CleanupStateAsync(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var cleanupThreshold = TimeSpan.FromHours(1);
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
                           && now - kvp.Value.CompletedAt.Value > TimeSpan.FromMinutes(30))
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
    /// </summary>
    private async Task UpdateMetricsAsync(object? state)
    {
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