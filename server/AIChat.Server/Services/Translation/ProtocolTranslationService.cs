using System.Diagnostics;
using AIChat.Server.Services.Translation.Translators;

namespace AIChat.Server.Services.Translation;

/// <summary>
/// Main implementation of the protocol translation service.
/// Orchestrates multiple translators to provide seamless conversion between different protocol formats.
/// Focuses exclusively on translation orchestration, delegating specialized concerns to dedicated services.
/// </summary>
public sealed class ProtocolTranslationService : IProtocolTranslationService, IDisposable
{
    private readonly ITranslatorRegistry _translatorRegistry;
    private readonly ITranslationMetricsCollector _metricsCollector;
    private readonly ITranslationCache _cache;
    private readonly TranslationOptions _options;
    private readonly ILogger<ProtocolTranslationService> _logger;
    private readonly ActivitySource _activitySource;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the ProtocolTranslationService.
    /// </summary>
    /// <param name="translatorRegistry">Registry for discovering and managing translators</param>
    /// <param name="metricsCollector">Service for collecting translation metrics</param>
    /// <param name="cache">Translation cache</param>
    /// <param name="options">Translation options</param>
    /// <param name="logger">Logger instance</param>
    public ProtocolTranslationService(
        ITranslatorRegistry translatorRegistry,
        ITranslationMetricsCollector metricsCollector,
        ITranslationCache cache,
        TranslationOptions options,
        ILogger<ProtocolTranslationService> logger)
    {
        _translatorRegistry = translatorRegistry ?? throw new ArgumentNullException(nameof(translatorRegistry));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _activitySource = new ActivitySource("AIChat.Server.ProtocolTranslation");

        _logger.LogInformation("ProtocolTranslationService initialized with caching: {CachingEnabled}", _options.EnableCaching);
    }

    /// <inheritdoc />
    public async Task<TranslationResult<TTarget>> TranslateAsync<TSource, TTarget>(
        TSource source,
        TranslationContext context,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var activity = _activitySource.StartActivity("TranslateAsync");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate inputs
            var validationResult = ValidateTranslationRequest<TSource, TTarget>(source, context, activity);
            if (validationResult != null)
            {
                return validationResult;
            }

            // Check cache first
            var cachedResult = await TryGetCachedResultAsync<TSource, TTarget>(source, context, cancellationToken);
            if (cachedResult != null)
            {
                _ = (activity?.SetTag("cache.hit", true));
                return cachedResult;
            }
            _ = (activity?.SetTag("cache.hit", false));

            // Get and validate translator
            var translator = await GetValidatedTranslatorAsync<TSource, TTarget>(source);
            if (translator == null)
            {
                var errorMessage = $"No suitable translator found for {typeof(TSource).Name} to {typeof(TTarget).Name}";
                var result = TranslationResult.Failure<TTarget>(errorMessage, "NO_TRANSLATOR");
                await RecordTranslationAsync(result, stopwatch.Elapsed, context, null);
                return result;
            }

            // Perform translation
            var translationResult = await ExecuteTranslationAsync(translator, source, context, cancellationToken);

            // Record metrics and cache if successful
            await PostProcessTranslationAsync(translationResult, stopwatch.Elapsed, context, translator, source, cancellationToken);

            return translationResult;
        }
        catch (OperationCanceledException)
        {
            return await HandleCancellationAsync<TTarget>(stopwatch.Elapsed, context);
        }
        catch (Exception ex)
        {
            return await HandleTranslationExceptionAsync<TTarget>(ex, stopwatch.Elapsed, context, activity);
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TranslationResult<TTarget>>> TranslateBatchAsync<TSource, TTarget>(
        IEnumerable<TSource> sources,
        TranslationContext context,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_options.EnableBatchTranslation)
        {
            return await ProcessIndividualTranslationsAsync<TSource, TTarget>(sources, context, cancellationToken);
        }

        var sourceList = sources.ToList();
        ValidateBatchSize(sourceList.Count);

        using var activity = _activitySource.StartActivity("TranslateBatchAsync");
        SetBatchActivityTags(activity, sourceList.Count, typeof(TSource).Name, typeof(TTarget).Name);

        _logger.LogInformation(
            "Starting batch translation of {Count} items from {SourceType} to {TargetType}",
            sourceList.Count,
            typeof(TSource).Name,
            typeof(TTarget).Name
        );

        var results = await ProcessBatchTranslationsAsync<TSource, TTarget>(sourceList, context, cancellationToken);

        LogBatchResults(results, activity);
        return results;
    }

