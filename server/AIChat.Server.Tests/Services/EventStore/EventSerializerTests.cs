using AIChat.Server.Services.EventStore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services.EventStore;

/// <summary>
/// Unit tests for EventSerializer classes.
/// Tests event serialization and deserialization functionality.
/// </summary>
public class EventSerializerTests
{
    private readonly JsonEventSerializer _serializer;
    private readonly Mock<ILogger<JsonEventSerializer>> _mockLogger;
    private static readonly string[] messageData = ["important", "test"];

    /// <summary>
    /// Initializes a new instance of the EventSerializerTests class.
    /// </summary>
    public EventSerializerTests()
    {
        _mockLogger = new Mock<ILogger<JsonEventSerializer>>();
        _serializer = new JsonEventSerializer(logger: _mockLogger.Object);
    }

    #region Basic Serialization Tests

    [Fact]
    public void JsonEventSerializer_ContentType_ShouldReturnApplicationJson()
    {
        // Act
        var contentType = _serializer.ContentType;

        // Assert
        Assert.Equal("application/json", contentType);
    }

    [Fact]
    public void JsonEventSerializer_Serialize_ShouldSerializeChatMessageSentEvent()
    {
        // Arrange
        var chatEvent = ChatMessageSentEvent.Create(
            "test-stream",
            1,
            new ChatMessageEventData
            {
                MessageId = "msg-123",
                UserId = "user-456",
                Content = "Hello, World!",
                MessageType = "text",
                ChatId = "chat-789"
            },
            correlationId: "corr-123");

        // Act
        var json = _serializer.Serialize(chatEvent);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("\"eventType\":\"ChatMessageSentEvent\"", json);
        Assert.Contains("\"streamId\":\"test-stream\"", json);
        Assert.Contains("\"version\":1", json);
        Assert.Contains("\"messageId\":\"msg-123\"", json);
        Assert.Contains("\"userId\":\"user-456\"", json);
        Assert.Contains("\"content\":\"Hello, World!\"", json);
    }

    [Fact]
    public void JsonEventSerializer_Serialize_ShouldSerializeStateChangedEvent()
    {
        // Arrange
        var stateEvent = StateChangedEvent.Create(
            "state-stream",
            2,
            new StateChangeEventData
            {
                EntityType = "Chat",
                EntityId = "chat-123",
                ChangeType = "Updated",
                StateData = "{\"title\":\"Updated Chat\"}"
            });

        // Act
        var json = _serializer.Serialize(stateEvent);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("\"eventType\":\"StateChangedEvent\"", json);
        Assert.Contains("\"streamId\":\"state-stream\"", json);
        Assert.Contains("\"version\":2", json);
        Assert.Contains("\"entityType\":\"Chat\"", json);
        Assert.Contains("\"entityId\":\"chat-123\"", json);
        Assert.Contains("\"changeType\":\"Updated\"", json);
    }

