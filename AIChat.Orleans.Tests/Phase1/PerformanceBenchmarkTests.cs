using AIChat.Orleans.Contracts;
using AIChat.Orleans.Grains;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Orleans.TestingHost;
using Orleans.Hosting;
using System.Diagnostics;

namespace AIChat.Orleans.Tests.Phase1;

/// <summary>
/// Performance benchmark tests for Orleans Phase 1 integration.
/// Establishes performance baselines for grain operations and system behavior.
/// </summary>
[TestFixture]
public class PerformanceBenchmarkTests : Phase1IntegrationTestBase
{
    [SetUp]
    public async Task Setup()
    {
        await SetupTestCluster();
    }

    [TearDown]
    public async Task TearDown()
    {
        await TearDownTestCluster();
    }

    [Test, Category("Performance")]
    [TestCase(100, Description = "Baseline: 100 grain activations")]
    [TestCase(500, Description = "Medium load: 500 grain activations")]
    [TestCase(1000, Description = "High load: 1000 grain activations")]
    public async Task GrainActivation_PerformanceBenchmark(int grainCount)
    {
        // Arrange
        Assert.That(TestCluster, Is.Not.Null);
        var stopwatch = Stopwatch.StartNew();

        // Act - Activate multiple grains
        var tasks = Enumerable.Range(0, grainCount)
            .Select(async i =>
            {
                var grain = GetGrain<IUserGrain>($"perf-user-{i}");
                return await grain.GetState();
            });

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert - Performance benchmarks
        var totalTime = stopwatch.ElapsedMilliseconds;
        var averageTimePerGrain = (double)totalTime / grainCount;
        var grainActivationsPerSecond = (double)grainCount / stopwatch.Elapsed.TotalSeconds;

        Console.WriteLine($"Grain Activation Benchmark Results:");
        Console.WriteLine($"  Total Grains: {grainCount:N0}");
        Console.WriteLine($"  Total Time: {totalTime:N0} ms");
        Console.WriteLine($"  Average Time per Grain: {averageTimePerGrain:F2} ms");
        Console.WriteLine($"  Activations per Second: {grainActivationsPerSecond:F0}");

        // Performance expectations for Phase 1
        Assert.That(averageTimePerGrain, Is.LessThan(100), 
            $"Grain activation should average < 100ms per grain, actual: {averageTimePerGrain:F2}ms");
        Assert.That(grainActivationsPerSecond, Is.GreaterThan(10), 
            $"Should achieve > 10 activations/sec, actual: {grainActivationsPerSecond:F0}");
        Assert.That(results.Length, Is.EqualTo(grainCount), "All grains should activate successfully");

        // Log baseline for monitoring
        TestContext.WriteLine($"BASELINE_GRAIN_ACTIVATION_{grainCount}: {averageTimePerGrain:F2}ms avg, {grainActivationsPerSecond:F0}/sec");
    }

    [Test, Category("Performance")]
    [TestCase(1000, Description = "1K activity records")]
    [TestCase(5000, Description = "5K activity records")]
    [TestCase(10000, Description = "10K activity records")]
    public async Task ActivityRecording_ThroughputBenchmark(int activityCount)
    {
        // Arrange
        Assert.That(TestCluster, Is.Not.Null);
        var grain = GetGrain<IUserGrain>("throughput-test-user");
        var stopwatch = Stopwatch.StartNew();

        // Act - Record activities sequentially
        for (int i = 0; i < activityCount; i++)
        {
            await grain.RecordActivity(ActivityType.MessageSent, $"activity-{i}");
        }

        stopwatch.Stop();

        // Assert - Throughput benchmarks
        var totalTime = stopwatch.ElapsedMilliseconds;
        var activitiesPerSecond = (double)activityCount / stopwatch.Elapsed.TotalSeconds;
        var averageTimePerActivity = (double)totalTime / activityCount;

        Console.WriteLine($"Activity Recording Throughput Results:");
        Console.WriteLine($"  Total Activities: {activityCount:N0}");
        Console.WriteLine($"  Total Time: {totalTime:N0} ms");
        Console.WriteLine($"  Activities per Second: {activitiesPerSecond:F0}");
        Console.WriteLine($"  Average Time per Activity: {averageTimePerActivity:F3} ms");

        // Performance expectations
        Assert.That(activitiesPerSecond, Is.GreaterThan(100), 
            $"Should achieve > 100 activities/sec, actual: {activitiesPerSecond:F0}");
        Assert.That(averageTimePerActivity, Is.LessThan(10), 
            $"Activity recording should average < 10ms, actual: {averageTimePerActivity:F3}ms");

        // Verify data integrity
        var state = await grain.GetState();
        Assert.That(state.Metrics.TotalActivities, Is.EqualTo(activityCount));

        TestContext.WriteLine($"BASELINE_ACTIVITY_THROUGHPUT_{activityCount}: {activitiesPerSecond:F0}/sec, {averageTimePerActivity:F3}ms avg");
    }

