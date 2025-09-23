using System.Diagnostics;

namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Collects and tracks performance metrics for event store operations.
/// Provides detailed instrumentation for monitoring event store performance.
/// </summary>
public class EventStoreMetricsCollector
{
    private readonly object _lock = new();
    private long _appendOperations;
    private long _readOperations;
    private long _queryOperations;
    private long _replayOperations;
    private long _failedOperations;
    private double _totalAppendTimeMs;
    private double _totalReadTimeMs;
    private double _totalQueryTimeMs;
    private double _totalReplayTimeMs;

    /// <summary>
    /// Starts tracking an append operation.
    /// </summary>
    /// <returns>A disposable activity for tracking timing</returns>
    public IDisposable StartAppendActivity()
    {
        return new MetricsActivity(this, OperationType.Append);
    }

    /// <summary>
    /// Starts tracking a read operation.
    /// </summary>
    /// <returns>A disposable activity for tracking timing</returns>
    public IDisposable StartReadActivity()
    {
        return new MetricsActivity(this, OperationType.Read);
    }

    /// <summary>
    /// Starts tracking a query operation.
    /// </summary>
    /// <returns>A disposable activity for tracking timing</returns>
    public IDisposable StartQueryActivity()
    {
        return new MetricsActivity(this, OperationType.Query);
    }

    /// <summary>
    /// Starts tracking a replay operation.
    /// </summary>
    /// <returns>A disposable activity for tracking timing</returns>
    public IDisposable StartReplayActivity()
    {
        return new MetricsActivity(this, OperationType.Replay);
    }

    /// <summary>
    /// Records a successful append operation.
    /// </summary>
    /// <param name="eventsCount">Number of events appended</param>
    public void RecordAppendSuccess(int eventsCount = 1)
    {
        lock (_lock)
        {
            _appendOperations += eventsCount;
        }
    }

    /// <summary>
    /// Records a failed append operation.
    /// </summary>
    public void RecordAppendFailure()
    {
        lock (_lock)
        {
            _failedOperations++;
        }
    }

    /// <summary>
    /// Records a successful read operation.
    /// </summary>
    public void RecordReadSuccess()
    {
        lock (_lock)
        {
            _readOperations++;
        }
    }

    /// <summary>
    /// Records a failed read operation.
    /// </summary>
    public void RecordReadFailure()
    {
        lock (_lock)
        {
            _failedOperations++;
        }
    }

    /// <summary>
    /// Records a successful query operation.
    /// </summary>
    public void RecordQuerySuccess()
    {
        lock (_lock)
        {
            _queryOperations++;
        }
    }

    /// <summary>
    /// Records a failed query operation.
    /// </summary>
    public void RecordQueryFailure()
    {
        lock (_lock)
        {
            _failedOperations++;
        }
    }

    /// <summary>
    /// Records a successful replay operation.
    /// </summary>
    public void RecordReplaySuccess()
    {
        lock (_lock)
        {
            _replayOperations++;
        }
    }

    /// <summary>
    /// Records a failed replay operation.
    /// </summary>
    public void RecordReplayFailure()
    {
        lock (_lock)
        {
            _failedOperations++;
        }
    }

    /// <summary>
    /// Records the execution time for an operation.
    /// </summary>
    /// <param name="operationType">The type of operation</param>
    /// <param name="executionTimeMs">The execution time in milliseconds</param>
    internal void RecordExecutionTime(OperationType operationType, double executionTimeMs)
    {
        lock (_lock)
        {
            switch (operationType)
            {
                case OperationType.Append:
                    _totalAppendTimeMs += executionTimeMs;
                    break;
                case OperationType.Read:
                    _totalReadTimeMs += executionTimeMs;
                    break;
                case OperationType.Query:
                    _totalQueryTimeMs += executionTimeMs;
                    break;
                case OperationType.Replay:
                    _totalReplayTimeMs += executionTimeMs;
                    break;
            }
        }
    }

