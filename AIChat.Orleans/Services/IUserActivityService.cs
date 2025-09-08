using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Logging;

namespace AIChat.Orleans.Services;

/// <summary>
/// Service interface for managing user activity tracking and monitoring.
/// Encapsulates business logic for Phase 1 shadow mode operations.
/// Follows the Single Responsibility Principle by focusing solely on activity management.
/// </summary>
public interface IUserActivityService
{
    /// <summary>
    /// Records a user activity with proper validation and circular buffer management.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="type">Type of activity performed</param>
    /// <param name="metadata">JSON metadata about the activity</param>
    /// <param name="maxBufferSize">Maximum size of the activity buffer</param>
    /// <param name="persistenceInterval">Interval for state persistence</param>
    /// <returns>Updated state and whether persistence is needed</returns>
    Task<(UserGrainState UpdatedState, bool ShouldPersist)> RecordActivityAsync(
        UserGrainState state,
        ActivityType type,
        string metadata,
        int maxBufferSize,
        int persistenceInterval);

    /// <summary>
    /// Performs a comprehensive health check on the user grain state.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="staleConnectionThresholdMinutes">Threshold for stale connections</param>
    /// <param name="activityBufferWarningThreshold">Warning threshold for activity buffer</param>
    /// <param name="staleOperationThresholdMinutes">Threshold for stale operations</param>
    /// <returns>Health check result</returns>
    Task<HealthCheckResult> PerformHealthCheckAsync(
        UserGrainState state,
        int staleConnectionThresholdMinutes,
        double activityBufferWarningThreshold,
        int staleOperationThresholdMinutes);

    /// <summary>
    /// Cleans up old activity records based on retention policy.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="retentionHours">Hours to retain activity records</param>
    /// <param name="operationRetentionMinutes">Minutes to retain completed operations</param>
    /// <returns>Updated state and number of items cleaned up</returns>
    Task<(UserGrainState UpdatedState, int ActivitiesRemoved, int OperationsRemoved)> CleanupOldDataAsync(
        UserGrainState state,
        int retentionHours,
        int operationRetentionMinutes);

    /// <summary>
    /// Updates activity-related metrics in the grain state.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <returns>Updated state with current metrics</returns>
    Task<UserGrainState> UpdateActivityMetricsAsync(UserGrainState state);

    /// <summary>
    /// Validates activity data before recording.
    /// </summary>
    /// <param name="type">Activity type</param>
    /// <param name="metadata">Activity metadata</param>
    /// <returns>Validation result</returns>
    Task<(bool IsValid, string[] Errors, string[] Warnings)> ValidateActivityAsync(ActivityType type, string metadata);
}

/// <summary>
/// Default implementation of the user activity service.
/// </summary>
public class UserActivityService : IUserActivityService
{
    private readonly ILogger<UserActivityService> _logger;

