using AIChat.Orleans.Contracts;
using AIChat.Orleans.Grains;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Orleans.TestingHost;
using Orleans.Hosting;

namespace AIChat.Orleans.Tests.Phase3;

/// <summary>
/// Phase 3 tests for UserGrain message buffering functionality.
/// Tests message buffering, TTL, overflow handling, and delivery.
/// </summary>
[TestFixture]
public class UserGrainBufferTests
{
    private TestCluster? _cluster;
    private IUserGrain? _grain;
    private const string TestUserId = "test-user-buffer";
    private const string TestChatId = "test-chat-buffer";

    [SetUp]
    public async Task Setup()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        
        _cluster = builder.Build();
        await _cluster.DeployAsync();
        
        _grain = _cluster.GrainFactory.GetGrain<IUserGrain>(TestUserId);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }
    }

    [Test]
    public async Task BufferMessageAsync_ShouldCreateBufferedMessage_WhenValidMessage()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        var message = CreateTestMessage();

        // Act
        var messageId = await _grain.BufferMessageAsync(message, BufferPriority.Normal);

        // Assert
        Assert.That(messageId, Is.Not.Null.And.Not.Empty);
        Assert.That(messageId, Does.StartWith($"{TestChatId}_"));

        // Verify message is buffered
        var bufferedMessage = await _grain.GetBufferedMessageAsync(messageId);
        Assert.That(bufferedMessage, Is.Not.Null);
        Assert.That(bufferedMessage!.MessageId, Is.EqualTo(messageId));
        Assert.That(bufferedMessage.ChatId, Is.EqualTo(TestChatId));
        Assert.That(bufferedMessage.Message.Content, Is.EqualTo(message.Content));
        Assert.That(bufferedMessage.Priority, Is.EqualTo(BufferPriority.Normal));
    }

    [Test]
    public async Task BufferStreamChunkAsync_ShouldCreateBufferedStreamChunk_WhenValidChunk()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        var chunk = CreateTestStreamChunk();

        // Act
        var messageId = await _grain.BufferStreamChunkAsync(chunk, BufferPriority.High);

        // Assert
        Assert.That(messageId, Is.Not.Null.And.Not.Empty);

        // Verify chunk is buffered as a message
        var bufferedMessage = await _grain.GetBufferedMessageAsync(messageId);
        Assert.That(bufferedMessage, Is.Not.Null);
        Assert.That(bufferedMessage!.Message.IsStreaming, Is.True);
        Assert.That(bufferedMessage.Priority, Is.EqualTo(BufferPriority.High));
    }

    [Test]
    public async Task GetBufferedMessagesAsync_ShouldReturnFilteredMessages_WhenHighPriorityFilter()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        var normalMessage = CreateTestMessage("Normal priority message");
        var highMessage = CreateTestMessage("High priority message");

        await _grain.BufferMessageAsync(normalMessage, BufferPriority.Normal);
        await _grain.BufferMessageAsync(highMessage, BufferPriority.High);

        // Act
        var highPriorityMessages = await _grain.GetBufferedMessagesAsync(TestChatId, null, true);
        var allMessages = await _grain.GetBufferedMessagesAsync(TestChatId);

        // Assert
        Assert.That(highPriorityMessages.Count(), Is.EqualTo(1));
        Assert.That(allMessages.Count(), Is.EqualTo(2));
        Assert.That(highPriorityMessages.First().Priority, Is.EqualTo(BufferPriority.High));
    }

    [Test]
    public async Task GetBufferedMessagesAsync_ShouldRespectLimit_WhenLimitSpecified()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        
        // Buffer 3 messages
        for (int i = 0; i < 3; i++)
        {
            var message = CreateTestMessage($"Test message {i}");
            await _grain.BufferMessageAsync(message, BufferPriority.Normal);
        }

        // Act
        var limitedMessages = await _grain.GetBufferedMessagesAsync(TestChatId, 2);
        var allMessages = await _grain.GetBufferedMessagesAsync(TestChatId);

        // Assert
        Assert.That(limitedMessages.Count(), Is.EqualTo(2));
        Assert.That(allMessages.Count(), Is.EqualTo(3));
    }

    [Test]
    public async Task RemoveBufferedMessageAsync_ShouldRemoveMessage_WhenMessageExists()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        var message = CreateTestMessage();
        var messageId = await _grain.BufferMessageAsync(message);

        // Act
        var removed = await _grain.RemoveBufferedMessageAsync(messageId);

        // Assert
        Assert.That(removed, Is.True);

        // Verify message is gone
        var bufferedMessage = await _grain.GetBufferedMessageAsync(messageId);
        Assert.That(bufferedMessage, Is.Null);
    }

    [Test]
    public async Task RemoveBufferedMessageAsync_ShouldReturnFalse_WhenMessageDoesNotExist()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        var nonExistentId = "non-existent-message-id";

        // Act
        var removed = await _grain.RemoveBufferedMessageAsync(nonExistentId);

        // Assert
        Assert.That(removed, Is.False);
    }

    [Test]
    public async Task MarkMessageDeliveredAsync_ShouldRemoveMessageAndUpdateMetrics()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        var message = CreateTestMessage();
        var messageId = await _grain.BufferMessageAsync(message);

        // Act
        var marked = await _grain.MarkMessageDeliveredAsync(messageId);

        // Assert
        Assert.That(marked, Is.True);

        // Verify message is removed
        var bufferedMessage = await _grain.GetBufferedMessageAsync(messageId);
        Assert.That(bufferedMessage, Is.Null);
    }

    [Test]
    public async Task RecordDeliveryAttemptAsync_ShouldUpdateAttemptInfo_WhenMessageExists()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        var message = CreateTestMessage();
        var messageId = await _grain.BufferMessageAsync(message);
        var error = "Connection timeout";

        // Act
        var recorded = await _grain.RecordDeliveryAttemptAsync(messageId, error);

        // Assert
        Assert.That(recorded, Is.True);

        // Verify attempt was recorded
        var bufferedMessage = await _grain.GetBufferedMessageAsync(messageId);
        Assert.That(bufferedMessage, Is.Not.Null);
        Assert.That(bufferedMessage!.DeliveryAttempts, Is.EqualTo(1));
        Assert.That(bufferedMessage.LastDeliveryError, Is.EqualTo(error));
        Assert.That(bufferedMessage.LastDeliveryAttempt, Is.Not.Null);
    }

    [Test]
    public async Task GetChatBufferAsync_ShouldReturnBufferInfo_WhenBufferExists()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        var message = CreateTestMessage();
        await _grain.BufferMessageAsync(message);

        // Act
        var buffer = await _grain.GetChatBufferAsync(TestChatId);

        // Assert
        Assert.That(buffer, Is.Not.Null);
        Assert.That(buffer!.ChatId, Is.EqualTo(TestChatId));
        Assert.That(buffer.Messages.Count, Is.EqualTo(1));
        Assert.That(buffer.MaxSize, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetBufferSummaryAsync_ShouldReturnSummaryInfo()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        
        // Buffer some messages
        var normalMessage = CreateTestMessage("Normal message");
        var highMessage = CreateTestMessage("High priority message");
        await _grain.BufferMessageAsync(normalMessage, BufferPriority.Normal);
        await _grain.BufferMessageAsync(highMessage, BufferPriority.High);

        // Act
        var summary = await _grain.GetBufferSummaryAsync();

        // Assert
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary.ContainsKey(TestChatId), Is.True);
        
        var chatSummary = summary[TestChatId];
        Assert.That(chatSummary.CurrentMessageCount, Is.EqualTo(2));
        Assert.That(chatSummary.HighPriorityCount, Is.EqualTo(1));
        Assert.That(chatSummary.UtilizationPercent, Is.GreaterThan(0));
    }

    [Test]
    public async Task ClearChatBufferAsync_ShouldRemoveAllMessagesFromChat()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        
        // Buffer multiple messages
        for (int i = 0; i < 3; i++)
        {
            var message = CreateTestMessage($"Message {i}");
            await _grain.BufferMessageAsync(message);
        }

        // Act
        var clearedCount = await _grain.ClearChatBufferAsync(TestChatId);

        // Assert
        Assert.That(clearedCount, Is.EqualTo(3));

        // Verify buffer is empty
        var messages = await _grain.GetBufferedMessagesAsync(TestChatId);
        Assert.That(messages.Count(), Is.EqualTo(0));

        var buffer = await _grain.GetChatBufferAsync(TestChatId);
        Assert.That(buffer, Is.Null);
    }

    [Test]
    public async Task BufferMessageAsync_ShouldThrowArgumentException_WhenMessageIsNull()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _grain.BufferMessageAsync(null!, BufferPriority.Normal));
    }

    [Test]
    public async Task BufferMessageAsync_ShouldThrowArgumentException_WhenChatIdIsEmpty()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        var message = CreateTestMessage();
        message.ChatId = "";

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(
            async () => await _grain.BufferMessageAsync(message, BufferPriority.Normal));
    }

    [Test]
    public async Task GetBufferedMessagesAsync_ShouldReturnEmpty_WhenChatHasNoBuffer()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        var nonExistentChatId = "non-existent-chat";

        // Act
        var messages = await _grain.GetBufferedMessagesAsync(nonExistentChatId);

        // Assert
        Assert.That(messages, Is.Not.Null);
        Assert.That(messages.Count(), Is.EqualTo(0));
    }

    private ChatMessage CreateTestMessage(string? content = null)
    {
        return new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = TestChatId,
            UserId = TestUserId,
            Content = content ?? "Test message content",
            Role = "user",
            Timestamp = DateTime.UtcNow
        };
    }

    private StreamChunk CreateTestStreamChunk()
    {
        return new StreamChunk
        {
            OperationId = Guid.NewGuid().ToString(),
            ChatId = TestChatId,
            Content = "Test chunk content",
            ChunkIndex = 0,
            IsComplete = false,
            TotalChunks = 3,
            MessageId = Guid.NewGuid().ToString()
        };
    }
}

/// <summary>
/// Test silo configurator for Orleans buffer testing.
/// </summary>
public class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorageAsDefault()
            .ConfigureLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Warning);
            });
    }
}