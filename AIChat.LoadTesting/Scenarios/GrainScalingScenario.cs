using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Models;
using AIChat.LoadTesting.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting.Scenarios;

/// <summary>
/// Tests Orleans grain scaling behavior under burst traffic patterns
/// </summary>
public class GrainScalingScenario : LoadTestScenarioBase
{
    private readonly GrainScalingScenarioConfig _config;
    private readonly SignalRConnectionManager _connectionManager;
    private readonly SystemMetricsCollector _metricsCollector;

    public GrainScalingScenario(
        ILogger<GrainScalingScenario> logger,
        IServiceProvider serviceProvider,
        IOptions<ScenariosConfiguration> scenarios,
        SignalRConnectionManager connectionManager,
        SystemMetricsCollector metricsCollector)
        : base(logger, serviceProvider)
    {
        _config = scenarios.Value.GrainScaling;
        _connectionManager = connectionManager;
        _metricsCollector = metricsCollector;
    }

    public override string Name => "Grain Scaling Test";
    public override string Description => $"Tests Orleans grain scaling with burst patterns: {string.Join(", ", _config.BurstUsers)} users";
    public override bool IsEnabled => _config.Enabled;

    public override Task<List<string>> ValidateConfigurationAsync()
    {
        var errors = new List<string>();

        if (_config.BurstUsers == null || _config.BurstUsers.Length == 0)
        {
            errors.Add("BurstUsers array cannot be empty");
        }

        if (_config.BurstUsers?.Any(u => u <= 0) == true)
        {
            errors.Add("All BurstUsers values must be greater than 0");
        }

        if (_config.BurstDurationSeconds <= 0)
        {
            errors.Add("BurstDurationSeconds must be greater than 0");
        }

        if (_config.RestDurationSeconds <= 0)
        {
            errors.Add("RestDurationSeconds must be greater than 0");
        }

        return Task.FromResult(errors);
    }

    public override async Task<ScenarioResult> ExecuteAsync(IProgress<TestProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var burstPatterns = string.Join(" → ", _config.BurstUsers);

        Logger.LogInformation("Starting Grain Scaling Test: burst pattern {BurstPattern}, {BurstDuration}s bursts, {RestDuration}s rest",
            burstPatterns, _config.BurstDurationSeconds, _config.RestDurationSeconds);

        var scalingResults = new List<BurstResult>();

        try
        {
            // Execute burst pattern for each user count
            for (int i = 0; i < _config.BurstUsers.Length; i++)
            {
                var userCount = _config.BurstUsers[i];
                var burstNumber = i + 1;

                Logger.LogInformation("Executing burst {BurstNumber}/{TotalBursts}: {UserCount} users",
                    burstNumber, _config.BurstUsers.Length, userCount);

                var burstResult = await ExecuteBurstAsync(userCount, burstNumber, _config.BurstUsers.Length, progress, cancellationToken);
                scalingResults.Add(burstResult);

                if (!burstResult.Success)
                {
                    return CreateResult(startTime, DateTime.UtcNow, false,
                        $"Burst {burstNumber} failed: {burstResult.FailureReason}");
                }

                // Rest period between bursts (except after the last one)
                if (i < _config.BurstUsers.Length - 1)
                {
                    Logger.LogInformation("Rest period: {RestDuration}s before next burst", _config.RestDurationSeconds);

                    await _connectionManager.DisconnectAllAsync();
                    await Task.Delay(TimeSpan.FromSeconds(_config.RestDurationSeconds), cancellationToken);
                }
            }

            // Create final result
            var endTime = DateTime.UtcNow;
            var result = CreateResult(startTime, endTime, true);

            PopulateScalingResults(result, scalingResults);

            Logger.LogInformation("Grain Scaling Test completed successfully in {Duration}. Grain scaling behavior validated across {BurstCount} burst patterns.",
                endTime - startTime, scalingResults.Count);

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Grain Scaling Test failed");
            return CreateResult(startTime, DateTime.UtcNow, false, ex.Message);
        }
        finally
        {
            await _connectionManager.DisconnectAllAsync();
        }
    }

