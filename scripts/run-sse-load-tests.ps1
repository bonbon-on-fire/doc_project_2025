#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Executes SSE load testing scenarios for Orleans integration
.DESCRIPTION
    This script runs the SSE load testing scenarios to validate Orleans SSE performance.
    It tests connection establishment, streaming throughput, and generates performance baselines.
.PARAMETER Scenario
    The specific scenario to run. Options: All, ConnectionLoad, Streaming
.PARAMETER ServerUrl
    The base URL of the server to test against
.PARAMETER OutputPath
    Path where test results should be saved
.EXAMPLE
    .\run-sse-load-tests.ps1 -Scenario All
    .\run-sse-load-tests.ps1 -Scenario ConnectionLoad -ServerUrl https://localhost:7152
#>

param(
    [ValidateSet("All", "ConnectionLoad", "Streaming", "Recovery", "MixedLoad")]
    [string]$Scenario = "All",

    [string]$ServerUrl = "https://localhost:7152",

    [string]$OutputPath = ".\load-test-reports"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Colors for output
$colors = @{
    Success = "Green"
    Warning = "Yellow"
    Error = "Red"
    Info = "Cyan"
    Header = "Magenta"
}

function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Type = "Info"
    )
    Write-Host $Message -ForegroundColor $colors[$Type]
}

function Test-Prerequisites {
    Write-ColorOutput "`n=== Checking Prerequisites ===" -Type "Header"

    # Check if .NET 9 is installed
    $dotnetVersion = dotnet --version
    if ($dotnetVersion -notlike "9.*") {
        Write-ColorOutput "Warning: .NET 9 is recommended. Current version: $dotnetVersion" -Type "Warning"
    } else {
        Write-ColorOutput "✓ .NET version: $dotnetVersion" -Type "Success"
    }

    # Check if server is reachable
    try {
        $response = Invoke-WebRequest -Uri "$ServerUrl/health" -Method Get -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -eq 200) {
            Write-ColorOutput "✓ Server is reachable at $ServerUrl" -Type "Success"
        }
    } catch {
        Write-ColorOutput "✗ Server is not reachable at $ServerUrl" -Type "Error"
        Write-ColorOutput "  Please ensure the server is running with Orleans enabled" -Type "Warning"
        Write-ColorOutput "  Run: pwsh build-and-start-server.ps1 -UseOrleans" -Type "Info"
        return $false
    }

    # Check if SSE endpoint exists
    try {
        $sseUrl = "$ServerUrl/api/monitoring/sse/metrics"
        $response = Invoke-WebRequest -Uri $sseUrl -Method Get -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -eq 200) {
            Write-ColorOutput "✓ SSE monitoring endpoint is available" -Type "Success"
        }
    } catch {
        Write-ColorOutput "✗ SSE monitoring endpoint not found" -Type "Error"
        Write-ColorOutput "  The server might not have SSE monitoring enabled" -Type "Warning"
        return $false
    }

    return $true
}

function Build-LoadTestProject {
    Write-ColorOutput "`n=== Building Load Test Project ===" -Type "Header"

    $projectPath = Join-Path $PSScriptRoot "..\server\AIChat.LoadTesting"
    Push-Location $projectPath

    try {
        Write-ColorOutput "Building AIChat.LoadTesting project..." -Type "Info"
        $result = dotnet build --configuration Release --no-restore 2>&1

        if ($LASTEXITCODE -eq 0) {
            Write-ColorOutput "✓ Build succeeded" -Type "Success"
            return $true
        } else {
            Write-ColorOutput "✗ Build failed" -Type "Error"
            Write-Host $result
            return $false
        }
    } finally {
        Pop-Location
    }
}

