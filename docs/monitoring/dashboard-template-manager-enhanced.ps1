# Orleans Dashboard Template Management System (Enhanced)
# Production-quality template sharing, versioning, and management with comprehensive error handling

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("list", "install", "update", "remove", "validate", "package", "publish", "search", "health")]
    [string]$Action,

    [Parameter(Mandatory = $false)]
    [ValidatePattern('^[a-zA-Z0-9\-_]+$')]
    [string]$TemplateName = "",

    [Parameter(Mandatory = $false)]
    [ValidatePattern('^(\d+\.\d+\.\d+|latest)$')]
    [string]$TemplateVersion = "latest",

    [Parameter(Mandatory = $false)]
    [ValidateScript({
        if ($_ -eq "") { return $true }
        if (-not (Test-Path $_ -PathType Container)) {
            throw "Repository path '$_' does not exist or is not a directory"
        }
        return $true
    })]
    [string]$RepositoryPath = "./templates",

    [Parameter(Mandatory = $false)]
    [string]$SourcePath = ".",

    [Parameter(Mandatory = $false)]
    [ValidatePattern('^https?://.*$')]
    [string]$GrafanaUrl = "http://localhost:3000",

    [Parameter(Mandatory = $false)]
    [string]$ApiKey = "",

    [Parameter(Mandatory = $false)]
    [string]$SearchQuery = "",

    [Parameter(Mandatory = $false)]
    [switch]$Force,

    [Parameter(Mandatory = $false)]
    [switch]$DryRun,

    [Parameter(Mandatory = $false)]
    [ValidateSet("DEBUG", "INFO", "WARN", "ERROR")]
    [string]$LogLevel = "INFO",

    [Parameter(Mandatory = $false)]
    [string]$LogFile = "",

    [Parameter(Mandatory = $false)]
    [switch]$NoBackup
)

# Enhanced logging and error handling framework
class EnhancedLogger {
    [string]$LogFile
    [string]$LogLevel
    [hashtable]$LogLevels
    [System.Collections.ArrayList]$LogBuffer

    EnhancedLogger([string]$logFile, [string]$logLevel) {
        $this.LogFile = $logFile
        $this.LogLevel = $logLevel
        $this.LogLevels = @{
            "DEBUG" = 0
            "INFO" = 1
            "WARN" = 2
            "ERROR" = 3
        }
        $this.LogBuffer = @()

        if ($this.LogFile -and -not (Test-Path (Split-Path $this.LogFile -Parent))) {
            New-Item -ItemType Directory -Path (Split-Path $this.LogFile -Parent) -Force | Out-Null
        }
    }

    [void]Log([string]$message, [string]$level, [string]$component = "") {
        if ($this.LogLevels[$level] -lt $this.LogLevels[$this.LogLevel]) {
            return
        }

        $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
        $processId = $PID
        $threadId = [System.Threading.Thread]::CurrentThread.ManagedThreadId
        $componentInfo = if ($component) { "[$component] " } else { "" }

        $logEntry = @{
            Timestamp = $timestamp
            Level = $level
            Component = $component
            Message = $message
            ProcessId = $processId
            ThreadId = $threadId
        }

        $this.LogBuffer.Add($logEntry) | Out-Null

        $formattedMessage = "$timestamp [$level] [PID:$processId|TID:$threadId] $componentInfo$message"

        # Console output with colors
        $color = switch ($level) {
            "DEBUG" { "DarkGray" }
            "INFO" { "White" }
            "WARN" { "Yellow" }
            "ERROR" { "Red" }
            default { "White" }
        }
        Write-Host $formattedMessage -ForegroundColor $color

        # File output
        if ($this.LogFile) {
            Add-Content -Path $this.LogFile -Value $formattedMessage -Encoding UTF8
        }
    }

    [void]Debug([string]$message, [string]$component = "") { $this.Log($message, "DEBUG", $component) }
    [void]Info([string]$message, [string]$component = "") { $this.Log($message, "INFO", $component) }
    [void]Warn([string]$message, [string]$component = "") { $this.Log($message, "WARN", $component) }
    [void]Error([string]$message, [string]$component = "") { $this.Log($message, "ERROR", $component) }