    private async Task<BurstResult> ExecuteBurstAsync(int userCount, int burstNumber, int totalBursts, IProgress<TestProgress>? progress, CancellationToken cancellationToken)
    {
        var burstStart = DateTime.UtcNow;
        var chatId = GenerateTestChatId();

        var result = new BurstResult
        {
            UserCount = userCount,
            BurstNumber = burstNumber,
            StartTime = burstStart
        };

        try
        {
            // Phase 1: Rapid connection burst to trigger grain activation
            Logger.LogInformation("Burst {BurstNumber}: Rapidly connecting {UserCount} users", burstNumber, userCount);

            var preActivationMetrics = _metricsCollector.GetLatestMetrics();
            var connectionStart = DateTime.UtcNow;

            var connectedUsers = await ConnectUsersBurstAsync(userCount, chatId, progress, burstNumber, totalBursts, cancellationToken);

            var connectionEnd = DateTime.UtcNow;
            var postActivationMetrics = _metricsCollector.GetLatestMetrics();

            result.ConnectedUsers = connectedUsers.Count;
            result.ConnectionTime = connectionEnd - connectionStart;
            result.GrainsBefore = preActivationMetrics?.Orleans.ActiveGrainCount ?? 0;
            result.GrainsAfter = postActivationMetrics?.Orleans.ActiveGrainCount ?? 0;
            result.GrainActivationCount = Math.Max(0, result.GrainsAfter - result.GrainsBefore);

            Logger.LogInformation("Burst {BurstNumber}: {ConnectedUsers}/{TargetUsers} connected in {ConnectionTime}ms, grain count: {GrainsBefore} → {GrainsAfter} (+{ActivationCount})",
                burstNumber, result.ConnectedUsers, userCount, result.ConnectionTime.TotalMilliseconds,
                result.GrainsBefore, result.GrainsAfter, result.GrainActivationCount);

            // Phase 2: Message burst to test grain message handling under load
            Logger.LogInformation("Burst {BurstNumber}: Testing message handling with {UserCount} users", burstNumber, userCount);

            var messageMetrics = await ExecuteMessageBurstAsync(connectedUsers, chatId, progress, burstNumber, totalBursts, cancellationToken);

            result.MessagesSent = messageMetrics.MessagesSent;
            result.MessagesReceived = messageMetrics.MessagesReceived;
            result.AverageLatencyMs = messageMetrics.AverageLatency;
            result.MaxLatencyMs = messageMetrics.MaxLatency;

            // Phase 3: Maintain load and measure scaling stability
            Logger.LogInformation("Burst {BurstNumber}: Maintaining load for {Duration}s", burstNumber, _config.BurstDurationSeconds);

            var stabilityEnd = burstStart.AddSeconds(_config.BurstDurationSeconds);
            while (DateTime.UtcNow < stabilityEnd && !cancellationToken.IsCancellationRequested)
            {
                var currentMetrics = _metricsCollector.GetLatestMetrics();

                // Report progress during stability phase
                var elapsed = DateTime.UtcNow - burstStart;
                var progressPercent = ((burstNumber - 1) / (double)totalBursts * 100) +
                                    (elapsed.TotalSeconds / _config.BurstDurationSeconds * (100.0 / totalBursts));

                ReportProgress(progress, new TestProgress
                {
                    CurrentScenario = $"Grain Scaling Burst {burstNumber}/{totalBursts}",
                    ActiveUsers = _connectionManager.ActiveConnectionCount,
                    ProgressPercent = progressPercent,
                    CurrentMetrics = currentMetrics ?? new SystemMetrics(),
                    LastUpdated = DateTime.UtcNow
                });

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            result.EndTime = DateTime.UtcNow;
            result.Success = true;

            // Validate scaling behavior
            ValidateScalingBehavior(result);

            return result;
        }
        catch (Exception ex)
        {
            result.EndTime = DateTime.UtcNow;
            result.Success = false;
            result.FailureReason = ex.Message;

            Logger.LogError(ex, "Burst {BurstNumber} failed", burstNumber);
            return result;
        }
    }

    private async Task<List<TestUser>> ConnectUsersBurstAsync(int userCount, string chatId, IProgress<TestProgress>? progress, int burstNumber, int totalBursts, CancellationToken cancellationToken)
    {
        var connectionTasks = Enumerable.Range(0, userCount)
            .Select(i => new Func<Task<TestUser?>>(async () =>
            {
                var userId = GenerateTestUserId();
                try
                {
                    var user = await _connectionManager.CreateConnectionAsync(userId, cancellationToken);
                    await _connectionManager.JoinChatAsync(userId, chatId, cancellationToken);
                    return user;
                }
                catch (Exception ex)
                {
                    Logger.LogTrace(ex, "Failed to connect user {UserId} in burst {BurstNumber}", userId, burstNumber);
                    return null;
                }
            }));

        // Execute connections with high concurrency for burst effect
        var results = await ExecuteConcurrentlyAsync(
            connectionTasks,
            maxConcurrency: Math.Min(userCount, 200), // High concurrency to create burst effect
            progress: null, // Don't report individual connection progress during burst
            operationName: "Burst Connection",
            cancellationToken: cancellationToken);

        return [.. results.Where(user => user != null).Cast<TestUser>()];
    }

    private async Task<MessageBurstMetrics> ExecuteMessageBurstAsync(List<TestUser> users, string chatId, IProgress<TestProgress>? progress, int burstNumber, int totalBursts, CancellationToken cancellationToken)
    {
        var messagesSent = 0;
        var messagesReceived = 0;
        var latencies = new List<double>();
        var random = new Random();

        // Send a burst of messages (2 messages per user)
        var messageTasks = users.SelectMany(user => Enumerable.Range(0, 2).Select(i =>
            new Func<Task>(async () =>
            {
                try
                {
                    var messageContent = GenerateTestMessage();
                    var message = await _connectionManager.SendMessageAsync(user.UserId, chatId, messageContent, cancellationToken);

                    _ = Interlocked.Increment(ref messagesSent);

                    // Wait for delivery (with timeout)
                    var timeout = DateTime.UtcNow.AddSeconds(5);
                    while (!message.IsDelivered && DateTime.UtcNow < timeout && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(50, cancellationToken);
                    }

                    if (message.IsDelivered && message.Latency.HasValue)
                    {
                        _ = Interlocked.Increment(ref messagesReceived);
                        lock (latencies)
                        {
                            latencies.Add(message.Latency.Value.TotalMilliseconds);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogTrace(ex, "Message send failed for user {UserId}", user.UserId);
                }
            })));

        // Execute message burst with controlled concurrency
        _ = await ExecuteConcurrentlyAsync(
            messageTasks.Select(task => new Func<Task<object?>>(() => task().ContinueWith(t => (object?)null))),
            maxConcurrency: 100,
            progress: null,
            operationName: "Message Burst",
            cancellationToken: cancellationToken);

        return new MessageBurstMetrics
        {
            MessagesSent = messagesSent,
            MessagesReceived = messagesReceived,
            AverageLatency = latencies.Count > 0 ? latencies.Average() : 0,
            MaxLatency = latencies.Count > 0 ? latencies.Max() : 0
        };
    }

    private void ValidateScalingBehavior(BurstResult result)
    {
        // Validate grain activation occurred appropriately
        var expectedGrainIncrease = Math.Max(1, result.UserCount / 10); // Expect at least 1 grain per 10 users

        if (result.GrainActivationCount < expectedGrainIncrease)
        {
            result.Warnings.Add($"Lower than expected grain activation: {result.GrainActivationCount} (expected >= {expectedGrainIncrease})");
        }

        // Validate connection success rate
        var connectionSuccessRate = result.ConnectedUsers / (double)result.UserCount;
        if (connectionSuccessRate < 0.95)
        {
            result.Warnings.Add($"Low connection success rate: {connectionSuccessRate:P2}");
        }

        // Validate message delivery under load
        var messageDeliveryRate = result.MessagesSent > 0 ? result.MessagesReceived / (double)result.MessagesSent : 0;
        if (messageDeliveryRate < 0.90)
        {
            result.Warnings.Add($"Low message delivery rate under burst load: {messageDeliveryRate:P2}");
        }

        // Validate reasonable connection time under load
        if (result.ConnectionTime.TotalSeconds > 30)
        {
            result.Warnings.Add($"Slow connection time under load: {result.ConnectionTime.TotalSeconds:F1}s");
        }
    }

    private void PopulateScalingResults(ScenarioResult result, List<BurstResult> scalingResults)
    {
        // Overall statistics
        result.TotalUsers = scalingResults.Sum(b => b.ConnectedUsers);
        result.SuccessfulConnections = result.TotalUsers;
        result.FailedConnections = scalingResults.Sum(b => b.UserCount - b.ConnectedUsers);
        result.TotalMessages = scalingResults.Sum(b => b.MessagesSent);
        result.DeliveredMessages = scalingResults.Sum(b => b.MessagesReceived);

        // Latency statistics from all bursts
        var allLatencies = scalingResults.Where(b => b.AverageLatencyMs > 0).Select(b => b.AverageLatencyMs);
        result.LatencyStats = CalculateLatencyStatistics(allLatencies);

        // Throughput statistics
        var totalDuration = result.Duration.TotalSeconds;
        result.ThroughputStats = new ThroughputStatistics
        {
            ConnectionsPerSecond = result.TotalUsers / totalDuration,
            MessagesPerSecond = result.TotalMessages / totalDuration,
            PeakConnectionsPerSecond = scalingResults.Max(b => b.ConnectedUsers / b.ConnectionTime.TotalSeconds)
        };

        // Resource statistics
        result.ResourceStats = _metricsCollector.GetResourceUsageStatistics();

        // Collect warnings from all bursts
        result.Errors = [.. scalingResults.SelectMany(b => b.Warnings)];

        // Log detailed scaling analysis
        Logger.LogInformation("Grain Scaling Analysis:");
        foreach (var burst in scalingResults)
        {
            Logger.LogInformation("  Burst {BurstNumber}: {UserCount} users → {ConnectedUsers} connected, {GrainsBefore}→{GrainsAfter} grains (+{ActivationCount}), {ConnectionTime:F1}s",
                burst.BurstNumber, burst.UserCount, burst.ConnectedUsers,
                burst.GrainsBefore, burst.GrainsAfter, burst.GrainActivationCount, burst.ConnectionTime.TotalSeconds);
        }

        Logger.LogInformation("  Peak Grain Count: {PeakGrains}", scalingResults.Max(b => b.GrainsAfter));
        Logger.LogInformation("  Total Grain Activations: {TotalActivations}", scalingResults.Sum(b => b.GrainActivationCount));
        Logger.LogInformation("  Average Scaling Response Time: {AvgScalingTime:F1}s", scalingResults.Average(b => b.ConnectionTime.TotalSeconds));
    }
}

/// <summary>
/// Result of a single burst in the scaling test
/// </summary>
public class BurstResult
{
    public int BurstNumber { get; set; }
    public int UserCount { get; set; }
    public int ConnectedUsers { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan ConnectionTime { get; set; }

    public int GrainsBefore { get; set; }
    public int GrainsAfter { get; set; }
    public int GrainActivationCount { get; set; }

    public int MessagesSent { get; set; }
    public int MessagesReceived { get; set; }
    public double AverageLatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }

    public bool Success { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Metrics from a message burst test
/// </summary>
public class MessageBurstMetrics
{
    public int MessagesSent { get; set; }
    public int MessagesReceived { get; set; }
    public double AverageLatency { get; set; }
    public double MaxLatency { get; set; }
}
