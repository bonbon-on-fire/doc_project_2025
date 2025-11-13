using System.Collections.Concurrent;

namespace AIChat.Server.Services.Translation;

/// <summary>
/// Service for collecting and managing translation metrics in a thread-safe manner.
/// Provides centralized metrics collection with optimized performance under concurrent access.
/// </summary>
public interface ITranslationMetricsCollector
{
    /// <summary>
    /// Records a translation operation result.
    /// This method is thread-safe and optimized for high-throughput scenarios.
    /// </summary>
    /// <param name="success">Whether the translation was successful</param>
    /// <param name="duration">Time taken for the translation</param>
    /// <param name="context">Translation context</param>
    /// <param name="translatorName">Name of the translator used</param>
    void RecordTranslation(bool success, TimeSpan duration, TranslationContext context, string? translatorName = null);

    /// <summary>
    /// Gets aggregated metrics across all translators.
    /// </summary>
    /// <returns>Global translation metrics</returns>
    Task<TranslationMetrics> GetGlobalMetricsAsync();

    /// <summary>
    /// Gets metrics for a specific translator.
    /// </summary>
    /// <param name="translatorName">Name of the translator</param>
    /// <returns>Translator-specific metrics or null if not found</returns>
    Task<TranslationMetrics?> GetTranslatorMetricsAsync(string translatorName);

    /// <summary>
    /// Gets metrics for all translators.
    /// </summary>
    /// <returns>Dictionary of translator names to their metrics</returns>
    Task<IReadOnlyDictionary<string, TranslationMetrics>> GetAllTranslatorMetricsAsync();

    /// <summary>
    /// Resets all metrics to zero.
    /// </summary>
    /// <returns>Task representing the async operation</returns>
    Task ResetMetricsAsync();

    /// <summary>
    /// Resets metrics for a specific translator.
    /// </summary>
    /// <param name="translatorName">Name of the translator</param>
    /// <returns>Task representing the async operation</returns>
    Task ResetTranslatorMetricsAsync(string translatorName);

    /// <summary>
    /// Gets collector statistics for monitoring purposes.
    /// </summary>
    /// <returns>Collector statistics</returns>
    MetricsCollectorStatistics GetCollectorStatistics();
}

/// <summary>
/// Thread-safe implementation of translation metrics collection.
/// Uses atomic operations and concurrent collections for optimal performance under load.
/// </summary>
public sealed class TranslationMetricsCollector : ITranslationMetricsCollector
{
    private readonly ILogger<TranslationMetricsCollector> _logger;

    /// <summary>
    /// Global metrics using thread-safe operations
    /// </summary>
    private long _globalTotalTranslations;
    private long _globalSuccessfulTranslations;
    private long _globalFailedTranslations;
    private long _globalTotalDurationTicks;
    private long _globalMinDurationTicks = long.MaxValue;
    private long _globalMaxDurationTicks;

    /// <summary>
    /// Per-translator metrics
    /// </summary>
    private readonly ConcurrentDictionary<string, TranslatorMetricsData> _translatorMetrics = new();

    /// <summary>
    /// Protocol breakdown
    /// </summary>
    private readonly ConcurrentDictionary<string, long> _translationsBySourceProtocol = new();
    private readonly ConcurrentDictionary<string, long> _translationsByTargetProtocol = new();

    /// <summary>
    /// Collector statistics
    /// </summary>
    private readonly DateTime _createdAt = DateTime.UtcNow;
    private DateTime _lastResetTime = DateTime.UtcNow;

