using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using AIChat.Server.Configuration;
using AIChat.Server.Extensions;
using AIChat.Server.Models.SSE;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Implementation of IStreamingBridge that converts Orleans grain streams to HTTP SSE streams.
/// Provides buffer management, backpressure handling, and error propagation.
/// </summary>
public sealed class StreamingBridge : IStreamingBridge
{
    private readonly ILogger<StreamingBridge> _logger;
    private readonly StreamingConfiguration _configuration;
    private readonly Channel<StreamItem> _buffer;
    private readonly SemaphoreSlim _writeSemaphore;
    private readonly object _statsLock = new();
    
    private long _itemsProcessed;
    private long _backpressureEvents;
    private long _totalProcessingTimeMs;
    private readonly Stopwatch _stopwatch;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the StreamingBridge class.
    /// </summary>
    /// <param name="logger">Logger for diagnostics</param>
    /// <param name="configuration">Streaming configuration settings</param>
    public StreamingBridge(
        ILogger<StreamingBridge> logger,
        IOptions<StreamingConfiguration> configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));

        // Validate configuration
        if (!_configuration.Validate(out var errors))
        {
            var errorMessage = string.Join("; ", errors);
            _logger.LogError("Invalid streaming configuration: {Errors}", errorMessage);
            throw new InvalidOperationException($"Invalid streaming configuration: {errorMessage}");
        }

        // Create bounded channel for buffering
        var channelOptions = new BoundedChannelOptions(_configuration.BufferSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = false
        };
        _buffer = Channel.CreateBounded<StreamItem>(channelOptions);
        
        _writeSemaphore = new SemaphoreSlim(1, 1);
        _stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation(
            "StreamingBridge initialized with buffer size: {BufferSize}, backpressure threshold: {Threshold}%",
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

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            httpResponse.HttpContext.RequestAborted);

        try
        {
            // Start consumer task
            var consumerTask = ConsumeAndWriteToHttpAsync(
                httpResponse,
                linkedCts.Token);

            // Start producer task
            var producerTask = ProduceFromGrainAsync(
                grainStream,
                formatter,
                linkedCts.Token);

            // Wait for both tasks to complete
            await Task.WhenAll(producerTask, consumerTask);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Stream conversion cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during stream conversion");
            await PropagateErrorAsync(ex, httpResponse, CancellationToken.None);
            throw;
        }
        finally
        {
            linkedCts.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<bool> HandleBackpressureAsync(
        float bufferUtilization,
        CancellationToken cancellationToken = default)
    {
        if (bufferUtilization < _configuration.BackpressureThreshold)
        {
            return false; // No backpressure needed
        }

        Interlocked.Increment(ref _backpressureEvents);
        
        var delay = _configuration.BackpressureDelayMs;
        
        if (_configuration.EnableAdaptiveBackpressure)
        {
            // Adaptive delay based on utilization
            var utilizationFactor = (bufferUtilization - _configuration.BackpressureThreshold) / 
                                   (100 - _configuration.BackpressureThreshold);
            delay = (int)(delay * (1 + utilizationFactor * 2)); // Up to 3x delay at 100% utilization
        }

        _logger.LogWarning(
            "Backpressure triggered. Buffer utilization: {Utilization}%, applying delay: {Delay}ms",
            bufferUtilization,
            delay);

        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task PropagateErrorAsync(
        Exception exception,
        HttpResponse httpResponse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(httpResponse);

        var errorEnvelope = SSEEventExtensions.CreateErrorEnvelope(
            httpResponse.HttpContext.TraceIdentifier,
            null,
            null,
            exception.Message,
            exception.GetType().Name);

        var errorData = $"data: {System.Text.Json.JsonSerializer.Serialize(errorEnvelope)}\n\n";
        var errorBytes = Encoding.UTF8.GetBytes(errorData);

        try
        {
            await _writeSemaphore.WaitAsync(cancellationToken);
            await httpResponse.Body.WriteAsync(errorBytes, cancellationToken);
            await httpResponse.Body.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to propagate error to HTTP stream");
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <inheritdoc />
    public BufferStatistics GetBufferStatistics()
    {
        lock (_statsLock)
        {
            var currentSize = _buffer.Reader.Count;
            var utilization = (float)currentSize / _configuration.BufferSize * 100;
            var avgProcessingTime = _itemsProcessed > 0 
                ? (double)_totalProcessingTimeMs / _itemsProcessed 
                : 0;

            return new BufferStatistics
            {
                Capacity = _configuration.BufferSize,
                CurrentSize = currentSize,
                UtilizationPercentage = utilization,
                ItemsProcessed = _itemsProcessed,
                BackpressureEvents = _backpressureEvents,
                AverageProcessingTimeMs = avgProcessingTime
            };
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _buffer.Writer.TryComplete();
            _writeSemaphore?.Dispose();
            _stopwatch?.Stop();

            var stats = GetBufferStatistics();
            _logger.LogInformation(
                "StreamingBridge disposed. Final stats - Items: {Items}, Backpressure events: {Backpressure}, Avg time: {AvgTime}ms",
                stats.ItemsProcessed,
                stats.BackpressureEvents,
                stats.AverageProcessingTimeMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during StreamingBridge disposal");
        }

        await Task.CompletedTask;
    }

    private async Task ProduceFromGrainAsync<T>(
        IAsyncEnumerable<T> grainStream,
        Func<T, string> formatter,
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

                // Check buffer utilization
                var stats = GetBufferStatistics();
                if (stats.UtilizationPercentage >= _configuration.BackpressureThreshold)
                {
                    await HandleBackpressureAsync(stats.UtilizationPercentage, cancellationToken);
                }

                // Try to write to buffer
                while (!_buffer.Writer.TryWrite(streamItem))
                {
                    // Buffer is full, apply backpressure
                    await HandleBackpressureAsync(100, cancellationToken);
                }

                if (_configuration.EnableTelemetry)
                {
                    _logger.LogDebug("Produced item to buffer. Current size: {Size}", stats.CurrentSize + 1);
                }
            }
        }
        finally
        {
            _buffer.Writer.TryComplete();
            _logger.LogInformation("Producer completed");
        }
    }

    private async Task ConsumeAndWriteToHttpAsync(
        HttpResponse httpResponse,
        CancellationToken cancellationToken)
    {
        var lastFlush = DateTime.UtcNow;
        var flushInterval = TimeSpan.FromMilliseconds(_configuration.FlushIntervalMs);

        try
        {
            await foreach (var item in _buffer.Reader.ReadAllAsync(cancellationToken))
            {
                var processingStart = _stopwatch.ElapsedMilliseconds;

                // Write to HTTP stream
                var data = $"data: {item.Data}\n\n";
                var bytes = Encoding.UTF8.GetBytes(data);

                await _writeSemaphore.WaitAsync(cancellationToken);
                try
                {
                    using var cts = new CancellationTokenSource(_configuration.WriteTimeoutMs);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, cts.Token);
                    
                    await httpResponse.Body.WriteAsync(bytes, linkedCts.Token);

                    // Flush periodically
                    if (DateTime.UtcNow - lastFlush >= flushInterval)
                    {
                        await httpResponse.Body.FlushAsync(linkedCts.Token);
                        lastFlush = DateTime.UtcNow;
                    }
                }
                finally
                {
                    _writeSemaphore.Release();
                }

                // Update statistics
                var processingTime = _stopwatch.ElapsedMilliseconds - processingStart;
                lock (_statsLock)
                {
                    _itemsProcessed++;
                    _totalProcessingTimeMs += processingTime;
                }

                if (_configuration.EnableTelemetry && processingTime > 10)
                {
                    _logger.LogWarning(
                        "Chunk processing exceeded 10ms threshold: {Time}ms",
                        processingTime);
                }
            }

            // Final flush
            await httpResponse.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Consumer cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in consumer");
            throw;
        }
        finally
        {
            _logger.LogInformation("Consumer completed");
        }
    }

    private record StreamItem
    {
        public required string Data { get; init; }
        public required DateTime Timestamp { get; init; }
    }
}