using System.Diagnostics;
using System.Net.Http.Json;
using AIChat.Orleans.Tests.TestUtilities;
using AIChat.Server.Models;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AIChat.Orleans.Tests.Phase4;

/// <summary>
/// Performance tests for Orleans SSE integration measuring routing overhead, concurrency, and memory usage.
/// </summary>
public class PerformanceTests : IClassFixture<OrleansTestFixture>
{
    private readonly OrleansTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PerformanceTests(OrleansTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task OrleansRoutingOverhead_ShouldBeAcceptable()
    {
        // Arrange
        await _fixture.InitializeAsync();

        var orleansTimings = new List<long>();
        var directTimings = new List<long>();
        var iterations = 10;

        // Warm up
        await WarmupSystem();

        // Act - Measure Orleans routing
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await MakeStreamRequest($"orleans-perf-{i}");
            sw.Stop();
            orleansTimings.Add(sw.ElapsedMilliseconds);
        }

        // Measure direct routing
        _fixture.OrleansEnabled = false;
        await _fixture.InitializeAsync();

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await MakeStreamRequest($"direct-perf-{i}");
            sw.Stop();
            directTimings.Add(sw.ElapsedMilliseconds);
        }

        // Assert
        var orleansAvg = orleansTimings.Average();
        var directAvg = directTimings.Average();
        var overhead = orleansAvg - directAvg;
        var overheadPercent = (overhead / directAvg) * 100;

        _ = orleansAvg.Should().BeLessThan(5000, "Orleans routing should complete within 5 seconds");
        _ = overheadPercent.Should().BeLessThan(50, "Orleans overhead should be less than 50%");

