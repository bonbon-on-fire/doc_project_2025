using AIChat.Orleans.Client.Services;
using AIChat.Server.Configuration;
using AIChat.Server.Services.Streaming;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AIChat.Orleans.Tests.TestUtilities.Infrastructure;

/// <summary>
/// Manages WebApplicationFactory configuration for integration tests.
/// Single Responsibility: Web application test setup only.
/// </summary>
public class TestWebApplicationManager : IDisposable
{
    private WebApplicationFactory<Program>? _factory;
    private readonly TestConfiguration _configuration;

    public TestWebApplicationManager(TestConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Gets the WebApplicationFactory instance.
    /// </summary>
    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Factory not initialized");

    /// <summary>
    /// Initializes the WebApplicationFactory with test services.
    /// </summary>
    public void Initialize(IClusterClient? orleansClient = null, IGrainFactory? grainFactory = null)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            _ = builder.ConfigureTestServices(services =>
            {
                ConfigureOrleansServices(services, orleansClient, grainFactory);
                ConfigureStreamingServices(services);
                ConfigureFeatureManagement(services);
            });

            _ = builder.ConfigureAppConfiguration(
                (context, config) => ConfigureTestSettings(config)
            );
        });
    }

    /// <summary>
    /// Creates an HttpClient configured for SSE testing.
    /// </summary>
    public HttpClient CreateSseClient()
    {
        var client = Factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );

        // Set SSE headers
        client.DefaultRequestHeaders.Add("Accept", "text/event-stream");
        client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

        return client;
    }

    /// <summary>
    /// Creates a standard HttpClient for testing.
    /// </summary>
    public HttpClient CreateStandardClient()
    {
        return Factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
    }

    private void ConfigureOrleansServices(
        IServiceCollection services,
        IClusterClient? orleansClient,
        IGrainFactory? grainFactory
    )
    {
        // Remove existing Orleans services if any
        var existingClient = services.FirstOrDefault(d => d.ServiceType == typeof(IClusterClient));
        if (existingClient != null)
        {
            _ = services.Remove(existingClient);
        }

        var existingGrainFactory = services.FirstOrDefault(d => d.ServiceType == typeof(IGrainFactory));
        if (existingGrainFactory != null)
        {
            _ = services.Remove(existingGrainFactory);
        }

        // Add test Orleans services if enabled
        if (_configuration.OrleansEnabled)
        {
            // For co-hosting mode (Test environment), use IGrainFactory
            if (grainFactory != null)
            {
                _ = services.AddSingleton(grainFactory);
            }
            // For client mode, use IClusterClient
            else if (orleansClient != null)
            {
                _ = services.AddSingleton(orleansClient);
            }

            _ = services.AddSingleton<IOrleansIntegrationService, OrleansIntegrationService>();
        }
    }

    private void ConfigureStreamingServices(IServiceCollection services)
    {
        // Configure streaming configuration
        _ = services.Configure<StreamingConfiguration>(options =>
        {
            options.BufferSize = _configuration.StreamingConfig.BufferSize;
            options.FlushIntervalMs = _configuration.StreamingConfig.FlushIntervalMs;
            options.BackpressureThreshold = _configuration.StreamingConfig.BackpressureThreshold;
            // Note: StreamingConfiguration doesn't have MaxConcurrentWrites or Enabled properties
            // Those are test-specific properties that don't map to the real configuration
        });

        // Configure resilient streaming
        _ = services.Configure<ResilientStreamingConfiguration>(options =>
        {
            options.Enabled = _configuration.ResilientStreamingEnabled;

            // Configure nested objects to match the real ResilientStreamingConfiguration structure
            options.Reconnection.MaxAttempts = _configuration.ResilientConfig.MaxRetryAttempts;
            options.Reconnection.InitialDelayMs = _configuration.ResilientConfig.RetryDelayMs;

            options.CircuitBreaker.FailureThreshold = _configuration.ResilientConfig.CircuitBreakerThreshold;
            options.CircuitBreaker.RecoveryTimeoutSeconds = _configuration.ResilientConfig.CircuitBreakerResetTimeoutMs / 1000;

            options.Buffer.Size = _configuration.ResilientConfig.PartialMessageBufferSize;
            options.Buffer.HighPrioritySize = Math.Min(10, _configuration.ResilientConfig.PartialMessageBufferSize / 5); // Ensure HighPrioritySize < Size

            options.PartialRecovery.ChunkTimeoutSeconds = _configuration.ResilientConfig.MessageTimeoutMs / 1000;

            options.HealthCheck.CheckIntervalSeconds = _configuration.ResilientConfig.HealthCheckIntervalMs / 1000;
        });

        // Add streaming services
        _ = services.AddScoped<IStreamingBridge, StreamingBridge>();
        _ = services.AddScoped<IStreamingBridgeFactory, StreamingBridgeFactory>();

        if (_configuration.ResilientStreamingEnabled)
        {
            _ = services.AddScoped<AIChat.Server.Services.Streaming.IResilientStreamManager, AIChat.Server.Services.Streaming.ResilientStreamManager>();
        }
    }

    private void ConfigureFeatureManagement(IServiceCollection services)
    {
        _ = services.AddSingleton(sp =>
        {
            var mock = new Mock<Microsoft.FeatureManagement.IFeatureManager>();
            _ = mock.Setup(x => x.IsEnabledAsync("ResilientStreaming"))
                .ReturnsAsync(_configuration.ResilientStreamingEnabled);
            _ = mock.Setup(x => x.IsEnabledAsync("OrleansIntegration"))
                .ReturnsAsync(_configuration.OrleansEnabled);
            return mock.Object;
        });
    }

    private void ConfigureTestSettings(IConfigurationBuilder config)
    {
        _ = config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Orleans:Enabled"] = _configuration.OrleansEnabled.ToString(),
                ["Features:ResilientStreaming"] =
                    _configuration.ResilientStreamingEnabled.ToString(),
                ["Logging:LogLevel:Default"] = _configuration.LogLevel,
                ["Logging:LogLevel:AIChat"] = "Debug",
            }
        );
    }

    public void Dispose()
    {
        _factory?.Dispose();
        GC.SuppressFinalize(this);
    }
}
