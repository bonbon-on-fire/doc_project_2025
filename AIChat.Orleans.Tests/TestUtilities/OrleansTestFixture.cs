using AIChat.Orleans.Tests.TestUtilities.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Orleans.TestingHost;
using Xunit;

namespace AIChat.Orleans.Tests.TestUtilities;

/// <summary>
/// Shared test fixture for Orleans SSE integration tests.
/// Refactored to follow Single Responsibility Principle by delegating to specialized managers.
/// </summary>
public class OrleansTestFixture : IAsyncLifetime
{
    private TestClusterManager? _clusterManager;
    private TestWebApplicationManager? _webAppManager;
    private TestConfiguration _configuration = TestConfiguration.Default;

    /// <summary>
    /// Gets the test cluster for Orleans grain interactions.
    /// </summary>
    public TestCluster Cluster => _clusterManager?.Cluster
        ?? throw new InvalidOperationException("Cluster not initialized");

    /// <summary>
    /// Gets the WebApplicationFactory for HTTP testing.
    /// </summary>
    public WebApplicationFactory<Program> WebAppFactory => _webAppManager?.Factory
        ?? throw new InvalidOperationException("WebApp not initialized");

    /// <summary>
    /// Gets or sets whether Orleans is enabled for the test.
    /// </summary>
    public bool OrleansEnabled
    {
        get => _configuration.OrleansEnabled;
        set => _configuration.OrleansEnabled = value;
    }

    /// <summary>
    /// Gets or sets whether resilient streaming is enabled.
    /// </summary>
    public bool ResilientStreamingEnabled
    {
        get => _configuration.ResilientStreamingEnabled;
        set => _configuration.ResilientStreamingEnabled = value;
    }

    /// <summary>
    /// Gets or sets the test configuration.
    /// </summary>
    public TestConfiguration Configuration
    {
        get => _configuration;
        set => _configuration = value ?? TestConfiguration.Default;
    }

    public async Task InitializeAsync()
    {
        // Initialize Orleans TestCluster if enabled
        if (_configuration.OrleansEnabled)
        {
            _clusterManager = new TestClusterManager();
            await _clusterManager.InitializeAsync();
        }

        // Initialize WebApplicationFactory with test configuration
        _webAppManager = new TestWebApplicationManager(_configuration);
        _webAppManager.Initialize(_clusterManager?.Client);
    }

    public async Task DisposeAsync()
    {
        _webAppManager?.Dispose();

        if (_clusterManager != null)
        {
            await _clusterManager.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates an HttpClient configured for SSE testing.
    /// </summary>
    public HttpClient CreateSseClient()
    {
        return _webAppManager == null
            ? throw new InvalidOperationException("WebAppManager not initialized. Call InitializeAsync first.")
            : _webAppManager.CreateSseClient();
    }

    /// <summary>
    /// Creates a standard HttpClient for testing.
    /// </summary>
    public HttpClient CreateStandardClient()
    {
        return _webAppManager == null
            ? throw new InvalidOperationException("WebAppManager not initialized. Call InitializeAsync first.")
            : _webAppManager.CreateStandardClient();
    }

    /// <summary>
    /// Configures the fixture with a custom configuration.
    /// Must be called before InitializeAsync.
    /// </summary>
    public OrleansTestFixture WithConfiguration(TestConfiguration configuration)
    {
        _configuration = configuration ?? TestConfiguration.Default;
        return this;
    }

    /// <summary>
    /// Configures the fixture using a builder.
    /// Must be called before InitializeAsync.
    /// </summary>
    public OrleansTestFixture WithConfiguration(Action<TestConfigurationBuilder> configure)
    {
        var builder = TestConfigurationBuilder.Create();
        configure(builder);
        _configuration = builder.Build();
        return this;
    }
}
