using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Orleans grain interface for performing health checks on the Orleans cluster.
/// Provides a dedicated grain for validating Orleans infrastructure health.
/// </summary>
public interface IHealthCheckGrain : IGrainWithStringKey
{
    /// <summary>
    /// Performs a comprehensive health check of the Orleans cluster.
    /// </summary>
    /// <returns>Health check result with status and detailed information</returns>
    Task<HealthCheckResult> CheckHealthAsync();

    /// <summary>
    /// Gets basic cluster information for health monitoring.
    /// </summary>
    /// <returns>Basic cluster status information</returns>
    Task<string> GetClusterStatusAsync();

    /// <summary>
    /// Performs a quick ping test to verify grain responsiveness.
    /// </summary>
    /// <returns>Ping response timestamp</returns>
    Task<DateTime> PingAsync();
}
