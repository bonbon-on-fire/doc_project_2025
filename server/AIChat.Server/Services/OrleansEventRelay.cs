using System.Diagnostics;
using AIChat.Orleans.Client.Services;
using AIChat.Server.Services.Routing;

namespace AIChat.Server.Services;

/// <summary>
/// Implementation of IOrleansEventRelay that bridges Orleans grain events to ChatService events.
/// This service enables seamless integration between Orleans grains and existing SignalR hub event handlers
/// by converting Orleans grain events to ChatService-compatible event formats.
///
/// Features:
/// - Comprehensive diagnostic logging with structured data
/// - Performance metrics collection
/// - Distributed tracing integration
/// - Health monitoring with detailed status reporting
/// - Thread-safe subscription management
/// </summary>
public class OrleansEventRelay : IOrleansEventRelay
{
    private readonly IOrleansIntegrationService? _orleansService;
    private readonly IDualModeRouter _dualModeRouter;
    private readonly ILogger<OrleansEventRelay> _logger;
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.OrleansEventRelay");

    // Track active subscriptions for health monitoring with timestamps
    private readonly Dictionary<string, EventSubscription> _activeSubscriptions = new();
    private readonly object _subscriptionLock = new();
    private readonly DateTime _serviceStartTime = DateTime.UtcNow;
    private long _totalOperationsCounter;
    private long _failedOperationsCounter;

    /// <summary>
    /// Initializes a new instance of the OrleansEventRelay.
    /// </summary>
    /// <param name="orleansService">Orleans integration service (can be null if Orleans not available)</param>
    /// <param name="dualModeRouter">Router for determining Orleans availability</param>
    /// <param name="logger">Logger instance</param>
    public OrleansEventRelay(
        IOrleansIntegrationService? orleansService,
        IDualModeRouter dualModeRouter,
        ILogger<OrleansEventRelay> logger)
    {
        _orleansService = orleansService;
        _dualModeRouter = dualModeRouter ?? throw new ArgumentNullException(nameof(dualModeRouter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> IsOrleansEnabledAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("IsOrleansEnabled");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Interlocked.Increment(ref _totalOperationsCounter);

            _logger.LogTrace("Checking Orleans availability status");

            // Use DualModeRouter to check Orleans health
            var isEnabled = await _dualModeRouter.IsOrleansEnabledAsync(cancellationToken);

            stopwatch.Stop();

            _logger.LogDebug(
                "Orleans availability check completed: {IsEnabled} (Duration: {ElapsedMs}ms, OrleansService: {HasOrleansService})",
                isEnabled, stopwatch.ElapsedMilliseconds, _orleansService != null
            );

            activity?.SetTag("orleans.enabled", isEnabled);
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);

            return isEnabled;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Interlocked.Increment(ref _failedOperationsCounter);

            _logger.LogWarning(ex,
                "Failed to check Orleans availability after {ElapsedMs}ms, assuming disabled (OrleansService: {HasOrleansService})",
                stopwatch.ElapsedMilliseconds, _orleansService != null
            );

            activity?.SetTag("orleans.enabled", false);
            activity?.SetTag("error", true);
            activity?.SetTag("error.type", ex.GetType().Name);

            return false;
        }
    }

    /// <inheritdoc />
    public async Task SubscribeToGrainEventsAsync(
        string chatId,
        Func<MessageCreatedEvent, Task> onMessageCreated,
        Func<StreamChunkEvent, Task> onStreamChunk,
        Func<MessageEvent, Task> onMessageReceived,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("SubscribeToGrainEvents");
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrEmpty(chatId))
        {
            throw new ArgumentException("Chat ID cannot be null or empty", nameof(chatId));
        }

        ArgumentNullException.ThrowIfNull(onMessageCreated);
        ArgumentNullException.ThrowIfNull(onStreamChunk);
        ArgumentNullException.ThrowIfNull(onMessageReceived);

