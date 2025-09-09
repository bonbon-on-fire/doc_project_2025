using System.Text;
using AIChat.Server.Configuration;
using AIChat.Server.Services.Streaming;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services.Streaming;

public class StreamingBridgeTests : IDisposable
{
    private readonly Mock<ILogger<StreamingBridge>> _loggerMock;
    private readonly StreamingConfiguration _configuration;
    private readonly IOptions<StreamingConfiguration> _configurationOptions;
    private readonly StreamingBridge _streamingBridge;
    private static readonly string[] stringArray = ["test"];

    public StreamingBridgeTests()
    {
        _loggerMock = new Mock<ILogger<StreamingBridge>>();
        _configuration = new StreamingConfiguration
        {
            BufferSize = 10,
            BackpressureThreshold = 80.0f,
            WriteTimeoutMs = 5000,
            BackpressureDelayMs = 50,
            EnableAdaptiveBackpressure = true,
            EnableTelemetry = true,
            MaxChunkSize = 1024,
            FlushIntervalMs = 100
        };
        _configurationOptions = Options.Create(_configuration);
        _streamingBridge = new StreamingBridge(_loggerMock.Object, _configurationOptions);
    }

    public void Dispose()
    {
        _streamingBridge?.DisposeAsync().AsTask().Wait();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ConvertGrainToHttpStreamAsyncSuccessfullyStreamsData()
    {
        // Arrange
        var testData = new[] { "chunk1", "chunk2", "chunk3" };
        var grainStream = GenerateAsyncEnumerable(testData);
        var httpContext = new DefaultHttpContext();
        var memoryStream = new MemoryStream();
        httpContext.Response.Body = memoryStream;

        static string formatter(string s)
        {
            return $"formatted_{s}";
        }

        // Act
        await _streamingBridge.ConvertGrainToHttpStreamAsync(
            grainStream,
            httpContext.Response,
            formatter,
            CancellationToken.None);

        // Assert
        memoryStream.Position = 0;
        var result = Encoding.UTF8.GetString(memoryStream.ToArray());

        Assert.Contains("formatted_chunk1", result);
        Assert.Contains("formatted_chunk2", result);
        Assert.Contains("formatted_chunk3", result);
        Assert.Contains("data:", result); // SSE format
    }

    [Fact]
    public async Task ConvertGrainToHttpStreamAsyncHandlesCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var grainStream = GenerateInfiniteAsyncEnumerable(cts.Token);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        // Act & Assert
        cts.CancelAfter(100);

        // Both OperationCanceledException and TaskCanceledException are valid
        // since TaskCanceledException derives from OperationCanceledException
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await _streamingBridge.ConvertGrainToHttpStreamAsync(
                grainStream,
                httpContext.Response,
                s => s,
                cts.Token));

        // Verify it's a cancellation-related exception
        Assert.True(exception is not null);
    }

    [Fact]
    public async Task HandleBackpressureAsyncTriggersWhenThresholdExceeded()
    {
        // Arrange
        var highUtilization = 85.0f; // Above 80% threshold

        // Act
        var result = await _streamingBridge.HandleBackpressureAsync(
            highUtilization,
            CancellationToken.None);

        // Assert
        Assert.True(result);

        // Verify backpressure event was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Backpressure triggered")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleBackpressureAsyncDoesNotTriggerBelowThreshold()
    {
        // Arrange
        var lowUtilization = 50.0f; // Below 80% threshold

        // Act
        var result = await _streamingBridge.HandleBackpressureAsync(
            lowUtilization,
            CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task PropagateErrorAsyncWritesErrorToHttpResponse()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");
        var httpContext = new DefaultHttpContext();
        var memoryStream = new MemoryStream();
        httpContext.Response.Body = memoryStream;
        httpContext.TraceIdentifier = "test-trace-id";

        // Act
        await _streamingBridge.PropagateErrorAsync(
            exception,
            httpContext.Response,
            CancellationToken.None);

        // Assert
        memoryStream.Position = 0;
        var result = Encoding.UTF8.GetString(memoryStream.ToArray());

        Assert.Contains("Test error", result);
        Assert.Contains("error", result);
        Assert.Contains("data:", result); // SSE format
    }

    [Fact]
    public void GetBufferStatisticsReturnsAccurateStats()
    {
        // Act
        var stats = _streamingBridge.GetBufferStatistics();

        // Assert
        Assert.Equal(10, stats.Capacity);
        Assert.Equal(0, stats.CurrentSize);
        Assert.Equal(0, stats.UtilizationPercentage);
        Assert.Equal(0, stats.ItemsProcessed);
        Assert.Equal(0, stats.BackpressureEvents);
    }

    [Fact]
    public async Task ConvertGrainToHttpStreamAsyncHandlesErrors()
    {
        // Arrange
        var grainStream = GenerateErrorAsyncEnumerable();
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        // Act & Assert
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _streamingBridge.ConvertGrainToHttpStreamAsync(
                grainStream,
                httpContext.Response,
                s => s,
                CancellationToken.None));
    }

    [Fact]
    public void ConstructorValidatesConfiguration()
    {
        // Arrange
        var invalidConfig = new StreamingConfiguration
        {
            BufferSize = 5, // Below minimum
            BackpressureThreshold = 110 // Above maximum
        };

        // Act & Assert
        _ = Assert.Throws<InvalidOperationException>(() =>
            new StreamingBridge(_loggerMock.Object, Options.Create(invalidConfig)));
    }

    [Fact]
    public async Task ConvertGrainToHttpStreamAsyncThrowsOnNullArguments()
    {
        // Arrange
        var grainStream = GenerateAsyncEnumerable(stringArray);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        // Act & Assert
        _ = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _streamingBridge.ConvertGrainToHttpStreamAsync<string>(
                null!,
                httpContext.Response,
                s => s,
                CancellationToken.None));

        _ = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _streamingBridge.ConvertGrainToHttpStreamAsync(
                grainStream,
                null!,
                s => s,
                CancellationToken.None));

        _ = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _streamingBridge.ConvertGrainToHttpStreamAsync(
                grainStream,
                httpContext.Response,
                null!,
                CancellationToken.None));
    }

    // Helper methods
    private static async IAsyncEnumerable<string> GenerateAsyncEnumerable(string[] items)
    {
        foreach (var item in items)
        {
            await Task.Delay(10);
            yield return item;
        }
    }

    private static async IAsyncEnumerable<string> GenerateInfiniteAsyncEnumerable(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var counter = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(10, cancellationToken);
            yield return $"item_{counter++}";
        }
    }

    private static async IAsyncEnumerable<string> GenerateErrorAsyncEnumerable()
    {
        yield return "item1";
        await Task.Delay(10);
        throw new InvalidOperationException("Simulated error");
    }
}
