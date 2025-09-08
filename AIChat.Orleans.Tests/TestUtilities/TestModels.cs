using AIChat.Server.Configuration;
using AIChat.Server.Services.Streaming;
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
    Error
}

/// <summary>
/// Extension of IStreamingBridge for testing.
/// </summary>
public interface ITestStreamingBridge : IStreamingBridge
{
    /// <summary>
    /// Converts items to SSE format for testing.
    /// </summary>
    IAsyncEnumerable<string> ConvertToSseAsync(
        IAsyncEnumerable<ChatStreamItem> items,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Test implementation of IStreamingBridgeFactory.
/// </summary>
public interface ITestStreamingBridgeFactory : IStreamingBridgeFactory
{
    new ITestStreamingBridge CreateBridge();
}

/// <summary>
/// Test wrapper for ResilientStreamManager to work with test factories.
/// </summary>
public class TestResilientStreamManager : IResilientStreamManager
{
    private readonly ResilientStreamManager _inner;

    public TestResilientStreamManager(
        ILogger<ResilientStreamManager> logger,
        ITestStreamingBridgeFactory bridgeFactory,
        IOptions<ResilientStreamingConfiguration> configuration)
    {
        // Create a wrapper that implements IStreamingBridgeFactory
        var wrappedFactory = new StreamingBridgeFactoryWrapper(bridgeFactory);
        _inner = new ResilientStreamManager(logger, wrappedFactory, configuration);
    }

    public Task<T> ProcessStreamWithRecoveryAsync<T>(
        string streamId,
        string userId,
        Func<IStreamingBridge, CancellationToken, Task<T>> streamProcessor,
        CancellationToken cancellationToken = default)
    {
        return _inner.ProcessStreamWithRecoveryAsync(streamId, userId, streamProcessor, cancellationToken);
    }

    public Task<StreamMetrics> GetMetricsAsync()
    {
        return _inner.GetMetricsAsync();
    }

    public ValueTask DisposeAsync()
    {
        return _inner.DisposeAsync();
    }

    private class StreamingBridgeFactoryWrapper : IStreamingBridgeFactory
    {
        private readonly ITestStreamingBridgeFactory _testFactory;

        public StreamingBridgeFactoryWrapper(ITestStreamingBridgeFactory testFactory)
        {
            _testFactory = testFactory;
        }

        public IStreamingBridge CreateBridge()
        {
            return _testFactory.CreateBridge();
        }
    }
}