        try
        {
            Interlocked.Increment(ref _totalOperationsCounter);

            activity?.SetTag("chat.id", chatId);

            _logger.LogInformation(
                "Starting Orleans event subscription for ChatId: {ChatId} (TotalSubscriptions: {CurrentSubscriptions})",
                chatId, _activeSubscriptions.Count
            );

            // Check if Orleans is available
            if (!await IsOrleansEnabledAsync(cancellationToken))
            {
                _logger.LogDebug(
                    "Orleans not available, skipping grain event subscription for ChatId: {ChatId} (OrleansService: {HasOrleansService})",
                    chatId, _orleansService != null
                );
                activity?.SetTag("subscription.skipped", true);
                activity?.SetTag("skip.reason", "orleans_disabled");
                return;
            }

            if (_orleansService == null)
            {
                _logger.LogWarning(
                    "Orleans integration service not available for ChatId: {ChatId}, subscription will be inactive",
                    chatId
                );
                activity?.SetTag("subscription.inactive", true);
                activity?.SetTag("inactive.reason", "service_unavailable");
                return;
            }

            bool wasAlreadySubscribed;
            lock (_subscriptionLock)
            {
                // Check if already subscribed
                if (_activeSubscriptions.TryGetValue(chatId, out var existingSubscription))
                {
                    wasAlreadySubscribed = true;
                    _logger.LogDebug(
                        "Already subscribed to events for ChatId: {ChatId} (SubscribedAt: {SubscribedAt})",
                        chatId, _activeSubscriptions[chatId].SubscribedAt
                    );
                    activity?.SetTag("subscription.duplicate", true);
                    return;
                }
                else
                {
                    wasAlreadySubscribed = false;

                    // Create subscription record
                    var subscription = new EventSubscription
                    {
                        ChatId = chatId,
                        OnMessageCreated = onMessageCreated,
                        OnStreamChunk = onStreamChunk,
                        OnMessageReceived = onMessageReceived,
                        SubscribedAt = DateTime.UtcNow
                    };

                    _activeSubscriptions[chatId] = subscription;
                }
            }

            // TODO: Implement Orleans stream subscription when grain streaming is available
            // For now, this is a placeholder that sets up the infrastructure
            // Future implementation will subscribe to Orleans streams and relay events

            stopwatch.Stop();

            _logger.LogInformation(
                "Successfully subscribed to Orleans events for ChatId: {ChatId} (Duration: {ElapsedMs}ms, TotalSubscriptions: {TotalSubscriptions}, AlreadySubscribed: {WasAlreadySubscribed})",
                chatId, stopwatch.ElapsedMilliseconds, _activeSubscriptions.Count, wasAlreadySubscribed
            );

            activity?.SetTag("subscription.success", true);
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
            activity?.SetTag("total.subscriptions", _activeSubscriptions.Count);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Interlocked.Increment(ref _failedOperationsCounter);

            _logger.LogError(ex,
                "Failed to subscribe to Orleans events for ChatId: {ChatId} after {ElapsedMs}ms (TotalSubscriptions: {TotalSubscriptions}, OrleansService: {HasOrleansService})",
                chatId, stopwatch.ElapsedMilliseconds, _activeSubscriptions.Count, _orleansService != null
            );

            // Clean up failed subscription
            lock (_subscriptionLock)
            {
                _activeSubscriptions.Remove(chatId);
            }

            activity?.SetTag("subscription.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task UnsubscribeFromGrainEventsAsync(string chatId, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("UnsubscribeFromGrainEvents");
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrEmpty(chatId))
        {
            throw new ArgumentException("Chat ID cannot be null or empty", nameof(chatId));
        }

        try
        {
            Interlocked.Increment(ref _totalOperationsCounter);

            activity?.SetTag("chat.id", chatId);

            _logger.LogInformation(
                "Starting Orleans event unsubscription for ChatId: {ChatId} (TotalSubscriptions: {CurrentSubscriptions})",
                chatId, _activeSubscriptions.Count
            );

            EventSubscription? removedSubscription = null;

            lock (_subscriptionLock)
            {
                if (!_activeSubscriptions.TryGetValue(chatId, out removedSubscription))
                {
                    _logger.LogDebug(
                        "No active subscription found for ChatId: {ChatId} (TotalSubscriptions: {TotalSubscriptions})",
                        chatId, _activeSubscriptions.Count
                    );
                    activity?.SetTag("subscription.found", false);
                    return;
                }
                else
                {
                    _activeSubscriptions.Remove(chatId);
                }
            }

            // TODO: Implement Orleans stream unsubscription when grain streaming is available
            // For now, just remove from tracking

            // Simulate async operation for future compatibility
            await Task.CompletedTask;

            stopwatch.Stop();

            var subscriptionDuration = removedSubscription != null
                ? DateTime.UtcNow - removedSubscription.SubscribedAt
                : TimeSpan.Zero;

            _logger.LogInformation(
                "Successfully unsubscribed from Orleans events for ChatId: {ChatId} (Duration: {ElapsedMs}ms, SubscriptionDuration: {SubscriptionDurationMs}ms, RemainingSubscriptions: {RemainingSubscriptions})",
                chatId, stopwatch.ElapsedMilliseconds, subscriptionDuration.TotalMilliseconds, _activeSubscriptions.Count
            );

            activity?.SetTag("unsubscription.success", true);
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
            activity?.SetTag("subscription.duration_ms", subscriptionDuration.TotalMilliseconds);
            activity?.SetTag("remaining.subscriptions", _activeSubscriptions.Count);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Interlocked.Increment(ref _failedOperationsCounter);

            _logger.LogError(ex,
                "Failed to unsubscribe from Orleans events for ChatId: {ChatId} after {ElapsedMs}ms (TotalSubscriptions: {TotalSubscriptions})",
                chatId, stopwatch.ElapsedMilliseconds, _activeSubscriptions.Count
            );

            activity?.SetTag("unsubscription.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SupportsRealTimeEventsAsync(string chatId, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("SupportsRealTimeEvents");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Interlocked.Increment(ref _totalOperationsCounter);

            activity?.SetTag("chat.id", chatId);

            _logger.LogTrace(
                "Checking real-time events support for ChatId: {ChatId} (TotalSubscriptions: {TotalSubscriptions})",
                chatId, _activeSubscriptions.Count
            );

            // Real-time events are supported when Orleans is available
            var isOrleansEnabled = await IsOrleansEnabledAsync(cancellationToken);

            stopwatch.Stop();

            _logger.LogTrace(
                "Real-time events support check completed for ChatId: {ChatId}: {IsSupported} (Duration: {ElapsedMs}ms, OrleansService: {HasOrleansService})",
                chatId, isOrleansEnabled, stopwatch.ElapsedMilliseconds, _orleansService != null
            );

            activity?.SetTag("realtime.supported", isOrleansEnabled);
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);

            return isOrleansEnabled;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Interlocked.Increment(ref _failedOperationsCounter);

            _logger.LogWarning(ex,
                "Failed to check real-time events support for ChatId: {ChatId} after {ElapsedMs}ms, assuming not supported (OrleansService: {HasOrleansService})",
                chatId, stopwatch.ElapsedMilliseconds, _orleansService != null
            );

            activity?.SetTag("realtime.supported", false);
            activity?.SetTag("error", true);
            activity?.SetTag("error.type", ex.GetType().Name);

            return false;
        }
    }