function Update-Configuration {
    param(
        [string]$ScenarioName
    )

    Write-ColorOutput "`nUpdating configuration for $ScenarioName scenario..." -Type "Info"

    $configPath = Join-Path $PSScriptRoot "..\server\AIChat.LoadTesting\appsettings.json"
    $config = Get-Content $configPath | ConvertFrom-Json

    # Update server URL
    $config.LoadTesting.ServerBaseUrl = $ServerUrl

    # Enable only the selected scenario
    $config.Scenarios.SseConnectionLoad.Enabled = ($ScenarioName -eq "ConnectionLoad")
    $config.Scenarios.SseStreaming.Enabled = ($ScenarioName -eq "Streaming")
    $config.Scenarios.SseRecovery.Enabled = ($ScenarioName -eq "Recovery")
    $config.Scenarios.SseMixedLoad.Enabled = ($ScenarioName -eq "MixedLoad")

    # Disable non-SSE scenarios
    $config.Scenarios.ConnectionLoad.Enabled = $false
    $config.Scenarios.MessageLatency.Enabled = $false
    $config.Scenarios.GrainScaling.Enabled = $false
    $config.Scenarios.BackgroundProcessing.Enabled = $false
    $config.Scenarios.MixedWorkload.Enabled = $false
    $config.Scenarios.EnduranceTest.Enabled = $false

    $config | ConvertTo-Json -Depth 10 | Set-Content $configPath
    Write-ColorOutput "✓ Configuration updated" -Type "Success"
}

function Run-LoadTest {
    param(
        [string]$ScenarioName
    )

    Write-ColorOutput "`n=== Running $ScenarioName Load Test ===" -Type "Header"

    $projectPath = Join-Path $PSScriptRoot "..\server\AIChat.LoadTesting"
    Push-Location $projectPath

    try {
        Write-ColorOutput "Starting load test execution..." -Type "Info"
        Write-ColorOutput "This may take several minutes depending on the scenario" -Type "Warning"

        # Create output directory if it doesn't exist
        if (-not (Test-Path $OutputPath)) {
            New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
        }

        # Run the load test
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $outputFile = Join-Path $OutputPath "sse-load-test-$ScenarioName-$timestamp.txt"

        dotnet run --configuration Release --no-build | Tee-Object -FilePath $outputFile

        if ($LASTEXITCODE -eq 0) {
            Write-ColorOutput "✓ Load test completed successfully" -Type "Success"
            Write-ColorOutput "Results saved to: $outputFile" -Type "Info"

            # Parse and display key metrics
            Display-KeyMetrics -OutputFile $outputFile

            return $true
        } else {
            Write-ColorOutput "✗ Load test failed with exit code: $LASTEXITCODE" -Type "Error"
            return $false
        }
    } finally {
        Pop-Location
    }
}

function Display-KeyMetrics {
    param(
        [string]$OutputFile
    )

    Write-ColorOutput "`n=== Key Performance Metrics ===" -Type "Header"

    if (Test-Path $OutputFile) {
        $content = Get-Content $OutputFile -Raw

        # Extract key metrics using regex patterns
        if ($content -match "Established: (\d+)/(\d+)") {
            $established = $Matches[1]
            $total = $Matches[2]
            Write-ColorOutput "Connections: $established/$total established" -Type "Info"
        }

        if ($content -match "Avg Latency: ([\d.]+)ms") {
            $avgLatency = $Matches[1]
            Write-ColorOutput "Average Latency: $avgLatency ms" -Type "Info"
        }

        if ($content -match "P99 Latency: ([\d.]+)ms") {
            $p99Latency = $Matches[1]
            Write-ColorOutput "P99 Latency: $p99Latency ms" -Type "Info"
        }

        if ($content -match "Throughput: ([\d.]+) chunks/s") {
            $throughput = $Matches[1]
            Write-ColorOutput "Throughput: $throughput chunks/second" -Type "Info"
        }

        if ($content -match "([\d.]+) bytes/s") {
            $bytesPerSec = $Matches[1]
            Write-ColorOutput "Data Rate: $bytesPerSec bytes/second" -Type "Info"
        }
    }
}