    /// <summary>
    /// Initializes a new instance of the TranslationMetricsCollector.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public TranslationMetricsCollector(ILogger<TranslationMetricsCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("TranslationMetricsCollector initialized with thread-safe operations");
    }

    /// <inheritdoc />
    public void RecordTranslation(bool success, TimeSpan duration, TranslationContext context, string? translatorName = null)
    {
        var durationTicks = duration.Ticks;

        // Update global metrics atomically
        Interlocked.Increment(ref _globalTotalTranslations);

        if (success)
        {
            Interlocked.Increment(ref _globalSuccessfulTranslations);
        }
        else
        {
            Interlocked.Increment(ref _globalFailedTranslations);
        }

        // Update duration tracking
        Interlocked.Add(ref _globalTotalDurationTicks, durationTicks);

        // Update min duration (thread-safe)
        UpdateMinDuration(durationTicks);

        // Update max duration (thread-safe)
        UpdateMaxDuration(durationTicks);

        // Update per-translator metrics if translator name is provided
        if (!string.IsNullOrEmpty(translatorName))
        {
            UpdateTranslatorMetrics(translatorName, success, duration);
        }

        // Update protocol metrics
        UpdateProtocolMetrics(context);

        _logger.LogTrace(
            "Recorded translation: Success={Success}, Duration={Duration}ms, Translator={TranslatorName}",
            success,
            duration.TotalMilliseconds,
            translatorName
        );
    }

    /// <inheritdoc />
    public async Task<TranslationMetrics> GetGlobalMetricsAsync()
    {
        var totalTranslations = Interlocked.Read(ref _globalTotalTranslations);
        var successfulTranslations = Interlocked.Read(ref _globalSuccessfulTranslations);
        var failedTranslations = Interlocked.Read(ref _globalFailedTranslations);
        var totalDurationTicks = Interlocked.Read(ref _globalTotalDurationTicks);
        var minDurationTicks = Interlocked.Read(ref _globalMinDurationTicks);
        var maxDurationTicks = Interlocked.Read(ref _globalMaxDurationTicks);

        var metrics = new TranslationMetrics
        {
            TotalTranslations = totalTranslations,
            SuccessfulTranslations = successfulTranslations,
            FailedTranslations = failedTranslations,
            MinTranslationTimeMs = minDurationTicks == long.MaxValue ? 0 : TimeSpan.FromTicks(minDurationTicks).TotalMilliseconds,
            MaxTranslationTimeMs = TimeSpan.FromTicks(maxDurationTicks).TotalMilliseconds,
            AverageTranslationTimeMs = totalTranslations > 0
                ? TimeSpan.FromTicks(totalDurationTicks / totalTranslations).TotalMilliseconds
                : 0,
            TranslationsBySourceProtocol = new Dictionary<string, long>(_translationsBySourceProtocol),
            TranslationsByTargetProtocol = new Dictionary<string, long>(_translationsByTargetProtocol),
            LastResetTime = _lastResetTime
        };

        return await Task.FromResult(metrics);
    }

    /// <inheritdoc />
    public async Task<TranslationMetrics?> GetTranslatorMetricsAsync(string translatorName)
    {
        if (string.IsNullOrEmpty(translatorName))
        {
            return null;
        }

        if (!_translatorMetrics.TryGetValue(translatorName, out var metricsData))
        {
            return null;
        }

        return await metricsData.ToTranslationMetricsAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, TranslationMetrics>> GetAllTranslatorMetricsAsync()
    {
        var result = new Dictionary<string, TranslationMetrics>();

        foreach (var kvp in _translatorMetrics)
        {
            result[kvp.Key] = await kvp.Value.ToTranslationMetricsAsync();
        }

        return result.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task ResetMetricsAsync()
    {
        _logger.LogInformation("Resetting all translation metrics");

        // Reset global metrics
        Interlocked.Exchange(ref _globalTotalTranslations, 0);
        Interlocked.Exchange(ref _globalSuccessfulTranslations, 0);
        Interlocked.Exchange(ref _globalFailedTranslations, 0);
        Interlocked.Exchange(ref _globalTotalDurationTicks, 0);
        Interlocked.Exchange(ref _globalMinDurationTicks, long.MaxValue);
        Interlocked.Exchange(ref _globalMaxDurationTicks, 0);

        // Clear collections
        _translatorMetrics.Clear();
        _translationsBySourceProtocol.Clear();
        _translationsByTargetProtocol.Clear();

        _lastResetTime = DateTime.UtcNow;

        _logger.LogInformation("All translation metrics reset at {ResetTime}", _lastResetTime);
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task ResetTranslatorMetricsAsync(string translatorName)
    {
        if (string.IsNullOrEmpty(translatorName))
        {
            return;
        }

        if (_translatorMetrics.TryRemove(translatorName, out _))
        {
            _logger.LogInformation("Reset metrics for translator: {TranslatorName}", translatorName);
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public MetricsCollectorStatistics GetCollectorStatistics()
    {
        return new MetricsCollectorStatistics
        {
            CreatedAt = _createdAt,
            LastResetTime = _lastResetTime,
            TrackedTranslatorCount = _translatorMetrics.Count,
            TrackedSourceProtocolCount = _translationsBySourceProtocol.Count,
            TrackedTargetProtocolCount = _translationsByTargetProtocol.Count,
            TotalRecordedTranslations = Interlocked.Read(ref _globalTotalTranslations)
        };
    }

    private void UpdateTranslatorMetrics(string translatorName, bool success, TimeSpan duration)
    {
        var metricsData = _translatorMetrics.GetOrAdd(translatorName, _ => new TranslatorMetricsData());
        metricsData.RecordTranslation(success, duration);
    }

    private void UpdateProtocolMetrics(TranslationContext context)
    {
        if (!string.IsNullOrEmpty(context.SourceProtocol))
        {
            _translationsBySourceProtocol.AddOrUpdate(
                context.SourceProtocol,
                1,
                (key, value) => value + 1
            );
        }

        if (!string.IsNullOrEmpty(context.TargetProtocol))
        {
            _translationsByTargetProtocol.AddOrUpdate(
                context.TargetProtocol,
                1,
                (key, value) => value + 1
            );
        }
    }

    private void UpdateMinDuration(long durationTicks)
    {
        long currentMin;
        do
        {
            currentMin = Interlocked.Read(ref _globalMinDurationTicks);
            if (durationTicks >= currentMin)
            {
                break; // Current value is already smaller
            }
        }
        while (Interlocked.CompareExchange(ref _globalMinDurationTicks, durationTicks, currentMin) != currentMin);
    }

    private void UpdateMaxDuration(long durationTicks)
    {
        long currentMax;
        do
        {
            currentMax = Interlocked.Read(ref _globalMaxDurationTicks);
            if (durationTicks <= currentMax)
            {
                break; // Current value is already larger
            }
        }
        while (Interlocked.CompareExchange(ref _globalMaxDurationTicks, durationTicks, currentMax) != currentMax);
    }
}

/// <summary>
/// Thread-safe metrics data for individual translators.
/// </summary>
internal sealed class TranslatorMetricsData
{
    private long _totalTranslations;
    private long _successfulTranslations;
    private long _failedTranslations;
    private long _totalDurationTicks;
    private long _minDurationTicks = long.MaxValue;
    private long _maxDurationTicks;
    private readonly DateTime _createdAt = DateTime.UtcNow;

    public void RecordTranslation(bool success, TimeSpan duration)
    {
        var durationTicks = duration.Ticks;

        Interlocked.Increment(ref _totalTranslations);

        if (success)
        {
            Interlocked.Increment(ref _successfulTranslations);
        }
        else
        {
            Interlocked.Increment(ref _failedTranslations);
        }

        Interlocked.Add(ref _totalDurationTicks, durationTicks);

        // Update min duration
        long currentMin;
        do
        {
            currentMin = Interlocked.Read(ref _minDurationTicks);
            if (durationTicks >= currentMin)
            {
                break;
            }
        }
        while (Interlocked.CompareExchange(ref _minDurationTicks, durationTicks, currentMin) != currentMin);

        // Update max duration
        long currentMax;
        do
        {
            currentMax = Interlocked.Read(ref _maxDurationTicks);
            if (durationTicks <= currentMax)
            {
                break;
            }
        }
        while (Interlocked.CompareExchange(ref _maxDurationTicks, durationTicks, currentMax) != currentMax);
    }

    public async Task<TranslationMetrics> ToTranslationMetricsAsync()
    {
        var totalTranslations = Interlocked.Read(ref _totalTranslations);
        var successfulTranslations = Interlocked.Read(ref _successfulTranslations);
        var failedTranslations = Interlocked.Read(ref _failedTranslations);
        var totalDurationTicks = Interlocked.Read(ref _totalDurationTicks);
        var minDurationTicks = Interlocked.Read(ref _minDurationTicks);
        var maxDurationTicks = Interlocked.Read(ref _maxDurationTicks);

        var metrics = new TranslationMetrics
        {
            TotalTranslations = totalTranslations,
            SuccessfulTranslations = successfulTranslations,
            FailedTranslations = failedTranslations,
            MinTranslationTimeMs = minDurationTicks == long.MaxValue ? 0 : TimeSpan.FromTicks(minDurationTicks).TotalMilliseconds,
            MaxTranslationTimeMs = TimeSpan.FromTicks(maxDurationTicks).TotalMilliseconds,
            AverageTranslationTimeMs = totalTranslations > 0
                ? TimeSpan.FromTicks(totalDurationTicks / totalTranslations).TotalMilliseconds
                : 0,
            LastResetTime = _createdAt
        };

        return await Task.FromResult(metrics);
    }
}

/// <summary>
/// Statistics for the metrics collector itself.
/// </summary>
public sealed class MetricsCollectorStatistics
{
    /// <summary>
    /// When the collector was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When metrics were last reset.
    /// </summary>
    public DateTime LastResetTime { get; set; }

    /// <summary>
    /// Number of translators being tracked.
    /// </summary>
    public int TrackedTranslatorCount { get; set; }

    /// <summary>
    /// Number of source protocols being tracked.
    /// </summary>
    public int TrackedSourceProtocolCount { get; set; }

    /// <summary>
    /// Number of target protocols being tracked.
    /// </summary>
    public int TrackedTargetProtocolCount { get; set; }

    /// <summary>
    /// Total number of translation operations recorded.
    /// </summary>
    public long TotalRecordedTranslations { get; set; }
}