    /// <inheritdoc />
    public async Task<OrleansEventRelayHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("GetHealth");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Interlocked.Increment(ref _totalOperationsCounter);

            _logger.LogTrace("Performing Orleans event relay health check");

            var isOrleansAvailable = await IsOrleansEnabledAsync(cancellationToken);
            int activeSubscriptionCount;
            var subscriptionDetails = new List<string>();

            lock (_subscriptionLock)
            {
                activeSubscriptionCount = _activeSubscriptions.Count;

                // Collect subscription details for diagnostics
                foreach (var subscription in _activeSubscriptions.Values)
                {
                    var duration = DateTime.UtcNow - subscription.SubscribedAt;
                    subscriptionDetails.Add($"{subscription.ChatId}:{duration.TotalMinutes:F1}m");
                }
            }

            stopwatch.Stop();

            var serviceUptime = DateTime.UtcNow - _serviceStartTime;
            var totalOps = Interlocked.Read(ref _totalOperationsCounter);
            var failedOps = Interlocked.Read(ref _failedOperationsCounter);
            var successRate = totalOps > 0 ? ((double)(totalOps - failedOps) / totalOps) * 100 : 100;

            var health = new OrleansEventRelayHealth
            {
                IsHealthy = true, // Service itself is healthy
                IsOrleansAvailable = isOrleansAvailable,
                ActiveSubscriptions = activeSubscriptionCount,
                Message = CreateHealthMessage(isOrleansAvailable, activeSubscriptionCount, serviceUptime, successRate, subscriptionDetails)
            };

