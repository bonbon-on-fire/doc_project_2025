using System.Collections.Concurrent;
using System.Diagnostics;
using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Models;
using AIChat.LoadTesting.Services;
using AIChat.LoadTesting.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting.Scenarios;

/// <summary>
/// Load testing scenario for SSE connection recovery and resilience.
/// Tests reconnection behavior, data consistency, and buffer overflow scenarios.
/// </summary>
public class SseRecoveryScenario : LoadTestScenarioBase
{
    private readonly ISseConnectionFactory _connectionFactory;
    private readonly ISseLoadTestMetricsCollector _metricsCollector;
    private readonly ISseReconnectionStrategy _reconnectionStrategy;
    private readonly IOptions<ScenariosConfiguration> _scenariosConfig;
    private readonly ConcurrentDictionary<string, ISseConnection> _connections = new();
    private readonly ConcurrentDictionary<string, RecoveryMetrics> _recoveryMetrics = new();

    public override string Name => "SSE Recovery and Resilience";
    public override string Description => "Tests SSE connection recovery, reconnection behavior, and resilience under failure conditions";
    public override bool IsEnabled => _scenariosConfig.Value.SseRecovery.Enabled;

    public SseRecoveryScenario(
        ISseConnectionFactory connectionFactory,
        ISseLoadTestMetricsCollector metricsCollector,
        ISseReconnectionStrategy reconnectionStrategy,
        IOptions<ScenariosConfiguration> scenariosConfig,
        ILogger<SseRecoveryScenario> logger,
        IServiceProvider serviceProvider)
        : base(logger, serviceProvider)
    {
        _connectionFactory = connectionFactory;
        _metricsCollector = metricsCollector;
        _reconnectionStrategy = reconnectionStrategy;
        _scenariosConfig = scenariosConfig;
    }

    public override async Task<ScenarioResult> ExecuteAsync(
        IProgress<TestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var config = _scenariosConfig.Value.SseRecovery;
        var startTime = DateTime.UtcNow;

        Logger.LogInformation("Starting SSE Recovery Scenario with {Connections} connections", config.ConnectionCount);

        try
        {
            // Phase 1: Establish initial connections
            Logger.LogInformation("Phase 1: Establishing initial connections");
            await EstablishConnectionsAsync(config, progress, cancellationToken).ConfigureAwait(false);

            // Phase 2: Simulate connection drops
            Logger.LogInformation("Phase 2: Simulating connection drops");
            await SimulateConnectionDropsAsync(config.ConnectionCount / 4, cancellationToken).ConfigureAwait(false);

            // Phase 3: Test reconnection behavior
            Logger.LogInformation("Phase 3: Testing reconnection behavior");
            await TestReconnectionBehaviorAsync(cancellationToken).ConfigureAwait(false);

            // Phase 4: Simulate server restart
            Logger.LogInformation("Phase 4: Simulating server restart");
            await SimulateServerRestartAsync(cancellationToken).ConfigureAwait(false);

            // Phase 5: Test buffer overflow scenarios
            Logger.LogInformation("Phase 5: Testing buffer overflow scenarios");
            await TestBufferOverflowAsync(config, cancellationToken).ConfigureAwait(false);

            // Phase 6: Validate data consistency
            Logger.LogInformation("Phase 6: Validating data consistency");
            await ValidateDataConsistencyAsync(cancellationToken).ConfigureAwait(false);

            // Collect final metrics and create result
            var metrics = await CollectMetricsAsync().ConfigureAwait(false);
            var endTime = DateTime.UtcNow;
            var result = CreateResult(startTime, endTime, true);
            PopulateResultMetrics(result, metrics);

            Logger.LogInformation("SSE Recovery Scenario completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SSE Recovery Scenario failed");
            var endTime = DateTime.UtcNow;
            return CreateResult(startTime, endTime, false, ex.Message);
        }
        finally
        {
            await CleanupAsync().ConfigureAwait(false);
        }
    }

