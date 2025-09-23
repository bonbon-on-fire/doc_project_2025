using AIChat.Server.Services.EventStore;
using Xunit;

namespace AIChat.Server.Tests.Services.EventStore;

/// <summary>
/// Unit tests for event projection functionality.
/// Tests event projection interfaces and implementations.
/// </summary>
public class EventProjectionTests
{
    #region ChatStateProjection Tests

    [Fact]
    public void ChatStateProjection_CreateInitialState_ShouldReturnEmptyState()
    {
        // Arrange
        var projection = new ChatStateProjection();

        // Act
        var initialState = projection.CreateInitialState();

        // Assert
        Assert.NotNull(initialState);
        Assert.Equal(string.Empty, initialState.ChatId);
        Assert.Empty(initialState.Messages);
        Assert.Equal(0, initialState.ParticipantCount);
        Assert.True(initialState.CreatedAt <= DateTimeOffset.UtcNow);
        Assert.True(initialState.LastActivityAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void ChatStateProjection_Name_ShouldReturnCorrectName()
    {
        // Arrange
        var projection = new ChatStateProjection();

        // Act
        var name = projection.Name;

        // Assert
        Assert.Equal("ChatStateProjection", name);
    }

    [Fact]
    public void ChatStateProjection_Version_ShouldReturnOne()
    {
        // Arrange
        var projection = new ChatStateProjection();

        // Act
        var version = projection.Version;

        // Assert
        Assert.Equal(1, version);
    }

    [Fact]
    public void ChatStateProjection_CanHandle_WithChatMessageSentEvent_ShouldReturnTrue()
    {
        // Arrange
        var projection = new ChatStateProjection();

        // Act
        var canHandle = projection.CanHandle("ChatMessageSentEvent");

        // Assert
        Assert.True(canHandle);
    }

    [Fact]
    public void ChatStateProjection_CanHandle_WithUnknownEventType_ShouldReturnFalse()
    {
        // Arrange
        var projection = new ChatStateProjection();

        // Act
        var canHandle = projection.CanHandle("UnknownEventType");

        // Assert
        Assert.False(canHandle);
    }

    [Fact]
    public void ChatStateProjection_Apply_WithChatMessageSentEvent_ShouldAddMessage()
    {
        // Arrange
        var projection = new ChatStateProjection();
        var initialState = projection.CreateInitialState();

        var chatEvent = ChatMessageSentEvent.Create(
            "chat-123",
            1,
            new ChatMessageEventData
            {
                MessageId = "msg-456",
                UserId = "user-789",
                Content = "Hello, World!",
                MessageType = "text",
                ChatId = "chat-123"
            });

        // Act
        var newState = projection.Apply(initialState, chatEvent);

        // Assert
        Assert.NotNull(newState);
        Assert.Equal("chat-123", newState.ChatId);
        Assert.Single(newState.Messages);

        var message = newState.Messages[0];
        Assert.Equal("msg-456", message.MessageId);
        Assert.Equal("user-789", message.UserId);
        Assert.Equal("Hello, World!", message.Content);
        Assert.Equal("text", message.MessageType);
        Assert.Equal(chatEvent.Timestamp, message.Timestamp);
        Assert.Equal(chatEvent.Timestamp, newState.LastActivityAt);
    }

    [Fact]
    public void ChatStateProjection_Apply_WithMultipleEvents_ShouldAccumulateMessages()
    {
        // Arrange
        var projection = new ChatStateProjection();
        var state = projection.CreateInitialState();

        var event1 = ChatMessageSentEvent.Create(
            "chat-123",
            1,
            new ChatMessageEventData
            {
                MessageId = "msg-1",
                UserId = "user-1",
                Content = "First message",
                MessageType = "text",
                ChatId = "chat-123"
            });

        var event2 = ChatMessageSentEvent.Create(
            "chat-123",
            2,
            new ChatMessageEventData
            {
                MessageId = "msg-2",
                UserId = "user-2",
                Content = "Second message",
                MessageType = "text",
                ChatId = "chat-123"
            });

        // Act
        state = projection.Apply(state, event1);
        state = projection.Apply(state, event2);

        // Assert
        Assert.Equal(2, state.Messages.Count);
        Assert.Equal("msg-1", state.Messages[0].MessageId);
        Assert.Equal("msg-2", state.Messages[1].MessageId);
        Assert.Equal("user-1", state.Messages[0].UserId);
        Assert.Equal("user-2", state.Messages[1].UserId);
    }

    [Fact]
    public void ChatStateProjection_Apply_WithUnhandledEvent_ShouldReturnUnchangedState()
    {
        // Arrange
        var projection = new ChatStateProjection();
        var initialState = projection.CreateInitialState();

        var unhandledEvent = StateChangedEvent.Create(
            "state-stream",
            1,
            new StateChangeEventData
            {
                EntityType = "User",
                EntityId = "user-123",
                ChangeType = "Updated",
                StateData = "{}"
            });

        // Act
        var newState = projection.Apply(initialState, unhandledEvent);

        // Assert
        Assert.Same(initialState, newState); // Should return the same instance
    }

    [Fact]
    public void ChatStateProjection_Apply_WithNullCurrentState_ShouldThrowArgumentNullException()
    {
        // Arrange
        var projection = new ChatStateProjection();
        var chatEvent = ChatMessageSentEvent.Create(
            "chat-123",
            1,
            new ChatMessageEventData
            {
                MessageId = "msg-456",
                UserId = "user-789",
                Content = "Hello, World!",
                MessageType = "text",
                ChatId = "chat-123"
            });

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            projection.Apply(null!, chatEvent));
    }

    [Fact]
    public void ChatStateProjection_Apply_WithNullEvent_ShouldThrowArgumentNullException()
    {
        // Arrange
        var projection = new ChatStateProjection();
        var state = projection.CreateInitialState();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            projection.Apply(state, null!));
    }

