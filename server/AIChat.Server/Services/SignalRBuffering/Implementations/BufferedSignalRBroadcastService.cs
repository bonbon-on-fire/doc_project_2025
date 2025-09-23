using System.Diagnostics;
using AIChat.Orleans.Services;
using AIChat.Server.Services.SignalRBuffering.Abstractions;
using AIChat.Server.Services.SignalRBuffering.Models;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services.SignalRBuffering.Implementations;

/// <summary>
/// Decorator implementation of ISignalRBroadcastService that adds message buffering capabilities.
/// This service transparently intercepts SignalR broadcast calls and routes them through the
/// message buffer for improved reliability, overflow handling, and delivery confirmation.
///
/// Features:
/// - Transparent buffering for all SignalR broadcast operations
/// - Automatic fallback to direct broadcast when buffering is disabled or unhealthy
/// - Comprehensive metrics collection and health monitoring
/// - Integration with Orleans grain broadcasting
/// - Zero breaking changes to existing SignalR contracts
///
/// The decorator pattern ensures that existing code continues to work unchanged while
/// gaining the benefits of message buffering and improved reliability.
/// </summary>
public sealed class BufferedSignalRBroadcastService : ISignalRBroadcastService, IDisposable
{
    private readonly ISignalRBroadcastService _innerService;
    private readonly ISignalRMessageBuffer _messageBuffer;
    private readonly SignalRBufferConfiguration _config;
    private readonly ILogger<BufferedSignalRBroadcastService> _logger;
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.BufferedSignalRBroadcastService");

    // Performance and health tracking
    private long _totalBroadcastRequests;
    private long _totalBufferedRequests;
    private long _totalDirectRequests;
    private long _totalFailedRequests;
    private DateTime _lastHealthCheck = DateTime.UtcNow;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the BufferedSignalRBroadcastService.
    /// </summary>
    /// <param name="innerService">The underlying SignalR broadcast service to decorate</param>
    /// <param name="messageBuffer">The message buffer for queuing broadcast operations</param>
    /// <param name="config">Configuration for buffer behavior</param>
    /// <param name="logger">Logger instance</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
    public BufferedSignalRBroadcastService(
        ISignalRBroadcastService innerService,
        ISignalRMessageBuffer messageBuffer,
        IOptions<SignalRBufferConfiguration> config,
        ILogger<BufferedSignalRBroadcastService> logger)
    {
        _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
        _messageBuffer = messageBuffer ?? throw new ArgumentNullException(nameof(messageBuffer));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to buffer events for monitoring (excluding MessageDelivered to avoid circular dependency)
        _messageBuffer.MessageFailed += OnMessageFailed;
        _messageBuffer.BufferOverflow += OnBufferOverflow;
        _messageBuffer.HealthStatusChanged += OnHealthStatusChanged;

        _logger.LogInformation(
            "BufferedSignalRBroadcastService initialized with buffer capacity {MaxCapacity}, " +
            "buffering enabled: {BufferingEnabled}, inner service: {InnerServiceType}",
            _messageBuffer.GetMaxCapacity(), _config.Enabled, _innerService.GetType().Name);
    }

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            ThrowIfDisposed();

