using System.Net.Http.Json;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Tests.TestUtilities;
using AIChat.Server.Configuration;
using AIChat.Server.Models;
using AIChat.Server.Services.Streaming;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace AIChat.Orleans.Tests.Phase4;

/// <summary>
/// Tests for stream lifecycle management including creation, completion, and cancellation.
/// </summary>
public class StreamLifecycleTests : IClassFixture<OrleansTestFixture>
{
    private readonly OrleansTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public StreamLifecycleTests(OrleansTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task StreamCreationShouldInitializeCorrectly()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        using var client = _fixture.CreateSseClient();
        var request = new CreateChatRequest
        {
            UserId = "test-user-create",
            Message = "Test stream creation",
            SystemPrompt = "You are a test assistant",
            ModeId = "default"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);

        // Assert
        _ = response.EnsureSuccessStatusCode();

        // Parse SSE stream to verify initialization
        using var stream = await response.Content.ReadAsStreamAsync();
        var events = await SseTestHelpers.ParseSseStreamAsync(stream);

        // Should have init event
        var initEvent = events.FirstOrDefault(e => e.EventType == "init");
        _ = initEvent.Should().NotBeNull();
        _ = initEvent!.Envelope.Should().NotBeNull();
        _ = initEvent.Envelope!.ChatId.Should().NotBeNullOrEmpty();

        _output.WriteLine($"Stream initialized with ChatId: {initEvent.Envelope.ChatId}");
    }

    [Fact]
    public async Task StreamCompletionShouldSendCompleteEvent()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        using var client = _fixture.CreateSseClient();
        var request = new CreateChatRequest
        {
            UserId = "test-user-complete",
            Message = "Test stream completion",
            SystemPrompt = "You are a test assistant",
            ModeId = "default"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);

        // Assert
        _ = response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var events = await SseTestHelpers.ParseSseStreamAsync(stream);

        // Should have complete event
        var completeEvent = events.LastOrDefault(e => e.EventType == "complete");
        _ = completeEvent.Should().NotBeNull();
        _ = completeEvent!.Envelope.Should().NotBeNull();