    [Fact]
    public void JsonEventSerializer_Serialize_WithNullEvent_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _serializer.Serialize(null!));
    }

    #endregion

    #region Deserialization Tests

    [Fact]
    public void JsonEventSerializer_Deserialize_ShouldDeserializeChatMessageSentEvent()
    {
        // Arrange
        var originalEvent = ChatMessageSentEvent.Create(
            "test-stream",
            1,
            new ChatMessageEventData
            {
                MessageId = "msg-123",
                UserId = "user-456",
                Content = "Hello, World!",
                MessageType = "text",
                ChatId = "chat-789"
            },
            correlationId: "corr-123");

        var json = _serializer.Serialize(originalEvent);

        // Act
        var deserializedEvent = _serializer.Deserialize("ChatMessageSentEvent", json);

        // Assert
        Assert.NotNull(deserializedEvent);
        Assert.IsType<ChatMessageSentEvent>(deserializedEvent);

        var chatEvent = (ChatMessageSentEvent)deserializedEvent;
        Assert.Equal(originalEvent.EventId, chatEvent.EventId);
        Assert.Equal(originalEvent.StreamId, chatEvent.StreamId);
        Assert.Equal(originalEvent.Version, chatEvent.Version);
        Assert.Equal(originalEvent.CorrelationId, chatEvent.CorrelationId);
        Assert.Equal(originalEvent.Data.MessageId, chatEvent.Data.MessageId);
        Assert.Equal(originalEvent.Data.UserId, chatEvent.Data.UserId);
        Assert.Equal(originalEvent.Data.Content, chatEvent.Data.Content);
    }

    [Fact]
    public void JsonEventSerializer_DeserializeGeneric_ShouldDeserializeChatMessageSentEvent()
    {
        // Arrange
        var originalEvent = ChatMessageSentEvent.Create(
            "test-stream",
            1,
            new ChatMessageEventData
            {
                MessageId = "msg-123",
                UserId = "user-456",
                Content = "Hello, World!",
                MessageType = "text",
                ChatId = "chat-789"
            });

        var json = _serializer.Serialize(originalEvent);

        // Act
        var deserializedEvent = _serializer.Deserialize<ChatMessageSentEvent>(json);

        // Assert
        Assert.NotNull(deserializedEvent);
        Assert.Equal(originalEvent.EventId, deserializedEvent.EventId);
        Assert.Equal(originalEvent.StreamId, deserializedEvent.StreamId);
        Assert.Equal(originalEvent.Version, deserializedEvent.Version);
        Assert.Equal(originalEvent.Data.MessageId, deserializedEvent.Data.MessageId);
    }

    [Fact]
    public void JsonEventSerializer_Deserialize_WithInvalidEventType_ShouldThrowEventDeserializationException()
    {
        // Arrange
        var json = "{\"eventType\":\"UnknownEvent\",\"eventId\":\"test\"}";

        // Act & Assert
        Assert.Throws<EventDeserializationException>(() =>
            _serializer.Deserialize("UnknownEventType", json));
    }

    [Fact]
    public void JsonEventSerializer_Deserialize_WithMalformedJson_ShouldThrowEventDeserializationException()
    {
        // Arrange
        var malformedJson = "{\"eventType\":\"ChatMessageSentEvent\""; // Missing closing brace

        // Act & Assert
        Assert.Throws<EventDeserializationException>(() =>
            _serializer.Deserialize("ChatMessageSentEvent", malformedJson));
    }

    [Fact]
    public void JsonEventSerializer_Deserialize_WithNullEventType_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _serializer.Deserialize(null!, "{}"));
    }

    [Fact]
    public void JsonEventSerializer_Deserialize_WithNullEventData_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _serializer.Deserialize("ChatMessageSentEvent", null!));
    }

    #endregion

    #region Metadata Serialization Tests

    [Fact]
    public void JsonEventSerializer_SerializeMetadata_ShouldSerializeDictionary()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            ["source"] = "test",
            ["timestamp"] = DateTime.UtcNow.ToString("O"),
            ["version"] = 1,
            ["flag"] = true
        };

        // Act
        var json = _serializer.SerializeMetadata(metadata);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("\"source\":\"test\"", json);
        Assert.Contains("\"version\":1", json);
        Assert.Contains("\"flag\":true", json);
    }

    [Fact]
    public void JsonEventSerializer_SerializeMetadata_WithNullMetadata_ShouldReturnNull()
    {
        // Act
        var result = _serializer.SerializeMetadata(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void JsonEventSerializer_SerializeMetadata_WithEmptyMetadata_ShouldReturnNull()
    {
        // Arrange
        var metadata = new Dictionary<string, object>();

        // Act
        var result = _serializer.SerializeMetadata(metadata);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void JsonEventSerializer_DeserializeMetadata_ShouldDeserializeDictionary()
    {
        // Arrange
        var originalMetadata = new Dictionary<string, object>
        {
            ["source"] = "test",
            ["version"] = 1,
            ["flag"] = true
        };

        var json = _serializer.SerializeMetadata(originalMetadata);

        // Act
        var deserializedMetadata = _serializer.DeserializeMetadata(json);

        // Assert
        Assert.NotNull(deserializedMetadata);
        Assert.Equal("test", deserializedMetadata["source"].ToString());
        Assert.Equal(1, Convert.ToInt32(deserializedMetadata["version"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(Convert.ToBoolean(deserializedMetadata["flag"], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void JsonEventSerializer_DeserializeMetadata_WithNullJson_ShouldReturnNull()
    {
        // Act
        var result = _serializer.DeserializeMetadata(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void JsonEventSerializer_DeserializeMetadata_WithEmptyJson_ShouldReturnNull()
    {
        // Act
        var result = _serializer.DeserializeMetadata("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void JsonEventSerializer_DeserializeMetadata_WithMalformedJson_ShouldThrowEventDeserializationException()
    {
        // Arrange
        var malformedJson = "{\"key\":\"value\""; // Missing closing brace

        // Act & Assert
        Assert.Throws<EventDeserializationException>(() =>
            _serializer.DeserializeMetadata(malformedJson));
    }

    #endregion

    #region Event Type Support Tests

    [Fact]
    public void JsonEventSerializer_SupportsEventType_WithKnownType_ShouldReturnTrue()
    {
        // Act
        var supports = _serializer.SupportsEventType("ChatMessageSentEvent");

        // Assert
        Assert.True(supports);
    }

    [Fact]
    public void JsonEventSerializer_SupportsEventType_WithUnknownType_ShouldReturnFalse()
    {
        // Act
        var supports = _serializer.SupportsEventType("UnknownEventType");

        // Assert
        Assert.False(supports);
    }

    [Fact]
    public void JsonEventSerializer_SupportsEventType_WithNullType_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _serializer.SupportsEventType(null!));
    }

    [Fact]
    public void JsonEventSerializer_RegisterEventType_ShouldAllowCustomRegistration()
    {
        // Arrange
        var customSerializer = new JsonEventSerializer();

        // Act
        customSerializer.RegisterEventType("CustomEvent", typeof(ChatMessageSentEvent));
        var supports = customSerializer.SupportsEventType("CustomEvent");

        // Assert
        Assert.True(supports);
    }

    [Fact]
    public void JsonEventSerializer_RegisterEventType_WithNonEventType_ShouldThrowArgumentException()
    {
        // Arrange
        var customSerializer = new JsonEventSerializer();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            customSerializer.RegisterEventType("InvalidType", typeof(string)));
    }

    #endregion

    #region Round-trip Tests

    [Fact]
    public void JsonEventSerializer_RoundTrip_ChatMessageSentEvent_ShouldPreserveAllData()
    {
        // Arrange
        var originalEvent = ChatMessageSentEvent.Create(
            "test-stream-123",
            42,
            new ChatMessageEventData
            {
                MessageId = "msg-456",
                UserId = "user-789",
                Content = "Test message with special chars: àáâãäå",
                MessageType = "text",
                ChatId = "chat-101112",
                Properties = new Dictionary<string, object>
                {
                    ["urgent"] = true,
                    ["priority"] = 5,
                    ["tags"] = messageData
                }
            },
            correlationId: "corr-abc",
            causationId: "cause-def",
            metadata: new Dictionary<string, object>
            {
                ["source"] = "unit-test",
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                ["environment"] = "test"
            });

        // Act
        var json = _serializer.Serialize(originalEvent);
        var deserializedEvent = _serializer.Deserialize<ChatMessageSentEvent>(json);

        // Assert
        Assert.Equal(originalEvent.EventId, deserializedEvent.EventId);
        Assert.Equal(originalEvent.StreamId, deserializedEvent.StreamId);
        Assert.Equal(originalEvent.EventType, deserializedEvent.EventType);
        Assert.Equal(originalEvent.Version, deserializedEvent.Version);
        Assert.Equal(originalEvent.Timestamp, deserializedEvent.Timestamp);
        Assert.Equal(originalEvent.CorrelationId, deserializedEvent.CorrelationId);
        Assert.Equal(originalEvent.CausationId, deserializedEvent.CausationId);

        // Verify data
        Assert.Equal(originalEvent.Data.MessageId, deserializedEvent.Data.MessageId);
        Assert.Equal(originalEvent.Data.UserId, deserializedEvent.Data.UserId);
        Assert.Equal(originalEvent.Data.Content, deserializedEvent.Data.Content);
        Assert.Equal(originalEvent.Data.MessageType, deserializedEvent.Data.MessageType);
        Assert.Equal(originalEvent.Data.ChatId, deserializedEvent.Data.ChatId);

        // Verify properties if they exist
        if (originalEvent.Data.Properties != null && deserializedEvent.Data.Properties != null)
        {
            Assert.Equal(originalEvent.Data.Properties["urgent"], deserializedEvent.Data.Properties["urgent"]);
            Assert.Equal(originalEvent.Data.Properties["priority"], deserializedEvent.Data.Properties["priority"]);
        }

        // Verify metadata if it exists
        if (originalEvent.Metadata != null && deserializedEvent.Metadata != null)
        {
            Assert.Equal(originalEvent.Metadata["source"], deserializedEvent.Metadata["source"]);
            Assert.Equal(originalEvent.Metadata["environment"], deserializedEvent.Metadata["environment"]);
        }
    }

    [Fact]
    public void JsonEventSerializer_RoundTrip_StateChangedEvent_ShouldPreserveAllData()
    {
        // Arrange
        var originalEvent = StateChangedEvent.Create(
            "state-stream-456",
            3,
            new StateChangeEventData
            {
                EntityType = "User",
                EntityId = "user-123",
                ChangeType = "Created",
                StateData = "{\"name\":\"John Doe\",\"email\":\"john@example.com\"}",
                PreviousStateData = null,
                EntityVersion = 1
            });

        // Act
        var json = _serializer.Serialize(originalEvent);
        var deserializedEvent = _serializer.Deserialize<StateChangedEvent>(json);

        // Assert
        Assert.Equal(originalEvent.EventId, deserializedEvent.EventId);
        Assert.Equal(originalEvent.StreamId, deserializedEvent.StreamId);
        Assert.Equal(originalEvent.Version, deserializedEvent.Version);
        Assert.Equal(originalEvent.Data.EntityType, deserializedEvent.Data.EntityType);
        Assert.Equal(originalEvent.Data.EntityId, deserializedEvent.Data.EntityId);
        Assert.Equal(originalEvent.Data.ChangeType, deserializedEvent.Data.ChangeType);
        Assert.Equal(originalEvent.Data.StateData, deserializedEvent.Data.StateData);
        Assert.Equal(originalEvent.Data.EntityVersion, deserializedEvent.Data.EntityVersion);
    }

    #endregion
}