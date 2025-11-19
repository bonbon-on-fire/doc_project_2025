using System.Collections.Concurrent;
using System.Diagnostics;
using AIChat.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AIChat.Server.Services.Recovery.Implementations;

/// <summary>
/// Implementation of recovery notification service that broadcasts real-time recovery progress
/// notifications via SignalR. Integrates with existing ChatHub infrastructure.
/// </summary>
public class RecoveryNotificationService : IRecoveryNotificationService
{
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<RecoveryNotificationService> _logger;

    /// <summary>
    /// Metrics tracking
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _activeConnections = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _groupMemberships = new();
    private long _totalNotificationsSent;
    private long _failedDeliveries;
    private readonly List<TimeSpan> _deliveryTimes = [];
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.RecoveryNotificationService");

    /// <summary>
    /// Initializes a new instance of the RecoveryNotificationService.
    /// </summary>
    /// <param name="hubContext">SignalR hub context for ChatHub</param>
    /// <param name="logger">Logger for diagnostics</param>
    public RecoveryNotificationService(
        IHubContext<ChatHub> hubContext,
        ILogger<RecoveryNotificationService> logger)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task NotifyRecoveryStartedAsync(
        string connectionId,
        string operationId,
        PointInTimeRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(request);

        using var activity = ActivitySource.StartActivity("NotifyRecoveryStarted");
        _ = (activity?.SetTag("operationId", operationId));
        _ = (activity?.SetTag("grainId", request.GrainId));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var notification = RecoveryNotification.Started(operationId, request.GrainId, request);

            await _hubContext.Clients.Client(connectionId)
                .SendAsync("RecoveryStarted", notification, cancellationToken);

            _ = Interlocked.Increment(ref _totalNotificationsSent);
            stopwatch.Stop();
            _deliveryTimes.Add(stopwatch.Elapsed);

            _logger.LogInformation(
                "Notified connection {ConnectionId} about recovery start for operation {OperationId}",
                connectionId, operationId);
        }
        catch (Exception ex)
        {
            _ = Interlocked.Increment(ref _failedDeliveries);
            _logger.LogError(ex,
                "Failed to notify connection {ConnectionId} about recovery start for operation {OperationId}",
                connectionId, operationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task NotifyRecoveryProgressAsync(
        string connectionId,
        string operationId,
        int progressPercentage,
        string currentStatus,
        RecoveryProgressDetails? details = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentOutOfRangeException.ThrowIfLessThan(progressPercentage, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(progressPercentage, 100);

        using var activity = ActivitySource.StartActivity("NotifyRecoveryProgress");
        _ = (activity?.SetTag("operationId", operationId));
        _ = (activity?.SetTag("progressPercentage", progressPercentage));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var notification = new
            {
                OperationId = operationId,
                ProgressPercentage = progressPercentage,
                CurrentStatus = currentStatus,
                Details = details,
                Timestamp = DateTimeOffset.UtcNow
            };

            await _hubContext.Clients.Client(connectionId)
                .SendAsync("RecoveryProgress", notification, cancellationToken);

            _ = Interlocked.Increment(ref _totalNotificationsSent);
            stopwatch.Stop();
            _deliveryTimes.Add(stopwatch.Elapsed);

            _logger.LogDebug(
                "Notified connection {ConnectionId} about recovery progress {Progress}% for operation {OperationId}",
                connectionId, progressPercentage, operationId);
        }
        catch (Exception ex)
        {
            _ = Interlocked.Increment(ref _failedDeliveries);
            _logger.LogError(ex,
                "Failed to notify connection {ConnectionId} about recovery progress for operation {OperationId}",
                connectionId, operationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task NotifyRecoveryCompletedAsync(
        string connectionId,
        string operationId,
        RecoveryStatus status,
        RecoveryCompletionResult? result = null,
        RecoveryErrorInfo? errorInfo = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(operationId);

        using var activity = ActivitySource.StartActivity("NotifyRecoveryCompleted");
        _ = (activity?.SetTag("operationId", operationId));
        _ = (activity?.SetTag("status", status.ToString()));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var notification = new
            {
                OperationId = operationId,
                Status = status.ToString(),
                Result = result,
                Error = errorInfo,
                Timestamp = DateTimeOffset.UtcNow
            };

            await _hubContext.Clients.Client(connectionId)
                .SendAsync("RecoveryCompleted", notification, cancellationToken);

            _ = Interlocked.Increment(ref _totalNotificationsSent);
            stopwatch.Stop();
            _deliveryTimes.Add(stopwatch.Elapsed);

            _logger.LogInformation(
                "Notified connection {ConnectionId} about recovery completion for operation {OperationId}. Status: {Status}",
                connectionId, operationId, status);
        }
        catch (Exception ex)
        {
            _ = Interlocked.Increment(ref _failedDeliveries);
            _logger.LogError(ex,
                "Failed to notify connection {ConnectionId} about recovery completion for operation {OperationId}",
                connectionId, operationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task BroadcastRecoveryToGroupAsync(
        string groupName,
        RecoveryNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupName);
        ArgumentNullException.ThrowIfNull(notification);

        using var activity = ActivitySource.StartActivity("BroadcastRecoveryToGroup");
        _ = (activity?.SetTag("groupName", groupName));
        _ = (activity?.SetTag("operationId", notification.OperationId));
        _ = (activity?.SetTag("notificationType", notification.Type.ToString()));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var eventName = notification.Type switch
            {
                RecoveryNotificationType.Started => "RecoveryStarted",
                RecoveryNotificationType.Progress => "RecoveryProgress",
                RecoveryNotificationType.Completed => "RecoveryCompleted",
                RecoveryNotificationType.Cancelled => "RecoveryCancelled",
                _ => "RecoveryNotification"
            };

            await _hubContext.Clients.Group(groupName)
                .SendAsync(eventName, notification, cancellationToken);

            // Estimate number of connections in group for metrics
            var groupSize = _groupMemberships.TryGetValue(groupName, out var members) ? members.Count : 1;
            _ = Interlocked.Add(ref _totalNotificationsSent, groupSize);

            stopwatch.Stop();
            _deliveryTimes.Add(stopwatch.Elapsed);

            _logger.LogInformation(
                "Broadcasted recovery notification to group {GroupName} for operation {OperationId}. Type: {Type}",
                groupName, notification.OperationId, notification.Type);
        }
        catch (Exception ex)
        {
            _ = Interlocked.Increment(ref _failedDeliveries);
            _logger.LogError(ex,
                "Failed to broadcast recovery notification to group {GroupName} for operation {OperationId}",
                groupName, notification.OperationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task JoinRecoveryGroupAsync(
        string connectionId,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(groupName);

        using var activity = ActivitySource.StartActivity("JoinRecoveryGroup");
        _ = (activity?.SetTag("connectionId", connectionId));
        _ = (activity?.SetTag("groupName", groupName));

        try
        {
            await _hubContext.Groups.AddToGroupAsync(connectionId, groupName, cancellationToken);

            // Track group membership for metrics
            _ = _groupMemberships.AddOrUpdate(
                groupName,
                _ => [connectionId],
                (_, members) =>
                {
                    members.Add(connectionId);
                    return members;
                });

            // Track active connection
            _activeConnections[connectionId] = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Connection {ConnectionId} joined recovery group {GroupName}",
                connectionId, groupName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to add connection {ConnectionId} to recovery group {GroupName}",
                connectionId, groupName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task LeaveRecoveryGroupAsync(
        string connectionId,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(groupName);

        using var activity = ActivitySource.StartActivity("LeaveRecoveryGroup");
        _ = (activity?.SetTag("connectionId", connectionId));
        _ = (activity?.SetTag("groupName", groupName));

        try
        {
            await _hubContext.Groups.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);

            // Update group membership tracking
            if (_groupMemberships.TryGetValue(groupName, out var members))
            {
                _ = members.Remove(connectionId);
                if (members.Count == 0)
                {
                    _ = _groupMemberships.TryRemove(groupName, out _);
                }
            }

            _logger.LogInformation(
                "Connection {ConnectionId} left recovery group {GroupName}",
                connectionId, groupName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to remove connection {ConnectionId} from recovery group {GroupName}",
                connectionId, groupName);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<RecoveryNotificationHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var activeConnections = _activeConnections.Count;
            var activeGroups = _groupMemberships.Count;

            // Clean up old connections (older than 1 hour)
            var cutoffTime = DateTimeOffset.UtcNow.AddHours(-1);
            var expiredConnections = _activeConnections
                .Where(kvp => kvp.Value < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var connectionId in expiredConnections)
            {
                _ = _activeConnections.TryRemove(connectionId, out _);
            }

            var healthStatus = RecoveryNotificationHealthStatus.Healthy(
                "RecoveryNotificationService",
                activeConnections,
                activeGroups);

            return Task.FromResult(healthStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recovery notification service health status");

            var unhealthyStatus = RecoveryNotificationHealthStatus.Unhealthy(
                "RecoveryNotificationService",
                [$"Health check failed: {ex.Message}"]);

            return Task.FromResult(unhealthyStatus);
        }
    }

    /// <inheritdoc />
    public Task<RecoveryNotificationMetrics> GetNotificationMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var totalSent = Interlocked.Read(ref _totalNotificationsSent);
            var failedCount = Interlocked.Read(ref _failedDeliveries);

            // Calculate recent notifications (simplified - in production would use a sliding window)
            var recentCount = Math.Min(totalSent, 100); // Approximate recent count

            // Calculate average delivery time
            var averageDeliveryTime = _deliveryTimes.Count > 0
                ? TimeSpan.FromMilliseconds(_deliveryTimes.Average(t => t.TotalMilliseconds))
                : TimeSpan.Zero;

            // Clean up old delivery times to prevent memory growth
            if (_deliveryTimes.Count > 1000)
            {
                _deliveryTimes.RemoveRange(0, _deliveryTimes.Count - 500);
            }

            var currentConnections = _activeConnections.Count;
            var peakConnections = currentConnections; // Simplified - in production would track actual peak

            var typeDistribution = new Dictionary<RecoveryNotificationType, long>
            {
                [RecoveryNotificationType.Started] = totalSent / 4,  // Rough estimates
                [RecoveryNotificationType.Progress] = totalSent / 2,
                [RecoveryNotificationType.Completed] = totalSent / 4,
                [RecoveryNotificationType.Cancelled] = totalSent / 20
            };

            var metrics = new RecoveryNotificationMetrics
            {
                TotalNotificationsSent = totalSent,
                RecentNotificationsSent = recentCount,
                AverageDeliveryTime = averageDeliveryTime,
                FailedDeliveries = failedCount,
                NotificationTypeDistribution = typeDistribution,
                CurrentActiveConnections = currentConnections,
                PeakConnections = peakConnections
            };

            return Task.FromResult(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recovery notification metrics");

            return Task.FromResult(RecoveryNotificationMetrics.Empty());
        }
    }

    /// <summary>
    /// Handles connection disconnect cleanup.
    /// This method should be called when a SignalR connection is disconnected.
    /// </summary>
    /// <param name="connectionId">The disconnected connection ID</param>
    public void HandleConnectionDisconnected(string connectionId)
    {
        _ = _activeConnections.TryRemove(connectionId, out _);

        // Remove from all groups
        foreach (var groupMembership in _groupMemberships.ToList())
        {
            if (groupMembership.Value.Remove(connectionId) && groupMembership.Value.Count == 0)
            {
                _ = _groupMemberships.TryRemove(groupMembership.Key, out _);
            }
        }

        _logger.LogDebug("Cleaned up recovery notification tracking for disconnected connection {ConnectionId}", connectionId);
    }
}
