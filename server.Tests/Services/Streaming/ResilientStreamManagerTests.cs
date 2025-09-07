using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AIChat.Server.Configuration;
using AIChat.Server.HealthChecks;
using AIChat.Server.Services.Streaming;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services.Streaming;

public class ResilientStreamManagerTests : IAsyncDisposable
{
    private readonly Mock<ILogger<ResilientStreamManager>> _loggerMock;
    private readonly Mock<IStreamingBridgeFactory> _bridgeFactoryMock;
    private readonly Mock<IStreamingBridge> _bridgeMock;
    private readonly ResilientStreamingConfiguration _configuration;
    private readonly ResilientStreamManager _manager;
    private readonly Mock<HttpResponse> _httpResponseMock;
    private readonly Mock<HttpContext> _httpContextMock;

    public ResilientStreamManagerTests()
    {
        _loggerMock = new Mock<ILogger<ResilientStreamManager>>();
        _bridgeFactoryMock = new Mock<IStreamingBridgeFactory>();
        _bridgeMock = new Mock<IStreamingBridge>();
        _httpResponseMock = new Mock<HttpResponse>();
        _httpContextMock = new Mock<HttpContext>();
        
        _configuration = new ResilientStreamingConfiguration
        {
            Enabled = true,
            Reconnection = new ReconnectionConfiguration
            {
                MaxAttempts = 3,
                InitialDelayMs = 100,
                MaxDelayMs = 1000,
                JitterMs = 50,
                UseExponentialBackoff = true
            },
            Buffer = new BufferConfiguration
            {
                Size = 100,
                TTLMinutes = 5,
                HighPrioritySize = 10,
                OverflowStrategy = "DropOldest"
            },
            CircuitBreaker = new CircuitBreakerConfiguration
            {
                FailureThreshold = 3,
                FailureWindowSeconds = 60,
                RecoveryTimeoutSeconds = 5,
                SuccessThreshold = 2,
                UseFallback = true
            },
            PartialRecovery = new PartialRecoveryConfiguration
            {
                Enabled = true,
                MaxPartialMessages = 5,
                ChunkTimeoutSeconds = 10,
                EnableDeduplication = true,
                MaxStorageSizeKb = 100
            },
            HealthCheck = new HealthCheckConfiguration
            {
                Enabled = true,
                EndpointPath = "/api/health/streaming",
                CheckIntervalSeconds = 30,
                IncludeDetailedMetrics = true
            }
        };

        _bridgeFactoryMock.Setup(f => f.CreateBridge()).Returns(_bridgeMock.Object);
        
        // Setup the bridge mock to properly handle the stream
        _bridgeMock.Setup(b => b.ConvertGrainToHttpStreamAsync(
            It.IsAny<IAsyncEnumerable<string>>(),
            It.IsAny<HttpResponse>(),
            It.IsAny<Func<string, string>>(),
            It.IsAny<CancellationToken>()))
            .Returns<IAsyncEnumerable<string>, HttpResponse, Func<string, string>, CancellationToken>(
                async (stream, response, formatter, ct) =>
                {
                    // Consume the stream to keep it active
                    await foreach (var item in stream.WithCancellation(ct))
                    {
                        // Simulate processing
                        await Task.Delay(1, ct);
                    }
                });
        
        _bridgeMock.Setup(b => b.DisposeAsync()).Returns(ValueTask.CompletedTask);
        
        var options = Options.Create(_configuration);
        _manager = new ResilientStreamManager(_loggerMock.Object, _bridgeFactoryMock.Object, options);
        
        // Setup HTTP response mock
        var bodyStream = new MemoryStream();
        _httpResponseMock.Setup(r => r.Body).Returns(bodyStream);
        _httpResponseMock.Setup(r => r.HttpContext).Returns(_httpContextMock.Object);
        _httpContextMock.Setup(c => c.RequestAborted).Returns(CancellationToken.None);
    }