    /// <summary>
    /// Gets the current metrics snapshot.
    /// </summary>
    /// <returns>Current event store metrics</returns>
    public EventStoreMetrics GetCurrentMetrics()
    {
        lock (_lock)
        {
            return new EventStoreMetrics
            {
                AppendOperations = _appendOperations,
                ReadOperations = _readOperations,
                QueryOperations = _queryOperations,
                ReplayOperations = _replayOperations,
                FailedOperations = _failedOperations,
                AverageAppendTimeMs = _appendOperations > 0 ? _totalAppendTimeMs / _appendOperations : 0,
                AverageReadTimeMs = _readOperations > 0 ? _totalReadTimeMs / _readOperations : 0,
                AverageQueryTimeMs = _queryOperations > 0 ? _totalQueryTimeMs / _queryOperations : 0,
                AverageReplayTimeMs = _replayOperations > 0 ? _totalReplayTimeMs / _replayOperations : 0,
                TotalEvents = 0, // Will be filled by the event store implementation
                TotalStreams = 0, // Will be filled by the event store implementation
                CollectedAt = DateTime.UtcNow
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
            _appendOperations = 0;
            _readOperations = 0;
            _queryOperations = 0;
            _replayOperations = 0;
            _failedOperations = 0;
            _totalAppendTimeMs = 0;
            _totalReadTimeMs = 0;
            _totalQueryTimeMs = 0;
            _totalReplayTimeMs = 0;
        }
    }

    /// <summary>
    /// Represents the type of operation being tracked.
    /// </summary>
    internal enum OperationType
    {
        Append,
        Read,
        Query,
        Replay
    }

    /// <summary>
    /// Activity tracker for measuring operation execution time.
    /// </summary>
    private sealed class MetricsActivity : IDisposable
    {
        private readonly EventStoreMetricsCollector _collector;
        private readonly OperationType _operationType;
        private readonly Stopwatch _stopwatch;
        private bool _disposed;

        public MetricsActivity(EventStoreMetricsCollector collector, OperationType operationType)
        {
            _collector = collector;
            _operationType = operationType;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _stopwatch.Stop();
                _collector.RecordExecutionTime(_operationType, _stopwatch.Elapsed.TotalMilliseconds);
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Cache statistics for event store operations.
/// </summary>
public record CacheStatistics
{
    /// <summary>
    /// Gets the total number of cache hits.
    /// </summary>
    public long Hits { get; init; }

    /// <summary>
    /// Gets the total number of cache misses.
    /// </summary>
    public long Misses { get; init; }

    /// <summary>
    /// Gets the cache hit ratio as a percentage.
    /// </summary>
    public double HitRatio => TotalRequests > 0 ? (double)Hits / TotalRequests * 100 : 0;

    /// <summary>
    /// Gets the total number of cache requests.
    /// </summary>
    public long TotalRequests => Hits + Misses;

    /// <summary>
    /// Gets the number of items currently in cache.
    /// </summary>
    public long ItemCount { get; init; }

    /// <summary>
    /// Gets the approximate memory usage of the cache in bytes.
    /// </summary>
    public long MemoryUsageBytes { get; init; }

    /// <summary>
    /// Creates empty cache statistics.
    /// </summary>
    /// <returns>Empty cache statistics</returns>
    public static CacheStatistics Empty()
    {
        return new CacheStatistics
        {
            Hits = 0,
            Misses = 0,
            ItemCount = 0,
            MemoryUsageBytes = 0
        };
    }

    /// <summary>
    /// Creates cache statistics with the specified values.
    /// </summary>
    /// <param name="hits">Number of cache hits</param>
    /// <param name="misses">Number of cache misses</param>
    /// <param name="itemCount">Number of items in cache</param>
    /// <param name="memoryUsageBytes">Memory usage in bytes</param>
    /// <returns>Cache statistics</returns>
    public static CacheStatistics Create(long hits, long misses, long itemCount = 0, long memoryUsageBytes = 0)
    {
        return new CacheStatistics
        {
            Hits = hits,
            Misses = misses,
            ItemCount = itemCount,
            MemoryUsageBytes = memoryUsageBytes
        };
    }
}

/// <summary>
/// Validation-specific metrics for event store operations.
/// </summary>
public record StateValidationMetrics
{
    /// <summary>
    /// Gets the total number of validation operations.
    /// </summary>
    public long ValidationOperations { get; init; }

    /// <summary>
    /// Gets the number of successful validations.
    /// </summary>
    public long SuccessfulValidations { get; init; }

    /// <summary>
    /// Gets the number of failed validations.
    /// </summary>
    public long FailedValidations { get; init; }

    /// <summary>
    /// Gets the average validation time in milliseconds.
    /// </summary>
    public double AverageValidationTimeMs { get; init; }

    /// <summary>
    /// Gets the validation success rate as a percentage.
    /// </summary>
    public double SuccessRate => ValidationOperations > 0
        ? (double)SuccessfulValidations / ValidationOperations * 100
        : 100;

    /// <summary>
    /// Creates empty validation metrics.
    /// </summary>
    /// <returns>Empty validation metrics</returns>
    public static StateValidationMetrics Empty()
    {
        return new StateValidationMetrics
        {
            ValidationOperations = 0,
            SuccessfulValidations = 0,
            FailedValidations = 0,
            AverageValidationTimeMs = 0
        };
    }
}

/// <summary>
/// Consistency checking metrics for event store operations.
/// </summary>
public record ConsistencyCheckMetrics
{
    /// <summary>
    /// Gets the total number of consistency checks performed.
    /// </summary>
    public long ConsistencyChecks { get; init; }

    /// <summary>
    /// Gets the number of consistency violations found.
    /// </summary>
    public long ConsistencyViolations { get; init; }

    /// <summary>
    /// Gets the average consistency check time in milliseconds.
    /// </summary>
    public double AverageCheckTimeMs { get; init; }

    /// <summary>
    /// Gets the consistency rate as a percentage.
    /// </summary>
    public double ConsistencyRate => ConsistencyChecks > 0
        ? (double)(ConsistencyChecks - ConsistencyViolations) / ConsistencyChecks * 100
        : 100;

    /// <summary>
    /// Creates empty consistency check metrics.
    /// </summary>
    /// <returns>Empty consistency check metrics</returns>
    public static ConsistencyCheckMetrics Empty()
    {
        return new ConsistencyCheckMetrics
        {
            ConsistencyChecks = 0,
            ConsistencyViolations = 0,
            AverageCheckTimeMs = 0
        };
    }
}

/// <summary>
/// Error recovery metrics for event store operations.
/// </summary>
public record ErrorRecoveryMetrics
{
    /// <summary>
    /// Gets the total number of recovery attempts.
    /// </summary>
    public long RecoveryAttempts { get; init; }

    /// <summary>
    /// Gets the number of successful recoveries.
    /// </summary>
    public long SuccessfulRecoveries { get; init; }

    /// <summary>
    /// Gets the number of failed recoveries.
    /// </summary>
    public long FailedRecoveries { get; init; }

    /// <summary>
    /// Gets the average recovery time in milliseconds.
    /// </summary>
    public double AverageRecoveryTimeMs { get; init; }

    /// <summary>
    /// Gets the recovery success rate as a percentage.
    /// </summary>
    public double RecoverySuccessRate => RecoveryAttempts > 0
        ? (double)SuccessfulRecoveries / RecoveryAttempts * 100
        : 100;

    /// <summary>
    /// Creates empty error recovery metrics.
    /// </summary>
    /// <returns>Empty error recovery metrics</returns>
    public static ErrorRecoveryMetrics Empty()
    {
        return new ErrorRecoveryMetrics
        {
            RecoveryAttempts = 0,
            SuccessfulRecoveries = 0,
            FailedRecoveries = 0,
            AverageRecoveryTimeMs = 0
        };
    }
}