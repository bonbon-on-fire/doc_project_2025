# Orleans Dashboard Configuration Validation System
# This script provides production-quality validation for dashboard configurations
# Following SOLID principles with comprehensive error handling and logging

param(
    [Parameter(Mandatory = $false)]
    [string]$ConfigPath = ".",

    [Parameter(Mandatory = $false)]
    [string]$Environment = "development",

    [Parameter(Mandatory = $false)]
    [string]$LogLevel = "INFO",

    [Parameter(Mandatory = $false)]
    [switch]$ValidateMetrics,

    [Parameter(Mandatory = $false)]
    [switch]$ValidateDataSources,

    [Parameter(Mandatory = $false)]
    [switch]$GenerateReport,

    [Parameter(Mandatory = $false)]
    [string]$ReportPath = "./validation-report.json"
)

# Configuration validation rules and environment mappings
class DashboardValidator {
    [string]$Environment
    [hashtable]$Config
    [System.Collections.ArrayList]$Errors
    [System.Collections.ArrayList]$Warnings
    [System.Collections.ArrayList]$Metrics

    DashboardValidator([string]$env) {
        $this.Environment = $env
        $this.Errors = @()
        $this.Warnings = @()
        $this.Metrics = @()
        $this.LoadEnvironmentConfig()
    }

    [void]LoadEnvironmentConfig() {
        $this.Config = @{
            development = @{
                PrometheusUrl = "http://localhost:9090"
                GrafanaUrl = "http://localhost:3000"
                RequiredMetrics = @(
                    "orleans_grain_activations_total",
                    "orleans_grain_operation_duration_seconds",
                    "orleans_grain_operation_errors_total",
                    "orleans_grain_state_size_bytes"
                )
                OptionalMetrics = @(
                    "orleans_silo_memory_usage_bytes",
                    "orleans_cluster_membership_total"
                )
                MaxQueryComplexity = 100
                MaxPanelsPerDashboard = 50
            }
            test = @{
                PrometheusUrl = "http://test-prometheus:9090"
                GrafanaUrl = "http://test-grafana:3000"
                RequiredMetrics = @(
                    "orleans_grain_activations_total",
                    "orleans_grain_operation_duration_seconds",
                    "orleans_grain_operation_errors_total"
                )
                OptionalMetrics = @()
                MaxQueryComplexity = 50
                MaxPanelsPerDashboard = 30
            }
            production = @{
                PrometheusUrl = "https://prometheus.production.company.com"
                GrafanaUrl = "https://grafana.production.company.com"
                RequiredMetrics = @(
                    "orleans_grain_activations_total",
                    "orleans_grain_operation_duration_seconds",
                    "orleans_grain_operation_errors_total",
                    "orleans_grain_state_size_bytes",
                    "orleans_silo_memory_usage_bytes",
                    "orleans_cluster_membership_total"
                )
                OptionalMetrics = @(
                    "orleans_grain_collection_events_total",
                    "orleans_request_queue_length"
                )
                MaxQueryComplexity = 200
                MaxPanelsPerDashboard = 100
            }
        }
    }

    [bool]ValidateDashboardFile([string]$filePath) {
        Write-Host "Validating dashboard file: $filePath" -ForegroundColor Blue

        if (-not (Test-Path $filePath)) {
            $this.AddError("Dashboard file not found: $filePath")
            return $false
        }

        try {
            $dashboard = Get-Content $filePath -Raw | ConvertFrom-Json
            return $this.ValidateDashboardStructure($dashboard, $filePath)
        }
        catch {
            $this.AddError("Failed to parse dashboard JSON in $filePath`: $($_.Exception.Message)")
            return $false
        }
    }

