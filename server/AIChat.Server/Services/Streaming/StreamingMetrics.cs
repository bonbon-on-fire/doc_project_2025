using System.Collections.Concurrent;
using System.Diagnostics;

namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Thread-safe metrics collector for streaming operations.
/// </summary>
public sealed class StreamingMetrics : IStreamingMetrics
{
    private long _itemsProcessed;
    private long _totalProcessingTimeMs;
    private long _maxProcessingTimeMs;
    private long _minProcessingTimeMs = long.MaxValue;
    private long _backpressureEvents;
    private long _totalBackpressureDelayMs;
    private long _totalBytesWritten;
    private long _errorCount;
    private readonly ConcurrentDictionary<string, long> _errorsByType;
    private readonly DateTime _startTime;
    private readonly Stopwatch _stopwatch;
    private readonly ILogger<StreamingMetrics>? _logger;

    /// <summary>
    /// Initializes a new instance of the StreamingMetrics class.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics</param>
    public StreamingMetrics(ILogger<StreamingMetrics>? logger = null)
    {
        _logger = logger;
        _errorsByType = new ConcurrentDictionary<string, long>();
        _startTime = DateTime.UtcNow;
        _stopwatch = Stopwatch.StartNew();
    }

    /// <inheritdoc />
    public void RecordItemProcessed(long processingTimeMs)
    {
        if (processingTimeMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processingTimeMs),
                "Processing time cannot be negative"
            );
        }

        _ = Interlocked.Increment(ref _itemsProcessed);
        _ = Interlocked.Add(ref _totalProcessingTimeMs, processingTimeMs);

        // Update max processing time
        long currentMax;
        do
        {
            currentMax = _maxProcessingTimeMs;
            if (processingTimeMs <= currentMax)
            {
                break;
            }
        } while (
            Interlocked.CompareExchange(ref _maxProcessingTimeMs, processingTimeMs, currentMax)
            != currentMax
        );

        // Update min processing time
        long currentMin;
        do
        {
            currentMin = _minProcessingTimeMs;
            if (processingTimeMs >= currentMin)
            {
                break;
            }
        } while (
            Interlocked.CompareExchange(ref _minProcessingTimeMs, processingTimeMs, currentMin)
            != currentMin
        );

        if (_logger?.IsEnabled(LogLevel.Trace) == true)
        {
            _logger.LogTrace(
                "Item processed in {Time}ms. Total items: {Count}",
                processingTimeMs,
                _itemsProcessed
            );
        }
    }

    /// <inheritdoc />
    public void RecordBackpressureEvent(int delayMs)
    {
        if (delayMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delayMs), "Delay cannot be negative");
        }

        _ = Interlocked.Increment(ref _backpressureEvents);
        _ = Interlocked.Add(ref _totalBackpressureDelayMs, delayMs);
    }

    /// <inheritdoc />
    public void RecordError(string errorType)
    {
        ArgumentNullException.ThrowIfNull(errorType);

        _ = Interlocked.Increment(ref _errorCount);
        _ = _errorsByType.AddOrUpdate(errorType, 1, (_, count) => count + 1);

        _logger?.LogWarning(
            "Error recorded: {ErrorType}. Total errors: {Count}",
            errorType,
            _errorCount
        );
    }

    /// <inheritdoc />
    public void RecordBytesWritten(long bytes)
    {
        if (bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "Bytes cannot be negative");
        }

        _ = Interlocked.Add(ref _totalBytesWritten, bytes);
    }

    /// <inheritdoc />
    public StreamingStatistics GetStatistics()
    {
        var itemsProcessed = Interlocked.Read(ref _itemsProcessed);
        var totalProcessingTime = Interlocked.Read(ref _totalProcessingTimeMs);
        var duration = _stopwatch.Elapsed;

        var avgProcessingTime =
            itemsProcessed > 0 ? (double)totalProcessingTime / itemsProcessed : 0;

        var throughput = duration.TotalSeconds > 0 ? itemsProcessed / duration.TotalSeconds : 0;

        var minTime = Interlocked.Read(ref _minProcessingTimeMs);
        if (minTime == long.MaxValue)
        {
            minTime = 0;
        }

        return new StreamingStatistics
        {
            ItemsProcessed = itemsProcessed,
            AverageProcessingTimeMs = avgProcessingTime,
            MaxProcessingTimeMs = Interlocked.Read(ref _maxProcessingTimeMs),
            MinProcessingTimeMs = minTime,
            BackpressureEvents = Interlocked.Read(ref _backpressureEvents),
            TotalBackpressureDelayMs = Interlocked.Read(ref _totalBackpressureDelayMs),
            ErrorCount = Interlocked.Read(ref _errorCount),
            ErrorsByType = new Dictionary<string, long>(_errorsByType),
            TotalBytesWritten = Interlocked.Read(ref _totalBytesWritten),
            ThroughputPerSecond = throughput,
            StartTime = _startTime,
            Duration = duration,
        };
    }

    /// <inheritdoc />
    public void Reset()
    {
        _ = Interlocked.Exchange(ref _itemsProcessed, 0);
        _ = Interlocked.Exchange(ref _totalProcessingTimeMs, 0);
        _ = Interlocked.Exchange(ref _maxProcessingTimeMs, 0);
        _ = Interlocked.Exchange(ref _minProcessingTimeMs, long.MaxValue);
        _ = Interlocked.Exchange(ref _backpressureEvents, 0);
        _ = Interlocked.Exchange(ref _totalBackpressureDelayMs, 0);
        _ = Interlocked.Exchange(ref _totalBytesWritten, 0);
        _ = Interlocked.Exchange(ref _errorCount, 0);
        _errorsByType.Clear();
        _stopwatch.Restart();

        _logger?.LogInformation("Streaming metrics reset");
    }
}
