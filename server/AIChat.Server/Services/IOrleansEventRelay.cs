namespace AIChat.Server.Services;

/// <summary>
/// Abstraction for relaying Orleans grain events to ChatService-compatible event handlers.
/// This service bridges Orleans grain events to the existing ChatService event system
/// used by SignalR hubs, enabling seamless integration between Orleans and direct service modes.
///
/// <example>
/// Usage in ChatHub:
/// <code>
/// // Check if Orleans is available
/// if (await _eventRelay.IsOrleansEnabledAsync())
/// {
///     // Subscribe to grain events
///     await _eventRelay.SubscribeToGrainEventsAsync(
///         chatId,
///         OnMessageCreated,
///         OnStreamChunkReceived,
///         OnMessageReceived
///     );
/// }
/// </code>
/// </example>
///
/// <remarks>
/// The service automatically falls back to NullOrleansEventRelay when Orleans is not available,
/// ensuring seamless operation in both Orleans and direct service modes.
/// </remarks>
/// </summary>
public interface IOrleansEventRelay
{
    /// <summary>
    /// Checks if Orleans event relay is currently enabled and available.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if Orleans events can be relayed, false if should fallback to direct service events</returns>
    Task<bool> IsOrleansEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to Orleans grain events for a specific chat and relays them as ChatService events.
    /// </summary>
    /// <param name="chatId">The chat ID to subscribe to events for</param>
    /// <param name="onMessageCreated">Callback for message created events</param>
    /// <param name="onStreamChunk">Callback for stream chunk events</param>
    /// <param name="onMessageReceived">Callback for message received events</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the subscription operation</returns>
    Task SubscribeToGrainEventsAsync(
        string chatId,
        Func<MessageCreatedEvent, Task> onMessageCreated,
        Func<StreamChunkEvent, Task> onStreamChunk,
        Func<MessageEvent, Task> onMessageReceived,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes from Orleans grain events for a specific chat.
    /// </summary>
    /// <param name="chatId">The chat ID to unsubscribe from</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the unsubscription operation</returns>
    Task UnsubscribeFromGrainEventsAsync(string chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if real-time events are supported for a specific chat.
    /// This helps determine whether to rely on Orleans streams or fallback to polling.
    /// </summary>
    /// <param name="chatId">The chat ID to check event support for</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if real-time events are supported for this chat</returns>
    Task<bool> SupportsRealTimeEventsAsync(string chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current health status of the Orleans event relay system.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Health information about the event relay system</returns>
    Task<OrleansEventRelayHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Health information for the Orleans event relay system.
/// </summary>
public record OrleansEventRelayHealth
{
    /// <summary>
    /// Gets whether the event relay system is healthy and functioning.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Gets whether Orleans is available for event streaming.
    /// </summary>
    public required bool IsOrleansAvailable { get; init; }

    /// <summary>
    /// Gets the number of active event subscriptions.
    /// </summary>
    public int ActiveSubscriptions { get; init; }

    /// <summary>
    /// Gets any error messages or status details.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the timestamp of this health check.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// No-op implementation of IOrleansEventRelay for when Orleans is not available.
/// Falls back to direct ChatService event handling.
/// </summary>
public class NullOrleansEventRelay : IOrleansEventRelay
{
    /// <inheritdoc />
    public Task<bool> IsOrleansEnabledAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task SubscribeToGrainEventsAsync(
        string chatId,
        Func<MessageCreatedEvent, Task> onMessageCreated,
        Func<StreamChunkEvent, Task> onStreamChunk,
        Func<MessageEvent, Task> onMessageReceived,
        CancellationToken cancellationToken = default)
    {
        // No-op - Orleans not available, will use direct service events
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnsubscribeFromGrainEventsAsync(string chatId, CancellationToken cancellationToken = default)
    {
        // No-op - Orleans not available
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> SupportsRealTimeEventsAsync(string chatId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<OrleansEventRelayHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OrleansEventRelayHealth
        {
            IsHealthy = true, // Null implementation is always "healthy"
            IsOrleansAvailable = false,
            ActiveSubscriptions = 0,
            Message = "Orleans event relay is disabled - using direct service events"
        });
    }
}