            try
            {
                // Service is available if either buffering is working or direct service is available
                return (_config.Enabled && IsBufferHealthy()) || _innerService.IsAvailable;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking service availability");
                return _innerService.IsAvailable; // Fallback to inner service
            }
        }
    }

    /// <inheritdoc />
    public async Task BroadcastToGroupAsync(string groupName, string methodName, object payload)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(groupName);
        ArgumentNullException.ThrowIfNull(methodName);
        ArgumentNullException.ThrowIfNull(payload);

        Interlocked.Increment(ref _totalBroadcastRequests);

        using var activity = ActivitySource.StartActivity("SignalRBroadcast");
        var stopwatch = Stopwatch.StartNew();
        activity?.SetTag("group.name", groupName);
        activity?.SetTag("method.name", methodName);

        try
        {
            // Determine whether to use buffering or direct broadcast
            var shouldUseBuffer = ShouldUseBuffering();

            if (shouldUseBuffer)
            {
                await BroadcastViaBufferAsync(groupName, methodName, payload);
                Interlocked.Increment(ref _totalBufferedRequests);
                activity?.SetTag("delivery.method", "buffered");
            }
            else
            {
                await BroadcastDirectlyAsync(groupName, methodName, payload);
                Interlocked.Increment(ref _totalDirectRequests);
                activity?.SetTag("delivery.method", "direct");
            }

            activity?.SetTag("broadcast.success", true);

            _logger.LogDebug(
                "Broadcast completed successfully: Group={GroupName}, Method={MethodName}, " +
                "DeliveryMethod={DeliveryMethod}",
                groupName, methodName, shouldUseBuffer ? "Buffered" : "Direct");
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalFailedRequests);
            activity?.SetTag("broadcast.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);

            _logger.LogError(ex,
                "Failed to broadcast message: Group={GroupName}, Method={MethodName}",
                groupName, methodName);

            // Attempt fallback to direct broadcast if buffering failed
            if (_config.Enabled)
            {
                try
                {
                    _logger.LogInformation(
                        "Attempting fallback to direct broadcast: Group={GroupName}, Method={MethodName}",
                        groupName, methodName);

                    await BroadcastDirectlyAsync(groupName, methodName, payload);
                    Interlocked.Increment(ref _totalDirectRequests);

                    _logger.LogInformation(
                        "Fallback broadcast succeeded: Group={GroupName}, Method={MethodName}",
                        groupName, methodName);

                    return;
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx,
                        "Fallback broadcast also failed: Group={GroupName}, Method={MethodName}",
                        groupName, methodName);
                }
            }

            throw; // Re-throw original exception if fallback also fails
        }
    }

    /// <summary>
    /// Gets comprehensive metrics about the buffered broadcast service performance.
    /// Includes both buffering and direct broadcast statistics.
    /// </summary>
    /// <returns>Service performance metrics</returns>
    public async Task<BufferedBroadcastMetrics> GetMetricsAsync()
    {
        ThrowIfDisposed();

        try
        {
            var bufferMetrics = await _messageBuffer.GetMetricsAsync();
            var bufferHealth = await _messageBuffer.GetHealthAsync();

            var totalRequests = Interlocked.Read(ref _totalBroadcastRequests);
            var bufferedRequests = Interlocked.Read(ref _totalBufferedRequests);
            var directRequests = Interlocked.Read(ref _totalDirectRequests);
            var failedRequests = Interlocked.Read(ref _totalFailedRequests);

            return new BufferedBroadcastMetrics
            {
                Timestamp = DateTime.UtcNow,
                TotalBroadcastRequests = totalRequests,
                BufferedRequests = bufferedRequests,
                DirectRequests = directRequests,
                FailedRequests = failedRequests,
                BufferingSuccessRate = totalRequests > 0 ? (double)bufferedRequests / totalRequests * 100.0 : 0.0,
                OverallSuccessRate = totalRequests > 0 ? (double)(totalRequests - failedRequests) / totalRequests * 100.0 : 100.0,
                BufferUtilization = bufferMetrics.BufferUtilizationPercent,
                BufferHealth = bufferHealth.Status,
                IsBufferingEnabled = _config.Enabled,
                InnerServiceAvailable = _innerService.IsAvailable,
                ServiceAvailable = IsAvailable
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving buffered broadcast metrics");
            throw;
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Determines whether to use buffering for the current broadcast operation.
    /// Takes into account configuration, buffer health, and system status.
    /// </summary>
    private bool ShouldUseBuffering()
    {
        // Buffering disabled in configuration
        if (!_config.Enabled)
        {
            _logger.LogTrace("Buffering disabled in configuration");
            return false;
        }

        // Check buffer health (with caching to avoid excessive health checks)
        var now = DateTime.UtcNow;
        if (now - _lastHealthCheck > TimeSpan.FromSeconds(5)) // Cache health for 5 seconds
        {
            _lastHealthCheck = now;
        }

        if (!IsBufferHealthy())
        {
            _logger.LogDebug("Buffer is unhealthy, using direct broadcast");
            return false;
        }

        // If inner service is unavailable, we must use buffering (if healthy)
        if (!_innerService.IsAvailable)
        {
            _logger.LogDebug("Inner service unavailable, forcing buffered delivery");
            return true;
        }

        // Default to buffering when enabled and healthy
        return true;
    }

    /// <summary>
    /// Checks if the message buffer is healthy enough for use.
    /// </summary>
    private bool IsBufferHealthy()
    {
        try
        {
            var health = _messageBuffer.GetHealthAsync().GetAwaiter().GetResult();
            return health.IsHealthy && health.Status != BufferHealthStatus.Unhealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking buffer health, assuming unhealthy");
            return false;
        }
    }

    /// <summary>
    /// Broadcasts a message via the buffer system.
    /// </summary>
    private async Task BroadcastViaBufferAsync(string groupName, string methodName, object payload)
    {
        var message = new SignalRMessage
        {
            Id = Guid.NewGuid().ToString(),
            GroupName = groupName,
            MethodName = methodName,
            Payload = payload,
            Timestamp = DateTime.UtcNow,
            Priority = DetermineMessagePriority(methodName, payload),
            Metadata = new Dictionary<string, object>
            {
                ["Source"] = "BufferedSignalRBroadcastService",
                ["BufferingEnabled"] = true,
                ["PayloadType"] = payload.GetType().Name
            }
        };

        var result = await _messageBuffer.EnqueueAsync(message);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to enqueue message for buffered broadcast: {result.ErrorMessage}");
        }

        _logger.LogTrace(
            "Message enqueued for buffered broadcast: ID={MessageId}, Group={GroupName}, " +
            "Method={MethodName}, BufferSize={BufferSize}",
            message.Id, groupName, methodName, result.CurrentBufferSize);
    }

    /// <summary>
    /// Broadcasts a message directly via the inner service.
    /// </summary>
    private async Task BroadcastDirectlyAsync(string groupName, string methodName, object payload)
    {
        await _innerService.BroadcastToGroupAsync(groupName, methodName, payload);

        _logger.LogTrace(
            "Message broadcast directly: Group={GroupName}, Method={MethodName}",
            groupName, methodName);
    }

    /// <summary>
    /// Determines the priority level for a message based on its content and method.
    /// This can be customized based on application-specific requirements.
    /// </summary>
    private static MessagePriority DetermineMessagePriority(string methodName, object payload)
    {
        // Prioritize error messages and system notifications
        if (methodName.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
            methodName.Contains("Alert", StringComparison.OrdinalIgnoreCase))
        {
            return MessagePriority.High;
        }

        // Critical system messages
        if (methodName.Contains("System", StringComparison.OrdinalIgnoreCase) ||
            methodName.Contains("Connection", StringComparison.OrdinalIgnoreCase))
        {
            return MessagePriority.Critical;
        }

        // Regular messages
        return MessagePriority.Normal;
    }

    #endregion

    #region Event Handlers

    private void OnMessageFailed(object? sender, MessageFailedEventArgs e)
    {
        _logger.LogWarning(
            "Buffered message delivery permanently failed: ID={MessageId}, Reason={FailureReason}, " +
            "RetryAttempts={RetryAttempts}, MovedToDeadLetter={MovedToDeadLetter}",
            e.Message.Id, e.FailureReason, e.RetryAttempts, e.MovedToDeadLetterQueue);
    }

    private void OnBufferOverflow(object? sender, BufferOverflowEventArgs e)
    {
        _logger.LogWarning(
            "Buffer overflow occurred: Strategy={OverflowStrategy}, DroppedCount={DroppedCount}, " +
            "CurrentBufferSize={CurrentBufferSize}",
            e.OverflowStrategy, e.DroppedMessageCount, e.CurrentBufferSize);
    }

    private void OnHealthStatusChanged(object? sender, BufferHealthChangedEventArgs e)
    {
        _logger.LogInformation(
            "Buffer health status changed: {PreviousStatus} → {CurrentStatus}, Message={Message}",
            e.PreviousStatus, e.CurrentStatus, e.HealthDetails.Message);
    }

    #endregion

    #region IDisposable Implementation

    /// <summary>
    /// Disposes the service and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            // Unsubscribe from buffer events
            _messageBuffer.MessageFailed -= OnMessageFailed;
            _messageBuffer.BufferOverflow -= OnBufferOverflow;
            _messageBuffer.HealthStatusChanged -= OnHealthStatusChanged;

            _logger.LogInformation(
                "BufferedSignalRBroadcastService disposed: TotalRequests={TotalRequests}, " +
                "BufferedRequests={BufferedRequests}, DirectRequests={DirectRequests}",
                Interlocked.Read(ref _totalBroadcastRequests),
                Interlocked.Read(ref _totalBufferedRequests),
                Interlocked.Read(ref _totalDirectRequests));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing BufferedSignalRBroadcastService");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    #endregion
}

/// <summary>
/// Represents comprehensive metrics for the buffered broadcast service.
/// Provides insights into buffering effectiveness and overall service health.
/// </summary>
public record BufferedBroadcastMetrics
{
    /// <summary>
    /// Gets the timestamp when these metrics were captured.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the total number of broadcast requests received.
    /// </summary>
    public long TotalBroadcastRequests { get; init; }

    /// <summary>
    /// Gets the number of requests that were processed via buffering.
    /// </summary>
    public long BufferedRequests { get; init; }

    /// <summary>
    /// Gets the number of requests that were processed directly.
    /// </summary>
    public long DirectRequests { get; init; }

    /// <summary>
    /// Gets the number of requests that failed completely.
    /// </summary>
    public long FailedRequests { get; init; }

    /// <summary>
    /// Gets the percentage of requests that used buffering.
    /// </summary>
    public double BufferingSuccessRate { get; init; }

    /// <summary>
    /// Gets the overall success rate (including both buffered and direct).
    /// </summary>
    public double OverallSuccessRate { get; init; }

    /// <summary>
    /// Gets the current buffer utilization percentage.
    /// </summary>
    public double BufferUtilization { get; init; }

    /// <summary>
    /// Gets the current buffer health status.
    /// </summary>
    public BufferHealthStatus BufferHealth { get; init; }

    /// <summary>
    /// Gets whether buffering is currently enabled.
    /// </summary>
    public bool IsBufferingEnabled { get; init; }

    /// <summary>
    /// Gets whether the inner SignalR service is available.
    /// </summary>
    public bool InnerServiceAvailable { get; init; }

    /// <summary>
    /// Gets whether the overall service is available.
    /// </summary>
    public bool ServiceAvailable { get; init; }
}