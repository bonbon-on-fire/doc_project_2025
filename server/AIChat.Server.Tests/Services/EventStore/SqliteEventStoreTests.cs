using AIChat.Server.Services.EventStore;
using AIChat.Server.Services.EventStore.Implementations;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services.EventStore;

/// <summary>
/// Unit tests for SqliteEventStore implementation.
/// Tests the complete event store functionality using an in-memory SQLite database.
/// </summary>
public class SqliteEventStoreTests : IDisposable
{
    private readonly string _tempDatabasePath;
    private readonly TestSqliteConnectionFactory _connectionFactory;
    private readonly IEventSerializer _serializer;
    private readonly Mock<ILogger<SqliteEventStore>> _mockLogger;
    private readonly EventStoreMetricsCollector _metrics;
    private readonly SqliteEventStore _eventStore;

    /// <summary>
    /// Initializes a new instance of the SqliteEventStoreTests class.
    /// </summary>
    public SqliteEventStoreTests()
    {
        // Create unique temporary SQLite database for testing
        _tempDatabasePath = Path.GetTempFileName();
        var connectionString = $"Data Source={_tempDatabasePath}";

        // Create proper connection factory that provides fresh connections
        _connectionFactory = new TestSqliteConnectionFactory(connectionString);

        // Set up dependencies
        _serializer = new JsonEventSerializer();
        _mockLogger = new Mock<ILogger<SqliteEventStore>>();
        _metrics = new EventStoreMetricsCollector();

        // Create event store instance
        _eventStore = new SqliteEventStore(_connectionFactory, _serializer, _mockLogger.Object, _metrics);

        // Initialize schema
        InitializeSchemaAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Initializes the event store schema in the test database.
    /// </summary>
    private async Task InitializeSchemaAsync()
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await EventStoreSchemaHelper.EnsureEventStoreSchemaAsync(connection);
    }

    /// <summary>
    /// Disposes the test resources.
    /// </summary>
    public void Dispose()
    {
        // Dispose connection factory
        _connectionFactory?.Dispose();

        // Clean up temporary file if it exists
        if (!string.IsNullOrEmpty(_tempDatabasePath) && File.Exists(_tempDatabasePath))
        {
            try
            {
                File.Delete(_tempDatabasePath);
            }
            catch
            {
                // Ignore errors when cleaning up temp files
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Generates a unique stream ID for test isolation.
    /// </summary>
    /// <param name="prefix">Optional prefix for the stream name.</param>
    /// <returns>A unique stream identifier.</returns>
    private static string GenerateUniqueStreamId(string prefix = "test-stream")
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    #region Basic Append Tests

    [Fact]
    public async Task AppendAsync_WithSingleEvent_ShouldSucceed()
    {
        // Arrange
        var chatEvent = ChatMessageSentEvent.Create(
            "chat-123",
            0,
            new ChatMessageEventData
            {
                MessageId = "msg-456",
                UserId = "user-789",
                Content = "Hello, World!",
                MessageType = "text",
                ChatId = "chat-123"
            });

        // Act
        var result = await _eventStore.AppendAsync(chatEvent);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(1, result.EventsProcessed);
    }

    [Fact]
    public async Task AppendAsync_WithMultipleEvents_ShouldSucceed()
    {
        // Arrange
        var events = new[]
        {
            ChatMessageSentEvent.Create(
                "chat-123",
                0,
                new ChatMessageEventData
                {
                    MessageId = "msg-1",
                    UserId = "user-1",
                    Content = "First message",
                    MessageType = "text",
                    ChatId = "chat-123"
                }),
            ChatMessageSentEvent.Create(
                "chat-123",
                1,
                new ChatMessageEventData
                {
                    MessageId = "msg-2",
                    UserId = "user-2",
                    Content = "Second message",
                    MessageType = "text",
                    ChatId = "chat-123"
                })
        };

        // Act
        var result = await _eventStore.AppendAsync(events);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(2, result.EventsProcessed);
    }

    [Fact]
    public async Task AppendAsync_WithDuplicateEventId_ShouldThrowDuplicateEventException()
    {
        // Arrange
        const string eventId = "duplicate-event-id";
        var event1 = ChatMessageSentEvent.Create(
            "chat-123",
            0,
            new ChatMessageEventData
            {
                MessageId = "msg-1",
                UserId = "user-1",
                Content = "First message",
                MessageType = "text",
                ChatId = "chat-123"
            }) with
        { EventId = eventId };

        var event2 = ChatMessageSentEvent.Create(
            "chat-123",
            1,
            new ChatMessageEventData
            {
                MessageId = "msg-2",
                UserId = "user-2",
                Content = "Second message",
                MessageType = "text",
                ChatId = "chat-123"
            }) with
        { EventId = eventId };

        // Act
        _ = await _eventStore.AppendAsync(event1);

        // Assert
        _ = await Assert.ThrowsAsync<DuplicateEventException>(() =>
            _eventStore.AppendAsync(event2));
    }

    [Fact]
    public async Task AppendAsync_WithNullEvent_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _eventStore.AppendAsync((IEvent)null!));
    }

    [Fact]
    public async Task AppendAsync_WithEmptyEventCollection_ShouldThrowArgumentException()
    {
        // Act & Assert
        _ = await Assert.ThrowsAsync<ArgumentException>(() =>
            _eventStore.AppendAsync([]));
    }

    #endregion Basic Append Tests

    #region Version Control Tests

    [Fact]
    public async Task AppendToStreamAsync_WithCorrectExpectedVersion_ShouldSucceed()
    {
        // Arrange
        var streamId = GenerateUniqueStreamId("version-test-stream");
        var events = new[]
        {
            ChatMessageSentEvent.Create(
                streamId,
                0,
                new ChatMessageEventData
                {
                    MessageId = "msg-1",
                    UserId = "user-1",
                    Content = "First message",
                    MessageType = "text",
                    ChatId = streamId
                })
        };

        // Act
        var result = await _eventStore.AppendToStreamAsync(streamId, -1, events);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.NewStreamVersion);
        Assert.Equal(-1, result.PreviousStreamVersion);
        Assert.Equal(1, result.EventsProcessed);
    }

