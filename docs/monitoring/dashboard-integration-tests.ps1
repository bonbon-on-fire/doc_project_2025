# Orleans Dashboard Integration Testing and Validation Framework
# Comprehensive testing suite for dashboard functionality, metrics connectivity, and performance validation

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("all", "connectivity", "configuration", "performance", "integration", "synthetic")]
    [string]$TestSuite = "all",

    [Parameter(Mandatory = $false)]
    [string]$Environment = "development",

    [Parameter(Mandatory = $false)]
    [string]$ConfigPath = ".",

    [Parameter(Mandatory = $false)]
    [string]$PrometheusUrl = "http://localhost:9090",

    [Parameter(Mandatory = $false)]
    [string]$GrafanaUrl = "http://localhost:3000",

    [Parameter(Mandatory = $false)]
    [string]$ApiKey = "",

    [Parameter(Mandatory = $false)]
    [int]$TimeoutSeconds = 30,

    [Parameter(Mandatory = $false)]
    [string]$ReportPath = "./test-results",

    [Parameter(Mandatory = $false)]
    [switch]$GenerateSyntheticData,

    [Parameter(Mandatory = $false)]
    [switch]$Verbose,

    [Parameter(Mandatory = $false)]
    [switch]$ContinueOnError
)

# Test framework classes
class TestResult {
    [string]$TestName
    [string]$TestSuite
    [bool]$Passed
    [string]$Message
    [object]$Data
    [datetime]$StartTime
    [datetime]$EndTime
    [int]$DurationMs
    [string]$ErrorDetails

    TestResult([string]$testName, [string]$testSuite) {
        $this.TestName = $testName
        $this.TestSuite = $testSuite
        $this.StartTime = Get-Date
        $this.Passed = $false
        $this.Message = ""
        $this.Data = $null
        $this.ErrorDetails = ""
    }

    [void]Complete([bool]$passed, [string]$message, [object]$data = $null) {
        $this.EndTime = Get-Date
        $this.DurationMs = [int]($this.EndTime - $this.StartTime).TotalMilliseconds
        $this.Passed = $passed
        $this.Message = $message
        $this.Data = $data
    }

    [void]Fail([string]$message, [string]$errorDetails = "") {
        $this.Complete($false, $message)
        $this.ErrorDetails = $errorDetails
    }

    [void]Pass([string]$message, [object]$data = $null) {
        $this.Complete($true, $message, $data)
    }
}

class TestRunner {
    [System.Collections.ArrayList]$Results
    [hashtable]$Config
    [string]$Environment
    [int]$TimeoutSeconds

    TestRunner([string]$environment, [int]$timeoutSeconds) {
        $this.Results = @()
        $this.Environment = $environment
        $this.TimeoutSeconds = $timeoutSeconds
        $this.LoadConfiguration()
    }

    [void]LoadConfiguration() {
        $this.Config = @{
            development = @{
                PrometheusUrl = "http://localhost:9090"
                GrafanaUrl = "http://localhost:3000"
                ExpectedMetrics = @(
                    "orleans_grain_activations_total",
                    "orleans_grain_operation_duration_seconds",
                    "orleans_grain_operation_errors_total"
                )
                PerformanceThresholds = @{
                    DashboardLoadTime = 3000  # ms
                    QueryResponseTime = 1000  # ms
                    MetricAvailability = 80   # percent
                }
                SyntheticData = @{
                    ActiveGrains = 100
                    ErrorRate = 0.01
                    LatencyP95 = 50
                }
            }
            test = @{
                PrometheusUrl = "http://test-prometheus:9090"
                GrafanaUrl = "http://test-grafana:3000"
                ExpectedMetrics = @(
                    "orleans_grain_activations_total",
                    "orleans_grain_operation_duration_seconds",
                    "orleans_grain_operation_errors_total",
                    "orleans_grain_state_size_bytes"
                )
                PerformanceThresholds = @{
                    DashboardLoadTime = 2000
                    QueryResponseTime = 500
                    MetricAvailability = 90
                }
                SyntheticData = @{
                    ActiveGrains = 500
                    ErrorRate = 0.005
                    LatencyP95 = 30
                }
            }
            production = @{
                PrometheusUrl = "https://prometheus.production.company.com"
                GrafanaUrl = "https://grafana.production.company.com"
                ExpectedMetrics = @(
                    "orleans_grain_activations_total",
                    "orleans_grain_operation_duration_seconds",
                    "orleans_grain_operation_errors_total",
                    "orleans_grain_state_size_bytes",
                    "orleans_silo_memory_usage_bytes",
                    "orleans_cluster_membership_total"
                )
                PerformanceThresholds = @{
                    DashboardLoadTime = 1500
                    QueryResponseTime = 300
                    MetricAvailability = 95
                }
                SyntheticData = @{
                    ActiveGrains = 10000
                    ErrorRate = 0.001
                    LatencyP95 = 20
                }
            }
        }
    }