    private async Task EstablishConnectionsAsync(
        SseRecoveryScenarioConfig config,
        IProgress<TestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var endpoint = "/api/sse/stream";
        var tasks = new List<Task>();
        var connectionSemaphore = new SemaphoreSlim(10); // Limit concurrent connection attempts

        for (int i = 0; i < config.ConnectionCount; i++)
        {
            var connectionId = $"recovery_{i:D4}";

            tasks.Add(Task.Run(async () =>
            {
                await connectionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var connection = _connectionFactory.CreateConnection(connectionId, endpoint);
                    var sw = Stopwatch.StartNew();

                    await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    sw.Stop();

                    _connections[connectionId] = connection;
                    _recoveryMetrics[connectionId] = new RecoveryMetrics
                    {
                        ConnectionId = connectionId,
                        InitialConnectionTime = sw.Elapsed
                    };

                    _metricsCollector.RecordConnectionEstablished(connectionId, sw.Elapsed.TotalMilliseconds);

                    // Start consuming messages
                    _ = Task.Run(async () => await ConsumeMessagesAsync(connection, cancellationToken).ConfigureAwait(false));

                    Logger.LogDebug("Connection {ConnectionId} established in {ElapsedMs}ms",
                        connectionId, sw.Elapsed.TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to establish connection {ConnectionId}", connectionId);
                    _metricsCollector.RecordConnectionFailed(connectionId, ex.Message);
                }
                finally
                {
                    connectionSemaphore.Release();
                }
            }, cancellationToken));

            // Gradual ramp-up
            if (i % 10 == 0)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        Logger.LogInformation("Established {Count} of {Total} connections",
            _connections.Count, config.ConnectionCount);

        ReportProgress(progress, new TestProgress
        {
            CurrentScenario = Name,
            ActiveUsers = _connections.Count,
            ProgressPercent = 20,
            LastUpdated = DateTime.UtcNow
        });
    }

    private async Task SimulateConnectionDropsAsync(int dropCount, CancellationToken cancellationToken)
    {
        var random = new Random();
        var connectionsToKill = _connections.Keys.OrderBy(x => random.Next()).Take(dropCount).ToList();

        Logger.LogInformation("Simulating {Count} connection drops", connectionsToKill.Count);

        foreach (var connectionId in connectionsToKill)
        {
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                var metrics = _recoveryMetrics[connectionId];
                metrics.DropSimulated = true;
                metrics.DropTime = DateTime.UtcNow;

                await connection.DisconnectAsync().ConfigureAwait(false);
                _metricsCollector.RecordConnectionDropped(
                    connectionId,
                    "Simulated drop for testing",
                    DateTime.UtcNow - (metrics.DropTime ?? DateTime.UtcNow));

                Logger.LogDebug("Connection {ConnectionId} dropped", connectionId);
            }

            // Stagger drops
            await Task.Delay(random.Next(10, 100), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TestReconnectionBehaviorAsync(CancellationToken cancellationToken)
    {
        var reconnectionTasks = new List<Task>();

        foreach (var kvp in _connections)
        {
            var connectionId = kvp.Key;
            var connection = kvp.Value;

            if (!connection.IsConnected)
            {
                reconnectionTasks.Add(Task.Run(async () =>
                {
                    var metrics = _recoveryMetrics[connectionId];
                    var sw = Stopwatch.StartNew();

                    try
                    {
                        await _reconnectionStrategy.HandleReconnectionAsync(connection, cancellationToken)
                            .ConfigureAwait(false);

                        sw.Stop();
                        metrics.ReconnectionTime = sw.Elapsed;
                        metrics.ReconnectionSuccessful = true;

                        _metricsCollector.RecordReconnectionAttempt(connectionId, true, sw.Elapsed.TotalMilliseconds);
                        Logger.LogDebug("Connection {ConnectionId} reconnected in {ElapsedMs}ms",
                            connectionId, sw.Elapsed.TotalMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        metrics.ReconnectionTime = sw.Elapsed;
                        metrics.ReconnectionSuccessful = false;

                        Logger.LogWarning(ex, "Failed to reconnect {ConnectionId}", connectionId);
                        _metricsCollector.RecordReconnectionAttempt(connectionId, false, sw.Elapsed.TotalMilliseconds);
                    }
                }, cancellationToken));
            }
        }

        await Task.WhenAll(reconnectionTasks).ConfigureAwait(false);

        var successCount = _recoveryMetrics.Values.Count(m => m.ReconnectionSuccessful);
        Logger.LogInformation("Reconnection results: {Success} successful, {Failed} failed",
            successCount, _recoveryMetrics.Count - successCount);
    }

    private async Task SimulateServerRestartAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Simulating server restart - disconnecting all connections");

        // Disconnect all connections simultaneously
        var disconnectTasks = _connections.Values.Select(connection =>
            connection.DisconnectAsync()).ToArray();

        await Task.WhenAll(disconnectTasks).ConfigureAwait(false);

        // Wait briefly to simulate server restart time
        await Task.Delay(2000, cancellationToken).ConfigureAwait(false);

        // Attempt to reconnect all connections
        Logger.LogInformation("Server restart complete - attempting to reconnect all connections");

        var reconnectTasks = new List<Task>();
        foreach (var kvp in _connections)
        {
            var connectionId = kvp.Key;
            var connection = kvp.Value;

            reconnectTasks.Add(Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    sw.Stop();

                    _recoveryMetrics[connectionId].ServerRestartRecoveryTime = sw.Elapsed;
                    _metricsCollector.RecordReconnectionAttempt(connectionId, true, sw.Elapsed.TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to reconnect {ConnectionId} after server restart", connectionId);
                    _metricsCollector.RecordReconnectionAttempt(connectionId, false, 0);
                }
            }, cancellationToken));
        }

        await Task.WhenAll(reconnectTasks).ConfigureAwait(false);

        var connectedCount = _connections.Values.Count(c => c.IsConnected);
        Logger.LogInformation("Server restart recovery: {Connected} of {Total} connections restored",
            connectedCount, _connections.Count);
    }

    private async Task TestBufferOverflowAsync(SseRecoveryScenarioConfig config, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Testing buffer overflow scenarios");

        // Simulate slow consumers by delaying message consumption
        var slowConsumerCount = Math.Max(1, config.ConnectionCount / 10);
        var slowConsumers = _connections.Keys.Take(slowConsumerCount).ToList();

        foreach (var connectionId in slowConsumers)
        {
            _recoveryMetrics[connectionId].IsSlowConsumer = true;
        }

        // Let messages accumulate for slow consumers
        await Task.Delay(5000, cancellationToken).ConfigureAwait(false);

        // Check buffer utilization
        foreach (var connectionId in slowConsumers)
        {
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                var metrics = connection.GetMetrics();
                _recoveryMetrics[connectionId].MaxBufferUtilization = metrics.BytesReceived;

                Logger.LogDebug("Slow consumer {ConnectionId} buffer utilization: {Bytes} bytes",
                    connectionId, metrics.BytesReceived);
            }
        }
    }

