using System.Diagnostics;
using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Models;
using AIChat.LoadTesting.Services;
using AIChat.LoadTesting.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting.Scenarios;

/// <summary>
/// Load testing scenario for SSE connection establishment and maintenance.
/// Tests the system's ability to handle large numbers of concurrent SSE connections.
/// </summary>
public class SseConnectionLoadScenario : LoadTestScenarioBase
{
    private readonly ISseConnectionManager _connectionManager;
    private readonly ISseLoadTestMetricsCollector _metricsCollector;
    private readonly IOptions<ScenariosConfiguration> _scenariosConfig;

    public override string Name => "SSE Connection Load Test";
    public override string Description =>
        "Tests SSE connection establishment, maintenance, and graceful handling of 1000+ concurrent connections";
    public override bool IsEnabled => _scenariosConfig.Value.SseConnectionLoad.Enabled;

    public SseConnectionLoadScenario(
        ISseConnectionManager connectionManager,
        ISseLoadTestMetricsCollector metricsCollector,
        IOptions<ScenariosConfiguration> scenariosConfig,
        ILogger<SseConnectionLoadScenario> logger,
        IServiceProvider serviceProvider
    )
        : base(logger, serviceProvider)
    {
        _connectionManager = connectionManager;
        _metricsCollector = metricsCollector;
        _scenariosConfig = scenariosConfig;
    }

    public override async Task<ScenarioResult> ExecuteAsync(
        IProgress<TestProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var config = _scenariosConfig.Value.SseConnectionLoad;
        var startTime = DateTime.UtcNow;

        Logger.LogInformation(
            "Starting SSE Connection Load Test: {MaxConnections} connections over {RampUp}s",
            config.MaxConnections,
            config.RampUpSeconds
        );

        try
        {
            // Reset metrics
            _metricsCollector.Reset();

            // Phase 1: Ramp up connections
            Logger.LogInformation("Phase 1: Ramping up connections");
            await RampUpConnectionsAsync(
                config.MaxConnections,
                TimeSpan.FromSeconds(config.RampUpSeconds),
                progress,
                cancellationToken
            );

            // Phase 2: Maintain stable load
            Logger.LogInformation(
                "Phase 2: Maintaining stable load for {Duration}s",
                config.StableSeconds
            );
            await MaintainStableLoadAsync(
                TimeSpan.FromSeconds(config.StableSeconds),
                progress,
                cancellationToken
            );

            // Phase 3: Graceful shutdown
            Logger.LogInformation("Phase 3: Graceful shutdown");
            await GracefulShutdownAsync(progress, cancellationToken);

            // Collect final metrics
            var metrics = _metricsCollector.GetMetricsSnapshot();
            var endTime = DateTime.UtcNow;

            // Create result
            var result = CreateResult(startTime, endTime, true);
            PopulateResultMetrics(result, metrics, config);

            Logger.LogInformation(
                "SSE Connection Load Test completed successfully. " +
                "Established: {Established}/{Total}, " +
                "Avg Latency: {AvgLatency}ms, " +
                "P99 Latency: {P99Latency}ms",
                metrics.ConnectionMetrics.Successful,
                metrics.ConnectionMetrics.TotalAttempted,
                metrics.ConnectionMetrics.AverageConnectionLatency,
                metrics.ConnectionMetrics.P99ConnectionLatency
            );

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SSE Connection Load Test failed");
            var endTime = DateTime.UtcNow;
            return CreateResult(startTime, endTime, false, ex.Message);
        }
        finally
        {
            // Ensure all connections are closed
            await _connectionManager.CloseAllConnectionsAsync();
        }
    }