    [Test, Category("Performance")]
    public async Task ConcurrentGrainOperations_ScalabilityBenchmark()
    {
        // Arrange
        Assert.That(TestCluster, Is.Not.Null);
        const int userCount = 50;
        const int operationsPerUser = 20;
        var stopwatch = Stopwatch.StartNew();

        // Act - Concurrent operations across multiple grains
        var tasks = Enumerable.Range(0, userCount)
            .Select(async userId =>
            {
                var grain = GetGrain<IUserGrain>($"concurrent-user-{userId}");
                
                for (int i = 0; i < operationsPerUser; i++)
                {
                    await grain.RecordActivity(ActivityType.MessageSent, $"operation-{i}");
                }
                
                return await grain.GetState();
            });

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert - Scalability benchmarks
        var totalOperations = userCount * operationsPerUser;
        var totalTime = stopwatch.ElapsedMilliseconds;
        var operationsPerSecond = (double)totalOperations / stopwatch.Elapsed.TotalSeconds;
        var averageTimePerUser = (double)totalTime / userCount;

        Console.WriteLine($"Concurrent Operations Scalability Results:");
        Console.WriteLine($"  Concurrent Users: {userCount:N0}");
        Console.WriteLine($"  Operations per User: {operationsPerUser:N0}");
        Console.WriteLine($"  Total Operations: {totalOperations:N0}");
        Console.WriteLine($"  Total Time: {totalTime:N0} ms");
        Console.WriteLine($"  Operations per Second: {operationsPerSecond:F0}");
        Console.WriteLine($"  Average Time per User: {averageTimePerUser:F2} ms");

        // Performance expectations for concurrent operations
        Assert.That(operationsPerSecond, Is.GreaterThan(200), 
            $"Concurrent operations should achieve > 200 ops/sec, actual: {operationsPerSecond:F0}");
        Assert.That(averageTimePerUser, Is.LessThan(5000), 
            $"User operation set should complete < 5000ms, actual: {averageTimePerUser:F2}ms");

        // Verify all operations completed successfully
        Assert.That(results.Length, Is.EqualTo(userCount));
        foreach (var state in results)
        {
            Assert.That(state.Metrics.TotalActivities, Is.EqualTo(operationsPerUser));
        }

        TestContext.WriteLine($"BASELINE_CONCURRENT_OPERATIONS: {operationsPerSecond:F0}/sec, {userCount} users");
    }

    [Test, Category("Performance")]
    public async Task HealthCheck_ResponseTimeBenchmark()
    {
        // Arrange
        Assert.That(TestCluster, Is.Not.Null);
        var grain = GetGrain<IUserGrain>("health-perf-user");
        await grain.RecordActivity(ActivityType.Connected, "initial activity");

        const int healthCheckCount = 100;
        var times = new List<long>();

        // Act - Multiple health checks to measure consistency
        for (int i = 0; i < healthCheckCount; i++)
        {
            var sw = Stopwatch.StartNew();
            var health = await grain.CheckHealth();
            sw.Stop();
            times.Add(sw.ElapsedMilliseconds);

            Assert.That(health.IsHealthy, Is.True, $"Health check {i} should be healthy");
        }

        // Assert - Health check performance
        var averageTime = times.Average();
        var maxTime = times.Max();
        var minTime = times.Min();
        var p95Time = times.OrderBy(t => t).Skip((int)(healthCheckCount * 0.95)).First();

        Console.WriteLine($"Health Check Response Time Results:");
        Console.WriteLine($"  Health Checks: {healthCheckCount:N0}");
        Console.WriteLine($"  Average Time: {averageTime:F2} ms");
        Console.WriteLine($"  Min Time: {minTime:N0} ms");
        Console.WriteLine($"  Max Time: {maxTime:N0} ms");
        Console.WriteLine($"  95th Percentile: {p95Time:N0} ms");

        // Performance expectations for health checks
        Assert.That(averageTime, Is.LessThan(50), 
            $"Average health check should be < 50ms, actual: {averageTime:F2}ms");
        Assert.That(p95Time, Is.LessThan(100), 
            $"95th percentile should be < 100ms, actual: {p95Time}ms");

        TestContext.WriteLine($"BASELINE_HEALTH_CHECK: {averageTime:F2}ms avg, {p95Time}ms p95");
    }

