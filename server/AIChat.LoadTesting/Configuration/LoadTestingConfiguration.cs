namespace AIChat.LoadTesting.Configuration;

/// <summary>
/// Main configuration for load testing parameters
/// </summary>
public class LoadTestingConfiguration
{
    public const string SectionName = "LoadTesting";

    public string ServerBaseUrl { get; set; } = "https://localhost:7152";
    public string SignalRHubUrl { get; set; } = "/chatHub";
    public int TestDurationSeconds { get; set; } = 600;
    public int ReportingIntervalSeconds { get; set; } = 30;
    public int MaxConcurrentUsers { get; set; } = 10000;
    public int RampUpDurationSeconds { get; set; } = 300;
    public int StableDurationSeconds { get; set; } = 600;
    public int RampDownDurationSeconds { get; set; } = 60;

    public string GetFullSignalRUrl()
    {
        return ServerBaseUrl.TrimEnd('/') + SignalRHubUrl;
    }
}

/// <summary>
/// Configuration for specific test scenarios
/// </summary>
public class ScenariosConfiguration
{
    public const string SectionName = "Scenarios";

    public ConnectionLoadScenarioConfig ConnectionLoad { get; set; } = new();
    public MessageLatencyScenarioConfig MessageLatency { get; set; } = new();
    public GrainScalingScenarioConfig GrainScaling { get; set; } = new();
    public BackgroundProcessingScenarioConfig BackgroundProcessing { get; set; } = new();
    public MixedWorkloadScenarioConfig MixedWorkload { get; set; } = new();
    public EnduranceTestScenarioConfig EnduranceTest { get; set; } = new();
    public SseConnectionLoadScenarioConfig SseConnectionLoad { get; set; } = new();
    public SseStreamingScenarioConfig SseStreaming { get; set; } = new();
    public SseRecoveryScenarioConfig SseRecovery { get; set; } = new();
    public SseMixedLoadScenarioConfig SseMixedLoad { get; set; } = new();
}

/// <summary>
/// Connection load testing scenario configuration
/// </summary>
public class ConnectionLoadScenarioConfig
{
    public bool Enabled { get; set; } = true;
    public int MaxUsers { get; set; } = 10000;
    public int RampUpSeconds { get; set; } = 300;
    public int StableSeconds { get; set; } = 600;
    public int ConnectionTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Message latency testing scenario configuration
/// </summary>
public class MessageLatencyScenarioConfig
{
    public bool Enabled { get; set; } = true;
    public int MaxUsers { get; set; } = 1000;
    public double MessagesPerSecond { get; set; } = 5.0;
    public int TestDurationSeconds { get; set; } = 600;
    public int MaxLatencyMs { get; set; } = 100;
}

/// <summary>
/// Grain scaling behavior testing scenario configuration
/// </summary>
public class GrainScalingScenarioConfig
{
    public bool Enabled { get; set; } = true;
    public int[] BurstUsers { get; set; } = [100, 500, 1000, 2500, 5000, 10000];
    public int BurstDurationSeconds { get; set; } = 120;
    public int RestDurationSeconds { get; set; } = 60;
}

/// <summary>
/// Background processing load testing scenario configuration
/// </summary>
public class BackgroundProcessingScenarioConfig
{
    public bool Enabled { get; set; } = true;
    public int MaxUsers { get; set; } = 500;
    public int OperationsPerUser { get; set; } = 10;
    public int ConcurrentOperations { get; set; } = 50;
}

/// <summary>
/// Mixed workload scenario configuration
/// </summary>
public class MixedWorkloadScenarioConfig
{
    public bool Enabled { get; set; } = true;
    public int ConnectionUsers { get; set; } = 5000;
    public int ActiveUsers { get; set; } = 1000;
    public double MessageRate { get; set; } = 2.0;
    public int TestDurationSeconds { get; set; } = 1800;
}

/// <summary>
/// Long-running endurance test scenario configuration
/// </summary>
public class EnduranceTestScenarioConfig
{
    public bool Enabled { get; set; }
    public int MaxUsers { get; set; } = 5000;
    public int TestDurationSeconds { get; set; } = 7200;
    public double MessageRate { get; set; } = 0.5;
}

/// <summary>
/// System monitoring configuration
/// </summary>
public class MonitoringConfiguration
{
    public const string SectionName = "Monitoring";

