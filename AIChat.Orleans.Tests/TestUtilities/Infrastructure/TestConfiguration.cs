namespace AIChat.Orleans.Tests.TestUtilities.Infrastructure;

/// <summary>
/// Centralized configuration for test infrastructure.
/// Follows Builder pattern for easy configuration.
/// </summary>
public class TestConfiguration
{
    public bool OrleansEnabled { get; set; } = true;
    public bool ResilientStreamingEnabled { get; set; }
    public string LogLevel { get; set; } = "Warning";

    public StreamingTestConfig StreamingConfig { get; set; } = new();
    public ResilientTestConfig ResilientConfig { get; set; } = new();
    public PerformanceTestConfig PerformanceConfig { get; set; } = new();

    /// <summary>
    /// Creates a default test configuration.
    /// </summary>
    public static TestConfiguration Default => new();

    /// <summary>
    /// Creates a configuration for Orleans testing.
    /// </summary>
    public static TestConfiguration ForOrleans()
    {
        return new()
        {
            OrleansEnabled = true,
            ResilientStreamingEnabled = false
        };
    }

    /// <summary>
    /// Creates a configuration for direct processing testing.
    /// </summary>
    public static TestConfiguration ForDirect()
    {
        return new()
        {
            OrleansEnabled = false,
            ResilientStreamingEnabled = false
        };
    }

    /// <summary>
    /// Creates a configuration for resilient streaming testing.
    /// </summary>
    public static TestConfiguration ForResilientStreaming()
    {
        return new()
        {
            OrleansEnabled = true,
            ResilientStreamingEnabled = true
        };
    }

    /// <summary>
    /// Creates a configuration for performance testing.
    /// </summary>
    public static TestConfiguration ForPerformance()
    {
        return new()
        {
            OrleansEnabled = true,
            LogLevel = "Error", // Reduce logging for performance tests
            PerformanceConfig = new PerformanceTestConfig
            {
                WarmupIterations = 5,
                TestIterations = 20,
                MaxAcceptableOverheadPercent = 50,
                MaxResponseTimeMs = 5000,
                ConcurrentUsers = 100
            }
        };
    }
}

/// <summary>
/// Streaming-specific test configuration.
/// </summary>
public class StreamingTestConfig
{
    public int BufferSize { get; set; } = 100;
    public int FlushIntervalMs { get; set; } = 100;
    public int MaxConcurrentWrites { get; set; } = 10;
    public int BackpressureThreshold { get; set; } = 80;
    public bool Enabled { get; set; } = true;
    public int TimeoutMs { get; set; } = 30000;
}

/// <summary>
/// Resilient streaming test configuration.
/// </summary>
public class ResilientTestConfig
{
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 100;
    public int CircuitBreakerThreshold { get; set; } = 5;
    public int CircuitBreakerResetTimeoutMs { get; set; } = 5000;
    public int PartialMessageBufferSize { get; set; } = 50;
    public int MessageTimeoutMs { get; set; } = 30000;
    public int HealthCheckIntervalMs { get; set; } = 10000;
}

/// <summary>
/// Performance test configuration.
/// </summary>
public class PerformanceTestConfig
{
    public int WarmupIterations { get; set; } = 3;
    public int TestIterations { get; set; } = 10;
    public int MaxAcceptableOverheadPercent { get; set; } = 50;
    public int MaxResponseTimeMs { get; set; } = 5000;
    public int ConcurrentUsers { get; set; } = 50;
    public bool EnableDetailedMetrics { get; set; }
}

/// <summary>
/// Fluent builder for TestConfiguration.
/// </summary>
public class TestConfigurationBuilder
{
    private readonly TestConfiguration _config = new();

    public static TestConfigurationBuilder Create()
    {
        return new();
    }

    public TestConfigurationBuilder WithOrleans(bool enabled = true)
    {
        _config.OrleansEnabled = enabled;
        return this;
    }

    public TestConfigurationBuilder WithResilientStreaming(bool enabled = true)
    {
        _config.ResilientStreamingEnabled = enabled;
        return this;
    }

    public TestConfigurationBuilder WithLogLevel(string level)
    {
        _config.LogLevel = level;
        return this;
    }

    public TestConfigurationBuilder WithStreamingConfig(Action<StreamingTestConfig> configure)
    {
        configure(_config.StreamingConfig);
        return this;
    }

    public TestConfigurationBuilder WithResilientConfig(Action<ResilientTestConfig> configure)
    {
        configure(_config.ResilientConfig);
        return this;
    }

    public TestConfigurationBuilder WithPerformanceConfig(Action<PerformanceTestConfig> configure)
    {
        configure(_config.PerformanceConfig);
        return this;
    }

    public TestConfiguration Build()
    {
        return _config;
    }
}
