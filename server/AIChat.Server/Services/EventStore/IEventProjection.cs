namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Interface for projecting events into state objects.
/// Implements the projection pattern for event sourcing replay scenarios.
/// </summary>
/// <typeparam name="T">The type of state being projected</typeparam>
public interface IEventProjection<T>
{
    /// <summary>
    /// Creates the initial state for the projection.
    /// This is the starting point before any events are applied.
    /// </summary>
    /// <returns>The initial state</returns>
    T CreateInitialState();

    /// <summary>
    /// Applies an event to the current state and returns the new state.
    /// This method should be pure (no side effects) and deterministic.
    /// </summary>
    /// <param name="currentState">The current state before applying the event</param>
    /// <param name="eventData">The event to apply</param>
    /// <returns>The new state after applying the event</returns>
    /// <exception cref="ArgumentNullException">Thrown when currentState or eventData is null</exception>
    /// <exception cref="EventProjectionException">Thrown when event application fails</exception>
    T Apply(T currentState, IEvent eventData);

    /// <summary>
    /// Determines if this projection can handle the given event type.
    /// Used for filtering events during replay.
    /// </summary>
    /// <param name="eventType">The event type to check</param>
    /// <returns>True if this projection can handle the event type, false otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when eventType is null</exception>
    bool CanHandle(string eventType);

    /// <summary>
    /// Gets the name of this projection for logging and debugging.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the version of this projection.
    /// Used for managing projection schema evolution.
    /// </summary>
    int Version { get; }
}

/// <summary>
/// Abstract base class for event projections that provides common functionality.
/// Inherit from this class to get standard projection behavior.
/// </summary>
/// <typeparam name="T">The type of state being projected</typeparam>
public abstract class EventProjectionBase<T> : IEventProjection<T>
{
    /// <summary>
    /// Gets the name of this projection.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the version of this projection.
    /// </summary>
    public virtual int Version => 1;

    /// <summary>
    /// Creates the initial state for the projection.
    /// </summary>
    /// <returns>The initial state</returns>
    public abstract T CreateInitialState();

    /// <summary>
    /// Applies an event to the current state and returns the new state.
    /// </summary>
    /// <param name="currentState">The current state</param>
    /// <param name="eventData">The event to apply</param>
    /// <returns>The new state after applying the event</returns>
    /// <exception cref="ArgumentNullException">Thrown when currentState or event is null</exception>
    /// <exception cref="EventProjectionException">Thrown when event application fails</exception>
    public T Apply(T currentState, IEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(eventData);

        if (!CanHandle(eventData.EventType))
        {
            return currentState; // Return unchanged state for unhandled events
        }

        try
        {
            return ApplyEvent(currentState, eventData);
        }
        catch (Exception ex) when (ex is not EventProjectionException)
        {
            throw new EventProjectionException(
                $"Failed to apply event {eventData.EventType} to projection {Name}",
                Name,
                eventData.EventType,
                eventData.EventId,
                eventData.CorrelationId,
                ex);
        }
    }

    /// <summary>
    /// Applies a specific event to the current state.
    /// Override this method to implement event-specific projection logic.
    /// </summary>
    /// <param name="currentState">The current state</param>
    /// <param name="eventData">The event to apply</param>
    /// <returns>The new state after applying the event</returns>
    protected abstract T ApplyEvent(T currentState, IEvent eventData);

    /// <summary>
    /// Determines if this projection can handle the given event type.
    /// Override this method to specify which events this projection handles.
    /// </summary>
    /// <param name="eventType">The event type to check</param>
    /// <returns>True if this projection can handle the event type</returns>
    public abstract bool CanHandle(string eventType);

    /// <summary>
    /// Validates the state after applying an event.
    /// Override this method to add custom state validation.
    /// </summary>
    /// <param name="state">The state to validate</param>
    /// <returns>True if the state is valid</returns>
    protected virtual bool ValidateState(T state)
    {
        return state != null;
    }
}

