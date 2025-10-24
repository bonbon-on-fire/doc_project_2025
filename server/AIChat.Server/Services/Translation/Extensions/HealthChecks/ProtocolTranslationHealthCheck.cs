using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AIChat.Server.Services.Translation.Extensions.HealthChecks;

/// <summary>
/// Health check implementation for the protocol translation service.
/// Validates that all translators are functioning correctly and performance metrics are within acceptable ranges.
/// </summary>
public class ProtocolTranslationHealthCheck : IHealthCheck
{
    private readonly IProtocolTranslationService _translationService;
    private readonly ILogger<ProtocolTranslationHealthCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the ProtocolTranslationHealthCheck.
    /// </summary>
    /// <param name="translationService">The protocol translation service</param>
    /// <param name="logger">Logger instance</param>
    public ProtocolTranslationHealthCheck(
        IProtocolTranslationService translationService,
        ILogger<ProtocolTranslationHealthCheck> logger)
    {
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Starting protocol translation health check");

            // Check the service health
            var serviceHealthResult = await _translationService.CheckHealthAsync(cancellationToken);

            stopwatch.Stop();

            var data = new Dictionary<string, object>
            {
                ["checkDuration"] = stopwatch.Elapsed.TotalMilliseconds,
                ["registeredTranslators"] = _translationService.GetRegisteredTranslators().ToList(),
                ["supportedTranslations"] = _translationService.GetSupportedTranslations().Select(t => $"{t.SourceType.Name} -> {t.TargetType.Name}").ToList()
            };

            // Add service health details
            data["serviceHealthStatus"] = serviceHealthResult.Status.ToString();
            data["serviceHealthDescription"] = serviceHealthResult.Description;
            data["serviceHealthDuration"] = serviceHealthResult.Duration.TotalMilliseconds;

            // Add translator-specific health data
            if (serviceHealthResult.TranslatorResults.Count != 0)
            {
                data["translatorResults"] = serviceHealthResult.TranslatorResults;
            }

            // Add metrics if available
            try
            {
                var metrics = await _translationService.GetMetricsAsync();
                data["totalTranslations"] = metrics.TotalTranslations;
                data["successfulTranslations"] = metrics.SuccessfulTranslations;
                data["failedTranslations"] = metrics.FailedTranslations;
                data["successRate"] = metrics.SuccessRate;
                data["averageTranslationTimeMs"] = metrics.AverageTranslationTimeMs;
                data["maxTranslationTimeMs"] = metrics.MaxTranslationTimeMs;
                data["minTranslationTimeMs"] = metrics.MinTranslationTimeMs;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve translation metrics for health check");
                data["metricsError"] = ex.Message;
            }

            // Determine overall health status
            var overallStatus = DetermineOverallStatus(serviceHealthResult, data);

            var description = CreateHealthDescription(overallStatus, serviceHealthResult, data);

            _logger.LogInformation(
                "Protocol translation health check completed in {Duration}ms with status: {Status}",
                stopwatch.ElapsedMilliseconds,
                overallStatus
            );

            return new HealthCheckResult(overallStatus, description, null, data);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogWarning("Protocol translation health check was cancelled after {Duration}ms", stopwatch.ElapsedMilliseconds);
            return HealthCheckResult.Unhealthy("Health check was cancelled", null, new Dictionary<string, object>
            {
                ["checkDuration"] = stopwatch.Elapsed.TotalMilliseconds,
                ["cancelled"] = true
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Protocol translation health check failed after {Duration}ms", stopwatch.ElapsedMilliseconds);
            return HealthCheckResult.Unhealthy($"Health check failed: {ex.Message}", ex, new Dictionary<string, object>
            {
                ["checkDuration"] = stopwatch.Elapsed.TotalMilliseconds,
                ["exception"] = ex.GetType().Name,
                ["errorMessage"] = ex.Message
            });
        }
    }

    private static Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus DetermineOverallStatus(TranslationHealthCheckResult serviceHealthResult, Dictionary<string, object> data)
    {
        // If the service itself reports unhealthy, we're unhealthy
        if (serviceHealthResult.Status == HealthStatus.Unhealthy)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy;
        }

        // Check critical metrics if available
        if (data.TryGetValue("successRate", out var successRateObj) && successRateObj is double successRate)
        {
            if (successRate < 90.0) // Less than 90% success rate is unhealthy
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy;
            }

            if (successRate < 95.0) // Less than 95% success rate is degraded
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded;
            }
        }

        // Check average response time
        if (data.TryGetValue("averageTranslationTimeMs", out var avgTimeObj) && avgTimeObj is double avgTime)
        {
            if (avgTime > 2000.0) // More than 2 seconds average is unhealthy
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy;
            }

            if (avgTime > 1000.0) // More than 1 second average is degraded
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded;
            }
        }

        // Check if we have registered translators
        if (data.TryGetValue("registeredTranslators", out var translatorsObj) && translatorsObj is List<string> translators && translators.Count == 0)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy;
        }

        // If service reports degraded, we're degraded
        if (serviceHealthResult.Status == HealthStatus.Degraded)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded;
        }

        // Everything looks good
        return Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy;
    }

    private static string CreateHealthDescription(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus status, TranslationHealthCheckResult serviceHealthResult, Dictionary<string, object> data)
    {
        var translatorCount = 0;
        if (data.TryGetValue("registeredTranslators", out var translatorsObj) && translatorsObj is List<string> translators)
        {
            translatorCount = translators.Count;
        }

        var baseDescription = $"Protocol translation service with {translatorCount} registered translators";

        return status switch
        {
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy => $"{baseDescription} - all systems functioning normally",
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded => $"{baseDescription} - experiencing degraded performance: {serviceHealthResult.Description}",
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy => $"{baseDescription} - service is unhealthy: {serviceHealthResult.Description}",
            _ => $"{baseDescription} - status unknown"
        };
    }
}