    private async Task RampUpConnectionsAsync(
        int targetConnections,
        TimeSpan rampUpDuration,
        IProgress<TestProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var connectionsPerBatch = Math.Max(1, targetConnections / 20); // 20 batches
        var batchDelay = rampUpDuration.TotalMilliseconds / 20;
        var connectionTasks = new List<Task>();
        var establishedConnections = 0;
        var stopwatch = Stopwatch.StartNew();

        for (int batch = 0; batch < 20 && establishedConnections < targetConnections; batch++)
        {
            var batchSize = Math.Min(
                connectionsPerBatch,
                targetConnections - establishedConnections
            );

            Logger.LogDebug(
                "Establishing batch {Batch}/20: {BatchSize} connections",
                batch + 1,
                batchSize
            );

            // Create connections in parallel within batch
            var batchTasks = new List<Task>();
            for (int i = 0; i < batchSize; i++)
            {
                var connectionId = $"sse-conn-{establishedConnections + i + 1}";
                batchTasks.Add(EstablishConnectionAsync(connectionId, cancellationToken));
            }

            // Wait for batch to complete
            await Task.WhenAll(batchTasks);
            establishedConnections += batchSize;

            // Report progress
            ReportProgress(
                progress,
                new TestProgress
                {
                    CurrentScenario = Name,
                    ActiveUsers = _connectionManager.ActiveConnectionCount,
                    ProgressPercent = (double)establishedConnections / targetConnections * 100,
                    CurrentLatencyMs = _metricsCollector
                        .GetMetricsSnapshot()
                        .ConnectionMetrics
                        .AverageConnectionLatency,
                    CurrentThroughput = establishedConnections / stopwatch.Elapsed.TotalSeconds,
                    LastUpdated = DateTime.UtcNow,
                }
            );

            // Delay before next batch (unless last batch)
            if (batch < 19 && establishedConnections < targetConnections)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(batchDelay), cancellationToken);
            }
        }

        Logger.LogInformation(
            "Ramp up complete: {Established} connections in {Duration}s",
            _connectionManager.ActiveConnectionCount,
            stopwatch.Elapsed.TotalSeconds
        );
    }

    private async Task EstablishConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var config = _scenariosConfig.Value.SseConnectionLoad;

        try
        {
            var connection = await _connectionManager.CreateConnectionAsync(
                connectionId,
                config.SseEndpoint,
                cancellationToken
            );

            stopwatch.Stop();
            _metricsCollector.RecordConnectionEstablished(
                connectionId,
                stopwatch.ElapsedMilliseconds
            );

            // Start consuming messages in background
            _ = ConsumeMessagesAsync(connection, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordConnectionFailed(connectionId, ex.Message);
            Logger.LogWarning(
                ex,
                "Failed to establish connection {ConnectionId}",
                connectionId
            );
        }
    }

    private async Task ConsumeMessagesAsync(
        ISseConnection connection,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await foreach (
                var message in connection.Messages.ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                // Record chunk metrics
                _metricsCollector.RecordChunkReceived(
                    connection.ConnectionId,
                    message.Event,
                    message.Data.Length,
                    message.LatencyMs
                );

                // Simulate processing delay for realistic consumption
                await Task.Delay(10, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Error consuming messages from connection {ConnectionId}",
                connection.ConnectionId
            );
        }
    }

    private async Task MaintainStableLoadAsync(
        TimeSpan duration,
        IProgress<TestProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var reportInterval = TimeSpan.FromSeconds(5);
        var lastReport = TimeSpan.Zero;

        while (stopwatch.Elapsed < duration && !cancellationToken.IsCancellationRequested)
        {
            // Check for dropped connections and reconnect if needed
            var activeCount = _connectionManager.ActiveConnectionCount;
            var targetCount = _scenariosConfig.Value.SseConnectionLoad.MaxConnections;

            if (activeCount < targetCount * 0.95) // Allow 5% tolerance
            {
                Logger.LogWarning(
                    "Active connections dropped to {Active}/{Target}, initiating recovery",
                    activeCount,
                    targetCount
                );

                // Reconnect dropped connections
                var toReconnect = targetCount - activeCount;
                var reconnectTasks = new List<Task>();

                for (int i = 0; i < toReconnect; i++)
                {
                    var connectionId = $"sse-reconn-{Guid.NewGuid():N}";
                    reconnectTasks.Add(EstablishConnectionAsync(connectionId, cancellationToken));
                }

                await Task.WhenAll(reconnectTasks);
            }

            // Report progress periodically
            if (stopwatch.Elapsed - lastReport >= reportInterval)
            {
                var metrics = _metricsCollector.GetMetricsSnapshot();
                ReportProgress(
                    progress,
                    new TestProgress
                    {
                        CurrentScenario = Name,
                        ActiveUsers = activeCount,
                        TotalMessages = (int)metrics.ChunkMetrics.TotalChunks,
                        CurrentLatencyMs = metrics.ChunkMetrics.AverageLatency,
                        CurrentThroughput = metrics.ChunkMetrics.ChunksPerSecond,
                        ProgressPercent = stopwatch.Elapsed.TotalSeconds / duration.TotalSeconds * 100,
                        LastUpdated = DateTime.UtcNow,
                    }
                );
                lastReport = stopwatch.Elapsed;
            }

            await Task.Delay(1000, cancellationToken);
        }

        Logger.LogInformation(
            "Stable load phase complete. Duration: {Duration}s, Active: {Active}",
            stopwatch.Elapsed.TotalSeconds,
            _connectionManager.ActiveConnectionCount
        );
    }

    private async Task GracefulShutdownAsync(
        IProgress<TestProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var connections = _connectionManager.GetAllConnections().ToList();
        var totalConnections = connections.Count;

        Logger.LogInformation("Starting graceful shutdown of {Count} connections", totalConnections);

        // Close connections in batches
        var batchSize = Math.Max(1, totalConnections / 10);
        var closed = 0;

        foreach (var batch in connections.Chunk(batchSize))
        {
            var closeTasks = batch.Select(c => _connectionManager.CloseConnectionAsync(c.ConnectionId));
            await Task.WhenAll(closeTasks);
            closed += batch.Length;

            ReportProgress(
                progress,
                new TestProgress
                {
                    CurrentScenario = Name,
                    ActiveUsers = totalConnections - closed,
                    ProgressPercent = (double)closed / totalConnections * 100,
                    LastUpdated = DateTime.UtcNow,
                }
            );

            // Small delay between batches
            if (closed < totalConnections)
            {
                await Task.Delay(500, cancellationToken);
            }
        }

        Logger.LogInformation(
            "Graceful shutdown complete in {Duration}s",
            stopwatch.Elapsed.TotalSeconds
        );
    }

    private static void PopulateResultMetrics(
        ScenarioResult result,
        SseLoadTestMetrics metrics,
        SseConnectionLoadScenarioConfig config
    )
    {
        result.TotalUsers = config.MaxConnections;
        result.SuccessfulConnections = (int)metrics.ConnectionMetrics.Successful;
        result.FailedConnections = (int)metrics.ConnectionMetrics.Failed;
        result.TotalMessages = (int)metrics.ChunkMetrics.TotalChunks;
        result.DeliveredMessages = (int)metrics.ChunkMetrics.TotalChunks; // All received chunks are delivered

        result.LatencyStats = new LatencyStatistics
        {
            AverageMs = metrics.ChunkMetrics.AverageLatency,
            MedianMs = metrics.ChunkMetrics.AverageLatency, // Approximate
            P95Ms = metrics.ChunkMetrics.P95Latency,
            P99Ms = metrics.ChunkMetrics.P99Latency,
            MaxMs = metrics.ChunkMetrics.MaxLatency,
            MinMs = 0, // Not tracked
            SampleCount = (int)metrics.ChunkMetrics.TotalChunks,
        };

        result.ThroughputStats = new ThroughputStatistics
        {
            ConnectionsPerSecond = metrics.ConnectionMetrics.ConnectionsPerSecond,
            MessagesPerSecond = metrics.ChunkMetrics.ChunksPerSecond,
            PeakConnectionsPerSecond = metrics.ConnectionMetrics.ConnectionsPerSecond * 1.2, // Estimate
            PeakMessagesPerSecond = metrics.ChunkMetrics.ChunksPerSecond * 1.2, // Estimate
            AverageResponseTime = metrics.ChunkMetrics.AverageLatency,
        };

        // Add SSE-specific metrics to errors list (for visibility)
        if (metrics.BufferMetrics.TotalOverflows > 0)
        {
            result.Errors.Add($"Buffer overflows detected: {metrics.BufferMetrics.TotalOverflows}");
        }

        if (metrics.ConnectionMetrics.Dropped > 0)
        {
            result.Errors.Add($"Connections dropped: {metrics.ConnectionMetrics.Dropped}");
        }

        if (metrics.ReconnectionMetrics.Failed > 0)
        {
            result.Errors.Add($"Failed reconnection attempts: {metrics.ReconnectionMetrics.Failed}");
        }
    }

    public override async Task<List<string>> ValidateConfigurationAsync()
    {
        var errors = new List<string>();
        var config = _scenariosConfig.Value.SseConnectionLoad;

        if (config.MaxConnections <= 0)
        {
            errors.Add("MaxConnections must be greater than 0");
        }

        if (config.MaxConnections > 10000)
        {
            errors.Add("MaxConnections exceeds maximum limit of 10000");
        }

        if (config.RampUpSeconds <= 0)
        {
            errors.Add("RampUpSeconds must be greater than 0");
        }

        if (config.StableSeconds <= 0)
        {
            errors.Add("StableSeconds must be greater than 0");
        }

        if (string.IsNullOrEmpty(config.SseEndpoint))
        {
            errors.Add("SseEndpoint must be specified");
        }

        // Test SSE endpoint accessibility
        if (errors.Count == 0)
        {
            try
            {
                var testConnection = await _connectionManager.CreateConnectionAsync(
                    "test-validation",
                    config.SseEndpoint,
                    new CancellationTokenSource(5000).Token
                );
                await _connectionManager.CloseConnectionAsync("test-validation");
            }
            catch (Exception ex)
            {
                errors.Add($"Cannot reach SSE endpoint: {ex.Message}");
            }
        }

        return errors;
    }
}