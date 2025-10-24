using System.Diagnostics;
using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Models;
using AIChat.LoadTesting.Services;
using AIChat.LoadTesting.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting.Scenarios;

/// <summary>
/// Load testing scenario for SSE streaming throughput.
/// Tests the system's ability to handle high-volume data streaming through SSE connections.
/// </summary>
public class SseStreamingScenario : LoadTestScenarioBase
{
    private readonly ISseConnectionManager _connectionManager;
    private readonly ISseLoadTestMetricsCollector _metricsCollector;
    private readonly IOptions<ScenariosConfiguration> _scenariosConfig;
    private readonly IOptions<LoadTestingConfiguration> _loadTestConfig;
    private readonly IOptions<SseConfiguration> _sseConfig;
    private readonly IHttpClientFactory _httpClientFactory;

    public override string Name => "SSE Streaming Throughput Test";
    public override string Description =>
        "Tests SSE streaming performance with sustained high-volume data transfer across multiple connections";
    public override bool IsEnabled => _scenariosConfig.Value.SseStreaming.Enabled;

    public SseStreamingScenario(
        ISseConnectionManager connectionManager,
        ISseLoadTestMetricsCollector metricsCollector,
        IOptions<ScenariosConfiguration> scenariosConfig,
        IOptions<LoadTestingConfiguration> loadTestConfig,
        IOptions<SseConfiguration> sseConfig,
        IHttpClientFactory httpClientFactory,
        ILogger<SseStreamingScenario> logger,
        IServiceProvider serviceProvider
    )
        : base(logger, serviceProvider)
    {
        _connectionManager = connectionManager;
        _metricsCollector = metricsCollector;
        _scenariosConfig = scenariosConfig;
        _loadTestConfig = loadTestConfig;
        _sseConfig = sseConfig;
        _httpClientFactory = httpClientFactory;
    }

    public override async Task<ScenarioResult> ExecuteAsync(
        IProgress<TestProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var config = _scenariosConfig.Value.SseStreaming;
        var startTime = DateTime.UtcNow;

        Logger.LogInformation(
            "Starting SSE Streaming Throughput Test: {Connections} connections for {Duration}s",
            config.ConnectionCount,
            config.TestDurationSeconds
        );

        try
        {
            // Reset metrics
            _metricsCollector.Reset();

            // Phase 1: Establish connections quickly
            Logger.LogInformation("Phase 1: Establishing {Count} connections", config.ConnectionCount);
            await EstablishConnectionsAsync(config.ConnectionCount, progress, cancellationToken);

            // Phase 2: Generate sustained streaming load
            Logger.LogInformation(
                "Phase 2: Generating sustained streaming load for {Duration}s",
                config.TestDurationSeconds
            );
            await GenerateStreamingLoadAsync(
                TimeSpan.FromSeconds(config.TestDurationSeconds),
                config,
                progress,
                cancellationToken
            );

            // Phase 3: Measure and validate throughput
            Logger.LogInformation("Phase 3: Measuring final throughput metrics");
            var throughputMetrics = await MeasureThroughputAsync(cancellationToken);

            // Phase 4: Cleanup
            Logger.LogInformation("Phase 4: Cleaning up connections");
            await _connectionManager.CloseAllConnectionsAsync();

            // Collect final metrics
            var metrics = _metricsCollector.GetMetricsSnapshot();
            var endTime = DateTime.UtcNow;

            // Create result
            var result = CreateResult(startTime, endTime, true);
            PopulateResultMetrics(result, metrics, config, throughputMetrics);

            Logger.LogInformation(
                "SSE Streaming Test completed. " +
                "Throughput: {ChunksPerSec} chunks/s, {BytesPerSec} bytes/s, " +
                "Avg Latency: {AvgLatency}ms, P99: {P99}ms",
                metrics.ChunkMetrics.ChunksPerSecond,
                metrics.ChunkMetrics.BytesPerSecond,
                metrics.ChunkMetrics.AverageLatency,
                metrics.ChunkMetrics.P99Latency
            );

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SSE Streaming Throughput Test failed");
            var endTime = DateTime.UtcNow;
            return CreateResult(startTime, endTime, false, ex.Message);
        }
        finally
        {
            await _connectionManager.CloseAllConnectionsAsync();
        }
    }