    [object]GetLogSummary() {
        $summary = @{
            TotalEntries = $this.LogBuffer.Count
            ByLevel = @{}
            ByComponent = @{}
            StartTime = if ($this.LogBuffer.Count -gt 0) { $this.LogBuffer[0].Timestamp } else { $null }
            EndTime = if ($this.LogBuffer.Count -gt 0) { $this.LogBuffer[-1].Timestamp } else { $null }
        }

        foreach ($entry in $this.LogBuffer) {
            # Count by level
            if (-not $summary.ByLevel.ContainsKey($entry.Level)) {
                $summary.ByLevel[$entry.Level] = 0
            }
            $summary.ByLevel[$entry.Level]++

            # Count by component
            if ($entry.Component) {
                if (-not $summary.ByComponent.ContainsKey($entry.Component)) {
                    $summary.ByComponent[$entry.Component] = 0
                }
                $summary.ByComponent[$entry.Component]++
            }
        }

        return $summary
    }
}

# Enhanced template registry with validation
class TemplateManager {
    [hashtable]$Registry
    [EnhancedLogger]$Logger
    [string]$SourcePath
    [string]$RepositoryPath

    TemplateManager([EnhancedLogger]$logger, [string]$sourcePath, [string]$repositoryPath) {
        $this.Logger = $logger
        $this.SourcePath = $sourcePath
        $this.RepositoryPath = $repositoryPath
        $this.LoadRegistry()
    }

    [void]LoadRegistry() {
        $this.Logger.Debug("Loading template registry", "TemplateManager")

        $this.Registry = @{
            "executive" = @{
                Name = "Executive Dashboard"
                Description = "High-level KPIs and business metrics for leadership with error handling"
                Version = "2.0.0"
                Author = "Orleans Monitoring Team"
                Tags = @("executive", "kpi", "business", "leadership", "resilient")
                File = "executive-dashboard-enhanced.json"
                Dependencies = @("prometheus", "grafana")
                MinGrafanaVersion = "8.0.0"
                Category = "Business"
                RequiredMetrics = @("orleans_grain_operation_errors_total", "orleans_grain_operation_duration_seconds")
                FallbackSupport = $true
            }
            "historical" = @{
                Name = "Historical Analysis Dashboard"
                Description = "Long-term trend analysis and capacity planning with resilient design"
                Version = "1.1.0"
                Author = "Orleans Monitoring Team"
                Tags = @("historical", "trends", "capacity", "analysis")
                File = "historical-analysis-dashboard.json"
                Dependencies = @("prometheus", "grafana")
                MinGrafanaVersion = "8.0.0"
                Category = "Analytics"
                RequiredMetrics = @("orleans_grain_activations_total", "orleans_grain_state_size_bytes")
                FallbackSupport = $true
            }
            "troubleshooting" = @{
                Name = "Troubleshooting Dashboard"
                Description = "Deep-dive debugging and error analysis with error state handling"
                Version = "1.1.0"
                Author = "Orleans Monitoring Team"
                Tags = @("troubleshooting", "debugging", "errors", "performance")
                File = "troubleshooting-dashboard.json"
                Dependencies = @("prometheus", "grafana")
                MinGrafanaVersion = "8.0.0"
                Category = "Operations"
                RequiredMetrics = @("orleans_grain_operation_errors_total", "orleans_grain_operation_duration_seconds")
                FallbackSupport = $true
            }
            "capacity" = @{
                Name = "Capacity Planning Dashboard"
                Description = "Resource utilization and scaling insights with fallback values"
                Version = "1.1.0"
                Author = "Orleans Monitoring Team"
                Tags = @("capacity", "planning", "resources", "scaling")
                File = "capacity-planning-dashboard.json"
                Dependencies = @("prometheus", "grafana")
                MinGrafanaVersion = "8.0.0"
                Category = "Infrastructure"
                RequiredMetrics = @("orleans_silo_memory_usage_bytes", "orleans_grain_activations_total")
                FallbackSupport = $true
            }
            "operational" = @{
                Name = "Enhanced Operational Dashboard"
                Description = "Real-time monitoring with improved error handling and connectivity status"
                Version = "2.0.0"
                Author = "Orleans Monitoring Team"
                Tags = @("operational", "realtime", "monitoring", "devops")
                File = "enhanced-operational-dashboard.json"
                Dependencies = @("prometheus", "grafana")
                MinGrafanaVersion = "8.0.0"
                Category = "Operations"
                RequiredMetrics = @("orleans_grain_activations_total", "orleans_grain_operation_duration_seconds")
                FallbackSupport = $true
            }
        }

        $this.Logger.Info("Template registry loaded with $($this.Registry.Count) templates", "TemplateManager")
    }

