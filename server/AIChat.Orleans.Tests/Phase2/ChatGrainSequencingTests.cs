using System.Text.Json;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Tests.TestUtilities.Infrastructure;
using NUnit.Framework;

namespace AIChat.Orleans.Tests.Phase2;

/// <summary>
/// Comprehensive tests for ChatGrain message sequencing functionality.
/// Tests sequence number assignment, order preservation, concurrent access handling, and recovery logic.
/// </summary>
[TestFixture]
public class ChatGrainSequencingTests
{
    private TestClusterManager? _clusterManager;
    private IChatGrain? _chatGrain;
    private string _testChatId = null!;

    [SetUp]
    public async Task Setup()
    {
        _clusterManager = new TestClusterManager();
        await _clusterManager.InitializeAsync();

        _testChatId = $"test-sequencing-chat-{Guid.NewGuid()}";
        _chatGrain = _clusterManager.Client.GetGrain<IChatGrain>(_testChatId);

        // Initialize chat for testing
        var initRequest = new ChatInitRequest
        {
            ChatId = _testChatId,
            Title = "Sequencing Test Chat",
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow,
            ChatType = "test",
            InitialParticipants = [
                new() { ParticipantId = "test-user", DisplayName = "Test User", Role = ParticipantRole.Owner }
            ]
        };

        await _chatGrain!.InitializeAsync(initRequest);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_clusterManager != null)
        {
            await _clusterManager.DisposeAsync();
        }
    }

    [Test]
    [Description("Tests that sequence numbers are assigned correctly to messages")]
    public async Task ProcessMessage_ShouldAssignSequenceNumbers()
    {
        // Arrange
        var message1 = new ChatMessage
        {
            UserId = "test-user",
            Content = "First message",
            Role = "user"
        };

        var message2 = new ChatMessage
        {
            UserId = "test-user",
            Content = "Second message",
            Role = "user"
        };

        // Act
        var result1 = await _chatGrain!.ProcessMessageAsync(message1);
        var result2 = await _chatGrain.ProcessMessageAsync(message2);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result1.Success, Is.True);
            Assert.That(result2.Success, Is.True);
            Assert.That(result1.Message, Is.Not.Null);
            Assert.That(result2.Message, Is.Not.Null);
        });

        // Verify sequence numbers in metadata
        var sequence1 = GetSequenceNumberFromMessage(result1.Message!);
        var sequence2 = GetSequenceNumberFromMessage(result2.Message!);

        Assert.Multiple(() =>
        {
            Assert.That(sequence1, Is.EqualTo(1), "First message should have sequence number 1");
            Assert.That(sequence2, Is.EqualTo(2), "Second message should have sequence number 2");
        });
    }

    [Test]
    [Description("Tests message processing order with sequential messages")]
    public async Task ProcessMessage_SequentialMessages_ShouldMaintainOrder()
    {
        // Arrange
        var messages = new List<ChatMessage>();
        const int messageCount = 5;

        for (int i = 0; i < messageCount; i++)
        {
            messages.Add(new ChatMessage
            {
                UserId = "test-user",
                Content = $"Message {i + 1}",
                Role = "user"
            });
        }

        // Act
        var results = new List<MessageResult>();
        foreach (var message in messages)
        {
            var result = await _chatGrain!.ProcessMessageAsync(message);
            results.Add(result);
        }

        // Assert
        Assert.That(results.All(r => r.Success), Is.True, "All messages should be processed successfully");

        // Verify sequence numbers are consecutive
        for (int i = 0; i < results.Count; i++)
        {
            var sequenceNumber = GetSequenceNumberFromMessage(results[i].Message!);
            Assert.That(sequenceNumber, Is.EqualTo(i + 1), $"Message {i + 1} should have sequence number {i + 1}");
        }

        // Verify messages are in chat history in correct order
        var history = await _chatGrain!.GetHistoryAsync();
        Assert.That(history, Has.Count.EqualTo(messageCount));

        for (int i = 0; i < history.Count; i++)
        {
            Assert.That(history[i].Content, Is.EqualTo($"Message {i + 1}"));
        }
    }

    [Test]
    [Description("Tests concurrent message processing to verify thread safety")]
    public async Task ProcessMessage_ConcurrentMessages_ShouldMaintainThreadSafety()
    {
        // Arrange
        const int concurrentMessages = 10;
        var tasks = new List<Task<MessageResult>>();

        // Act - Send messages concurrently
        for (int i = 0; i < concurrentMessages; i++)
        {
            var messageIndex = i; // Capture loop variable
            var task = Task.Run(async () =>
            {
                var message = new ChatMessage
                {
                    UserId = "test-user",
                    Content = $"Concurrent message {messageIndex}",
                    Role = "user"
                };
                return await _chatGrain!.ProcessMessageAsync(message);
            });
            tasks.Add(task);
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.That(results.All(r => r.Success), Is.True, "All concurrent messages should be processed successfully");

        // Verify all sequence numbers are unique and consecutive
        var sequenceNumbers = results
            .Select(r => GetSequenceNumberFromMessage(r.Message!))
            .OrderBy(s => s)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sequenceNumbers, Has.Count.EqualTo(concurrentMessages));
            Assert.That(sequenceNumbers.Distinct().Count(), Is.EqualTo(concurrentMessages), "All sequence numbers should be unique");
            Assert.That(sequenceNumbers.First(), Is.EqualTo(1), "First sequence number should be 1");
            Assert.That(sequenceNumbers.Last(), Is.EqualTo(concurrentMessages), $"Last sequence number should be {concurrentMessages}");
        });

        // Verify sequence numbers are consecutive
        for (int i = 0; i < sequenceNumbers.Count; i++)
        {
            Assert.That(sequenceNumbers[i], Is.EqualTo(i + 1), $"Sequence number at position {i} should be {i + 1}");
        }
    }

    [Test]
    [Description("Tests system message processing maintains sequence order")]
    public async Task SendSystemMessage_ShouldMaintainSequenceOrder()
    {
        // Arrange & Act
        var userMessage = await _chatGrain!.ProcessMessageAsync(new ChatMessage
        {
            UserId = "test-user",
            Content = "User message",
            Role = "user"
        });

        var systemMessage = await _chatGrain.SendSystemMessageAsync("System message");

        var userMessage2 = await _chatGrain.ProcessMessageAsync(new ChatMessage
        {
            UserId = "test-user",
            Content = "Second user message",
            Role = "user"
        });

        // Assert
        var userSeq1 = GetSequenceNumberFromMessage(userMessage.Message!);
        var systemSeq = GetSequenceNumberFromMessage(systemMessage);
        var userSeq2 = GetSequenceNumberFromMessage(userMessage2.Message!);

        Assert.Multiple(() =>
        {
            Assert.That(userSeq1, Is.EqualTo(1));
            Assert.That(systemSeq, Is.EqualTo(2));
            Assert.That(userSeq2, Is.EqualTo(3));
        });
    }

    [Test]
    [Description("Tests message editing preserves sequence information")]
    public async Task EditMessage_ShouldPreserveSequenceNumber()
    {
        // Arrange
        var message = await _chatGrain!.ProcessMessageAsync(new ChatMessage
        {
            UserId = "test-user",
            Content = "Original content",
            Role = "user"
        });

        var originalSequence = GetSequenceNumberFromMessage(message.Message!);

        // Act
        var editedMessage = await _chatGrain.EditMessageAsync(message.Message!.Id, "Edited content");

        // Assert
        var editedSequence = GetSequenceNumberFromMessage(editedMessage);
        Assert.Multiple(() =>
        {
            Assert.That(editedSequence, Is.EqualTo(originalSequence), "Sequence number should be preserved after editing");
            Assert.That(editedMessage.Content, Is.EqualTo("Edited content"));
        });
    }

    [Test]
    [Description("Tests message deletion maintains sequence integrity")]
    public async Task DeleteMessage_ShouldMaintainSequenceIntegrity()
    {
        // Arrange
        var message1 = await _chatGrain!.ProcessMessageAsync(new ChatMessage
        {
            UserId = "test-user",
            Content = "Message 1",
            Role = "user"
        });

        var message2 = await _chatGrain.ProcessMessageAsync(new ChatMessage
        {
            UserId = "test-user",
            Content = "Message 2",
            Role = "user"
        });

        var message3 = await _chatGrain.ProcessMessageAsync(new ChatMessage
        {
            UserId = "test-user",
            Content = "Message 3",
            Role = "user"
        });

        // Act
        var deleteResult = await _chatGrain.DeleteMessageAsync(message2.Message!.Id);

        // Assert
        Assert.That(deleteResult, Is.True);

        // Verify remaining messages still have correct sequence numbers
        var history = await _chatGrain.GetHistoryAsync();
        Assert.That(history, Has.Count.EqualTo(2));

        var seq1 = GetSequenceNumberFromMessage(history.First(m => m.Content == "Message 1"));
        var seq3 = GetSequenceNumberFromMessage(history.First(m => m.Content == "Message 3"));

        Assert.Multiple(() =>
        {
            Assert.That(seq1, Is.EqualTo(1));
            Assert.That(seq3, Is.EqualTo(3)); // Sequence number should remain unchanged
        });
    }

    [Test]
    [Description("Tests sequence processing configuration is applied")]
    public async Task SequenceProcessing_ShouldRespectConfiguration()
    {
        // This test verifies that the sequence processing configuration is properly initialized
        // and accessible. The actual behavior testing would require more complex scenarios
        // that simulate out-of-order messages, which is challenging in a unit test environment.

        // Arrange & Act
        var health = await _chatGrain!.CheckHealthAsync();

        // Assert
        Assert.That(health.IsHealthy, Is.True, "Chat grain should be healthy with sequence processing enabled");

        // Process a message to ensure sequence tracking is working
        var message = await _chatGrain.ProcessMessageAsync(new ChatMessage
        {
            UserId = "test-user",
            Content = "Configuration test message",
            Role = "user"
        });

        Assert.Multiple(() =>
        {
            Assert.That(message.Success, Is.True);
            Assert.That(GetSequenceNumberFromMessage(message.Message!), Is.EqualTo(1));
        });
    }

    [Test]
    [Description("Tests high-volume message processing maintains sequence integrity")]
    public async Task ProcessMessage_HighVolume_ShouldMaintainSequenceIntegrity()
    {
        // Arrange
        const int messageCount = 100;
        var messages = new List<ChatMessage>();

        for (int i = 0; i < messageCount; i++)
        {
            messages.Add(new ChatMessage
            {
                UserId = "test-user",
                Content = $"High volume message {i + 1}",
                Role = "user"
            });
        }

        // Act
        var results = new List<MessageResult>();
        foreach (var message in messages)
        {
            var result = await _chatGrain!.ProcessMessageAsync(message);
            results.Add(result);
        }

        // Assert
        Assert.That(results.All(r => r.Success), Is.True, "All high-volume messages should be processed successfully");

        // Verify sequence numbers are consecutive
        var sequenceNumbers = results
            .Select(r => GetSequenceNumberFromMessage(r.Message!))
            .ToList();

        for (int i = 0; i < sequenceNumbers.Count; i++)
        {
            Assert.That(sequenceNumbers[i], Is.EqualTo(i + 1), $"Message {i + 1} should have sequence number {i + 1}");
        }

        // Verify final state
        var finalState = await _chatGrain!.GetStateAsync();
        Assert.That(finalState.MessageCount, Is.EqualTo(messageCount));
    }

    [Test]
    [Description("Tests message acknowledgment works with sequenced messages")]
    public async Task AcknowledgeMessage_WithSequencing_ShouldWork()
    {
        // Arrange
        var message = await _chatGrain!.ProcessMessageAsync(new ChatMessage
        {
            UserId = "test-user",
            Content = "Message to acknowledge",
            Role = "user"
        });

        // Act
        await _chatGrain.AcknowledgeMessageAsync(message.Message!.Id, "test-user");

        // Assert
        var status = await _chatGrain.GetMessageStatusAsync(message.Message!.Id);
        Assert.That(status.AcknowledgedBy, Contains.Item("test-user"));
    }

    /// <summary>
    /// Helper method to extract sequence number from message metadata.
    /// </summary>
    private static long GetSequenceNumberFromMessage(ChatMessage message)
    {
        if (string.IsNullOrEmpty(message.Metadata))
        {
            return 0;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(message.Metadata) ?? [];
            if (metadata.TryGetValue("SequenceNumber", out var sequenceNumberObj))
            {
                return Convert.ToInt64(sequenceNumberObj, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch (JsonException)
        {
            // Return 0 if parsing fails
        }

        return 0;
    }
}