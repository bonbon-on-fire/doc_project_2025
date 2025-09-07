using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.TestingHost;
using AIChat.Orleans.Grains;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.Orleans.Tests.TestUtilities.Infrastructure;

/// <summary>
/// Manages Orleans TestCluster lifecycle for integration tests.
/// Single Responsibility: Orleans cluster management only.
/// </summary>
public class TestClusterManager : IAsyncDisposable
{
    private TestCluster? _cluster;
    private bool _isInitialized;

    /// <summary>
    /// Gets the test cluster instance.
    /// </summary>
    public TestCluster Cluster => _cluster ?? throw new InvalidOperationException("Cluster not initialized");

    /// <summary>
    /// Gets the cluster client for grain interactions.
    /// </summary>
    public IClusterClient Client => Cluster.Client;

    /// <summary>
    /// Gets whether the cluster is currently running.
    /// </summary>
    public bool IsRunning => _cluster != null && _isInitialized;

    /// <summary>
    /// Initializes and starts the Orleans test cluster.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        
        _cluster = builder.Build();
        await _cluster.DeployAsync();
        _isInitialized = true;
    }

    /// <summary>
    /// Stops all silos in the cluster (simulates failure).
    /// </summary>
    public async Task StopAllSilosAsync()
    {
        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
        }
    }

    /// <summary>
    /// Restarts the cluster after a failure.
    /// </summary>
    public async Task RestartAsync()
    {
        await DisposeAsync();
        await InitializeAsync();
    }

    /// <summary>
    /// Disposes of the test cluster resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
            _cluster = null;
            _isInitialized = false;
        }
    }

    /// <summary>
    /// Test silo configurator for Orleans.
    /// </summary>
    private class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "test-cluster";
                    options.ServiceId = "test-service";
                })
                .Configure<EndpointOptions>(options =>
                {
                    options.AdvertisedIPAddress = System.Net.IPAddress.Loopback;
                })
                .ConfigureServices(services =>
                {
                    // Register grain assemblies
                    services.AddSingleton(typeof(UserGrain).Assembly);
                    // Dashboard is not needed for tests
                });
        }
    }
}