using System.Text.Json;
using WhatsAppWaha.Core.Models;

namespace WhatsAppWaha.Core.Tests.Models;

public class NtfyModelsTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    #region NtfyMessage Tests

    [Fact]
    public void NtfyMessage_Deserialize_ValidJson_ShouldDeserializeCorrectly()
    {
        // Arrange
        var json = """
            {
                "id": "msg123",
                "time": 1640995200,
                "expires": 1641081600,
                "topic": "test-topic",
                "message": "Hello World",
                "title": "Test Title",
                "priority": 3,
                "tags": ["test", "example"],
                "click": "https://example.com",
                "event": "message"
            }
            """;

        // Act
        var message = JsonSerializer.Deserialize<NtfyMessage>(json, _jsonOptions);

        // Assert
        Assert.NotNull(message);
        Assert.Equal("msg123", message.Id);
        Assert.Equal(1640995200, message.Timestamp);
        Assert.Equal(1641081600, message.Expires);
        Assert.Equal("test-topic", message.Topic);
        Assert.Equal("Hello World", message.Message);
        Assert.Equal("Test Title", message.Title);
        Assert.Equal(3, message.Priority);
        Assert.Equal(2, message.Tags?.Length);
        Assert.Equal("test", message.Tags?[0]);
        Assert.Equal("example", message.Tags?[1]);
        Assert.Equal("https://example.com", message.ClickUrl);
        Assert.Equal("message", message.Event);
    }

    [Fact]
    public void NtfyMessage_DateTime_ShouldConvertTimestampCorrectly()
    {
        // Arrange
        var message = new NtfyMessage { Timestamp = 1640995200 }; // 2022-01-01 00:00:00 UTC

        // Act
        var dateTime = message.DateTime;

        // Assert
        Assert.Equal(new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc), dateTime);
    }

    [Fact]
    public void NtfyMessage_ExpirationDateTime_ShouldConvertExpiresCorrectly()
    {
        // Arrange
        var message = new NtfyMessage { Expires = 1641081600 }; // 2022-01-02 00:00:00 UTC

        // Act
        var expiration = message.ExpirationDateTime;

        // Assert
        Assert.NotNull(expiration);
        Assert.Equal(new DateTime(2022, 1, 2, 0, 0, 0, DateTimeKind.Utc), expiration.Value);
    }

    [Fact]
    public void NtfyMessage_ExpirationDateTime_NullExpires_ShouldReturnNull()
    {
        // Arrange
        var message = new NtfyMessage { Expires = null };

        // Act
        var expiration = message.ExpirationDateTime;

        // Assert
        Assert.Null(expiration);
    }

    [Theory]
    [InlineData("msg123", "Hello", "message", true)]
    [InlineData("msg123", "Hello", null, true)]
    [InlineData("msg123", "", "message", false)]
    [InlineData("", "Hello", "message", false)]
    [InlineData("msg123", "Hello", "keepalive", false)]
    [InlineData(null, "Hello", "message", false)]
    public void NtfyMessage_IsValidMessage_ShouldReturnCorrectValue(string? id, string message, string? eventType, bool expected)
    {
        // Arrange
        var ntfyMessage = new NtfyMessage
        {
            Id = id ?? string.Empty,
            Message = message,
            Event = eventType
        };

        // Act
        var isValid = ntfyMessage.IsValidMessage;

        // Assert
        Assert.Equal(expected, isValid);
    }

    [Fact]
    public void NtfyAttachment_Deserialize_ValidJson_ShouldDeserializeCorrectly()
    {
        // Arrange
        var json = """
            {
                "name": "test.pdf",
                "type": "application/pdf",
                "size": 12345,
                "url": "https://example.com/test.pdf"
            }
            """;

        // Act
        var attachment = JsonSerializer.Deserialize<NtfyAttachment>(json, _jsonOptions);

        // Assert
        Assert.NotNull(attachment);
        Assert.Equal("test.pdf", attachment.Name);
        Assert.Equal("application/pdf", attachment.Type);
        Assert.Equal(12345, attachment.Size);
        Assert.Equal("https://example.com/test.pdf", attachment.Url);
    }

    [Fact]
    public void NtfyAction_Deserialize_ValidJson_ShouldDeserializeCorrectly()
    {
        // Arrange
        var json = """
            {
                "id": "action1",
                "action": "http",
                "label": "Click me",
                "url": "https://example.com/webhook",
                "method": "POST",
                "headers": {"Content-Type": "application/json"},
                "body": "{\"test\": true}"
            }
            """;

        // Act
        var action = JsonSerializer.Deserialize<NtfyAction>(json, _jsonOptions);

        // Assert
        Assert.NotNull(action);
        Assert.Equal("action1", action.Id);
        Assert.Equal("http", action.Action);
        Assert.Equal("Click me", action.Label);
        Assert.Equal("https://example.com/webhook", action.Url);
        Assert.Equal("POST", action.Method);
        Assert.NotNull(action.Headers);
        Assert.Equal("application/json", action.Headers["Content-Type"]);
        Assert.Equal("{\"test\": true}", action.Body);
    }

    #endregion

    #region NtfyPollingResponse Tests

    [Fact]
    public void NtfyPollingResponse_CreateSuccess_ShouldCreateValidResponse()
    {
        // Arrange
        var messages = new List<NtfyMessage>
        {
            new() { Id = "1", Message = "Test 1", Topic = "test" },
            new() { Id = "2", Message = "Test 2", Topic = "test" }
        };
        const string topic = "test-topic";
        const int duplicateCount = 1;

        // Act
        var response = NtfyPollingResponse.CreateSuccess(messages, topic, duplicateCount);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsSuccess);
        Assert.Equal(messages, response.Messages);
        Assert.Equal(topic, response.Topic);
        Assert.Equal(duplicateCount, response.DuplicateCount);
        Assert.Equal(1, response.NewMessageCount); // 2 messages - 1 duplicate
        Assert.Null(response.ErrorMessage);
    }

    [Fact]
    public void NtfyPollingResponse_CreateFailure_ShouldCreateFailedResponse()
    {
        // Arrange
        const string topic = "test-topic";
        const string errorMessage = "Connection failed";

        // Act
        var response = NtfyPollingResponse.CreateFailure(topic, errorMessage);

        // Assert
        Assert.NotNull(response);
        Assert.False(response.IsSuccess);
        Assert.Equal(topic, response.Topic);
        Assert.Equal(errorMessage, response.ErrorMessage);
        Assert.Empty(response.Messages);
        Assert.Equal(0, response.DuplicateCount);
        Assert.Equal(0, response.NewMessageCount);
    }

    [Fact]
    public void NtfyPollingResponse_ValidNewMessages_ShouldFilterCorrectly()
    {
        // Arrange
        var messages = new List<NtfyMessage>
        {
            new() { Id = "1", Message = "Valid", Event = "message" },
            new() { Id = "2", Message = "", Event = "message" }, // Invalid: empty message
            new() { Id = "3", Message = "Keepalive", Event = "keepalive" }, // Invalid: keepalive
            new() { Id = "4", Message = "Valid 2", Event = "message" }
        };
        var response = NtfyPollingResponse.CreateSuccess(messages, "test-topic");

        // Act
        var validMessages = response.ValidNewMessages.ToList();

        // Assert
        Assert.Equal(2, validMessages.Count);
        Assert.Equal("1", validMessages[0].Id);
        Assert.Equal("4", validMessages[1].Id);
    }

    #endregion

    #region MessageProcessingContext Tests

    [Fact]
    public void MessageProcessingContext_Constructor_DefaultMaxIds_ShouldSetCorrectly()
    {
        // Act
        var context = new MessageProcessingContext();

        // Assert
        Assert.Equal(1000, context.MaxProcessedIds);
        Assert.Equal(0, context.ProcessedMessageCount);
    }

    [Fact]
    public void MessageProcessingContext_Constructor_CustomMaxIds_ShouldSetCorrectly()
    {
        // Arrange
        const int maxIds = 500;

        // Act
        var context = new MessageProcessingContext(maxIds);

        // Assert
        Assert.Equal(maxIds, context.MaxProcessedIds);
        Assert.Equal(0, context.ProcessedMessageCount);
    }

    [Fact]
    public void MessageProcessingContext_Constructor_ZeroMaxIds_ShouldThrowException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new MessageProcessingContext(0));
    }

    [Fact]
    public void MessageProcessingContext_MarkMessageAsProcessed_NewMessage_ShouldReturnTrue()
    {
        // Arrange
        var context = new MessageProcessingContext();
        const string messageId = "test-message-1";

        // Act
        var result = context.MarkMessageAsProcessed(messageId);

        // Assert
        Assert.True(result);
        Assert.Equal(1, context.ProcessedMessageCount);
        Assert.True(context.IsMessageProcessed(messageId));
    }

    [Fact]
    public void MessageProcessingContext_MarkMessageAsProcessed_DuplicateMessage_ShouldReturnFalse()
    {
        // Arrange
        var context = new MessageProcessingContext();
        const string messageId = "test-message-1";
        context.MarkMessageAsProcessed(messageId);

        // Act
        var result = context.MarkMessageAsProcessed(messageId);

        // Assert
        Assert.False(result);
        Assert.Equal(1, context.ProcessedMessageCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MessageProcessingContext_MarkMessageAsProcessed_InvalidId_ShouldReturnFalse(string? messageId)
    {
        // Arrange
        var context = new MessageProcessingContext();

        // Act
        var result = context.MarkMessageAsProcessed(messageId!);

        // Assert
        Assert.False(result);
        Assert.Equal(0, context.ProcessedMessageCount);
    }

    [Fact]
    public void MessageProcessingContext_MarkMessagesAsProcessed_MultipleMessages_ShouldProcessCorrectly()
    {
        // Arrange
        var context = new MessageProcessingContext();
        var messageIds = new[] { "msg1", "msg2", "msg3", "msg1" }; // msg1 is duplicate

        // Act
        var newlyProcessedCount = context.MarkMessagesAsProcessed(messageIds);

        // Assert
        Assert.Equal(3, newlyProcessedCount);
        Assert.Equal(3, context.ProcessedMessageCount);
        Assert.True(context.IsMessageProcessed("msg1"));
        Assert.True(context.IsMessageProcessed("msg2"));
        Assert.True(context.IsMessageProcessed("msg3"));
    }

    [Fact]
    public void MessageProcessingContext_MaxCapacity_ShouldRemoveOldestMessages()
    {
        // Arrange
        var context = new MessageProcessingContext(3); // Small capacity for testing

        // Act
        context.MarkMessageAsProcessed("msg1");
        context.MarkMessageAsProcessed("msg2");
        context.MarkMessageAsProcessed("msg3");
        context.MarkMessageAsProcessed("msg4"); // Should evict msg1

        // Assert
        Assert.Equal(3, context.ProcessedMessageCount);
        Assert.False(context.IsMessageProcessed("msg1")); // Evicted
        Assert.True(context.IsMessageProcessed("msg2"));
        Assert.True(context.IsMessageProcessed("msg3"));
        Assert.True(context.IsMessageProcessed("msg4"));
    }

    [Fact]
    public void MessageProcessingContext_FilterUnprocessedMessages_ShouldFilterCorrectly()
    {
        // Arrange
        var context = new MessageProcessingContext();
        context.MarkMessageAsProcessed("processed-1");
        
        var messages = new List<NtfyMessage>
        {
            new() { Id = "processed-1", Message = "Already processed" },
            new() { Id = "new-1", Message = "New message 1" },
            new() { Id = "new-2", Message = "New message 2" },
            new() { Id = "", Message = "Invalid ID" }
        };

        // Act
        var unprocessed = context.FilterUnprocessedMessages(messages).ToList();

        // Assert
        Assert.Equal(2, unprocessed.Count);
        Assert.Equal("new-1", unprocessed[0].Id);
        Assert.Equal("new-2", unprocessed[1].Id);
    }

    [Fact]
    public void MessageProcessingContext_Clear_ShouldRemoveAllMessages()
    {
        // Arrange
        var context = new MessageProcessingContext();
        context.MarkMessageAsProcessed("msg1");
        context.MarkMessageAsProcessed("msg2");

        // Act
        context.Clear();

        // Assert
        Assert.Equal(0, context.ProcessedMessageCount);
        Assert.False(context.IsMessageProcessed("msg1"));
        Assert.False(context.IsMessageProcessed("msg2"));
    }

    [Fact]
    public void MessageProcessingContext_GetStatistics_ShouldReturnCorrectData()
    {
        // Arrange
        var context = new MessageProcessingContext(100);
        context.MarkMessageAsProcessed("msg1");
        context.MarkMessageAsProcessed("msg2");

        // Act
        var stats = context.GetStatistics();

        // Assert
        Assert.Equal(2, stats["ProcessedMessageCount"]);
        Assert.Equal(100, stats["MaxProcessedIds"]);
        Assert.Equal(0.02, stats["MemoryUtilization"]);
        Assert.True(stats.ContainsKey("CreatedAt"));
        Assert.True(stats.ContainsKey("LastUsedAt"));
        Assert.True(stats.ContainsKey("AgeInMinutes"));
    }

    #endregion

    #region NtfyNotification Tests

    [Fact]
    public void NtfyNotification_Serialize_ShouldExcludeTopic()
    {
        // Arrange
        var notification = new NtfyNotification
        {
            Topic = "test-topic",
            Title = "Test Title",
            Message = "Test Message",
            Priority = 3,
            Tags = new[] { "test" }
        };

        // Act
        var json = JsonSerializer.Serialize(notification, _jsonOptions);

        // Assert
        Assert.DoesNotContain("topic", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("title", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("message", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("priority", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tags", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NtfyNotification_CreateSuccess_ShouldCreateCorrectNotification()
    {
        // Arrange
        const string topic = "test-topic";
        const string title = "Success Title";
        const string message = "Success Message";

        // Act
        var notification = NtfyNotification.CreateSuccess(topic, title, message);

        // Assert
        Assert.Equal(topic, notification.Topic);
        Assert.Equal(title, notification.Title);
        Assert.Equal(message, notification.Message);
        Assert.Equal(3, notification.Priority);
        Assert.Contains("white_check_mark", notification.Tags!);
        Assert.Equal("✅", notification.Icon);
    }

    [Fact]
    public void NtfyNotification_CreateError_ShouldCreateCorrectNotification()
    {
        // Arrange
        const string topic = "test-topic";
        const string title = "Error Title";
        const string message = "Error Message";

        // Act
        var notification = NtfyNotification.CreateError(topic, title, message);

        // Assert
        Assert.Equal(topic, notification.Topic);
        Assert.Equal(title, notification.Title);
        Assert.Equal(message, notification.Message);
        Assert.Equal(4, notification.Priority);
        Assert.Contains("x", notification.Tags!);
        Assert.Contains("rotating_light", notification.Tags!);
        Assert.Equal("❌", notification.Icon);
    }

    [Fact]
    public void NtfyNotification_CreateInfo_ShouldCreateCorrectNotification()
    {
        // Arrange
        const string topic = "test-topic";
        const string title = "Info Title";
        const string message = "Info Message";

        // Act
        var notification = NtfyNotification.CreateInfo(topic, title, message);

        // Assert
        Assert.Equal(topic, notification.Topic);
        Assert.Equal(title, notification.Title);
        Assert.Equal(message, notification.Message);
        Assert.Equal(2, notification.Priority);
        Assert.Contains("information_source", notification.Tags!);
        Assert.Equal("ℹ️", notification.Icon);
    }

    [Fact]
    public void NtfyNotification_CreateWarning_ShouldCreateCorrectNotification()
    {
        // Arrange
        const string topic = "test-topic";
        const string title = "Warning Title";
        const string message = "Warning Message";

        // Act
        var notification = NtfyNotification.CreateWarning(topic, title, message);

        // Assert
        Assert.Equal(topic, notification.Topic);
        Assert.Equal(title, notification.Title);
        Assert.Equal(message, notification.Message);
        Assert.Equal(3, notification.Priority);
        Assert.Contains("warning", notification.Tags!);
        Assert.Equal("⚠️", notification.Icon);
    }

    [Fact]
    public void NtfyNotification_CreateMessageProcessing_SingleMessage_ShouldCreateCorrectNotification()
    {
        // Arrange
        const string topic = "test-topic";
        const int messageCount = 1;
        const long processingTimeMs = 150;

        // Act
        var notification = NtfyNotification.CreateMessageProcessing(topic, messageCount, processingTimeMs);

        // Assert
        Assert.Equal(topic, notification.Topic);
        Assert.Equal("Message Processed", notification.Title);
        Assert.Equal("Processing completed in 150ms", notification.Message);
        Assert.Equal(2, notification.Priority);
        Assert.Contains("gear", notification.Tags!);
        Assert.Equal("⚙️", notification.Icon);
    }

    [Fact]
    public void NtfyNotification_CreateMessageProcessing_MultipleMessages_ShouldCreateCorrectNotification()
    {
        // Arrange
        const string topic = "test-topic";
        const int messageCount = 5;
        const long processingTimeMs = 0;

        // Act
        var notification = NtfyNotification.CreateMessageProcessing(topic, messageCount, processingTimeMs);

        // Assert
        Assert.Equal(topic, notification.Topic);
        Assert.Equal("5 Messages Processed", notification.Title);
        Assert.Equal("Processing completed", notification.Message);
    }

    [Fact]
    public void NtfyNotification_CreateWahaMessageSent_Success_ShouldCreateCorrectNotification()
    {
        // Arrange
        const string topic = "test-topic";
        const string phoneNumber = "+1234567890";
        const string messageText = "Hello World";
        const bool success = true;

        // Act
        var notification = NtfyNotification.CreateWahaMessageSent(topic, phoneNumber, messageText, success);

        // Assert
        Assert.Equal(topic, notification.Topic);
        Assert.Equal("Message Sent", notification.Title);
        Assert.Equal("To +1234567890: Hello World", notification.Message);
        Assert.Equal(3, notification.Priority); // Success priority
        Assert.Contains("white_check_mark", notification.Tags!);
    }

    [Fact]
    public void NtfyNotification_CreateWahaMessageSent_Failure_ShouldCreateCorrectNotification()
    {
        // Arrange
        const string topic = "test-topic";
        const string phoneNumber = "+1234567890";
        const string messageText = "Hello World";
        const bool success = false;

        // Act
        var notification = NtfyNotification.CreateWahaMessageSent(topic, phoneNumber, messageText, success);

        // Assert
        Assert.Equal(topic, notification.Topic);
        Assert.Equal("Message Send Failed", notification.Title);
        Assert.Equal("To +1234567890: Hello World", notification.Message);
        Assert.Equal(4, notification.Priority); // Error priority
        Assert.Contains("x", notification.Tags!);
    }

    [Fact]
    public void NtfyNotification_CreateWahaMessageSent_LongMessage_ShouldTruncate()
    {
        // Arrange
        const string topic = "test-topic";
        const string phoneNumber = "+1234567890";
        var longMessage = new string('A', 60); // 60 characters, should be truncated
        const bool success = true;

        // Act
        var notification = NtfyNotification.CreateWahaMessageSent(topic, phoneNumber, longMessage, success);

        // Assert
        Assert.Contains("...", notification.Message);
        Assert.True(notification.Message.Length < 100); // Should be truncated
    }

    #endregion
}