    [Test, Category("Performance")]
    public async Task StateRetrieval_ResponseTimeBenchmark()
    {
        // Arrange
        Assert.That(TestCluster, Is.Not.Null);
        var grain = GetGrain<IUserGrain>("state-perf-user");
        
        // Pre-populate with activities
        for (int i = 0; i < 50; i++)
        {
            await grain.RecordActivity(ActivityType.MessageSent, $"setup-activity-{i}");
        }

        const int retrievalCount = 100;
        var times = new List<long>();

        // Act - Multiple state retrievals
        for (int i = 0; i < retrievalCount; i++)
        {
            var sw = Stopwatch.StartNew();
            var state = await grain.GetState();
            sw.Stop();
            times.Add(sw.ElapsedMilliseconds);

            Assert.That(state, Is.Not.Null, $"State retrieval {i} should succeed");
        }

        // Assert - State retrieval performance
        var averageTime = times.Average();
        var maxTime = times.Max();
        var p95Time = times.OrderBy(t => t).Skip((int)(retrievalCount * 0.95)).First();

        Console.WriteLine($"State Retrieval Response Time Results:");
        Console.WriteLine($"  State Retrievals: {retrievalCount:N0}");
        Console.WriteLine($"  Average Time: {averageTime:F2} ms");
        Console.WriteLine($"  Max Time: {maxTime:N0} ms");
        Console.WriteLine($"  95th Percentile: {p95Time:N0} ms");

        // Performance expectations
        Assert.That(averageTime, Is.LessThan(20), 
            $"Average state retrieval should be < 20ms, actual: {averageTime:F2}ms");
        Assert.That(p95Time, Is.LessThan(50), 
            $"95th percentile should be < 50ms, actual: {p95Time}ms");

        TestContext.WriteLine($"BASELINE_STATE_RETRIEVAL: {averageTime:F2}ms avg, {p95Time}ms p95");
    }

    [Test, Category("Performance")]
    public async Task MemoryUsage_ScalabilityBenchmark()
    {
        // Arrange
        Assert.That(TestCluster, Is.Not.Null);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var initialMemory = GC.GetTotalMemory(false);
        const int grainCount = 1000;

        // Act - Create many grains and add activities
        var grains = new List<IUserGrain>();
        for (int i = 0; i < grainCount; i++)
        {
            var grain = GetGrain<IUserGrain>($"memory-test-user-{i}");
            await grain.RecordActivity(ActivityType.Connected, "initial activity");
            grains.Add(grain);
        }

        // Force garbage collection and measure memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var peakMemory = GC.GetTotalMemory(false);
        var memoryPerGrain = (peakMemory - initialMemory) / grainCount;

        Console.WriteLine($"Memory Usage Scalability Results:");
        Console.WriteLine($"  Grains Created: {grainCount:N0}");
        Console.WriteLine($"  Initial Memory: {initialMemory:N0} bytes");
        Console.WriteLine($"  Peak Memory: {peakMemory:N0} bytes");
        Console.WriteLine($"  Memory Increase: {(peakMemory - initialMemory):N0} bytes");
        Console.WriteLine($"  Memory per Grain: {memoryPerGrain:N0} bytes");

        // Memory usage expectations (reasonable limits for grain overhead)
        Assert.That(memoryPerGrain, Is.LessThan(50000), // Less than 50KB per grain
            $"Memory per grain should be < 50KB, actual: {memoryPerGrain:N0} bytes");

        TestContext.WriteLine($"BASELINE_MEMORY_USAGE: {memoryPerGrain:N0} bytes/grain, {grainCount} grains");
    }

    [Test, Category("Performance")]
    public async Task EndToEnd_UserJourney_PerformanceBenchmark()
    {
        // Arrange - Simulate complete user journey
        Assert.That(TestCluster, Is.Not.Null);
        var userId = "e2e-perf-user";
        var stopwatch = Stopwatch.StartNew();

        // Act - Complete user journey
        var grain = GetGrain<IUserGrain>(userId);

        // 1. User connects
        await grain.RecordActivity(ActivityType.Connected, "user connected");
        
        // 2. User sends multiple messages
        for (int i = 0; i < 20; i++)
        {
            await grain.RecordActivity(ActivityType.MessageSent, $"message-{i}");
        }
        
        // 3. Health check
        var health = await grain.CheckHealth();
        
        // 4. Get state
        var state = await grain.GetState();
        
        // 5. User disconnects
        await grain.RecordActivity(ActivityType.Disconnected, "user disconnected");

        stopwatch.Stop();

        // Assert - End-to-end performance
        var totalTime = stopwatch.ElapsedMilliseconds;

        Console.WriteLine($"End-to-End User Journey Results:");
        Console.WriteLine($"  Total Operations: 23 (connect + 20 messages + health + state + disconnect)");
        Console.WriteLine($"  Total Time: {totalTime:N0} ms");
        Console.WriteLine($"  Average Time per Operation: {(double)totalTime / 23:F2} ms");

        Assert.That(totalTime, Is.LessThan(5000), 
            $"Complete user journey should take < 5 seconds, actual: {totalTime}ms");
        Assert.That(health.IsHealthy, Is.True, "User should be healthy after journey");
        Assert.That(state.Metrics.TotalActivities, Is.EqualTo(22), "Should have 22 total activities");

        TestContext.WriteLine($"BASELINE_E2E_JOURNEY: {totalTime}ms total, {(double)totalTime / 23:F2}ms per op");
    }
}