function Generate-PerformanceBaseline {
    Write-ColorOutput "`n=== Generating Performance Baseline ===" -Type "Header"

    $baselinePath = Join-Path $OutputPath "sse-performance-baseline.json"
    $baseline = @{
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        ServerUrl = $ServerUrl
        Scenarios = @{}
    }

    # Aggregate results from all test runs
    $testFiles = Get-ChildItem -Path $OutputPath -Filter "sse-load-test-*.txt" |
                 Sort-Object -Property LastWriteTime -Descending |
                 Select-Object -First 10

    foreach ($file in $testFiles) {
        $content = Get-Content $file.FullName -Raw
        $scenarioName = if ($file.Name -match "sse-load-test-(\w+)-") { $Matches[1] } else { "Unknown" }

        if (-not $baseline.Scenarios.ContainsKey($scenarioName)) {
            $baseline.Scenarios[$scenarioName] = @{
                Runs = @()
            }
        }

        $run = @{
            Timestamp = $file.LastWriteTime
            Metrics = @{}
        }

        # Extract metrics
        if ($content -match "Established: (\d+)/(\d+)") {
            $run.Metrics.ConnectionsEstablished = [int]$Matches[1]
            $run.Metrics.ConnectionsAttempted = [int]$Matches[2]
        }

        if ($content -match "Avg Latency: ([\d.]+)ms") {
            $run.Metrics.AverageLatencyMs = [double]$Matches[1]
        }

        if ($content -match "P99 Latency: ([\d.]+)ms") {
            $run.Metrics.P99LatencyMs = [double]$Matches[1]
        }

        $baseline.Scenarios[$scenarioName].Runs += $run
    }

    $baseline | ConvertTo-Json -Depth 10 | Set-Content $baselinePath
    Write-ColorOutput "✓ Performance baseline saved to: $baselinePath" -Type "Success"
}

# Main execution
function Main {
    Write-ColorOutput "`n╔════════════════════════════════════════════════╗" -Type "Header"
    Write-ColorOutput "║       Orleans SSE Load Testing Suite          ║" -Type "Header"
    Write-ColorOutput "║         Task: ORL-ST-P1-002                   ║" -Type "Header"
    Write-ColorOutput "╚════════════════════════════════════════════════╝" -Type "Header"

    # Check prerequisites
    if (-not (Test-Prerequisites)) {
        Write-ColorOutput "`n✗ Prerequisites check failed. Please fix the issues above." -Type "Error"
        exit 1
    }

    # Build the project
    if (-not (Build-LoadTestProject)) {
        Write-ColorOutput "`n✗ Build failed. Please fix compilation errors." -Type "Error"
        exit 1
    }

    # Run scenarios
    $scenarios = if ($Scenario -eq "All") {
        @("ConnectionLoad", "Streaming", "Recovery", "MixedLoad")
    } else {
        @($Scenario)
    }

    $allSucceeded = $true
    foreach ($scenarioName in $scenarios) {
        Update-Configuration -ScenarioName $scenarioName

        if (-not (Run-LoadTest -ScenarioName $scenarioName)) {
            $allSucceeded = $false
            Write-ColorOutput "✗ Scenario $scenarioName failed" -Type "Error"
        } else {
            Write-ColorOutput "✓ Scenario $scenarioName completed" -Type "Success"
        }
    }

    # Generate performance baseline
    Generate-PerformanceBaseline

    # Final summary
    Write-ColorOutput "`n=== Test Execution Summary ===" -Type "Header"
    if ($allSucceeded) {
        Write-ColorOutput "✓ All SSE load tests completed successfully!" -Type "Success"
        Write-ColorOutput "`nNext steps:" -Type "Info"
        Write-ColorOutput "1. Review the performance baseline in: $OutputPath" -Type "Info"
        Write-ColorOutput "2. Compare with acceptance criteria (1000+ connections, <100ms p99)" -Type "Info"
        Write-ColorOutput "3. Document any performance issues or bottlenecks" -Type "Info"
        exit 0
    } else {
        Write-ColorOutput "✗ Some tests failed. Please review the logs." -Type "Error"
        exit 1
    }
}

# Execute main function
Main