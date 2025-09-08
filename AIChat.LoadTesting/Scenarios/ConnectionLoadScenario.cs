using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Models;
using AIChat.LoadTesting.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting.Scenarios;

/// <summary>
/// Tests the ability to establish and maintain 10,000 concurrent SignalR connections
/// </summary>
public class ConnectionLoadScenario : LoadTestScenarioBase
{
    private readonly ConnectionLoadScenarioConfig _config;
    private readonly SignalRConnectionManager _connectionManager;
    private readonly SystemMetricsCollector _metricsCollector;

    public ConnectionLoadScenario(
        ILogger<ConnectionLoadScenario> logger,
        IServiceProvider serviceProvider,
        IOptions<ScenariosConfiguration> scenarios,
        SignalRConnectionManager connectionManager,
        SystemMetricsCollector metricsCollector)
        : base(logger, serviceProvider)
    {
        _config = scenarios.Value.ConnectionLoad;
        _connectionManager = connectionManager;
        _metricsCollector = metricsCollector;
    }

    public override string Name => "Connection Load Test";
    public override string Description => $"Tests establishing and maintaining {_config.MaxUsers:N0} concurrent SignalR connections";
    public override bool IsEnabled => _config.Enabled;

    public override Task<List<string>> ValidateConfigurationAsync()
    {
        var errors = new List<string>();

        if (_config.MaxUsers <= 0)
        {
            errors.Add("MaxUsers must be greater than 0");
        }

        if (_config.MaxUsers > 50000)
        {
            errors.Add("MaxUsers should not exceed 50,000 for safety");
        }

        if (_config.RampUpSeconds <= 0)
        {
            errors.Add("RampUpSeconds must be greater than 0");
        }

        if (_config.StableSeconds <= 0)
        {
            errors.Add("StableSeconds must be greater than 0");
        }

        if (_config.ConnectionTimeoutSeconds <= 0)
        {
            errors.Add("ConnectionTimeoutSeconds must be greater than 0");
        }

        return Task.FromResult(errors);
    }

    public override async Task<ScenarioResult> ExecuteAsync(IProgress<TestProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("Starting Connection Load Test: {MaxUsers:N0} users, {RampUpSeconds}s ramp-up, {StableSeconds}s stable",
            _config.MaxUsers, _config.RampUpSeconds, _config.StableSeconds);

        try
        {
            // Phase 1: Ramp up connections
            _logger.LogInformation("Phase 1: Ramping up {MaxUsers:N0} connections over {RampUpSeconds} seconds", _config.MaxUsers, _config.RampUpSeconds);
            var rampUpResult = await RampUpConnectionsAsync(progress, cancellationToken);

            if (!rampUpResult.Success)
            {
                return CreateResult(startTime, DateTime.UtcNow, false, rampUpResult.FailureReason);
            }

            // Phase 2: Maintain stable connections
            _logger.LogInformation("Phase 2: Maintaining connections for {StableSeconds} seconds", _config.StableSeconds);
            var stableResult = await MaintainStableConnectionsAsync(progress, cancellationToken);

            if (!stableResult.Success)
            {
                return CreateResult(startTime, DateTime.UtcNow, false, stableResult.FailureReason);
            }

            // Phase 3: Collect final results
            var endTime = DateTime.UtcNow;
            var result = CreateResult(startTime, endTime, true);

            await PopulateResultMetricsAsync(result);

            _logger.LogInformation("Connection Load Test completed successfully in {Duration}", endTime - startTime);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection Load Test failed");
            return CreateResult(startTime, DateTime.UtcNow, false, ex.Message);
        }
        finally
        {
            // Cleanup connections
            await _connectionManager.DisconnectAllAsync();
        }
    }