    [TestResult]RunTest([string]$testName, [string]$testSuite, [scriptblock]$testScript) {
        $result = [TestResult]::new($testName, $testSuite)

        Write-Host "🧪 Running test: $testName" -ForegroundColor Cyan

        try {
            $testOutput = & $testScript
            if ($testOutput -is [bool]) {
                if ($testOutput) {
                    $result.Pass("Test passed")
                } else {
                    $result.Fail("Test failed")
                }
            } elseif ($testOutput -is [hashtable] -and $testOutput.ContainsKey("Success")) {
                if ($testOutput.Success) {
                    $result.Pass($testOutput.Message, $testOutput.Data)
                } else {
                    $result.Fail($testOutput.Message, $testOutput.ErrorDetails)
                }
            } else {
                $result.Pass("Test completed", $testOutput)
            }
        }
        catch {
            $result.Fail("Test failed with exception: $($_.Exception.Message)", $_.ScriptStackTrace)
        }

        $statusIcon = if ($result.Passed) { "✅" } else { "❌" }
        $statusColor = if ($result.Passed) { "Green" } else { "Red" }
        Write-Host "$statusIcon $testName - $($result.Message) ($($result.DurationMs)ms)" -ForegroundColor $statusColor

        $this.Results.Add($result) | Out-Null
        return $result
    }

    [object]GetSummary() {
        $passed = ($this.Results | Where-Object { $_.Passed }).Count
        $failed = ($this.Results | Where-Object { -not $_.Passed }).Count
        $totalDuration = ($this.Results | Measure-Object -Property DurationMs -Sum).Sum

        return @{
            TotalTests = $this.Results.Count
            Passed = $passed
            Failed = $failed
            SuccessRate = if ($this.Results.Count -gt 0) { [math]::Round(($passed / $this.Results.Count) * 100, 2) } else { 0 }
            TotalDurationMs = $totalDuration
            Environment = $this.Environment
            TestSuites = ($this.Results | Group-Object TestSuite | ForEach-Object {
                @{
                    Suite = $_.Name
                    Count = $_.Count
                    Passed = ($_.Group | Where-Object { $_.Passed }).Count
                    Failed = ($_.Group | Where-Object { -not $_.Passed }).Count
                }
            })
        }
    }
}

# Test implementations
function Test-PrometheusConnectivity {
    param([TestRunner]$runner)

    $prometheusUrl = $runner.Config[$runner.Environment].PrometheusUrl

    try {
        $response = Invoke-WebRequest -Uri "$prometheusUrl/api/v1/query?query=up" -Method Get -TimeoutSec $runner.TimeoutSeconds -UseBasicParsing

        if ($response.StatusCode -eq 200) {
            $data = $response.Content | ConvertFrom-Json
            if ($data.status -eq "success") {
                return @{
                    Success = $true
                    Message = "Prometheus connectivity verified"
                    Data = @{
                        Url = $prometheusUrl
                        ResponseTime = $response.Headers["X-Response-Time-Ms"]
                        Status = $data.status
                    }
                }
            } else {
                return @{
                    Success = $false
                    Message = "Prometheus query failed: $($data.error)"
                    ErrorDetails = $data.errorType
                }
            }
        } else {
            return @{
                Success = $false
                Message = "Prometheus returned status code: $($response.StatusCode)"
                ErrorDetails = $response.Content
            }
        }
    }
    catch {
        return @{
            Success = $false
            Message = "Failed to connect to Prometheus: $($_.Exception.Message)"
            ErrorDetails = $_.Exception.ToString()
        }
    }
}

