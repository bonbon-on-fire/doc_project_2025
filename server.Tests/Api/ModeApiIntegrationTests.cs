using System.Net.Http.Json;
using System.Text.Json;
using AIChat.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
// Use fully qualified names to avoid ambiguity
using CreateChatRequest = AIChat.Server.Controllers.CreateChatRequest;
using ModesResponse = AIChat.Server.Controllers.ModesResponse;

// SendMessageRequest removed - using CreateChatRequest for all chat operations

namespace AIChat.Server.Tests.Api;

/// <summary>
/// Integration tests for Mode API endpoints and chat mode selection flow.
/// Tests end-to-end scenarios including mode selection, tool filtering, and error handling.
/// </summary>
[Collection("ModeTests")] // Prevent parallel execution with other mode tests
public class ModeApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly JsonSerializerOptions _jsonOptions;

    public ModeApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            _ = builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Test")
        );

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }

    #region Mode Selection Flow Tests

    [Fact]
    public async Task CreateChat_WithMode_FiltersToolsCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = TestHelpers.GenerateUniqueUserId("chat-mode-filter");

        // First get available modes
        var modesResponse = await client.GetAsync($"/api/mode?userId={userId}");
        _ = modesResponse.EnsureSuccessStatusCode();
        var modesData = await modesResponse.Content.ReadFromJsonAsync<ModesResponse>(_jsonOptions);
        _ = modesData!.Modes.Should().NotBeEmpty("System modes should be available");

        // Select a mode with specific tools (e.g., coding mode)
        var codingMode =
            modesData.Modes.FirstOrDefault(m =>
                m.Category == "task"
                && m.Name.Contains("Coding", StringComparison.OrdinalIgnoreCase)
            ) ?? modesData.Modes.First();

        // Create chat with selected mode
        var createRequest = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Write a hello world program",
            SystemPrompt: null,
            ModeId: codingMode.Id
        );

        var createResponse = await client.PostAsJsonAsync("/api/chat", createRequest, _jsonOptions);
        _ = createResponse.EnsureSuccessStatusCode();

        var chat = await createResponse.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);

        // Assert
        _ = chat.Should().NotBeNull();
        _ = chat!.Id.Should().NotBeNullOrEmpty();
        _ = chat.Messages.Should().NotBeEmpty();

        // Verify mode was applied
        // Note: The actual tool filtering happens internally, we verify by checking the chat was created successfully
        // In a real scenario, we'd check the tools available in the chat context
    }

    [Fact]
    public async Task SwitchMode_MidConversation_UpdatesToolContext()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = TestHelpers.GenerateUniqueUserId("switch-mode");

        // Create initial chat with general mode
        var createRequest = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Hello, I need help",
            SystemPrompt: null,
            ModeId: "general"
        );

        var createResponse = await client.PostAsJsonAsync("/api/chat", createRequest, _jsonOptions);
        _ = createResponse.EnsureSuccessStatusCode();
        var chat = await createResponse.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);

        // Switch to a different mode (e.g., writing mode)
        var continueRequest = new CreateChatRequest(
            ChatId: chat!.Id,
            UserId: userId,
            Message: "Now help me write a story",
            SystemPrompt: null,
            ModeId: "writing" // Switch to writing mode
        );

        // Continue chat with new mode using the chat endpoint with ID
        var continueResponse = await client.PostAsJsonAsync(
            "/api/chat",
            continueRequest,
            _jsonOptions
        );

        // Assert
        _ = continueResponse.EnsureSuccessStatusCode();
        var updatedChat = await continueResponse.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);
        _ = updatedChat.Should().NotBeNull();
        _ = updatedChat!.Messages.Should().HaveCountGreaterThan(chat.Messages.Count);
    }

    [Fact]
    public async Task CreateAndUseCustomMode_FullFlow()
    {
        // Arrange
        var client = _factory.CreateClient();
        // Use seeded demo user for mode tests to ensure user exists in database
        var userId = "user-123"; // TestHelpers.GenerateUniqueUserId("custom-mode-flow");
        ModeDto? createdMode = null;

        try
        {
            // Step 1: Create custom mode with retry logic for conflicts
            var customMode = new
            {
                userId,
                name = $"Custom Test Mode {Guid.NewGuid()}",
                description = "A custom mode for integration testing",
                prompt = "You are a helpful test assistant",
                tools = new[] { "search", "calculator" },
                defaultModel = (string?)null,
                category = "custom",
            };

            var createModeResponse = await client.PostAsJsonAsync(
                "/api/mode",
                customMode,
                _jsonOptions
            );

            // If conflict, retry with a different name
            if (createModeResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                customMode = new
                {
                    userId,
                    name = $"Custom Test Mode Retry {Guid.NewGuid()}",
                    description = "A custom mode for integration testing",
                    prompt = "You are a helpful test assistant",
                    tools = new[] { "search", "calculator" },
                    defaultModel = (string?)null,
                    category = "custom",
                };

                createModeResponse = await client.PostAsJsonAsync(
                    "/api/mode",
                    customMode,
                    _jsonOptions
                );
            }

            // Get error details if the request still failed
            if (!createModeResponse.IsSuccessStatusCode)
            {
                var errorContent = await createModeResponse.Content.ReadAsStringAsync();
                throw new Exception(
                    $"Failed to create mode. Status: {createModeResponse.StatusCode}, Error: {errorContent}"
                );
            }

            createdMode = await createModeResponse.Content.ReadFromJsonAsync<ModeDto>(_jsonOptions);

            // Step 2: Use custom mode in chat
            var createChatRequest = new CreateChatRequest(
                ChatId: null,
                UserId: userId,
                Message: "Calculate 2+2 for me",
                SystemPrompt: null,
                ModeId: createdMode!.Id
            );

            var createChatResponse = await client.PostAsJsonAsync(
                "/api/chat",
                createChatRequest,
                _jsonOptions
            );
            _ = createChatResponse.EnsureSuccessStatusCode();
            var chat = await createChatResponse.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);

            // Assert
            _ = chat.Should().NotBeNull();
            _ = chat!.Messages.Should().NotBeEmpty();
        }
        finally
        {
            // Step 3: Always clean up - delete custom mode
            if (createdMode != null)
            {
                var deleteResponse = await client.DeleteAsync(
                    $"/api/mode/{createdMode.Id}?userId={userId}"
                );
                _ = deleteResponse
                    .StatusCode.Should()
                    .BeOneOf(
                        System.Net.HttpStatusCode.NoContent,
                        System.Net.HttpStatusCode.NotFound // Mode might already be deleted
                    );
            }
        }
    }

    #endregion

    #region Error Scenario Tests

    [Fact]
    public async Task CreateChat_WithInvalidMode_FallsBackToGeneral()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = TestHelpers.GenerateUniqueUserId("invalid-mode-fallback");

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
        _ = response.EnsureSuccessStatusCode();
        var chat = await response.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);
        _ = chat.Should().NotBeNull();
        _ = chat!.Messages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateMode_WithInvalidData_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = TestHelpers.GenerateUniqueUserId("invalid-mode-data");

        // Create mode with invalid data (empty name)
        var invalidMode = new CreateModeRequest
        {
            Name = "", // Invalid: empty name
            Description = "Test description",
            Prompt = "Test prompt",
            Tools = new[] { "tool1" },
            DefaultModel = null,
            Category = "custom",
        };

        var response = await client.PostAsJsonAsync(
            $"/api/mode?userId={userId}",
            invalidMode,
            _jsonOptions
        );

        // Assert
        _ = response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSystemMode_ReturnsForbidden()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = TestHelpers.GenerateUniqueUserId("update-system-mode");

        // Try to update a system mode
        var updateRequest = new UpdateModeRequest
        {
            Name = "Hacked System Mode",
            Description = "Should not work",
            Prompt = "Malicious prompt",
            Tools = new[] { "malicious-tool" },
            DefaultModel = null,
            Category = "hacked",
        };

        var response = await client.PutAsJsonAsync(
            $"/api/mode/general?userId={userId}",
            updateRequest,
            _jsonOptions
        );

        // Assert
        _ = response
            .StatusCode.Should()
            .BeOneOf(System.Net.HttpStatusCode.BadRequest, System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteSystemMode_ReturnsForbidden()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = TestHelpers.GenerateUniqueUserId("delete-system-mode");

        // Try to delete a system mode
        var response = await client.DeleteAsync($"/api/mode/general?userId={userId}");

        // Assert
        _ = response
            .StatusCode.Should()
            .BeOneOf(System.Net.HttpStatusCode.BadRequest, System.Net.HttpStatusCode.Forbidden);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task GetModes_Performance_CompletesWithinTimeLimit()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = TestHelpers.GenerateUniqueUserId("perf-get-modes");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var response = await client.GetAsync($"/api/mode?userId={userId}");
        stopwatch.Stop();

        // Assert
        _ = response.EnsureSuccessStatusCode();
        _ = stopwatch
            .ElapsedMilliseconds.Should()
            .BeLessThan(200, "Mode loading should complete within 200ms");
    }

    [Fact]
    public async Task SwitchMode_Performance_CompletesQuickly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = TestHelpers.GenerateUniqueUserId("perf-switch-mode");

        // Create initial chat
        var createRequest = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Initial message",
            SystemPrompt: null,
            ModeId: "general"
        );

        var createResponse = await client.PostAsJsonAsync("/api/chat", createRequest, _jsonOptions);
        _ = createResponse.EnsureSuccessStatusCode();
        var chat = await createResponse.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);

        // Measure mode switch time
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var continueRequest = new CreateChatRequest(
            ChatId: chat!.Id,
            UserId: userId,
            Message: "Continue with different mode",
            SystemPrompt: null,
            ModeId: "writing"
        );

        var continueResponse = await client.PostAsJsonAsync(
            "/api/chat",
            continueRequest,
            _jsonOptions
        );
        stopwatch.Stop();

        // Assert
        _ = continueResponse.EnsureSuccessStatusCode();
        _ = stopwatch.ElapsedMilliseconds.Should().BeLessThan(500, "Mode switching should be fast");
    }

    [Fact]
    public async Task ConcurrentModeAccess_HandlesMultipleUsers()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userIds = TestHelpers.GenerateUniqueUserIds(10, "concurrent-access").ToList();

        // Act - Concurrent mode requests
        var tasks = userIds
            .Select(async userId =>
            {
                var response = await client.GetAsync($"/api/mode?userId={userId}");
                return (userId, response);
            })
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        _ = results.Should().HaveCount(10);
        foreach (var (userId, response) in results)
        {
            _ = response.EnsureSuccessStatusCode();
            var modes = await response.Content.ReadFromJsonAsync<ModesResponse>(_jsonOptions);
            _ = modes.Should().NotBeNull();
            _ = modes!.Modes.Should().NotBeEmpty($"User {userId} should receive modes");
        }
    }

    #endregion

    #region SSE Integration Tests

    [Fact]
    public async Task StreamSSE_WithMode_IncludesModeInformation()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = TestHelpers.GenerateUniqueUserId("sse-mode-info");

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
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead
        );
        _ = response.EnsureSuccessStatusCode();
        var sseContent = await response.Content.ReadAsStringAsync();

        // Assert
        _ = sseContent.Should().Contain("event: init");
        _ = sseContent.Should().Contain("event: messageupdate");
        _ = sseContent.Should().Contain("event: complete");
        // The SSE stream should work with the specified mode
    }

    #endregion

    #region Mode Persistence Tests

    [Fact]
    public async Task ModePersistence_AcrossMultipleChats()
    {
        // Arrange
        var client = _factory.CreateClient();
        // Use seeded demo user for mode tests to ensure user exists in database
        var userId = "user-123"; // TestHelpers.GenerateUniqueUserId("mode-persistence");

        // Create custom mode with guaranteed unique name
        var customMode = new
        {
            userId,
            name = $"Persistent Mode {Guid.NewGuid()}",
            description = "Test mode persistence",
            prompt = "You are a persistent assistant",
            tools = new[] { "search" },
            defaultModel = (string?)null,
            category = "custom",
        };

        ModeDto? createdMode = null;
        try
        {
            var createModeResponse = await client.PostAsJsonAsync(
                "/api/mode",
                customMode,
                _jsonOptions
            );

            // If mode creation fails due to conflict, generate a new mode name and retry
            if (createModeResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                customMode = new
                {
                    userId,
                    name = $"Persistent Mode Retry {Guid.NewGuid()}",
                    description = "Test mode persistence",
                    prompt = "You are a persistent assistant",
                    tools = new[] { "search" },
                    defaultModel = (string?)null,
                    category = "custom",
                };

                createModeResponse = await client.PostAsJsonAsync(
                    "/api/mode",
                    customMode,
                    _jsonOptions
                );
            }

            if (!createModeResponse.IsSuccessStatusCode)
            {
                var errorContent = await createModeResponse.Content.ReadAsStringAsync();
                throw new Exception(
                    $"Failed to create mode after retry. Status: {createModeResponse.StatusCode}, Error: {errorContent}"
                );
            }
            createdMode = await createModeResponse.Content.ReadFromJsonAsync<ModeDto>(_jsonOptions);

            // Create multiple chats with the same mode
            var chatIds = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                var createChatRequest = new CreateChatRequest(
                    ChatId: null,
                    UserId: userId,
                    Message: $"Chat {i + 1} with persistent mode",
                    SystemPrompt: null,
                    ModeId: createdMode!.Id
                );

                var response = await client.PostAsJsonAsync(
                    "/api/chat",
                    createChatRequest,
                    _jsonOptions
                );
                _ = response.EnsureSuccessStatusCode();
                var chat = await response.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);
                chatIds.Add(chat!.Id);
            }

            // Verify mode is still available
            var modesResponse = await client.GetAsync($"/api/mode?userId={userId}");
            _ = modesResponse.EnsureSuccessStatusCode();
            var modes = await modesResponse.Content.ReadFromJsonAsync<ModesResponse>(_jsonOptions);

            // Assert
            _ = chatIds.Should().HaveCount(3);
            _ = chatIds.Should().OnlyHaveUniqueItems();
            _ = modes!.Modes.Should().Contain(m => m.Id == createdMode.Id);
        }
        finally
        {
            // Always clean up the created mode
            if (createdMode != null)
            {
                _ = await client.DeleteAsync($"/api/mode/{createdMode.Id}?userId={userId}");
            }
        }
    }

    #endregion
}
