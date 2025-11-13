using System.Collections.Concurrent;

namespace AIChat.Server.Services.StateManagement;

/// <summary>
/// Collects and tracks performance metrics for state manager operations.
/// Thread-safe implementation for concurrent access across multiple operations.
/// </summary>
public class StateManagerMetricsCollector
{
    private readonly object _lock = new();
    private long _readOperations;
    private long _writeOperations;
    private long _deleteOperations;
    private long _failedOperations;
    private double _totalReadTimeMs;
    private double _totalWriteTimeMs;
    private double _totalDeleteTimeMs;
    private readonly ConcurrentQueue<double> _recentReadTimes = new();
    private readonly ConcurrentQueue<double> _recentWriteTimes = new();
    private readonly ConcurrentQueue<double> _recentDeleteTimes = new();
    private const int MaxRecentTimeSamples = 1000;

    /// <summary>
    /// Records a read operation completion.
    /// </summary>
    /// <param name="durationMs">The duration of the operation in milliseconds</param>
    /// <param name="success">Whether the operation was successful</param>
    public void RecordRead(double durationMs, bool success)
    {
        lock (_lock)
        {
            _readOperations++;
            _totalReadTimeMs += durationMs;

            if (!success)
            {
                _failedOperations++;
            }
        }

        // Track recent times for more accurate averages
        _recentReadTimes.Enqueue(durationMs);
        if (_recentReadTimes.Count > MaxRecentTimeSamples)
        {
            _ = _recentReadTimes.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Records a write operation completion.
    /// </summary>
    /// <param name="durationMs">The duration of the operation in milliseconds</param>
    /// <param name="success">Whether the operation was successful</param>
    public void RecordWrite(double durationMs, bool success)
    {
        lock (_lock)
        {
            _writeOperations++;
            _totalWriteTimeMs += durationMs;

            if (!success)
            {
                _failedOperations++;
            }
        }

        // Track recent times for more accurate averages
        _recentWriteTimes.Enqueue(durationMs);
        if (_recentWriteTimes.Count > MaxRecentTimeSamples)
        {
            _ = _recentWriteTimes.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Records a delete operation completion.
    /// </summary>
    /// <param name="durationMs">The duration of the operation in milliseconds</param>
    /// <param name="success">Whether the operation was successful</param>
    public void RecordDelete(double durationMs, bool success)
    {
        lock (_lock)
        {
            _deleteOperations++;
            _totalDeleteTimeMs += durationMs;

            if (!success)
            {
                _failedOperations++;
            }
        }

        // Track recent times for more accurate averages
        _recentDeleteTimes.Enqueue(durationMs);
        if (_recentDeleteTimes.Count > MaxRecentTimeSamples)
        {
            _ = _recentDeleteTimes.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Records a failed operation without timing information.
    /// </summary>
    public void RecordFailure()
    {
        lock (_lock)
        {
            _failedOperations++;
        }
    }

    /// <summary>
    /// Gets the current metrics snapshot.
    /// </summary>
    /// <param name="cacheStatistics">Optional cache statistics to include</param>
    /// <returns>Current state manager metrics</returns>
    public StateManagerMetrics GetMetrics(CacheStatistics? cacheStatistics = null)
    {
        lock (_lock)
        {
            return new StateManagerMetrics
            {
                ReadOperations = _readOperations,
                WriteOperations = _writeOperations,
                DeleteOperations = _deleteOperations,
                FailedOperations = _failedOperations,
                AverageReadTimeMs = CalculateAverageReadTime(),
                AverageWriteTimeMs = CalculateAverageWriteTime(),
                CacheStatistics = cacheStatistics,
                CollectedAt = DateTime.UtcNow,
                AdditionalMetrics = GetAdditionalMetrics()
            };
        }
    }

    /// <summary>
    /// Resets all metrics to zero.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _readOperations = 0;
            _writeOperations = 0;
            _deleteOperations = 0;
            _failedOperations = 0;
            _totalReadTimeMs = 0;
            _totalWriteTimeMs = 0;
            _totalDeleteTimeMs = 0;
        }

        // Clear recent time queues
        while (_recentReadTimes.TryDequeue(out _)) { }
        while (_recentWriteTimes.TryDequeue(out _)) { }
        while (_recentDeleteTimes.TryDequeue(out _)) { }
    }

    /// <summary>
    /// Gets the current operation counts without timing information.
    /// </summary>
    /// <returns>A summary of operation counts</returns>
    public OperationCounts GetOperationCounts()
    {
        lock (_lock)
        {
            return new OperationCounts
            {
                ReadOperations = _readOperations,
                WriteOperations = _writeOperations,
                DeleteOperations = _deleteOperations,
                FailedOperations = _failedOperations,
                TotalOperations = _readOperations + _writeOperations + _deleteOperations
            };
        }
    }

    private double CalculateAverageReadTime()
    {
        if (_readOperations == 0)
        {
            return 0;
        }

        // Use recent samples for more accurate average if available
        var recentTimes = _recentReadTimes.ToArray();
        if (recentTimes.Length > 0)
        {
            return recentTimes.Average();
        }

        return _totalReadTimeMs / _readOperations;
    }

    private double CalculateAverageWriteTime()
    {
        if (_writeOperations == 0)
        {
            return 0;
        }

        // Use recent samples for more accurate average if available
        var recentTimes = _recentWriteTimes.ToArray();
        if (recentTimes.Length > 0)
        {
            return recentTimes.Average();
        }

        return _totalWriteTimeMs / _writeOperations;
    }

    private Dictionary<string, object> GetAdditionalMetrics()
    {
        var recentReadTimes = _recentReadTimes.ToArray();
        var recentWriteTimes = _recentWriteTimes.ToArray();
        var recentDeleteTimes = _recentDeleteTimes.ToArray();

        var metrics = new Dictionary<string, object>
        {
            ["TotalOperationTimeMs"] = _totalReadTimeMs + _totalWriteTimeMs + _totalDeleteTimeMs,
            ["RecentReadSampleCount"] = recentReadTimes.Length,
            ["RecentWriteSampleCount"] = recentWriteTimes.Length,
            ["RecentDeleteSampleCount"] = recentDeleteTimes.Length
        };

        // Add percentile information if we have enough samples
        if (recentReadTimes.Length >= 10)
        {
            Array.Sort(recentReadTimes);
            metrics["ReadTimeP50Ms"] = recentReadTimes[recentReadTimes.Length / 2];
            metrics["ReadTimeP95Ms"] = recentReadTimes[(int)(recentReadTimes.Length * 0.95)];
            metrics["ReadTimeP99Ms"] = recentReadTimes[(int)(recentReadTimes.Length * 0.99)];
        }

        if (recentWriteTimes.Length >= 10)
        {
            Array.Sort(recentWriteTimes);
            metrics["WriteTimeP50Ms"] = recentWriteTimes[recentWriteTimes.Length / 2];
            metrics["WriteTimeP95Ms"] = recentWriteTimes[(int)(recentWriteTimes.Length * 0.95)];
            metrics["WriteTimeP99Ms"] = recentWriteTimes[(int)(recentWriteTimes.Length * 0.99)];
        }

        return metrics;
    }
}

/// <summary>
/// Represents a summary of operation counts for quick access.
/// </summary>
public record OperationCounts
{
    /// <summary>
    /// Gets the total number of read operations.
    /// </summary>
    public long ReadOperations { get; init; }

    /// <summary>
    /// Gets the total number of write operations.
    /// </summary>
    public long WriteOperations { get; init; }

    /// <summary>
    /// Gets the total number of delete operations.
    /// </summary>
    public long DeleteOperations { get; init; }

    /// <summary>
    /// Gets the total number of failed operations.
    /// </summary>
    public long FailedOperations { get; init; }

    /// <summary>
    /// Gets the total number of all operations.
    /// </summary>
    public long TotalOperations { get; init; }

    /// <summary>
    /// Gets the success rate as a percentage.
    /// </summary>
    public double SuccessRate => TotalOperations > 0 ? (double)(TotalOperations - FailedOperations) / TotalOperations * 100 : 100;

    /// <summary>
    /// Gets the failure rate as a percentage.
    /// </summary>
    public double FailureRate => 100 - SuccessRate;
}
