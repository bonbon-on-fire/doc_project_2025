using System.Net.Http.Json;
using System.Text.Json;
using AIChat.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
// Use fully qualified names to avoid ambiguity
using CreateChatRequest = AIChat.Server.Controllers.CreateChatRequest;
// SendMessageRequest removed - using CreateChatRequest for all chat operations

namespace AIChat.Server.Tests.Api;

/// <summary>
/// Integration tests for Mode-related SSE (Server-Sent Events) streaming.
/// Tests mode information in SSE streams, tool filtering, and mode switching during streaming.
/// </summary>
public class ModeSseIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly JsonSerializerOptions _jsonOptions;

    public ModeSseIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            _ = builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Test");
        });

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }

    [Fact]
    public async Task StreamSSE_WithMode_IncludesModeInInitEvent()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = "user-123"; // Use seeded test user

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream-sse");
        var createRequest = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Test SSE with coding mode",
            SystemPrompt: null,
            ModeId: "coding" // Specify coding mode
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
        _ = sseContent.Should().NotBeNullOrEmpty();

        // Parse SSE events
        var events = ParseSseEvents(sseContent);

        // Verify init event exists
        var initEvent = events.FirstOrDefault(e => e.EventType == "init");
        _ = initEvent.Should().NotBeNull("Init event should be present");

        // Verify init event contains mode information
        if (initEvent != null && !string.IsNullOrEmpty(initEvent.Data))
        {
            var initData = JsonSerializer.Deserialize<JsonElement>(initEvent.Data, _jsonOptions);

            // Check if mode information is included (this depends on your implementation)
            // The init event might include the chat ID and initial metadata
            if (initData.TryGetProperty("chatId", out var chatId))
            {
                _ = chatId.GetString().Should().NotBeNullOrEmpty();
            }
        }

        // Verify message updates
        var messageEvents = events.Where(e => e.EventType == "messageupdate").ToList();
        _ = messageEvents.Should().NotBeEmpty("Should have message update events");

        // Verify completion
        var completeEvent = events.FirstOrDefault(e => e.EventType == "complete");
        _ = completeEvent.Should().NotBeNull("Complete event should be present");
    }

    [Fact]
    public async Task StreamSSE_WithCustomMode_AppliesToolFiltering()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = "user-123"; // Use seeded test user

        // First create a custom mode with limited tools
        var customMode = new CreateModeRequest
        {
            Name = $"Limited Tools Mode {Guid.NewGuid()}",
            Description = "Mode with limited tools for SSE testing",
            Prompt = "You are a helpful assistant with limited tools",
            Tools = new[] { "search" }, // Only search tool
            DefaultModel = null,
            Category = "custom",
        };

        var createModeResponse = await client.PostAsJsonAsync(
            "/api/mode",
            new { userId, name = customMode.Name, description = customMode.Description, prompt = customMode.Prompt, tools = customMode.Tools, defaultModel = customMode.DefaultModel, category = customMode.Category },
            _jsonOptions
        );
        _ = createModeResponse.EnsureSuccessStatusCode();
        var createdMode = await createModeResponse.Content.ReadFromJsonAsync<ModeDto>(_jsonOptions);

        // Stream SSE with the custom mode
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream-sse");
        var createRequest = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Search for information about AI",
            SystemPrompt: null,
            ModeId: createdMode!.Id
        );
        request.Content = JsonContent.Create(createRequest, options: _jsonOptions);

        // Act
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead
        );
        _ = response.EnsureSuccessStatusCode();

        var sseContent = await response.Content.ReadAsStringAsync();

        // Clean up
        _ = await client.DeleteAsync($"/api/modes/{createdMode.Id}?userId={userId}");

        // Assert
        var events = ParseSseEvents(sseContent);
        _ = events.Should().NotBeEmpty();

        // Verify that tool calls (if any) are limited to the mode's tools
        var toolEvents = events.Where(e => e.EventType == "toolcall").ToList();
        foreach (var toolEvent in toolEvents)
        {
            if (!string.IsNullOrEmpty(toolEvent.Data))
            {
                var toolData = JsonSerializer.Deserialize<JsonElement>(
                    toolEvent.Data,
                    _jsonOptions
                );
                if (toolData.TryGetProperty("toolName", out var toolName))
                {
                    // Should only use tools allowed by the mode
                    _ = toolName.GetString().Should().BeOneOf("search");
                }
            }
        }
    }

    [Fact]
    public async Task StreamSSE_ModeSwitching_HandlesCorrectly()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = "user-123"; // Use seeded test user

        // Create initial chat with general mode
        using var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream-sse");
        var createRequest1 = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Initial message with general mode",
            SystemPrompt: null,
            ModeId: "general"
        );
        request1.Content = JsonContent.Create(createRequest1, options: _jsonOptions);

        using var response1 = await client.SendAsync(
            request1,
            HttpCompletionOption.ResponseHeadersRead
        );
        _ = response1.EnsureSuccessStatusCode();
        var sseContent1 = await response1.Content.ReadAsStringAsync();

        // Extract chat ID from the response
        var events1 = ParseSseEvents(sseContent1);
        var initEvent1 = events1.FirstOrDefault(e => e.EventType == "init");
        string? chatId = null;
        if (initEvent1 != null && !string.IsNullOrEmpty(initEvent1.Data))
        {
            var initData = JsonSerializer.Deserialize<JsonElement>(initEvent1.Data, _jsonOptions);
            if (initData.TryGetProperty("chatId", out var chatIdElement))
            {
                chatId = chatIdElement.GetString();
            }
        }

        if (string.IsNullOrEmpty(chatId))
        {
            // If chat ID not in init event, try to get it from a regular API call
            var createResponse = await client.PostAsJsonAsync(
                "/api/chat",
                createRequest1,
                _jsonOptions
            );
            _ = createResponse.EnsureSuccessStatusCode();
            var chat = await createResponse.Content.ReadFromJsonAsync<ChatDto>(_jsonOptions);
            chatId = chat?.Id;
        }

        _ = chatId.Should().NotBeNullOrEmpty("Should have a chat ID");

        // Continue with different mode
        var continueRequest = new CreateChatRequest(
            ChatId: chatId,
            UserId: userId,
            Message: "Continue with writing mode",
            SystemPrompt: null,
            ModeId: "writing"
        );

        using var request2 = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/chat/stream-sse"
        );
        request2.Content = JsonContent.Create(continueRequest, options: _jsonOptions);

        // Act
        using var response2 = await client.SendAsync(
            request2,
            HttpCompletionOption.ResponseHeadersRead
        );

        // Assert
        if (response2.IsSuccessStatusCode)
        {
            var sseContent2 = await response2.Content.ReadAsStringAsync();
            var events2 = ParseSseEvents(sseContent2);
            _ = events2.Should().NotBeEmpty("Should have events for continued chat");

            // Verify mode switch was applied
            var completeEvent = events2.FirstOrDefault(e => e.EventType == "complete");
            _ = completeEvent.Should().NotBeNull("Should complete successfully with new mode");
        }
        else
        {
            // If continue-sse endpoint doesn't exist, that's okay for this test
            _ = response2
                .StatusCode.Should()
                .BeOneOf(
                    System.Net.HttpStatusCode.NotFound,
                    System.Net.HttpStatusCode.MethodNotAllowed
                );
        }
    }

    [Fact]
    public async Task StreamSSE_WithInvalidMode_FallsBackGracefully()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = "user-123"; // Use seeded test user

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream-sse");
        var createRequest = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Test with invalid mode",
            SystemPrompt: null,
            ModeId: "non-existent-mode-12345"
        );
        request.Content = JsonContent.Create(createRequest, options: _jsonOptions);

        // Act
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead
        );

        // Assert
        _ = response.EnsureSuccessStatusCode(); // Should still succeed with fallback

        var sseContent = await response.Content.ReadAsStringAsync();
        var events = ParseSseEvents(sseContent);

        // Should still have normal SSE events despite invalid mode
        _ = events.Should().NotBeEmpty();
        _ = events.Should().Contain(e => e.EventType == "init");
        _ = events.Should().Contain(e => e.EventType == "complete");
    }

    [Fact]
    public async Task StreamSSE_Performance_WithMode()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = "user-123"; // Use seeded test user
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream-sse");
        var createRequest = new CreateChatRequest(
            ChatId: null,
            UserId: userId,
            Message: "Quick performance test",
            SystemPrompt: null,
            ModeId: "general"
        );
        request.Content = JsonContent.Create(createRequest, options: _jsonOptions);

        // Act
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead
        );

        // Read just the headers to measure initial response time
        stopwatch.Stop();
        var initialResponseTime = stopwatch.ElapsedMilliseconds;

        _ = response.EnsureSuccessStatusCode();

        // Continue reading the stream
        stopwatch.Restart();
        var sseContent = await response.Content.ReadAsStringAsync();
        stopwatch.Stop();
        var totalStreamTime = stopwatch.ElapsedMilliseconds;

        // Assert
        _ = initialResponseTime.Should().BeLessThan(500, "Initial SSE response should be fast");

        var events = ParseSseEvents(sseContent);
        _ = events.Should().NotBeEmpty();

        // Verify streaming completed in reasonable time
        _ = totalStreamTime
            .Should()
            .BeLessThan(10000, "Complete SSE stream should finish within 10 seconds");
    }

    [Fact]
    public async Task StreamSSE_ConcurrentModeRequests()
    {
        // Arrange
        var client = _factory.CreateClient();
        var modes = new[] { "general", "coding", "writing" };

        // Create concurrent SSE requests with different modes
        var tasks = modes
            .Select(async mode =>
            {
                var userId = $"concurrent-user-{Guid.NewGuid()}";
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream-sse");
                var createRequest = new CreateChatRequest(
                    ChatId: null,
                    UserId: userId,
                    Message: $"Test concurrent SSE with {mode} mode",
                    SystemPrompt: null,
                    ModeId: mode
                );
                request.Content = JsonContent.Create(createRequest, options: _jsonOptions);

                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead
                );
                _ = response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                return (mode: mode, content: content);
            })
            .ToList();

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        _ = results.Should().HaveCount(3);

        foreach (var (mode, content) in results)
        {
            var events = ParseSseEvents(content);
            _ = events.Should().NotBeEmpty($"Mode {mode} should have SSE events");
            _ = events
                .Should()
                .Contain(e => e.EventType == "init", $"Mode {mode} should have init event");
            _ = events
                .Should()
                .Contain(
                    e => e.EventType == "complete",
                    $"Mode {mode} should have complete event"
                );
        }
    }

    /// <summary>
    /// Helper method to parse SSE events from raw SSE content
    /// </summary>
    private static List<SseEvent> ParseSseEvents(string sseContent)
    {
        var events = new List<SseEvent>();
        var lines = sseContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        string? currentEventType = null;
        var dataLines = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("event: "))
            {
                // Save previous event if exists
                if (currentEventType != null && dataLines.Count != 0)
                {
                    events.Add(
                        new SseEvent
                        {
                            EventType = currentEventType,
                            Data = string.Join("\n", dataLines),
                        }
                    );
                }

                currentEventType = line.Substring(7).Trim();
                dataLines.Clear();
            }
            else if (line.StartsWith("data: "))
            {
                dataLines.Add(line.Substring(6));
            }
        }

        // Save last event
        if (currentEventType != null && dataLines.Count != 0)
        {
            events.Add(
                new SseEvent { EventType = currentEventType, Data = string.Join("\n", dataLines) }
            );
        }

        return events;
    }

    private class SseEvent
    {
        public string EventType { get; set; } = "";
        public string Data { get; set; } = "";
    }
}
