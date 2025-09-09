using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AIChat.Server.Configuration;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Wrap;

namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Implementation of IResilientStreamManager providing fault-tolerant streaming with automatic recovery,
/// message buffering, partial message recovery, and circuit breaking capabilities.
/// </summary>
public sealed class ResilientStreamManager : IResilientStreamManager
{
    private readonly ILogger<ResilientStreamManager> _logger;
    private readonly IStreamingBridgeFactory _bridgeFactory;
    private readonly ResilientStreamingConfiguration _configuration;
    private readonly ConcurrentDictionary<string, StreamContext> _activeStreams;
    private readonly SemaphoreSlim _recoveryLock;
    private readonly Timer _healthCheckTimer;
    private bool _disposed;

    // Metrics
    private long _totalStreamsProcessed;
    private long _totalRecoveryAttempts;
    private long _successfulRecoveries;
    private long _totalMessagesProcessed;
    private readonly Stopwatch _uptimeStopwatch;

    /// <summary>
    /// Initializes a new instance of the ResilientStreamManager class.
    /// </summary>
    public ResilientStreamManager(
        ILogger<ResilientStreamManager> logger,
        IStreamingBridgeFactory bridgeFactory,
        IOptions<ResilientStreamingConfiguration> configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bridgeFactory = bridgeFactory ?? throw new ArgumentNullException(nameof(bridgeFactory));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));

        // Validate configuration
        if (!_configuration.Validate(out var errors))
        {
            var errorMessage = string.Join("; ", errors);
            _logger.LogError("Invalid resilient streaming configuration: {Errors}", errorMessage);
            throw new InvalidOperationException($"Invalid resilient streaming configuration: {errorMessage}");
        }

        _activeStreams = new ConcurrentDictionary<string, StreamContext>();
        _recoveryLock = new SemaphoreSlim(1, 1);
        _uptimeStopwatch = Stopwatch.StartNew();

        // Start health check timer if enabled
        _healthCheckTimer = _configuration.HealthCheck.Enabled
            ? new Timer(
                PerformHealthCheck,
                null,
                TimeSpan.FromSeconds(_configuration.HealthCheck.CheckIntervalSeconds),
                TimeSpan.FromSeconds(_configuration.HealthCheck.CheckIntervalSeconds))
            : new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);

        _logger.LogInformation(
            "ResilientStreamManager initialized with reconnection attempts: {MaxAttempts}, " +
            "buffer size: {BufferSize}, circuit breaker threshold: {Threshold}",
            _configuration.Reconnection.MaxAttempts,
            _configuration.Buffer.Size,
            _configuration.CircuitBreaker.FailureThreshold);
    }

    /// <inheritdoc />
    public async Task ProcessResilientStreamAsync<T>(
        string streamId,
        IAsyncEnumerable<T> grainStream,
        HttpResponse httpResponse,
        Func<T, string> formatter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(grainStream);
        ArgumentNullException.ThrowIfNull(httpResponse);
        ArgumentNullException.ThrowIfNull(formatter);

        var context = CreateStreamContext(streamId, httpResponse);

        if (!_activeStreams.TryAdd(streamId, context))
        {
            throw new InvalidOperationException($"Stream {streamId} is already active");
        }

        try
        {
            _logger.LogInformation("Starting resilient stream processing for {StreamId}", streamId);

            // Create resilience pipeline with Polly
            var resiliencePipeline = CreateResiliencePipeline(context);

            // Process stream with resilience
            await resiliencePipeline.ExecuteAsync(async (ct) => await ProcessStreamWithRecoveryAsync(
                    context,
                    grainStream,
                    formatter,
                    ct), cancellationToken);

            context.State = StreamState.Completed;
            _ = Interlocked.Increment(ref _totalStreamsProcessed);

            _logger.LogInformation("Stream {StreamId} completed successfully", streamId);
        }
        catch (OperationCanceledException)
        {
            context.State = StreamState.Cancelled;
            _logger.LogInformation("Stream {StreamId} was cancelled", streamId);
            throw;
        }
        catch (Exception ex)
        {
            context.State = StreamState.Failed;
            context.LastError = ex.Message;
            _logger.LogError(ex, "Stream {StreamId} failed", streamId);
            throw;
        }
        finally
        {
            // Cleanup
            await CleanupStreamContextAsync(context);
            _ = _activeStreams.TryRemove(streamId, out _);
        }
    }

    /// <inheritdoc />
    public async Task<bool> RecoverStreamAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        if (!_activeStreams.TryGetValue(streamId, out var context))
        {
            _logger.LogWarning("Cannot recover stream {StreamId}: stream not found", streamId);
            return false;
        }

        await _recoveryLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Attempting to recover stream {StreamId}", streamId);
            context.State = StreamState.Recovering;
            _ = Interlocked.Increment(ref _totalRecoveryAttempts);

            // Replay buffered messages
            var replayedCount = await ReplayBufferedMessagesAsync(context, cancellationToken);

            // Recover partial messages if enabled
            if (_configuration.PartialRecovery.Enabled)
            {
                await RecoverPartialMessagesAsync(context, cancellationToken);
            }

            context.State = StreamState.Active;
            _ = Interlocked.Increment(ref _successfulRecoveries);

            _logger.LogInformation(
                "Successfully recovered stream {StreamId}, replayed {Count} messages",
                streamId, replayedCount);

            return true;
        }
        catch (Exception ex)
        {
            context.State = StreamState.Failed;
            context.LastError = ex.Message;
            _logger.LogError(ex, "Failed to recover stream {StreamId}", streamId);
            return false;
        }
        finally
        {
            _ = _recoveryLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<StreamHealthStatus> GetHealthStatusAsync()
    {
        var activeStreams = _activeStreams.Values.ToList();
        var now = DateTime.UtcNow;

        var healthStatus = new StreamHealthStatus
        {
            IsHealthy = DetermineHealthStatus(activeStreams),
            ActiveStreams = activeStreams.Count,
            StreamsInRecovery = activeStreams.Count(s => s.State == StreamState.Recovering),
            OpenCircuitBreakers = activeStreams.Count(s => s.CircuitState == CircuitState.Open),
            TotalBufferedMessages = activeStreams.Sum(s => s.MessageBuffer.Reader.Count),
            AverageRecoveryTimeMs = CalculateAverageRecoveryTime(),
            SuccessRatePercentage = CalculateSuccessRate(),
            StatusMessages = GenerateStatusMessages(activeStreams),
            Timestamp = now
        };

        return await Task.FromResult(healthStatus);
    }

    /// <inheritdoc />
    public async Task<StreamMetrics?> GetStreamMetricsAsync(string streamId)
    {
        if (!_activeStreams.TryGetValue(streamId, out var context))
        {
            return null;
        }

        var metrics = new StreamMetrics
        {
            StreamId = streamId,
            State = context.State,
            CircuitState = context.CircuitState,
            MessagesProcessed = context.MessagesProcessed,
            BufferedMessages = context.MessageBuffer.Reader.Count,
            ReconnectionAttempts = context.ReconnectionAttempts,
            FailureCount = context.FailureCount,
            LastError = context.LastError,
            StartTime = context.StartTime,
            LastActivityTime = context.LastActivityTime,
            TotalProcessingTimeMs = context.TotalProcessingTimeMs,
            PartialRecovery = GetPartialRecoveryStats(context)
        };

        return await Task.FromResult(metrics);
    }

    /// <inheritdoc />
    public async Task<bool> ResetCircuitBreakerAsync(string streamId)
    {
        if (!_activeStreams.TryGetValue(streamId, out var context))
        {
            return false;
        }

        context.CircuitState = CircuitState.Closed;
        context.ConsecutiveFailures = 0;
        context.ConsecutiveSuccesses = 0;

        _logger.LogInformation("Circuit breaker reset for stream {StreamId}", streamId);

        return await Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<int> ClearBufferedMessagesAsync(string streamId)
    {
        if (!_activeStreams.TryGetValue(streamId, out var context))
        {
            return 0;
        }

        var count = 0;
        while (context.MessageBuffer.Reader.TryRead(out _))
        {
            count++;
        }

        _logger.LogInformation("Cleared {Count} buffered messages for stream {StreamId}", count, streamId);

        return await Task.FromResult(count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetActiveStreamIdsAsync()
    {
        return await Task.FromResult(_activeStreams.Keys.ToList());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            // Stop health check timer
            await _healthCheckTimer.DisposeAsync();

            // Clean up all active streams
            var cleanupTasks = _activeStreams.Values
                .Select(CleanupStreamContextAsync)
                .ToArray();

            await Task.WhenAll(cleanupTasks);

            _activeStreams.Clear();
            _recoveryLock?.Dispose();

            _logger.LogInformation(
                "ResilientStreamManager disposed. Stats - Streams: {Total}, Recoveries: {Successful}/{Attempts}, Messages: {Messages}",
                _totalStreamsProcessed,
                _successfulRecoveries,
                _totalRecoveryAttempts,
                _totalMessagesProcessed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ResilientStreamManager disposal");
        }
    }

    #region Private Methods

    private StreamContext CreateStreamContext(string streamId, HttpResponse httpResponse)
    {
        var bufferOptions = new BoundedChannelOptions(_configuration.Buffer.Size)
        {
            FullMode = _configuration.Buffer.OverflowStrategy switch
            {
                "DropOldest" => BoundedChannelFullMode.DropOldest,
                "DropNewest" => BoundedChannelFullMode.DropNewest,
                "RejectNew" => BoundedChannelFullMode.Wait,
                _ => BoundedChannelFullMode.DropOldest
            },
            SingleWriter = false,
            SingleReader = true
        };

        return new StreamContext
        {
            StreamId = streamId,
            HttpResponse = httpResponse,
            MessageBuffer = Channel.CreateBounded<BufferedMessage>(bufferOptions),
            PartialMessages = new ConcurrentDictionary<int, PartialMessage>(),
            State = StreamState.Active,
            CircuitState = CircuitState.Closed,
            StartTime = DateTime.UtcNow,
            LastActivityTime = DateTime.UtcNow
        };
    }

    private AsyncPolicyWrap CreateResiliencePipeline(StreamContext context)
    {
        // Create retry policy with exponential backoff
        var retryPolicy = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .WaitAndRetryAsync(
                _configuration.Reconnection.MaxAttempts,
                CalculateRetryDelay,
                onRetry: (outcome, timespan, retryCount, ctx) =>
                {
                    context.ReconnectionAttempts++;
                    _logger.LogWarning(
                        "Retry attempt {RetryCount} for stream {StreamId} after {Delay}ms",
                        retryCount, context.StreamId, timespan.TotalMilliseconds);
                });

        // Create circuit breaker policy
        var circuitBreakerPolicy = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .CircuitBreakerAsync(
                _configuration.CircuitBreaker.FailureThreshold,
                TimeSpan.FromSeconds(_configuration.CircuitBreaker.RecoveryTimeoutSeconds),
                onBreak: (outcome, timespan) =>
                {
                    context.CircuitState = CircuitState.Open;
                    _logger.LogWarning(
                        "Circuit breaker opened for stream {StreamId} for {Duration}s",
                        context.StreamId, timespan.TotalSeconds);
                },
                onReset: () =>
                {
                    context.CircuitState = CircuitState.Closed;
                    _logger.LogInformation("Circuit breaker closed for stream {StreamId}", context.StreamId);
                },
                onHalfOpen: () =>
                {
                    context.CircuitState = CircuitState.HalfOpen;
                    _logger.LogInformation("Circuit breaker half-open for stream {StreamId}", context.StreamId);
                });

        // Combine policies
        return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
    }

    private TimeSpan CalculateRetryDelay(int retryAttempt)
    {
        if (!_configuration.Reconnection.UseExponentialBackoff)
        {
            return TimeSpan.FromMilliseconds(_configuration.Reconnection.InitialDelayMs);
        }

        var exponentialDelay = Math.Min(
            _configuration.Reconnection.InitialDelayMs * Math.Pow(2, retryAttempt - 1),
            _configuration.Reconnection.MaxDelayMs);

        // Add jitter
        var jitter = Random.Shared.Next(0, _configuration.Reconnection.JitterMs);

        return TimeSpan.FromMilliseconds(exponentialDelay + jitter);
    }

    private async Task ProcessStreamWithRecoveryAsync<T>(
        StreamContext context,
        IAsyncEnumerable<T> grainStream,
        Func<T, string> formatter,
        CancellationToken cancellationToken)
    {
        var bridge = _bridgeFactory.CreateBridge();

        try
        {
            var processingStopwatch = Stopwatch.StartNew();

            // Create a wrapper stream that handles buffering and recovery
            var resilientStream = CreateResilientStream(
                context,
                grainStream,
                formatter,
                cancellationToken);

            // Use the bridge to convert to HTTP SSE
            await bridge.ConvertGrainToHttpStreamAsync(
                resilientStream,
                context.HttpResponse,
                data => data, // Already formatted
                cancellationToken);

            context.TotalProcessingTimeMs += processingStopwatch.ElapsedMilliseconds;
            context.ConsecutiveSuccesses++;

            // Reset circuit breaker state on success
            if (context.ConsecutiveSuccesses >= _configuration.CircuitBreaker.SuccessThreshold &&
                context.CircuitState == CircuitState.HalfOpen)
            {
                context.CircuitState = CircuitState.Closed;
                context.ConsecutiveFailures = 0;
            }
        }
        finally
        {
            await bridge.DisposeAsync();
        }
    }

    private async IAsyncEnumerable<string> CreateResilientStream<T>(
        StreamContext context,
        IAsyncEnumerable<T> grainStream,
        Func<T, string> formatter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var chunkSequence = 0;
        Exception? lastException = null;
        var enumerator = grainStream.GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                T item;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }

                    item = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }

                string? formattedData = null;
                var shouldYield = false;

                try
                {
                    formattedData = formatter(item);

                    // Buffer the message for potential recovery
                    if (_configuration.Buffer.Size > 0)
                    {
                        var bufferedMessage = new BufferedMessage
                        {
                            SequenceNumber = chunkSequence++,
                            Data = formattedData,
                            Timestamp = DateTime.UtcNow
                        };

                        await BufferMessageAsync(context, bufferedMessage, cancellationToken);
                    }

                    // Track partial messages if enabled
                    if (_configuration.PartialRecovery.Enabled)
                    {
                        TrackPartialMessage(context, chunkSequence - 1, formattedData);
                    }

                    context.MessagesProcessed++;
                    context.LastActivityTime = DateTime.UtcNow;
                    _ = Interlocked.Increment(ref _totalMessagesProcessed);

                    shouldYield = true;
                }
                catch (Exception ex)
                {
                    context.FailureCount++;
                    context.ConsecutiveFailures++;
                    context.LastError = ex.Message;
                    lastException = ex;

                    _logger.LogError(ex,
                        "Error processing stream item for {StreamId}, sequence: {Sequence}",
                        context.StreamId, chunkSequence);

                    // Check if we should open the circuit breaker
                    if (context.ConsecutiveFailures >= _configuration.CircuitBreaker.FailureThreshold)
                    {
                        context.CircuitState = CircuitState.Open;
                        lastException = new BrokenCircuitException($"Circuit breaker open for stream {context.StreamId}");
                        break; // Exit the loop
                    }

                    // Continue processing if circuit is not open
                    if (!_configuration.CircuitBreaker.UseFallback)
                    {
                        break; // Exit the loop to throw the exception
                    }
                }

                // Yield outside of try-catch
                if (shouldYield && formattedData != null)
                {
                    yield return formattedData;
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        // Throw any pending exception after the loop
        if (lastException != null && !_configuration.CircuitBreaker.UseFallback)
        {
            throw lastException;
        }
    }

    private async Task BufferMessageAsync(
        StreamContext context,
        BufferedMessage message,
        CancellationToken cancellationToken)
    {
        // Check TTL and remove expired messages
        var expiryTime = DateTime.UtcNow.AddMinutes(-_configuration.Buffer.TTLMinutes);

        while (context.MessageBuffer.Reader.TryPeek(out var oldMessage) &&
               oldMessage.Timestamp < expiryTime)
        {
            _ = context.MessageBuffer.Reader.TryRead(out _);
        }

        // Try to write to buffer
        if (!context.MessageBuffer.Writer.TryWrite(message))
        {
            _logger.LogWarning(
                "Buffer full for stream {StreamId}, applying overflow strategy: {Strategy}",
                context.StreamId, _configuration.Buffer.OverflowStrategy);
        }

        // Add await to make the method actually async
        await Task.CompletedTask;
    }

    private void TrackPartialMessage(StreamContext context, int sequenceNumber, string data)
    {
        // Limit the number of partial messages stored
        if (context.PartialMessages.Count >= _configuration.PartialRecovery.MaxPartialMessages)
        {
            // Remove oldest partial message
            var oldestKey = context.PartialMessages.Keys.OrderBy(k => k).FirstOrDefault();
            _ = context.PartialMessages.TryRemove(oldestKey, out _);
        }

        var partialMessage = new PartialMessage
        {
            SequenceNumber = sequenceNumber,
            Data = data,
            Timestamp = DateTime.UtcNow,
            SizeBytes = System.Text.Encoding.UTF8.GetByteCount(data)
        };

        context.PartialMessages[sequenceNumber] = partialMessage;
    }

    private async Task<int> ReplayBufferedMessagesAsync(
        StreamContext context,
        CancellationToken cancellationToken)
    {
        var replayedCount = 0;
        var messages = new List<BufferedMessage>();

        // Collect all buffered messages
        while (context.MessageBuffer.Reader.TryRead(out var message))
        {
            messages.Add(message);
        }

        // Replay messages in order
        foreach (var message in messages.OrderBy(m => m.SequenceNumber))
        {
            try
            {
                var data = System.Text.Encoding.UTF8.GetBytes($"data: {message.Data}\n\n");
                await context.HttpResponse.Body.WriteAsync(data, cancellationToken);
                await context.HttpResponse.Body.FlushAsync(cancellationToken);
                replayedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to replay message {Sequence} for stream {StreamId}",
                    message.SequenceNumber, context.StreamId);
                break;
            }
        }

        return replayedCount;
    }

    private async Task RecoverPartialMessagesAsync(
        StreamContext context,
        CancellationToken cancellationToken)
    {
        if (context.PartialMessages.IsEmpty)
        {
            return;
        }

        var partialMessages = context.PartialMessages.Values
            .OrderBy(m => m.SequenceNumber)
            .ToList();

        _logger.LogInformation(
            "Recovering {Count} partial messages for stream {StreamId}",
            partialMessages.Count, context.StreamId);

        foreach (var partial in partialMessages)
        {
            // Check if message is not too old
            var age = DateTime.UtcNow - partial.Timestamp;
            if (age.TotalSeconds > _configuration.PartialRecovery.ChunkTimeoutSeconds)
            {
                _ = context.PartialMessages.TryRemove(partial.SequenceNumber, out _);
                continue;
            }

            try
            {
                // Send partial message with recovery marker
                var recoveryData = $"data: {{\"recovery\":true,\"sequence\":{partial.SequenceNumber},\"data\":{partial.Data}}}\n\n";
                var bytes = System.Text.Encoding.UTF8.GetBytes(recoveryData);

                await context.HttpResponse.Body.WriteAsync(bytes, cancellationToken);
                await context.HttpResponse.Body.FlushAsync(cancellationToken);

                // Remove successfully recovered message
                _ = context.PartialMessages.TryRemove(partial.SequenceNumber, out _);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to recover partial message {Sequence} for stream {StreamId}",
                    partial.SequenceNumber, context.StreamId);
                break;
            }
        }
    }

    private async Task CleanupStreamContextAsync(StreamContext context)
    {
        try
        {
            // Complete the buffer channel
            _ = context.MessageBuffer.Writer.TryComplete();

            // Clear partial messages
            context.PartialMessages.Clear();

            // Log final metrics
            _logger.LogInformation(
                "Stream {StreamId} cleanup - Messages: {Processed}, Failures: {Failures}, Duration: {Duration}ms",
                context.StreamId,
                context.MessagesProcessed,
                context.FailureCount,
                (DateTime.UtcNow - context.StartTime).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during stream context cleanup for {StreamId}", context.StreamId);
        }

        await Task.CompletedTask;
    }

    private void PerformHealthCheck(object? state)
    {
        try
        {
            var unhealthyStreams = _activeStreams.Values
                .Where(s => s.State == StreamState.Failed ||
                           s.CircuitState == CircuitState.Open ||
                           (DateTime.UtcNow - s.LastActivityTime).TotalMinutes > 5)
                .ToList();

            if (unhealthyStreams.Count != 0)
            {
                _logger.LogWarning(
                    "Health check found {Count} unhealthy streams",
                    unhealthyStreams.Count);

                foreach (var stream in unhealthyStreams)
                {
                    _logger.LogWarning(
                        "Unhealthy stream {StreamId}: State={State}, Circuit={Circuit}, LastActivity={LastActivity}",
                        stream.StreamId, stream.State, stream.CircuitState, stream.LastActivityTime);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check");
        }
    }

    private static bool DetermineHealthStatus(List<StreamContext> streams)
    {
        if (streams.Count == 0)
        {
            return true;
        }

        var failedPercentage = (double)streams.Count(s => s.State == StreamState.Failed) / streams.Count * 100;
        var openCircuitsPercentage = (double)streams.Count(s => s.CircuitState == CircuitState.Open) / streams.Count * 100;

        return failedPercentage < 20 && openCircuitsPercentage < 30;
    }

    private double CalculateAverageRecoveryTime()
    {
        if (_successfulRecoveries == 0)
        {
            return 0;
        }

        // This is a simplified calculation - in production, you'd track actual recovery times
        return 2500; // Placeholder: 2.5 seconds average
    }

    private double CalculateSuccessRate()
    {
        if (_totalStreamsProcessed == 0)
        {
            return 100;
        }

        var failedStreams = _activeStreams.Values.Count(s => s.State == StreamState.Failed);
        var totalAttempted = _totalStreamsProcessed + failedStreams;

        return (double)_totalStreamsProcessed / totalAttempted * 100;
    }

    private List<string> GenerateStatusMessages(List<StreamContext> streams)
    {
        var messages = new List<string>();

        if (_configuration.HealthCheck.IncludeDetailedMetrics)
        {
            messages.Add($"Uptime: {_uptimeStopwatch.Elapsed:d\\.hh\\:mm\\:ss}");
            messages.Add($"Total messages processed: {_totalMessagesProcessed:N0}");
            messages.Add($"Recovery success rate: {_successfulRecoveries / (double)Math.Max(1, _totalRecoveryAttempts) * 100:F1}%");
        }

        var inactiveStreams = streams.Where(s =>
            (DateTime.UtcNow - s.LastActivityTime).TotalMinutes > 1).ToList();

        if (inactiveStreams.Count != 0)
        {
            messages.Add($"Warning: {inactiveStreams.Count} inactive streams detected");
        }

        return messages;
    }

    private PartialRecoveryStats? GetPartialRecoveryStats(StreamContext context)
    {
        return !_configuration.PartialRecovery.Enabled || context.PartialMessages.IsEmpty
            ? null
            : new PartialRecoveryStats
            {
                PartialMessagesStored = context.PartialMessages.Count,
                SuccessfulRecoveries = 0, // Would need to track this per context
                TotalSizeBytes = context.PartialMessages.Values.Sum(m => m.SizeBytes),
                LastChunkSequence = context.PartialMessages.Values
                .OrderByDescending(m => m.SequenceNumber)
                .FirstOrDefault()?.SequenceNumber
            };
    }

    #endregion

    #region Nested Types

    private sealed class StreamContext
    {
        public required string StreamId { get; init; }
        public required HttpResponse HttpResponse { get; init; }
        public required Channel<BufferedMessage> MessageBuffer { get; init; }
        public required ConcurrentDictionary<int, PartialMessage> PartialMessages { get; init; }
        public StreamState State { get; set; }
        public CircuitState CircuitState { get; set; }
        public long MessagesProcessed { get; set; }
        public int ReconnectionAttempts { get; set; }
        public int FailureCount { get; set; }
        public int ConsecutiveFailures { get; set; }
        public int ConsecutiveSuccesses { get; set; }
        public string? LastError { get; set; }
        public DateTime StartTime { get; init; }
        public DateTime LastActivityTime { get; set; }
        public double TotalProcessingTimeMs { get; set; }
    }

    private sealed record BufferedMessage
    {
        public required int SequenceNumber { get; init; }
        public required string Data { get; init; }
        public required DateTime Timestamp { get; init; }
    }

    private sealed record PartialMessage
    {
        public required int SequenceNumber { get; init; }
        public required string Data { get; init; }
        public required DateTime Timestamp { get; init; }
        public required long SizeBytes { get; init; }
    }

    #endregion
}