    private async Task<(bool Success, string FailureReason)> RampUpConnectionsAsync(IProgress<TestProgress>? progress, CancellationToken cancellationToken)
    {
        var rampUpInterval = TimeSpan.FromMilliseconds(_config.RampUpSeconds * 1000.0 / _config.MaxUsers);
        var connectionsPerBatch = Math.Max(1, _config.MaxUsers / (_config.RampUpSeconds * 10)); // 10 batches per second
        var batchInterval = TimeSpan.FromMilliseconds(100); // 100ms between batches

        _logger.LogDebug("Ramp-up strategy: {ConnectionsPerBatch} connections per batch, {BatchInterval}ms interval",
            connectionsPerBatch, batchInterval.TotalMilliseconds);

        var connectedUsers = 0;
        var failedConnections = 0;
        var startTime = DateTime.UtcNow;

        for (var batch = 0; batch < Math.Ceiling((double)_config.MaxUsers / connectionsPerBatch); batch++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return (false, "Operation was cancelled");
            }

            var connectionsInThisBatch = Math.Min(connectionsPerBatch, _config.MaxUsers - connectedUsers);

            // Create connection tasks for this batch
            var connectionTasks = new List<Task>();

            for (var i = 0; i < connectionsInThisBatch; i++)
            {
                var userId = GenerateTestUserId();

                connectionTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        _ = await _connectionManager.CreateConnectionAsync(userId, cancellationToken);
                        _ = Interlocked.Increment(ref connectedUsers);
                    }
                    catch (Exception ex)
                    {
                        _ = Interlocked.Increment(ref failedConnections);
                        _logger.LogWarning(ex, "Failed to connect user {UserId}", userId);
                    }
                }, cancellationToken));
            }

            // Wait for batch to complete with timeout
            try
            {
                using var batchTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(_config.ConnectionTimeoutSeconds));
                using var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, batchTimeout.Token);

                await Task.WhenAll(connectionTasks);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return (false, "Operation was cancelled");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Batch {BatchNumber} timed out after {TimeoutSeconds} seconds", batch, _config.ConnectionTimeoutSeconds);
            }

            // Report progress
            var progressPercent = (double)connectedUsers / _config.MaxUsers * 50; // 50% for ramp-up phase
            var metrics = _metricsCollector.GetLatestMetrics();

            ReportProgress(progress, new TestProgress
            {
                CurrentScenario = "Connection Ramp-Up",
                ActiveUsers = connectedUsers,
                ProgressPercent = progressPercent,
                CurrentMetrics = metrics ?? new SystemMetrics(),
                LastUpdated = DateTime.UtcNow
            });

            _logger.LogDebug("Batch {BatchNumber}: {ConnectedUsers}/{MaxUsers} users connected ({FailedConnections} failures)",
                batch, connectedUsers, _config.MaxUsers, failedConnections);

            // Wait before next batch (unless this was the last batch)
            if (connectedUsers < _config.MaxUsers)
            {
                await Task.Delay(batchInterval, cancellationToken);
            }
        }

        var rampUpDuration = DateTime.UtcNow - startTime;
        var connectionSuccessRate = connectedUsers / (double)_config.MaxUsers;

        _logger.LogInformation("Ramp-up completed: {ConnectedUsers}/{MaxUsers} connected ({SuccessRate:P2}) in {Duration}",
            connectedUsers, _config.MaxUsers, connectionSuccessRate, rampUpDuration);

        // Check if we met the minimum success rate (99%)
        if (connectionSuccessRate < 0.99)
        {
            return (false, $"Connection success rate {connectionSuccessRate:P2} below required 99%");
        }

        return (true, string.Empty);
    }

    private async Task<(bool Success, string FailureReason)> MaintainStableConnectionsAsync(IProgress<TestProgress>? progress, CancellationToken cancellationToken)
    {
        var stablePhaseStart = DateTime.UtcNow;
        var stablePhaseEnd = stablePhaseStart.AddSeconds(_config.StableSeconds);
        var reportInterval = TimeSpan.FromSeconds(10); // Report every 10 seconds
        var lastReportTime = stablePhaseStart;

        _logger.LogInformation("Maintaining {ActiveConnections} connections for {Duration} seconds",
            _connectionManager.ActiveConnectionCount, _config.StableSeconds);

        while (DateTime.UtcNow < stablePhaseEnd)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return (false, "Operation was cancelled");
            }

            // Check connection health
            var activeConnections = _connectionManager.ActiveConnectionCount;
            var connectionMetrics = _connectionManager.GetConnectionMetrics();

            // Log periodic status
            if (DateTime.UtcNow - lastReportTime >= reportInterval)
            {
                _logger.LogInformation("Stable phase: {ActiveConnections} active connections, {ErrorCount} errors",
                    activeConnections, connectionMetrics.ErrorCount);
                lastReportTime = DateTime.UtcNow;
            }

            // Report progress
            var elapsed = DateTime.UtcNow - stablePhaseStart;
            var progressPercent = 50 + (elapsed.TotalSeconds / _config.StableSeconds * 50); // 50-100% for stable phase
            var metrics = _metricsCollector.GetLatestMetrics();

            ReportProgress(progress, new TestProgress
            {
                CurrentScenario = "Connection Stability",
                ActiveUsers = activeConnections,
                ProgressPercent = progressPercent,
                CurrentMetrics = metrics ?? new SystemMetrics(),
                LastUpdated = DateTime.UtcNow
            });

            // Check for significant connection drops
            var expectedConnections = _config.MaxUsers * 0.99; // Allow for 1% connection loss
            if (activeConnections < expectedConnections)
            {
                return (false, $"Too many connections lost: {activeConnections} < {expectedConnections:F0}");
            }

            // Wait a bit before next check
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        var stableDuration = DateTime.UtcNow - stablePhaseStart;
        _logger.LogInformation("Stable phase completed: maintained connections for {Duration}", stableDuration);

        return (true, string.Empty);
    }

    private Task PopulateResultMetricsAsync(ScenarioResult result)
    {
        var connectionMetrics = _connectionManager.GetConnectionMetrics();
        var resourceStats = _metricsCollector.GetResourceUsageStatistics();

        // Connection metrics
        result.TotalUsers = _config.MaxUsers;
        result.SuccessfulConnections = connectionMetrics.ConnectedUsers;
        result.FailedConnections = _config.MaxUsers - connectionMetrics.ConnectedUsers;

        // Connection latency (connection time)
        var users = _connectionManager.GetUsers().ToList();
        var connectionTimes = users.Select(u => u.Metrics.ConnectionTime.TotalMilliseconds);
        result.LatencyStats = CalculateLatencyStatistics(connectionTimes);

        // Throughput statistics
        var testDuration = result.Duration.TotalSeconds;
        result.ThroughputStats = new ThroughputStatistics
        {
            ConnectionsPerSecond = result.SuccessfulConnections / testDuration,
            MessagesPerSecond = 0, // No messages in connection test
            PeakConnectionsPerSecond = result.SuccessfulConnections / Math.Max(1, _config.RampUpSeconds),
            AverageResponseTime = result.LatencyStats.AverageMs
        };

        // Resource statistics
        result.ResourceStats = resourceStats;

        // Error collection
        result.Errors = [.. users
            .Where(u => !string.IsNullOrEmpty(u.Metrics.LastErrorMessage))
            .Select(u => $"User {u.UserId}: {u.Metrics.LastErrorMessage}")
            .Take(10)];

        _logger.LogInformation("Connection Load Test Results: {SuccessfulConnections}/{TotalUsers} connected ({SuccessRate:P2}), Avg connection time: {AvgConnectionTime:F1}ms",
            result.SuccessfulConnections, result.TotalUsers, result.ConnectionSuccessRate, result.LatencyStats.AverageMs);

        return Task.CompletedTask;
    }
}
