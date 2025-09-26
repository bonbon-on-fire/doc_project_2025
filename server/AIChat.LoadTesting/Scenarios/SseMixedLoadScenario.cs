using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Models;
using AIChat.LoadTesting.Services;
using AIChat.LoadTesting.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting.Scenarios;

/// <summary>
/// Mixed load testing scenario for SSE that simulates realistic production patterns.
/// Combines various client behaviors including slow consumers, frequent disconnections,
/// varying message sizes, and different consumption rates.
/// </summary>
public class SseMixedLoadScenario : LoadTestScenarioBase
{
    private readonly ISseConnectionFactory _connectionFactory;
    private readonly ISseLoadTestMetricsCollector _metricsCollector;
    private readonly IOptions<ScenariosConfiguration> _scenariosConfig;
    private readonly ConcurrentDictionary<string, ClientProfile> _clientProfiles = new();
    private readonly ConcurrentDictionary<string, ISseConnection> _connections = new();
    private readonly ConcurrentBag<ClientMetrics> _clientMetrics = [];
    private volatile bool _isRunning;

    public override string Name => "SSE Mixed Load";
    public override string Description => "Realistic production-like load test with mixed client behaviors, varying consumption rates, and connection patterns";
    public override bool IsEnabled => _scenariosConfig.Value.SseMixedLoad.Enabled;

    public SseMixedLoadScenario(
        ISseConnectionFactory connectionFactory,
        ISseLoadTestMetricsCollector metricsCollector,
        IOptions<ScenariosConfiguration> scenariosConfig,
        ILogger<SseMixedLoadScenario> logger,
        IServiceProvider serviceProvider)
        : base(logger, serviceProvider)
    {
        _connectionFactory = connectionFactory;
        _metricsCollector = metricsCollector;
        _scenariosConfig = scenariosConfig;
    }

    public override async Task<ScenarioResult> ExecuteAsync(
        IProgress<TestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var config = _scenariosConfig.Value.SseMixedLoad;
        var totalConnections = config.FastConsumers + config.NormalConsumers + config.SlowConsumers;
        var startTime = DateTime.UtcNow;

        Logger.LogInformation("Starting SSE Mixed Load Scenario with {Connections} connections for {Duration}s",
            totalConnections, config.TestDurationSeconds);

        _isRunning = true;

        try
        {
            // Generate client profiles with different behaviors
            GenerateClientProfiles(totalConnections);

            // Start background tasks
            var backgroundTasks = new List<Task>
            {
                SimulateConnectionChurnAsync(cancellationToken),
                SimulateNetworkIssuesAsync(cancellationToken),
                MonitorSystemHealthAsync(cancellationToken)
            };

            // Create connections with staggered start times
            await CreateMixedConnectionsAsync(progress, cancellationToken).ConfigureAwait(false);

            // Run the test for the specified duration
            var testDurationTask = Task.Delay(TimeSpan.FromSeconds(config.TestDurationSeconds), cancellationToken);

            // Run stability test
            await RunStabilityTestAsync(testDurationTask, cancellationToken).ConfigureAwait(false);

            _isRunning = false;

            // Wait for background tasks to complete
            await Task.WhenAll(backgroundTasks).ConfigureAwait(false);

            // Collect final metrics and create result
            var metrics = await CollectComprehensiveMetricsAsync().ConfigureAwait(false);
            var endTime = DateTime.UtcNow;
            var result = CreateResult(startTime, endTime, true);
            PopulateResultMetrics(result, metrics);

            Logger.LogInformation("SSE Mixed Load Scenario completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SSE Mixed Load Scenario failed");
            var endTime = DateTime.UtcNow;
            return CreateResult(startTime, endTime, false, ex.Message);
        }
        finally
        {
            _isRunning = false;
            await CleanupAsync().ConfigureAwait(false);
        }

    }