    [bool]ValidateTemplate([string]$templateId) {
        $this.Logger.Debug("Validating template: $templateId", "TemplateManager")

        if (-not $this.Registry.ContainsKey($templateId)) {
            $this.Logger.Error("Template '$templateId' not found in registry", "TemplateManager")
            return $false
        }

        $template = $this.Registry[$templateId]
        $templateFile = Join-Path $this.SourcePath $template.File

        if (-not (Test-Path $templateFile)) {
            $this.Logger.Error("Template file not found: $templateFile", "TemplateManager")
            return $false
        }

        try {
            $content = Get-Content $templateFile -Raw | ConvertFrom-Json
            if (-not $content.dashboard) {
                $this.Logger.Error("Invalid dashboard format in $templateFile", "TemplateManager")
                return $false
            }

            $this.Logger.Debug("Template validation passed for $templateId", "TemplateManager")
            return $true
        }
        catch {
            $this.Logger.Error("Failed to parse template JSON: $($_.Exception.Message)", "TemplateManager")
            return $false
        }
    }

    [object]GetTemplate([string]$templateId) {
        if ($this.Registry.ContainsKey($templateId)) {
            return $this.Registry[$templateId]
        }
        return $null
    }

    [object[]]ListTemplates([string]$filter = "") {
        $templates = @()
        foreach ($templateId in $this.Registry.Keys) {
            $template = $this.Registry[$templateId]
            if (-not $filter -or $template.Name -like "*$filter*" -or $template.Category -like "*$filter*") {
                $templates += [PSCustomObject]@{
                    Id = $templateId
                    Name = $template.Name
                    Version = $template.Version
                    Category = $template.Category
                    Description = $template.Description
                    FallbackSupport = $template.FallbackSupport
                }
            }
        }
        return $templates
    }
}

# Enhanced operation result tracking
class OperationResult {
    [bool]$Success
    [string]$Message
    [object]$Data
    [System.Collections.ArrayList]$Errors
    [System.Collections.ArrayList]$Warnings
    [hashtable]$Metadata

    OperationResult() {
        $this.Success = $true
        $this.Message = ""
        $this.Data = $null
        $this.Errors = @()
        $this.Warnings = @()
        $this.Metadata = @{}
    }

    [void]AddError([string]$error) {
        $this.Errors.Add($error) | Out-Null
        $this.Success = $false
    }

    [void]AddWarning([string]$warning) {
        $this.Warnings.Add($warning) | Out-Null
    }

    [void]SetMetadata([string]$key, [object]$value) {
        $this.Metadata[$key] = $value
    }

    [object]ToSummary() {
        return @{
            Success = $this.Success
            Message = $this.Message
            ErrorCount = $this.Errors.Count
            WarningCount = $this.Warnings.Count
            HasData = $this.Data -ne $null
            Metadata = $this.Metadata
        }
    }
}

# Initialize enhanced components
if (-not $LogFile) {
    $LogFile = Join-Path $PWD "dashboard-manager-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
}

$script:Logger = [EnhancedLogger]::new($LogFile, $LogLevel)
$script:TemplateManager = [TemplateManager]::new($script:Logger, $SourcePath, $RepositoryPath)

