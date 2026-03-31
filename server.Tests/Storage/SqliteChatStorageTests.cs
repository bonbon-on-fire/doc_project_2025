using System.Text.Json;
using AIChat.Server.Services;
using AchieveAi.LmDotnetTools.LmCore.Messages;
using AIChat.Server.Storage;
using AIChat.Server.Storage.Sqlite;
using FluentAssertions;
using Xunit;

namespace AIChat.Server.Tests.Storage;

public class SqliteChatStorageTests
{
    private static SqliteConnectionFactory CreateFactory()
        => new SqliteConnectionFactory("Data Source=File:storagetest?mode=memory&cache=shared", keepRootOpen: true);

    private static JsonSerializerOptions JsonOptions => new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public async Task CreateChat_List_Delete_Cascade()
    {
        var factory = CreateFactory();
        await TestDatabaseInitializer.InitializeAsync(factory);
        var storage = new SqliteChatStorage(factory);

        var (ok, _, chat) = await storage.CreateChatAsync("user-123", "t", DateTime.UtcNow, DateTime.UtcNow, null);
        ok.Should().BeTrue();
        chat!.Id.Should().NotBeNullOrEmpty();

        // Insert two messages
        var m1 = new MessageRecord
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chat.Id,
            Role = "user",
            Kind = "text",
            TimestampUtc = DateTime.UtcNow,
            SequenceNumber = 0,
            MessageJson = JsonSerializer.Serialize(new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chat.Id,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = 0,
                Text = "hello"
            }, JsonOptions)
        };
        var ins1 = await storage.InsertMessageAsync(m1);
        ins1.Success.Should().BeTrue();

        var m2 = new MessageRecord
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chat.Id,
            Role = "assistant",
            Kind = "reasoning",
            TimestampUtc = DateTime.UtcNow,
            SequenceNumber = 1,
            MessageJson = JsonSerializer.Serialize(new ReasoningMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chat.Id,
                Role = "assistant",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = 1,
                Reasoning = "why",
                Visibility = ReasoningVisibility.Plain
            }, JsonOptions)
        };
        var ins2 = await storage.InsertMessageAsync(m2);
        ins2.Success.Should().BeTrue();

        var list = await storage.ListChatMessagesOrderedAsync(chat.Id);
        list.Success.Should().BeTrue();
        list.Messages.Should().HaveCount(2);
        list.Messages[0].SequenceNumber.Should().Be(0);
        list.Messages[1].SequenceNumber.Should().Be(1);
        list.Messages[0].Role.Should().Be("user");
        list.Messages[0].Kind.Should().Be("text");
        list.Messages[1].Role.Should().Be("assistant");
        list.Messages[1].Kind.Should().Be("reasoning");

        // Delete chat and ensure messages are gone
        var del = await storage.DeleteChatAsync(chat.Id);
        del.Success.Should().BeTrue();

        var listAfter = await storage.ListChatMessagesOrderedAsync(chat.Id);
        listAfter.Success.Should().BeTrue();
        listAfter.Messages.Should().BeEmpty();

        await factory.DisposeAsync();
    }

    [Fact]
    public async Task Sequence_Allocation_Handles_Conflicts_With_Retry()
    {
        var factory = CreateFactory();
        await TestDatabaseInitializer.InitializeAsync(factory);
        var storage = new SqliteChatStorage(factory);
        var (ok, _, chat) = await storage.CreateChatAsync("user-123", "t", DateTime.UtcNow, DateTime.UtcNow, null);
        ok.Should().BeTrue();

        // Simulate concurrent allocate+insert for the same chat
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var alloc = await storage.AllocateSequenceAsync(chat!.Id);
            alloc.Success.Should().BeTrue();
            var seq = alloc.NextSequence;
            var msg = new MessageRecord
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chat!.Id,
                Role = "user",
                Kind = "text",
                TimestampUtc = DateTime.UtcNow,
                SequenceNumber = seq,
                MessageJson = JsonSerializer.Serialize(new TextMessageDto
                {
                    Id = Guid.NewGuid().ToString(),
                    ChatId = chat!.Id,
                    Role = "user",
                    Timestamp = DateTime.UtcNow,
                    SequenceNumber = seq,
                    Text = "hello"
                }, JsonOptions)
            };
            var ins = await storage.InsertMessageAsync(msg);
            ins.Success.Should().BeTrue();
            return ins.Message!.SequenceNumber;
        });

        var results = await Task.WhenAll(tasks);
        results.Distinct().Count().Should().Be(results.Length);
        results.Min().Should().Be(0);

        var list = await storage.ListChatMessagesOrderedAsync(chat!.Id);
        list.Messages.Should().HaveCount(10);
        list.Messages.Select(m => m.SequenceNumber).Should().BeInAscendingOrder();

        await factory.DisposeAsync();
    }

    [Fact]
    public async Task GetMessageContent_Parses_Text_And_Reasoning()
    {
        var factory = CreateFactory();
        await TestDatabaseInitializer.InitializeAsync(factory);
        var storage = new SqliteChatStorage(factory);
        var (ok, _, chat) = await storage.CreateChatAsync("user-123", "t", DateTime.UtcNow, DateTime.UtcNow, null);
        ok.Should().BeTrue();

        var text = new TextMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chat!.Id,
            Role = "user",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 0,
            Text = "hello world"
        };
        var m1 = new MessageRecord
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chat.Id,
            Role = text.Role,
            Kind = "text",
            TimestampUtc = text.Timestamp,
            SequenceNumber = 0,
            MessageJson = JsonSerializer.Serialize(text, JsonOptions)
        };
        (await storage.InsertMessageAsync(m1)).Success.Should().BeTrue();

        var reasoning = new ReasoningMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chat.Id,
            Role = "assistant",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            Reasoning = "because",
            Visibility = ReasoningVisibility.Plain
        };
        var m2 = new MessageRecord
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chat.Id,
            Role = reasoning.Role,
            Kind = "reasoning",
            TimestampUtc = reasoning.Timestamp,
            SequenceNumber = 1,
            MessageJson = JsonSerializer.Serialize(reasoning, JsonOptions)
        };
        (await storage.InsertMessageAsync(m2)).Success.Should().BeTrue();

        var c1 = await storage.GetMessageContentAsync(m1.Id);
        c1.Success.Should().BeTrue();
        c1.Content.Should().Be("hello world");

        var c2 = await storage.GetMessageContentAsync(m2.Id);
        c2.Success.Should().BeTrue();
        c2.Content.Should().Be("because");

        await factory.DisposeAsync();
    }
}


