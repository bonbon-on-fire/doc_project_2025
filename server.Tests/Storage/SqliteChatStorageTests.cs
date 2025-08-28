using System.Text.Json;
using AchieveAi.LmDotnetTools.LmCore.Messages;
using AIChat.Server.Services;
using AIChat.Server.Storage;
using AIChat.Server.Storage.Sqlite;
using FluentAssertions;
using Xunit;

namespace AIChat.Server.Tests.Storage;

public class SqliteChatStorageTests
{
    private static SqliteConnectionFactory CreateFactory()
    {
        return new SqliteConnectionFactory(
            "Data Source=File:storagetest?mode=memory&cache=shared",
            keepRootOpen: true
        );
    }

    private static JsonSerializerOptions JsonOptions =>
        new()
        {
            DefaultIgnoreCondition = System
                .Text
                .Json
                .Serialization
                .JsonIgnoreCondition
                .WhenWritingNull,
        };

    [Fact]
    public async Task CreateChat_List_Delete_Cascade()
    {
        var factory = CreateFactory();
        await TestDatabaseInitializer.InitializeAsync(factory);
        var storage = new SqliteChatStorage(factory);
        var userId = TestHelpers.GenerateUniqueUserId("chat-crud-test");

        var (ok, _, chat) = await storage.CreateChatAsync(
            userId,
            "t",
            DateTime.UtcNow,
            DateTime.UtcNow,
            null
        );
        _ = ok.Should().BeTrue();
        _ = chat!.Id.Should().NotBeNullOrEmpty();

        // Insert two messages
        var m1 = new MessageRecord
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chat.Id,
            Role = "user",
            Kind = "text",
            TimestampUtc = DateTime.UtcNow,
            SequenceNumber = 0,
            MessageJson = JsonSerializer.Serialize(
                new TextMessageDto
                {
                    Id = Guid.NewGuid().ToString(),
                    ChatId = chat.Id,
                    Role = "user",
                    Timestamp = DateTime.UtcNow,
                    SequenceNumber = 0,
                    Text = "hello",
                },
                JsonOptions
            ),
        };
        var (Success, Error, Message) = await storage.InsertMessageAsync(m1);
        _ = Success.Should().BeTrue();

