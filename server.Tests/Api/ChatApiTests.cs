using System.Net.Http.Json;
using AIChat.Server.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AIChat.Server.Tests.Api;

public class ChatApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ChatApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Test");
        });
    }

    [Fact]
    public async Task Create_And_Get_Chat_Works()
    {
        var client = _factory.CreateClient();
        var create = new CreateChatRequest(null, "user-123", "hello world", null);
        var res = await client.PostAsJsonAsync("/api/chat", create);
        res.EnsureSuccessStatusCode();
        var chat = await res.Content.ReadFromJsonAsync<AIChat.Server.Services.ChatDto>();
        chat!.Id.Should().NotBeNullOrEmpty();
        chat.Messages.Should().NotBeEmpty();

        var get = await client.GetAsync($"/api/chat/{chat.Id}");
        get.EnsureSuccessStatusCode();
        var chat2 = await get.Content.ReadFromJsonAsync<AIChat.Server.Services.ChatDto>();
        chat2!.Id.Should().Be(chat.Id);
        chat2.Messages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task History_And_Delete_Works()
    {
        var client = _factory.CreateClient();
        // Create one chat
        var create = new CreateChatRequest(null, "user-123", "hello again", null);
        (await client.PostAsJsonAsync("/api/chat", create)).EnsureSuccessStatusCode();

        var hist = await client.GetAsync("/api/chat/history?userId=user-123&page=1&pageSize=10");
        hist.EnsureSuccessStatusCode();
        var history = await hist.Content.ReadFromJsonAsync<ChatHistoryResponse>();
        history!.Chats.Should().NotBeNull();
        history.Chats.Should().NotBeEmpty();

        var id = history.Chats.First().Id;
        var del = await client.DeleteAsync($"/api/chat/{id}");
        del.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Stream_SSE_Completes_And_Contains_Done()
    {
        var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream-sse");
        req.Content = JsonContent.Create(new CreateChatRequest(null, "user-123", "Hello reasoning test", null));
        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        var text = await res.Content.ReadAsStringAsync();
        text.Should().Contain("event: init");
        text.Should().Contain("event: messageupdate");
        text.Should().Contain("event: complete");
        text.Should().Contain("data:");
    }
}

