using AIChat.Orleans.Configuration;
using AIChat.Orleans.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace AIChat.Orleans.Tests.Phase1;

/// <summary>
/// Comprehensive TestCluster setup for Orleans Phase 1 integration tests.
/// Provides all required services and configurations for testing Orleans grains.
/// </summary>
public class Phase1TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            // Use memory storage for tests
            .AddMemoryGrainStorageAsDefault()
            .AddMemoryGrainStorage("UserGrainStorage")
            .AddMemoryGrainStorage("PubSubStore")
            
            // Configure services required by grains
            .ConfigureServices(services =>
            {
                // Add Orleans metrics collector (required by UserGrain)
                services.AddSingleton<IOrleansMetricsCollector, OrleansMetricsCollector>();
                
                // Add Orleans grain configuration with test-friendly settings
                services.Configure<OrleansGrainConfiguration>(config =>
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
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Warning);
                // Only show errors for Orleans runtime during tests
                logging.AddFilter("Orleans", LogLevel.Error);
                logging.AddFilter("Microsoft", LogLevel.Error);
                // But allow our Orleans components to log at Debug level
                logging.AddFilter("AIChat.Orleans", LogLevel.Debug);
            });
    }
}

/// <summary>
/// TestCluster configurator for client-side testing.
/// </summary>
public class Phase1TestClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        // Configure client-side services if needed
        // Note: IClientBuilder doesn't have ConfigureLogging in Orleans 9.x
        // Logging is configured at the host level
    }
}

/// <summary>
/// Base class for Orleans Phase 1 integration tests.
/// Provides shared TestCluster setup and common utilities.
/// </summary>
public abstract class Phase1IntegrationTestBase
{
    protected TestCluster? TestCluster { get; private set; }

    /// <summary>
    /// Sets up the TestCluster with Phase 1 configuration.
    /// Call this from test class SetUp method.
    /// </summary>
    protected async Task SetupTestCluster()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<Phase1TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<Phase1TestClientConfigurator>();
        
        TestCluster = builder.Build();
        await TestCluster.DeployAsync();
    }

    /// <summary>
    /// Tears down the TestCluster.
    /// Call this from test class TearDown method.
    /// </summary>
    protected async Task TearDownTestCluster()
    {
        if (TestCluster != null)
        {
            await TestCluster.StopAllSilosAsync();
            TestCluster.Dispose();
            TestCluster = null;
        }
    }

    /// <summary>
    /// Gets a grain from the test cluster with proper error handling.
    /// </summary>
    /// <typeparam name="TGrain">Type of grain interface</typeparam>
    /// <param name="grainId">Grain identifier</param>
    /// <returns>Grain proxy</returns>
    protected TGrain GetGrain<TGrain>(string grainId) where TGrain : IGrainWithStringKey
    {
        if (TestCluster == null)
            throw new InvalidOperationException("TestCluster not initialized. Call SetupTestCluster() first.");
            
        return TestCluster.GrainFactory.GetGrain<TGrain>(grainId);
    }

    /// <summary>
    /// Waits for grain activation with timeout.
    /// Useful for ensuring grains are ready before testing.
    /// </summary>
    /// <param name="grain">Grain to activate</param>
    /// <param name="timeout">Timeout for activation</param>
    protected async Task WaitForGrainActivation(IGrain grain, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(10);
        
        using var cts = new CancellationTokenSource(timeout.Value);
        
        // Ping the grain to ensure activation
        var pingMethod = grain.GetType().GetMethod("GetState") ?? 
                        grain.GetType().GetMethod("CheckHealth");
        
        if (pingMethod != null)
        {
            try
            {
                var result = pingMethod.Invoke(grain, null);
                if (result is Task task)
                {
                    await task.WaitAsync(cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Grain activation timed out after {timeout}");
            }
        }
    }
}