/// <summary>
/// Extension methods for registering the protocol translation health check.
/// </summary>
public static class ProtocolTranslationHealthCheckExtensions
{
    /// <summary>
    /// Adds the protocol translation health check to the health check builder.
    /// </summary>
    /// <param name="builder">The health checks builder</param>
    /// <param name="name">The health check name</param>
    /// <param name="failureStatus">The status to return when the health check fails</param>
    /// <param name="tags">Tags to associate with the health check</param>
    /// <returns>The health checks builder for chaining</returns>
    public static IHealthChecksBuilder AddProtocolTranslation(
        this IHealthChecksBuilder builder,
        string name = "protocol_translation",
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        return builder.AddCheck<ProtocolTranslationHealthCheck>(
            name,
            failureStatus,
            tags ?? []);
    }

    /// <summary>
    /// Adds the protocol translation health check with custom configuration.
    /// </summary>
    /// <param name="builder">The health checks builder</param>
    /// <param name="configure">Configuration delegate</param>
    /// <returns>The health checks builder for chaining</returns>
    public static IHealthChecksBuilder AddProtocolTranslationWithOptions(
        this IHealthChecksBuilder builder,
        Action<ProtocolTranslationHealthCheckOptions> configure)
    {
        var options = new ProtocolTranslationHealthCheckOptions();
        configure(options);

        return builder.AddCheck<ProtocolTranslationHealthCheck>(
            options.Name,
            options.FailureStatus,
            options.Tags,
            options.Timeout);
    }
}

/// <summary>
/// Configuration options for the protocol translation health check.
/// </summary>
public class ProtocolTranslationHealthCheckOptions
{
    /// <summary>
    /// The name of the health check.
    /// </summary>
    public string Name { get; set; } = "protocol_translation";

    /// <summary>
    /// The status to return when the health check fails.
    /// </summary>
    public Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus? FailureStatus { get; set; }

    /// <summary>
    /// Tags to associate with the health check.
    /// </summary>
    public IEnumerable<string>? Tags { get; set; }

    /// <summary>
    /// The timeout for the health check.
    /// </summary>
    public TimeSpan? Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Minimum success rate threshold for healthy status.
    /// </summary>
    public double HealthySuccessRateThreshold { get; set; } = 95.0;

    /// <summary>
    /// Minimum success rate threshold for degraded status.
    /// </summary>
    public double DegradedSuccessRateThreshold { get; set; } = 90.0;

    /// <summary>
    /// Maximum average response time in milliseconds for healthy status.
    /// </summary>
    public double HealthyAverageTimeThreshold { get; set; } = 1000.0;

    /// <summary>
    /// Maximum average response time in milliseconds for degraded status.
    /// </summary>
    public double DegradedAverageTimeThreshold { get; set; } = 2000.0;
}