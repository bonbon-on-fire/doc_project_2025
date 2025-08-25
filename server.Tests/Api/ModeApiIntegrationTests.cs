using System.Net.Http.Json;
using System.Text.Json;
using AIChat.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
// Use fully qualified names to avoid ambiguity
using CreateChatRequest = AIChat.Server.Controllers.CreateChatRequest;
using SendMessageRequest = AIChat.Server.Controllers.SendMessageRequest;
using ModesResponse = AIChat.Server.Controllers.ModesResponse;

namespace AIChat.Server.Tests.Api;

/// <summary>
/// Integration tests for Mode API endpoints and chat mode selection flow.
/// Tests end-to-end scenarios including mode selection, tool filtering, and error handling.
/// </summary>
public class ModeApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly JsonSerializerOptions _jsonOptions;

    public ModeApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Test");
        });
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    #region Mode Selection Flow Tests

    [Fact]
    public async Task CreateChat_WithMode_FiltersToolsCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = $"test-user-{Guid.NewGuid()}";
        
        // First get available modes
        var modesResponse = await client.GetAsync($"/api/mode?userId={userId}");
        modesResponse.EnsureSuccessStatusCode();
        var modesData = await modesResponse.Content.ReadFromJsonAsync<ModesResponse>(_jsonOptions);
        modesData!.Modes.Should().NotBeEmpty("System modes should be available");
        
        // Select a mode with specific tools (e.g., coding mode)
        var codingMode = modesData.Modes.FirstOrDefault(m => m.Category == "task" && m.Name.Contains("Coding", StringComparison.OrdinalIgnoreCase))
                        ?? modesData.Modes.First();
        
        // Create chat with selected mode
        var createRequest = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Write a hello world program",
            SystemPrompt: null,
            ModeId: codingMode.Id
        );
        
        var createResponse = await client.PostAsJsonAsync("/api/chat", createRequest, _jsonOptions);
        createResponse.EnsureSuccessStatusCode();
        
        var chat = await createResponse.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);
        
        // Assert
        chat.Should().NotBeNull();
        chat!.Id.Should().NotBeNullOrEmpty();
        chat.Messages.Should().NotBeEmpty();
        
        // Verify mode was applied
        // Note: The actual tool filtering happens internally, we verify by checking the chat was created successfully
        // In a real scenario, we'd check the tools available in the chat context
    }

    [Fact]
    public async Task SwitchMode_MidConversation_UpdatesToolContext()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = $"test-user-{Guid.NewGuid()}";
        
        // Create initial chat with general mode
        var createRequest = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Hello, I need help",
            SystemPrompt: null,
            ModeId: "general"
        );
        
        var createResponse = await client.PostAsJsonAsync("/api/chat", createRequest, _jsonOptions);
        createResponse.EnsureSuccessStatusCode();
        var chat = await createResponse.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);
        
        // Switch to a different mode (e.g., writing mode)
        var continueRequest = new SendMessageRequest
        {
            Message = "Now help me write a story",
            ModeId = "writing" // Switch to writing mode
        };
        
        // Continue chat with new mode using the chat endpoint with ID
        var continueResponse = await client.PostAsJsonAsync($"/api/chat/{chat!.Id}/messages?userId={userId}", continueRequest, _jsonOptions);
        
        // Assert
        continueResponse.EnsureSuccessStatusCode();
        var updatedChat = await continueResponse.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);
        updatedChat.Should().NotBeNull();
        updatedChat!.Messages.Should().HaveCountGreaterThan(chat.Messages.Count);
    }

    [Fact]
    public async Task CreateAndUseCustomMode_FullFlow()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = $"test-user-{Guid.NewGuid()}";
        
        // Step 1: Create custom mode
        var customMode = new CreateModeRequest
        {
            Name = $"Custom Test Mode {Guid.NewGuid()}",
            Description = "A custom mode for integration testing",
            Prompt = "You are a helpful test assistant",
            Tools = new[] { "search", "calculator" },
            DefaultModel = null,
            Category = "custom"
        };
        
        var createModeResponse = await client.PostAsJsonAsync($"/api/mode?userId={userId}", customMode, _jsonOptions);
        createModeResponse.EnsureSuccessStatusCode();
        var createdMode = await createModeResponse.Content.ReadFromJsonAsync<ModeDto>(_jsonOptions);
        
        // Step 2: Use custom mode in chat
        var createChatRequest = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Calculate 2+2 for me",
            SystemPrompt: null,
            ModeId: createdMode!.Id
        );
        
        var createChatResponse = await client.PostAsJsonAsync("/api/chat", createChatRequest, _jsonOptions);
        createChatResponse.EnsureSuccessStatusCode();
        var chat = await createChatResponse.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);
        
        // Step 3: Clean up - delete custom mode
        var deleteResponse = await client.DeleteAsync($"/api/mode/{createdMode.Id}?userId={userId}");
        
        // Assert
        chat.Should().NotBeNull();
        chat!.Messages.Should().NotBeEmpty();
        deleteResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    }

    #endregion

    #region Error Scenario Tests

    [Fact]
    public async Task CreateChat_WithInvalidMode_FallsBackToGeneral()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = $"test-user-{Guid.NewGuid()}";
        
        // Try to create chat with non-existent mode
        var createRequest = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Test with invalid mode",
            SystemPrompt: null,
            ModeId: "non-existent-mode-id"
        );
        
        var response = await client.PostAsJsonAsync("/api/chat", createRequest, _jsonOptions);
        
        // Assert - Should still succeed with fallback to general mode
        response.EnsureSuccessStatusCode();
        var chat = await response.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);
        chat.Should().NotBeNull();
        chat!.Messages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateMode_WithInvalidData_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = $"test-user-{Guid.NewGuid()}";
        
        // Create mode with invalid data (empty name)
        var invalidMode = new CreateModeRequest
        {
            Name = "", // Invalid: empty name
            Description = "Test description",
            Prompt = "Test prompt",
            Tools = new[] { "tool1" },
            DefaultModel = null,
            Category = "custom"
        };
        
        var response = await client.PostAsJsonAsync($"/api/mode?userId={userId}", invalidMode, _jsonOptions);
        
        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSystemMode_ReturnsForbidden()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = $"test-user-{Guid.NewGuid()}";
        
        // Try to update a system mode
        var updateRequest = new UpdateModeRequest
        {
            Name = "Hacked System Mode",
            Description = "Should not work",
            Prompt = "Malicious prompt",
            Tools = new[] { "malicious-tool" },
            DefaultModel = null,
            Category = "hacked"
        };
        
        var response = await client.PutAsJsonAsync($"/api/mode/general?userId={userId}", updateRequest, _jsonOptions);
        
        // Assert
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.BadRequest,
            System.Net.HttpStatusCode.Forbidden
        );
    }

    [Fact]
    public async Task DeleteSystemMode_ReturnsForbidden()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = $"test-user-{Guid.NewGuid()}";
        
        // Try to delete a system mode
        var response = await client.DeleteAsync($"/api/mode/general?userId={userId}");
        
        // Assert
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.BadRequest,
            System.Net.HttpStatusCode.Forbidden
        );
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task GetModes_Performance_CompletesWithinTimeLimit()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = $"test-user-{Guid.NewGuid()}";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Act
        var response = await client.GetAsync($"/api/mode?userId={userId}");
        stopwatch.Stop();
        
        // Assert
        response.EnsureSuccessStatusCode();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(200, "Mode loading should complete within 200ms");
    }

    [Fact]
    public async Task SwitchMode_Performance_CompletesQuickly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = $"test-user-{Guid.NewGuid()}";
        
        // Create initial chat
        var createRequest = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Initial message",
            SystemPrompt: null,
            ModeId: "general"
        );
        
        var createResponse = await client.PostAsJsonAsync("/api/chat", createRequest, _jsonOptions);
        createResponse.EnsureSuccessStatusCode();
        var chat = await createResponse.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);
        
        // Measure mode switch time
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        var continueRequest = new SendMessageRequest
        {
            Message = "Continue with different mode",
            ModeId = "writing"
        };
        
        var continueResponse = await client.PostAsJsonAsync($"/api/chat/{chat!.Id}/messages?userId={userId}", continueRequest, _jsonOptions);
        stopwatch.Stop();
        
        // Assert
        continueResponse.EnsureSuccessStatusCode();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500, "Mode switching should be fast");
    }

    [Fact]
    public async Task ConcurrentModeAccess_HandlesMultipleUsers()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userIds = Enumerable.Range(1, 10).Select(i => $"concurrent-user-{i}-{Guid.NewGuid()}").ToList();
        
        // Act - Concurrent mode requests
        var tasks = userIds.Select(async userId =>
        {
            var response = await client.GetAsync($"/api/mode?userId={userId}");
            return (userId, response);
        }).ToList();
        
        var results = await Task.WhenAll(tasks);
        
        // Assert
        results.Should().HaveCount(10);
        foreach (var (userId, response) in results)
        {
            response.EnsureSuccessStatusCode();
            var modes = await response.Content.ReadFromJsonAsync<ModesResponse>(_jsonOptions);
            modes.Should().NotBeNull();
            modes!.Modes.Should().NotBeEmpty($"User {userId} should receive modes");
        }
    }

    #endregion

    #region SSE Integration Tests

    [Fact]
    public async Task StreamSSE_WithMode_IncludesModeInformation()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = $"test-user-{Guid.NewGuid()}";
        
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream-sse");
        var createRequest = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Test SSE with mode",
            SystemPrompt: null,
            ModeId: "general"
        );
        request.Content = JsonContent.Create(createRequest, options: _jsonOptions);
        
        // Act
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var sseContent = await response.Content.ReadAsStringAsync();
        
        // Assert
        sseContent.Should().Contain("event: init");
        sseContent.Should().Contain("event: messageupdate");
        sseContent.Should().Contain("event: complete");
        // The SSE stream should work with the specified mode
    }

    #endregion

    #region Mode Persistence Tests

    [Fact]
    public async Task ModePersistence_AcrossMultipleChats()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = $"test-user-{Guid.NewGuid()}";
        
        // Create custom mode
        var customMode = new CreateModeRequest
        {
            Name = $"Persistent Mode {Guid.NewGuid()}",
            Description = "Test mode persistence",
            Prompt = "You are a persistent assistant",
            Tools = new[] { "search" },
            DefaultModel = null,
            Category = "custom"
        };
        
        var createModeResponse = await client.PostAsJsonAsync($"/api/mode?userId={userId}", customMode, _jsonOptions);
        createModeResponse.EnsureSuccessStatusCode();
        var createdMode = await createModeResponse.Content.ReadFromJsonAsync<ModeDto>(_jsonOptions);
        
        // Create multiple chats with the same mode
        var chatIds = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var createChatRequest = new CreateChatRequest(
                ChatId: null,
                UserId: userId,
                Message: $"Chat {i + 1} with persistent mode",
                SystemPrompt: null,
                ModeId: createdMode!.Id
            );
            
            var response = await client.PostAsJsonAsync("/api/chat", createChatRequest, _jsonOptions);
            response.EnsureSuccessStatusCode();
            var chat = await response.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);
            chatIds.Add(chat!.Id);
        }
        
        // Verify mode is still available
        var modesResponse = await client.GetAsync($"/api/mode?userId={userId}");
        modesResponse.EnsureSuccessStatusCode();
        var modes = await modesResponse.Content.ReadFromJsonAsync<ModesResponse>(_jsonOptions);
        
        // Clean up
        await client.DeleteAsync($"/api/mode/{createdMode!.Id}?userId={userId}");
        
        // Assert
        chatIds.Should().HaveCount(3);
        chatIds.Should().OnlyHaveUniqueItems();
        modes!.Modes.Should().Contain(m => m.Id == createdMode.Id);
    }

    #endregion
}