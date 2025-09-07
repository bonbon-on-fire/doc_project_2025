using System.Net.Http.Json;
using AIChat.Orleans.Tests.TestUtilities;
using AIChat.Server.Configuration;
using AIChat.Server.Models;
using AIChat.Server.Services.Streaming;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Polly.CircuitBreaker;
using Xunit;
using Xunit.Abstractions;

namespace AIChat.Orleans.Tests.Phase4;

/// <summary>
/// Tests for stream recovery scenarios including reconnection, partial recovery, and circuit breaking.
/// </summary>
public class RecoveryScenarioTests : IClassFixture<OrleansTestFixture>
{
    private readonly OrleansTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public RecoveryScenarioTests(OrleansTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task ReconnectionAfterFailure_ShouldRecoverStream()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        _fixture.ResilientStreamingEnabled = true;
        await _fixture.InitializeAsync();

        var userId = "test-user-reconnect";
        var attemptCount = 0;
        
        // Simulate a failure on first attempt
        var mockBridgeFactory = new Mock<ITestStreamingBridgeFactory>();
        mockBridgeFactory
            .Setup(x => x.CreateBridge())
            .Returns(() =>
            {
                attemptCount++;
                if (attemptCount == 1)
                {
                    throw new InvalidOperationException("Simulated connection failure");
                }
                
                var services = _fixture.WebAppFactory.Services;
                var logger = services.GetRequiredService<ILogger<StreamingBridge>>();
                var config = services.GetRequiredService<IOptions<StreamingConfiguration>>();
                return new StreamingBridge(logger, config);
            });

        // Replace factory in DI container
        var serviceProvider = _fixture.WebAppFactory.Services;
        var resilientManager = new TestResilientStreamManager(
            serviceProvider.GetRequiredService<ILogger<ResilientStreamManager>>(),
            mockBridgeFactory.Object,
            serviceProvider.GetRequiredService<IOptions<ResilientStreamingConfiguration>>());

        // Act
        var streamId = $"stream-{Guid.NewGuid()}";
        var recoveryTask = Task.Run(async () =>
        {
            await resilientManager.ProcessStreamWithRecoveryAsync(
                streamId,
                userId,
                async (bridge, ct) =>
                {
                    // Simulate stream processing
                    await Task.Delay(100, ct);
                    return "Success";
                },
                CancellationToken.None);
        });

        await recoveryTask;

        // Assert
        attemptCount.Should().Be(2, "Should have retried after failure");
        var metrics = await resilientManager.GetMetricsAsync();
        metrics.TotalRecoveryAttempts.Should().BeGreaterThan(0);
        metrics.SuccessfulRecoveries.Should().BeGreaterThan(0);

        _output.WriteLine($"Recovery successful after {attemptCount} attempts");
    }

    [Fact]
    public async Task PartialMessageRecovery_ShouldResumeFromLastPoint()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        _fixture.ResilientStreamingEnabled = true;
        await _fixture.InitializeAsync();

        var config = new ResilientStreamingConfiguration
        {
            Enabled = true,
            PartialMessageBufferSize = 5,
            MaxRetryAttempts = 3,
            RetryDelayMs = 100
        };

        var logger = new Mock<ILogger<ResilientStreamManager>>();
        var bridgeFactory = new Mock<ITestStreamingBridgeFactory>();
        
        var manager = new TestResilientStreamManager(
            logger.Object,
            bridgeFactory.Object,
            Options.Create(config));

        var messagesProcessed = new List<string>();
        var failureOccurred = false;

        // Setup bridge to fail mid-stream
        bridgeFactory
            .Setup(x => x.CreateBridge())
            .Returns(() =>
            {
                var mockBridge = new Mock<ITestStreamingBridge>();
                mockBridge
                    .Setup(b => b.ConvertToSseAsync(It.IsAny<IAsyncEnumerable<ChatStreamItem>>(), It.IsAny<CancellationToken>()))
                    .Returns(async (IAsyncEnumerable<ChatStreamItem> items, CancellationToken ct) =>
                    {
                        var count = 0;
                        await foreach (var item in items.WithCancellation(ct))
                        {
                            count++;
                            if (count == 3 && !failureOccurred)
                            {
                                failureOccurred = true;
                                throw new IOException("Simulated stream failure");
                            }
                            
                            messagesProcessed.Add(item.Content ?? "");
                            // Cannot use yield in lambda, need to return as enumerable
                        }
                    });
                
                return mockBridge.Object;
            });