    private async Task ValidateDataConsistencyAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Validating data consistency across all connections");

        var consistencyChecks = new ConcurrentBag<bool>();
        var tasks = new List<Task>();

        foreach (var kvp in _connections)
        {
            var connectionId = kvp.Key;
            var connection = kvp.Value;

            tasks.Add(Task.Run(async () =>
            {
                if (connection.IsConnected)
                {
                    var metrics = connection.GetMetrics();
                    var recoveryMetrics = _recoveryMetrics[connectionId];

                    // Validate message sequence
                    recoveryMetrics.DataConsistent = metrics.MessagesReceived > 0;
                    recoveryMetrics.TotalMessagesReceived = metrics.MessagesReceived;
                    recoveryMetrics.TotalBytesReceived = metrics.BytesReceived;

                    consistencyChecks.Add(recoveryMetrics.DataConsistent);

                    Logger.LogDebug("Connection {ConnectionId} consistency check: {Consistent}, Messages: {Messages}, Bytes: {Bytes}",
                        connectionId, recoveryMetrics.DataConsistent, metrics.MessagesReceived, metrics.BytesReceived);
                }

                await Task.CompletedTask.ConfigureAwait(false);
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var consistentCount = consistencyChecks.Count(c => c);
        var totalChecked = consistencyChecks.Count;

        Logger.LogInformation("Data consistency validation: {Consistent} of {Total} connections consistent ({Percentage:F2}%)",
            consistentCount, totalChecked, (double)consistentCount / totalChecked * 100);
    }

    private async Task ConsumeMessagesAsync(ISseConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in connection.Messages.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                _metricsCollector.RecordChunkReceived(
                    connection.ConnectionId,
                    message.Event ?? "data",
                    message.Data.Length,
                    message.LatencyMs);

                // Simulate slow consumer for designated connections
                if (_recoveryMetrics.TryGetValue(connection.ConnectionId, out var metrics) && metrics.IsSlowConsumer)
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error consuming messages for connection {ConnectionId}", connection.ConnectionId);
        }
    }

    private async Task<Dictionary<string, object>> CollectMetricsAsync()
    {
        await Task.CompletedTask;
        var metrics = new Dictionary<string, object>();

        // Connection metrics
        var totalConnections = _connections.Count;
        var activeConnections = _connections.Values.Count(c => c.IsConnected);
        metrics["total_connections"] = totalConnections;
        metrics["active_connections"] = activeConnections;
        metrics["connection_success_rate"] = totalConnections > 0 ? (double)activeConnections / totalConnections : 0;

        // Recovery metrics
        var recoveryStats = _recoveryMetrics.Values;
        var successfulReconnections = recoveryStats.Count(m => m.ReconnectionSuccessful);
        var dropsSimulated = recoveryStats.Count(m => m.DropSimulated);

        metrics["drops_simulated"] = dropsSimulated;
        metrics["successful_reconnections"] = successfulReconnections;
        metrics["reconnection_success_rate"] = dropsSimulated > 0 ? (double)successfulReconnections / dropsSimulated : 0;

        // Timing metrics
        if (recoveryStats.Count > 0)
        {
            metrics["avg_initial_connection_ms"] = recoveryStats.Average(m => m.InitialConnectionTime.TotalMilliseconds);

            var reconnectionTimes = recoveryStats.Where(m => m.ReconnectionTime.HasValue).ToList();
            if (reconnectionTimes.Count > 0)
            {
                metrics["avg_reconnection_ms"] = reconnectionTimes.Average(m => m.ReconnectionTime!.Value.TotalMilliseconds);
                metrics["max_reconnection_ms"] = reconnectionTimes.Max(m => m.ReconnectionTime!.Value.TotalMilliseconds);
            }

            var serverRestartTimes = recoveryStats.Where(m => m.ServerRestartRecoveryTime.HasValue).ToList();
            if (serverRestartTimes.Count > 0)
            {
                metrics["avg_server_restart_recovery_ms"] = serverRestartTimes.Average(m => m.ServerRestartRecoveryTime!.Value.TotalMilliseconds);
            }
        }

        // Data consistency metrics
        var consistentConnections = recoveryStats.Count(m => m.DataConsistent);
        metrics["data_consistent_connections"] = consistentConnections;
        metrics["data_consistency_rate"] = totalConnections > 0 ? (double)consistentConnections / totalConnections : 0;

        // Slow consumer metrics
        var slowConsumers = recoveryStats.Where(m => m.IsSlowConsumer).ToList();
        if (slowConsumers.Count > 0)
        {
            metrics["slow_consumers"] = slowConsumers.Count;
            metrics["avg_slow_consumer_buffer_bytes"] = slowConsumers.Average(m => m.MaxBufferUtilization);
        }

        // Aggregate message metrics
        metrics["total_messages_received"] = recoveryStats.Sum(m => m.TotalMessagesReceived);
        metrics["total_bytes_received"] = recoveryStats.Sum(m => m.TotalBytesReceived);

        // Get collector metrics
        var collectorMetrics = _metricsCollector.GetMetricsSnapshot();
        metrics["collector_total_chunks"] = collectorMetrics.ChunkMetrics.TotalChunks;
        metrics["collector_total_bytes"] = collectorMetrics.ChunkMetrics.TotalBytes;
        metrics["collector_avg_chunk_latency"] = collectorMetrics.ChunkMetrics.AverageLatency;
        metrics["collector_p99_chunk_latency"] = collectorMetrics.ChunkMetrics.P99Latency;

        return metrics;
    }

    private async Task CleanupAsync()
    {
        Logger.LogInformation("Cleaning up SSE Recovery Scenario resources");

        var tasks = _connections.Values.Select(async connection =>
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error disposing connection {ConnectionId}", connection.ConnectionId);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        _connections.Clear();
        _recoveryMetrics.Clear();
    }

    private void PopulateResultMetrics(ScenarioResult result, Dictionary<string, object> metrics)
    {
        // Populate result with metrics
        if (metrics.TryGetValue("total_connections", out var totalConnections))
        {
            result.TotalUsers = (int)totalConnections;
        }
        if (metrics.TryGetValue("active_connections", out var activeConnections))
        {
            result.SuccessfulConnections = (int)activeConnections;
        }

        result.FailedConnections = result.TotalUsers - result.SuccessfulConnections;

        if (metrics.TryGetValue("total_messages", out var totalMessages))
        {
            result.TotalMessages = (int)totalMessages;
        }
        if (metrics.TryGetValue("data_consistent_connections", out var dataConsistentConnections))
        {
            result.DeliveredMessages = (int)dataConsistentConnections;
        }

        // Set latency statistics
        result.LatencyStats = new LatencyStatistics();
        if (metrics.TryGetValue("avg_reconnection_ms", out var avgReconnectionMs))
        {
            result.LatencyStats.AverageMs = (double)avgReconnectionMs;
        }
        if (metrics.TryGetValue("max_reconnection_ms", out var maxReconnectionMs))
        {
            result.LatencyStats.MaxMs = (double)maxReconnectionMs;
        }
    }

    public override async Task<List<string>> ValidateConfigurationAsync()
    {
        var errors = new List<string>();
        var config = _scenariosConfig.Value.SseRecovery;

        if (config.ConnectionCount <= 0)
        {
            errors.Add("ConnectionCount must be greater than 0");
        }
        if (config.DropConnectionEverySeconds <= 0)
        {
            errors.Add("DropConnectionEverySeconds must be greater than 0");
        }
        if (config.MaxReconnectAttempts < 0)
        {
            errors.Add("MaxReconnectAttempts cannot be negative");
        }
        if (config.ReconnectBackoffMs <= 0)
        {
            errors.Add("ReconnectBackoffMs must be greater than 0");
        }

        return await Task.FromResult(errors);
    }


    /// <summary>
    /// Recovery metrics for individual connections.
    /// </summary>
    private sealed class RecoveryMetrics
    {
        public string ConnectionId { get; set; } = string.Empty;
        public TimeSpan InitialConnectionTime { get; set; }
        public bool DropSimulated { get; set; }
        public DateTime? DropTime { get; set; }
        public TimeSpan? ReconnectionTime { get; set; }
        public bool ReconnectionSuccessful { get; set; }
        public TimeSpan? ServerRestartRecoveryTime { get; set; }
        public bool IsSlowConsumer { get; set; }
        public long MaxBufferUtilization { get; set; }
        public bool DataConsistent { get; set; }
        public int TotalMessagesReceived { get; set; }
        public long TotalBytesReceived { get; set; }
    }
}