/// <summary>
/// A simple projection that handles multiple event types using delegates.
/// Useful for creating projections without inheritance.
/// </summary>
/// <typeparam name="T">The type of state being projected</typeparam>
public class DelegateEventProjection<T> : IEventProjection<T>
{
    private readonly Func<T> _initialStateFactory;
    private readonly Dictionary<string, Func<T, IEvent, T>> _eventHandlers;

    /// <summary>
    /// Gets the name of this projection.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the version of this projection.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Initializes a new instance of the DelegateEventProjection class.
    /// </summary>
    /// <param name="name">The projection name</param>
    /// <param name="initialStateFactory">Function to create initial state</param>
    /// <param name="version">The projection version</param>
    public DelegateEventProjection(
        string name,
        Func<T> initialStateFactory,
        int version = 1)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _initialStateFactory = initialStateFactory ?? throw new ArgumentNullException(nameof(initialStateFactory));
        Version = version;
        _eventHandlers = [];
    }

    /// <summary>
    /// Adds an event handler for a specific event type.
    /// </summary>
    /// <param name="eventType">The event type to handle</param>
    /// <param name="handler">The handler function</param>
    /// <returns>This projection for fluent configuration</returns>
    public DelegateEventProjection<T> Handle(string eventType, Func<T, IEvent, T> handler)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(handler);

        _eventHandlers[eventType] = handler;
        return this;
    }

    /// <summary>
    /// Adds a typed event handler for a specific event type.
    /// </summary>
    /// <typeparam name="TEvent">The specific event type</typeparam>
    /// <param name="handler">The typed handler function</param>
    /// <returns>This projection for fluent configuration</returns>
    public DelegateEventProjection<T> Handle<TEvent>(Func<T, TEvent, T> handler)
        where TEvent : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        var eventType = typeof(TEvent).Name;
        _eventHandlers[eventType] = (state, eventData) =>
        {
            if (eventData is TEvent typedEvent)
            {
                return handler(state, typedEvent);
            }
            return state; // Return unchanged for type mismatch
        };
        return this;
    }

    /// <summary>
    /// Creates the initial state for the projection.
    /// </summary>
    /// <returns>The initial state</returns>
    public T CreateInitialState()
    {
        try
        {
            return _initialStateFactory();
        }
        catch (Exception ex)
        {
            throw new EventProjectionException(
                $"Failed to create initial state for projection {Name}",
                Name,
                innerException: ex);
        }
    }

    /// <summary>
    /// Applies an event to the current state.
    /// </summary>
    /// <param name="currentState">The current state</param>
    /// <param name="eventData">The event to apply</param>
    /// <returns>The new state</returns>
    public T Apply(T currentState, IEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(eventData);

        if (_eventHandlers.TryGetValue(eventData.EventType, out var handler))
        {
            try
            {
                return handler(currentState, eventData);
            }
            catch (Exception ex)
            {
                throw new EventProjectionException(
                    $"Failed to apply event {eventData.EventType} to projection {Name}",
                    Name,
                    eventData.EventType,
                    eventData.EventId,
                    eventData.CorrelationId,
                    ex);
            }
        }

        return currentState; // Return unchanged for unhandled events
    }

    /// <summary>
    /// Determines if this projection can handle the given event type.
    /// </summary>
    /// <param name="eventType">The event type to check</param>
    /// <returns>True if this projection can handle the event type</returns>
    public bool CanHandle(string eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return _eventHandlers.ContainsKey(eventType);
    }
}

/// <summary>
/// Exception thrown when event projection fails.
/// </summary>
public class EventProjectionException : EventStoreException
{
    /// <summary>
    /// Gets the projection name that failed.
    /// </summary>
    public string? ProjectionName { get; }

    /// <summary>
    /// Gets the event type that caused the failure.
    /// </summary>
    public string? EventType { get; }

