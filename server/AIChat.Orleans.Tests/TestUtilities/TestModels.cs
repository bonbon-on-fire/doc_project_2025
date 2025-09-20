using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.Orleans.Tests.TestUtilities;

/// <summary>
/// Test model for streaming items.
/// </summary>
public class ChatStreamItem
{
    public StreamItemType Type { get; set; }
    public string? Content { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Type of stream item.
/// </summary>
public enum StreamItemType
{
    Content,
    Delta,
    ToolCall,
    ToolResult,
    Complete,
    Error,
}

/// <summary>
/// Extension of IStreamingBridge for testing.
/// </summary>
public interface ITestStreamingBridge : Mocks.IStreamingBridge
{
    /// <summary>
    /// Converts items to SSE format for testing.
    /// </summary>
    IAsyncEnumerable<string> ConvertToSseAsync(
        IAsyncEnumerable<ChatStreamItem> items,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Test implementation of IStreamingBridgeFactory.
/// </summary>
public interface ITestStreamingBridgeFactory : Mocks.IStreamingBridgeFactory
{
    new ITestStreamingBridge CreateBridge();
}

/// <summary>
/// Test implementation of ResilientStreamManager with proper retry logic for testing.
/// </summary>
public class TestResilientStreamManager : Mocks.IResilientStreamManager
{
    private readonly ILogger<Mocks.ResilientStreamManager> _logger;
    private readonly ITestStreamingBridgeFactory _bridgeFactory;
    private readonly Mocks.ResilientStreamingConfiguration _configuration;

    public TestResilientStreamManager(
        ILogger<Mocks.ResilientStreamManager> logger,
        ITestStreamingBridgeFactory bridgeFactory,
        IOptions<Mocks.ResilientStreamingConfiguration> configuration
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bridgeFactory = bridgeFactory ?? throw new ArgumentNullException(nameof(bridgeFactory));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task<T> ProcessStreamWithRecoveryAsync<T>(
        string streamId,
        string userId,
        Func<Mocks.IStreamingBridge, CancellationToken, Task<T>> streamProcessor,
        CancellationToken cancellationToken = default
    )
    {
        var maxRetries = _configuration.MaxRetryAttempts;
        var delayMs = _configuration.RetryDelayMs;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogDebug("Stream {StreamId} attempt {Attempt} of {MaxAttempts}", streamId, attempt, maxRetries);

                var bridge = _bridgeFactory.CreateBridge();
                var result = await streamProcessor(bridge, cancellationToken);

                _logger.LogDebug("Stream {StreamId} succeeded on attempt {Attempt}", streamId, attempt);
                return result;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Stream {StreamId} failed on attempt {Attempt}, retrying in {DelayMs}ms",
                    streamId, attempt, delayMs);

                await Task.Delay(delayMs, cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogError(ex, "Stream {StreamId} failed on final attempt {Attempt}", streamId, attempt);
            }
        }

        throw lastException ?? new InvalidOperationException($"Stream {streamId} failed after {maxRetries} attempts");
    }

    public Task<Mocks.StreamMetrics> GetMetricsAsync()
    {
        return Task.FromResult(new Mocks.StreamMetrics
        {
            TotalStreamsProcessed = 1,
            TotalRecoveryAttempts = 0,
            SuccessfulRecoveries = 0,
            TotalMessagesProcessed = 0,
            Uptime = TimeSpan.Zero,
        });
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        GC.SuppressFinalize(this);
    }
}