    [bool]ValidateDashboardStructure([object]$dashboard, [string]$filePath) {
        $isValid = $true

        # Validate required dashboard properties
        if (-not $dashboard.dashboard) {
            $this.AddError("Missing 'dashboard' root property in $filePath")
            $isValid = $false
        }

        if (-not $dashboard.dashboard.title) {
            $this.AddError("Missing dashboard title in $filePath")
            $isValid = $false
        }

        if (-not $dashboard.dashboard.panels -or $dashboard.dashboard.panels.Count -eq 0) {
            $this.AddWarning("Dashboard has no panels in $filePath")
        }

        # Validate panel count limits
        if ($dashboard.dashboard.panels.Count -gt $this.Config[$this.Environment].MaxPanelsPerDashboard) {
            $this.AddWarning("Dashboard exceeds recommended panel count ($($dashboard.dashboard.panels.Count) > $($this.Config[$this.Environment].MaxPanelsPerDashboard)) in $filePath")
        }

        # Validate each panel
        foreach ($panel in $dashboard.dashboard.panels) {
            if (-not $this.ValidatePanel($panel, $filePath)) {
                $isValid = $false
            }
        }

        return $isValid
    }

    [bool]ValidatePanel([object]$panel, [string]$filePath) {
        $isValid = $true

        if (-not $panel.title) {
            $this.AddWarning("Panel missing title in $filePath")
        }

        if (-not $panel.type) {
            $this.AddError("Panel missing type in $filePath")
            $isValid = $false
        }

        # Validate panel targets (queries)
        if ($panel.targets) {
            foreach ($target in $panel.targets) {
                if (-not $this.ValidateQuery($target, $filePath)) {
                    $isValid = $false
                }
            }
        }

        return $isValid
    }

    [bool]ValidateQuery([object]$target, [string]$filePath) {
        if (-not $target.expr) {
            $this.AddWarning("Query target missing expression in $filePath")
            return $true  # Not critical
        }

        # Extract metric names from query
        $metricPattern = '[a-zA-Z_][a-zA-Z0-9_]*(?:\{[^}]*\})?'
        $metrics = [regex]::Matches($target.expr, $metricPattern) | ForEach-Object { $_.Value.Split('{')[0] }

        foreach ($metric in $metrics) {
            if ($metric -match '^orleans_') {
                $this.Metrics.Add($metric) | Out-Null
            }
        }

        # Validate query complexity (simple heuristic)
        $complexity = ($target.expr -split '\+|\-|\*|\/|\(|\)').Count
        if ($complexity -gt $this.Config[$this.Environment].MaxQueryComplexity) {
            $this.AddWarning("Query complexity may be too high ($complexity operations) in $filePath")
        }

        return $true
    }

    [bool]ValidateMetricAvailability() {
        if (-not $this.Metrics -or $this.Metrics.Count -eq 0) {
            $this.AddWarning("No Orleans metrics found in dashboard queries")
            return $true
        }

        $uniqueMetrics = $this.Metrics | Select-Object -Unique
        $requiredMetrics = $this.Config[$this.Environment].RequiredMetrics

        foreach ($required in $requiredMetrics) {
            if ($required -notin $uniqueMetrics) {
                $this.AddError("Required metric '$required' not found in any dashboard")
            }
        }

        # Check for unknown metrics (potential typos)
        $knownMetrics = $requiredMetrics + $this.Config[$this.Environment].OptionalMetrics
        foreach ($metric in $uniqueMetrics) {
            if ($metric -notin $knownMetrics -and $metric -match '^orleans_') {
                $this.AddWarning("Unknown Orleans metric '$metric' - verify it exists in Prometheus")
            }
        }

        return $this.Errors.Count -eq 0
    }

    [bool]ValidateDataSourceConnectivity() {
        $isValid = $true

        try {
            # Test Prometheus connectivity
            $prometheusUrl = $this.Config[$this.Environment].PrometheusUrl
            $response = Invoke-WebRequest -Uri "$prometheusUrl/api/v1/query?query=up" -Method Get -TimeoutSec 10 -UseBasicParsing

            if ($response.StatusCode -ne 200) {
                $this.AddError("Cannot connect to Prometheus at $prometheusUrl (Status: $($response.StatusCode))")
                $isValid = $false
            } else {
                Write-Host "Prometheus connectivity verified: $prometheusUrl" -ForegroundColor Green
            }
        }
        catch {
            $this.AddError("Prometheus connectivity failed: $($_.Exception.Message)")
            $isValid = $false
        }

        try {
            # Test Grafana connectivity
            $grafanaUrl = $this.Config[$this.Environment].GrafanaUrl
            $response = Invoke-WebRequest -Uri "$grafanaUrl/api/health" -Method Get -TimeoutSec 10 -UseBasicParsing

            if ($response.StatusCode -ne 200) {
                $this.AddError("Cannot connect to Grafana at $grafanaUrl (Status: $($response.StatusCode))")
                $isValid = $false
            } else {
                Write-Host "Grafana connectivity verified: $grafanaUrl" -ForegroundColor Green
            }
        }
        catch {
            $this.AddError("Grafana connectivity failed: $($_.Exception.Message)")
            $isValid = $false
        }

        return $isValid
    }

