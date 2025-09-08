using System.Diagnostics;
using AIChat.Server.Services.Streaming;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AIChat.Server.HealthChecks;

/// <summary>
/// Health check implementation for the resilient streaming system.
/// Monitors stream health, circuit breaker states, and recovery metrics.
/// </summary>
public class ResilientStreamingHealthCheck : IHealthCheck
{
    private readonly IResilientStreamManager _streamManager;
    private readonly ILogger<ResilientStreamingHealthCheck> _logger;
    private readonly int _unhealthyThresholdActiveStreams;
    private readonly int _unhealthyThresholdOpenCircuits;
    private readonly double _unhealthyThresholdSuccessRate;

    /// <summary>
    /// Initializes a new instance of the ResilientStreamingHealthCheck class.
    /// </summary>
    /// <param name="streamManager">The resilient stream manager to check</param>
    /// <param name="logger">Logger for diagnostics</param>
    public ResilientStreamingHealthCheck(
        IResilientStreamManager streamManager,
        ILogger<ResilientStreamingHealthCheck> logger)
    {
        _streamManager = streamManager ?? throw new ArgumentNullException(nameof(streamManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Configurable thresholds - could be moved to configuration
        _unhealthyThresholdActiveStreams = 100; // Unhealthy if more than 100 active streams
        _unhealthyThresholdOpenCircuits = 5; // Unhealthy if more than 5 open circuits
        _unhealthyThresholdSuccessRate = 80.0; // Unhealthy if success rate below 80%
    }

    /// <summary>
    /// Performs the health check for resilient streaming.
    /// </summary>
    /// <param name="context">Health check context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check result</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Get health status from stream manager
            var status = await _streamManager.GetHealthStatusAsync();

            stopwatch.Stop();

            // Build health check data
            var data = new Dictionary<string, object>
            {
                ["activeStreams"] = status.ActiveStreams,
                ["streamsInRecovery"] = status.StreamsInRecovery,
                ["openCircuitBreakers"] = status.OpenCircuitBreakers,
                ["totalBufferedMessages"] = status.TotalBufferedMessages,
                ["averageRecoveryTimeMs"] = status.AverageRecoveryTimeMs,
                ["successRatePercentage"] = status.SuccessRatePercentage,
                ["checkDurationMs"] = stopwatch.ElapsedMilliseconds,
                ["timestamp"] = status.Timestamp
            };

            // Add detailed metrics if available
            if (status.StatusMessages.Count != 0)
            {
                data["statusMessages"] = status.StatusMessages;
            }

            // Determine health status
            if (!status.IsHealthy)
            {
                return HealthCheckResult.Unhealthy(
                    "Resilient streaming system is unhealthy",
                    data: data);
            }

            // Check against thresholds
            var issues = new List<string>();

            if (status.ActiveStreams > _unhealthyThresholdActiveStreams)
            {
                issues.Add($"Too many active streams: {status.ActiveStreams} > {_unhealthyThresholdActiveStreams}");
            }

            if (status.OpenCircuitBreakers > _unhealthyThresholdOpenCircuits)
            {
                issues.Add($"Too many open circuit breakers: {status.OpenCircuitBreakers} > {_unhealthyThresholdOpenCircuits}");
            }

            if (status.SuccessRatePercentage < _unhealthyThresholdSuccessRate)
            {
                issues.Add($"Success rate too low: {status.SuccessRatePercentage:F1}% < {_unhealthyThresholdSuccessRate}%");
            }

            if (status.AverageRecoveryTimeMs > 5000) // 5 seconds as per requirement
            {
                issues.Add($"Recovery time too high: {status.AverageRecoveryTimeMs:F0}ms > 5000ms");
            }

            // Return appropriate health status
            if (issues.Count != 0)
            {
                var description = string.Join("; ", issues);

                return issues.Count >= 2 || status.OpenCircuitBreakers > _unhealthyThresholdOpenCircuits
                    ? HealthCheckResult.Unhealthy(description, data: data)
                    : HealthCheckResult.Degraded(description, data: data);
            }

            // Check for warnings
            var warnings = new List<string>();

            if (status.StreamsInRecovery > 0)
            {
                warnings.Add($"{status.StreamsInRecovery} streams in recovery");
            }

            if (status.TotalBufferedMessages > 500)
            {
                warnings.Add($"High number of buffered messages: {status.TotalBufferedMessages}");
            }

            return warnings.Count != 0
                ? HealthCheckResult.Healthy(
                    $"Resilient streaming is healthy with warnings: {string.Join("; ", warnings)}",
                    data: data)
                : HealthCheckResult.Healthy(
                "Resilient streaming system is healthy",
                data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing resilient streaming health check");

            return HealthCheckResult.Unhealthy(
                "Failed to check resilient streaming health",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    ["error"] = ex.Message,
                    ["type"] = ex.GetType().Name
                });
        }
    }
}

/// <summary>
/// Extension methods for registering resilient streaming health checks.
/// </summary>
public static class ResilientStreamingHealthCheckExtensions
{
    /// <summary>
    /// Adds resilient streaming health check to the health check builder.
    /// </summary>
    /// <param name="builder">The health check builder</param>
    /// <param name="name">Name of the health check</param>
    /// <param name="failureStatus">The failure status to report</param>
    /// <param name="tags">Tags for the health check</param>
    /// <param name="timeout">Timeout for the health check</param>
    /// <returns>The health check builder</returns>
    public static IHealthChecksBuilder AddResilientStreamingHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "resilient-streaming",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new ResilientStreamingHealthCheck(
                sp.GetRequiredService<IResilientStreamManager>(),
                sp.GetRequiredService<ILogger<ResilientStreamingHealthCheck>>()),
            failureStatus,
            tags,
            timeout));
    }
}