    /// <inheritdoc />
    public bool CanTranslate<TSource, TTarget>()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _translatorRegistry.CanTranslate<TSource, TTarget>();
    }

    /// <inheritdoc />
    public bool CanTranslate(Type sourceType, Type targetType)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _translatorRegistry.CanTranslate(sourceType, targetType);
    }

    /// <inheritdoc />
    public IEnumerable<(Type SourceType, Type TargetType)> GetSupportedTranslations()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _translatorRegistry.GetSupportedTranslations();
    }

    /// <inheritdoc />
    public IEnumerable<string> GetRegisteredTranslators()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _translatorRegistry.GetRegisteredTranslators();
    }

    /// <inheritdoc />
    public async Task<TranslationMetrics> GetMetricsAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return await _metricsCollector.GetGlobalMetricsAsync();
    }

    /// <inheritdoc />
    public async Task<TranslationMetrics?> GetTranslatorMetricsAsync(string translatorName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return await _metricsCollector.GetTranslatorMetricsAsync(translatorName);
    }

    /// <inheritdoc />
    public async Task ResetMetricsAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _metricsCollector.ResetMetricsAsync();
        _logger.LogInformation("All translation metrics have been reset");
    }

    /// <inheritdoc />
    public async Task<TranslationHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var translatorResults = await BuildTranslatorHealthResultsAsync();
            var overallHealthy = DetermineOverallHealth(translatorResults);

            stopwatch.Stop();

            var result = overallHealthy
                ? TranslationHealthCheckResult.Healthy("All translators are healthy", stopwatch.Elapsed)
                : TranslationHealthCheckResult.Degraded("Some translators are experiencing issues", stopwatch.Elapsed);

            result.TranslatorResults = translatorResults;
            result.Data["TotalTranslators"] = translatorResults.Count;
            result.Data["HealthyTranslators"] = translatorResults.Count(kvp => kvp.Value.Status == HealthStatus.Healthy);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Health check failed");
            return TranslationHealthCheckResult.Unhealthy($"Health check failed: {ex.Message}", stopwatch.Elapsed);
        }
    }

    // Helper methods for focused orchestration

    private TranslationResult<TTarget>? ValidateTranslationRequest<TSource, TTarget>(TSource source, TranslationContext context, Activity? activity)
    {
        if (EqualityComparer<TSource?>.Default.Equals(source, default))
        {
            return TranslationResult.Failure<TTarget>("Source cannot be null", "NULL_SOURCE");
        }

        if (context == null)
        {
            return TranslationResult.Failure<TTarget>("Translation context cannot be null", "NULL_CONTEXT");
        }

        _ = (activity?.SetTag("source.type", typeof(TSource).Name));
        _ = (activity?.SetTag("target.type", typeof(TTarget).Name));
        _ = (activity?.SetTag("correlation.id", context.CorrelationId));

        _logger.LogDebug(
            "Starting translation from {SourceType} to {TargetType}, CorrelationId: {CorrelationId}",
            typeof(TSource).Name,
            typeof(TTarget).Name,
            context.CorrelationId
        );

        return null; // No validation errors
    }

    private async Task<TranslationResult<TTarget>?> TryGetCachedResultAsync<TSource, TTarget>(TSource source, TranslationContext context, CancellationToken cancellationToken)
    {
        if (!_options.EnableCaching)
        {
            return null;
        }

        var cacheKey = GenerateCacheKey<TSource, TTarget>(source, context);
        var cachedResult = await _cache.GetAsync<TranslationResult<TTarget>>(cacheKey, cancellationToken);

        if (cachedResult != null)
        {
            _logger.LogDebug("Translation result found in cache for key: {CacheKey}", cacheKey);
        }

        return cachedResult;
    }

    private async Task<IMessageTranslator<TSource, TTarget>?> GetValidatedTranslatorAsync<TSource, TTarget>(TSource source)
    {
        var translator = _translatorRegistry.GetTranslator<TSource, TTarget>();

        if (translator == null)
        {
            return null;
        }

        if (!translator.CanTranslate(source))
        {
            _logger.LogWarning("Translator {TranslatorName} cannot handle the provided source", translator.TranslatorName);
            return null;
        }

        return await Task.FromResult(translator);
    }

    private async Task<TranslationResult<TTarget>> ExecuteTranslationAsync<TSource, TTarget>(
        IMessageTranslator<TSource, TTarget> translator,
        TSource source,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.TimeoutMs);

        return await translator.TranslateAsync(source, context, timeoutCts.Token);
    }

    private async Task PostProcessTranslationAsync<TSource, TTarget>(
        TranslationResult<TTarget> result,
        TimeSpan elapsed,
        TranslationContext context,
        IMessageTranslator<TSource, TTarget> translator,
        TSource source,
        CancellationToken cancellationToken)
    {
        result.Duration = elapsed;

        // Record metrics
        await RecordTranslationAsync(result, elapsed, context, translator.TranslatorName);

        // Cache successful results
        if (result.Success && _options.EnableCaching && !EqualityComparer<TTarget?>.Default.Equals(result.Data, default))
        {
            var cacheKey = GenerateCacheKey<TSource, TTarget>(source, context);
            var cacheExpiration = TimeSpan.FromMinutes(_options.CacheExpirationMinutes);
            await _cache.SetAsync(cacheKey, result, cacheExpiration, cancellationToken);
        }

        _logger.LogInformation(
            "Translation completed: {SourceType} → {TargetType}, Success: {Success}, Duration: {Duration}ms",
            typeof(TSource).Name,
            typeof(TTarget).Name,
            result.Success,
            result.Duration.TotalMilliseconds
        );
    }

    private async Task<TranslationResult<TTarget>> HandleCancellationAsync<TTarget>(TimeSpan elapsed, TranslationContext context)
    {
        const string errorMessage = "Translation operation was cancelled or timed out";
        _logger.LogWarning("Translation operation was cancelled or timed out");
        var result = TranslationResult.Failure<TTarget>(errorMessage, "TIMEOUT", elapsed);
        await RecordTranslationAsync(result, elapsed, context, null);
        return result;
    }

    private async Task<TranslationResult<TTarget>> HandleTranslationExceptionAsync<TTarget>(
        Exception ex,
        TimeSpan elapsed,
        TranslationContext context,
        Activity? activity)
    {
        var errorMessage = $"Translation failed with exception: {ex.Message}";
        _logger.LogError(ex, "Translation failed with exception: {ExceptionMessage}", ex.Message);
        _ = (activity?.SetTag("error.type", ex.GetType().Name));
        var result = TranslationResult.Failure<TTarget>(errorMessage, "EXCEPTION", elapsed);
        await RecordTranslationAsync(result, elapsed, context, null);
        return result;
    }

    private async Task RecordTranslationAsync<T>(TranslationResult<T> result, TimeSpan duration, TranslationContext context, string? translatorName)
    {
        _metricsCollector.RecordTranslation(result.Success, duration, context, translatorName);
        await Task.CompletedTask;
    }

    // Batch processing helper methods

    private async Task<IEnumerable<TranslationResult<TTarget>>> ProcessIndividualTranslationsAsync<TSource, TTarget>(
        IEnumerable<TSource> sources,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        var individualResults = new List<TranslationResult<TTarget>>();
        foreach (var source in sources)
        {
            var result = await TranslateAsync<TSource, TTarget>(source, context, cancellationToken);
            individualResults.Add(result);
        }
        return individualResults;
    }

    private void ValidateBatchSize(int batchSize)
    {
        if (batchSize > _options.MaxBatchSize)
        {
            throw new InvalidOperationException($"Batch size {batchSize} exceeds maximum allowed size {_options.MaxBatchSize}");
        }
    }

    private static void SetBatchActivityTags(Activity? activity, int batchSize, string sourceTypeName, string targetTypeName)
    {
        _ = (activity?.SetTag("batch.size", batchSize));
        _ = (activity?.SetTag("source.type", sourceTypeName));
        _ = (activity?.SetTag("target.type", targetTypeName));
    }

    private async Task<TranslationResult<TTarget>[]> ProcessBatchTranslationsAsync<TSource, TTarget>(
        List<TSource> sourceList,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        var tasks = sourceList.Select(async source =>
        {
            var individualContext = CreateIndividualContext(context);
            return await TranslateAsync<TSource, TTarget>(source, individualContext, cancellationToken);
        });

        return await Task.WhenAll(tasks);
    }

    private static void LogBatchResults<TTarget>(IEnumerable<TranslationResult<TTarget>> results, Activity? activity)
    {
        var resultArray = results.ToArray();
        var successCount = resultArray.Count(r => r.Success);

        _ = (activity?.SetTag("batch.success_count", successCount));
        _ = (activity?.SetTag("batch.total_count", resultArray.Length));
    }

    // Health check helper methods

    private async Task<Dictionary<string, TranslatorHealthResult>> BuildTranslatorHealthResultsAsync()
    {
        var translatorResults = new Dictionary<string, TranslatorHealthResult>();
        foreach (var translatorName in _translatorRegistry.GetRegisteredTranslators())
        {
            var metrics = await _metricsCollector.GetTranslatorMetricsAsync(translatorName);
            translatorResults[translatorName] = CreateTranslatorHealthResult(metrics);
        }

        return translatorResults;
    }

    private static TranslatorHealthResult CreateTranslatorHealthResult(TranslationMetrics? metrics)
    {
        var healthResult = new TranslatorHealthResult
        {
            Status = HealthStatus.Healthy,
            Description = "Translator is functioning normally"
        };

        if (metrics != null)
        {
            // Check success rate
            if (metrics.SuccessRate < 95 && metrics.TotalTranslations > 10)
            {
                healthResult.Status = HealthStatus.Degraded;
                healthResult.Description = $"Low success rate: {metrics.SuccessRate:F1}%";
            }

            // Check average response time
            if (metrics.AverageTranslationTimeMs > 1000)
            {
                healthResult.Status = HealthStatus.Degraded;
                healthResult.Description = $"High average response time: {metrics.AverageTranslationTimeMs:F1}ms";
            }

            healthResult.RecentErrorCount = (int)metrics.FailedTranslations;
            healthResult.Data["TotalTranslations"] = metrics.TotalTranslations;
            healthResult.Data["SuccessRate"] = metrics.SuccessRate;
            healthResult.Data["AverageTimeMs"] = metrics.AverageTranslationTimeMs;
        }

        return healthResult;
    }

    private static bool DetermineOverallHealth(Dictionary<string, TranslatorHealthResult> translatorResults)
    {
        return translatorResults.Values.All(result => result.Status == HealthStatus.Healthy);
    }

    // Utility methods

    private static string GenerateCacheKey<TSource, TTarget>(TSource source, TranslationContext context)
    {
        var sourceHash = source?.GetHashCode() ?? 0;
        var contextHash = $"{context.UserId}_{context.SessionId}_{context.ConnectionId}".GetHashCode();
        return $"{typeof(TSource).Name}_{typeof(TTarget).Name}_{sourceHash}_{contextHash}";
    }

    private static TranslationContext CreateIndividualContext(TranslationContext baseContext)
    {
        return new TranslationContext
        {
            UserId = baseContext.UserId,
            SessionId = baseContext.SessionId,
            ConnectionId = baseContext.ConnectionId,
            SourceProtocol = baseContext.SourceProtocol,
            TargetProtocol = baseContext.TargetProtocol,
            CorrelationId = Guid.NewGuid().ToString(), // Individual correlation ID
            Timestamp = DateTime.UtcNow,
            Properties = new Dictionary<string, object>(baseContext.Properties)
        };
    }

    // IDisposable implementation

    /// <summary>
    /// Disposes the resources used by the ProtocolTranslationService.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _activitySource?.Dispose();
        _disposed = true;

        _logger.LogInformation("ProtocolTranslationService disposed");
    }
}
