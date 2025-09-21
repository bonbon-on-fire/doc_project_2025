using System.ComponentModel.DataAnnotations;
using AIChat.Orleans.Contracts;
using NUnit.Framework;

namespace AIChat.Orleans.Tests.Phase1;

/// <summary>
/// Unit tests for chat-related models and DTOs.
/// </summary>
[TestFixture]
public class ChatModelsTests
{
    #region ChatInitRequest Tests

    [Test]
    public void ChatInitRequestShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var request = new ChatInitRequest
        {
            ChatId = "chat-1",
            Title = "Test Chat",
            CreatedBy = "user-1"
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(request.ChatType, Is.EqualTo("ai-assistant"));
            Assert.That(request.InitialParticipants, Is.Not.Null);
            Assert.That(request.InitialParticipants, Is.Empty);
            Assert.That(request.CreatedAt, Is.Not.EqualTo(default(DateTime)));
        });
    }

    [Test]
    public void ChatInitRequestShouldValidateRequiredFields()
    {
        // Arrange
        var request = new ChatInitRequest
        {
            ChatId = "chat-1",
            Title = "Test Chat",
            CreatedBy = "user-1"
        };

        var validationContext = new ValidationContext(request);
        var validationResults = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, validationContext, validationResults, true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(isValid, Is.True);
            Assert.That(validationResults, Is.Empty);
        });
    }

    [Test]
    public void ChatInitRequestShouldFailValidationWithInvalidTitle()
    {
        // Arrange
        var request = new ChatInitRequest
        {
            ChatId = "chat-1",
            Title = "", // Invalid: empty title
            CreatedBy = "user-1"
        };

        var validationContext = new ValidationContext(request);
        var validationResults = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, validationContext, validationResults, true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(isValid, Is.False);
            Assert.That(validationResults, Has.Count.GreaterThan(0));
        });
    }

    [Test]
    public void ChatInitRequestShouldEnforceMaxParticipantsRange()
    {
        // Arrange
        var request = new ChatInitRequest
        {
            ChatId = "chat-1",
            Title = "Test Chat",
            CreatedBy = "user-1",
            MaxParticipants = 1001 // Over the limit
        };

        var validationContext = new ValidationContext(request);
        var validationResults = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, validationContext, validationResults, true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(isValid, Is.False);
            Assert.That(validationResults.Any(r => r.MemberNames.Contains(nameof(ChatInitRequest.MaxParticipants))), Is.True);
        });
    }

    #endregion

    #region ChatState Tests

    [Test]
    public void ChatStateShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var state = new ChatState
        {
            ChatId = "chat-1",
            Title = "Test Chat",
            CreatedBy = "user-1"
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(state.Status, Is.EqualTo(ChatStatus.Active));
            Assert.That(state.ChatType, Is.EqualTo("ai-assistant"));
            Assert.That(state.MessageCount, Is.EqualTo(0));
            Assert.That(state.ParticipantCount, Is.EqualTo(0));
            Assert.That(state.Version, Is.EqualTo(1));
            Assert.That(state.ArchivedAt, Is.Null);
        });
    }

    [Test]
    public void ChatStateShouldTrackTimestamps()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // Act
        var state = new ChatState
        {
            ChatId = "chat-1",
            Title = "Test Chat",
            CreatedBy = "user-1",
            CreatedAt = now,
            LastActivityAt = now.AddMinutes(10)
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(state.CreatedAt, Is.EqualTo(now));
            Assert.That(state.LastActivityAt, Is.EqualTo(now.AddMinutes(10)));
            Assert.That(state.LastActivityAt, Is.GreaterThan(state.CreatedAt));
        });
    }

    #endregion

    #region MessageResult Tests

    [Test]
    public void MessageResultCreateSuccessShouldReturnSuccessResult()
    {
        // Arrange
        var message = new ChatMessage
        {
            Id = "msg-1",
            ChatId = "chat-1",
            UserId = "user-1",
            Content = "Test message"
        };

        // Act
        var result = MessageResult.CreateSuccess(message);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Is.EqualTo(message));
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.ErrorCode, Is.Null);
        });
    }

    [Test]
    public void MessageResultCreateFailureShouldReturnFailureResult()
    {
        // Arrange
        const string errorMessage = "Failed to process message";
        const string errorCode = "MSG_001";

        // Act
        var result = MessageResult.CreateFailure(errorMessage, errorCode);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.Null);
            Assert.That(result.ErrorMessage, Is.EqualTo(errorMessage));
            Assert.That(result.ErrorCode, Is.EqualTo(errorCode));
        });
    }

    #endregion

    #region ChatParticipant Tests

    [Test]
    public void ChatParticipantShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var participant = new ChatParticipant
        {
            ParticipantId = "user-1",
            DisplayName = "Test User"
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(participant.Role, Is.EqualTo(ParticipantRole.Member));
            Assert.That(participant.Status, Is.EqualTo(PresenceStatus.Online));
            Assert.That(participant.ParticipantType, Is.EqualTo("user"));
            Assert.That(participant.IsTyping, Is.False);
            Assert.That(participant.JoinedAt, Is.Not.EqualTo(default(DateTime)));
        });
    }

    [Test]
    public void ChatParticipantShouldValidateRequiredFields()
    {
        // Arrange
        var participant = new ChatParticipant
        {
            ParticipantId = "user-1",
            DisplayName = "Test User"
        };

        var validationContext = new ValidationContext(participant);
        var validationResults = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(participant, validationContext, validationResults, true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(isValid, Is.True);
            Assert.That(validationResults, Is.Empty);
        });
    }

    #endregion

    #region StreamMessage Tests

    [Test]
    public void StreamMessageShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var streamMessage = new StreamMessage
        {
            ChatId = "chat-1",
            UserId = "user-1",
            Content = "Test stream content"
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(streamMessage.Role, Is.EqualTo("user"));
            Assert.That(streamMessage.Timestamp, Is.Not.EqualTo(default(DateTime)));
            Assert.That(streamMessage.ModeId, Is.Null);
            Assert.That(streamMessage.SystemPrompt, Is.Null);
        });
    }

    [Test]
    public void StreamMessageShouldValidateRequiredFields()
    {
        // Arrange
        var streamMessage = new StreamMessage
        {
            ChatId = "chat-1",
            UserId = "user-1",
            Content = "Test content"
        };

        var validationContext = new ValidationContext(streamMessage);
        var validationResults = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(streamMessage, validationContext, validationResults, true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(isValid, Is.True);
            Assert.That(validationResults, Is.Empty);
        });
    }

    #endregion

    #region StreamHandle Tests

    [Test]
    public void StreamHandleShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var handle = new StreamHandle
        {
            StreamId = "stream-1",
            ChatId = "chat-1"
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(handle.Status, Is.EqualTo(StreamStatus.Active));
            Assert.That(handle.CreatedAt, Is.Not.EqualTo(default(DateTime)));
            Assert.That(handle.EstimatedCompletionTime, Is.Null);
        });
    }

    #endregion

    #region MessageStatus Tests

    [Test]
    public void MessageStatusShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var status = new MessageStatus
        {
            MessageId = "msg-1"
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(status.Status, Is.EqualTo(DeliveryStatus.Pending));
            Assert.That(status.AcknowledgedBy, Is.Not.Null);
            Assert.That(status.AcknowledgedBy, Is.Empty);
            Assert.That(status.PendingDelivery, Is.Not.Null);
            Assert.That(status.PendingDelivery, Is.Empty);
            Assert.That(status.FullyDeliveredAt, Is.Null);
        });
    }

    [Test]
    public void MessageStatusShouldTrackAcknowledgements()
    {
        // Arrange
        var status = new MessageStatus
        {
            MessageId = "msg-1"
        };

        // Act
        status.AcknowledgedBy.Add("user-1");
        status.AcknowledgedBy.Add("user-2");
        status.PendingDelivery.Add("user-3");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(status.AcknowledgedBy, Has.Count.EqualTo(2));
            Assert.That(status.PendingDelivery, Has.Count.EqualTo(1));
            Assert.That(status.AcknowledgedBy, Does.Contain("user-1"));
            Assert.That(status.AcknowledgedBy, Does.Contain("user-2"));
            Assert.That(status.PendingDelivery, Does.Contain("user-3"));
        });
    }

    #endregion

    #region Enum Tests

    [Test]
    public void ChatStatusShouldHaveExpectedValues()
    {
        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(Enum.GetValues<ChatStatus>(), Does.Contain(ChatStatus.Active));
            Assert.That(Enum.GetValues<ChatStatus>(), Does.Contain(ChatStatus.Archived));
            Assert.That(Enum.GetValues<ChatStatus>(), Does.Contain(ChatStatus.Suspended));
            Assert.That(Enum.GetValues<ChatStatus>(), Does.Contain(ChatStatus.Initializing));
            Assert.That(Enum.GetValues<ChatStatus>(), Does.Contain(ChatStatus.Deleted));
        });
    }

    [Test]
    public void ParticipantRoleShouldHaveHierarchicalValues()
    {
        // Assert - verify roles are in ascending order of permissions
        Assert.Multiple(() =>
        {
            Assert.That((int)ParticipantRole.Guest, Is.EqualTo(4));
            Assert.That((int)ParticipantRole.Member, Is.EqualTo(0));
            Assert.That((int)ParticipantRole.Moderator, Is.EqualTo(1));
            Assert.That((int)ParticipantRole.Admin, Is.EqualTo(2));
            Assert.That((int)ParticipantRole.Owner, Is.EqualTo(3));
        });
    }

    [Test]
    public void ChatActionShouldCoverAllPermissions()
    {
        // Assert
        var actions = Enum.GetValues<ChatAction>();

        Assert.Multiple(() =>
        {
            Assert.That(actions, Does.Contain(ChatAction.SendMessage));
            Assert.That(actions, Does.Contain(ChatAction.EditOwnMessage));
            Assert.That(actions, Does.Contain(ChatAction.DeleteOwnMessage));
            Assert.That(actions, Does.Contain(ChatAction.AddParticipant));
            Assert.That(actions, Does.Contain(ChatAction.RemoveParticipant));
            Assert.That(actions, Does.Contain(ChatAction.ArchiveChat));
            Assert.That(actions, Has.Length.EqualTo(11)); // Ensure we have all expected actions
        });
    }

    #endregion

    #region ParticipantUpdate Tests

    [Test]
    public void ParticipantUpdateShouldAllowPartialUpdates()
    {
        // Arrange & Act
        var update = new ParticipantUpdate
        {
            ParticipantId = "user-1",
            DisplayName = "New Name"
            // Role and Metadata remain null for partial update
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(update.Role, Is.Null);
            Assert.That(update.Metadata, Is.Null);
            Assert.That(update.DisplayName, Is.EqualTo("New Name"));
            Assert.That(update.UpdatedAt, Is.Not.EqualTo(default(DateTime)));
        });
    }

    #endregion

    #region StreamSubscriptionHandle Tests

    [Test]
    public void StreamSubscriptionHandleShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var handle = new StreamSubscriptionHandle
        {
            SubscriptionId = "sub-1",
            StreamId = Guid.NewGuid()
        };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(handle.Namespace, Is.EqualTo("chat"));
            Assert.That(handle.IsActive, Is.True);
            Assert.That(handle.CreatedAt, Is.Not.EqualTo(default(DateTime)));
        });
    }

    #endregion
}