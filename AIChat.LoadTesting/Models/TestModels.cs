using System.Text.Json.Serialization;

namespace AIChat.LoadTesting.Models;

/// <summary>
/// Represents a test user in the load testing scenario
/// </summary>
public class TestUser
{
    public string UserId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public DateTime ConnectedAt { get; set; }
    public DateTime LastActivity { get; set; }
    public bool IsConnected { get; set; }
    public int MessagesSent { get; set; }
    public int MessagesReceived { get; set; }
    public List<string> JoinedChats { get; set; } = new();
    public TestUserMetrics Metrics { get; set; } = new();
}

/// <summary>
/// Metrics tracked for each test user
/// </summary>
public class TestUserMetrics
{
    public TimeSpan ConnectionTime { get; set; }
    public List<double> MessageLatencies { get; set; } = new();
    public int ConnectionAttempts { get; set; }
    public int ConnectionFailures { get; set; }
    public int MessageFailures { get; set; }
    public DateTime LastError { get; set; }
    public string LastErrorMessage { get; set; } = string.Empty;

    public double AverageLatency => MessageLatencies.Count > 0 ? MessageLatencies.Average() : 0;
    public double MaxLatency => MessageLatencies.Count > 0 ? MessageLatencies.Max() : 0;
    public double ConnectionSuccessRate => ConnectionAttempts > 0 ? (double)(ConnectionAttempts - ConnectionFailures) / ConnectionAttempts : 0;
}

/// <summary>
/// Message sent during load testing
/// </summary>
public class TestMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ChatId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReceivedAt { get; set; }
    public bool IsDelivered { get; set; }
    public TimeSpan? Latency => ReceivedAt.HasValue ? ReceivedAt.Value - SentAt : null;
}

/// <summary>
/// Real-time system metrics during load testing
/// </summary>
public class SystemMetrics
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double CpuUsagePercent { get; set; }
    public long MemoryUsageMB { get; set; }
    public long MemoryAvailableMB { get; set; }
    public int ActiveConnections { get; set; }
    public int ActiveGrains { get; set; }
    public double NetworkBandwidthMbps { get; set; }
    public OrleansMetrics Orleans { get; set; } = new();
}

/// <summary>
/// Orleans-specific metrics
/// </summary>
public class OrleansMetrics
{
    public bool SiloHealthy { get; set; }
    public int ActiveGrainCount { get; set; }
    public double GrainActivationRate { get; set; }
    public double GrainDeactivationRate { get; set; }
    public double MessageProcessingRate { get; set; }
    public int BackgroundQueueDepth { get; set; }
    public double AverageMessageLatencyMs { get; set; }
    public int ErrorCount { get; set; }
}

/// <summary>
/// Load testing scenario result
/// </summary>
public class ScenarioResult
{
    public string ScenarioName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    
    public int TotalUsers { get; set; }
    public int SuccessfulConnections { get; set; }
    public int FailedConnections { get; set; }
    public double ConnectionSuccessRate => TotalUsers > 0 ? (double)SuccessfulConnections / TotalUsers : 0;
    
    public int TotalMessages { get; set; }
    public int DeliveredMessages { get; set; }
    public double MessageDeliveryRate => TotalMessages > 0 ? (double)DeliveredMessages / TotalMessages : 0;
    
    public LatencyStatistics LatencyStats { get; set; } = new();
    public ThroughputStatistics ThroughputStats { get; set; } = new();
    public ResourceUsageStatistics ResourceStats { get; set; } = new();
    
    public List<string> Errors { get; set; } = new();
    public bool Success { get; set; }
    public string FailureReason { get; set; } = string.Empty;
}

/// <summary>
/// Latency statistics
/// </summary>
public class LatencyStatistics
{
    public double AverageMs { get; set; }
    public double MedianMs { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }
    public double MaxMs { get; set; }
    public double MinMs { get; set; }
    public int SampleCount { get; set; }
}

/// <summary>
/// Throughput statistics
/// </summary>
public class ThroughputStatistics
{
    public double ConnectionsPerSecond { get; set; }
    public double MessagesPerSecond { get; set; }
    public double PeakConnectionsPerSecond { get; set; }
    public double PeakMessagesPerSecond { get; set; }
    public double AverageResponseTime { get; set; }
}

/// <summary>
/// Resource usage statistics
/// </summary>
public class ResourceUsageStatistics
{
    public double AverageCpuPercent { get; set; }
    public double MaxCpuPercent { get; set; }
    public long AverageMemoryMB { get; set; }
    public long MaxMemoryMB { get; set; }
    public double AverageNetworkMbps { get; set; }
    public double MaxNetworkMbps { get; set; }
    public bool ResourceLimitsExceeded { get; set; }
    public List<string> ResourceWarnings { get; set; } = new();
}

/// <summary>
/// Comprehensive load test report
/// </summary>
public class LoadTestReport
{
    public string TestName { get; set; } = string.Empty;
    public DateTime TestStartTime { get; set; }
    public DateTime TestEndTime { get; set; }
    public TimeSpan TotalDuration => TestEndTime - TestStartTime;
    
    public List<ScenarioResult> ScenarioResults { get; set; } = new();
    public LoadTestSummary Summary { get; set; } = new();
    public ValidationResults ValidationResults { get; set; } = new();
    
    public string ConfigurationUsed { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

/// <summary>
/// Overall load test summary
/// </summary>
public class LoadTestSummary
{
    public int TotalScenarios { get; set; }
    public int SuccessfulScenarios { get; set; }
    public int FailedScenarios { get; set; }
    
    public int TotalConnections { get; set; }
    public int SuccessfulConnections { get; set; }
    public double OverallConnectionSuccessRate => TotalConnections > 0 ? (double)SuccessfulConnections / TotalConnections : 0;
    
    public int TotalMessages { get; set; }
    public int DeliveredMessages { get; set; }
    public double OverallMessageDeliveryRate => TotalMessages > 0 ? (double)DeliveredMessages / TotalMessages : 0;
    
    public double AverageLatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }
    
    public double PeakCpuPercent { get; set; }
    public long PeakMemoryMB { get; set; }
    public int MaxConcurrentUsers { get; set; }
    
    public bool TestPassed { get; set; }
    public List<string> CriticalIssues { get; set; } = new();
}

/// <summary>
/// Validation results against acceptance criteria
/// </summary>
public class ValidationResults
{
    public bool AllCriteriaPass { get; set; }
    
    public ValidationCriterion Users10kConnected { get; set; } = new();
    public ValidationCriterion MessagesUnder100ms { get; set; } = new();
    public ValidationCriterion NoMessagesLost { get; set; } = new();
    public ValidationCriterion SystemScales { get; set; } = new();
    public ValidationCriterion ResourcesWithinLimits { get; set; } = new();
    
    public List<string> FailureReasons { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Individual validation criterion result
/// </summary>
public class ValidationCriterion
{
    public bool Pass { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExpectedValue { get; set; } = string.Empty;
    public string ActualValue { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
}

/// <summary>
/// Real-time test progress information
/// </summary>
public class TestProgress
{
    public string CurrentScenario { get; set; } = string.Empty;
    public int ActiveUsers { get; set; }
    public int TotalMessages { get; set; }
    public double CurrentLatencyMs { get; set; }
    public double CurrentThroughput { get; set; }
    public SystemMetrics CurrentMetrics { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public double ProgressPercent { get; set; }
}