    /// <summary>
    /// Initializes a new instance of the EventProjectionException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="projectionName">The projection name</param>
    /// <param name="eventType">The event type</param>
    /// <param name="eventId">Optional event identifier</param>
    /// <param name="correlationId">Optional correlation identifier</param>
    /// <param name="innerException">Optional inner exception</param>
    public EventProjectionException(
        string message,
        string? projectionName = null,
        string? eventType = null,
        string? eventId = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base(
            message,
            EventStoreErrorCode.InternalError,
            eventId: eventId,
            correlationId: correlationId,
            context: new Dictionary<string, object>
            {
                ["projectionName"] = projectionName ?? "unknown",
                ["eventType"] = eventType ?? "unknown"
            },
            innerException: innerException)
    {
        ProjectionName = projectionName;
        EventType = eventType;
    }

    public EventProjectionException(string message, EventStoreErrorCode errorCode = EventStoreErrorCode.InternalError, string? streamId = null, string? eventId = null, string? correlationId = null, Dictionary<string, object>? context = null, Exception? innerException = null) : base(message, errorCode, streamId, eventId, correlationId, context, innerException)
    {
    }

    public EventProjectionException() : base()
    {
    }

    public EventProjectionException(string? message) : base(message)
    {
    }

    public EventProjectionException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Sample projection for chat state reconstruction.
/// Demonstrates how to implement event projection for chat events.
/// </summary>
public class ChatStateProjection : EventProjectionBase<ChatProjectionState>
{
    /// <summary>
    /// Gets the name of this projection.
    /// </summary>
    public override string Name => "ChatStateProjection";

    /// <summary>
    /// Gets the version of this projection.
    /// </summary>
    public override int Version => 1;

    /// <summary>
    /// Creates the initial chat state.
    /// </summary>
    /// <returns>Empty chat state</returns>
    public override ChatProjectionState CreateInitialState()
    {
        return new ChatProjectionState
        {
            ChatId = string.Empty,
            Messages = [],
            ParticipantCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Determines if this projection can handle the given event type.
    /// </summary>
    /// <param name="eventType">The event type</param>
    /// <returns>True if it's a chat-related event</returns>
    public override bool CanHandle(string eventType)
    {
        return eventType switch
        {
            nameof(ChatMessageSentEvent) => true,
            _ => false
        };
    }

    /// <summary>
    /// Applies a chat event to the current state.
    /// </summary>
    /// <param name="currentState">The current state</param>
    /// <param name="eventData">The event to apply</param>
    /// <returns>The updated state</returns>
    protected override ChatProjectionState ApplyEvent(ChatProjectionState currentState, IEvent eventData)
    {
        return eventData switch
        {
            ChatMessageSentEvent chatEvent => ApplyChatMessageSent(currentState, chatEvent),
            _ => currentState
        };
    }

    private static ChatProjectionState ApplyChatMessageSent(ChatProjectionState state, ChatMessageSentEvent chatEvent)
    {
        var newMessage = new ChatMessageProjection
        {
            MessageId = chatEvent.Data.MessageId,
            UserId = chatEvent.Data.UserId,
            Content = chatEvent.Data.Content,
            MessageType = chatEvent.Data.MessageType,
            Timestamp = chatEvent.Timestamp
        };

        var updatedMessages = new List<ChatMessageProjection>(state.Messages) { newMessage };

        return state with
        {
            ChatId = chatEvent.Data.ChatId,
            Messages = updatedMessages,
            LastActivityAt = chatEvent.Timestamp
        };
    }
}

/// <summary>
/// Represents the projected state of a chat from events.
/// </summary>
public record ChatProjectionState
{
    /// <summary>
    /// Gets or initializes the chat identifier.
    /// </summary>
    public required string ChatId { get; init; }

    /// <summary>
    /// Gets or initializes the list of messages in the chat.
    /// </summary>
    public required IReadOnlyList<ChatMessageProjection> Messages { get; init; }

    /// <summary>
    /// Gets or initializes the number of participants.
    /// </summary>
    public required int ParticipantCount { get; init; }

    /// <summary>
    /// Gets or initializes when the chat was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets or initializes the last activity timestamp.
    /// </summary>
    public required DateTimeOffset LastActivityAt { get; init; }
}

/// <summary>
/// Represents a projected chat message.
/// </summary>
public record ChatMessageProjection
{
    /// <summary>
    /// Gets or initializes the message identifier.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// Gets or initializes the user identifier.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Gets or initializes the message content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets or initializes the message type.
    /// </summary>
    public required string MessageType { get; init; }

    /// <summary>
    /// Gets or initializes the message timestamp.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }
}