function Test-GrafanaConnectivity {
    param([TestRunner]$runner, [string]$apiKey)

    $grafanaUrl = $runner.Config[$runner.Environment].GrafanaUrl

    try {
        $headers = @{}
        if ($apiKey) {
            $headers["Authorization"] = "Bearer $apiKey"
        }

        $response = Invoke-WebRequest -Uri "$grafanaUrl/api/health" -Headers $headers -Method Get -TimeoutSec $runner.TimeoutSeconds -UseBasicParsing

        if ($response.StatusCode -eq 200) {
            $data = $response.Content | ConvertFrom-Json
            return @{
                Success = $true
                Message = "Grafana connectivity verified"
                Data = @{
                    Url = $grafanaUrl
                    Database = $data.database
                    Version = $data.version
                }
            }
        } else {
            return @{
                Success = $false
                Message = "Grafana returned status code: $($response.StatusCode)"
                ErrorDetails = $response.Content
            }
        }
    }
    catch {
        return @{
            Success = $false
            Message = "Failed to connect to Grafana: $($_.Exception.Message)"
            ErrorDetails = $_.Exception.ToString()
        }
    }
}

function Test-OrleansMetricsAvailability {
    param([TestRunner]$runner)

    $prometheusUrl = $runner.Config[$runner.Environment].PrometheusUrl
    $expectedMetrics = $runner.Config[$runner.Environment].ExpectedMetrics
    $availableMetrics = @()
    $missingMetrics = @()

    foreach ($metric in $expectedMetrics) {
        try {
            $response = Invoke-WebRequest -Uri "$prometheusUrl/api/v1/query?query=$metric" -Method Get -TimeoutSec $runner.TimeoutSeconds -UseBasicParsing

            if ($response.StatusCode -eq 200) {
                $data = $response.Content | ConvertFrom-Json
                if ($data.status -eq "success" -and $data.data.result.Count -gt 0) {
                    $availableMetrics += $metric
                } else {
                    $missingMetrics += $metric
                }
            } else {
                $missingMetrics += $metric
            }
        }
        catch {
            $missingMetrics += $metric
        }
    }

    $availability = if ($expectedMetrics.Count -gt 0) {
        [math]::Round(($availableMetrics.Count / $expectedMetrics.Count) * 100, 2)
    } else {
        0
    }

    $threshold = $runner.Config[$runner.Environment].PerformanceThresholds.MetricAvailability
    $success = $availability -ge $threshold

    return @{
        Success = $success
        Message = if ($success) {
            "Orleans metrics availability: $availability% (threshold: $threshold%)"
        } else {
            "Orleans metrics availability below threshold: $availability% < $threshold%"
        }
        Data = @{
            AvailabilityPercent = $availability
            AvailableMetrics = $availableMetrics
            MissingMetrics = $missingMetrics
            Threshold = $threshold
        }
        ErrorDetails = if (-not $success) {
            "Missing metrics: $($missingMetrics -join ', ')"
        } else {
            ""
        }
    }
}

function Test-DashboardConfiguration {
    param([TestRunner]$runner, [string]$configPath)

    $dashboardFiles = Get-ChildItem -Path $configPath -Filter "*dashboard*.json" -File

    if ($dashboardFiles.Count -eq 0) {
        return @{
            Success = $false
            Message = "No dashboard files found in $configPath"
            ErrorDetails = "Expected to find dashboard JSON files for testing"
        }
    }

    $validDashboards = 0
    $invalidDashboards = @()
    $configurationIssues = @()

    foreach ($file in $dashboardFiles) {
        try {
            $content = Get-Content $file.FullName -Raw | ConvertFrom-Json

            if (-not $content.dashboard) {
                $invalidDashboards += $file.Name
                $configurationIssues += "$($file.Name): Missing 'dashboard' root property"
                continue
            }

            if (-not $content.dashboard.title) {
                $configurationIssues += "$($file.Name): Missing dashboard title"
            }

            if (-not $content.dashboard.panels -or $content.dashboard.panels.Count -eq 0) {
                $configurationIssues += "$($file.Name): Dashboard has no panels"
            }

            # Check for fallback queries (resilient design)
            $hasFallbacks = $false
            foreach ($panel in $content.dashboard.panels) {
                if ($panel.targets) {
                    foreach ($target in $panel.targets) {
                        if ($target.expr -and $target.expr -match "OR on\(\) vector\(") {
                            $hasFallbacks = $true
                            break
                        }
                    }
                }
                if ($hasFallbacks) { break }
            }

            if (-not $hasFallbacks) {
                $configurationIssues += "$($file.Name): Dashboard lacks fallback queries for resilient design"
            }

            $validDashboards++
        }
        catch {
            $invalidDashboards += $file.Name
            $configurationIssues += "$($file.Name): JSON parse error - $($_.Exception.Message)"
        }
    }

    $success = $invalidDashboards.Count -eq 0

    return @{
        Success = $success
        Message = if ($success) {
            "All $($dashboardFiles.Count) dashboard configurations are valid"
        } else {
            "$($invalidDashboards.Count) dashboard configurations have issues"
        }
        Data = @{
            TotalDashboards = $dashboardFiles.Count
            ValidDashboards = $validDashboards
            InvalidDashboards = $invalidDashboards
            ConfigurationIssues = $configurationIssues
        }
        ErrorDetails = if (-not $success) {
            $configurationIssues -join "; "
        } else {
            ""
        }
    }
}