# Enhanced operation functions with proper error handling
function Invoke-HealthCheck {
    [CmdletBinding()]
    param()

    $result = [OperationResult]::new()
    $script:Logger.Info("Starting health check", "HealthCheck")

    try {
        # Check source path
        if (-not (Test-Path $SourcePath)) {
            $result.AddError("Source path does not exist: $SourcePath")
        } else {
            $script:Logger.Debug("Source path exists: $SourcePath", "HealthCheck")
        }

        # Check repository path
        if (-not (Test-Path $RepositoryPath)) {
            $result.AddWarning("Repository path does not exist (will be created): $RepositoryPath")
        } else {
            $script:Logger.Debug("Repository path exists: $RepositoryPath", "HealthCheck")
        }

        # Validate all templates
        $templateCount = 0
        $validTemplates = 0
        foreach ($templateId in $script:TemplateManager.Registry.Keys) {
            $templateCount++
            if ($script:TemplateManager.ValidateTemplate($templateId)) {
                $validTemplates++
            } else {
                $result.AddError("Template validation failed: $templateId")
            }
        }

        $result.SetMetadata("TotalTemplates", $templateCount)
        $result.SetMetadata("ValidTemplates", $validTemplates)

        # Test Grafana connectivity if API key provided
        if ($ApiKey) {
            try {
                $headers = @{ "Authorization" = "Bearer $ApiKey" }
                $response = Invoke-WebRequest -Uri "$GrafanaUrl/api/health" -Headers $headers -Method Get -TimeoutSec 10 -UseBasicParsing
                if ($response.StatusCode -eq 200) {
                    $script:Logger.Info("Grafana connectivity verified", "HealthCheck")
                    $result.SetMetadata("GrafanaConnectivity", $true)
                } else {
                    $result.AddWarning("Grafana returned status code: $($response.StatusCode)")
                    $result.SetMetadata("GrafanaConnectivity", $false)
                }
            }
            catch {
                $result.AddWarning("Cannot connect to Grafana: $($_.Exception.Message)")
                $result.SetMetadata("GrafanaConnectivity", $false)
            }
        } else {
            $result.AddWarning("No API key provided, skipping Grafana connectivity test")
            $result.SetMetadata("GrafanaConnectivity", "SKIPPED")
        }

        if ($result.Success) {
            $result.Message = "Health check passed - all systems operational"
            $script:Logger.Info("Health check completed successfully", "HealthCheck")
        } else {
            $result.Message = "Health check found issues that need attention"
            $script:Logger.Warn("Health check completed with errors", "HealthCheck")
        }

    }
    catch {
        $result.AddError("Health check failed with exception: $($_.Exception.Message)")
        $script:Logger.Error("Health check failed: $($_.Exception.Message)", "HealthCheck")
    }

    return $result
}

function Invoke-ListTemplates {
    [CmdletBinding()]
    param([string]$Filter = "")

    $result = [OperationResult]::new()
    $script:Logger.Info("Listing templates with filter: '$Filter'", "ListTemplates")

    try {
        $templates = $script:TemplateManager.ListTemplates($Filter)
        $result.Data = $templates
        $result.Message = "Found $($templates.Count) templates"
        $result.SetMetadata("TemplateCount", $templates.Count)

        # Display templates
        Write-Host "`n📋 Orleans Dashboard Templates" -ForegroundColor Cyan
        Write-Host "================================" -ForegroundColor Cyan

        if ($templates.Count -eq 0) {
            Write-Host "No templates found matching filter: '$Filter'" -ForegroundColor Yellow
        } else {
            foreach ($template in $templates) {
                $statusIcon = if ($template.FallbackSupport) { "🛡️" } else { "⚠️" }
                Write-Host "`n$statusIcon " -NoNewline
                Write-Host "$($template.Name)" -ForegroundColor White -NoNewline
                Write-Host " (v$($template.Version))" -ForegroundColor Gray
                Write-Host "   ID: $($template.Id)" -ForegroundColor DarkGray
                Write-Host "   Category: $($template.Category)" -ForegroundColor DarkGray
                Write-Host "   $($template.Description)" -ForegroundColor Gray
            }
        }

        $script:Logger.Info("Template listing completed successfully", "ListTemplates")
    }
    catch {
        $result.AddError("Failed to list templates: $($_.Exception.Message)")
        $script:Logger.Error("Template listing failed: $($_.Exception.Message)", "ListTemplates")
    }

    return $result
}