            _logger.LogDebug(
                "Event relay health check completed: {IsHealthy}, Orleans: {IsOrleansAvailable}, Subscriptions: {ActiveSubscriptions}, Success Rate: {SuccessRate:F1}%, Uptime: {UptimeHours:F1}h (Duration: {ElapsedMs}ms)",
                health.IsHealthy, health.IsOrleansAvailable, health.ActiveSubscriptions, successRate, serviceUptime.TotalHours, stopwatch.ElapsedMilliseconds
            );

            activity?.SetTag("health.healthy", health.IsHealthy);
            activity?.SetTag("health.orleans_available", health.IsOrleansAvailable);
            activity?.SetTag("health.active_subscriptions", health.ActiveSubscriptions);
            activity?.SetTag("health.success_rate", successRate);
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);

            return health;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Interlocked.Increment(ref _failedOperationsCounter);

            _logger.LogError(ex,
                "Failed to perform health check for Orleans event relay after {ElapsedMs}ms (OrleansService: {HasOrleansService})",
                stopwatch.ElapsedMilliseconds, _orleansService != null
            );

            activity?.SetTag("health.healthy", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);

            return new OrleansEventRelayHealth
            {
                IsHealthy = false,
                IsOrleansAvailable = false,
                ActiveSubscriptions = 0,
                Message = $"Health check failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Creates a detailed health message with diagnostics.
    /// </summary>
    private static string CreateHealthMessage(bool isOrleansAvailable, int activeSubscriptions, TimeSpan uptime, double successRate, List<string> subscriptionDetails)
    {
        var baseMessage = isOrleansAvailable
            ? $"Orleans event relay operational with {activeSubscriptions} active subscriptions"
            : "Orleans event relay using fallback mode - Orleans not available";

        var diagnostics = $" | Uptime: {uptime.TotalHours:F1}h, Success Rate: {successRate:F1}%";

        if (subscriptionDetails.Count > 0 && subscriptionDetails.Count <= 5)
        {
            diagnostics += $", Subscriptions: [{string.Join(", ", subscriptionDetails)}]";
        }
        else if (subscriptionDetails.Count > 5)
        {
            diagnostics += $", Subscriptions: {subscriptionDetails.Count} active (oldest: {subscriptionDetails.First()})";
        }

        return baseMessage + diagnostics;
    }

    /// <summary>
    /// Internal class to track event subscriptions with enhanced diagnostics.
    /// </summary>
    private sealed class EventSubscription
    {
        public required string ChatId { get; init; }
        public required Func<MessageCreatedEvent, Task> OnMessageCreated { get; init; }
        public required Func<StreamChunkEvent, Task> OnStreamChunk { get; init; }
        public required Func<MessageEvent, Task> OnMessageReceived { get; init; }
        public DateTime SubscribedAt { get; init; }

        /// <summary>
        /// Gets the duration this subscription has been active.
        /// </summary>
        public TimeSpan Duration => DateTime.UtcNow - SubscribedAt;
    }
}