function Test-DashboardPerformance {
    param([TestRunner]$runner, [string]$grafanaUrl, [string]$apiKey)

    if (-not $apiKey) {
        return @{
            Success = $false
            Message = "API key required for dashboard performance testing"
            ErrorDetails = "Cannot test dashboard loading times without Grafana API access"
        }
    }

    $headers = @{ "Authorization" = "Bearer $apiKey" }
    $threshold = $runner.Config[$runner.Environment].PerformanceThresholds.DashboardLoadTime

    try {
        $startTime = Get-Date
        $response = Invoke-WebRequest -Uri "$grafanaUrl/api/dashboards/home" -Headers $headers -Method Get -TimeoutSec $runner.TimeoutSeconds -UseBasicParsing
        $endTime = Get-Date
        $loadTime = [int]($endTime - $startTime).TotalMilliseconds

        $success = $loadTime -le $threshold

        return @{
            Success = $success
            Message = if ($success) {
                "Dashboard load time: ${loadTime}ms (threshold: ${threshold}ms)"
            } else {
                "Dashboard load time exceeds threshold: ${loadTime}ms > ${threshold}ms"
            }
            Data = @{
                LoadTimeMs = $loadTime
                ThresholdMs = $threshold
                ResponseCode = $response.StatusCode
            }
            ErrorDetails = if (-not $success) {
                "Performance threshold exceeded"
            } else {
                ""
            }
        }
    }
    catch {
        return @{
            Success = $false
            Message = "Dashboard performance test failed: $($_.Exception.Message)"
            ErrorDetails = $_.Exception.ToString()
        }
    }
}

function Test-SyntheticDataGeneration {
    param([TestRunner]$runner)

    $syntheticData = $runner.Config[$runner.Environment].SyntheticData

    try {
        # Simulate synthetic metrics data generation
        $metrics = @{
            "orleans_grain_activations_total" = $syntheticData.ActiveGrains
            "orleans_grain_operation_errors_total" = [math]::Round($syntheticData.ActiveGrains * $syntheticData.ErrorRate)
            "orleans_grain_operation_duration_seconds" = @{
                p95 = $syntheticData.LatencyP95 / 1000.0
                avg = ($syntheticData.LatencyP95 / 1000.0) * 0.6
            }
        }

        # Validate synthetic data ranges
        $validationErrors = @()

        if ($metrics["orleans_grain_activations_total"] -le 0) {
            $validationErrors += "Active grains must be positive"
        }

        if ($metrics["orleans_grain_operation_errors_total"] -lt 0) {
            $validationErrors += "Error count cannot be negative"
        }

        if ($metrics["orleans_grain_operation_duration_seconds"].p95 -le 0) {
            $validationErrors += "Latency must be positive"
        }

        $success = $validationErrors.Count -eq 0

        return @{
            Success = $success
            Message = if ($success) {
                "Synthetic data generation successful for $($metrics.Keys.Count) metrics"
            } else {
                "Synthetic data validation failed: $($validationErrors -join ', ')"
            }
            Data = @{
                GeneratedMetrics = $metrics
                ValidationErrors = $validationErrors
                Environment = $runner.Environment
            }
            ErrorDetails = if (-not $success) {
                $validationErrors -join "; "
            } else {
                ""
            }
        }
    }
    catch {
        return @{
            Success = $false
            Message = "Synthetic data generation failed: $($_.Exception.Message)"
            ErrorDetails = $_.Exception.ToString()
        }
    }
}