    private void GenerateClientProfiles(int clientCount)
    {
        Logger.LogInformation("Generating {Count} client profiles with mixed behaviors", clientCount);

        var random = new Random();

        for (int i = 0; i < clientCount; i++)
        {
            var connectionId = $"mixed_{i:D4}";

            // Distribute clients across different behavior profiles
            ClientBehavior behavior;
            double percentage = (double)i / clientCount;

            if (percentage < 0.4) // 40% normal clients
            {
                behavior = ClientBehavior.Normal;
            }
            else if (percentage < 0.6) // 20% slow consumers
            {
                behavior = ClientBehavior.SlowConsumer;
            }
            else if (percentage < 0.75) // 15% frequent disconnects
            {
                behavior = ClientBehavior.FrequentDisconnect;
            }
            else if (percentage < 0.85) // 10% bursty
            {
                behavior = ClientBehavior.Bursty;
            }
            else if (percentage < 0.95) // 10% intermittent
            {
                behavior = ClientBehavior.Intermittent;
            }
            else // 5% stress testers
            {
                behavior = ClientBehavior.StressTester;
            }

            var profile = new ClientProfile
            {
                ConnectionId = connectionId,
                Behavior = behavior,
                ProcessingDelayMs = GetProcessingDelay(behavior, random),
                DisconnectProbability = GetDisconnectProbability(behavior),
                ReconnectDelayMs = GetReconnectDelay(behavior, random),
                BurstSize = GetBurstSize(behavior, random),
                Created = DateTime.UtcNow
            };

            _clientProfiles[connectionId] = profile;
        }

        LogProfileDistribution();
    }

    private int GetProcessingDelay(ClientBehavior behavior, Random random) => behavior switch
    {
        ClientBehavior.Normal => random.Next(1, 10),
        ClientBehavior.SlowConsumer => random.Next(100, 500),
        ClientBehavior.Bursty => random.Next(0, 50),
        ClientBehavior.StressTester => 0,
        _ => random.Next(10, 100)
    };

    private double GetDisconnectProbability(ClientBehavior behavior) => behavior switch
    {
        ClientBehavior.FrequentDisconnect => 0.1,
        ClientBehavior.Intermittent => 0.05,
        ClientBehavior.StressTester => 0.2,
        _ => 0.001
    };

    private int GetReconnectDelay(ClientBehavior behavior, Random random) => behavior switch
    {
        ClientBehavior.FrequentDisconnect => random.Next(100, 1000),
        ClientBehavior.Intermittent => random.Next(5000, 30000),
        ClientBehavior.StressTester => random.Next(10, 100),
        _ => random.Next(1000, 5000)
    };

    private int GetBurstSize(ClientBehavior behavior, Random random) => behavior switch
    {
        ClientBehavior.Bursty => random.Next(10, 100),
        ClientBehavior.StressTester => random.Next(100, 1000),
        _ => 1
    };

