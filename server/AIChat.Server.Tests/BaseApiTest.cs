using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Serilog;
using Serilog.Context;
using Serilog.Core.Enrichers;
using Serilog.Formatting.Compact;
using Xunit;

namespace AIChat.Server.Tests;

/// <summary>
/// Base class for API integration tests that provides properly configured WebApplicationFactory.
/// </summary>
public abstract class BaseApiTest : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly object _loggerLock = new();
    private static bool _loggerInitialized;
    protected static readonly ILogger TestLogger = Log.ForContext<BaseApiTest>();

    protected WebApplicationFactory<Program> Factory { get; }

    protected BaseApiTest(WebApplicationFactory<Program> factory)
    {
        InitializeSerilogOnce();
        Factory = factory.WithWebHostBuilder(builder =>
        {
            _ = builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Test");

            // Set dummy API key for tests to avoid real LLM calls
            Environment.SetEnvironmentVariable("LLM_API_KEY", "DUMMY");

            // Set content root to solution root so agents directory can be found
            var solutionRoot = FindSolutionRoot();
            if (solutionRoot != null)
            {
                _ = builder.UseContentRoot(solutionRoot);
            }
        });
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

            // Get solution root directory for consistent log placement
            var solutionRoot = FindSolutionRoot();
            var logPath = solutionRoot != null
                ? Path.Combine(solutionRoot, "test-logs")
                : Path.GetFullPath("test-logs");

            Directory.CreateDirectory(logPath);

            var logFileName = Path.Combine(logPath, "server-tests-.jsonl");

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
    /// Finds the solution root directory by looking for the .git directory or .sln file.
    /// </summary>
    private static string? FindSolutionRoot()
    {
        var current = Directory.GetCurrentDirectory();

        while (current != null)
        {
            // Look for .git directory or .sln file or agents directory
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                Directory.GetFiles(current, "*.sln").Length > 0 ||
                Directory.Exists(Path.Combine(current, "agents")))
            {
                // Verify we have the agents directory with some .agent.md files
                var agentsPath = Path.Combine(current, "agents");
                if (Directory.Exists(agentsPath) &&
                    Directory.GetFiles(agentsPath, "*.agent.md", SearchOption.AllDirectories).Length > 0)
                {
                    return current;
                }
            }

            current = Directory.GetParent(current)?.FullName;
        }

        // If we can't find the solution root, try a hardcoded path relative to test output
        // This is a fallback for when tests are running from the output directory
        var testAssemblyLocation = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        if (testAssemblyLocation != null)
        {
            // Try going up from the test assembly location
            current = testAssemblyLocation;
            for (int i = 0; i < 10; i++) // Prevent infinite loop
            {
                current = Directory.GetParent(current)?.FullName;
                if (current == null)
                {
                    break;
                }

                var agentsPath = Path.Combine(current, "agents");
                if (Directory.Exists(agentsPath) &&
                    Directory.GetFiles(agentsPath, "*.agent.md", SearchOption.AllDirectories).Length > 0)
                {
                    return current;
                }
            }
        }

        return null;
    }
}