    #endregion

    #region DelegateEventProjection Tests

    [Fact]
    public void DelegateEventProjection_WithCustomHandlers_ShouldWork()
    {
        // Arrange
        var projection = new DelegateEventProjection<int>(
            "CounterProjection",
            () => 0,
            version: 1)
            .Handle<ChatMessageSentEvent>((count, @event) => count + 1)
            .Handle<StateChangedEvent>((count, @event) => count + 2);

        // Act & Assert - Name and Version
        Assert.Equal("CounterProjection", projection.Name);
        Assert.Equal(1, projection.Version);

        // Act & Assert - Initial State
        var initialState = projection.CreateInitialState();
        Assert.Equal(0, initialState);

        // Act & Assert - CanHandle
        Assert.True(projection.CanHandle("ChatMessageSentEvent"));
        Assert.True(projection.CanHandle("StateChangedEvent"));
        Assert.False(projection.CanHandle("UnknownEvent"));

        // Act & Assert - Apply Events
        var chatEvent = ChatMessageSentEvent.Create(
            "chat-123",
            1,
            new ChatMessageEventData
            {
                MessageId = "msg-1",
                UserId = "user-1",
                Content = "Test",
                MessageType = "text",
                ChatId = "chat-123"
            });

        var stateEvent = StateChangedEvent.Create(
            "state-123",
            1,
            new StateChangeEventData
            {
                EntityType = "Test",
                EntityId = "test-1",
                ChangeType = "Created",
                StateData = "{}"
            });

        var state = initialState;
        state = projection.Apply(state, chatEvent);
        Assert.Equal(1, state);

        state = projection.Apply(state, stateEvent);
        Assert.Equal(3, state);
    }

