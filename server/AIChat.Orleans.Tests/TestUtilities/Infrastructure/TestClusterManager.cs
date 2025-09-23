using AIChat.Orleans.Configuration;
using AIChat.Orleans.Grains;
using AIChat.Orleans.Metrics;
using AIChat.Orleans.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.TestingHost;

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
    public TestCluster Cluster =>
        _cluster ?? throw new InvalidOperationException("Cluster not initialized");

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
        {
            return;
        }

        var builder = new TestClusterBuilder();
        _ = builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        _ = builder.AddClientBuilderConfigurator<TestClientConfigurator>();

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
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Test silo configurator for Orleans.
    /// </summary>
    private sealed class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            _ = siloBuilder
                // Use memory storage for tests
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("UserGrainStorage")
                .AddMemoryGrainStorage("ChatGrainStorage")
                .AddMemoryGrainStorage("PubSubStore")
                // Let TestClusterBuilder handle the ClusterId to avoid conflicts
                .Configure<EndpointOptions>(options =>
                    options.AdvertisedIPAddress = System.Net.IPAddress.Loopback
                )
                // Orleans 9.x auto-discovers grain assemblies - no explicit registration needed
                .ConfigureServices(services =>
                {
                    // Register Orleans metrics collector (required by grains)
                    _ = services.AddSingleton<IOrleansMetricsCollector, OrleansMetricsCollector>();

                    // Register SignalR broadcast service (required by ChatGrain)
                    _ = services.AddSingleton<ISignalRBroadcastService, NullSignalRBroadcastService>();

                    // Add Orleans grain configuration with test-friendly settings
                    _ = services.Configure<OrleansGrainConfiguration>(config =>
                    {
                        config.UserGrain.MaxActivityBufferSize = 50; // Smaller buffer for tests
                        config.UserGrain.CleanupIntervalMinutes = 1; // Faster cleanup for tests
                        config.UserGrain.EnablePeriodicTimers = false; // Disable timers for tests
                        config.Connections.MaxConnectionsPerUser = 5;
                        config.Persistence.ActivityPersistenceInterval = 5;
                    });
                })
                // Configure logging for tests (reduced noise)
                .ConfigureLogging(logging =>
                {
                    _ = logging.ClearProviders();
                    _ = logging.AddConsole();
                    _ = logging.SetMinimumLevel(LogLevel.Warning);
                    // Only show errors for Orleans runtime during tests
                    _ = logging.AddFilter("Orleans", LogLevel.Error);
                    _ = logging.AddFilter("Microsoft", LogLevel.Error);
                    // But allow our Orleans components to log at Debug level
                    _ = logging.AddFilter("AIChat.Orleans", LogLevel.Debug);
                });
        }
    }

    /// <summary>
    /// Test client configurator for Orleans.
    /// </summary>
    private sealed class TestClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            // Orleans 9.x auto-discovers grain assemblies - no explicit client configuration needed
        }
    }
}