    [void]AddError([string]$message) {
        $this.Errors.Add(@{
            Level = "ERROR"
            Message = $message
            Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        }) | Out-Null
        Write-Host "ERROR: $message" -ForegroundColor Red
    }

    [void]AddWarning([string]$message) {
        $this.Warnings.Add(@{
            Level = "WARNING"
            Message = $message
            Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        }) | Out-Null
        Write-Host "WARNING: $message" -ForegroundColor Yellow
    }

    [object]GenerateReport() {
        return @{
            Environment = $this.Environment
            ValidationTimestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
            Summary = @{
                ErrorCount = $this.Errors.Count
                WarningCount = $this.Warnings.Count
                MetricsFound = ($this.Metrics | Select-Object -Unique).Count
                OverallStatus = if ($this.Errors.Count -eq 0) { "PASS" } else { "FAIL" }
            }
            Errors = $this.Errors
            Warnings = $this.Warnings
            Metrics = $this.Metrics | Select-Object -Unique | Sort-Object
            Configuration = $this.Config[$this.Environment]
        }
    }
}

# Main validation logic
function Invoke-DashboardValidation {
    Write-Host "Starting Orleans Dashboard Configuration Validation" -ForegroundColor Cyan
    Write-Host "Environment: $Environment" -ForegroundColor Cyan
    Write-Host "Configuration Path: $ConfigPath" -ForegroundColor Cyan
    Write-Host ""

    $validator = [DashboardValidator]::new($Environment)
    $dashboardFiles = Get-ChildItem -Path $ConfigPath -Filter "*dashboard*.json" -File

    if ($dashboardFiles.Count -eq 0) {
        Write-Host "No dashboard files found in $ConfigPath" -ForegroundColor Red
        return $false
    }

    Write-Host "Found $($dashboardFiles.Count) dashboard files to validate" -ForegroundColor Blue

    $overallValid = $true
    foreach ($file in $dashboardFiles) {
        if (-not $validator.ValidateDashboardFile($file.FullName)) {
            $overallValid = $false
        }
    }

    # Validate metric availability if requested
    if ($ValidateMetrics) {
        Write-Host "`nValidating metric availability..." -ForegroundColor Blue
        if (-not $validator.ValidateMetricAvailability()) {
            $overallValid = $false
        }
    }

    # Validate data source connectivity if requested
    if ($ValidateDataSources) {
        Write-Host "`nValidating data source connectivity..." -ForegroundColor Blue
        if (-not $validator.ValidateDataSourceConnectivity()) {
            $overallValid = $false
        }
    }

    # Generate report
    $report = $validator.GenerateReport()

    Write-Host "`n=== VALIDATION SUMMARY ===" -ForegroundColor Cyan
    Write-Host "Overall Status: $($report.Summary.OverallStatus)" -ForegroundColor $(if ($report.Summary.OverallStatus -eq "PASS") { "Green" } else { "Red" })
    Write-Host "Errors: $($report.Summary.ErrorCount)" -ForegroundColor $(if ($report.Summary.ErrorCount -eq 0) { "Green" } else { "Red" })
    Write-Host "Warnings: $($report.Summary.WarningCount)" -ForegroundColor $(if ($report.Summary.WarningCount -eq 0) { "Green" } else { "Yellow" })
    Write-Host "Unique Metrics Found: $($report.Summary.MetricsFound)" -ForegroundColor Blue

    if ($GenerateReport) {
        $report | ConvertTo-Json -Depth 10 | Out-File -FilePath $ReportPath -Encoding UTF8
        Write-Host "Detailed report saved to: $ReportPath" -ForegroundColor Blue
    }

    return $overallValid
}

# Entry point
try {
    $result = Invoke-DashboardValidation
    exit $(if ($result) { 0 } else { 1 })
}
catch {
    Write-Host "Validation failed with exception: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    exit 1
}