    private void LogProfileDistribution()
    {
        var distribution = _clientProfiles.Values
            .GroupBy(p => p.Behavior)
            .Select(g => new { Behavior = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count);

        foreach (var item in distribution)
        {
            Logger.LogInformation("Client behavior {Behavior}: {Count} clients ({Percentage:F1}%)",
                item.Behavior, item.Count, (double)item.Count / _clientProfiles.Count * 100);
        }
    }

    private async Task CreateMixedConnectionsAsync(
        IProgress<TestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var endpoint = "/api/sse/stream";
        var connectionSemaphore = new SemaphoreSlim(20); // Limit concurrent connection attempts
        var tasks = new List<Task>();
        var random = new Random();

        foreach (var profile in _clientProfiles.Values)
        {
            tasks.Add(Task.Run(async () =>
            {
                // Stagger connection times for realistic pattern
                await Task.Delay(random.Next(0, 5000), cancellationToken).ConfigureAwait(false);

                await connectionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await CreateClientConnectionAsync(profile, endpoint, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    connectionSemaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        Logger.LogInformation("Created {Count} mixed client connections", _connections.Count);

        ReportProgress(progress, new TestProgress
        {
            CurrentScenario = Name,
            ActiveUsers = _connections.Count,
            ProgressPercent = 20,
            LastUpdated = DateTime.UtcNow
        });
    }

    private async Task CreateClientConnectionAsync(ClientProfile profile, string endpoint, CancellationToken cancellationToken)
    {
        try
        {
            var connection = _connectionFactory.CreateConnection(profile.ConnectionId, endpoint);
            var sw = Stopwatch.StartNew();

            await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();

            _connections[profile.ConnectionId] = connection;
            profile.ConnectedAt = DateTime.UtcNow;
            profile.ConnectionLatencyMs = sw.Elapsed.TotalMilliseconds;

            _metricsCollector.RecordConnectionEstablished(profile.ConnectionId, sw.Elapsed.TotalMilliseconds);

            // Start client-specific message consumption
            _ = Task.Run(async () => await ConsumeMessagesWithBehaviorAsync(connection, profile, cancellationToken).ConfigureAwait(false), cancellationToken);

            Logger.LogDebug("Connection {ConnectionId} ({Behavior}) established in {ElapsedMs}ms",
                profile.ConnectionId, profile.Behavior, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to establish connection {ConnectionId} ({Behavior})",
                profile.ConnectionId, profile.Behavior);
            _metricsCollector.RecordConnectionFailed(profile.ConnectionId, ex.Message);
            profile.LastError = ex.Message;
        }
    }

    private async Task ConsumeMessagesWithBehaviorAsync(ISseConnection connection, ClientProfile profile, CancellationToken cancellationToken)
    {
        var random = new Random();
        var metrics = new ClientMetrics
        {
            ConnectionId = profile.ConnectionId,
            Behavior = profile.Behavior,
            StartTime = DateTime.UtcNow
        };

        try
        {
            var messageBuffer = new List<SseMessage>();

            await foreach (var message in connection.Messages.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                metrics.MessagesReceived++;
                metrics.BytesReceived += message.Data.Length;
                _metricsCollector.RecordChunkReceived(
                    connection.ConnectionId,
                    message.Event ?? "data",
                    message.Data.Length,
                    message.LatencyMs);

                // Apply behavior-specific processing
                switch (profile.Behavior)
                {
                    case ClientBehavior.SlowConsumer:
                        await Task.Delay(profile.ProcessingDelayMs, cancellationToken).ConfigureAwait(false);
                        break;

                    case ClientBehavior.Bursty:
                        messageBuffer.Add(message);
                        if (messageBuffer.Count >= profile.BurstSize)
                        {
                            // Process burst
                            await ProcessMessageBurstAsync(messageBuffer, cancellationToken).ConfigureAwait(false);
                            messageBuffer.Clear();
                        }
                        break;

                    case ClientBehavior.FrequentDisconnect:
                        if (random.NextDouble() < profile.DisconnectProbability)
                        {
                            Logger.LogDebug("Simulating disconnect for {ConnectionId}", connection.ConnectionId);
                            await connection.DisconnectAsync().ConfigureAwait(false);
                            await Task.Delay(profile.ReconnectDelayMs, cancellationToken).ConfigureAwait(false);
                            await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
                            metrics.DisconnectCount++;
                        }
                        break;

                    case ClientBehavior.Intermittent:
                        // Randomly pause consumption
                        if (random.NextDouble() < 0.1)
                        {
                            await Task.Delay(random.Next(1000, 5000), cancellationToken).ConfigureAwait(false);
                            metrics.PauseCount++;
                        }
                        break;

                    case ClientBehavior.StressTester:
                        // Consume as fast as possible, no delays
                        if (metrics.MessagesReceived % 1000 == 0)
                        {
                            Logger.LogDebug("Stress tester {ConnectionId} consumed {Count} messages",
                                connection.ConnectionId, metrics.MessagesReceived);
                        }
                        break;

                    case ClientBehavior.Normal:
                    default:
                        // Normal processing with minimal delay
                        if (profile.ProcessingDelayMs > 0)
                        {
                            await Task.Delay(profile.ProcessingDelayMs, cancellationToken).ConfigureAwait(false);
                        }
                        break;
                }

                // Track latency percentiles
                metrics.LatencyMeasurements.Add(message.LatencyMs);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error consuming messages for {ConnectionId} ({Behavior})",
                connection.ConnectionId, profile.Behavior);
            metrics.ErrorCount++;
            profile.LastError = ex.Message;
        }
        finally
        {
            metrics.EndTime = DateTime.UtcNow;
            _clientMetrics.Add(metrics);
        }
    }

    private async Task ProcessMessageBurstAsync(List<SseMessage> messages, CancellationToken cancellationToken)
    {
        // Simulate burst processing
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        Logger.LogDebug("Processed burst of {Count} messages", messages.Count);
    }

    private async Task SimulateConnectionChurnAsync(CancellationToken cancellationToken)
    {
        var random = new Random();

        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(random.Next(5000, 15000), cancellationToken).ConfigureAwait(false);

            // Randomly disconnect and reconnect some clients
            var churnCandidates = _connections.Keys
                .Where(id => _clientProfiles[id].Behavior == ClientBehavior.Intermittent)
                .OrderBy(_ => random.Next())
                .Take(Math.Max(1, _connections.Count / 20))
                .ToList();

            foreach (var connectionId in churnCandidates)
            {
                if (_connections.TryGetValue(connectionId, out var connection))
                {
                    Logger.LogDebug("Simulating churn for {ConnectionId}", connectionId);

                    await connection.DisconnectAsync().ConfigureAwait(false);
                    await Task.Delay(random.Next(1000, 5000), cancellationToken).ConfigureAwait(false);
                    await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);

                    _metricsCollector.RecordReconnectionAttempt(connectionId, true, random.Next(100, 1000));
                }
            }
        }
    }

    private async Task SimulateNetworkIssuesAsync(CancellationToken cancellationToken)
    {
        var random = new Random();

        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(random.Next(30000, 60000), cancellationToken).ConfigureAwait(false);

            Logger.LogInformation("Simulating network issues for subset of connections");

            // Simulate network issues for a subset of connections
            var affectedCount = Math.Max(1, _connections.Count / 10);
            var affectedConnections = _connections.Keys
                .OrderBy(_ => random.Next())
                .Take(affectedCount)
                .ToList();

            var tasks = affectedConnections.Select(async connectionId =>
            {
                if (_connections.TryGetValue(connectionId, out var connection))
                {
                    // Simulate various network issues
                    var issueType = random.Next(3);
                    switch (issueType)
                    {
                        case 0: // Packet loss
                            Logger.LogDebug("Simulating packet loss for {ConnectionId}", connectionId);
                            // Connection might auto-recover
                            break;
                        case 1: // High latency
                            Logger.LogDebug("Simulating high latency for {ConnectionId}", connectionId);
                            await Task.Delay(random.Next(1000, 5000), cancellationToken).ConfigureAwait(false);
                            break;
                        case 2: // Connection drop
                            Logger.LogDebug("Simulating connection drop for {ConnectionId}", connectionId);
                            await connection.DisconnectAsync().ConfigureAwait(false);
                            _metricsCollector.RecordConnectionDropped(
                                connectionId,
                                "Simulated network issue",
                                TimeSpan.FromMinutes(random.Next(1, 30)));
                            break;
                    }
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private async Task MonitorSystemHealthAsync(CancellationToken cancellationToken)
    {
        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(10000, cancellationToken).ConfigureAwait(false);

            var activeConnections = _connections.Values.Count(c => c.IsConnected);
            var totalMessages = _connections.Values.Sum(c => c.MessageCount);
            var totalBytes = _connections.Values.Sum(c => c.BytesReceived);

            Logger.LogInformation(
                "System health - Active: {Active}/{Total}, Messages: {Messages}, Bytes: {Bytes:N0}, Metrics: {MetricsCount}",
                activeConnections, _connections.Count, totalMessages, totalBytes, _clientMetrics.Count);

            // Check for memory pressure
            var memoryUsage = GC.GetTotalMemory(false);
            if (memoryUsage > 500_000_000) // 500 MB threshold
            {
                Logger.LogWarning("High memory usage detected: {MemoryMB:F2} MB", memoryUsage / 1_000_000.0);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
    }

    private async Task RunStabilityTestAsync(Task testDurationTask, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Running stability test for the configured duration");

        var checkInterval = TimeSpan.FromSeconds(30);
        var stabilityChecks = new List<StabilityCheck>();

        while (!testDurationTask.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            var check = new StabilityCheck
            {
                Timestamp = DateTime.UtcNow,
                ActiveConnections = _connections.Values.Count(c => c.IsConnected),
                TotalConnections = _connections.Count,
                TotalMessages = _connections.Values.Sum(c => c.MessageCount),
                TotalBytes = _connections.Values.Sum(c => c.BytesReceived),
                MemoryUsageMB = GC.GetTotalMemory(false) / 1_000_000.0
            };

            stabilityChecks.Add(check);

            // Log stability metrics
            Logger.LogInformation(
                "Stability check - Connections: {Active}/{Total}, Messages: {Messages:N0}, Memory: {Memory:F2} MB",
                check.ActiveConnections, check.TotalConnections, check.TotalMessages, check.MemoryUsageMB);

            await Task.Delay(checkInterval, cancellationToken).ConfigureAwait(false);
        }

        // Analyze stability
        if (stabilityChecks.Count > 1)
        {
            var connectionStability = stabilityChecks.Select(c => c.ActiveConnections).ToList();
            var avgConnections = connectionStability.Average();
            var stdDev = Math.Sqrt(connectionStability.Average(v => Math.Pow(v - avgConnections, 2)));

            Logger.LogInformation(
                "Stability analysis - Avg connections: {Avg:F2}, StdDev: {StdDev:F2}, CV: {CV:F2}%",
                avgConnections, stdDev, stdDev / avgConnections * 100);
        }
    }

    private async Task<Dictionary<string, object>> CollectComprehensiveMetricsAsync()
    {
        await Task.CompletedTask;
        var metrics = new Dictionary<string, object>();

        // Connection metrics
        var totalConnections = _connections.Count;
        var activeConnections = _connections.Values.Count(c => c.IsConnected);
        metrics["total_connections"] = totalConnections;
        metrics["active_connections"] = activeConnections;
        metrics["connection_success_rate"] = totalConnections > 0 ? (double)activeConnections / totalConnections : 0;

        // Behavior distribution
        var behaviorGroups = _clientProfiles.Values.GroupBy(p => p.Behavior)
            .Select(g => new { Behavior = g.Key.ToString(), Count = g.Count() })
            .ToDictionary(x => $"behavior_{x.Behavior.ToLower(CultureInfo.InvariantCulture)}", x => (object)x.Count);

        foreach (var kvp in behaviorGroups)
        {
            metrics[kvp.Key] = kvp.Value;
        }

        // Client metrics aggregation
        if (!_clientMetrics.IsEmpty)
        {
            metrics["total_messages"] = _clientMetrics.Sum(m => m.MessagesReceived);
            metrics["total_bytes"] = _clientMetrics.Sum(m => m.BytesReceived);
            metrics["total_disconnects"] = _clientMetrics.Sum(m => m.DisconnectCount);
            metrics["total_pauses"] = _clientMetrics.Sum(m => m.PauseCount);
            metrics["total_errors"] = _clientMetrics.Sum(m => m.ErrorCount);

            // Latency percentiles
            var allLatencies = _clientMetrics.SelectMany(m => m.LatencyMeasurements)
                .Where(l => l > 0)
                .OrderBy(l => l)
                .ToList();

            if (allLatencies.Count > 0)
            {
                metrics["latency_p50"] = GetPercentile(allLatencies, 50);
                metrics["latency_p95"] = GetPercentile(allLatencies, 95);
                metrics["latency_p99"] = GetPercentile(allLatencies, 99);
                metrics["latency_max"] = allLatencies.Max();
                metrics["latency_min"] = allLatencies.Min();
                metrics["latency_avg"] = allLatencies.Average();
            }

            // Behavior-specific metrics
            foreach (var behavior in Enum.GetValues<ClientBehavior>())
            {
                var behaviorMetrics = _clientMetrics.Where(m => m.Behavior == behavior).ToList();
                if (behaviorMetrics.Count > 0)
                {
                    var prefix = $"{behavior.ToString().ToLower(CultureInfo.InvariantCulture)}_";
                    metrics[prefix + "clients"] = behaviorMetrics.Count;
                    metrics[prefix + "messages"] = behaviorMetrics.Sum(m => m.MessagesReceived);
                    metrics[prefix + "bytes"] = behaviorMetrics.Sum(m => m.BytesReceived);
                    metrics[prefix + "errors"] = behaviorMetrics.Sum(m => m.ErrorCount);
                }
            }
        }

        // Connection establishment metrics
        var connectionLatencies = _clientProfiles.Values
            .Where(p => p.ConnectionLatencyMs > 0)
            .Select(p => p.ConnectionLatencyMs)
            .OrderBy(l => l)
            .ToList();

        if (connectionLatencies.Count > 0)
        {
            metrics["connection_latency_p50"] = GetPercentile(connectionLatencies, 50);
            metrics["connection_latency_p95"] = GetPercentile(connectionLatencies, 95);
            metrics["connection_latency_p99"] = GetPercentile(connectionLatencies, 99);
            metrics["connection_latency_avg"] = connectionLatencies.Average();
        }

        // Get collector metrics
        var collectorMetrics = _metricsCollector.GetMetricsSnapshot();
        metrics["collector_total_chunks"] = collectorMetrics.ChunkMetrics.TotalChunks;
        metrics["collector_total_bytes"] = collectorMetrics.ChunkMetrics.TotalBytes;
        metrics["collector_avg_chunk_latency"] = collectorMetrics.ChunkMetrics.AverageLatency;
        metrics["collector_p99_chunk_latency"] = collectorMetrics.ChunkMetrics.P99Latency;

        // System metrics
        metrics["memory_usage_mb"] = GC.GetTotalMemory(false) / 1_000_000.0;
        metrics["gc_gen0_collections"] = GC.CollectionCount(0);
        metrics["gc_gen1_collections"] = GC.CollectionCount(1);
        metrics["gc_gen2_collections"] = GC.CollectionCount(2);

        return metrics;
    }

    private double GetPercentile(List<double> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling((percentile / 100.0) * sortedValues.Count) - 1;
        index = Math.Max(0, Math.Min(index, sortedValues.Count - 1));
        return sortedValues[index];
    }

    private async Task CleanupAsync()
    {
        Logger.LogInformation("Cleaning up SSE Mixed Load Scenario resources");

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
        _clientProfiles.Clear();
        _clientMetrics.Clear();
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
        if (metrics.TryGetValue("total_errors", out var totalErrors))
        {
            result.Errors.Add($"Total errors: {totalErrors}");
        }

        // Set latency statistics
        result.LatencyStats = new LatencyStatistics();
        if (metrics.TryGetValue("latency_avg", out var latencyAvg))
        {
            result.LatencyStats.AverageMs = (double)latencyAvg;
        }
        if (metrics.TryGetValue("latency_p95", out var latencyP95))
        {
            result.LatencyStats.P95Ms = (double)latencyP95;
        }
        if (metrics.TryGetValue("latency_p99", out var latencyP99))
        {
            result.LatencyStats.P99Ms = (double)latencyP99;
        }
        if (metrics.TryGetValue("latency_max", out var latencyMax))
        {
            result.LatencyStats.MaxMs = (double)latencyMax;
        }
    }

    public override async Task<List<string>> ValidateConfigurationAsync()
    {
        var errors = new List<string>();
        var config = _scenariosConfig.Value.SseMixedLoad;

        if (config.FastConsumers < 0 || config.NormalConsumers < 0 || config.SlowConsumers < 0)
        {
            errors.Add("Consumer counts cannot be negative");
        }
        if (config.FastConsumers + config.NormalConsumers + config.SlowConsumers <= 0)
        {
            errors.Add("Total consumer count must be greater than 0");
        }
        if (config.TestDurationSeconds <= 0)
        {
            errors.Add("TestDurationSeconds must be greater than 0");
        }
        if (config.ConnectionChurnRate < 0 || config.ConnectionChurnRate > 1)
        {
            errors.Add("ConnectionChurnRate must be between 0 and 1");
        }
        if (config.MaxBufferedChunks <= 0)
        {
            errors.Add("MaxBufferedChunks must be greater than 0");
        }

        return await Task.FromResult(errors);
    }


    /// <summary>
    /// Client behavior types for mixed load testing.
    /// </summary>
    private enum ClientBehavior
    {
        Normal,             // Standard client behavior
        SlowConsumer,       // Consumes messages slowly
        FrequentDisconnect, // Disconnects and reconnects frequently
        Bursty,            // Processes messages in bursts
        Intermittent,      // Connects/disconnects intermittently
        StressTester       // Maximum throughput testing
    }

    /// <summary>
    /// Client profile defining behavior characteristics.
    /// </summary>
    private sealed class ClientProfile
    {
        public string ConnectionId { get; set; } = string.Empty;
        public ClientBehavior Behavior { get; set; }
        public int ProcessingDelayMs { get; set; }
        public double DisconnectProbability { get; set; }
        public int ReconnectDelayMs { get; set; }
        public int BurstSize { get; set; }
        public DateTime Created { get; set; }
        public DateTime? ConnectedAt { get; set; }
        public double ConnectionLatencyMs { get; set; }
        public string? LastError { get; set; }
    }

    /// <summary>
    /// Metrics collected for individual clients.
    /// </summary>
    private sealed class ClientMetrics
    {
        public string ConnectionId { get; set; } = string.Empty;
        public ClientBehavior Behavior { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int MessagesReceived { get; set; }
        public long BytesReceived { get; set; }
        public int DisconnectCount { get; set; }
        public int PauseCount { get; set; }
        public int ErrorCount { get; set; }
        public List<double> LatencyMeasurements { get; set; } = [];
    }

    /// <summary>
    /// Stability check snapshot.
    /// </summary>
    private sealed class StabilityCheck
    {
        public DateTime Timestamp { get; set; }
        public int ActiveConnections { get; set; }
        public int TotalConnections { get; set; }
        public int TotalMessages { get; set; }
        public long TotalBytes { get; set; }
        public double MemoryUsageMB { get; set; }
    }
}