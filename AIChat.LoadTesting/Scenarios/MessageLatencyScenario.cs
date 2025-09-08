using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Models;
using AIChat.LoadTesting.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting.Scenarios;

/// <summary>
/// Tests message latency under load to validate < 100ms delivery requirement
/// </summary>
public class MessageLatencyScenario : LoadTestScenarioBase
{
    private readonly MessageLatencyScenarioConfig _config;
    private readonly SignalRConnectionManager _connectionManager;
    private readonly SystemMetricsCollector _metricsCollector;

    public MessageLatencyScenario(
        ILogger<MessageLatencyScenario> logger,
        IServiceProvider serviceProvider,
        IOptions<ScenariosConfiguration> scenarios,
        SignalRConnectionManager connectionManager,
        SystemMetricsCollector metricsCollector)
        : base(logger, serviceProvider)
    {
        _config = scenarios.Value.MessageLatency;
        _connectionManager = connectionManager;
        _metricsCollector = metricsCollector;
    }

    public override string Name => "Message Latency Test";
    public override string Description => $"Tests message delivery latency with {_config.MaxUsers:N0} users sending {_config.MessagesPerSecond}/s messages";
    public override bool IsEnabled => _config.Enabled;

    public override Task<List<string>> ValidateConfigurationAsync()
    {
        var errors = new List<string>();

        if (_config.MaxUsers <= 0)
        {
            errors.Add("MaxUsers must be greater than 0");
        }

        if (_config.MessagesPerSecond <= 0)
        {
            errors.Add("MessagesPerSecond must be greater than 0");
        }

        if (_config.TestDurationSeconds <= 0)
        {
            errors.Add("TestDurationSeconds must be greater than 0");
        }

        if (_config.MaxLatencyMs <= 0)
        {
            errors.Add("MaxLatencyMs must be greater than 0");
        }

        return Task.FromResult(errors);
    }

    public override async Task<ScenarioResult> ExecuteAsync(IProgress<TestProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("Starting Message Latency Test: {MaxUsers} users, {MessagesPerSecond}/s rate, {Duration}s duration",
            _config.MaxUsers, _config.MessagesPerSecond, _config.TestDurationSeconds);

        try
        {
            // Phase 1: Establish connections
            _logger.LogInformation("Phase 1: Establishing {MaxUsers} connections for latency testing", _config.MaxUsers);
            var connectionResult = await EstablishConnectionsAsync(progress, cancellationToken);

            if (!connectionResult.Success)
            {
                return CreateResult(startTime, DateTime.UtcNow, false, connectionResult.FailureReason);
            }

            // Phase 2: Run message latency test
            _logger.LogInformation("Phase 2: Running message latency test for {Duration} seconds", _config.TestDurationSeconds);
            var latencyResult = await RunMessageLatencyTestAsync(progress, cancellationToken);

            if (!latencyResult.Success)
            {
                return CreateResult(startTime, DateTime.UtcNow, false, latencyResult.FailureReason);
            }

            // Phase 3: Collect results
            var endTime = DateTime.UtcNow;
            var result = CreateResult(startTime, endTime, true);

            await PopulateResultMetricsAsync(result);

            // Validate latency requirement
            if (result.LatencyStats.AverageMs > _config.MaxLatencyMs)
            {
                result.Success = false;
                result.FailureReason = $"Average latency {result.LatencyStats.AverageMs:F1}ms exceeds limit {_config.MaxLatencyMs}ms";
            }

            _logger.LogInformation("Message Latency Test completed: avg {AvgLatency:F1}ms, p95 {P95Latency:F1}ms, {MessageCount} messages",
                result.LatencyStats.AverageMs, result.LatencyStats.P95Ms, result.TotalMessages);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message Latency Test failed");
            return CreateResult(startTime, DateTime.UtcNow, false, ex.Message);
        }
        finally
        {
            // Cleanup connections
            await _connectionManager.DisconnectAllAsync();
        }
    }