    /// <summary>
    /// Initializes a new instance of the UserActivityService.
    /// </summary>
    /// <param name="logger">Logger for service operations</param>
    public UserActivityService(ILogger<UserActivityService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<(UserGrainState UpdatedState, bool ShouldPersist)> RecordActivityAsync(
        UserGrainState state,
        ActivityType type,
        string metadata,
        int maxBufferSize,
        int persistenceInterval)
    {
        try
        {
            _logger.LogDebug(
                "Recording activity {ActivityType} for user {UserId}",
                type, state.UserId);

            var activity = new ActivityRecord
            {
                Type = type,
                Metadata = metadata ?? string.Empty,
                Timestamp = DateTime.UtcNow,
                CorrelationId = Guid.NewGuid().ToString()
            };

            // Add to circular buffer
            state.RecentActivity.Enqueue(activity);

            // Maintain buffer size limit
            while (state.RecentActivity.Count > maxBufferSize)
            {
                var removed = state.RecentActivity.Dequeue();
                _logger.LogTrace(
                    "Removed old activity {ActivityType} from buffer for user {UserId}",
                    removed.Type, state.UserId);
            }

            // Update timestamps and metrics
            state.LastActivity = DateTime.UtcNow;
            state.Metrics.TotalActivities++;

            // Determine if persistence is needed
            var shouldPersist = state.Metrics.TotalActivities % persistenceInterval == 0;

            _logger.LogDebug(
                "Activity recorded for user {UserId}. Total activities: {TotalActivities}, Buffer size: {BufferSize}, Should persist: {ShouldPersist}",
                state.UserId, state.Metrics.TotalActivities, state.RecentActivity.Count, shouldPersist);

            return Task.FromResult((state, shouldPersist));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to record activity {ActivityType} for user {UserId}",
                type, state.UserId);

            // Return original state and no persistence on error
            return Task.FromResult((state, false));
        }
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> PerformHealthCheckAsync(
        UserGrainState state,
        int staleConnectionThresholdMinutes,
        double activityBufferWarningThreshold,
        int staleOperationThresholdMinutes)
    {
        try
        {
            _logger.LogDebug("Performing health check for user {UserId}", state.UserId);

            var warnings = new List<string>();
            var now = DateTime.UtcNow;

            // Check for stale connections
            var staleThreshold = now.AddMinutes(-staleConnectionThresholdMinutes);
            var staleConnections = state.Connections.Values
                .Count(c => c.LastActivity <= staleThreshold);

            if (staleConnections > 0)
            {
                warnings.Add($"{staleConnections} stale connections detected");
            }

            // Check activity buffer usage
            var maxBufferSize = Math.Max(state.RecentActivity.Count, 100); // Assume default of 100
            var warningThreshold = (int)(maxBufferSize * activityBufferWarningThreshold);
            if (state.RecentActivity.Count > warningThreshold)
            {
                warnings.Add($"Activity buffer usage high: {state.RecentActivity.Count}/{maxBufferSize}");
            }

            // Check for stale operations
            var operationStaleThreshold = now.AddMinutes(-staleOperationThresholdMinutes);
            var staleOperations = state.ActiveOperations.Values
                .Count(op => op.StartedAt <= operationStaleThreshold
                           && op.Status == OperationStatus.InProgress);

            if (staleOperations > 0)
            {
                warnings.Add($"{staleOperations} stale operations detected");
            }

            // Check overall grain health
            var isHealthy = warnings.Count == 0;

            var result = new HealthCheckResult
            {
                IsHealthy = isHealthy,
                GrainId = state.UserId,
                LastActivity = state.LastActivity,
                Metrics = state.Metrics,
                CheckedAt = now,
                AdditionalInfo = $"Connections: {state.Connections.Count}, " +
                               $"Chats: {state.ActiveChats.Count}, " +
                               $"Operations: {state.ActiveOperations.Count}, " +
                               $"Activities: {state.RecentActivity.Count}",
                Warnings = warnings
            };

            _logger.LogDebug(
                "Health check completed for user {UserId}. Healthy: {IsHealthy}, Warnings: {WarningCount}",
                state.UserId, isHealthy, warnings.Count);

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for user {UserId}", state.UserId);

            return Task.FromResult(new HealthCheckResult
            {
                IsHealthy = false,
                GrainId = state.UserId,
                CheckedAt = DateTime.UtcNow,
                AdditionalInfo = $"Health check exception: {ex.Message}",
                Warnings = ["Health check threw exception"]
            });
        }
    }

    /// <inheritdoc />
    public Task<(UserGrainState UpdatedState, int ActivitiesRemoved, int OperationsRemoved)> CleanupOldDataAsync(
        UserGrainState state,
        int retentionHours,
        int operationRetentionMinutes)
    {
        try
        {
            _logger.LogDebug("Cleaning up old data for user {UserId}", state.UserId);

            var now = DateTime.UtcNow;
            var activityThreshold = now.AddHours(-retentionHours);
            var operationThreshold = now.AddMinutes(-operationRetentionMinutes);

            // Clean up old activities
            var oldActivitiesCount = state.RecentActivity.Count;
            var newActivityQueue = new Queue<ActivityRecord>();

            while (state.RecentActivity.Count > 0)
            {
                var activity = state.RecentActivity.Dequeue();
                if (activity.Timestamp > activityThreshold)
                {
                    newActivityQueue.Enqueue(activity);
                }
            }

            state.RecentActivity = newActivityQueue;
            var activitiesRemoved = oldActivitiesCount - newActivityQueue.Count;

            // Clean up completed operations
            var completedOps = state.ActiveOperations
                .Where(kvp => kvp.Value.Status == OperationStatus.Completed
                           && kvp.Value.CompletedAt.HasValue
                           && kvp.Value.CompletedAt.Value <= operationThreshold)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var opId in completedOps)
            {
                _ = state.ActiveOperations.Remove(opId);
            }

            _logger.LogDebug(
                "Cleanup completed for user {UserId}. Activities removed: {ActivitiesRemoved}, Operations removed: {OperationsRemoved}",
                state.UserId, activitiesRemoved, completedOps.Count);

            return Task.FromResult((state, activitiesRemoved, completedOps.Count));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old data for user {UserId}", state.UserId);
            return Task.FromResult((state, 0, 0));
        }
    }

    /// <inheritdoc />
    public Task<UserGrainState> UpdateActivityMetricsAsync(UserGrainState state)
    {
        try
        {
            _logger.LogTrace("Updating activity metrics for user {UserId}", state.UserId);

            // Update connection count
            state.Metrics.ActiveConnections = state.Connections.Count;

            // Update active operations count
            state.Metrics.ActiveOperationsCount = state.ActiveOperations
                .Count(kvp => kvp.Value.Status is OperationStatus.Queued or OperationStatus.InProgress);

            return Task.FromResult(state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update activity metrics for user {UserId}", state.UserId);
            return Task.FromResult(state);
        }
    }

    /// <inheritdoc />
    public Task<(bool IsValid, string[] Errors, string[] Warnings)> ValidateActivityAsync(ActivityType type, string metadata)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            // Validate activity type
            if (!Enum.IsDefined(type))
            {
                errors.Add($"Invalid activity type: {type}");
            }

            // Validate metadata
            if (string.IsNullOrEmpty(metadata))
            {
                warnings.Add("Activity metadata is empty");
            }
            else
            {
                // Check metadata size
                if (metadata.Length > 10000) // 10KB limit
                {
                    errors.Add("Activity metadata exceeds maximum size limit");
                }

                // Try to validate as JSON if it looks like JSON
                if (metadata.TrimStart().StartsWith("{") || metadata.TrimStart().StartsWith("["))
                {
                    try
                    {
                        _ = System.Text.Json.JsonSerializer.Deserialize<object>(metadata);
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        warnings.Add("Activity metadata appears to be invalid JSON");
                    }
                }
            }

            return Task.FromResult((errors.Count == 0, errors.ToArray(), warnings.ToArray()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate activity {ActivityType}", type);
            return Task.FromResult((false, new[] { $"Validation failed: {ex.Message}" }, Array.Empty<string>()));
        }
    }
}
