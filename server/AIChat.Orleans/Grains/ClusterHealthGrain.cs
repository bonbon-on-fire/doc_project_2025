using System.Globalization;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Tracing;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Grains;

/// <summary>
/// Stateless worker grain that provides cluster-wide health monitoring.
/// Enables WebHost controllers to query cluster health through grain interfaces
/// rather than directly accessing management APIs.
/// </summary>
/// <remarks>
/// This grain is marked as a stateless worker, meaning Orleans can create multiple
/// instances across silos for better scalability. It provides basic cluster health
/// information by validating grain activation and responsiveness.
///
/// Architecture Pattern: Grain Facade
/// - Provides clean boundary for WebHost monitoring
/// - Enables independent hosting of Orleans silo and WebHost
/// - Validates cluster health through grain activation success
/// </remarks>
[StatelessWorker]
[Reentrant]
public sealed class ClusterHealthGrain : Grain, IClusterHealthGrain
{
    private readonly ILogger<ClusterHealthGrain> _logger;

    /// <summary>
    /// Initializes a new instance of the ClusterHealthGrain.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics</param>
    public ClusterHealthGrain(ILogger<ClusterHealthGrain> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<ClusterHealthStatus> GetClusterHealthAsync()
    {
        using var activity = OrleansActivitySource.StartGrainActivity(
            "ClusterHealthGrain",
            nameof(GetClusterHealthAsync),
            this.GetPrimaryKeyLong().ToString(CultureInfo.InvariantCulture)
        );

        try
        {
            _logger.LogDebug("Retrieving cluster health status via ClusterHealthGrain");

            // If this grain activated successfully, the cluster is operational
            var healthStatus = new ClusterHealthStatus
            {
                IsHealthy = true,
                Status = "Healthy",
                CheckedAt = DateTime.UtcNow,
                ActiveSiloCount = 1 // Simplified - at least one silo is active (this one)
            };

            healthStatus.Details["grain_activation"] = "successful";
            healthStatus.Details["checked_from_silo"] = RuntimeIdentity.ToString();

            _logger.LogDebug("Cluster health check completed: {Status}", healthStatus.Status);

            OrleansActivitySource.SetSuccess(activity, new Dictionary<string, object>
            {
                { "health.status", healthStatus.Status },
                { "health.is_healthy", healthStatus.IsHealthy }
            });

            return Task.FromResult(healthStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in GetClusterHealthAsync");
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<List<SiloInfo>> GetSiloStatusAsync()
    {
        using var activity = OrleansActivitySource.StartGrainActivity(
            "ClusterHealthGrain",
            nameof(GetSiloStatusAsync),
            this.GetPrimaryKeyLong().ToString(CultureInfo.InvariantCulture)
        );

        try
        {
            _logger.LogDebug("Retrieving silo status information via ClusterHealthGrain");

            // Simplified implementation - return basic info about current silo
            var siloInfoList = new List<SiloInfo>
            {
                new()
                {
                    SiloAddress = RuntimeIdentity.ToString(),
                    Status = "Active",
                    SinceWhen = DateTime.UtcNow, // Approximation
                    Metadata =
                    {
                        ["grain_host"] = RuntimeIdentity.ToString()
                    }
                }
            };

            _logger.LogDebug("Successfully retrieved silo status information");

            OrleansActivitySource.SetSuccess(activity, new Dictionary<string, object>
            {
                { "silo_count", siloInfoList.Count }
            });

            return Task.FromResult(siloInfoList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in GetSiloStatusAsync");
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<DateTime> PingAsync()
    {
        var timestamp = DateTime.UtcNow;
        _logger.LogTrace("Cluster health ping received at {Timestamp}", timestamp);
        return Task.FromResult(timestamp);
    }
}
