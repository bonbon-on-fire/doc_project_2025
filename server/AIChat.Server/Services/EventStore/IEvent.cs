using System.Text.Json.Serialization;

namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Base interface for all events in the system.
/// Represents an immutable event that occurred at a specific point in time.
/// </summary>
public interface IEvent
{
    /// <summary>
    /// Gets the unique identifier for this event.
    /// Must be unique across all events in the system.
    /// </summary>
    string EventId { get; }

    /// <summary>
    /// Gets the stream identifier this event belongs to.
    /// Events are grouped into streams for ordering and replay purposes.
    /// </summary>
    string StreamId { get; }

    /// <summary>
    /// Gets the type name of the event for serialization and routing.
    /// Used for polymorphic deserialization and event handling.
    /// </summary>
    string EventType { get; }

    /// <summary>
    /// Gets the version number of this event within its stream.
    /// Provides ordering and helps detect concurrency conflicts.
    /// </summary>
    long Version { get; }

    /// <summary>
    /// Gets the timestamp when this event was created.
    /// Represents the business time when the event occurred.
    /// </summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the correlation identifier for tracing related events.
    /// Links events that are part of the same business operation.
    /// </summary>
    string? CorrelationId { get; }

    /// <summary>
    /// Gets the causation identifier indicating what caused this event.
    /// Links events in cause-and-effect chains.
    /// </summary>
    string? CausationId { get; }

    /// <summary>
    /// Gets additional metadata for the event.
    /// Contains non-essential data like user context, source information, etc.
    /// </summary>
    Dictionary<string, object>? Metadata { get; }
}

/// <summary>
/// Base record implementation of IEvent that provides common event functionality.
/// Use this as a base for all concrete event types to ensure consistency.
/// </summary>
public abstract record EventBase : IEvent
{
    /// <summary>
    /// Gets or initializes the unique identifier for this event.
    /// </summary>
    public required string EventId { get; init; }

    /// <summary>
    /// Gets or initializes the stream identifier this event belongs to.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets the type name of the event for serialization.
    /// Defaults to the concrete type name.
    /// </summary>
    [JsonIgnore]
    public virtual string EventType => GetType().Name;

    /// <summary>
    /// Gets or initializes the version number within the stream.
    /// </summary>
    public required long Version { get; init; }

    /// <summary>
    /// Gets or initializes the timestamp when this event was created.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets or initializes the correlation identifier for tracing.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets or initializes the causation identifier.
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>
    /// Gets or initializes additional metadata for the event.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a new event ID using a GUID.
    /// </summary>
    /// <returns>A new unique event ID</returns>
    public static string NewEventId() => Guid.NewGuid().ToString();

    /// <summary>
    /// Creates a new correlation ID using a GUID.
    /// </summary>
    /// <returns>A new unique correlation ID</returns>
    public static string NewCorrelationId() => Guid.NewGuid().ToString();

    /// <summary>
    /// Creates basic metadata with timestamp and source information.
    /// </summary>
    /// <param name="source">The source that created this event</param>
    /// <param name="additionalMetadata">Additional metadata to include</param>
    /// <returns>Metadata dictionary</returns>
    public static Dictionary<string, object> CreateMetadata(
        string source,
        Dictionary<string, object>? additionalMetadata = null)
    {
        var metadata = new Dictionary<string, object>
        {
            ["source"] = source,
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
        };

        if (additionalMetadata != null)
        {
            foreach (var kvp in additionalMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        return metadata;
    }
}

/// <summary>
/// Represents a domain event with specific event data.
/// Use this for events that carry business-specific payload.
/// </summary>
/// <typeparam name="TData">The type of event data</typeparam>
public abstract record DomainEvent<TData> : EventBase
    where TData : class
{
    /// <summary>
    /// Gets or initializes the event-specific data payload.
    /// </summary>
    public required TData Data { get; init; }
}

/// <summary>
/// Represents event data for chat message events.
/// </summary>
public record ChatMessageEventData
{
    /// <summary>
    /// Gets or initializes the message identifier.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// Gets or initializes the user who sent the message.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Gets or initializes the message content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets or initializes the type of message.
    /// </summary>
    public required string MessageType { get; init; }

    /// <summary>
    /// Gets or initializes the chat identifier.
    /// </summary>
    public required string ChatId { get; init; }

    /// <summary>
    /// Gets or initializes additional message properties.
    /// </summary>
    public Dictionary<string, object>? Properties { get; init; }
}

/// <summary>
/// Event raised when a chat message is sent.
/// </summary>
public record ChatMessageSentEvent : DomainEvent<ChatMessageEventData>
{
    /// <summary>
    /// Creates a new ChatMessageSentEvent.
    /// </summary>
    /// <param name="streamId">The stream identifier (typically chat ID)</param>
    /// <param name="version">The event version within the stream</param>
    /// <param name="messageData">The message event data</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <param name="causationId">Optional causation ID</param>
    /// <param name="metadata">Optional additional metadata</param>
    /// <returns>A new ChatMessageSentEvent</returns>
    public static ChatMessageSentEvent Create(
        string streamId,
        long version,
        ChatMessageEventData messageData,
        string? correlationId = null,
        string? causationId = null,
        Dictionary<string, object>? metadata = null)
    {
        return new ChatMessageSentEvent
        {
            EventId = NewEventId(),
            StreamId = streamId,
            Version = version,
            Timestamp = DateTimeOffset.UtcNow,
            Data = messageData,
            CorrelationId = correlationId,
            CausationId = causationId,
            Metadata = metadata ?? CreateMetadata("ChatGrain")
        };
    }
}

/// <summary>
/// Represents event data for state change events.
/// </summary>
public record StateChangeEventData
{
    /// <summary>
    /// Gets or initializes the type of entity that changed.
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// Gets or initializes the identifier of the entity that changed.
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// Gets or initializes the type of change (Created, Updated, Deleted).
    /// </summary>
    public required string ChangeType { get; init; }

    /// <summary>
    /// Gets or initializes the serialized state data.
    /// </summary>
    public required string StateData { get; init; }

    /// <summary>
    /// Gets or initializes the previous state data (for updates).
    /// </summary>
    public string? PreviousStateData { get; init; }

    /// <summary>
    /// Gets or initializes the entity version after the change.
    /// </summary>
    public long? EntityVersion { get; init; }
}

/// <summary>
/// Event raised when entity state changes.
/// Integrates with IStateManager to provide audit trail.
/// </summary>
public record StateChangedEvent : DomainEvent<StateChangeEventData>
{
    /// <summary>
    /// Creates a new StateChangedEvent.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="version">The event version within the stream</param>
    /// <param name="stateData">The state change event data</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <param name="causationId">Optional causation ID</param>
    /// <param name="metadata">Optional additional metadata</param>
    /// <returns>A new StateChangedEvent</returns>
    public static StateChangedEvent Create(
        string streamId,
        long version,
        StateChangeEventData stateData,
        string? correlationId = null,
        string? causationId = null,
        Dictionary<string, object>? metadata = null)
    {
        return new StateChangedEvent
        {
            EventId = NewEventId(),
            StreamId = streamId,
            Version = version,
            Timestamp = DateTimeOffset.UtcNow,
            Data = stateData,
            CorrelationId = correlationId,
            CausationId = causationId,
            Metadata = metadata ?? CreateMetadata("StateManager")
        };
    }
}