# Main test execution
function Invoke-DashboardIntegrationTests {
    Write-Host "🧪 Orleans Dashboard Integration Testing Framework" -ForegroundColor Cyan
    Write-Host "=================================================" -ForegroundColor Cyan
    Write-Host "Environment: $Environment" -ForegroundColor Cyan
    Write-Host "Test Suite: $TestSuite" -ForegroundColor Cyan
    Write-Host "Timeout: $TimeoutSeconds seconds" -ForegroundColor Cyan
    Write-Host ""

    $runner = [TestRunner]::new($Environment, $TimeoutSeconds)

    # Run test suites based on selection
    if ($TestSuite -eq "all" -or $TestSuite -eq "connectivity") {
        Write-Host "🔌 Connectivity Tests" -ForegroundColor Yellow
        Write-Host "===================" -ForegroundColor Yellow

        $runner.RunTest("Prometheus Connectivity", "connectivity", {
            Test-PrometheusConnectivity -runner $runner
        })

        $runner.RunTest("Grafana Connectivity", "connectivity", {
            Test-GrafanaConnectivity -runner $runner -apiKey $ApiKey
        })

        $runner.RunTest("Orleans Metrics Availability", "connectivity", {
            Test-OrleansMetricsAvailability -runner $runner
        })
        Write-Host ""
    }

    if ($TestSuite -eq "all" -or $TestSuite -eq "configuration") {
        Write-Host "⚙️ Configuration Tests" -ForegroundColor Yellow
        Write-Host "=====================" -ForegroundColor Yellow

        $runner.RunTest("Dashboard Configuration Validation", "configuration", {
            Test-DashboardConfiguration -runner $runner -configPath $ConfigPath
        })
        Write-Host ""
    }

    if ($TestSuite -eq "all" -or $TestSuite -eq "performance") {
        Write-Host "⚡ Performance Tests" -ForegroundColor Yellow
        Write-Host "==================" -ForegroundColor Yellow

        $runner.RunTest("Dashboard Load Performance", "performance", {
            Test-DashboardPerformance -runner $runner -grafanaUrl $GrafanaUrl -apiKey $ApiKey
        })
        Write-Host ""
    }

    if ($TestSuite -eq "all" -or $TestSuite -eq "synthetic") {
        Write-Host "🔬 Synthetic Data Tests" -ForegroundColor Yellow
        Write-Host "======================" -ForegroundColor Yellow

        $runner.RunTest("Synthetic Data Generation", "synthetic", {
            Test-SyntheticDataGeneration -runner $runner
        })
        Write-Host ""
    }

    # Generate test summary
    $summary = $runner.GetSummary()

    Write-Host "📊 Test Summary" -ForegroundColor Cyan
    Write-Host "===============" -ForegroundColor Cyan
    Write-Host "Total Tests: $($summary.TotalTests)" -ForegroundColor White
    Write-Host "Passed: " -NoNewline -ForegroundColor White
    Write-Host "$($summary.Passed)" -ForegroundColor Green
    Write-Host "Failed: " -NoNewline -ForegroundColor White
    Write-Host "$($summary.Failed)" -ForegroundColor Red
    Write-Host "Success Rate: $($summary.SuccessRate)%" -ForegroundColor $(if ($summary.SuccessRate -ge 80) { "Green" } elseif ($summary.SuccessRate -ge 60) { "Yellow" } else { "Red" })
    Write-Host "Total Duration: $($summary.TotalDurationMs)ms" -ForegroundColor White
    Write-Host ""

    # Test suite breakdown
    if ($summary.TestSuites.Count -gt 1) {
        Write-Host "Test Suite Breakdown:" -ForegroundColor White
        foreach ($suite in $summary.TestSuites) {
            $suiteRate = if ($suite.Count -gt 0) { [math]::Round(($suite.Passed / $suite.Count) * 100, 1) } else { 0 }
            Write-Host "  $($suite.Suite): $($suite.Passed)/$($suite.Count) passed ($suiteRate%)" -ForegroundColor White
        }
        Write-Host ""
    }

    # Generate detailed report if requested
    if ($ReportPath) {
        if (-not (Test-Path $ReportPath)) {
            New-Item -ItemType Directory -Path $ReportPath -Force | Out-Null
        }

        $reportFile = Join-Path $ReportPath "dashboard-test-results-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"

        $fullReport = @{
            Summary = $summary
            TestResults = $runner.Results
            Environment = $Environment
            Configuration = $runner.Config[$Environment]
            Timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
        }

        $fullReport | ConvertTo-Json -Depth 10 | Out-File -FilePath $reportFile -Encoding UTF8
        Write-Host "📄 Detailed report saved to: $reportFile" -ForegroundColor Blue
    }

    # Exit with appropriate code
    $exitCode = if ($summary.Failed -eq 0) { 0 } else { 1 }
    Write-Host "Test execution completed with exit code: $exitCode" -ForegroundColor $(if ($exitCode -eq 0) { "Green" } else { "Red" })

    return $exitCode
}

# Entry point
try {
    $exitCode = Invoke-DashboardIntegrationTests
    exit $exitCode
}
catch {
    Write-Host "CRITICAL TEST FAILURE: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    exit 1
}