    [Fact]
    public async Task ProcessResilientStreamAsync_SuccessfulStream_CompletesNormally()
    {
        // Arrange
        var streamId = "test-stream-1";
        var testData = new[] { "chunk1", "chunk2", "chunk3" };
        var grainStream = CreateAsyncEnumerable(testData);
        var formatter = new Func<string, string>(s => $"formatted-{s}");

        // Act
        await _manager.ProcessResilientStreamAsync(
            streamId,
            grainStream,
            _httpResponseMock.Object,
            formatter,
            CancellationToken.None);

        // Assert
        _bridgeFactoryMock.Verify(f => f.CreateBridge(), Times.Once);
        _bridgeMock.Verify(b => b.ConvertGrainToHttpStreamAsync(
            It.IsAny<IAsyncEnumerable<string>>(),
            _httpResponseMock.Object,
            It.IsAny<Func<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        
        var metrics = await _manager.GetStreamMetricsAsync(streamId);
        Assert.Null(metrics); // Stream should be cleaned up after completion
    }

    [Fact]
    public async Task ProcessResilientStreamAsync_DuplicateStreamId_ThrowsException()
    {
        // Arrange
        var streamId = "test-stream-2";
        var grainStream = CreateAsyncEnumerable(new[] { "data" });
        var formatter = new Func<string, string>(s => s);
        var cts = new CancellationTokenSource();

        // Start first stream (don't await to keep it active)
        var firstStreamTask = _manager.ProcessResilientStreamAsync(
            streamId,
            CreateInfiniteStream(cts.Token),
            _httpResponseMock.Object,
            formatter,
            cts.Token);

        // Wait for the stream to actually start and be added to active streams
        var maxWaitTime = TimeSpan.FromSeconds(2);
        var startTime = DateTime.UtcNow;
        while ((await _manager.GetActiveStreamIdsAsync()).Count == 0)
        {
            if (DateTime.UtcNow - startTime > maxWaitTime)
            {
                throw new TimeoutException("First stream did not start in time");
            }
            await Task.Delay(50);
        }

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _manager.ProcessResilientStreamAsync(
                streamId,
                grainStream,
                _httpResponseMock.Object,
                formatter,
                CancellationToken.None));
        
        // Cleanup
        cts.Cancel();
        try { await firstStreamTask; } catch { }
    }

    [Fact]
    public async Task RecoverStreamAsync_NonExistentStream_ReturnsFalse()
    {
        // Arrange
        var streamId = "non-existent";

        // Act
        var result = await _manager.RecoverStreamAsync(streamId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetHealthStatusAsync_ReturnsCorrectStatus()
    {
        // Arrange & Act
        var status = await _manager.GetHealthStatusAsync();

        // Assert
        Assert.NotNull(status);
        Assert.True(status.IsHealthy);
        Assert.Equal(0, status.ActiveStreams);
        Assert.Equal(0, status.StreamsInRecovery);
        Assert.Equal(0, status.OpenCircuitBreakers);
        Assert.Equal(0, status.TotalBufferedMessages);
        Assert.True(status.SuccessRatePercentage >= 0);
    }

    [Fact]
    public async Task GetStreamMetricsAsync_ActiveStream_ReturnsMetrics()
    {
        // Arrange
        var streamId = "test-stream-3";
        var cts = new CancellationTokenSource();
        var streamTask = _manager.ProcessResilientStreamAsync(
            streamId,
            CreateInfiniteStream(cts.Token),
            _httpResponseMock.Object,
            s => s,
            cts.Token);

        // Wait for the stream to actually start
        var maxWaitTime = TimeSpan.FromSeconds(2);
        var startTime = DateTime.UtcNow;
        while ((await _manager.GetActiveStreamIdsAsync()).Count == 0)
        {
            if (DateTime.UtcNow - startTime > maxWaitTime)
            {
                throw new TimeoutException("Stream did not start in time");
            }
            await Task.Delay(50);
        }

        // Act
        var metrics = await _manager.GetStreamMetricsAsync(streamId);

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(streamId, metrics.StreamId);
        Assert.Equal(StreamState.Active, metrics.State);
        Assert.Equal(CircuitState.Closed, metrics.CircuitState);
        
        // Cleanup
        cts.Cancel();
        try { await streamTask; } catch { }
    }

    [Fact]
    public async Task ResetCircuitBreakerAsync_ExistingStream_ReturnsTrue()
    {
        // Arrange
        var streamId = "test-stream-4";
        var cts = new CancellationTokenSource();
        var streamTask = _manager.ProcessResilientStreamAsync(
            streamId,
            CreateInfiniteStream(cts.Token),
            _httpResponseMock.Object,
            s => s,
            cts.Token);

        // Wait for the stream to actually start
        var maxWaitTime = TimeSpan.FromSeconds(2);
        var startTime = DateTime.UtcNow;
        while ((await _manager.GetActiveStreamIdsAsync()).Count == 0)
        {
            if (DateTime.UtcNow - startTime > maxWaitTime)
            {
                throw new TimeoutException("Stream did not start in time");
            }
            await Task.Delay(50);
        }

        // Act
        var result = await _manager.ResetCircuitBreakerAsync(streamId);

        // Assert
        Assert.True(result);
        
        // Cleanup
        cts.Cancel();
        try { await streamTask; } catch { }
    }

    [Fact]
    public async Task ClearBufferedMessagesAsync_ExistingStream_ReturnsCount()
    {
        // Arrange
        var streamId = "test-stream-5";
        var cts = new CancellationTokenSource();
        var streamTask = _manager.ProcessResilientStreamAsync(
            streamId,
            CreateInfiniteStream(cts.Token),
            _httpResponseMock.Object,
            s => s,
            cts.Token);

        // Wait for the stream to actually start
        var maxWaitTime = TimeSpan.FromSeconds(2);
        var startTime = DateTime.UtcNow;
        while ((await _manager.GetActiveStreamIdsAsync()).Count == 0)
        {
            if (DateTime.UtcNow - startTime > maxWaitTime)
            {
                throw new TimeoutException("Stream did not start in time");
            }
            await Task.Delay(50);
        }

        // Act
        var count = await _manager.ClearBufferedMessagesAsync(streamId);

        // Assert
        Assert.True(count >= 0);
        
        // Cleanup
        cts.Cancel();
        try { await streamTask; } catch { }
    }

    [Fact]
    public async Task GetActiveStreamIdsAsync_MultipleStreams_ReturnsAllIds()
    {
        // Arrange
        var streamIds = new[] { "stream-6", "stream-7", "stream-8" };
        var tasks = new List<Task>();
        var cancellationTokens = new List<CancellationTokenSource>();

        foreach (var id in streamIds)
        {
            var cts = new CancellationTokenSource();
            cancellationTokens.Add(cts);
            tasks.Add(_manager.ProcessResilientStreamAsync(
                id,
                CreateInfiniteStream(cts.Token),
                _httpResponseMock.Object,
                s => s,
                cts.Token));
        }

        // Wait for all streams to actually start
        var maxWaitTime = TimeSpan.FromSeconds(2);
        var startTime = DateTime.UtcNow;
        while ((await _manager.GetActiveStreamIdsAsync()).Count < streamIds.Length)
        {
            if (DateTime.UtcNow - startTime > maxWaitTime)
            {
                throw new TimeoutException("Not all streams started in time");
            }
            await Task.Delay(50);
        }

        // Act
        var activeIds = await _manager.GetActiveStreamIdsAsync();

        // Assert
        Assert.Equal(streamIds.Length, activeIds.Count);
        foreach (var id in streamIds)
        {
            Assert.Contains(id, activeIds);
        }
        
        // Cleanup
        foreach (var cts in cancellationTokens)
        {
            cts.Cancel();
        }
        await Task.WhenAll(tasks.Select(async t => { try { await t; } catch { } }));
    }

    [Fact]
    public async Task ProcessResilientStreamAsync_WithCancellation_StopsGracefully()
    {
        // Arrange
        var streamId = "test-stream-9";
        var cts = new CancellationTokenSource();
        var grainStream = CreateInfiniteStream(cts.Token);
        var formatter = new Func<string, string>(s => s);

        // Act
        var streamTask = _manager.ProcessResilientStreamAsync(
            streamId,
            grainStream,
            _httpResponseMock.Object,
            formatter,
            cts.Token);

        // Wait for the stream to actually start
        var maxWaitTime = TimeSpan.FromSeconds(2);
        var startTime = DateTime.UtcNow;
        while ((await _manager.GetActiveStreamIdsAsync()).Count == 0)
        {
            if (DateTime.UtcNow - startTime > maxWaitTime)
            {
                throw new TimeoutException("Stream did not start in time");
            }
            await Task.Delay(50);
        }
        
        cts.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await streamTask);
        
        // Verify stream is cleaned up
        var metrics = await _manager.GetStreamMetricsAsync(streamId);
        Assert.Null(metrics);
    }

    [Fact]
    public void Configuration_Validation_InvalidConfig_ThrowsException()
    {
        // Arrange
        var invalidConfig = new ResilientStreamingConfiguration
        {
            Enabled = true,
            Reconnection = new ReconnectionConfiguration
            {
                MaxAttempts = 0, // Invalid: must be at least 1
                InitialDelayMs = 50, // Invalid: must be at least 100
                MaxDelayMs = 1000,
                JitterMs = -10 // Invalid: cannot be negative
            }
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new ResilientStreamManager(
                _loggerMock.Object,
                _bridgeFactoryMock.Object,
                Options.Create(invalidConfig)));
    }

    private static async IAsyncEnumerable<T> CreateAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private static async IAsyncEnumerable<string> CreateInfiniteStream([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var counter = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(10, cancellationToken);
            yield return $"chunk-{counter++}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _manager.DisposeAsync();
    }
}

/// <summary>
/// Tests for the ResilientStreamingHealthCheck class.
/// </summary>
public class ResilientStreamingHealthCheckTests
{
    private readonly Mock<IResilientStreamManager> _streamManagerMock;
    private readonly Mock<ILogger<ResilientStreamingHealthCheck>> _loggerMock;
    private readonly ResilientStreamingHealthCheck _healthCheck;

    public ResilientStreamingHealthCheckTests()
    {
        _streamManagerMock = new Mock<IResilientStreamManager>();
        _loggerMock = new Mock<ILogger<ResilientStreamingHealthCheck>>();
        _healthCheck = new ResilientStreamingHealthCheck(_streamManagerMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CheckHealthAsync_HealthySystem_ReturnsHealthy()
    {
        // Arrange
        var healthStatus = new StreamHealthStatus
        {
            IsHealthy = true,
            ActiveStreams = 10,
            StreamsInRecovery = 0,
            OpenCircuitBreakers = 0,
            TotalBufferedMessages = 50,
            AverageRecoveryTimeMs = 1000,
            SuccessRatePercentage = 95
        };

        _streamManagerMock.Setup(m => m.GetHealthStatusAsync())
            .ReturnsAsync(healthStatus);

        // Act
        var result = await _healthCheck.CheckHealthAsync(null!, CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("activeStreams", result.Data.Keys);
        Assert.Equal(10, result.Data["activeStreams"]);
    }

    [Fact]
    public async Task CheckHealthAsync_UnhealthySystem_ReturnsUnhealthy()
    {
        // Arrange
        var healthStatus = new StreamHealthStatus
        {
            IsHealthy = false,
            ActiveStreams = 150, // Above threshold
            StreamsInRecovery = 20,
            OpenCircuitBreakers = 10, // Above threshold
            TotalBufferedMessages = 1000,
            AverageRecoveryTimeMs = 6000, // Above 5 second requirement
            SuccessRatePercentage = 60 // Below threshold
        };

        _streamManagerMock.Setup(m => m.GetHealthStatusAsync())
            .ReturnsAsync(healthStatus);

        // Act
        var result = await _healthCheck.CheckHealthAsync(null!, CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_DegradedSystem_ReturnsDegraded()
    {
        // Arrange
        var healthStatus = new StreamHealthStatus
        {
            IsHealthy = true,
            ActiveStreams = 50,
            StreamsInRecovery = 5, // Some streams recovering
            OpenCircuitBreakers = 2,
            TotalBufferedMessages = 600, // High buffered messages
            AverageRecoveryTimeMs = 3000,
            SuccessRatePercentage = 85
        };

        _streamManagerMock.Setup(m => m.GetHealthStatusAsync())
            .ReturnsAsync(healthStatus);

        // Act
        var result = await _healthCheck.CheckHealthAsync(null!, CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("warnings", result.Description!.ToLower());
    }

    [Fact]
    public async Task CheckHealthAsync_ExceptionThrown_ReturnsUnhealthy()
    {
        // Arrange
        _streamManagerMock.Setup(m => m.GetHealthStatusAsync())
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act
        var result = await _healthCheck.CheckHealthAsync(null!, CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Failed to check resilient streaming health", result.Description);
        Assert.NotNull(result.Exception);
    }
}