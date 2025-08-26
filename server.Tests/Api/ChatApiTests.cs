using System.Net.Http.Json;
using AIChat.Server.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AIChat.Server.Tests.Api;

public class ChatApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory.WithWebHostBuilder(builder =>
        {
            _ = builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Test");
        });

    [Fact]
    public async Task Create_And_Get_Chat_Works()
    {
        var client = _factory.CreateClient();
        var create = new CreateChatRequest(null, "user-123", "hello world", null, null);
        var res = await client.PostAsJsonAsync("/api/chat", create);
        _ = res.EnsureSuccessStatusCode();
        var chat = await res.Content.ReadFromJsonAsync<AIChat.Server.Services.ChatDto>();
        _ = chat!.Id.Should().NotBeNullOrEmpty();
        _ = chat.Messages.Should().NotBeEmpty();

        var get = await client.GetAsync($"/api/chat/{chat.Id}");
        _ = get.EnsureSuccessStatusCode();
        var chat2 = await get.Content.ReadFromJsonAsync<AIChat.Server.Services.ChatDto>();
        _ = chat2!.Id.Should().Be(chat.Id);
        _ = chat2.Messages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task History_And_Delete_Works()
    {
        var client = _factory.CreateClient();
        // Create one chat
        var create = new CreateChatRequest(null, "user-123", "hello again", null, null);
        _ = (await client.PostAsJsonAsync("/api/chat", create)).EnsureSuccessStatusCode();

        var hist = await client.GetAsync("/api/chat/history?userId=user-123&page=1&pageSize=10");
        _ = hist.EnsureSuccessStatusCode();
        var history = await hist.Content.ReadFromJsonAsync<ChatHistoryResponse>();
        _ = history!.Chats.Should().NotBeNull();
        _ = history.Chats.Should().NotBeEmpty();

        var id = history.Chats.First().Id;
        var del = await client.DeleteAsync($"/api/chat/{id}");
        _ = del.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Stream_SSE_Completes_And_Contains_Done()
    {
        var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream-sse");
        req.Content = JsonContent.Create(
            new CreateChatRequest(null, "user-123", "Hello reasoning test", null, null)
        );
        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        _ = res.EnsureSuccessStatusCode();
        var text = await res.Content.ReadAsStringAsync();
        _ = text.Should().Contain("event: init");
        _ = text.Should().Contain("event: messageupdate");
        _ = text.Should().Contain("event: complete");
        _ = text.Should().Contain("data:");
    }
}