    public bool CollectSystemMetrics { get; set; } = true;
    public int MetricsIntervalSeconds { get; set; } = 10;
    public bool MonitorOrleansGrains { get; set; } = true;
    public string MonitoringApiUrl { get; set; } = "/api/monitoring";
}

/// <summary>
/// Test validation thresholds configuration
/// </summary>
public class ValidationConfiguration
{
    public const string SectionName = "Validation";

    public int MaxAllowedLatencyMs { get; set; } = 100;
    public double MaxAllowedErrorRate { get; set; } = 0.05;
    public double MinConnectionSuccessRate { get; set; } = 0.99;
    public int MaxMemoryUsageMB { get; set; } = 8192;
    public double MaxCpuUsagePercent { get; set; } = 80.0;
}

/// <summary>
/// Orleans client configuration for direct grain testing
/// </summary>
public class OrleansConfiguration
{
    public const string SectionName = "Orleans";

    public string ClusterId { get; set; } = "dev";
    public string ServiceId { get; set; } = "AIChat.LoadTesting";
    public string[] Endpoints { get; set; } = ["localhost:11111"];
}


/// <summary>
/// SSE connection load testing scenario configuration
/// </summary>
public class SseConnectionLoadScenarioConfig
{
    public bool Enabled { get; set; } = true;
    public int MaxConnections { get; set; } = 1000;
    public int RampUpSeconds { get; set; } = 60;
    public int StableSeconds { get; set; } = 300;
    public int ConnectionTimeoutMs { get; set; } = 5000;
    public int ReconnectDelayMs { get; set; } = 1000;
    public string SseEndpoint { get; set; } = "/api/sse/stream";
}

/// <summary>
/// SSE streaming throughput testing scenario configuration
/// </summary>
public class SseStreamingScenarioConfig
{
    public bool Enabled { get; set; } = true;
    public int ConnectionCount { get; set; } = 500;
    public int TestDurationSeconds { get; set; } = 600;
    public int ChunkSizeBytes { get; set; } = 1024;
    public double ChunksPerSecond { get; set; } = 10.0;
    public int BufferSizeKB { get; set; } = 64;
}

/// <summary>
/// SSE recovery and resilience testing scenario configuration
/// </summary>
public class SseRecoveryScenarioConfig
{
    public bool Enabled { get; set; } = true;
    public int ConnectionCount { get; set; } = 100;
    public int DropConnectionEverySeconds { get; set; } = 30;
    public int MaxReconnectAttempts { get; set; } = 5;
    public int ReconnectBackoffMs { get; set; } = 1000;
    public bool TestBufferOverflow { get; set; } = true;
}

/// <summary>
/// SSE mixed load testing scenario configuration
/// </summary>
public class SseMixedLoadScenarioConfig
{
    public bool Enabled { get; set; } = true;
    public int FastConsumers { get; set; } = 300;
    public int NormalConsumers { get; set; } = 500;
    public int SlowConsumers { get; set; } = 200;
    public int TestDurationSeconds { get; set; } = 1200;
    public double ConnectionChurnRate { get; set; } = 0.1;
    public int MaxBufferedChunks { get; set; } = 100;
}

/// <summary>
/// SSE-specific configuration
/// </summary>
public class SseConfiguration
{
    public const string SectionName = "Sse";

    public string BaseEndpoint { get; set; } = "/api/sse";
    public string StreamEndpoint { get; set; } = "/api/sse/stream";
    public string MonitoringEndpoint { get; set; } = "/api/monitoring/sse";
    public int DefaultTimeoutMs { get; set; } = 30000;
    public int BufferSizeKB { get; set; } = 64;
    public bool EnableCompression { get; set; } = true;
    public bool EnableAutoReconnect { get; set; } = true;
    public int MaxReconnectAttempts { get; set; } = 5;
    public int ReconnectBackoffMs { get; set; } = 1000;
}