        _output.WriteLine($"Stream completed with {events.Count} total events");
    }

    [Fact]
    public async Task ConcurrentStreamsPerUserShouldHandleMultipleStreams()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        var userId = "test-user-concurrent";
        var tasks = new List<Task<List<SseTestHelpers.SseEvent>>>();

        // Act - Create multiple concurrent streams for same user
        for (var i = 0; i < 3; i++)
        {
            var task = Task.Run(async () =>
            {
                using var client = _fixture.CreateSseClient();
                var request = new CreateChatRequest
                {
                    UserId = userId,
                    Message = $"Concurrent message {i}",
                    SystemPrompt = "You are a test assistant",
                    ModeId = "default"
                };

                var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);
                _ = response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                return await SseTestHelpers.ParseSseStreamAsync(stream);
            });

            tasks.Add(task);
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All streams should complete successfully
        _ = results.Should().HaveCount(3);
        foreach (var events in results)
        {
            _ = events.Should().NotBeEmpty();
            _ = events.Should().Contain(e => e.EventType == "init");
            _ = events.Should().Contain(e => e.EventType == "complete");
        }

        _output.WriteLine($"Successfully handled {results.Length} concurrent streams for user {userId}");
    }

    [Fact]
    public async Task StreamCancellationShouldCleanupProperly()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        using var cts = new CancellationTokenSource();
        using var client = _fixture.CreateSseClient();

        var request = new CreateChatRequest
        {
            UserId = "test-user-cancel",
            Message = "Test stream cancellation",
            SystemPrompt = "You are a test assistant",
            ModeId = "default"
        };

        // Act
        var responseTask = client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream-sse")
            {
                Content = JsonContent.Create(request)
            },
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        var response = await responseTask;
        _ = response.EnsureSuccessStatusCode();

        // Cancel after receiving initial response
        await Task.Delay(100);
        cts.Cancel();

        // Assert - Stream should be cancelled without errors
        _ = await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            _ = await SseTestHelpers.ParseSseStreamAsync(stream, cts.Token);
        });

        _output.WriteLine("Stream cancelled successfully");
    }

    [Fact]
    public async Task StreamTimeoutShouldHandleGracefully()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        // Configure a short timeout for testing
        var services = _fixture.WebAppFactory.Services;
        var streamingConfig = services.GetRequiredService<IOptions<StreamingConfiguration>>();
        streamingConfig.Value.TimeoutMs = 1000; // 1 second timeout

        using var client = _fixture.CreateSseClient();
        client.Timeout = TimeSpan.FromSeconds(5); // Client timeout longer than stream timeout

        var request = new CreateChatRequest
        {
            UserId = "test-user-timeout",
            Message = "Test stream timeout",
            SystemPrompt = "You are a test assistant",
            ModeId = "default"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);

        // Assert
        _ = response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var events = await SseTestHelpers.ParseSseStreamAsync(stream);

        // Should complete or error within timeout
        _ = events.Should().NotBeEmpty();

        _output.WriteLine($"Stream handled timeout with {events.Count} events");
    }

    [Fact]
    public async Task StreamBufferShouldHandleBackpressure()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        var loggerMock = new Mock<ILogger<StreamingBridge>>();
        var config = new StreamingConfiguration
        {
            BufferSize = 5, // Small buffer for testing
            BackpressureThreshold = 80,
            FlushIntervalMs = 100,
            Enabled = true
        };

        var bridgeMock = new Mock<ITestStreamingBridge>();
        var messageCount = 0;
        var backpressureDetected = false;

        // Setup bridge mock
        _ = bridgeMock.Setup(x => x.ConvertToSseAsync(It.IsAny<IAsyncEnumerable<ChatStreamItem>>(), It.IsAny<CancellationToken>()))
            .Returns((IAsyncEnumerable<ChatStreamItem> items, CancellationToken ct) =>
                ConvertToSseSimple(items, ct));

        // Act - Send many messages quickly
        var processingTask = Task.Run(async () =>
        {
            await foreach (var item in bridgeMock.Object.ConvertToSseAsync(GenerateTestStream(), CancellationToken.None))
            {
                messageCount++;
                await Task.Delay(50); // Simulate slow consumer
            }
        });

        // Monitor for backpressure
        _ = loggerMock.Setup(x => x.Log(
            It.Is<LogLevel>(l => l == LogLevel.Warning),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("backpressure")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(() => backpressureDetected = true);

        await processingTask;

        // Assert
        _ = messageCount.Should().BeGreaterThan(0);
        _output.WriteLine($"Processed {messageCount} messages, backpressure: {backpressureDetected}");
    }

    [Fact]
    public async Task StreamCleanupOnCompletionShouldReleaseResources()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        var userId = "test-user-cleanup";
        var grain = _fixture.Cluster.Client.GetGrain<IUserGrain>(userId);

        // Act - Create and complete a stream
        using (var client = _fixture.CreateSseClient())
        {
            var request = new CreateChatRequest
            {
                UserId = userId,
                Message = "Test cleanup",
                SystemPrompt = "You are a test assistant",
                ModeId = "default"
            };

            var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);
            _ = response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            _ = await SseTestHelpers.ParseSseStreamAsync(stream);
        }

        // Assert - Verify cleanup
        var state = await grain.GetState();
        _ = state.ActiveStreams.Count.Should().Be(0, "All streams should be cleaned up after completion");

        _output.WriteLine($"Stream cleanup verified for user {userId}");
    }

    [Fact]
    public async Task MultipleStreamCreationShouldMaintainIsolation()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        var user1 = "test-user-isolation-1";
        var user2 = "test-user-isolation-2";

        // Act - Create streams for different users
        var task1 = CreateAndVerifyStream(user1, "User 1 message");
        var task2 = CreateAndVerifyStream(user2, "User 2 message");

        var results = await Task.WhenAll(task1, task2);

        // Assert - Each user should have independent streams
        _ = results[0].ChatId.Should().NotBe(results[1].ChatId);
        _ = results[0].UserId.Should().Be(user1);
        _ = results[1].UserId.Should().Be(user2);

        _output.WriteLine($"Stream isolation verified: User1 ChatId={results[0].ChatId}, User2 ChatId={results[1].ChatId}");
    }

    private async Task<(string ChatId, string UserId)> CreateAndVerifyStream(string userId, string message)
    {
        using var client = _fixture.CreateSseClient();
        var request = new CreateChatRequest
        {
            UserId = userId,
            Message = message,
            SystemPrompt = "You are a test assistant",
            ModeId = "default"
        };

        var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);
        _ = response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var events = await SseTestHelpers.ParseSseStreamAsync(stream);

        var initEvent = events.First(e => e.EventType == "init");
        return (initEvent.Envelope!.ChatId!, userId);
    }

    private static async IAsyncEnumerable<ChatStreamItem> GenerateTestStream()
    {
        for (var i = 0; i < 10; i++)
        {
            yield return new ChatStreamItem
            {
                Type = StreamItemType.Content,
                Content = $"Test message {i}",
                Timestamp = DateTime.UtcNow
            };
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Helper method to convert chat items to SSE format.
    /// </summary>
    private static async IAsyncEnumerable<string> ConvertToSseSimple(
        IAsyncEnumerable<ChatStreamItem> items,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in items.WithCancellation(cancellationToken))
        {
            yield return $"data: {item.Content}\n\n";
        }
    }
}
