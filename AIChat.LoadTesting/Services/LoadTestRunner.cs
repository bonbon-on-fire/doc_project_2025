using System.Reflection;
using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Models;
using AIChat.LoadTesting.Scenarios;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting.Services;

/// <summary>
/// Orchestrates load testing scenarios and validates acceptance criteria
/// </summary>
public class LoadTestRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LoadTestRunner> _logger;
    private readonly ValidationConfiguration _validationConfig;
    private readonly SystemMetricsCollector _metricsCollector;

    public LoadTestRunner(
        IServiceProvider serviceProvider,
        ILogger<LoadTestRunner> logger,
        IOptions<ValidationConfiguration> validationConfig,
        SystemMetricsCollector metricsCollector)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _validationConfig = validationConfig.Value;
        _metricsCollector = metricsCollector;
    }

    /// <summary>
    /// Executes all enabled load testing scenarios
    /// </summary>
    public async Task<LoadTestReport> ExecuteLoadTestsAsync(CommandLineOptions options)
    {
        var testStartTime = DateTime.UtcNow;
        
        _logger.LogInformation("Starting AIChat Orleans Load Testing");
        _logger.LogInformation("Target: 10,000 concurrent users with <100ms message latency");

        var report = new LoadTestReport
        {
            TestName = "AIChat Orleans Load Testing",
            TestStartTime = testStartTime,
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
            Environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"
        };

        try
        {
            // Get all enabled scenarios
            var scenarios = GetEnabledScenarios(options);
            _logger.LogInformation("Executing {ScenarioCount} scenarios: {ScenarioNames}",
                scenarios.Count, string.Join(", ", scenarios.Select(s => s.Name)));

            var scenarioResults = new List<ScenarioResult>();

            // Execute scenarios sequentially to avoid resource conflicts
            foreach (var scenario in scenarios)
            {
                _logger.LogInformation("=== Starting {ScenarioName} ===", scenario.Name);
                
                var progress = new Progress<TestProgress>(p => ReportProgress(scenario.Name, p));
                var scenarioResult = await ExecuteScenarioWithRetryAsync(scenario, progress);
                
                scenarioResults.Add(scenarioResult);

                _logger.LogInformation("=== {ScenarioName} completed: {Status} ===", 
                    scenario.Name, scenarioResult.Success ? "SUCCESS" : "FAILED");

                if (!scenarioResult.Success)
                {
                    _logger.LogError("Scenario failed: {FailureReason}", scenarioResult.FailureReason);
                }

                // Brief pause between scenarios
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            report.TestEndTime = DateTime.UtcNow;
            report.ScenarioResults = scenarioResults;

            // Generate summary and validate acceptance criteria
            report.Summary = GenerateTestSummary(scenarioResults);
            report.ValidationResults = ValidateAcceptanceCriteria(scenarioResults);

            _logger.LogInformation("Load testing completed in {Duration}. Overall result: {Result}",
                report.TotalDuration, report.Summary.TestPassed ? "PASSED" : "FAILED");

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load testing failed with unexpected error");
            
            report.TestEndTime = DateTime.UtcNow;
            report.Summary.TestPassed = false;
            report.Summary.CriticalIssues.Add($"Unexpected failure: {ex.Message}");
            
            return report;
        }
    }

    private List<ILoadTestScenario> GetEnabledScenarios(CommandLineOptions options)
    {
        var allScenarios = _serviceProvider.GetServices<ILoadTestScenario>().ToList();

        if (options.SpecificScenarios.Any())
        {
            // Filter to only requested scenarios
            var requestedScenarios = allScenarios
                .Where(s => options.SpecificScenarios.Contains(s.Name.Replace(" ", ""), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (!requestedScenarios.Any())
            {
                _logger.LogWarning("No matching scenarios found for: {RequestedScenarios}", 
                    string.Join(", ", options.SpecificScenarios));
            }

            return requestedScenarios;
        }

        // Return all enabled scenarios
        return allScenarios.Where(s => s.IsEnabled).ToList();
    }

    private async Task<ScenarioResult> ExecuteScenarioWithRetryAsync(ILoadTestScenario scenario, IProgress<TestProgress> progress)
    {
        const int maxRetries = 1; // Allow one retry for transient failures
        
        for (int attempt = 1; attempt <= maxRetries + 1; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    _logger.LogInformation("Retrying {ScenarioName} (attempt {Attempt}/{MaxAttempts})", 
                        scenario.Name, attempt, maxRetries + 1);
                    
                    // Wait before retry
                    await Task.Delay(TimeSpan.FromSeconds(10));
                }

                var result = await scenario.ExecuteAsync(progress);
                
                if (result.Success || attempt == maxRetries + 1)
                {
                    return result;
                }
                
                _logger.LogWarning("Scenario {ScenarioName} failed on attempt {Attempt}: {FailureReason}. Retrying...", 
                    scenario.Name, attempt, result.FailureReason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scenario {ScenarioName} threw exception on attempt {Attempt}", scenario.Name, attempt);
                
                if (attempt == maxRetries + 1)
                {
                    return new ScenarioResult
                    {
                        ScenarioName = scenario.Name,
                        StartTime = DateTime.UtcNow,
                        EndTime = DateTime.UtcNow,
                        Success = false,
                        FailureReason = $"Exception after {maxRetries + 1} attempts: {ex.Message}"
                    };
                }
            }
        }

        throw new InvalidOperationException("Should never reach this point");
    }

    private void ReportProgress(string scenarioName, TestProgress progress)
    {
        _logger.LogInformation("{Scenario}: {Progress:F1}% - {ActiveUsers} users, {TotalMessages} messages, Latency: {CurrentLatency:F1}ms",
            scenarioName, progress.ProgressPercent, progress.ActiveUsers, progress.TotalMessages, progress.CurrentLatencyMs);
    }

    private LoadTestSummary GenerateTestSummary(List<ScenarioResult> scenarioResults)
    {
        var summary = new LoadTestSummary
        {
            TotalScenarios = scenarioResults.Count,
            SuccessfulScenarios = scenarioResults.Count(r => r.Success),
            FailedScenarios = scenarioResults.Count(r => !r.Success),
            
            TotalConnections = scenarioResults.Sum(r => r.TotalUsers),
            SuccessfulConnections = scenarioResults.Sum(r => r.SuccessfulConnections),
            
            TotalMessages = scenarioResults.Sum(r => r.TotalMessages),
            DeliveredMessages = scenarioResults.Sum(r => r.DeliveredMessages)
        };

        // Calculate latency statistics
        var allLatencies = scenarioResults
            .Where(r => r.LatencyStats.SampleCount > 0)
            .Select(r => r.LatencyStats.AverageMs)
            .ToList();

        if (allLatencies.Any())
        {
            summary.AverageLatencyMs = allLatencies.Average();
            summary.P95LatencyMs = scenarioResults
                .Where(r => r.LatencyStats.SampleCount > 0)
                .Average(r => r.LatencyStats.P95Ms);
            summary.MaxLatencyMs = scenarioResults
                .Where(r => r.LatencyStats.SampleCount > 0)
                .Max(r => r.LatencyStats.MaxMs);
        }

        // Resource statistics
        var resourceStats = _metricsCollector.GetResourceUsageStatistics();
        summary.PeakCpuPercent = resourceStats.MaxCpuPercent;
        summary.PeakMemoryMB = resourceStats.MaxMemoryMB;

        // Find max concurrent users from connection load scenario
        var connectionLoadResult = scenarioResults.FirstOrDefault(r => r.ScenarioName.Contains("Connection Load"));
        summary.MaxConcurrentUsers = connectionLoadResult?.SuccessfulConnections ?? 0;

        // Check for critical issues
        foreach (var result in scenarioResults)
        {
            if (!result.Success)
            {
                summary.CriticalIssues.Add($"{result.ScenarioName}: {result.FailureReason}");
            }

            if (result.LatencyStats.AverageMs > _validationConfig.MaxAllowedLatencyMs)
            {
                summary.CriticalIssues.Add($"{result.ScenarioName}: High latency {result.LatencyStats.AverageMs:F1}ms > {_validationConfig.MaxAllowedLatencyMs}ms");
            }

            if (result.ConnectionSuccessRate < _validationConfig.MinConnectionSuccessRate)
            {
                summary.CriticalIssues.Add($"{result.ScenarioName}: Low connection success rate {result.ConnectionSuccessRate:P2} < {_validationConfig.MinConnectionSuccessRate:P2}");
            }
        }

        if (resourceStats.MaxCpuPercent > _validationConfig.MaxCpuUsagePercent)
        {
            summary.CriticalIssues.Add($"CPU usage exceeded limit: {resourceStats.MaxCpuPercent:F1}% > {_validationConfig.MaxCpuUsagePercent}%");
        }

        if (resourceStats.MaxMemoryMB > _validationConfig.MaxMemoryUsageMB)
        {
            summary.CriticalIssues.Add($"Memory usage exceeded limit: {resourceStats.MaxMemoryMB}MB > {_validationConfig.MaxMemoryUsageMB}MB");
        }

        // Overall test pass/fail determination
        summary.TestPassed = summary.FailedScenarios == 0 && !summary.CriticalIssues.Any();

        return summary;
    }

    private ValidationResults ValidateAcceptanceCriteria(List<ScenarioResult> scenarioResults)
    {
        var validation = new ValidationResults();
        
        // Find relevant scenario results
        var connectionLoadResult = scenarioResults.FirstOrDefault(r => r.ScenarioName.Contains("Connection Load"));
        var messageLatencyResult = scenarioResults.FirstOrDefault(r => r.ScenarioName.Contains("Message Latency"));
        var grainScalingResult = scenarioResults.FirstOrDefault(r => r.ScenarioName.Contains("Grain Scaling"));

        // 1. 10K Users Connect Successfully
        validation.Users10kConnected = ValidateUsers10kConnected(connectionLoadResult);

        // 2. Messages Delivered < 100ms
        validation.MessagesUnder100ms = ValidateMessagesUnder100ms(messageLatencyResult, scenarioResults);

        // 3. No Messages Lost
        validation.NoMessagesLost = ValidateNoMessagesLost(scenarioResults);

        // 4. System Scales Appropriately
        validation.SystemScales = ValidateSystemScales(grainScalingResult, connectionLoadResult);

        // 5. Resources Within Limits
        validation.ResourcesWithinLimits = ValidateResourcesWithinLimits();

        // Overall validation result
        var criteriaResults = new[]
        {
            validation.Users10kConnected,
            validation.MessagesUnder100ms,
            validation.NoMessagesLost,
            validation.SystemScales,
            validation.ResourcesWithinLimits
        };

        validation.AllCriteriaPass = criteriaResults.All(c => c.Pass);

        // Collect failure reasons
        validation.FailureReasons = criteriaResults
            .Where(c => !c.Pass)
            .Select(c => $"{c.Name}: {c.FailureReason}")
            .ToList();

        return validation;
    }

    private ValidationCriterion ValidateUsers10kConnected(ScenarioResult? connectionResult)
    {
        var criterion = new ValidationCriterion
        {
            Name = "10K Users Connected",
            Description = "System must successfully connect 10,000 concurrent users",
            ExpectedValue = ">= 9,900 connections (99% success rate)"
        };

        if (connectionResult == null)
        {
            criterion.Pass = false;
            criterion.ActualValue = "Connection Load scenario not executed";
            criterion.FailureReason = "Connection Load scenario was not run";
            return criterion;
        }

        var minRequired = 9900; // 99% of 10,000
        criterion.ActualValue = $"{connectionResult.SuccessfulConnections:N0} connections ({connectionResult.ConnectionSuccessRate:P2})";
        
        if (connectionResult.SuccessfulConnections >= minRequired)
        {
            criterion.Pass = true;
        }
        else
        {
            criterion.Pass = false;
            criterion.FailureReason = $"Only {connectionResult.SuccessfulConnections:N0} users connected, need >= {minRequired:N0}";
        }

        return criterion;
    }

    private ValidationCriterion ValidateMessagesUnder100ms(ScenarioResult? latencyResult, List<ScenarioResult> allResults)
    {
        var criterion = new ValidationCriterion
        {
            Name = "Messages Under 100ms",
            Description = "Messages must be delivered with average latency < 100ms",
            ExpectedValue = "< 100ms average latency"
        };

        // Use latency-specific result if available, otherwise use all scenarios with messages
        var resultsWithMessages = latencyResult != null 
            ? new[] { latencyResult } 
            : allResults.Where(r => r.TotalMessages > 0);

        if (!resultsWithMessages.Any())
        {
            criterion.Pass = false;
            criterion.ActualValue = "No message latency data available";
            criterion.FailureReason = "No scenarios with message latency testing were executed";
            return criterion;
        }

        var avgLatency = resultsWithMessages
            .Where(r => r.LatencyStats.SampleCount > 0)
            .Average(r => r.LatencyStats.AverageMs);

        criterion.ActualValue = $"{avgLatency:F1}ms average";

        if (avgLatency < _validationConfig.MaxAllowedLatencyMs)
        {
            criterion.Pass = true;
        }
        else
        {
            criterion.Pass = false;
            criterion.FailureReason = $"Average latency {avgLatency:F1}ms exceeds {_validationConfig.MaxAllowedLatencyMs}ms limit";
        }

        return criterion;
    }

    private ValidationCriterion ValidateNoMessagesLost(List<ScenarioResult> scenarioResults)
    {
        var criterion = new ValidationCriterion
        {
            Name = "No Messages Lost",
            Description = "All sent messages must be delivered successfully",
            ExpectedValue = ">= 99% delivery rate"
        };

        var resultsWithMessages = scenarioResults.Where(r => r.TotalMessages > 0).ToList();

        if (!resultsWithMessages.Any())
        {
            criterion.Pass = true; // No messages to lose
            criterion.ActualValue = "No messages sent";
            return criterion;
        }

        var overallDeliveryRate = resultsWithMessages.Sum(r => r.DeliveredMessages) / 
                                 (double)resultsWithMessages.Sum(r => r.TotalMessages);

        criterion.ActualValue = $"{overallDeliveryRate:P2} delivery rate";

        if (overallDeliveryRate >= 0.99) // 99% delivery rate
        {
            criterion.Pass = true;
        }
        else
        {
            criterion.Pass = false;
            criterion.FailureReason = $"Message delivery rate {overallDeliveryRate:P2} below required 99%";
        }

        return criterion;
    }

    private ValidationCriterion ValidateSystemScales(ScenarioResult? grainScalingResult, ScenarioResult? connectionResult)
    {
        var criterion = new ValidationCriterion
        {
            Name = "System Scales Appropriately",
            Description = "Orleans grains must scale appropriately with load",
            ExpectedValue = "Grain activation responds to load increases"
        };

        // Check if we have scaling data from grain scaling scenario
        if (grainScalingResult?.Success == true)
        {
            criterion.Pass = true;
            criterion.ActualValue = "Grain scaling behavior validated across burst patterns";
            return criterion;
        }

        // Fallback: Check if connection load succeeded as evidence of scaling
        if (connectionResult?.Success == true && connectionResult.SuccessfulConnections >= 5000)
        {
            criterion.Pass = true;
            criterion.ActualValue = $"System handled {connectionResult.SuccessfulConnections:N0} concurrent users successfully";
            return criterion;
        }

        criterion.Pass = false;
        criterion.ActualValue = grainScalingResult?.FailureReason ?? "No scaling validation data available";
        criterion.FailureReason = "Unable to validate grain scaling behavior";

        return criterion;
    }

    private ValidationCriterion ValidateResourcesWithinLimits()
    {
        var criterion = new ValidationCriterion
        {
            Name = "Resources Within Limits",
            Description = "System resources must stay within acceptable limits during testing",
            ExpectedValue = $"CPU < {_validationConfig.MaxCpuUsagePercent}%, Memory < {_validationConfig.MaxMemoryUsageMB}MB"
        };

        var resourceStats = _metricsCollector.GetResourceUsageStatistics();

        var cpuOk = resourceStats.MaxCpuPercent <= _validationConfig.MaxCpuUsagePercent;
        var memoryOk = resourceStats.MaxMemoryMB <= _validationConfig.MaxMemoryUsageMB;

        criterion.ActualValue = $"Peak: {resourceStats.MaxCpuPercent:F1}% CPU, {resourceStats.MaxMemoryMB:N0}MB Memory";

        if (cpuOk && memoryOk)
        {
            criterion.Pass = true;
        }
        else
        {
            criterion.Pass = false;
            var issues = new List<string>();
            
            if (!cpuOk)
                issues.Add($"CPU {resourceStats.MaxCpuPercent:F1}% > {_validationConfig.MaxCpuUsagePercent}%");
            
            if (!memoryOk)
                issues.Add($"Memory {resourceStats.MaxMemoryMB:N0}MB > {_validationConfig.MaxMemoryUsageMB}MB");

            criterion.FailureReason = string.Join(", ", issues);
        }

        return criterion;
    }
}