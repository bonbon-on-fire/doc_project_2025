using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIChat.Orleans.Tests.TestUtilities;
using AIChat.Orleans.Tests.TestUtilities.Base;
using AIChat.Orleans.Tests.TestUtilities.Builders;
using AIChat.Server.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AIChat.Orleans.Tests.Phase4;

/// <summary>
/// Tests for SSE routing decisions between Orleans and direct processing.
/// Refactored to use base class and builder patterns for improved maintainability.
/// </summary>
public class SseRoutingTests : OrleansTestBase
{
    public SseRoutingTests(OrleansTestFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    [Fact]
    public async Task StreamChatCompletionSse_WithOrleansEnabled_ShouldRouteToOrleans()
    {
        // Arrange
        Fixture.OrleansEnabled = true;
        await Fixture.InitializeAsync();
        
        var request = CreateChatRequestBuilder.Create()
            .ForOrleansRouting()
            .Build();

        // Act
        var response = await MakeStreamRequestAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertOrleansRouted(response);
    }

    [Fact]
    public async Task StreamChatCompletionSse_WithOrleansDisabled_ShouldUseDirect()
    {
        // Arrange
        Fixture.OrleansEnabled = false;
        await Fixture.InitializeAsync();
        
        var request = CreateChatRequestBuilder.Create()
            .ForDirectProcessing()
            .Build();

        // Act
        var response = await MakeStreamRequestAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertDirectProcessing(response);
    }

    [Fact]
    public async Task StreamChatCompletionSse_WithoutSseHeaders_ShouldReturn400()
    {
        // Arrange
        await Fixture.InitializeAsync();
        
        using var client = CreateStandardClient(); // No SSE headers
        var request = CreateChatRequestBuilder.Create().Build();

        // Act
        var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var content = await response.Content.ReadAsStringAsync();
        LogTestStep("Error response: {0}", content);
    }

    [Fact]
    public async Task StreamChatCompletionSse_WithSignalRProtocolHeader_ShouldRouteToSignalR()
    {
        // Arrange
        await Fixture.InitializeAsync();
        
        using var client = Fixture.WebAppFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Preferred-Protocol", "SignalR");
        
        var request = CreateChatRequestBuilder.Create()
            .WithUserId("test-user-signalr")
            .WithMessage("Test message for SignalR")
            .Build();

        // Act
        var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
        result.Should().ContainKey("OperationId");
        result.Should().ContainKey("Status");
        
        LogTestStep("SignalR operation initiated: {0}", content);
    }

    [Fact]
    public async Task StreamChatCompletionSse_OrleansFallback_ShouldUseDirect()
    {
        // Arrange
        Fixture.OrleansEnabled = true;
        await Fixture.InitializeAsync();
        
        // Simulate Orleans failure by stopping the cluster
        await Fixture.Cluster.StopAllSilosAsync();
        
        using var client = CreateSseClient();
        var request = CreateChatRequestBuilder.Create()
            .WithUserId("test-user-fallback")
            .WithMessage("Test message for fallback")
            .Build();

        // Act
        var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Should fallback to direct processing when Orleans fails
        response.Headers.Should().ContainKey("X-Processing-Mode");
        response.Headers.GetValues("X-Processing-Mode").First().Should().Be("direct");

        LogTestStep("Successfully fell back to direct processing after Orleans failure");
    }

    [Fact]
    public async Task StreamChatCompletionSse_WithResilientStreaming_ShouldUseResilientManager()
    {
        // Arrange
        Fixture.OrleansEnabled = true;
        Fixture.ResilientStreamingEnabled = true;
        await Fixture.InitializeAsync();
        
        using var client = CreateSseClient();
        var request = CreateChatRequestBuilder.Create()
            .WithUserId("test-user-resilient")
            .WithMessage("Test message for resilient streaming")
            .Build();

        // Act
        var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Orleans-Routed");
        response.Headers.GetValues("X-Orleans-Routed").First().Should().Be("true");
        
        // Verify resilient streaming is being used by checking service registration
        var resilientManager = Fixture.WebAppFactory.Services.GetService<Server.Services.Streaming.IResilientStreamManager>();
        resilientManager.Should().NotBeNull();

        LogTestStep("Resilient streaming enabled and used for Orleans routing");
    }

    [Fact]
    public async Task StreamChatCompletionSse_MultipleRoutingDecisions_ShouldBeConsistent()
    {
        // Arrange
        Fixture.OrleansEnabled = true;
        await Fixture.InitializeAsync();
        
        using var client = CreateSseClient();
        var routingResults = new List<string>();

        // Act - Make multiple requests
        for (int i = 0; i < 5; i++)
        {
            var request = CreateChatRequestBuilder.Create()
                .WithUserId($"test-user-{i}")
                .WithMessage($"Test message {i}")
                .Build();

            var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);
            response.EnsureSuccessStatusCode();
            
            var routingMode = response.Headers.GetValues("X-Processing-Mode").First();
            routingResults.Add(routingMode);
        }

        // Assert - All requests should use the same routing
        routingResults.Should().AllBe("orleans");
        LogTestStep("All {0} requests consistently routed to Orleans", routingResults.Count);
    }

    [Fact]
    public async Task StreamChatCompletionSse_InvalidUserId_ShouldReturn400()
    {
        // Arrange
        Fixture.OrleansEnabled = true;
        await Fixture.InitializeAsync();
        
        var request = CreateChatRequestBuilder.Create()
            .WithInvalidData()
            .Build();

        // Act
        var response = await MakeStreamRequestAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("UserId");
        LogTestStep("Validation error for empty UserId: {0}", content);
    }

    [Fact]
    public async Task StreamChatCompletionSse_WithExistingChatId_ShouldContinueConversation()
    {
        // Arrange
        await Fixture.InitializeAsync();
        
        using var client = CreateSseClient();
        var existingChatId = Guid.NewGuid().ToString();
        
        var request = CreateChatRequestBuilder.Create()
            .WithChatId(existingChatId)
            .WithUserId("test-user")
            .WithMessage("Continue conversation")
            .Build();

        // Act
        var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Processing-Mode");
        
        LogTestStep("Continued conversation with ChatId: {0}", existingChatId);
    }
}