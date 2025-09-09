using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Orleans grain interface for performing health checks on the Orleans cluster.
/// Provides a dedicated grain for validating Orleans infrastructure health.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IHealthCheckGrain")]
public interface IHealthCheckGrain : IGrainWithStringKey
{
    /// <summary>
    /// Performs a comprehensive health check of the Orleans cluster.
    /// </summary>
    /// <returns>Health check result with status and detailed information</returns>
    [Alias("CheckHealthAsync")]
    Task<HealthCheckResult> CheckHealthAsync();

    /// <summary>
    /// Gets basic cluster information for health monitoring.
    /// </summary>
    /// <returns>Basic cluster status information</returns>
    [Alias("GetClusterStatusAsync")]
    Task<string> GetClusterStatusAsync();

    /// <summary>
    /// Performs a quick ping test to verify grain responsiveness.
    /// </summary>
    /// <returns>Ping response timestamp</returns>
    [Alias("PingAsync")]
    Task<DateTime> PingAsync();
}
