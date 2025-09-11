using AIChat.Orleans.Client.Services;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using AspNetHealthCheckResult = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult;

namespace AIChat.Server.Services;

/// <summary>
/// ASP.NET Core health check implementation for Orleans infrastructure.
/// Integrates with the Orleans grain system to provide comprehensive health monitoring.
/// </summary>
public sealed class OrleansHealthCheck : IHealthCheck
{
    private readonly IOrleansIntegrationService _orleansService;
    private readonly IGrainFactory? _grainFactory;
    private readonly ILogger<OrleansHealthCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the OrleansHealthCheck.
    /// </summary>
    /// <param name="orleansService">Orleans integration service</param>
    /// <param name="grainFactory">Orleans grain factory (optional - may be null if Orleans is disabled)</param>
    /// <param name="logger">Logger instance</param>
    public OrleansHealthCheck(
        IOrleansIntegrationService orleansService,
        IGrainFactory? grainFactory,
        ILogger<OrleansHealthCheck> logger
    )
    {
        _orleansService = orleansService ?? throw new ArgumentNullException(nameof(orleansService));
        _grainFactory = grainFactory;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AspNetHealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            _logger.LogDebug("Starting Orleans health check");

            // First, check if Orleans integration is enabled via feature flags
            var isOrleansEnabled = await _orleansService.IsOrleansHealthyAsync();

            if (!isOrleansEnabled)
            {
                _logger.LogDebug("Orleans integration is disabled via feature flags");
                return AspNetHealthCheckResult.Healthy(
                    "Orleans integration is disabled by feature flag"
                );
            }

            // If Orleans is enabled but we don't have a grain factory, that's unhealthy
            if (_grainFactory == null)
            {
                _logger.LogWarning("Orleans is enabled but grain factory is not available");
                return AspNetHealthCheckResult.Unhealthy(
                    "Orleans is enabled but grain factory is not available"
                );
            }

            // Perform comprehensive Orleans health check using the dedicated grain
            var healthCheckResult = await PerformOrleansHealthCheckAsync(cancellationToken);

            _logger.LogDebug(
                "Orleans health check completed with status: {IsHealthy}",
                healthCheckResult.Status
            );
            return healthCheckResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Orleans health check was cancelled");
            return AspNetHealthCheckResult.Unhealthy("Orleans health check was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orleans health check failed with exception");
            return AspNetHealthCheckResult.Unhealthy($"Orleans health check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Performs comprehensive Orleans health check using the dedicated health check grain.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check result</returns>
    private async Task<AspNetHealthCheckResult> PerformOrleansHealthCheckAsync(
        CancellationToken cancellationToken
    )
    {
        const int timeoutMs = 5000; // 5 second timeout as specified in task requirements

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            timeoutCts.CancelAfter(timeoutMs);

            // Get the health check grain
            var healthGrain = _grainFactory!.GetGrain<IHealthCheckGrain>("orleans-health-check");

            // Perform health check with timeout
            var result = await healthGrain.CheckHealthAsync().WaitAsync(timeoutCts.Token);

            // Convert Orleans health result to ASP.NET Core health result
            if (result.IsHealthy)
            {
                var data = new Dictionary<string, object>
                {
                    ["grainId"] = result.GrainId,
                    ["lastActivity"] = result.LastActivity,
                    ["checkedAt"] = result.CheckedAt,
                    ["additionalInfo"] = result.AdditionalInfo ?? "No additional information",
                };

                if (result.Warnings.Count > 0)
                {
                    data["warnings"] = result.Warnings;
                    return AspNetHealthCheckResult.Degraded(
                        "Orleans is healthy but has warnings",
                        data: data
                    );
                }

                return AspNetHealthCheckResult.Healthy("Orleans cluster is healthy", data);
            }
            else
            {
                var data = new Dictionary<string, object>
                {
                    ["grainId"] = result.GrainId,
                    ["checkedAt"] = result.CheckedAt,
                    ["additionalInfo"] = result.AdditionalInfo ?? "No additional information",
                    ["warnings"] = result.Warnings,
                };

                return AspNetHealthCheckResult.Unhealthy(
                    "Orleans cluster is unhealthy",
                    data: data
                );
            }
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Orleans health check timed out after {TimeoutMs}ms", timeoutMs);
            return AspNetHealthCheckResult.Unhealthy(
                $"Orleans health check timed out after {timeoutMs}ms"
            );
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Orleans health check was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orleans health check grain operation failed");
            return AspNetHealthCheckResult.Unhealthy(
                $"Orleans health check grain failed: {ex.Message}"
            );
        }
    }
}