    [Fact]
    public async Task AppendToStreamAsync_WithWrongExpectedVersion_ShouldThrowConcurrencyException()
    {
        // Arrange
        var streamId = GenerateUniqueStreamId("concurrency-test-stream");

        // First, append an event to create the stream
        var firstEvent = ChatMessageSentEvent.Create(
            streamId,
            0,
            new ChatMessageEventData
            {
                MessageId = "msg-1",
                UserId = "user-1",
                Content = "First message",
                MessageType = "text",
                ChatId = streamId
            });

        _ = await _eventStore.AppendAsync(firstEvent);

        // Now try to append with wrong expected version
        var secondEvent = ChatMessageSentEvent.Create(
            streamId,
            1,
            new ChatMessageEventData
            {
                MessageId = "msg-2",
                UserId = "user-2",
                Content = "Second message",
                MessageType = "text",
                ChatId = streamId
            });

        // Act & Assert
        _ = await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _eventStore.AppendToStreamAsync(streamId, -1, [secondEvent]));
    }

    #endregion Version Control Tests

    #region Read Tests

    [Fact]
    public async Task GetEventsAsync_WithExistingStream_ShouldReturnEvents()
    {
        // Arrange
        var streamId = GenerateUniqueStreamId("read-test-stream");
        var events = new[]
        {
            ChatMessageSentEvent.Create(
                streamId,
                0,
                new ChatMessageEventData
                {
                    MessageId = "msg-1",
                    UserId = "user-1",
                    Content = "First message",
                    MessageType = "text",
                    ChatId = streamId
                }),
            ChatMessageSentEvent.Create(
                streamId,
                1,
                new ChatMessageEventData
                {
                    MessageId = "msg-2",
                    UserId = "user-2",
                    Content = "Second message",
                    MessageType = "text",
                    ChatId = streamId
                })
        };

        _ = await _eventStore.AppendAsync(events);

        // Act
        var result = await _eventStore.GetEventsAsync(streamId);

        // Assert
        Assert.Equal(2, result.Events.Count);
        Assert.Equal(2, result.TotalCount);

        var firstEvent = result.Events[0] as ChatMessageSentEvent;
        var secondEvent = result.Events[1] as ChatMessageSentEvent;

        Assert.NotNull(firstEvent);
        Assert.NotNull(secondEvent);
        Assert.Equal("msg-1", firstEvent.Data.MessageId);
        Assert.Equal("msg-2", secondEvent.Data.MessageId);
        Assert.Equal(0, firstEvent.Version);
        Assert.Equal(1, secondEvent.Version);
    }

    [Fact]
    public async Task GetEventsAsync_WithNonExistentStream_ShouldReturnEmptyResult()
    {
        // Act
        var result = await _eventStore.GetEventsAsync("non-existent-stream");

        // Assert
        Assert.Empty(result.Events);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetEventsAsync_WithVersionRange_ShouldReturnFilteredEvents()
    {
        // Arrange
        var streamId = GenerateUniqueStreamId("version-range-stream");
        var events = Enumerable.Range(0, 5)
            .Select(i => ChatMessageSentEvent.Create(
                streamId,
                i,
                new ChatMessageEventData
                {
                    MessageId = $"msg-{i}",
                    UserId = $"user-{i}",
                    Content = $"Message {i}",
                    MessageType = "text",
                    ChatId = streamId
                }))
            .ToArray();

        _ = await _eventStore.AppendAsync(events);

        // Act
        var result = await _eventStore.GetEventsAsync(streamId, 2, 4);

        // Assert
        Assert.Equal(3, result.Events.Count); // Events with versions 2, 3, 4
        Assert.Equal(3, result.TotalCount);

        var eventVersions = result.Events.Select(e => e.Version).ToArray();
        Assert.Equal([2L, 3L, 4L], eventVersions);
    }

    [Fact]
    public async Task GetStreamVersionAsync_WithExistingStream_ShouldReturnCurrentVersion()
    {
        // Arrange
        var streamId = GenerateUniqueStreamId("version-test-stream");
        var events = Enumerable.Range(0, 3)
            .Select(i => ChatMessageSentEvent.Create(
                streamId,
                i,
                new ChatMessageEventData
                {
                    MessageId = $"msg-{i}",
                    UserId = $"user-{i}",
                    Content = $"Message {i}",
                    MessageType = "text",
                    ChatId = streamId
                }))
            .ToArray();

        _ = await _eventStore.AppendAsync(events);

        // Act
        var version = await _eventStore.GetStreamVersionAsync(streamId);

        // Assert
        Assert.Equal(2, version);
    }

    [Fact]
    public async Task GetStreamVersionAsync_WithNonExistentStream_ShouldReturnMinusOne()
    {
        // Act
        var version = await _eventStore.GetStreamVersionAsync("non-existent-stream");

        // Assert
        Assert.Equal(-1, version);
    }

    [Fact]
    public async Task StreamExistsAsync_WithExistingStream_ShouldReturnTrue()
    {
        // Arrange
        var streamId = GenerateUniqueStreamId("exists-test-stream");
        var chatEvent = ChatMessageSentEvent.Create(
            streamId,
            0,
            new ChatMessageEventData
            {
                MessageId = "msg-1",
                UserId = "user-1",
                Content = "Test message",
                MessageType = "text",
                ChatId = streamId
            });

        _ = await _eventStore.AppendAsync(chatEvent);

        // Act
        var exists = await _eventStore.StreamExistsAsync(streamId);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task StreamExistsAsync_WithNonExistentStream_ShouldReturnFalse()
    {
        // Act
        var exists = await _eventStore.StreamExistsAsync("non-existent-stream");

        // Assert
        Assert.False(exists);
    }

    #endregion Read Tests

    #region Query Tests

    [Fact]
    public async Task QueryEventsAsync_WithEventTypeFilter_ShouldReturnFilteredEvents()
    {
        // Arrange
        var chatEvent = ChatMessageSentEvent.Create(
            GenerateUniqueStreamId("chat-stream"),
            0,
            new ChatMessageEventData
            {
                MessageId = "msg-1",
                UserId = "user-1",
                Content = "Chat message",
                MessageType = "text",
                ChatId = "chat-123"
            });

        var stateEvent = StateChangedEvent.Create(
            GenerateUniqueStreamId("state-stream"),
            0,
            new StateChangeEventData
            {
                EntityType = "User",
                EntityId = "user-1",
                ChangeType = "Updated",
                StateData = "{}"
            });

        _ = await _eventStore.AppendAsync([chatEvent, stateEvent]);

        var query = EventQuery.ForEventType("ChatMessageSentEvent");

        // Act
        var result = await _eventStore.QueryEventsAsync(query);

        // Assert
        _ = Assert.Single(result.Events);
        _ = Assert.IsType<ChatMessageSentEvent>(result.Events[0]);
    }

    [Fact]
    public async Task GetEventsByCorrelationIdAsync_WithMatchingEvents_ShouldReturnEvents()
    {
        // Arrange
        const string correlationId = "test-correlation-123";

        var event1 = ChatMessageSentEvent.Create(
            "stream-1",
            0,
            new ChatMessageEventData
            {
                MessageId = "msg-1",
                UserId = "user-1",
                Content = "Message 1",
                MessageType = "text",
                ChatId = "chat-123"
            },
            correlationId: correlationId);

        var event2 = ChatMessageSentEvent.Create(
            "stream-2",
            0,
            new ChatMessageEventData
            {
                MessageId = "msg-2",
                UserId = "user-2",
                Content = "Message 2",
                MessageType = "text",
                ChatId = "chat-456"
            },
            correlationId: correlationId);

        var event3 = ChatMessageSentEvent.Create(
            "stream-3",
            0,
            new ChatMessageEventData
            {
                MessageId = "msg-3",
                UserId = "user-3",
                Content = "Message 3",
                MessageType = "text",
                ChatId = "chat-789"
            },
            correlationId: "different-correlation");

        _ = await _eventStore.AppendAsync([event1, event2, event3]);

        // Act
        var result = await _eventStore.GetEventsByCorrelationIdAsync(correlationId);

        // Assert
        Assert.Equal(2, result.Events.Count);
        Assert.All(result.Events, e => Assert.Equal(correlationId, e.CorrelationId));
    }

    [Fact]
    public async Task GetEventsByTimeRangeAsync_WithinRange_ShouldReturnEvents()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow.AddHours(-1);

        var event1 = ChatMessageSentEvent.Create(
            "stream-1",
            0,
            new ChatMessageEventData
            {
                MessageId = "msg-1",
                UserId = "user-1",
                Content = "Message 1",
                MessageType = "text",
                ChatId = "chat-123"
            }) with
        { Timestamp = baseTime };

        var event2 = ChatMessageSentEvent.Create(
            "stream-2",
            0,
            new ChatMessageEventData
            {
                MessageId = "msg-2",
                UserId = "user-2",
                Content = "Message 2",
                MessageType = "text",
                ChatId = "chat-456"
            }) with
        { Timestamp = baseTime.AddMinutes(30) };

        var event3 = ChatMessageSentEvent.Create(
            "stream-3",
            0,
            new ChatMessageEventData
            {
                MessageId = "msg-3",
                UserId = "user-3",
                Content = "Message 3",
                MessageType = "text",
                ChatId = "chat-789"
            }) with
        { Timestamp = baseTime.AddHours(2) };

        _ = await _eventStore.AppendAsync([event1, event2, event3]);

        // Act
        var result = await _eventStore.GetEventsByTimeRangeAsync(
            baseTime.AddMinutes(-5),
            baseTime.AddHours(1));

        // Assert
        Assert.Equal(2, result.Events.Count); // Should get event1 and event2
        Assert.All(result.Events, e =>
            Assert.True(e.Timestamp >= baseTime.AddMinutes(-5) &&
                       e.Timestamp <= baseTime.AddHours(1)));
    }

    #endregion Query Tests

    #region Replay Tests

    [Fact]
    public async Task ReplayAsync_WithChatProjection_ShouldRebuildState()
    {
        // Arrange
        var streamId = GenerateUniqueStreamId("replay-test-stream");
        var events = new[]
        {
            ChatMessageSentEvent.Create(
                streamId,
                0,
                new ChatMessageEventData
                {
                    MessageId = "msg-1",
                    UserId = "alice",
                    Content = "Hello!",
                    MessageType = "text",
                    ChatId = streamId
                }),
            ChatMessageSentEvent.Create(
                streamId,
                1,
                new ChatMessageEventData
                {
                    MessageId = "msg-2",
                    UserId = "bob",
                    Content = "Hi Alice!",
                    MessageType = "text",
                    ChatId = streamId
                })
        };

        _ = await _eventStore.AppendAsync(events);

        var projection = new ChatStateProjection();

        // Act
        var result = await _eventStore.ReplayAsync(streamId, projection);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(2, result.EventsProcessed);
        Assert.Equal(1, result.Version);

        var state = result.State;
        Assert.Equal(streamId, state.ChatId);
        Assert.Equal(2, state.Messages.Count);
        Assert.Equal("alice", state.Messages[0].UserId);
        Assert.Equal("bob", state.Messages[1].UserId);
    }

    [Fact]
    public async Task ReplayAsync_ToSpecificVersion_ShouldStopAtVersion()
    {
        // Arrange
        var streamId = GenerateUniqueStreamId("replay-version-test");
        var events = Enumerable.Range(0, 5)
            .Select(i => ChatMessageSentEvent.Create(
                streamId,
                i,
                new ChatMessageEventData
                {
                    MessageId = $"msg-{i}",
                    UserId = $"user-{i}",
                    Content = $"Message {i}",
                    MessageType = "text",
                    ChatId = streamId
                }))
            .ToArray();

        _ = await _eventStore.AppendAsync(events);

        var projection = new ChatStateProjection();

        // Act
        var result = await _eventStore.ReplayAsync(streamId, 3, projection);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(4, result.EventsProcessed);
        Assert.Equal(3, result.Version);

        var state = result.State;
        Assert.Equal(4, state.Messages.Count);
        Assert.Equal("msg-3", state.Messages[state.Messages.Count - 1].MessageId);
    }

    [Fact]
    public async Task ReplayAsync_MultipleStreams_ShouldCombineEvents()
    {
        // Arrange
        const string stream1 = "stream-1";
        const string stream2 = "stream-2";

        var events1 = new[]
        {
            ChatMessageSentEvent.Create(
                stream1,
                0,
                new ChatMessageEventData
                {
                    MessageId = "msg-1-1",
                    UserId = "alice",
                    Content = "Hello from stream 1!",
                    MessageType = "text",
                    ChatId = "chat-1"
                })
        };

        var events2 = new[]
        {
            ChatMessageSentEvent.Create(
                stream2,
                0,
                new ChatMessageEventData
                {
                    MessageId = "msg-2-1",
                    UserId = "bob",
                    Content = "Hello from stream 2!",
                    MessageType = "text",
                    ChatId = "chat-2"
                })
        };

        _ = await _eventStore.AppendAsync(events1);
        _ = await _eventStore.AppendAsync(events2);

        var projection = new DelegateEventProjection<int>("MessageCounter", () => 0)
            .Handle<ChatMessageSentEvent>((count, @event) => count + 1);

        // Act
        var result = await _eventStore.ReplayAsync([stream1, stream2], projection);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.EventsProcessed);
        Assert.Equal(2, result.State); // Should count both messages
    }

    #endregion Replay Tests

    #region Health and Metrics Tests

    [Fact]
    public async Task CheckHealthAsync_WithValidStore_ShouldReturnHealthy()
    {
        // Act
        var health = await _eventStore.CheckHealthAsync();

        // Assert
        Assert.True(health.IsHealthy);
        Assert.True(health.IsStorageHealthy);
        Assert.True(health.IsSerializationHealthy);
        Assert.Equal("SQLite Event Store", health.Implementation);
    }

    [Fact]
    public async Task GetMetricsAsync_ShouldReturnCurrentMetrics()
    {
        // Arrange
        // Perform some operations to generate metrics
        var chatEvent = ChatMessageSentEvent.Create(
            GenerateUniqueStreamId("metrics-test"),
            0,
            new ChatMessageEventData
            {
                MessageId = "msg-1",
                UserId = "user-1",
                Content = "Test message",
                MessageType = "text",
                ChatId = "chat-123"
            });

        _ = await _eventStore.AppendAsync(chatEvent);
        _ = await _eventStore.GetEventsAsync(chatEvent.StreamId);

        // Act
        var metrics = await _eventStore.GetMetricsAsync();

        // Assert
        Assert.True(metrics.AppendOperations > 0);
        Assert.True(metrics.ReadOperations > 0);
        Assert.True(metrics.TotalEvents > 0);
        Assert.True(metrics.TotalStreams > 0);
        Assert.Equal(0, metrics.FailedOperations);
    }

    [Fact]
    public async Task OptimizeAsync_ShouldCompleteWithoutError()
    {
        // Act & Assert
        await _eventStore.OptimizeAsync();
        // Should complete without throwing an exception
    }

    #endregion Health and Metrics Tests

    #region Error Handling Tests

    [Fact]
    public async Task GetEventsAsync_WithNullStreamId_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _eventStore.GetEventsAsync(null!));
    }

    [Fact]
    public async Task QueryEventsAsync_WithNullQuery_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _eventStore.QueryEventsAsync(null!));
    }

    [Fact]
    public async Task QueryEventsAsync_WithInvalidQuery_ShouldThrowEventValidationException()
    {
        // Arrange
        var invalidQuery = new EventQuery
        {
            PageSize = -1 // Invalid page size
        };

        // Act & Assert
        _ = await Assert.ThrowsAsync<EventValidationException>(() =>
            _eventStore.QueryEventsAsync(invalidQuery));
    }

    [Fact]
    public async Task ReplayAsync_WithNullProjection_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _eventStore.ReplayAsync<ChatProjectionState>("stream-1", null!));
    }

    #endregion Error Handling Tests
}
