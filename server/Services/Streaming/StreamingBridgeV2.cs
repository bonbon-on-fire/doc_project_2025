using System.Diagnostics;
using System.Text;
using AIChat.Server.Configuration;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Refactored implementation of IStreamingBridge that orchestrates separate components
/// for better separation of concerns and adherence to SOLID principles.
/// </summary>
public sealed class StreamingBridgeV2 : IStreamingBridge
{
    private readonly ILogger<StreamingBridgeV2> _logger;
    private readonly StreamingConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private IBufferManager<StreamItem>? _bufferManager;
    private IHttpStreamWriter? _httpWriter;
    private IBackpressureHandler? _backpressureHandler;
    private IStreamingMetrics? _metrics;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the StreamingBridgeV2 class.
    /// </summary>
    /// <param name="logger">Logger for diagnostics</param>
    /// <param name="configuration">Streaming configuration settings</param>
    /// <param name="loggerFactory">Logger factory for creating component loggers</param>
    public StreamingBridgeV2(
        ILogger<StreamingBridgeV2> logger,
        IOptions<StreamingConfiguration> configuration,
        ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

        // Validate configuration
        if (!_configuration.Validate(out var errors))
        {
            var errorMessage = string.Join("; ", errors);
            _logger.LogError("Invalid streaming configuration: {Errors}", errorMessage);
            throw new InvalidOperationException($"Invalid streaming configuration: {errorMessage}");
        }

        _logger.LogInformation(
            "StreamingBridgeV2 initialized with buffer size: {BufferSize}, backpressure threshold: {Threshold}%",
            _configuration.BufferSize,
            _configuration.BackpressureThreshold);
    }

    /// <inheritdoc />
    public async Task ConvertGrainToHttpStreamAsync<T>(
        IAsyncEnumerable<T> grainStream,
        HttpResponse httpResponse,
        Func<T, string> formatter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainStream);
        ArgumentNullException.ThrowIfNull(httpResponse);
        ArgumentNullException.ThrowIfNull(formatter);

        ThrowIfDisposed();

        // Initialize components for this streaming session
        await using var bufferManager = new BufferManager<StreamItem>(_configuration.BufferSize);
        await using var httpWriter = new HttpStreamWriter(
            httpResponse,
            _loggerFactory.CreateLogger<HttpStreamWriter>(),
            _configuration.WriteTimeoutMs);
        
        var backpressureHandler = new BackpressureHandler(
            _loggerFactory.CreateLogger<BackpressureHandler>(),
            _configuration.BackpressureThreshold,
            _configuration.BackpressureDelayMs,
            _configuration.EnableAdaptiveBackpressure);
        
        var metrics = new StreamingMetrics(_loggerFactory.CreateLogger<StreamingMetrics>());

