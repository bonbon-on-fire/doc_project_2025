using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Comprehensive grain interface for session management in the system.
/// This interface extends all segregated interfaces to provide backward compatibility
/// while supporting the new Interface Segregation Principle-based architecture.
///
/// For new code, prefer using the specific segregated interfaces:
/// - ISessionStateGrain for state management and persistence
/// - ISessionConnectionGrain for connection management and reconnection
/// - ISessionProtocolGrain for protocol-specific operations
/// - ISessionMonitoringGrain for monitoring and diagnostics
/// </summary>
[Alias("AIChat.Orleans.Contracts.ISessionGrain")]
public interface ISessionGrain
    : ISessionStateGrain,
      ISessionConnectionGrain,
      ISessionProtocolGrain,
      ISessionMonitoringGrain
{
    // This interface now inherits all methods from the four segregated interfaces.
    // No additional methods are defined here to maintain clean separation of concerns.
    //
    // Inherited from ISessionStateGrain:
    // - InitializeAsync(SessionInitRequest request)
    // - GetStateAsync()
    // - UpdateMetadataAsync(Dictionary<string, object> metadata)
    // - ArchiveAsync(string? reason)
    // - GetHistoryAsync(int? limit, DateTime? afterTimestamp)
    // - CheckHealthAsync()
    // - SaveSnapshotAsync()
    // - RestoreSnapshotAsync(string snapshotId)
    //
    // Inherited from ISessionConnectionGrain:
    // - ConnectAsync(ConnectionRequest connectionInfo)
    // - DisconnectAsync(string? reason)
    // - ReconnectAsync(ReconnectionRequest request)
    // - GetConnectionStatusAsync()
    // - HeartbeatAsync()
    // - GetConnectionMetricsAsync()
    // - RegisterConnectionAttemptAsync(ConnectionAttempt attempt)
    // - CleanupConnectionAsync(bool force)
    // - UpdateConnectionConfigurationAsync(ConnectionConfiguration configuration)
    //
    // Inherited from ISessionProtocolGrain:
    // - GetProtocolConfigurationAsync()
    // - UpdateProtocolConfigurationAsync(ProtocolConfiguration configuration)
    // - SwitchProtocolAsync(ProtocolSwitchRequest request)
    // - ValidateProtocolAsync(string protocolType)
    // - GetProtocolStateAsync()
    // - UpdateProtocolStateAsync(ProtocolState state)
    // - NegotiateProtocolAsync(ProtocolCapabilities clientCapabilities)
    // - GetAvailableProtocolsAsync()
    // - HandleProtocolMessageAsync(ProtocolMessage message)
    //
    // Inherited from ISessionMonitoringGrain:
    // - GetMetricsAsync()
    // - RecordMetricAsync(string metricName, double value, Dictionary<string, object>? metadata)
    // - GetPerformanceStatsAsync(DateTime startTime, DateTime endTime)
    // - CollectDiagnosticsAsync(DiagnosticLevel diagnosticLevel)
    // - RegisterAlertAsync(AlertConfiguration alert)
    // - GetActiveAlertsAsync()
    // - AcknowledgeAlertAsync(string alertId, string acknowledgedBy)
    // - StartTraceAsync(string traceName)
    // - StopTraceAsync(string traceId)
    // - GetResourceUsageAsync()
}
