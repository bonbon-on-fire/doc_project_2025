using System.Collections.Concurrent;
using AIChat.Server.Configuration;
using AIChat.Server.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Manages adaptive buffer sizing based on usage patterns and trends.
/// </summary>
public sealed class AdaptiveBufferManager : IAdaptiveBufferManager
{
    private readonly ILogger<AdaptiveBufferManager> _logger;
    private readonly AdaptiveBufferingConfiguration _configuration;
    private readonly ConcurrentQueue<UsageDataPoint> _usageHistory;
    private readonly ITrendAnalyzer _trendAnalyzer;
    private readonly ISystemTime _systemTime;
    private readonly Services.Abstractions.ITimer _analysisTimer;
    private readonly Lock _sizingLock = new();

    private int _currentBufferSize;
    private DateTime _lastScaleOperation = DateTime.MinValue;
    private long _totalSizeAdjustments;
    private long _scaleUpOperations;
    private long _scaleDownOperations;
    private bool _disposed;

    /// <summary>
    /// Event raised when buffer size should be adjusted.
    /// </summary>
    public event EventHandler<BufferSizeChangedEventArgs>? BufferSizeChanged;

    /// <summary>
    /// Initializes a new instance of the AdaptiveBufferManager class.
    /// </summary>
    /// <param name="logger">Logger for diagnostics</param>
    /// <param name="configuration">Streaming configuration</param>
    /// <param name="trendAnalyzer">Trend analyzer service</param>
    /// <param name="systemTime">System time abstraction</param>
    /// <param name="timerFactory">Timer factory for creating timers</param>
    /// <param name="initialSize">Initial buffer size</param>
    public AdaptiveBufferManager(
        ILogger<AdaptiveBufferManager> logger,
        IOptions<StreamingConfiguration> configuration,
        ITrendAnalyzer trendAnalyzer,
        ISystemTime systemTime,
        ITimerFactory timerFactory,
        int initialSize = StreamingConstants.Buffer.DefaultInitialSize)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value?.AdaptiveBuffering
            ?? throw new ArgumentNullException(nameof(configuration));
        _trendAnalyzer = trendAnalyzer ?? throw new ArgumentNullException(nameof(trendAnalyzer));
        _systemTime = systemTime ?? throw new ArgumentNullException(nameof(systemTime));
        ArgumentNullException.ThrowIfNull(timerFactory);

        _currentBufferSize = Math.Clamp(initialSize, _configuration.MinSize, _configuration.MaxSize);
        _usageHistory = new ConcurrentQueue<UsageDataPoint>();

        // Start analysis timer
        var analysisInterval = TimeSpan.FromSeconds(
            Math.Max(
                StreamingConstants.Timing.MinAnalysisIntervalSeconds,
                _configuration.ScaleCooldownSeconds / StreamingConstants.Timing.AnalysisIntervalDivisor));

        _analysisTimer = timerFactory.CreateTimer(AnalyzeAndAdjust, null);
        _analysisTimer.Start(analysisInterval, analysisInterval);