    private async Task EstablishConnectionsAsync(
        int connectionCount,
        IProgress<TestProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task>();

        // Create connections in parallel
        for (int i = 0; i < connectionCount; i++)
        {
            var connectionId = $"sse-stream-{i + 1}";
            tasks.Add(EstablishStreamingConnectionAsync(connectionId, cancellationToken));

            // Report progress periodically
            if (i % 10 == 0)
            {
                ReportProgress(
                    progress,
                    new TestProgress
                    {
                        CurrentScenario = Name,
                        ActiveUsers = i,
                        ProgressPercent = (double)i / connectionCount * 100,
                        LastUpdated = DateTime.UtcNow,
                    }
                );
            }
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        Logger.LogInformation(
            "Established {Count} connections in {Duration}s",
            _connectionManager.ActiveConnectionCount,
            stopwatch.Elapsed.TotalSeconds
        );
    }

    private async Task EstablishStreamingConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken
    )
    {
        var config = _scenariosConfig.Value.SseStreaming;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var connection = await _connectionManager.CreateConnectionAsync(
                connectionId,
                _sseConfig.Value.StreamEndpoint,
                cancellationToken
            );

            stopwatch.Stop();
            _metricsCollector.RecordConnectionEstablished(
                connectionId,
                stopwatch.ElapsedMilliseconds
            );

            // Start high-throughput message consumption
            _ = Task.Run(async () =>
            {
                await ConsumeStreamAsync(connection, config.BufferSizeKB * 1024, cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordConnectionFailed(connectionId, ex.Message);
            Logger.LogWarning(ex, "Failed to establish streaming connection {ConnectionId}", connectionId);
        }
    }

    private async Task ConsumeStreamAsync(
        ISseConnection connection,
        int bufferSize,
        CancellationToken cancellationToken
    )
    {
        var buffer = new Queue<SseMessage>(bufferSize / 100); // Approximate message count
        var lastBufferCheck = DateTime.UtcNow;

        try
        {
            await foreach (
                var message in connection.Messages.ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                // Add to buffer
                buffer.Enqueue(message);

                // Record metrics
                _metricsCollector.RecordChunkReceived(
                    connection.ConnectionId,
                    message.Event,
                    message.Data.Length,
                    message.LatencyMs
                );

                // Check buffer utilization periodically
                if ((DateTime.UtcNow - lastBufferCheck).TotalSeconds >= 1)
                {
                    var bufferUtilization = buffer.Count * 100; // Approximate bytes
                    _metricsCollector.RecordBufferMetrics(
                        connection.ConnectionId,
                        bufferUtilization,
                        bufferSize
                    );
                    lastBufferCheck = DateTime.UtcNow;
                }

                // Simulate processing to maintain realistic buffer levels
                if (buffer.Count > 100)
                {
                    // Process batch
                    for (int i = 0; i < Math.Min(50, buffer.Count); i++)
                    {
                        _ = buffer.Dequeue();
                    }

                    // Simulate processing time
                    await Task.Delay(5, cancellationToken);
                }
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
                "Error consuming stream from connection {ConnectionId}",
                connection.ConnectionId
            );
        }
    }

    private async Task GenerateStreamingLoadAsync(
        TimeSpan duration,
        SseStreamingScenarioConfig config,
        IProgress<TestProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var reportInterval = TimeSpan.FromSeconds(5);
        var lastReport = TimeSpan.Zero;
        var httpClient = _httpClientFactory.CreateClient("SSE");

        // Generate additional load by triggering server-side streaming
        var loadGeneratorTask = Task.Run(async () =>
        {
            while (stopwatch.Elapsed < duration && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Trigger server to generate chunks
                    var triggerUrl = $"{_loadTestConfig.Value.ServerBaseUrl}/api/test/generate-sse-load";
                    var content = new StringContent(
                        $"{{\"chunksPerSecond\": {config.ChunksPerSecond}, \"chunkSize\": {config.ChunkSizeBytes}}}",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    );

                    using var response = await httpClient.PostAsync(triggerUrl, content, cancellationToken);
                    // Don't wait for response, just trigger
                }
                catch
                {
                    // Ignore errors from load generation triggers
                }

                await Task.Delay(1000, cancellationToken);
            }
        }, cancellationToken);

        // Monitor and report progress
        while (stopwatch.Elapsed < duration && !cancellationToken.IsCancellationRequested)
        {
            if (stopwatch.Elapsed - lastReport >= reportInterval)
            {
                var metrics = _metricsCollector.GetMetricsSnapshot();

                ReportProgress(
                    progress,
                    new TestProgress
                    {
                        CurrentScenario = Name,
                        ActiveUsers = _connectionManager.ActiveConnectionCount,
                        TotalMessages = (int)metrics.ChunkMetrics.TotalChunks,
                        CurrentLatencyMs = metrics.ChunkMetrics.AverageLatency,
                        CurrentThroughput = metrics.ChunkMetrics.ChunksPerSecond,
                        ProgressPercent = stopwatch.Elapsed.TotalSeconds / duration.TotalSeconds * 100,
                        LastUpdated = DateTime.UtcNow,
                    }
                );

                Logger.LogDebug(
                    "Streaming progress: {Chunks} chunks, {Bytes} bytes, {ChunksPerSec} chunks/s",
                    metrics.ChunkMetrics.TotalChunks,
                    metrics.ChunkMetrics.TotalBytes,
                    metrics.ChunkMetrics.ChunksPerSecond
                );

                lastReport = stopwatch.Elapsed;
            }

            await Task.Delay(1000, cancellationToken);
        }

        await loadGeneratorTask;
        stopwatch.Stop();

        Logger.LogInformation(
            "Streaming load generation complete. Duration: {Duration}s",
            stopwatch.Elapsed.TotalSeconds
        );
    }

    private async Task<ThroughputMetrics> MeasureThroughputAsync(
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var initialMetrics = _metricsCollector.GetMetricsSnapshot();

        // Measure for 5 seconds to get accurate throughput
        await Task.Delay(5000, cancellationToken);

        var finalMetrics = _metricsCollector.GetMetricsSnapshot();
        stopwatch.Stop();

        var chunksDelta = finalMetrics.ChunkMetrics.TotalChunks - initialMetrics.ChunkMetrics.TotalChunks;
        var bytesDelta = finalMetrics.ChunkMetrics.TotalBytes - initialMetrics.ChunkMetrics.TotalBytes;
        var timeDelta = stopwatch.Elapsed.TotalSeconds;

        return new ThroughputMetrics
        {
            ChunksPerSecond = chunksDelta / timeDelta,
            BytesPerSecond = bytesDelta / timeDelta,
            MeasurementDuration = stopwatch.Elapsed,
        };
    }

    private static void PopulateResultMetrics(
        ScenarioResult result,
        SseLoadTestMetrics metrics,
        SseStreamingScenarioConfig config,
        ThroughputMetrics throughputMetrics
    )
    {
        result.TotalUsers = config.ConnectionCount;
        result.SuccessfulConnections = (int)metrics.ConnectionMetrics.Successful;
        result.FailedConnections = (int)metrics.ConnectionMetrics.Failed;
        result.TotalMessages = (int)metrics.ChunkMetrics.TotalChunks;
        result.DeliveredMessages = (int)metrics.ChunkMetrics.TotalChunks;

        result.LatencyStats = new LatencyStatistics
        {
            AverageMs = metrics.ChunkMetrics.AverageLatency,
            MedianMs = metrics.ChunkMetrics.AverageLatency, // Approximate
            P95Ms = metrics.ChunkMetrics.P95Latency,
            P99Ms = metrics.ChunkMetrics.P99Latency,
            MaxMs = metrics.ChunkMetrics.MaxLatency,
            MinMs = 0,
            SampleCount = (int)metrics.ChunkMetrics.TotalChunks,
        };

        result.ThroughputStats = new ThroughputStatistics
        {
            ConnectionsPerSecond = metrics.ConnectionMetrics.ConnectionsPerSecond,
            MessagesPerSecond = throughputMetrics.ChunksPerSecond,
            PeakConnectionsPerSecond = metrics.ConnectionMetrics.ConnectionsPerSecond,
            PeakMessagesPerSecond = metrics.ChunkMetrics.ChunksPerSecond * 1.2, // Estimate peak
            AverageResponseTime = metrics.ChunkMetrics.AverageLatency,
        };

        // Add throughput details to errors for visibility
        result.Errors.Add($"Throughput achieved: {throughputMetrics.ChunksPerSecond:F2} chunks/s");
        result.Errors.Add($"Data rate: {throughputMetrics.BytesPerSecond / 1024:F2} KB/s");
        result.Errors.Add($"Total data transferred: {metrics.ChunkMetrics.TotalBytes / (1024 * 1024):F2} MB");

        // Report chunk type distribution
        foreach (var chunkType in metrics.ChunkMetrics.ChunkTypeBreakdown)
        {
            result.Errors.Add(
                $"Chunk type '{chunkType.Type}': {chunkType.Count} chunks, " +
                $"{chunkType.TotalBytes / 1024:F2} KB, " +
                $"Avg latency: {chunkType.AverageLatency:F2}ms"
            );
        }

        // Report buffer issues
        if (metrics.BufferMetrics.TotalOverflows > 0)
        {
            result.Errors.Add($"Buffer overflows: {metrics.BufferMetrics.TotalOverflows}");
            result.Success = false;
            result.FailureReason = "Buffer overflows detected during streaming";
        }
    }

    public override Task<List<string>> ValidateConfigurationAsync()
    {
        var errors = new List<string>();
        var config = _scenariosConfig.Value.SseStreaming;

        if (config.ConnectionCount <= 0)
        {
            errors.Add("ConnectionCount must be greater than 0");
        }

        if (config.TestDurationSeconds <= 0)
        {
            errors.Add("TestDurationSeconds must be greater than 0");
        }

        if (config.ChunkSizeBytes <= 0)
        {
            errors.Add("ChunkSizeBytes must be greater than 0");
        }

        if (config.ChunksPerSecond <= 0)
        {
            errors.Add("ChunksPerSecond must be greater than 0");
        }

        if (config.BufferSizeKB <= 0)
        {
            errors.Add("BufferSizeKB must be greater than 0");
        }

        // Validate throughput feasibility
        var expectedThroughput = config.ConnectionCount * config.ChunksPerSecond * config.ChunkSizeBytes;
        if (expectedThroughput > 100 * 1024 * 1024) // 100 MB/s warning threshold
        {
            errors.Add(
                $"Expected throughput ({expectedThroughput / (1024 * 1024)} MB/s) may exceed network capacity"
            );
        }

        return Task.FromResult(errors);
    }

    private sealed class ThroughputMetrics
    {
        public double ChunksPerSecond { get; set; }
        public double BytesPerSecond { get; set; }
        public TimeSpan MeasurementDuration { get; set; }
    }
}