        _output.WriteLine($"Orleans avg: {orleansAvg}ms, Direct avg: {directAvg}ms");
        _output.WriteLine($"Routing overhead: {overhead}ms ({overheadPercent:F2}%)");
    }

    [Fact]
    public async Task HighConcurrency_ShouldHandleMultipleUsers()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        var userCount = 50;
        var successCount = 0;
        var failureCount = 0;
        var totalDuration = 0L;

        // Act - Create concurrent streams
        var tasks = new List<Task>();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < userCount; i++)
        {
            var userId = $"concurrent-user-{i}";
            var task = Task.Run(async () =>
            {
                try
                {
                    var userSw = Stopwatch.StartNew();
                    await MakeStreamRequest(userId);
                    userSw.Stop();

                    _ = Interlocked.Add(ref totalDuration, userSw.ElapsedMilliseconds);
                    _ = Interlocked.Increment(ref successCount);
                }
                catch
                {
                    _ = Interlocked.Increment(ref failureCount);
                }
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        // Assert
        _ = successCount.Should().BeGreaterThan((int)(userCount * 0.95), "At least 95% success rate");
        _ = failureCount.Should().BeLessThan((int)(userCount * 0.05), "Less than 5% failure rate");

        var avgDuration = totalDuration / (double)successCount;
        _ = avgDuration.Should().BeLessThan(10000, "Average request should complete within 10 seconds");

        var throughput = successCount / (sw.ElapsedMilliseconds / 1000.0);
        _ = throughput.Should().BeGreaterThan(1, "Should handle at least 1 request per second");

        _output.WriteLine($"Concurrency test: {successCount}/{userCount} successful");
        _output.WriteLine($"Average duration: {avgDuration}ms");
        _output.WriteLine($"Throughput: {throughput:F2} requests/sec");
    }

    [Fact]
    public async Task MemoryBoundaries_ShouldBeRespected()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        var initialMemory = GC.GetTotalMemory(true);
        var peakMemory = initialMemory;
        var memoryMeasurements = new List<long>();

        // Act - Create streams and monitor memory
        var monitorTask = Task.Run(async () =>
        {
            while (memoryMeasurements.Count < 20)
            {
                var currentMemory = GC.GetTotalMemory(false);
                memoryMeasurements.Add(currentMemory);
                peakMemory = Math.Max(peakMemory, currentMemory);
                await Task.Delay(500);
            }
        });

        var streamTasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            var task = MakeStreamRequest($"memory-test-{i}");
            streamTasks.Add(task);
            await Task.Delay(100); // Stagger requests
        }

        await Task.WhenAll(streamTasks);
        await monitorTask;

        // Force cleanup
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(true);

        // Assert
        var memoryIncrease = peakMemory - initialMemory;
        var memoryIncreaseMB = memoryIncrease / (1024.0 * 1024.0);
        _ = memoryIncreaseMB.Should().BeLessThan(100, "Memory increase should be less than 100MB");

        var memoryLeak = finalMemory - initialMemory;
        var memoryLeakMB = memoryLeak / (1024.0 * 1024.0);
        _ = memoryLeakMB.Should().BeLessThan(10, "Memory should be released after streams complete");

        _output.WriteLine($"Initial memory: {initialMemory / 1024.0 / 1024.0:F2}MB");
        _output.WriteLine($"Peak memory: {peakMemory / 1024.0 / 1024.0:F2}MB");
        _output.WriteLine($"Final memory: {finalMemory / 1024.0 / 1024.0:F2}MB");
        _output.WriteLine($"Peak increase: {memoryIncreaseMB:F2}MB");
    }

    [Fact]
    public async Task Throughput_UnderLoad_ShouldMaintainBaseline()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        var duration = TimeSpan.FromSeconds(10);
        var completedRequests = 0;
        var errors = 0;
        var latencies = new List<long>();

        // Act - Generate load for duration
        var cts = new CancellationTokenSource(duration);
        var loadTask = Task.Run(async () =>
        {
            var tasks = new List<Task>();

            while (!cts.Token.IsCancellationRequested)
            {
                var task = Task.Run(async () =>
                {
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        await MakeStreamRequest($"load-test-{Guid.NewGuid()}");
                        sw.Stop();

                        lock (latencies)
                        {
                            latencies.Add(sw.ElapsedMilliseconds);
                        }

                        _ = Interlocked.Increment(ref completedRequests);
                    }
                    catch
                    {
                        _ = Interlocked.Increment(ref errors);
                    }
                });

                tasks.Add(task);
                await Task.Delay(100); // 10 requests per second target
            }

            await Task.WhenAll(tasks);
        });

        try
        {
            await loadTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when duration expires
        }

        // Assert
        var throughput = completedRequests / duration.TotalSeconds;
        _ = throughput.Should().BeGreaterThan(5, "Should maintain at least 5 requests/sec");

        var errorRate = errors / (double)(completedRequests + errors);
        _ = errorRate.Should().BeLessThan(0.05, "Error rate should be less than 5%");

        if (latencies.Any())
        {
            var p50 = GetPercentile(latencies, 50);
            var p95 = GetPercentile(latencies, 95);
            var p99 = GetPercentile(latencies, 99);

            _ = p50.Should().BeLessThan(2000, "P50 latency should be under 2 seconds");
            _ = p95.Should().BeLessThan(5000, "P95 latency should be under 5 seconds");
            _ = p99.Should().BeLessThan(10000, "P99 latency should be under 10 seconds");

            _output.WriteLine($"Throughput: {throughput:F2} req/sec");
            _output.WriteLine($"Completed: {completedRequests}, Errors: {errors}");
            _output.WriteLine($"Latency P50: {p50}ms, P95: {p95}ms, P99: {p99}ms");
        }
    }

    [Fact]
    public async Task StreamingOverhead_ShouldBeMinimal()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        var messageCount = 100;
        var streamingTimes = new List<long>();

        // Act - Measure streaming overhead
        for (int i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();

            using var client = _fixture.CreateSseClient();
            var request = new CreateChatRequest
            {
                UserId = $"streaming-test-{i}",
                Message = $"Generate {messageCount} messages",
                SystemPrompt = "You are a test assistant",
                ModeId = "default"
            };

            var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);
            _ = response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            var events = await SseTestHelpers.ParseSseStreamAsync(stream);

            sw.Stop();
            streamingTimes.Add(sw.ElapsedMilliseconds);

            _output.WriteLine($"Iteration {i + 1}: {sw.ElapsedMilliseconds}ms for {events.Count} events");
        }

        // Assert
        var avgTime = streamingTimes.Average();
        var timePerMessage = avgTime / messageCount;

        _ = timePerMessage.Should().BeLessThan(100, "Should process each message in under 100ms");

        _output.WriteLine($"Average streaming time: {avgTime}ms");
        _output.WriteLine($"Time per message: {timePerMessage:F2}ms");
    }

    [Fact]
    public async Task OrleansGrainActivation_ShouldBeEfficient()
    {
        // Arrange
        _fixture.OrleansEnabled = true;
        await _fixture.InitializeAsync();

        var coldStartTimes = new List<long>();
        var warmStartTimes = new List<long>();

        // Act - Measure cold starts
        for (int i = 0; i < 5; i++)
        {
            var userId = $"cold-start-{Guid.NewGuid()}"; // New grain each time
            var sw = Stopwatch.StartNew();
            await MakeStreamRequest(userId);
            sw.Stop();
            coldStartTimes.Add(sw.ElapsedMilliseconds);
        }

        // Measure warm starts (reuse same user)
        var warmUserId = "warm-start-user";
        for (int i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            await MakeStreamRequest(warmUserId);
            sw.Stop();
            warmStartTimes.Add(sw.ElapsedMilliseconds);
        }

        // Assert
        var avgColdStart = coldStartTimes.Average();
        var avgWarmStart = warmStartTimes.Average();

        _ = avgColdStart.Should().BeLessThan(3000, "Cold start should be under 3 seconds");
        _ = avgWarmStart.Should().BeLessThan(avgColdStart * 0.5, "Warm start should be at least 50% faster");

        _output.WriteLine($"Average cold start: {avgColdStart}ms");
        _output.WriteLine($"Average warm start: {avgWarmStart}ms");
        _output.WriteLine($"Improvement: {(1 - (avgWarmStart / avgColdStart)) * 100:F2}%");
    }

    private async Task WarmupSystem()
    {
        // Make a few requests to warm up the system
        for (int i = 0; i < 3; i++)
        {
            await MakeStreamRequest($"warmup-{i}");
        }
    }

    private async Task MakeStreamRequest(string userId)
    {
        using var client = _fixture.CreateSseClient();
        var request = new CreateChatRequest
        {
            UserId = userId,
            Message = "Performance test message",
            SystemPrompt = "You are a test assistant",
            ModeId = "default"
        };

        var response = await client.PostAsJsonAsync("/api/chat/stream-sse", request);
        _ = response.EnsureSuccessStatusCode();

        // Consume the stream
        using var stream = await response.Content.ReadAsStreamAsync();
        _ = await SseTestHelpers.ParseSseStreamAsync(stream);
    }

    private static long GetPercentile(List<long> values, int percentile)
    {
        var sorted = values.OrderBy(x => x).ToList();
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Max(0, index)];
    }
}