function Invoke-ValidateTemplate {
    [CmdletBinding()]
    param([string]$TemplateId)

    $result = [OperationResult]::new()
    $script:Logger.Info("Validating template: $TemplateId", "ValidateTemplate")

    try {
        if (-not $TemplateId) {
            # Validate all templates
            $validationResults = @()
            foreach ($templateId in $script:TemplateManager.Registry.Keys) {
                $isValid = $script:TemplateManager.ValidateTemplate($templateId)
                $validationResults += @{
                    TemplateId = $templateId
                    Valid = $isValid
                }
                if (-not $isValid) {
                    $result.AddError("Template validation failed: $templateId")
                }
            }
            $result.Data = $validationResults
            $result.SetMetadata("TotalValidated", $validationResults.Count)
            $result.SetMetadata("ValidCount", ($validationResults | Where-Object { $_.Valid }).Count)
        } else {
            # Validate specific template
            $isValid = $script:TemplateManager.ValidateTemplate($TemplateId)
            $result.Data = @{ TemplateId = $TemplateId; Valid = $isValid }

            if (-not $isValid) {
                $result.AddError("Template validation failed: $TemplateId")
            }
        }

        if ($result.Success) {
            $result.Message = "Template validation completed successfully"
            $script:Logger.Info("Template validation completed", "ValidateTemplate")
        } else {
            $result.Message = "Template validation found issues"
            $script:Logger.Warn("Template validation completed with errors", "ValidateTemplate")
        }
    }
    catch {
        $result.AddError("Template validation failed with exception: $($_.Exception.Message)")
        $script:Logger.Error("Template validation failed: $($_.Exception.Message)", "ValidateTemplate")
    }

    return $result
}

# Main execution with comprehensive error handling
function Invoke-MainOperation {
    $script:Logger.Info("Starting dashboard template manager", "Main")
    $script:Logger.Info("Action: $Action, Template: $TemplateName, Version: $TemplateVersion", "Main")

    if ($DryRun) {
        $script:Logger.Info("DRY RUN MODE - No changes will be made", "Main")
    }

    try {
        $result = switch ($Action) {
            "health" { Invoke-HealthCheck }
            "list" { Invoke-ListTemplates -Filter $TemplateName }
            "validate" { Invoke-ValidateTemplate -TemplateId $TemplateName }
            default {
                $error = [OperationResult]::new()
                $error.AddError("Action '$Action' is not yet implemented in enhanced version")
                $error
            }
        }

        # Display results
        Write-Host "`n📊 Operation Summary" -ForegroundColor Cyan
        Write-Host "===================" -ForegroundColor Cyan
        Write-Host "Status: " -NoNewline
        if ($result.Success) {
            Write-Host "SUCCESS" -ForegroundColor Green
        } else {
            Write-Host "FAILED" -ForegroundColor Red
        }
        Write-Host "Message: $($result.Message)" -ForegroundColor White

        if ($result.Errors.Count -gt 0) {
            Write-Host "`nErrors ($($result.Errors.Count)):" -ForegroundColor Red
            foreach ($error in $result.Errors) {
                Write-Host "  ❌ $error" -ForegroundColor Red
            }
        }

        if ($result.Warnings.Count -gt 0) {
            Write-Host "`nWarnings ($($result.Warnings.Count)):" -ForegroundColor Yellow
            foreach ($warning in $result.Warnings) {
                Write-Host "  ⚠️ $warning" -ForegroundColor Yellow
            }
        }

        if ($result.Metadata.Count -gt 0) {
            Write-Host "`nMetadata:" -ForegroundColor Blue
            foreach ($key in $result.Metadata.Keys) {
                Write-Host "  $key`: $($result.Metadata[$key])" -ForegroundColor Blue
            }
        }

        # Log summary
        $logSummary = $script:Logger.GetLogSummary()
        Write-Host "`n📋 Log Summary" -ForegroundColor Cyan
        Write-Host "==============" -ForegroundColor Cyan
        Write-Host "Total Log Entries: $($logSummary.TotalEntries)" -ForegroundColor White
        Write-Host "Log File: $LogFile" -ForegroundColor White

        if ($logSummary.ByLevel.Count -gt 0) {
            Write-Host "By Level:" -ForegroundColor White
            foreach ($level in $logSummary.ByLevel.Keys) {
                Write-Host "  $level`: $($logSummary.ByLevel[$level])" -ForegroundColor White
            }
        }

        $script:Logger.Info("Operation completed", "Main")
        return if ($result.Success) { 0 } else { 1 }
    }
    catch {
        $script:Logger.Error("Operation failed with unhandled exception: $($_.Exception.Message)", "Main")
        $script:Logger.Error("Stack trace: $($_.ScriptStackTrace)", "Main")
        Write-Host "`n❌ FATAL ERROR: $($_.Exception.Message)" -ForegroundColor Red
        return 1
    }
}

# Entry point with error handling
try {
    $exitCode = Invoke-MainOperation
    exit $exitCode
}
catch {
    Write-Host "CRITICAL FAILURE: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    exit 1
}