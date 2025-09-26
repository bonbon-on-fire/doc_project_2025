using AIChat.Orleans.Contracts;
using NUnit.Framework;

namespace AIChat.Orleans.Tests.Phase1;

/// <summary>
/// Unit tests for ChatGrain exception types.
/// </summary>
[TestFixture]
public class ChatGrainExceptionTests
{
    [Test]
    public void ChatGrainExceptionShouldIncludeCorrelationId()
    {
        // Arrange & Act
        var exception = new ChatGrainException("Test error", "chat123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.ChatId, Is.EqualTo("chat123"));
            Assert.That(exception.CorrelationId, Is.Not.Null);
            Assert.That(exception.CorrelationId, Is.Not.Empty);
            Assert.That(Guid.TryParse(exception.CorrelationId, out _), Is.True);
        });
    }

    [Test]
    public void ChatNotFoundExceptionShouldIncludeChatId()
    {
        // Arrange & Act
        var exception = new ChatNotFoundException("chat123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.ChatId, Is.EqualTo("chat123"));
            Assert.That(exception.Message, Does.Contain("chat123"));
            Assert.That(exception.Message, Does.Contain("not found"));
        });
    }

    [Test]
    public void ChatAlreadyExistsExceptionShouldIncludeChatId()
    {
        // Arrange & Act
        var exception = new ChatAlreadyExistsException("chat123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.ChatId, Is.EqualTo("chat123"));
            Assert.That(exception.Message, Does.Contain("chat123"));
            Assert.That(exception.Message, Does.Contain("already exists"));
        });
    }

    [Test]
    public void ParticipantNotFoundExceptionShouldIncludeBothIds()
    {
        // Arrange & Act
        var exception = new ParticipantNotFoundException("chat123", "user456");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.ChatId, Is.EqualTo("chat123"));
            Assert.That(exception.ParticipantId, Is.EqualTo("user456"));
            Assert.That(exception.Message, Does.Contain("chat123"));
            Assert.That(exception.Message, Does.Contain("user456"));
        });
    }

    [Test]
    public void MessageNotFoundExceptionShouldIncludeBothIds()
    {
        // Arrange & Act
        var exception = new MessageNotFoundException("chat123", "msg789");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.ChatId, Is.EqualTo("chat123"));
            Assert.That(exception.MessageId, Is.EqualTo("msg789"));
            Assert.That(exception.Message, Does.Contain("chat123"));
            Assert.That(exception.Message, Does.Contain("msg789"));
        });
    }

    [Test]
    public void StreamNotFoundExceptionShouldIncludeBothIds()
    {
        // Arrange & Act
        var exception = new StreamNotFoundException("chat123", "stream456");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.ChatId, Is.EqualTo("chat123"));
            Assert.That(exception.StreamId, Is.EqualTo("stream456"));
            Assert.That(exception.Message, Does.Contain("chat123"));
            Assert.That(exception.Message, Does.Contain("stream456"));
        });
    }

    [Test]
    public void PermissionDeniedExceptionShouldIncludeAllDetails()
    {
        // Arrange & Act
        var exception = new PermissionDeniedException("chat123", "user456", "DeleteMessage");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.ChatId, Is.EqualTo("chat123"));
            Assert.That(exception.ParticipantId, Is.EqualTo("user456"));
            Assert.That(exception.Action, Is.EqualTo("DeleteMessage"));
            Assert.That(exception.Message, Does.Contain("chat123"));
            Assert.That(exception.Message, Does.Contain("user456"));
            Assert.That(exception.Message, Does.Contain("DeleteMessage"));
        });
    }

    [Test]
    public void ChatArchivedExceptionShouldIncludeChatId()
    {
        // Arrange & Act
        var exception = new ChatArchivedException("chat123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.ChatId, Is.EqualTo("chat123"));
            Assert.That(exception.Message, Does.Contain("chat123"));
            Assert.That(exception.Message, Does.Contain("archived"));
        });
    }

    [Test]
    public void InvalidChatStateExceptionShouldIncludeReason()
    {
        // Arrange & Act
        var exception = new InvalidChatStateException("chat123", "Missing required participants");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.ChatId, Is.EqualTo("chat123"));
            Assert.That(exception.Reason, Is.EqualTo("Missing required participants"));
            Assert.That(exception.Message, Does.Contain("chat123"));
            Assert.That(exception.Message, Does.Contain("Missing required participants"));
        });
    }

    [Test]
    public void ChatGrainExceptionWithInnerExceptionShouldPreserveIt()
    {
        // Arrange
        var inner = new InvalidOperationException("Inner error");

        // Act
        var exception = new ChatGrainException("Outer error", inner, "chat123");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception.InnerException, Is.EqualTo(inner));
            Assert.That(exception.ChatId, Is.EqualTo("chat123"));
            Assert.That(exception.Message, Is.EqualTo("Outer error"));
        });
    }

    [Test]
    public void AllExceptionsShouldInheritFromChatGrainException()
    {
        // Arrange
        var exceptionTypes = new[]
        {
            typeof(ChatNotFoundException),
            typeof(ChatAlreadyExistsException),
            typeof(ParticipantNotFoundException),
            typeof(MessageNotFoundException),
            typeof(StreamNotFoundException),
            typeof(PermissionDeniedException),
            typeof(ChatArchivedException),
            typeof(InvalidChatStateException)
        };

        // Assert
        foreach (var exceptionType in exceptionTypes)
        {
            Assert.That(typeof(ChatGrainException).IsAssignableFrom(exceptionType), Is.True,
                $"{exceptionType.Name} should inherit from ChatGrainException");
        }
    }
}