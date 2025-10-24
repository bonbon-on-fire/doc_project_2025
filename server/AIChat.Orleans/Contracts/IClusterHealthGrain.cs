using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Stateless worker grain for accessing Orleans cluster health status.
/// Provides cluster-wide health information for WebHost monitoring endpoints.
/// This enables architectural separation between WebHost and Silo internals.
/// </summary>
/// <remarks>
/// This grain acts as a monitoring facade, allowing WebHost controllers to query
/// cluster health through grain interfaces rather than directly accessing management APIs.
/// As a stateless worker, it's lightweight and can be called from any silo in the cluster.
/// </remarks>
[Alias("AIChat.Orleans.Contracts.IClusterHealthGrain")]
public interface IClusterHealthGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// Performs a comprehensive health check of the Orleans cluster.
    /// </summary>
    /// <returns>Cluster health status including silo count, connectivity, and overall health</returns>
    [Alias("GetClusterHealthAsync")]
    Task<ClusterHealthStatus> GetClusterHealthAsync();

    /// <summary>
    /// Gets detailed information about all active silos in the cluster.
    /// </summary>
    /// <returns>List of silo information including addresses, status, and activation counts</returns>
    [Alias("GetSiloStatusAsync")]
    Task<List<SiloInfo>> GetSiloStatusAsync();

    /// <summary>
    /// Performs a quick ping test to verify grain responsiveness.
    /// </summary>
    /// <returns>Timestamp when the ping was received</returns>
    [Alias("PingAsync")]
    Task<DateTime> PingAsync();
}

/// <summary>
/// Represents the overall health status of the Orleans cluster.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ClusterHealthStatus")]
public class ClusterHealthStatus
{
    /// <summary>
    /// Indicates whether the cluster is healthy and operational.
    /// </summary>
    [Id(0)]
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Number of active silos in the cluster.
    /// </summary>
    [Id(1)]
    public int ActiveSiloCount { get; set; }

    /// <summary>
    /// Timestamp when the health check was performed.
    /// </summary>
    [Id(2)]
    public DateTime CheckedAt { get; set; }

    /// <summary>
    /// Overall cluster state description (e.g., "Healthy", "Degraded", "Unhealthy").
    /// </summary>
    [Id(3)]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// List of warnings or issues detected during health check.
    /// </summary>
    [Id(4)]
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Additional diagnostic information about cluster health.
    /// </summary>
    [Id(5)]
    public Dictionary<string, object> Details { get; set; } = [];
}

/// <summary>
/// Represents information about a single silo in the cluster.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.SiloInfo")]
public class SiloInfo
{
    /// <summary>
    /// Unique identifier for the silo.
    /// </summary>
    [Id(0)]
    public string SiloAddress { get; set; } = string.Empty;

    /// <summary>
    /// Silo name if configured.
    /// </summary>
    [Id(1)]
    public string? SiloName { get; set; }

    /// <summary>
    /// Current status of the silo (e.g., "Active", "Stopping", "Dead").
    /// </summary>
    [Id(2)]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Number of active grain activations on this silo.
    /// </summary>
    [Id(3)]
    public int ActivationCount { get; set; }

    /// <summary>
    /// When the silo became active in the cluster.
    /// </summary>
    [Id(4)]
    public DateTime? SinceWhen { get; set; }

    /// <summary>
    /// Additional silo-specific information.
    /// </summary>
    [Id(5)]
    public Dictionary<string, object> Metadata { get; set; } = [];
}
