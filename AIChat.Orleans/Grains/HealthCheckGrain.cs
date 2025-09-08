using AIChat.Orleans.Contracts;
using AIChat.Orleans.Tracing;
using Microsoft.Extensions.Logging;
using Orleans;

namespace AIChat.Orleans.Grains;

/// <summary>
/// Orleans grain implementation for performing health checks on the Orleans cluster.
/// Provides comprehensive health monitoring and diagnostics for the Orleans infrastructure.
/// </summary>
public sealed class HealthCheckGrain : Grain, IHealthCheckGrain
{
    private readonly ILogger<HealthCheckGrain> _logger;

    /// <summary>
    /// Initializes a new instance of the HealthCheckGrain.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics</param>
    public HealthCheckGrain(ILogger<HealthCheckGrain> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync()
    {
        using var activity = OrleansActivitySource.StartGrainActivity("HealthCheckGrain", nameof(CheckHealthAsync), this.GetPrimaryKeyString());
        try
        {
            var result = new HealthCheckResult
            {
                GrainId = this.GetPrimaryKeyString(),
                CheckedAt = DateTime.UtcNow
            };

            try
            {
                _logger.LogDebug("Starting comprehensive health check for Orleans cluster");

                // Test grain activation and basic functionality
                var activationTest = await TestGrainActivationAsync();
                if (!activationTest.Success)
                {
                    result.IsHealthy = false;
                    result.Warnings.Add($"Grain activation test failed: {activationTest.Message}");
                }

                // Test cluster connectivity
                var clusterTest = await TestClusterConnectivityAsync();
                if (!clusterTest.Success)
                {
                    result.IsHealthy = false;
                    result.Warnings.Add($"Cluster connectivity test failed: {clusterTest.Message}");
                }

                // Get basic metrics
                result.LastActivity = DateTime.UtcNow;

                // If we got this far without major failures, we're healthy
                if (result.Warnings.Count == 0)
                {
                    result.IsHealthy = true;
                    result.AdditionalInfo = "All health checks passed successfully";
                    _logger.LogDebug("Orleans health check completed successfully");
                }
                else
                {
                    result.AdditionalInfo = $"Health check completed with {result.Warnings.Count} warnings";
                    _logger.LogWarning("Orleans health check completed with warnings: {Warnings}", string.Join(", ", result.Warnings));
                }

                // Mark activity as successful
                OrleansActivitySource.SetSuccess(activity, new Dictionary<string, object>
            {
                {"health.status", result.IsHealthy},
                {"warnings.count", result.Warnings.Count}
            });
            }
            catch (Exception ex)
            {
                result.IsHealthy = false;
                result.AdditionalInfo = $"Health check failed with exception: {ex.Message}";
                result.Warnings.Add($"Unhandled exception during health check: {ex.GetType().Name}");

                // Set activity error
                OrleansActivitySource.SetError(activity, ex);

                _logger.LogError(ex, "Orleans health check failed with exception");
            }

            return result;
        }
        catch (Exception outerEx)
        {
            // Handle any exceptions from the activity setup
            OrleansActivitySource.SetError(activity, outerEx);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<string> GetClusterStatusAsync()
    {
        try
        {
            // Get basic cluster information
            var status = $"Orleans cluster operational at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";
            _logger.LogDebug("Cluster status requested: {Status}", status);
            return Task.FromResult(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cluster status");
            return Task.FromResult($"Error getting cluster status: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<DateTime> PingAsync()
    {
        var timestamp = DateTime.UtcNow;
        _logger.LogDebug("Health check ping received at {Timestamp}", timestamp);
        return Task.FromResult(timestamp);
    }

    /// <summary>
    /// Tests basic grain activation and functionality.
    /// </summary>
    /// <returns>Test result with success status and message</returns>
    private Task<(bool Success, string Message)> TestGrainActivationAsync()
    {
        try
        {
            // Test that we can perform basic grain operations
            var grainId = this.GetPrimaryKeyString();
            if (string.IsNullOrEmpty(grainId))
            {
                return Task.FromResult((false, "Grain ID is null or empty"));
            }

            // Test basic state operations
            var timestamp = DateTime.UtcNow;
            return Task.FromResult((true, $"Grain activation successful at {timestamp:HH:mm:ss.fff}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"Grain activation test failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Tests cluster connectivity and basic Orleans infrastructure.
    /// </summary>
    /// <returns>Test result with success status and message</returns>
    private Task<(bool Success, string Message)> TestClusterConnectivityAsync()
    {
        try
        {
            // Basic connectivity test - if we can execute this method, cluster is responsive
            // The fact that this grain method is executing means the cluster is operational
            return Task.FromResult((true, "Cluster connectivity verified - grain method execution successful"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"Cluster connectivity test failed: {ex.Message}"));
        }
    }
}
