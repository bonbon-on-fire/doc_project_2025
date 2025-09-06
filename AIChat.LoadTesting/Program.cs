using System.Reflection;
using System.Text.Json;
using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Models;
using AIChat.LoadTesting.Scenarios;
using AIChat.LoadTesting.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting;

/// <summary>
/// AIChat Load Testing Application
/// Tests Orleans grain integration with 10,000 concurrent users
/// </summary>
class Program
{
    private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    private static IServiceProvider? _serviceProvider;
    private static ILogger<Program>? _logger;

    static async Task<int> Main(string[] args)
    {
        try
        {
            // Setup configuration and services
            var configuration = BuildConfiguration();
            _serviceProvider = BuildServiceProvider(configuration);
            _logger = _serviceProvider.GetRequiredService<ILogger<Program>>();

            _logger.LogInformation("AIChat Load Testing v{Version} starting", Version);

            // Parse command line arguments
            var options = ParseCommandLineOptions(args);
            
            if (options.ShowHelp)
            {
                ShowHelp();
                return 0;
            }

            if (options.ShowVersion)
            {
                Console.WriteLine($"AIChat Load Testing v{Version}");
                return 0;
            }

            // Validate configuration
            var validationErrors = await ValidateConfigurationAsync();
            if (validationErrors.Any())
            {
                _logger.LogError("Configuration validation failed:");
                foreach (var error in validationErrors)
                {
                    _logger.LogError("  - {Error}", error);
                }
                return 1;
            }

            // Execute load tests
            var testRunner = _serviceProvider.GetRequiredService<LoadTestRunner>();
            var report = await testRunner.ExecuteLoadTestsAsync(options);

            // Generate reports
            await GenerateReportsAsync(report, options);

            // Determine exit code based on test results
            var exitCode = report.Summary.TestPassed ? 0 : 1;
            
            _logger.LogInformation("Load testing completed. Exit code: {ExitCode}", exitCode);
            return exitCode;
        }
        catch (Exception ex)
        {
            var logger = _logger ?? CreateConsoleLogger();
            logger.LogError(ex, "Load testing failed with unexpected error");
            return 1;
        }
        finally
        {
            // Cleanup
            if (_serviceProvider is IDisposable disposableServiceProvider)
            {
                disposableServiceProvider.Dispose();
            }
        }
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables("LOADTEST_")
            .Build();
    }

    private static IServiceProvider BuildServiceProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();