        _logger.LogInformation(
            "AdaptiveBufferManager initialized. Initial size: {Size}, Min: {Min}, Max: {Max}",
            _currentBufferSize,
            _configuration.MinSize,
            _configuration.MaxSize);
    }

    /// <summary>
    /// Records current buffer usage for trend analysis.
    /// </summary>
    /// <param name="currentUsage">Current number of items in buffer</param>
    /// <param name="capacity">Current buffer capacity</param>
    public void RecordUsage(int currentUsage, int capacity)
    {
        if (_disposed)
        {
            return;
        }

        var utilization = capacity > 0 ? (float)currentUsage / capacity * 100 : 0;
        var dataPoint = new UsageDataPoint
        {
            Timestamp = _systemTime.UtcNow,
            Usage = currentUsage,
            Capacity = capacity,
            Utilization = utilization
        };

        _usageHistory.Enqueue(dataPoint);

        // Keep history within window
        var cutoff = _systemTime.UtcNow.AddMinutes(-_configuration.WindowSizeMinutes);
        while (_usageHistory.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
        {
            _ = _usageHistory.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Gets the current recommended buffer size.
    /// </summary>
    public int CurrentBufferSize => _currentBufferSize;

    /// <summary>
    /// Gets statistics about buffer sizing operations.
    /// </summary>
    public AdaptiveBufferStatistics GetStatistics()
    {
        var history = _usageHistory.ToArray();
        var avgUtilization = history.Length != 0
            ? history.Average(h => h.Utilization)
            : 0;

        return new AdaptiveBufferStatistics
        {
            CurrentSize = _currentBufferSize,
            MinSize = _configuration.MinSize,
            MaxSize = _configuration.MaxSize,
            TotalAdjustments = _totalSizeAdjustments,
            ScaleUpOperations = _scaleUpOperations,
            ScaleDownOperations = _scaleDownOperations,
            AverageUtilization = avgUtilization,
            DataPointsCount = history.Length,
            LastAdjustmentTime = _lastScaleOperation
        };
    }

    private void AnalyzeAndAdjust(object? state)
    {
        if (!_configuration.Enabled || _disposed)
        {
            return;
        }

        try
        {
            var history = _usageHistory.ToArray();
            if (history.Length < StreamingConstants.Buffer.MinDataPointsForAnalysis)
            {
                return; // Need enough data points
            }

            // Check cooldown
            if (_systemTime.UtcNow - _lastScaleOperation < TimeSpan.FromSeconds(_configuration.ScaleCooldownSeconds))
            {
                return;
            }

            // Calculate trend and get recommendation
            var trend = _trendAnalyzer.CalculateTrend(history);
            var currentUtilization = history.LastOrDefault()?.Utilization ?? 0;
            var avgUtilization = history.Average(h => h.Utilization);

            var recommendation = _trendAnalyzer.GetScalingRecommendation(
                currentUtilization,
                avgUtilization,
                trend,
                _configuration.ScaleUpThreshold,
                _configuration.ScaleDownThreshold);

            lock (_sizingLock)
            {
                if (recommendation.Action == ScalingAction.None)
                {
                    return;
                }

                var newSize = _currentBufferSize;
                var scaleFactor = recommendation.AggressiveScaleFactor ?? recommendation.ScaleFactor;

                if (recommendation.Action == ScalingAction.ScaleUp)
                {
                    newSize = (int)Math.Min(
                        _currentBufferSize * scaleFactor,
                        _configuration.MaxSize);
                    _scaleUpOperations++;
                }
                else if (recommendation.Action == ScalingAction.ScaleDown)
                {
                    newSize = (int)Math.Max(
                        _currentBufferSize / scaleFactor,
                        _configuration.MinSize);
                    _scaleDownOperations++;
                }

                if (newSize != _currentBufferSize)
                {
                    var oldSize = _currentBufferSize;
                    _currentBufferSize = newSize;
                    _lastScaleOperation = _systemTime.UtcNow;
                    _totalSizeAdjustments++;

                    _logger.LogInformation(
                        "Buffer size adjusted. Current: {Current}, New: {New}, Reason: {Reason}",
                        oldSize, newSize, recommendation.Reason);

                    // Raise event
                    BufferSizeChanged?.Invoke(this, new BufferSizeChangedEventArgs
                    {
                        OldSize = oldSize,
                        NewSize = newSize,
                        Reason = recommendation.Reason,
                        AverageUtilization = avgUtilization,
                        Trend = trend
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during buffer size analysis");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _analysisTimer?.Dispose();

        var stats = GetStatistics();
        _logger.LogInformation(
            "AdaptiveBufferManager disposed. Total adjustments: {Total}, Scale up: {Up}, Scale down: {Down}",
            stats.TotalAdjustments,
            stats.ScaleUpOperations,
            stats.ScaleDownOperations);
    }
}

/// <summary>
/// Event args for buffer size change events.
/// </summary>
public sealed class BufferSizeChangedEventArgs : EventArgs
{
    public int OldSize { get; init; }
    public int NewSize { get; init; }
    public string Reason { get; init; } = string.Empty;
    public float AverageUtilization { get; init; }
    public UsageTrend Trend { get; init; }
}

/// <summary>
/// Buffer usage trend direction.
/// </summary>
public enum UsageTrend
{
    Stable,
    Increasing,
    Decreasing
}

/// <summary>
/// Statistics for adaptive buffer sizing operations.
/// </summary>
public sealed class AdaptiveBufferStatistics
{
    public int CurrentSize { get; init; }
    public int MinSize { get; init; }
    public int MaxSize { get; init; }
    public long TotalAdjustments { get; init; }
    public long ScaleUpOperations { get; init; }
    public long ScaleDownOperations { get; init; }
    public float AverageUtilization { get; init; }
    public int DataPointsCount { get; init; }
    public DateTime LastAdjustmentTime { get; init; }
}