    [Fact]
    public void DelegateEventProjection_Constructor_WithNullName_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new DelegateEventProjection<int>(null!, () => 0));
    }

    [Fact]
    public void DelegateEventProjection_Constructor_WithNullInitialStateFactory_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new DelegateEventProjection<int>("Test", null!));
    }

    [Fact]
    public void DelegateEventProjection_Handle_WithNullEventType_ShouldThrowArgumentNullException()
    {
        // Arrange
        var projection = new DelegateEventProjection<int>("Test", () => 0);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            projection.Handle(null!, (state, @event) => state));
    }

    [Fact]
    public void DelegateEventProjection_Handle_WithNullHandler_ShouldThrowArgumentNullException()
    {
        // Arrange
        var projection = new DelegateEventProjection<int>("Test", () => 0);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            projection.Handle("TestEvent", null!));
    }

    [Fact]
    public void DelegateEventProjection_HandleGeneric_WithNullHandler_ShouldThrowArgumentNullException()
    {
        // Arrange
        var projection = new DelegateEventProjection<int>("Test", () => 0);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            projection.Handle<ChatMessageSentEvent>(null!));
    }

    [Fact]
    public void DelegateEventProjection_CreateInitialState_WithFailingFactory_ShouldThrowEventProjectionException()
    {
        // Arrange
        var projection = new DelegateEventProjection<int>(
            "FailingProjection",
            () => throw new InvalidOperationException("Factory failed"));

        // Act & Assert
        var exception = Assert.Throws<EventProjectionException>(() =>
            projection.CreateInitialState());

        Assert.Contains("Failed to create initial state", exception.Message);
        Assert.Equal("FailingProjection", exception.ProjectionName);
    }

    [Fact]
    public void DelegateEventProjection_Apply_WithFailingHandler_ShouldThrowEventProjectionException()
    {
        // Arrange
        var projection = new DelegateEventProjection<int>("FailingProjection", () => 0)
            .Handle<ChatMessageSentEvent>((state, @event) => throw new InvalidOperationException("Handler failed"));

        var chatEvent = ChatMessageSentEvent.Create(
            "chat-123",
            1,
            new ChatMessageEventData
            {
                MessageId = "msg-1",
                UserId = "user-1",
                Content = "Test",
                MessageType = "text",
                ChatId = "chat-123"
            });

        // Act & Assert
        var exception = Assert.Throws<EventProjectionException>(() =>
            projection.Apply(0, chatEvent));

        Assert.Contains("Failed to apply event", exception.Message);
        Assert.Equal("FailingProjection", exception.ProjectionName);
        Assert.Equal("ChatMessageSentEvent", exception.EventType);
        Assert.Equal(chatEvent.EventId, exception.EventId);
    }

    [Fact]
    public void DelegateEventProjection_Apply_WithTypeMismatch_ShouldReturnUnchangedState()
    {
        // Arrange
        var projection = new DelegateEventProjection<int>("Test", () => 0)
            .Handle<ChatMessageSentEvent>((state, @event) => state + 1);

        // Create a StateChangedEvent but register it as ChatMessageSentEvent type
        var wrongEvent = StateChangedEvent.Create(
            "state-123",
            1,
            new StateChangeEventData
            {
                EntityType = "Test",
                EntityId = "test-1",
                ChangeType = "Created",
                StateData = "{}"
            });

        // Act
        var result = projection.Apply(5, wrongEvent);

        // Assert
        Assert.Equal(5, result); // Should return unchanged state
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void EventProjectionException_Constructor_ShouldSetProperties()
    {
        // Arrange
        var message = "Test error message";
        var projectionName = "TestProjection";
        var eventType = "TestEvent";
        var eventId = "event-123";
        var correlationId = "corr-456";
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new EventProjectionException(
            message,
            projectionName,
            eventType,
            eventId,
            correlationId,
            innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(projectionName, exception.ProjectionName);
        Assert.Equal(eventType, exception.EventType);
        Assert.Equal(eventId, exception.EventId);
        Assert.Equal(correlationId, exception.CorrelationId);
        Assert.Equal(innerException, exception.InnerException);
        Assert.Equal(EventStoreErrorCode.InternalError, exception.ErrorCode);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void EventProjection_CompleteWorkflow_ShouldRebuildChatState()
    {
        // Arrange
        var projection = new ChatStateProjection();
        var events = new List<IEvent>
        {
            ChatMessageSentEvent.Create(
                "chat-123",
                1,
                new ChatMessageEventData
                {
                    MessageId = "msg-1",
                    UserId = "user-alice",
                    Content = "Hello everyone!",
                    MessageType = "text",
                    ChatId = "chat-123"
                }),
            ChatMessageSentEvent.Create(
                "chat-123",
                2,
                new ChatMessageEventData
                {
                    MessageId = "msg-2",
                    UserId = "user-bob",
                    Content = "Hi Alice!",
                    MessageType = "text",
                    ChatId = "chat-123"
                }),
            ChatMessageSentEvent.Create(
                "chat-123",
                3,
                new ChatMessageEventData
                {
                    MessageId = "msg-3",
                    UserId = "user-alice",
                    Content = "How is everyone doing?",
                    MessageType = "text",
                    ChatId = "chat-123"
                })
        };

        // Act
        var state = projection.CreateInitialState();
        foreach (var @event in events)
        {
            state = projection.Apply(state, @event);
        }

        // Assert
        Assert.Equal("chat-123", state.ChatId);
        Assert.Equal(3, state.Messages.Count);

        Assert.Equal("msg-1", state.Messages[0].MessageId);
        Assert.Equal("user-alice", state.Messages[0].UserId);
        Assert.Equal("Hello everyone!", state.Messages[0].Content);

        Assert.Equal("msg-2", state.Messages[1].MessageId);
        Assert.Equal("user-bob", state.Messages[1].UserId);
        Assert.Equal("Hi Alice!", state.Messages[1].Content);

        Assert.Equal("msg-3", state.Messages[2].MessageId);
        Assert.Equal("user-alice", state.Messages[2].UserId);
        Assert.Equal("How is everyone doing?", state.Messages[2].Content);

        // Last activity should be the timestamp of the last event
        Assert.Equal(events.Last().Timestamp, state.LastActivityAt);
    }

    #endregion
}