        var m2 = new MessageRecord
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chat.Id,
            Role = "assistant",
            Kind = "reasoning",
            TimestampUtc = DateTime.UtcNow,
            SequenceNumber = 1,
            MessageJson = JsonSerializer.Serialize(
                new ReasoningMessageDto
                {
                    Id = Guid.NewGuid().ToString(),
                    ChatId = chat.Id,
                    Role = "assistant",
                    Timestamp = DateTime.UtcNow,
                    SequenceNumber = 1,
                    Reasoning = "why",
                    Visibility = ReasoningVisibility.Plain,
                },
                JsonOptions
            ),
        };
        var ins2 = await storage.InsertMessageAsync(m2);
        _ = ins2.Success.Should().BeTrue();

        var list = await storage.ListChatMessagesOrderedAsync(chat.Id);
        _ = list.Success.Should().BeTrue();
        _ = list.Messages.Should().HaveCount(2);
        _ = list.Messages[0].SequenceNumber.Should().Be(0);
        _ = list.Messages[1].SequenceNumber.Should().Be(1);
        _ = list.Messages[0].Role.Should().Be("user");
        _ = list.Messages[0].Kind.Should().Be("text");
        _ = list.Messages[1].Role.Should().Be("assistant");
        _ = list.Messages[1].Kind.Should().Be("reasoning");

        // Delete chat and ensure messages are gone
        var del = await storage.DeleteChatAsync(chat.Id);
        _ = del.Success.Should().BeTrue();

        var listAfter = await storage.ListChatMessagesOrderedAsync(chat.Id);
        _ = listAfter.Success.Should().BeTrue();
        _ = listAfter.Messages.Should().BeEmpty();

        await factory.DisposeAsync();
    }

    [Fact]
    public async Task Sequence_Allocation_Handles_Conflicts_With_Retry()
    {
        var factory = CreateFactory();
        await TestDatabaseInitializer.InitializeAsync(factory);
        var storage = new SqliteChatStorage(factory);
        var userId = TestHelpers.GenerateUniqueUserId("sequence-test");
        var (ok, _, chat) = await storage.CreateChatAsync(
            userId,
            "t",
            DateTime.UtcNow,
            DateTime.UtcNow,
            null
        );
        _ = ok.Should().BeTrue();

        // Simulate concurrent allocate+insert for the same chat
        var tasks = Enumerable
            .Range(0, 10)
            .Select(async _n =>
            {
                var (Success, Error, NextSequence) = await storage.AllocateSequenceAsync(chat!.Id);
                _ = Success.Should().BeTrue();
                var seq = NextSequence;
                var msg = new MessageRecord
                {
                    Id = Guid.NewGuid().ToString(),
                    ChatId = chat!.Id,
                    Role = "user",
                    Kind = "text",
                    TimestampUtc = DateTime.UtcNow,
                    SequenceNumber = seq,
                    MessageJson = JsonSerializer.Serialize(
                        new TextMessageDto
                        {
                            Id = Guid.NewGuid().ToString(),
                            ChatId = chat!.Id,
                            Role = "user",
                            Timestamp = DateTime.UtcNow,
                            SequenceNumber = seq,
                            Text = "hello",
                        },
                        JsonOptions
                    ),
                };
                var ins = await storage.InsertMessageAsync(msg);
                _ = ins.Success.Should().BeTrue();
                return ins.Message!.SequenceNumber;
            });

        var results = await Task.WhenAll(tasks);
        _ = results.Distinct().Count().Should().Be(results.Length);
        _ = results.Min().Should().Be(0);

        var list = await storage.ListChatMessagesOrderedAsync(chat!.Id);
        _ = list.Messages.Should().HaveCount(10);
        _ = list.Messages.Select(m => m.SequenceNumber).Should().BeInAscendingOrder();

        await factory.DisposeAsync();
    }

    [Fact]
    public async Task GetMessageContent_Parses_Text_And_Reasoning()
    {
        var factory = CreateFactory();
        await TestDatabaseInitializer.InitializeAsync(factory);
        var storage = new SqliteChatStorage(factory);
        var userId = TestHelpers.GenerateUniqueUserId("message-content-test");
        var (ok, _, chat) = await storage.CreateChatAsync(
            userId,
            "t",
            DateTime.UtcNow,
            DateTime.UtcNow,
            null
        );
        _ = ok.Should().BeTrue();

        var text = new TextMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chat!.Id,
            Role = "user",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 0,
            Text = "hello world",
        };
        var m1 = new MessageRecord
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chat.Id,
            Role = text.Role,
            Kind = "text",
            TimestampUtc = text.Timestamp,
            SequenceNumber = 0,
            MessageJson = JsonSerializer.Serialize(text, JsonOptions),
        };
        _ = (await storage.InsertMessageAsync(m1)).Success.Should().BeTrue();

        var reasoning = new ReasoningMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chat.Id,
            Role = "assistant",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            Reasoning = "because",
            Visibility = ReasoningVisibility.Plain,
        };
        var m2 = new MessageRecord
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chat.Id,
            Role = reasoning.Role,
            Kind = "reasoning",
            TimestampUtc = reasoning.Timestamp,
            SequenceNumber = 1,
            MessageJson = JsonSerializer.Serialize(reasoning, JsonOptions),
        };
        _ = (await storage.InsertMessageAsync(m2)).Success.Should().BeTrue();

        var (Success, Error, Content) = await storage.GetMessageContentAsync(m1.Id);
        _ = Success.Should().BeTrue();
        _ = Content.Should().Be("hello world");

        var c2 = await storage.GetMessageContentAsync(m2.Id);
        _ = c2.Success.Should().BeTrue();
        _ = c2.Content.Should().Be("because");

        await factory.DisposeAsync();
    }
}
