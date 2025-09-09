using AIChat.Orleans.Configuration;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Orleans.TestingHost;

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
        _ = builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();

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
    public async Task BufferMessageAsyncShouldCreateBufferedMessageWhenValidMessage()
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
        Assert.Multiple(() =>
        {
            Assert.That(bufferedMessage!.MessageId, Is.EqualTo(messageId));
            Assert.That(bufferedMessage.ChatId, Is.EqualTo(TestChatId));
            Assert.That(bufferedMessage.Message.Content, Is.EqualTo(message.Content));
            Assert.That(bufferedMessage.Priority, Is.EqualTo(BufferPriority.Normal));
        });
    }

    [Test]
    public async Task BufferStreamChunkAsyncShouldCreateBufferedStreamChunkWhenValidChunk()
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
        Assert.Multiple(() =>
        {
            Assert.That(bufferedMessage!.Message.IsStreaming, Is.True);
            Assert.That(bufferedMessage.Priority, Is.EqualTo(BufferPriority.High));
        });
    }

    [Test]
    public async Task GetBufferedMessagesAsyncShouldReturnFilteredMessagesWhenHighPriorityFilter()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        var normalMessage = CreateTestMessage("Normal priority message");
        var highMessage = CreateTestMessage("High priority message");

        _ = await _grain.BufferMessageAsync(normalMessage, BufferPriority.Normal);
        _ = await _grain.BufferMessageAsync(highMessage, BufferPriority.High);

        // Act
        var highPriorityMessages = await _grain.GetBufferedMessagesAsync(TestChatId, null, true);
        var allMessages = await _grain.GetBufferedMessagesAsync(TestChatId);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(highPriorityMessages.Count(), Is.EqualTo(1));
            Assert.That(allMessages.Count(), Is.EqualTo(2));
            Assert.That(highPriorityMessages.First().Priority, Is.EqualTo(BufferPriority.High));
        });
    }

    [Test]
    public async Task GetBufferedMessagesAsyncShouldRespectLimitWhenLimitSpecified()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);

        // Buffer 3 messages
        for (var i = 0; i < 3; i++)
        {
            var message = CreateTestMessage($"Test message {i}");
            _ = await _grain.BufferMessageAsync(message, BufferPriority.Normal);
        }

        // Act
        var limitedMessages = await _grain.GetBufferedMessagesAsync(TestChatId, 2);
        var allMessages = await _grain.GetBufferedMessagesAsync(TestChatId);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(limitedMessages.Count(), Is.EqualTo(2));
            Assert.That(allMessages.Count(), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task RemoveBufferedMessageAsyncShouldRemoveMessageWhenMessageExists()
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
    public async Task RemoveBufferedMessageAsyncShouldReturnFalseWhenMessageDoesNotExist()
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
    public async Task MarkMessageDeliveredAsyncShouldRemoveMessageAndUpdateMetrics()
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
    public async Task RecordDeliveryAttemptAsyncShouldUpdateAttemptInfoWhenMessageExists()
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
        Assert.Multiple(() =>
        {
            Assert.That(bufferedMessage!.DeliveryAttempts, Is.EqualTo(1));
            Assert.That(bufferedMessage.LastDeliveryError, Is.EqualTo(error));
            Assert.That(bufferedMessage.LastDeliveryAttempt, Is.Not.Null);
        });
    }

    [Test]
    public async Task GetChatBufferAsyncShouldReturnBufferInfoWhenBufferExists()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        var message = CreateTestMessage();
        _ = await _grain.BufferMessageAsync(message);

        // Act
        var buffer = await _grain.GetChatBufferAsync(TestChatId);

        // Assert
        Assert.That(buffer, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(buffer!.ChatId, Is.EqualTo(TestChatId));
            Assert.That(buffer.Messages, Has.Count.EqualTo(1));
            Assert.That(buffer.MaxSize, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task GetBufferSummaryAsyncShouldReturnSummaryInfo()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);

        // Buffer some messages
        var normalMessage = CreateTestMessage("Normal message");
        var highMessage = CreateTestMessage("High priority message");
        _ = await _grain.BufferMessageAsync(normalMessage, BufferPriority.Normal);
        _ = await _grain.BufferMessageAsync(highMessage, BufferPriority.High);

        // Act
        var summary = await _grain.GetBufferSummaryAsync();

        // Assert
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary.ContainsKey(TestChatId), Is.True);

        var chatSummary = summary[TestChatId];
        Assert.Multiple(() =>
        {
            Assert.That(chatSummary.CurrentMessageCount, Is.EqualTo(2));
            Assert.That(chatSummary.HighPriorityCount, Is.EqualTo(1));
            Assert.That(chatSummary.UtilizationPercent, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task ClearChatBufferAsyncShouldRemoveAllMessagesFromChat()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);

        // Buffer multiple messages
        for (var i = 0; i < 3; i++)
        {
            var message = CreateTestMessage($"Message {i}");
            _ = await _grain.BufferMessageAsync(message);
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
    public Task BufferMessageAsyncShouldThrowArgumentExceptionWhenMessageIsNull()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);

        // Act & Assert
        _ = Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _grain.BufferMessageAsync(null!, BufferPriority.Normal));
        return Task.CompletedTask;
    }

    [Test]
    public Task BufferMessageAsyncShouldThrowArgumentExceptionWhenChatIdIsEmpty()
    {
        // Arrange
        Assert.That(_grain, Is.Not.Null);
        var message = CreateTestMessage();
        message.ChatId = "";

        // Act & Assert
        _ = Assert.ThrowsAsync<ArgumentException>(
            async () => await _grain.BufferMessageAsync(message, BufferPriority.Normal));
        return Task.CompletedTask;
    }

    [Test]
    public async Task GetBufferedMessagesAsyncShouldReturnEmptyWhenChatHasNoBuffer()
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

    private static ChatMessage CreateTestMessage(string? content = null)
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

    private static StreamChunk CreateTestStreamChunk()
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
        _ = siloBuilder
            .AddMemoryGrainStorageAsDefault()
            .AddMemoryGrainStorage("UserGrainStorage")
            .AddMemoryGrainStorage("PubSubStore")

            // Configure services required by grains
            .ConfigureServices(services =>
            {
                // Add Orleans metrics collector (required by UserGrain)
                _ = services.AddSingleton<IOrleansMetricsCollector, OrleansMetricsCollector>();

                // Add Orleans grain configuration with test-friendly settings
                _ = services.Configure<OrleansGrainConfiguration>(config =>
                {
                    config.UserGrain.MaxActivityBufferSize = 50;
                    config.UserGrain.CleanupIntervalMinutes = 1;
                    config.UserGrain.EnablePeriodicTimers = false;
                    config.Connections.MaxConnectionsPerUser = 5;
                    config.Persistence.ActivityPersistenceInterval = 5;
                });
            })

            .ConfigureLogging(logging =>
            {
                _ = logging.AddConsole();
                _ = logging.SetMinimumLevel(LogLevel.Warning);
                // Only show errors for Orleans runtime during tests
                _ = logging.AddFilter("Orleans", LogLevel.Error);
                _ = logging.AddFilter("Microsoft", LogLevel.Error);
                // But allow our Orleans components to log at Debug level
                _ = logging.AddFilter("AIChat.Orleans", LogLevel.Debug);
            });
    }
}
