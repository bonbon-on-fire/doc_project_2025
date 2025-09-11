using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;

namespace AIChat.Server.Controllers;

/// <summary>
/// Provides server capabilities information to clients
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CapabilitiesController : ControllerBase
{
    private readonly ILogger<CapabilitiesController> _logger;
    private readonly IFeatureManager _featureManager;
    private readonly IGrainFactory? _grainFactory;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public CapabilitiesController(
        ILogger<CapabilitiesController> logger,
        IFeatureManager featureManager,
        IHostEnvironment environment,
        IConfiguration configuration,
        IGrainFactory? grainFactory = null
    )
    {
        _logger = logger;
        _featureManager = featureManager;
        _environment = environment;
        _configuration = configuration;
        _grainFactory = grainFactory;
    }

    /// <summary>
    /// Gets the current server capabilities
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ServerCapabilities>> GetCapabilities()
    {
        var capabilities = new ServerCapabilities
        {
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
            Environment = _environment.EnvironmentName,
            Timestamp = DateTime.UtcNow,
            Orleans = await GetOrleansCapabilities(),
            Features = await GetEnabledFeatures(),
            StreamingProtocols = GetStreamingProtocols(),
        };

        return Ok(capabilities);
    }

    private async Task<OrleansCapabilities> GetOrleansCapabilities()
    {
        var orleansCapabilities = new OrleansCapabilities
        {
            Enabled = false,
            CoHosted = false,
            Available = false,
            RoutingEnabled = false,
        };

        // Check if Orleans feature is enabled
        var orleansEnabled = await _featureManager.IsEnabledAsync("OrleansIntegration");
        orleansCapabilities.Enabled = orleansEnabled;

        if (!orleansEnabled)
        {
            return orleansCapabilities;
        }

        // Check if Orleans grain factory is available
        if (_grainFactory != null)
        {
            orleansCapabilities.Available = true;

            // In Development/Test, Orleans is co-hosted
            orleansCapabilities.CoHosted =
                _environment.IsDevelopment() || _environment.EnvironmentName == "Test";

            // Try to check cluster health
            try
            {
                var managementGrain = _grainFactory.GetGrain<IManagementGrain>(0);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var hosts = await managementGrain.GetHosts();
                orleansCapabilities.ClusterSize = hosts?.Count ?? 0;
                orleansCapabilities.Healthy = orleansCapabilities.ClusterSize > 0;
                orleansCapabilities.RoutingEnabled = orleansCapabilities.Healthy;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not get Orleans cluster status");
                orleansCapabilities.Healthy = false;
                orleansCapabilities.RoutingEnabled = false;
            }
        }

        return orleansCapabilities;
    }

    private async Task<Dictionary<string, bool>> GetEnabledFeatures()
    {
        var features = new Dictionary<string, bool>();

        // Check key features
        var featureNames = new[]
        {
            "OrleansIntegration",
            "BackgroundProcessing",
            "StreamingEnhancements",
            "ResilientStreaming",
        };

        foreach (var feature in featureNames)
        {
            features[feature] = await _featureManager.IsEnabledAsync(feature);
        }

        return features;
    }

    private List<string> GetStreamingProtocols()
    {
        var protocols = new List<string> { "SSE" }; // Always support SSE

        // Check if SignalR is configured
        if (_configuration.GetValue<bool>("SignalR:Enabled", true))
        {
            protocols.Add("SignalR");
        }

        return protocols;
    }
}

/// <summary>
/// Server capabilities response model
/// </summary>
public class ServerCapabilities
{
    public string Version { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public OrleansCapabilities Orleans { get; set; } = new();
    public Dictionary<string, bool> Features { get; set; } = [];
    public List<string> StreamingProtocols { get; set; } = [];
}

/// <summary>
/// Orleans-specific capabilities
/// </summary>
public class OrleansCapabilities
{
    public bool Enabled { get; set; }
    public bool Available { get; set; }
    public bool CoHosted { get; set; }
    public bool Healthy { get; set; }
    public bool RoutingEnabled { get; set; }
    public int ClusterSize { get; set; }
}