        // Store references for GetBufferStatistics
        _bufferManager = bufferManager;
        _httpWriter = httpWriter;
        _backpressureHandler = backpressureHandler;
        _metrics = metrics;

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            httpResponse.HttpContext.RequestAborted);

        try
        {
            // Start consumer task
            var consumerTask = ConsumeAndWriteAsync(
                bufferManager,
                httpWriter,
                metrics,
                linkedCts.Token);

            // Start producer task
            var producerTask = ProduceFromGrainAsync(
                grainStream,
                formatter,
                bufferManager,
                backpressureHandler,
                metrics,
                linkedCts.Token);

            // Wait for both tasks to complete
            await Task.WhenAll(producerTask, consumerTask);

            _logger.LogInformation(
                "Stream conversion completed. Stats: {Stats}",
                metrics.GetStatistics());
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Stream conversion cancelled");
            metrics.RecordError("OperationCancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during stream conversion");
            metrics.RecordError(ex.GetType().Name);
            await PropagateErrorAsync(ex, httpResponse, CancellationToken.None);
            throw;
        }
        finally
        {
            linkedCts.Dispose();
            _bufferManager = null;
            _httpWriter = null;
            _backpressureHandler = null;
            _metrics = null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> HandleBackpressureAsync(
        float bufferUtilization,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_backpressureHandler == null)
        {
            // Create a default handler if not in streaming context
            var handler = new BackpressureHandler(
                _loggerFactory.CreateLogger<BackpressureHandler>(),
                _configuration.BackpressureThreshold,
                _configuration.BackpressureDelayMs,
                _configuration.EnableAdaptiveBackpressure);

            var delay = await handler.ApplyBackpressureAsync(bufferUtilization, cancellationToken);
            return delay > 0;
        }

        var appliedDelay = await _backpressureHandler.ApplyBackpressureAsync(bufferUtilization, cancellationToken);
        return appliedDelay > 0;
    }

    /// <inheritdoc />
    public async Task PropagateErrorAsync(
        Exception exception,
        HttpResponse httpResponse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(httpResponse);

        ThrowIfDisposed();

        if (_httpWriter != null)
        {
            await _httpWriter.WriteErrorAsync(
                exception,
                httpResponse.HttpContext.TraceIdentifier,
                cancellationToken);
        }
        else
        {
            // Fallback if not in streaming context
            await using var writer = new HttpStreamWriter(
                httpResponse,
                _loggerFactory.CreateLogger<HttpStreamWriter>(),
                _configuration.WriteTimeoutMs);

            await writer.WriteErrorAsync(
                exception,
                httpResponse.HttpContext.TraceIdentifier,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public BufferStatistics GetBufferStatistics()
    {
        ThrowIfDisposed();

        if (_bufferManager == null || _metrics == null)
        {
            // Return empty statistics if not in streaming context
            return new BufferStatistics
            {
                Capacity = _configuration.BufferSize,
                CurrentSize = 0,
                UtilizationPercentage = 0,
                ItemsProcessed = 0,
                BackpressureEvents = 0,
                AverageProcessingTimeMs = 0
            };
        }

        var stats = _metrics.GetStatistics();
        return new BufferStatistics
        {
            Capacity = _bufferManager.Capacity,
            CurrentSize = _bufferManager.Count,
            UtilizationPercentage = _bufferManager.UtilizationPercentage,
            ItemsProcessed = stats.ItemsProcessed,
            BackpressureEvents = stats.BackpressureEvents,
            AverageProcessingTimeMs = stats.AverageProcessingTimeMs
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        _logger.LogInformation("StreamingBridgeV2 disposed");

        GC.SuppressFinalize(this);
        await Task.CompletedTask;
    }

    private async Task ProduceFromGrainAsync<T>(
        IAsyncEnumerable<T> grainStream,
        Func<T, string> formatter,
        IBufferManager<StreamItem> bufferManager,
        IBackpressureHandler backpressureHandler,
        IStreamingMetrics metrics,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in grainStream.WithCancellation(cancellationToken))
            {
                var formattedData = formatter(item);
                var streamItem = new StreamItem
                {
                    Data = formattedData,
                    Timestamp = DateTime.UtcNow
                };

                // Check if backpressure is needed
                var utilization = bufferManager.UtilizationPercentage;
                if (backpressureHandler.ShouldApplyBackpressure(utilization))
                {
                    var delay = await backpressureHandler.ApplyBackpressureAsync(utilization, cancellationToken);
                    metrics.RecordBackpressureEvent(delay);
                }

                // Try non-blocking write first
                if (!await bufferManager.TryWriteAsync(streamItem))
                {
                    // Buffer is full, apply maximum backpressure
                    var delay = await backpressureHandler.ApplyBackpressureAsync(100, cancellationToken);
                    metrics.RecordBackpressureEvent(delay);
                    
                    // Then do blocking write
                    await bufferManager.WriteAsync(streamItem, cancellationToken);
                }

                if (_configuration.EnableTelemetry)
                {
                    _logger.LogDebug(
                        "Produced item to buffer. Current size: {Size}, Utilization: {Utilization:F1}%",
                        bufferManager.Count,
                        utilization);
                }
            }
        }
        finally
        {
            bufferManager.Complete();
            _logger.LogInformation(
                "Producer completed. Total backpressure events: {Events}",
                backpressureHandler.BackpressureEventCount);
        }
    }

    private async Task ConsumeAndWriteAsync(
        IBufferManager<StreamItem> bufferManager,
        IHttpStreamWriter httpWriter,
        IStreamingMetrics metrics,
        CancellationToken cancellationToken)
    {
        var lastFlush = DateTime.UtcNow;
        var flushInterval = TimeSpan.FromMilliseconds(_configuration.FlushIntervalMs);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await foreach (var item in bufferManager.ReadAllAsync(cancellationToken))
            {
                var processingStart = stopwatch.ElapsedMilliseconds;

                // Write to HTTP stream
                await httpWriter.WriteDataAsync(item.Data, cancellationToken);

                // Track metrics
                var processingTime = stopwatch.ElapsedMilliseconds - processingStart;
                metrics.RecordItemProcessed(processingTime);
                metrics.RecordBytesWritten(Encoding.UTF8.GetByteCount(item.Data));

                // Flush periodically
                if (DateTime.UtcNow - lastFlush >= flushInterval)
                {
                    await httpWriter.FlushAsync(cancellationToken);
                    lastFlush = DateTime.UtcNow;
                }

                if (_configuration.EnableTelemetry && processingTime > 10)
                {
                    _logger.LogWarning(
                        "Chunk processing exceeded 10ms threshold: {Time}ms",
                        processingTime);
                }
            }

            // Final flush
            await httpWriter.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Consumer cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in consumer");
            metrics.RecordError(ex.GetType().Name);
            throw;
        }
        finally
        {
            _logger.LogInformation(
                "Consumer completed. Total items: {Items}, Total bytes: {Bytes}",
                metrics.GetStatistics().ItemsProcessed,
                httpWriter.BytesWritten);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(StreamingBridgeV2));
    }

    private record StreamItem
    {
        public required string Data { get; init; }
        public required DateTime Timestamp { get; init; }
    }
}