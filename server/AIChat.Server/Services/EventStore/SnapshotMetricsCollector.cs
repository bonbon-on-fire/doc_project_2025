using System.Diagnostics;

namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Collects and tracks performance metrics for snapshot store operations.
/// Provides detailed instrumentation for monitoring snapshot store performance.
/// </summary>
public class SnapshotMetricsCollector
{
    private readonly object _lock = new();
    private long _createOperations;
    private long _readOperations;
    private long _queryOperations;
    private long _deleteOperations;
    private long _optimizeOperations;
    private long _failedOperations;
    private double _totalCreateTimeMs;
    private double _totalReadTimeMs;
    private double _totalQueryTimeMs;
    private double _totalDeleteTimeMs;
    private double _totalOptimizeTimeMs;

    /// <summary>
    /// Starts tracking a create operation.
    /// </summary>
    /// <returns>A disposable activity for tracking timing</returns>
    public IDisposable StartCreateActivity()
    {
        return new SnapshotMetricsActivity(this, SnapshotOperationType.Create);
    }

    /// <summary>
    /// Starts tracking a read operation.
    /// </summary>
    /// <returns>A disposable activity for tracking timing</returns>
    public IDisposable StartReadActivity()
    {
        return new SnapshotMetricsActivity(this, SnapshotOperationType.Read);
    }

    /// <summary>
    /// Starts tracking a query operation.
    /// </summary>
    /// <returns>A disposable activity for tracking timing</returns>
    public IDisposable StartQueryActivity()
    {
        return new SnapshotMetricsActivity(this, SnapshotOperationType.Query);
    }

    /// <summary>
    /// Starts tracking a delete operation.
    /// </summary>
    /// <returns>A disposable activity for tracking timing</returns>
    public IDisposable StartDeleteActivity()
    {
        return new SnapshotMetricsActivity(this, SnapshotOperationType.Delete);
    }

    /// <summary>
    /// Starts tracking an optimize operation.
    /// </summary>
    /// <returns>A disposable activity for tracking timing</returns>
    public IDisposable StartOptimizeActivity()
    {
        return new SnapshotMetricsActivity(this, SnapshotOperationType.Optimize);
    }

    /// <summary>
    /// Records a successful create operation.
    /// </summary>
    /// <param name="snapshotsCount">Number of snapshots created</param>
    public void RecordCreateSuccess(int snapshotsCount = 1)
    {
        lock (_lock)
        {
            _createOperations += snapshotsCount;
        }
    }

    /// <summary>
    /// Records a failed create operation.
    /// </summary>
    public void RecordCreateFailure()
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
    /// Records a read operation that found no results.
    /// </summary>
    public void RecordReadNotFound()
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
    /// Records a successful delete operation.
    /// </summary>
    /// <param name="snapshotsCount">Number of snapshots deleted</param>
    public void RecordDeleteSuccess(int snapshotsCount = 1)
    {
        lock (_lock)
        {
            _deleteOperations += snapshotsCount;
        }
    }

    /// <summary>
    /// Records a failed delete operation.
    /// </summary>
    public void RecordDeleteFailure()
    {
        lock (_lock)
        {
            _failedOperations++;
        }
    }

    /// <summary>
    /// Records a successful optimize operation.
    /// </summary>
    public void RecordOptimizeSuccess()
    {
        lock (_lock)
        {
            _optimizeOperations++;
        }
    }

    /// <summary>
    /// Records a failed optimize operation.
    /// </summary>
    public void RecordOptimizeFailure()
    {
        lock (_lock)
        {
            _failedOperations++;
        }
    }

    /// <summary>
    /// Gets the current metrics snapshot.
    /// </summary>
    /// <returns>A snapshot of current metrics</returns>
    public SnapshotMetrics GetCurrentMetrics()
    {
        lock (_lock)
        {
            return new SnapshotMetrics
            {
                CreateOperations = _createOperations,
                ReadOperations = _readOperations,
                QueryOperations = _queryOperations,
                DeleteOperations = _deleteOperations,
                FailedOperations = _failedOperations,
                AverageCreateTimeMs = _createOperations > 0 ? _totalCreateTimeMs / _createOperations : 0,
                AverageReadTimeMs = _readOperations > 0 ? _totalReadTimeMs / _readOperations : 0,
                AverageQueryTimeMs = _queryOperations > 0 ? _totalQueryTimeMs / _queryOperations : 0,
                AverageDeleteTimeMs = _deleteOperations > 0 ? _totalDeleteTimeMs / _deleteOperations : 0
            };
        }
    }

    /// <summary>
    /// Resets all metrics counters.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _createOperations = 0;
            _readOperations = 0;
            _queryOperations = 0;
            _deleteOperations = 0;
            _optimizeOperations = 0;
            _failedOperations = 0;
            _totalCreateTimeMs = 0;
            _totalReadTimeMs = 0;
            _totalQueryTimeMs = 0;
            _totalDeleteTimeMs = 0;
            _totalOptimizeTimeMs = 0;
        }
    }

    /// <summary>
    /// Records timing for an operation.
    /// </summary>
    /// <param name="operationType">The type of operation</param>
    /// <param name="durationMs">The duration in milliseconds</param>
    internal void RecordTiming(SnapshotOperationType operationType, double durationMs)
    {
        lock (_lock)
        {
            switch (operationType)
            {
                case SnapshotOperationType.Create:
                    _totalCreateTimeMs += durationMs;
                    break;
                case SnapshotOperationType.Read:
                    _totalReadTimeMs += durationMs;
                    break;
                case SnapshotOperationType.Query:
                    _totalQueryTimeMs += durationMs;
                    break;
                case SnapshotOperationType.Delete:
                    _totalDeleteTimeMs += durationMs;
                    break;
                case SnapshotOperationType.Optimize:
                    _totalOptimizeTimeMs += durationMs;
                    break;
            }
        }
    }

    /// <summary>
    /// Disposable activity for tracking operation timing.
    /// </summary>
    private sealed class SnapshotMetricsActivity : IDisposable
    {
        private readonly SnapshotMetricsCollector _collector;
        private readonly SnapshotOperationType _operationType;
        private readonly Stopwatch _stopwatch;
        private bool _disposed;

        public SnapshotMetricsActivity(SnapshotMetricsCollector collector, SnapshotOperationType operationType)
        {
            _collector = collector;
            _operationType = operationType;
            _stopwatch = Stopwatch.StartNew();
        }

        /// <summary>
        /// Gets the duration of the operation so far.
        /// </summary>
        public TimeSpan Duration => _stopwatch.Elapsed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _stopwatch.Stop();
                _collector.RecordTiming(_operationType, _stopwatch.Elapsed.TotalMilliseconds);
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Types of snapshot operations for metrics tracking.
/// </summary>
internal enum SnapshotOperationType
{
    /// <summary>
    /// Snapshot creation operation.
    /// </summary>
    Create,

    /// <summary>
    /// Snapshot read operation.
    /// </summary>
    Read,

    /// <summary>
    /// Snapshot query operation.
    /// </summary>
    Query,

    /// <summary>
    /// Snapshot delete operation.
    /// </summary>
    Delete,

    /// <summary>
    /// Snapshot store optimization operation.
    /// </summary>
    Optimize
}