    private async Task<(bool Success, string FailureReason)> EstablishConnectionsAsync(IProgress<TestProgress>? progress, CancellationToken cancellationToken)
    {
        var connectedUsers = 0;
        var failedConnections = 0;
        var chatId = GenerateTestChatId();

        // Connection tasks with controlled concurrency
        var connectionTasks = Enumerable.Range(0, _config.MaxUsers)
            .Select(i => new Func<Task>(async () =>
            {
                var userId = GenerateTestUserId();
                try
                {
                    var user = await _connectionManager.CreateConnectionAsync(userId, cancellationToken);
                    await _connectionManager.JoinChatAsync(userId, chatId, cancellationToken);
                    _ = Interlocked.Increment(ref connectedUsers);

                    // Report progress during connection phase
                    if (connectedUsers % 50 == 0) // Report every 50 connections
                    {
                        ReportProgress(progress, new TestProgress
                        {
                            CurrentScenario = "Connecting Users for Latency Test",
                            ActiveUsers = connectedUsers,
                            ProgressPercent = (double)connectedUsers / _config.MaxUsers * 25, // 25% for connection phase
                            LastUpdated = DateTime.UtcNow
                        });
                    }
                }
                catch (Exception ex)
                {
                    _ = Interlocked.Increment(ref failedConnections);
                    _logger.LogWarning(ex, "Failed to connect user {UserId} for latency test", userId);
                }
            }));

        // Execute connection tasks with limited concurrency
        _ = await ExecuteConcurrentlyAsync(
            connectionTasks.Select(task => new Func<Task<object?>>(() => task().ContinueWith(t => (object?)null))),
            maxConcurrency: 100,
            progress: progress,
            operationName: "Connection",
            cancellationToken: cancellationToken);

        var connectionSuccessRate = connectedUsers / (double)_config.MaxUsers;
        _logger.LogInformation("Connection phase: {ConnectedUsers}/{MaxUsers} connected ({SuccessRate:P2})",
            connectedUsers, _config.MaxUsers, connectionSuccessRate);

        if (connectionSuccessRate < 0.95) // Allow 5% connection failures
        {
            return (false, $"Connection success rate {connectionSuccessRate:P2} below required 95%");
        }

        return (true, string.Empty);
    }

