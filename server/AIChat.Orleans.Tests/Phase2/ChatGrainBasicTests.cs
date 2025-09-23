using AIChat.Orleans.Contracts;
using AIChat.Orleans.Grains;
using AIChat.Orleans.Tests.TestUtilities.Infrastructure;
using NUnit.Framework;
using Orleans;

namespace AIChat.Orleans.Tests.Phase2;

/// <summary>
/// Basic functional tests for ChatGrain implementation.
/// Tests core functionality across all 27 interface methods to validate implementation completion.
/// </summary>
[TestFixture]
public class ChatGrainBasicTests
{
    private TestClusterManager? _clusterManager;
    private IChatGrain? _chatGrain;
    private string _testChatId = null!;

    [SetUp]
    public async Task Setup()
    {
        _clusterManager = new TestClusterManager();
        await _clusterManager.InitializeAsync();

        _testChatId = $"test-chat-{Guid.NewGuid()}";
        _chatGrain = _clusterManager.Client.GetGrain<IChatGrain>(_testChatId);
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
    public async Task ChatGrain_FullWorkflow_ShouldExecuteSuccessfully()
    {
        // Test 1: IChatStateGrain.InitializeAsync
        var initRequest = new ChatInitRequest
        {
            ChatId = _testChatId,
            Title = "Test Chat",
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow,
            ChatType = "one-on-one",
            ModeId = "default-mode",
            SystemPrompt = "You are a helpful assistant",
            Metadata = "{}",
            InitialParticipants = [
                new() { ParticipantId = "test-user", DisplayName = "Test User", Role = ParticipantRole.Owner }
            ]
        };

        var chatState = await _chatGrain!.InitializeAsync(initRequest);
        Assert.Multiple(() =>
        {
            Assert.That(chatState.ChatId, Is.EqualTo(_testChatId));
            Assert.That(chatState.Title, Is.EqualTo("Test Chat"));
            Assert.That(chatState.Status, Is.EqualTo(ChatStatus.Active));
            Assert.That(chatState.ParticipantCount, Is.EqualTo(1));
        });

        // Test 2: IChatStateGrain.GetStateAsync
        var state = await _chatGrain.GetStateAsync();
        Assert.That(state.ChatId, Is.EqualTo(_testChatId));

        // Test 3: IChatStateGrain.UpdateMetadataAsync
        var updatedState = await _chatGrain.UpdateMetadataAsync("{\"theme\": \"dark\"}");
        Assert.That(updatedState.Metadata, Is.EqualTo("{\"theme\": \"dark\"}"));

        // Test 4: IChatStateGrain.CheckHealthAsync
        var health = await _chatGrain.CheckHealthAsync();
        Assert.That(health.IsHealthy, Is.True);

        // Test 5: IChatStateGrain.GetHistoryAsync
        var history = await _chatGrain.GetHistoryAsync();
        Assert.That(history, Is.Empty);

        // Test 6: IChatMessagingGrain.ProcessMessageAsync
        var message = new ChatMessage
        {
            UserId = "test-user",
            Content = "Hello, world!",
            Role = "user"
        };

        var messageResult = await _chatGrain.ProcessMessageAsync(message);
        Assert.Multiple(() =>
        {
            Assert.That(messageResult.Success, Is.True);
            Assert.That(messageResult.Message, Is.Not.Null);
            Assert.That(messageResult.Message!.Content, Is.EqualTo("Hello, world!"));
        });

        // Test 7: IChatMessagingGrain.SendSystemMessageAsync
        var systemMessage = await _chatGrain.SendSystemMessageAsync("System message");
        Assert.Multiple(() =>
        {
            Assert.That(systemMessage.Content, Is.EqualTo("System message"));
            Assert.That(systemMessage.UserId, Is.EqualTo("system"));
        });

        // Test 8: IChatMessagingGrain.EditMessageAsync
        var editedMessage = await _chatGrain.EditMessageAsync(messageResult.Message!.Id, "Updated content");
        Assert.That(editedMessage.Content, Is.EqualTo("Updated content"));

        // Test 9: IChatMessagingGrain.AcknowledgeMessageAsync
        await _chatGrain.AcknowledgeMessageAsync(messageResult.Message!.Id, "test-user");
        // Should not throw

        // Test 10: IChatMessagingGrain.GetMessageStatusAsync
        var messageStatus = await _chatGrain.GetMessageStatusAsync(messageResult.Message!.Id);
        Assert.That(messageStatus.MessageId, Is.EqualTo(messageResult.Message.Id));

        // Test 11: IChatMessagingGrain.DeleteMessageAsync
        var deleteResult = await _chatGrain.DeleteMessageAsync(systemMessage.Id);
        Assert.That(deleteResult, Is.True);

        // Test 12: IChatStreamingGrain.StartStreamAsync
        var streamMessage = new StreamMessage
        {
            ChatId = _testChatId,
            UserId = "test-user",
            Content = "Starting stream..."
        };

        var streamHandle = await _chatGrain.StartStreamAsync(streamMessage);
        Assert.Multiple(() =>
        {
            Assert.That(streamHandle.StreamId, Is.Not.Null.And.Not.Empty);
            Assert.That(streamHandle.ChatId, Is.EqualTo(_testChatId));
            Assert.That(streamHandle.Status, Is.EqualTo(StreamStatus.Active));
        });

        // Test 13: IChatStreamingGrain.ProcessStreamChunkAsync
        var chunk = new StreamChunk
        {
            OperationId = streamHandle.StreamId,
            Content = " chunk content",
            ChunkIndex = 1,
            IsComplete = false
        };

        await _chatGrain.ProcessStreamChunkAsync(chunk);
        // Should not throw

        // Test 14: IChatStreamingGrain.GetStreamStateAsync
        var streamState = await _chatGrain.GetStreamStateAsync(streamHandle.StreamId);
        Assert.That(streamState, Is.Not.Null);
        Assert.That(streamState!.Status, Is.EqualTo(StreamStatus.Active));

        // Test 15: IChatStreamingGrain.GetActiveStreamsAsync
        var activeStreams = await _chatGrain.GetActiveStreamsAsync();
        Assert.That(activeStreams, Has.Count.EqualTo(1));

        // Test 16: IChatStreamingGrain.CompleteStreamAsync
        var completedStream = await _chatGrain.CompleteStreamAsync(streamHandle.StreamId, "Final content");
        Assert.That(completedStream.Status, Is.EqualTo(StreamStatus.Completed));

        // Test 17: IChatStreamingGrain.CancelStreamAsync (create new stream first)
        var streamHandle2 = await _chatGrain.StartStreamAsync(streamMessage);
        await _chatGrain.CancelStreamAsync(streamHandle2.StreamId, "Test cancellation");
        var cancelledState = await _chatGrain.GetStreamStateAsync(streamHandle2.StreamId);
        Assert.That(cancelledState!.Status, Is.EqualTo(StreamStatus.Cancelled));

        // Test 18: IChatStreamingGrain.SubscribeToStreamAsync
        var subscription = await _chatGrain.SubscribeToStreamAsync(Guid.NewGuid());
        Assert.That(subscription.IsActive, Is.True);

        // Test 19: IChatParticipantGrain.AddParticipantAsync
        var newParticipant = new ChatParticipant
        {
            ParticipantId = "new-user",
            DisplayName = "New User",
            Role = ParticipantRole.Member,
            Status = PresenceStatus.Online
        };

        await _chatGrain.AddParticipantAsync(newParticipant);
        var updatedChatState = await _chatGrain.GetStateAsync();
        Assert.That(updatedChatState.ParticipantCount, Is.EqualTo(2));

        // Test 20: IChatParticipantGrain.GetParticipantAsync
        var participant = await _chatGrain.GetParticipantAsync("new-user");
        Assert.Multiple(() =>
        {
            Assert.That(participant, Is.Not.Null);
            Assert.That(participant!.DisplayName, Is.EqualTo("New User"));
        });

        // Test 21: IChatParticipantGrain.GetParticipantsAsync
        var participants = await _chatGrain.GetParticipantsAsync();
        Assert.That(participants, Has.Count.EqualTo(2));

        // Test 22: IChatParticipantGrain.UpdateParticipantAsync
        var update = new ParticipantUpdate
        {
            ParticipantId = "new-user",
            DisplayName = "Updated User",
            Role = ParticipantRole.Moderator
        };

        var updatedParticipant = await _chatGrain.UpdateParticipantAsync(update);
        Assert.Multiple(() =>
        {
            Assert.That(updatedParticipant.DisplayName, Is.EqualTo("Updated User"));
            Assert.That(updatedParticipant.Role, Is.EqualTo(ParticipantRole.Moderator));
        });

        // Test 23: IChatParticipantGrain.UpdatePresenceAsync
        await _chatGrain.UpdatePresenceAsync("new-user", PresenceStatus.Away);
        var participantAfterPresenceUpdate = await _chatGrain.GetParticipantAsync("new-user");
        Assert.That(participantAfterPresenceUpdate!.Status, Is.EqualTo(PresenceStatus.Away));

        // Test 24: IChatParticipantGrain.CheckPermissionAsync
        var hasPermission = await _chatGrain.CheckPermissionAsync("test-user", ChatAction.SendMessage);
        Assert.That(hasPermission, Is.True);

        var noPermission = await _chatGrain.CheckPermissionAsync("new-user", ChatAction.DeleteChat);
        Assert.That(noPermission, Is.False);

        // Test 25: IChatParticipantGrain.NotifyParticipantsAsync
        await _chatGrain.NotifyParticipantsAsync("TestEvent", new { Message = "Test notification" });
        // Should not throw

        // Test 26: IChatParticipantGrain.RemoveParticipantAsync
        var removeResult = await _chatGrain.RemoveParticipantAsync("new-user");
        Assert.That(removeResult, Is.True);

        var finalChatState = await _chatGrain.GetStateAsync();
        Assert.That(finalChatState.ParticipantCount, Is.EqualTo(1));

        // Test 27: IChatStateGrain.ArchiveAsync
        await _chatGrain.ArchiveAsync();
        var archivedState = await _chatGrain.GetStateAsync();
        Assert.Multiple(() =>
        {
            Assert.That(archivedState.Status, Is.EqualTo(ChatStatus.Archived));
            Assert.That(archivedState.ArchivedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task ChatGrain_StateManagement_ShouldPersistCorrectly()
    {
        // Initialize chat
        var initRequest = new ChatInitRequest
        {
            ChatId = _testChatId,
            Title = "Persistence Test",
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow,
            ChatType = "group",
            InitialParticipants = [
                new() { ParticipantId = "test-user", DisplayName = "Test User", Role = ParticipantRole.Owner }
            ]
        };

        await _chatGrain!.InitializeAsync(initRequest);

        // Add some state changes
        await _chatGrain.ProcessMessageAsync(new ChatMessage
        {
            UserId = "test-user",
            Content = "Test message",
            Role = "user"
        });

        await _chatGrain.AddParticipantAsync(new ChatParticipant
        {
            ParticipantId = "user2",
            DisplayName = "User 2",
            Role = ParticipantRole.Member
        });

        // Validate state persistence by getting current state
        var finalState = await _chatGrain.GetStateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(finalState.Title, Is.EqualTo("Persistence Test"));
            Assert.That(finalState.ParticipantCount, Is.EqualTo(2));
            Assert.That(finalState.MessageCount, Is.EqualTo(1));
            Assert.That(finalState.Version, Is.GreaterThan(1), "State version should increment with changes");
        });
    }

    [Test]
    public async Task ChatGrain_AllInterfaceMethods_ShouldBeImplemented()
    {
        // This test verifies that all 27 interface methods are implemented
        // by calling each one and ensuring no NotImplementedException is thrown

        await _chatGrain!.InitializeAsync(new ChatInitRequest
        {
            ChatId = _testChatId,
            Title = "Interface Test",
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow,
            ChatType = "test",
            InitialParticipants = [
                new() { ParticipantId = "test-user", DisplayName = "Test User", Role = ParticipantRole.Owner }
            ]
        });

        // IChatStateGrain methods (6 total)
        Assert.DoesNotThrowAsync(async () => await _chatGrain.GetStateAsync());
        Assert.DoesNotThrowAsync(async () => await _chatGrain.UpdateMetadataAsync("{}"));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.GetHistoryAsync());
        Assert.DoesNotThrowAsync(async () => await _chatGrain.CheckHealthAsync());
        // ArchiveAsync tested separately to avoid affecting other tests

        // IChatMessagingGrain methods (6 total)
        var msg = await _chatGrain.ProcessMessageAsync(new ChatMessage { UserId = "test-user", Content = "test", Role = "user" });
        Assert.DoesNotThrowAsync(async () => await _chatGrain.SendSystemMessageAsync("test"));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.EditMessageAsync(msg.Message!.Id, "updated"));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.AcknowledgeMessageAsync(msg.Message!.Id, "test-user"));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.GetMessageStatusAsync(msg.Message!.Id));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.DeleteMessageAsync(msg.Message!.Id));

        // IChatStreamingGrain methods (7 total)
        var stream = await _chatGrain.StartStreamAsync(new StreamMessage { ChatId = _testChatId, UserId = "test-user", Content = "test" });
        Assert.DoesNotThrowAsync(async () => await _chatGrain.ProcessStreamChunkAsync(new StreamChunk { OperationId = stream.StreamId, Content = "chunk", ChunkIndex = 1 }));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.GetStreamStateAsync(stream.StreamId));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.GetActiveStreamsAsync());
        Assert.DoesNotThrowAsync(async () => await _chatGrain.CompleteStreamAsync(stream.StreamId));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.CancelStreamAsync("non-existent", "test"));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.SubscribeToStreamAsync(Guid.NewGuid()));

        // IChatParticipantGrain methods (8 total)
        Assert.DoesNotThrowAsync(async () => await _chatGrain.AddParticipantAsync(new ChatParticipant { ParticipantId = "user2", DisplayName = "User 2", Role = ParticipantRole.Member }));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.GetParticipantAsync("user2"));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.GetParticipantsAsync());
        Assert.DoesNotThrowAsync(async () => await _chatGrain.UpdateParticipantAsync(new ParticipantUpdate { ParticipantId = "user2", DisplayName = "Updated User 2" }));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.UpdatePresenceAsync("user2", PresenceStatus.Away));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.CheckPermissionAsync("user2", ChatAction.SendMessage));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.NotifyParticipantsAsync("test", new { }));
        Assert.DoesNotThrowAsync(async () => await _chatGrain.RemoveParticipantAsync("user2"));

        // All 27 methods tested successfully - no NotImplementedException thrown
        Assert.Pass("All 27 interface methods are implemented and functional");
    }
}