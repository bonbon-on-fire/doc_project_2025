using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using AIChat.Orleans.Tests.TestUtilities.Builders;
using AIChat.Server.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Serilog;
using Serilog.Context;
using Serilog.Core.Enrichers;
using Serilog.Formatting.Compact;
using Xunit;
using Xunit.Abstractions;

namespace AIChat.Orleans.Tests.TestUtilities.Base;

/// <summary>
/// Base class for Orleans integration tests providing common functionality.
/// Implements Template Method pattern for consistent test execution.
/// </summary>
public abstract class OrleansTestBase : IClassFixture<OrleansTestFixture>
{
    private static readonly object _loggerLock = new();
    private static bool _loggerInitialized;
    protected static readonly ILogger TestLogger = Log.ForContext<OrleansTestBase>();

    protected OrleansTestFixture Fixture { get; }
    protected ITestOutputHelper Output { get; }

    protected OrleansTestBase(OrleansTestFixture fixture, ITestOutputHelper output)
    {
        InitializeSerilogOnce();
        Fixture = fixture;
        Output = output;
    }

    private static void InitializeSerilogOnce()
    {
        if (_loggerInitialized)
        {
            return;
        }

        lock (_loggerLock)
        {
            if (_loggerInitialized)
            {
                return;
            }

            // Find solution root and create log directory
            var currentDir = Directory.GetCurrentDirectory();
            var solutionRoot = currentDir;

            // Navigate up to find solution root
            while (solutionRoot != null && !Directory.Exists(Path.Combine(solutionRoot, "test-logs")) &&
                   !File.Exists(Path.Combine(solutionRoot, "DOC_Project_2025.sln")))
            {
                var parent = Directory.GetParent(solutionRoot);
                solutionRoot = parent?.FullName;
            }

            var logPath = solutionRoot != null
                ? Path.Combine(solutionRoot, "test-logs")
                : Path.GetFullPath("test-logs");

            Directory.CreateDirectory(logPath);

            var logFileName = Path.Combine(logPath, "orleans-tests-.jsonl");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    formatter: new CompactJsonFormatter(),
                    path: logFileName,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    shared: true
                )
                .CreateLogger();

            _loggerInitialized = true;
        }
    }

    protected IDisposable LogTestContext([CallerMemberName] string testName = "")
    {
        var testClass = GetType().Name;
        return LogContext.Push(
            new PropertyEnricher("TestName", testName),
            new PropertyEnricher("TestClass", testClass)
        );
    }

    protected void LogTestActivity(string message, [CallerMemberName] string testName = "")
    {
        using (LogTestContext(testName))
        {
            TestLogger.Information(message);
        }
    }

    /// <summary>
    /// Creates an HTTP client configured for SSE testing.
    /// </summary>
    protected HttpClient CreateSseClient()
    {
        return Fixture.CreateSseClient();
    }

    /// <summary>
    /// Creates a standard HTTP client from the WebApplicationFactory.
    /// </summary>
    protected HttpClient CreateStandardClient()
    {
        return Fixture.WebAppFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
    }

    /// <summary>
    /// Makes a stream request with the provided request data.
    /// </summary>
    protected async Task<HttpResponseMessage> MakeStreamRequestAsync(CreateChatRequest request)
    {
        using var client = CreateSseClient();
        return await client.PostAsJsonAsync("/api/chat/stream-sse", request);
    }

    /// <summary>
    /// Makes a stream request with a simple user ID.
    /// </summary>
    protected async Task<HttpResponseMessage> MakeStreamRequestAsync(string userId)
    {
        var request = CreateChatRequestBuilder.Create().WithUserId(userId).Build();
        return await MakeStreamRequestAsync(request);
    }

    /// <summary>
    /// Extracts a header value from the response.
    /// </summary>
    protected static string? GetHeaderValue(HttpResponseMessage response, string headerName)
    {
        return response.Headers.TryGetValues(headerName, out var values)
            ? values.FirstOrDefault()
            : null;
    }

    /// <summary>
    /// Logs a test step for debugging purposes.
    /// </summary>
    protected void LogTestStep(string step, params object[] args)
    {
        Output.WriteLine(
            $"[{DateTime.UtcNow:HH:mm:ss.fff}] {string.Format(CultureInfo.InvariantCulture, step, args)}"
        );
    }

    /// <summary>
    /// Performs test-specific setup. Override in derived classes.
    /// </summary>
    protected virtual Task SetupAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs test-specific cleanup. Override in derived classes.
    /// </summary>
    protected virtual Task CleanupAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes a test with setup and cleanup.
    /// </summary>
    protected async Task ExecuteTestAsync(Func<Task> testAction)
    {
        try
        {
            await SetupAsync();
            await testAction();
        }
        finally
        {
            await CleanupAsync();
        }
    }

    /// <summary>
    /// Warms up the system by making initial requests.
    /// </summary>
    protected async Task WarmupSystemAsync(int requestCount = 3)
    {
        LogTestStep("Warming up system with {0} requests", requestCount);

        for (var i = 0; i < requestCount; i++)
        {
            var request = CreateChatRequestBuilder
                .Create()
                .WithUserId($"warmup-{i}")
                .WithMessage("Warmup request")
                .Build();

            using var response = await MakeStreamRequestAsync(request);
            _ = response.EnsureSuccessStatusCode();
        }

        LogTestStep("Warmup complete");
    }

    /// <summary>
    /// Asserts that Orleans routing was used.
    /// </summary>
    protected void AssertOrleansRouted(HttpResponseMessage response)
    {
        var orleansRouted = GetHeaderValue(response, "X-Orleans-Routed");
        var processingMode = GetHeaderValue(response, "X-Processing-Mode");

        Assert.Equal("true", orleansRouted);
        Assert.Equal("orleans", processingMode);

        LogTestStep(
            "Verified Orleans routing: routed={0}, mode={1}",
            orleansRouted ?? "null",
            processingMode ?? "null"
        );
    }

    /// <summary>
    /// Asserts that direct processing was used.
    /// </summary>
    protected void AssertDirectProcessing(HttpResponseMessage response)
    {
        var orleansRouted = GetHeaderValue(response, "X-Orleans-Routed");
        var processingMode = GetHeaderValue(response, "X-Processing-Mode");

        Assert.Equal("false", orleansRouted);
        Assert.Equal("direct", processingMode);

        LogTestStep(
            "Verified direct processing: routed={0}, mode={1}",
            orleansRouted ?? "null",
            processingMode ?? "null"
        );
    }
}