    private async Task<(bool Success, string FailureReason)> RunMessageLatencyTestAsync(IProgress<TestProgress>? progress, CancellationToken cancellationToken)
    {
        var testStart = DateTime.UtcNow;
        var testEnd = testStart.AddSeconds(_config.TestDurationSeconds);
        var messageInterval = TimeSpan.FromSeconds(1.0 / _config.MessagesPerSecond);

        var users = _connectionManager.GetUsers().Where(u => u.IsConnected).ToList();
        var chatId = users.FirstOrDefault()?.JoinedChats.FirstOrDefault() ?? GenerateTestChatId();

        _logger.LogInformation("Starting latency test with {UserCount} users, message interval {MessageInterval}ms",
            users.Count, messageInterval.TotalMilliseconds);

        var messagesSent = 0;
        var messagesReceived = 0;
        var lastProgressReport = DateTime.UtcNow;
        var progressReportInterval = TimeSpan.FromSeconds(10);

        // Create a list of message sending tasks
        var messageTasks = new List<Task>();
        var random = new Random();

        while (DateTime.UtcNow < testEnd && !cancellationToken.IsCancellationRequested)
        {
            // Select a random user to send a message
            var user = users[random.Next(users.Count)];

            if (user.IsConnected)
            {
                var messageContent = GenerateTestMessage();

                // Create message task with latency tracking
                var messageTask = Task.Run(async () =>
                {
                    try
                    {
                        var message = await _connectionManager.SendMessageAsync(user.UserId, chatId, messageContent, cancellationToken);
                        _ = Interlocked.Increment(ref messagesSent);

                        // Wait for message delivery confirmation (with timeout)
                        var deliveryTimeout = DateTime.UtcNow.AddSeconds(10);
                        while (!message.IsDelivered && DateTime.UtcNow < deliveryTimeout && !cancellationToken.IsCancellationRequested)
                        {
                            await Task.Delay(10, cancellationToken);
                        }

                        if (message.IsDelivered)
                        {
                            _ = Interlocked.Increment(ref messagesReceived);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Failed to send message for user {UserId}", user.UserId);
                    }
                }, cancellationToken);

                messageTasks.Add(messageTask);

                // Clean up completed tasks to prevent memory buildup
                _ = messageTasks.RemoveAll(t => t.IsCompleted);
            }

            // Report progress periodically
            if (DateTime.UtcNow - lastProgressReport >= progressReportInterval)
            {
                var elapsed = DateTime.UtcNow - testStart;
                var progressPercent = 25 + (elapsed.TotalSeconds / _config.TestDurationSeconds * 70); // 25-95% for test phase
                var metrics = _metricsCollector.GetLatestMetrics();

                ReportProgress(progress, new TestProgress
                {
                    CurrentScenario = "Message Latency Testing",
                    ActiveUsers = users.Count(u => u.IsConnected),
                    TotalMessages = messagesSent,
                    CurrentThroughput = messagesSent / elapsed.TotalSeconds,
                    ProgressPercent = progressPercent,
                    CurrentMetrics = metrics ?? new SystemMetrics(),
                    LastUpdated = DateTime.UtcNow
                });

                _logger.LogDebug("Latency test progress: {MessagesSent} sent, {MessagesReceived} received, {Duration} elapsed",
                    messagesSent, messagesReceived, elapsed);

                lastProgressReport = DateTime.UtcNow;
            }

            // Wait for next message interval
            await Task.Delay(messageInterval, cancellationToken);
        }

        // Wait for remaining message tasks to complete
        try
        {
            await Task.WhenAll(messageTasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Some message tasks failed during completion");
        }

        var actualDuration = DateTime.UtcNow - testStart;
        var messageDeliveryRate = messagesReceived / (double)messagesSent;

        _logger.LogInformation("Message latency test completed: {MessagesSent} sent, {MessagesReceived} received ({DeliveryRate:P2}) in {Duration}",
            messagesSent, messagesReceived, messageDeliveryRate, actualDuration);

        // Check message delivery rate
        if (messageDeliveryRate < 0.99) // Require 99% delivery rate
        {
            return (false, $"Message delivery rate {messageDeliveryRate:P2} below required 99%");
        }

        return (true, string.Empty);
    }

    private Task PopulateResultMetricsAsync(ScenarioResult result)
    {
        var users = _connectionManager.GetUsers().ToList();
        var connectionMetrics = _connectionManager.GetConnectionMetrics();
        var resourceStats = _metricsCollector.GetResourceUsageStatistics();

        // Connection statistics
        result.TotalUsers = _config.MaxUsers;
        result.SuccessfulConnections = connectionMetrics.ConnectedUsers;
        result.FailedConnections = _config.MaxUsers - connectionMetrics.ConnectedUsers;

        // Message statistics
        result.TotalMessages = connectionMetrics.TotalMessagesSent;
        result.DeliveredMessages = connectionMetrics.TotalMessagesReceived;

        // Latency statistics from all user metrics
        var allLatencies = users.SelectMany(u => u.Metrics.MessageLatencies).ToList();
        result.LatencyStats = CalculateLatencyStatistics(allLatencies);

        // Throughput statistics
        var testDuration = result.Duration.TotalSeconds;
        result.ThroughputStats = new ThroughputStatistics
        {
            ConnectionsPerSecond = result.SuccessfulConnections / testDuration,
            MessagesPerSecond = result.TotalMessages / testDuration,
            PeakMessagesPerSecond = _config.MessagesPerSecond * _config.MaxUsers,
            AverageResponseTime = result.LatencyStats.AverageMs
        };

        // Resource statistics
        result.ResourceStats = resourceStats;

        // Collect errors
        result.Errors = [.. users
            .Where(u => !string.IsNullOrEmpty(u.Metrics.LastErrorMessage))
            .Select(u => $"User {u.UserId}: {u.Metrics.LastErrorMessage}")
            .Take(10)];

        // Log detailed results
        _logger.LogInformation("Message Latency Results:");
        _logger.LogInformation("  Average Latency: {AvgLatency:F1}ms (target: <{MaxLatency}ms)",
            result.LatencyStats.AverageMs, _config.MaxLatencyMs);
        _logger.LogInformation("  95th Percentile: {P95Latency:F1}ms", result.LatencyStats.P95Ms);
        _logger.LogInformation("  99th Percentile: {P99Latency:F1}ms", result.LatencyStats.P99Ms);
        _logger.LogInformation("  Max Latency: {MaxLatency:F1}ms", result.LatencyStats.MaxMs);
        _logger.LogInformation("  Message Delivery: {DeliveredMessages}/{TotalMessages} ({DeliveryRate:P2})",
            result.DeliveredMessages, result.TotalMessages, result.MessageDeliveryRate);
        _logger.LogInformation("  Throughput: {Throughput:F1} messages/second", result.ThroughputStats.MessagesPerSecond);

        return Task.CompletedTask;
    }
}
