using AIChat.LoadTesting.Models;
using Microsoft.Extensions.Logging;

namespace AIChat.LoadTesting.Scenarios;

/// <summary>
/// Interface for load testing scenarios
/// </summary>
public interface ILoadTestScenario
{
    /// <summary>
    /// Name of the scenario
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what this scenario tests
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Whether this scenario is enabled
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Executes the load testing scenario
    /// </summary>
    /// <param name="progress">Progress reporting callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scenario result</returns>
    Task<ScenarioResult> ExecuteAsync(
        IProgress<TestProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Validates the scenario configuration
    /// </summary>
    /// <returns>Validation errors, if any</returns>
    Task<List<string>> ValidateConfigurationAsync();
}

/// <summary>
/// Base class for load testing scenarios
/// </summary>
public abstract class LoadTestScenarioBase : ILoadTestScenario
{
    protected ILogger Logger { get; }
    protected IServiceProvider ServiceProvider { get; }

    protected LoadTestScenarioBase(ILogger logger, IServiceProvider serviceProvider)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ServiceProvider =
            serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract bool IsEnabled { get; }

    public abstract Task<ScenarioResult> ExecuteAsync(
        IProgress<TestProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    public virtual Task<List<string>> ValidateConfigurationAsync()
    {
        return Task.FromResult(new List<string>());
    }

    /// <summary>
    /// Reports progress to the progress callback
    /// </summary>
    protected void ReportProgress(IProgress<TestProgress>? progress, TestProgress testProgress)
    {
        try
        {
            progress?.Report(testProgress);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to report progress for scenario {ScenarioName}", Name);
        }
    }

    /// <summary>
    /// Creates a new scenario result with basic information
    /// </summary>
    protected ScenarioResult CreateResult(
        DateTime startTime,
        DateTime endTime,
        bool success = true,
        string failureReason = ""
    )
    {
        return new ScenarioResult
        {
            ScenarioName = Name,
            StartTime = startTime,
            EndTime = endTime,
            Success = success,
            FailureReason = failureReason,
        };
    }

    /// <summary>
    /// Calculates latency statistics from a list of latencies
    /// </summary>
    protected static LatencyStatistics CalculateLatencyStatistics(IEnumerable<double> latencies)
    {
        var latencyList = latencies.Where(l => l > 0).ToList();

        if (latencyList.Count == 0)
        {
            return new LatencyStatistics();
        }

        latencyList.Sort();

        return new LatencyStatistics
        {
            AverageMs = latencyList.Average(),
            MedianMs = GetPercentile(latencyList, 0.5),
            P95Ms = GetPercentile(latencyList, 0.95),
            P99Ms = GetPercentile(latencyList, 0.99),
            MaxMs = latencyList.Max(),
            MinMs = latencyList.Min(),
            SampleCount = latencyList.Count,
        };
    }

    /// <summary>
    /// Gets a percentile value from a sorted list
    /// </summary>
    protected static double GetPercentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        index = Math.Max(0, Math.Min(index, sortedValues.Count - 1));

        return sortedValues[index];
    }

    /// <summary>
    /// Generates a random chat ID for testing
    /// </summary>
    protected static string GenerateTestChatId()
    {
        return $"test-chat-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Generates a random user ID for testing
    /// </summary>
    protected static string GenerateTestUserId()
    {
        return $"test-user-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Generates random message content for testing
    /// </summary>
    protected static string GenerateTestMessage()
    {
        var messages = new[]
        {
            "Hello, this is a test message!",
            "Testing the chat system with Orleans grains.",
            "How is the performance under load?",
            "This message is part of a load testing scenario.",
            "Checking latency and throughput measurements.",
            "Orleans grain activation and scaling test.",
            "SignalR real-time communication verification.",
            "Background processing load testing in progress.",
            "Monitoring system resource usage during tests.",
            "Validating 10,000 concurrent user capacity.",
        };

        var random = new Random();
        return messages[random.Next(messages.Length)];
    }

    /// <summary>
    /// Creates a delay with some randomization to avoid thundering herd effects
    /// </summary>
    protected static async Task RandomDelayAsync(
        TimeSpan baseDelay,
        double variationPercent = 0.2,
        CancellationToken cancellationToken = default
    )
    {
        var random = new Random();
        var variation = 1.0 + ((random.NextDouble() - 0.5) * 2 * variationPercent);
        var actualDelay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * variation);

        await Task.Delay(actualDelay, cancellationToken);
    }

    /// <summary>
    /// Executes tasks with controlled concurrency and progress reporting
    /// </summary>
    protected async Task<T[]> ExecuteConcurrentlyAsync<T>(
        IEnumerable<Func<Task<T>>> taskFactories,
        int maxConcurrency,
        IProgress<TestProgress>? progress = null,
        string operationName = "Operation",
        CancellationToken cancellationToken = default
    )
    {
        var taskList = taskFactories.ToList();
        var results = new T[taskList.Count];
        var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var completed = 0;

        var tasks = taskList.Select(
            async (taskFactory, index) =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    results[index] = await taskFactory();

                    var current = Interlocked.Increment(ref completed);
                    ReportProgress(
                        progress,
                        new TestProgress
                        {
                            CurrentScenario = Name,
                            ProgressPercent = (double)current / taskList.Count * 100,
                            LastUpdated = DateTime.UtcNow,
                        }
                    );
                }
                finally
                {
                    _ = semaphore.Release();
                }
            }
        );

        await Task.WhenAll(tasks);
        semaphore.Dispose();

        return results;
    }
}