        // Act
        var streamId = $"stream-{Guid.NewGuid()}";
        await manager.ProcessStreamWithRecoveryAsync(
            streamId,
            "test-user",
            async (bridge, ct) =>
            {
                var testItems = GenerateTestItems(5);
                var testBridge = bridge as ITestStreamingBridge;
                if (testBridge == null)
                    throw new InvalidOperationException("Bridge must be ITestStreamingBridge");
                    
                var sseStream = testBridge.ConvertToSseAsync(testItems, ct);
                
                await foreach (var item in sseStream)
                {
                    // Process SSE items
                }
                
                return "Completed";
            },
            CancellationToken.None);

        // Assert
        messagesProcessed.Should().HaveCount(5, "Should process all messages despite failure");
        messagesProcessed.Should().BeEquivalentTo(new[] { "Message 1", "Message 2", "Message 3", "Message 4", "Message 5" });

        _output.WriteLine($"Recovered and processed {messagesProcessed.Count} messages");
    }

    [Fact]
    public async Task CircuitBreaker_ShouldOpenAfterThreshold()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        _fixture.ResilientStreamingEnabled = true;
        await _fixture.InitializeAsync();

        var config = new ResilientStreamingConfiguration
        {
            Enabled = true,
            CircuitBreakerThreshold = 3,
            CircuitBreakerResetTimeoutMs = 5000,
            MaxRetryAttempts = 1
        };

        var logger = new Mock<ILogger<ResilientStreamManager>>();
        var bridgeFactory = new Mock<ITestStreamingBridgeFactory>();
        
        // Setup bridge to always fail
        bridgeFactory
            .Setup(x => x.CreateBridge())
            .Throws(new InvalidOperationException("Persistent failure"));

        var manager = new TestResilientStreamManager(
            logger.Object,
            bridgeFactory.Object,
            Options.Create(config));

        var failureCount = 0;
        var circuitBreakerOpened = false;

        // Act - Attempt multiple operations to trigger circuit breaker
        for (int i = 0; i < 5; i++)
        {
            try
            {
                await manager.ProcessStreamWithRecoveryAsync(
                    $"stream-{i}",
                    "test-user",
                    async (bridge, ct) =>
                    {
                        await Task.Delay(10, ct);
                        return "Success";
                    },
                    CancellationToken.None);
            }
            catch (BrokenCircuitException)
            {
                circuitBreakerOpened = true;
                _output.WriteLine($"Circuit breaker opened after attempt {i + 1}");
            }
            catch (Exception)
            {
                failureCount++;
            }
        }

        // Assert
        failureCount.Should().Be(3, "Should fail up to threshold");
        circuitBreakerOpened.Should().BeTrue("Circuit breaker should open after threshold");

        _output.WriteLine($"Circuit breaker opened after {failureCount} failures");
    }

    [Fact]
    public async Task CircuitBreaker_ShouldResetAfterTimeout()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        _fixture.ResilientStreamingEnabled = true;
        await _fixture.InitializeAsync();

        var config = new ResilientStreamingConfiguration
        {
            Enabled = true,
            CircuitBreakerThreshold = 2,
            CircuitBreakerResetTimeoutMs = 1000, // 1 second reset
            MaxRetryAttempts = 1
        };

        var logger = new Mock<ILogger<ResilientStreamManager>>();
        var bridgeFactory = new Mock<ITestStreamingBridgeFactory>();
        
        var attemptCount = 0;
        bridgeFactory
            .Setup(x => x.CreateBridge())
            .Returns(() =>
            {
                attemptCount++;
                if (attemptCount <= 2)
                {
                    throw new InvalidOperationException("Initial failures");
                }
                
                // Success after circuit reset
                var mockBridge = new Mock<ITestStreamingBridge>();
                return mockBridge.Object;
            });

        var manager = new TestResilientStreamManager(
            logger.Object,
            bridgeFactory.Object,
            Options.Create(config));

        // Act - Trigger circuit breaker
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await manager.ProcessStreamWithRecoveryAsync(
                    $"stream-fail-{i}",
                    "test-user",
                    async (bridge, ct) => await Task.FromResult("Success"),
                    CancellationToken.None);
            }
            catch { /* Expected failures */ }
        }

        // Wait for circuit reset
        await Task.Delay(1500);

        // Attempt after reset
        var success = false;
        try
        {
            await manager.ProcessStreamWithRecoveryAsync(
                "stream-success",
                "test-user",
                async (bridge, ct) =>
                {
                    success = true;
                    return await Task.FromResult("Success");
                },
                CancellationToken.None);
        }
        catch { /* Ignore */ }

        // Assert
        attemptCount.Should().Be(3);
        success.Should().BeTrue("Should succeed after circuit reset");

        _output.WriteLine("Circuit breaker reset successfully after timeout");
    }

    [Fact]
    public async Task GracefulDegradation_ShouldFallbackWhenOrleansUnavailable()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        // Stop Orleans cluster to simulate failure
        await _fixture.Cluster.StopAllSilosAsync();

        using var client = _fixture.CreateSseClient();
        var request = new CreateChatRequest
        {
            UserId = "test-user-degradation",
            Message = "Test graceful degradation",
            SystemPrompt = "You are a test assistant",
            ModeId = "default"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);

        // Assert
        response.EnsureSuccessStatusCode();
        response.Headers.GetValues("X-Processing-Mode").First().Should().Be("direct");

        using var stream = await response.Content.ReadAsStreamAsync();
        var events = await SseTestHelpers.ParseSseStreamAsync(stream);
        
        events.Should().NotBeEmpty();
        events.Should().Contain(e => e.EventType == "init");

        _output.WriteLine("Successfully degraded to direct processing when Orleans unavailable");
    }

    [Fact]
    public async Task RetryMechanism_ShouldRespectConfiguration()
    {
        // Arrange
        var config = new ResilientStreamingConfiguration
        {
            Enabled = true,
            MaxRetryAttempts = 3,
            RetryDelayMs = 100
        };

        var logger = new Mock<ILogger<ResilientStreamManager>>();
        var bridgeFactory = new Mock<ITestStreamingBridgeFactory>();
        
        var attemptCount = 0;
        bridgeFactory
            .Setup(x => x.CreateBridge())
            .Returns(() =>
            {
                attemptCount++;
                if (attemptCount < 3)
                {
                    throw new InvalidOperationException($"Failure {attemptCount}");
                }
                
                var mockBridge = new Mock<ITestStreamingBridge>();
                return mockBridge.Object;
            });

        var manager = new TestResilientStreamManager(
            logger.Object,
            bridgeFactory.Object,
            Options.Create(config));

        var startTime = DateTime.UtcNow;

        // Act
        await manager.ProcessStreamWithRecoveryAsync(
            "stream-retry",
            "test-user",
            async (bridge, ct) => await Task.FromResult("Success"),
            CancellationToken.None);

        var duration = DateTime.UtcNow - startTime;

        // Assert
        attemptCount.Should().Be(3, "Should retry up to configured limit");
        duration.TotalMilliseconds.Should().BeGreaterThan(200, "Should respect retry delays");

        _output.WriteLine($"Retried {attemptCount} times over {duration.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task StreamRecovery_WithPartialData_ShouldNotDuplicateMessages()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        _fixture.ResilientStreamingEnabled = true;
        await _fixture.InitializeAsync();

        var processedMessages = new HashSet<string>();
        var duplicateDetected = false;

        var config = new ResilientStreamingConfiguration
        {
            Enabled = true,
            PartialMessageBufferSize = 10,
            MaxRetryAttempts = 2
        };

        var logger = new Mock<ILogger<ResilientStreamManager>>();
        var bridgeFactory = new Mock<ITestStreamingBridgeFactory>();
        
        var failureSimulated = false;
        bridgeFactory
            .Setup(x => x.CreateBridge())
            .Returns(() =>
            {
                var mockBridge = new Mock<ITestStreamingBridge>();
                mockBridge
                    .Setup(b => b.ConvertToSseAsync(It.IsAny<IAsyncEnumerable<ChatStreamItem>>(), It.IsAny<CancellationToken>()))
                    .Returns(async (IAsyncEnumerable<ChatStreamItem> items, CancellationToken ct) =>
                    {
                        var count = 0;
                        await foreach (var item in items.WithCancellation(ct))
                        {
                            count++;
                            var messageId = $"{item.Content}-{count}";
                            
                            if (!processedMessages.Add(messageId))
                            {
                                duplicateDetected = true;
                            }
                            
                            if (count == 5 && !failureSimulated)
                            {
                                failureSimulated = true;
                                throw new IOException("Simulated failure");
                            }
                            
                            // Cannot use yield in lambda, need to return as enumerable
                        }
                    });
                
                return mockBridge.Object;
            });

        var manager = new TestResilientStreamManager(
            logger.Object,
            bridgeFactory.Object,
            Options.Create(config));

        // Act
        await manager.ProcessStreamWithRecoveryAsync(
            "stream-no-dup",
            "test-user",
            async (bridge, ct) =>
            {
                var testItems = GenerateTestItems(10);
                var testBridge = bridge as ITestStreamingBridge;
                if (testBridge == null)
                    throw new InvalidOperationException("Bridge must be ITestStreamingBridge");
                    
                var sseStream = testBridge.ConvertToSseAsync(testItems, ct);
                
                await foreach (var item in sseStream)
                {
                    // Process items
                }
                
                return "Completed";
            },
            CancellationToken.None);

        // Assert
        duplicateDetected.Should().BeFalse("Should not duplicate messages during recovery");
        processedMessages.Count.Should().Be(10, "Should process all unique messages");

        _output.WriteLine($"Processed {processedMessages.Count} unique messages without duplication");
    }

    private async IAsyncEnumerable<ChatStreamItem> GenerateTestItems(int count)
    {
        for (int i = 1; i <= count; i++)
        {
            yield return new ChatStreamItem
            {
                Type = StreamItemType.Content,
                Content = $"Message {i}",
                Timestamp = DateTime.UtcNow
            };
            await Task.Yield();
        }
    }

    /// <summary>
    /// Helper method to convert items to SSE format with simulated failure.
    /// </summary>
    private static async IAsyncEnumerable<string> ConvertToSseWithFailure(
        IAsyncEnumerable<ChatStreamItem> items,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        List<string> messagesProcessed,
        int failureAtCount = 3)
    {
        var count = 0;
        var failureOccurred = false;
        
        await foreach (var item in items.WithCancellation(cancellationToken))
        {
            count++;
            if (count == failureAtCount && !failureOccurred)
            {
                failureOccurred = true;
                throw new IOException("Simulated stream failure");
            }
            
            messagesProcessed.Add(item.Content ?? "");
            yield return $"data: {item.Content}\n\n";
        }
    }
    
    /// <summary>
    /// Helper method to convert items to SSE format for duplicate detection.
    /// </summary>
    private static async IAsyncEnumerable<string> ConvertToSseWithDuplicateCheck(
        IAsyncEnumerable<ChatStreamItem> items,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        HashSet<string> processedIds,
        bool simulateFailure = false,
        int failureAtCount = 5)
    {
        var count = 0;
        var failureOccurred = false;
        
        await foreach (var item in items.WithCancellation(cancellationToken))
        {
            count++;
            
            // Check for duplicates
            var messageId = item.Content ?? "";
            if (!processedIds.Add(messageId))
            {
                throw new InvalidOperationException($"Duplicate message detected: {messageId}");
            }
            
            // Simulate failure if requested
            if (simulateFailure && count == failureAtCount && !failureOccurred)
            {
                failureOccurred = true;
                throw new IOException("Simulated failure");
            }
            
            yield return $"data: {item.Content}\n\n";
        }
    }
}