        // Configuration
        services.Configure<LoadTestingConfiguration>(configuration.GetSection(LoadTestingConfiguration.SectionName));
        services.Configure<ScenariosConfiguration>(configuration.GetSection(ScenariosConfiguration.SectionName));
        services.Configure<MonitoringConfiguration>(configuration.GetSection(MonitoringConfiguration.SectionName));
        services.Configure<ValidationConfiguration>(configuration.GetSection(ValidationConfiguration.SectionName));
        services.Configure<OrleansConfiguration>(configuration.GetSection(OrleansConfiguration.SectionName));

        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
            });
            
            // Add file logging for detailed logs
            var logLevel = configuration.GetValue("Logging:LogLevel:Default", "Information");
            builder.SetMinimumLevel(Enum.Parse<LogLevel>(logLevel));
        });

        // HTTP Client
        services.AddHttpClient();

        // Core services
        services.AddSingleton<SignalRConnectionManager>();
        services.AddSingleton<SystemMetricsCollector>();
        services.AddSingleton<LoadTestRunner>();

        // Scenarios
        services.AddScoped<ILoadTestScenario, ConnectionLoadScenario>();
        services.AddScoped<ILoadTestScenario, MessageLatencyScenario>();
        services.AddScoped<ILoadTestScenario, GrainScalingScenario>();

        return services.BuildServiceProvider();
    }

    private static async Task<List<string>> ValidateConfigurationAsync()
    {
        var errors = new List<string>();

        try
        {
            var scenarios = _serviceProvider!.GetServices<ILoadTestScenario>();
            
            foreach (var scenario in scenarios.Where(s => s.IsEnabled))
            {
                var scenarioErrors = await scenario.ValidateConfigurationAsync();
                errors.AddRange(scenarioErrors.Select(e => $"{scenario.Name}: {e}"));
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Configuration validation error: {ex.Message}");
        }

        return errors;
    }

    private static async Task GenerateReportsAsync(LoadTestReport report, CommandLineOptions options)
    {
        var reportDir = Path.Combine(Directory.GetCurrentDirectory(), "load-test-reports");
        Directory.CreateDirectory(reportDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        
        // Generate JSON report
        var jsonReportPath = Path.Combine(reportDir, $"load-test-report-{timestamp}.json");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var jsonReport = JsonSerializer.Serialize(report, jsonOptions);
        await File.WriteAllTextAsync(jsonReportPath, jsonReport);

        // Generate summary report
        var summaryReportPath = Path.Combine(reportDir, $"load-test-summary-{timestamp}.txt");
        var summaryReport = GenerateSummaryReport(report);
        await File.WriteAllTextAsync(summaryReportPath, summaryReport);

        // Generate CSV report for metrics analysis
        var csvReportPath = Path.Combine(reportDir, $"load-test-metrics-{timestamp}.csv");
        var csvReport = GenerateMetricsCsv(report);
        await File.WriteAllTextAsync(csvReportPath, csvReport);

        _logger?.LogInformation("Reports generated:");
        _logger?.LogInformation("  JSON Report: {JsonReportPath}", jsonReportPath);
        _logger?.LogInformation("  Summary Report: {SummaryReportPath}", summaryReportPath);
        _logger?.LogInformation("  Metrics CSV: {CsvReportPath}", csvReportPath);

        // Console output
        if (!options.Quiet)
        {
            Console.WriteLine();
            Console.WriteLine("=== LOAD TEST SUMMARY ===");
            Console.WriteLine(GenerateConsoleSummary(report));
        }
    }

    private static string GenerateSummaryReport(LoadTestReport report)
    {
        var summary = new System.Text.StringBuilder();
        
        summary.AppendLine("AIChat Orleans Load Testing Report");
        summary.AppendLine("================================");
        summary.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        summary.AppendLine($"Test Duration: {report.TotalDuration}");
        summary.AppendLine($"Version: {Version}");
        summary.AppendLine();

        summary.AppendLine("OVERALL RESULTS");
        summary.AppendLine("--------------");
        summary.AppendLine($"Test Status: {(report.Summary.TestPassed ? "PASSED" : "FAILED")}");
        summary.AppendLine($"Scenarios: {report.Summary.SuccessfulScenarios}/{report.Summary.TotalScenarios} passed");
        summary.AppendLine($"Connections: {report.Summary.SuccessfulConnections:N0}/{report.Summary.TotalConnections:N0} ({report.Summary.OverallConnectionSuccessRate:P2})");
        summary.AppendLine($"Messages: {report.Summary.DeliveredMessages:N0}/{report.Summary.TotalMessages:N0} ({report.Summary.OverallMessageDeliveryRate:P2})");
        summary.AppendLine($"Average Latency: {report.Summary.AverageLatencyMs:F1}ms");
        summary.AppendLine($"P95 Latency: {report.Summary.P95LatencyMs:F1}ms");
        summary.AppendLine($"Peak CPU: {report.Summary.PeakCpuPercent:F1}%");
        summary.AppendLine($"Peak Memory: {report.Summary.PeakMemoryMB:N0}MB");
        summary.AppendLine($"Max Concurrent Users: {report.Summary.MaxConcurrentUsers:N0}");
        summary.AppendLine();

        summary.AppendLine("ACCEPTANCE CRITERIA VALIDATION");
        summary.AppendLine("----------------------------");
        summary.AppendLine($"All Criteria Pass: {(report.ValidationResults.AllCriteriaPass ? "YES" : "NO")}");
        summary.AppendLine($"10K Users Connected: {(report.ValidationResults.Users10kConnected.Pass ? "PASS" : "FAIL")} - {report.ValidationResults.Users10kConnected.ActualValue}");
        summary.AppendLine($"Messages < 100ms: {(report.ValidationResults.MessagesUnder100ms.Pass ? "PASS" : "FAIL")} - {report.ValidationResults.MessagesUnder100ms.ActualValue}");
        summary.AppendLine($"No Messages Lost: {(report.ValidationResults.NoMessagesLost.Pass ? "PASS" : "FAIL")} - {report.ValidationResults.NoMessagesLost.ActualValue}");
        summary.AppendLine($"System Scales: {(report.ValidationResults.SystemScales.Pass ? "PASS" : "FAIL")} - {report.ValidationResults.SystemScales.ActualValue}");
        summary.AppendLine($"Resources Within Limits: {(report.ValidationResults.ResourcesWithinLimits.Pass ? "PASS" : "FAIL")} - {report.ValidationResults.ResourcesWithinLimits.ActualValue}");
        summary.AppendLine();

        summary.AppendLine("SCENARIO RESULTS");
        summary.AppendLine("---------------");
        foreach (var scenario in report.ScenarioResults)
        {
            summary.AppendLine($"{scenario.ScenarioName}: {(scenario.Success ? "PASSED" : "FAILED")}");
            summary.AppendLine($"  Duration: {scenario.Duration}");
            summary.AppendLine($"  Users: {scenario.SuccessfulConnections:N0}/{scenario.TotalUsers:N0}");
            summary.AppendLine($"  Messages: {scenario.DeliveredMessages:N0}/{scenario.TotalMessages:N0}");
            summary.AppendLine($"  Latency: avg {scenario.LatencyStats.AverageMs:F1}ms, p95 {scenario.LatencyStats.P95Ms:F1}ms");
            
            if (!scenario.Success)
            {
                summary.AppendLine($"  Failure: {scenario.FailureReason}");
            }
            
            if (scenario.Errors.Any())
            {
                summary.AppendLine($"  Errors: {scenario.Errors.Count} (showing first 3)");
                foreach (var error in scenario.Errors.Take(3))
                {
                    summary.AppendLine($"    - {error}");
                }
            }
            summary.AppendLine();
        }

        if (report.Summary.CriticalIssues.Any())
        {
            summary.AppendLine("CRITICAL ISSUES");
            summary.AppendLine("--------------");
            foreach (var issue in report.Summary.CriticalIssues)
            {
                summary.AppendLine($"- {issue}");
            }
            summary.AppendLine();
        }

        return summary.ToString();
    }

    private static string GenerateMetricsCsv(LoadTestReport report)
    {
        var csv = new System.Text.StringBuilder();
        
        // CSV Header
        csv.AppendLine("Scenario,Duration_Seconds,Total_Users,Successful_Connections,Connection_Success_Rate,Total_Messages,Delivered_Messages,Message_Delivery_Rate,Avg_Latency_Ms,P95_Latency_Ms,Max_Latency_Ms,Messages_Per_Second,Avg_CPU_Percent,Max_CPU_Percent,Avg_Memory_MB,Max_Memory_MB,Success");
        
        // Data rows
        foreach (var scenario in report.ScenarioResults)
        {
            csv.AppendLine($"{scenario.ScenarioName},{scenario.Duration.TotalSeconds:F1},{scenario.TotalUsers},{scenario.SuccessfulConnections},{scenario.ConnectionSuccessRate:F4},{scenario.TotalMessages},{scenario.DeliveredMessages},{scenario.MessageDeliveryRate:F4},{scenario.LatencyStats.AverageMs:F2},{scenario.LatencyStats.P95Ms:F2},{scenario.LatencyStats.MaxMs:F2},{scenario.ThroughputStats.MessagesPerSecond:F2},{scenario.ResourceStats.AverageCpuPercent:F1},{scenario.ResourceStats.MaxCpuPercent:F1},{scenario.ResourceStats.AverageMemoryMB},{scenario.ResourceStats.MaxMemoryMB},{scenario.Success}");
        }
        
        return csv.ToString();
    }

    private static string GenerateConsoleSummary(LoadTestReport report)
    {
        var summary = new System.Text.StringBuilder();
        
        summary.AppendLine($"Test Status: {(report.Summary.TestPassed ? "✅ PASSED" : "❌ FAILED")}");
        summary.AppendLine($"Duration: {report.TotalDuration}");
        summary.AppendLine($"Scenarios: {report.Summary.SuccessfulScenarios}/{report.Summary.TotalScenarios}");
        summary.AppendLine($"Max Concurrent Users: {report.Summary.MaxConcurrentUsers:N0}");
        summary.AppendLine($"Connection Success Rate: {report.Summary.OverallConnectionSuccessRate:P2}");
        summary.AppendLine($"Message Delivery Rate: {report.Summary.OverallMessageDeliveryRate:P2}");
        summary.AppendLine($"Average Latency: {report.Summary.AverageLatencyMs:F1}ms");
        summary.AppendLine($"Peak Resources: {report.Summary.PeakCpuPercent:F1}% CPU, {report.Summary.PeakMemoryMB:N0}MB Memory");
        
        summary.AppendLine();
        summary.AppendLine("Acceptance Criteria:");
        summary.AppendLine($"  10K Users Connected: {(report.ValidationResults.Users10kConnected.Pass ? "✅" : "❌")} {report.ValidationResults.Users10kConnected.ActualValue}");
        summary.AppendLine($"  Messages < 100ms: {(report.ValidationResults.MessagesUnder100ms.Pass ? "✅" : "❌")} {report.ValidationResults.MessagesUnder100ms.ActualValue}");
        summary.AppendLine($"  No Messages Lost: {(report.ValidationResults.NoMessagesLost.Pass ? "✅" : "❌")} {report.ValidationResults.NoMessagesLost.ActualValue}");
        summary.AppendLine($"  System Scales: {(report.ValidationResults.SystemScales.Pass ? "✅" : "❌")} {report.ValidationResults.SystemScales.ActualValue}");
        summary.AppendLine($"  Resources Within Limits: {(report.ValidationResults.ResourcesWithinLimits.Pass ? "✅" : "❌")} {report.ValidationResults.ResourcesWithinLimits.ActualValue}");

        return summary.ToString();
    }

    private static CommandLineOptions ParseCommandLineOptions(string[] args)
    {
        var options = new CommandLineOptions();
        
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "-v":
                case "--version":
                    options.ShowVersion = true;
                    break;
                case "-q":
                case "--quiet":
                    options.Quiet = true;
                    break;
                case "--scenario":
                    if (i + 1 < args.Length)
                    {
                        options.SpecificScenarios.Add(args[++i]);
                    }
                    break;
                case "--output-dir":
                    if (i + 1 < args.Length)
                    {
                        options.OutputDirectory = args[++i];
                    }
                    break;
            }
        }
        
        return options;
    }

    private static void ShowHelp()
    {
        Console.WriteLine($"AIChat Load Testing v{Version}");
        Console.WriteLine("Tests Orleans grain integration with up to 10,000 concurrent users");
        Console.WriteLine();
        Console.WriteLine("Usage: AIChat.LoadTesting [OPTIONS]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help              Show this help message");
        Console.WriteLine("  -v, --version          Show version information");
        Console.WriteLine("  -q, --quiet            Suppress console output");
        Console.WriteLine("  --scenario <name>      Run only specific scenario (can be repeated)");
        Console.WriteLine("  --output-dir <dir>     Output directory for reports");
        Console.WriteLine();
        Console.WriteLine("Available Scenarios:");
        Console.WriteLine("  ConnectionLoad         Test 10,000 concurrent connections");
        Console.WriteLine("  MessageLatency         Test message delivery latency < 100ms");
        Console.WriteLine("  GrainScaling          Test Orleans grain scaling behavior");
        Console.WriteLine();
        Console.WriteLine("Configuration:");
        Console.WriteLine("  Edit appsettings.json to modify test parameters");
        Console.WriteLine("  Use environment variables with LOADTEST_ prefix to override settings");
        Console.WriteLine();
        Console.WriteLine("Exit Codes:");
        Console.WriteLine("  0 - All tests passed and acceptance criteria met");
        Console.WriteLine("  1 - Tests failed or acceptance criteria not met");
    }

    private static ILogger<Program> CreateConsoleLogger()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        return loggerFactory.CreateLogger<Program>();
    }
}

/// <summary>
/// Command line options
/// </summary>
public class CommandLineOptions
{
    public bool ShowHelp { get; set; }
    public bool ShowVersion { get; set; }
    public bool Quiet { get; set; }
    public List<string> SpecificScenarios { get; set; } = new();
    public string? OutputDirectory { get; set; }
}