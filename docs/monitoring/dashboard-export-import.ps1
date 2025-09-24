# Orleans Dashboard Export/Import Management Script
# This script provides comprehensive dashboard configuration management for Orleans monitoring

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("export", "import", "list", "validate", "backup")]
    [string]$Action,

    [Parameter(Mandatory = $false)]
    [string]$GrafanaUrl = "http://localhost:3000",

    [Parameter(Mandatory = $false)]
    [string]$ApiKey = "",

    [Parameter(Mandatory = $false)]
    [string]$DashboardPath = ".",

    [Parameter(Mandatory = $false)]
    [string]$DashboardId = "",

    [Parameter(Mandatory = $false)]
    [string]$BackupPath = "./backups",

    [Parameter(Mandatory = $false)]
    [switch]$AllDashboards,

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

# Configuration
$OrleansKeywords = @("orleans", "grain", "metrics")
$DashboardTemplates = @{
    "executive"         = "executive-dashboard.json"
    "historical"        = "historical-analysis-dashboard.json"
    "troubleshooting"   = "troubleshooting-dashboard.json"
    "capacity"          = "capacity-planning-dashboard.json"
    "operational"       = "orleans-metrics-dashboard.json"
    "realtime"          = "enhanced-operational-dashboard.json"
}

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "ERROR" { "Red" }
        "WARN" { "Yellow" }
        "SUCCESS" { "Green" }
        default { "White" }
    }
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

function Test-GrafanaConnection {
    try {
        $headers = @{
            "Authorization" = "Bearer $ApiKey"
            "Content-Type"  = "application/json"
        }

        $response = Invoke-RestMethod -Uri "$GrafanaUrl/api/health" -Headers $headers -Method Get
        Write-Log "Grafana connection successful" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "Failed to connect to Grafana: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Export-OrleanseDashboard {
    param([string]$Id, [string]$OutputPath)

    try {
        $headers = @{
            "Authorization" = "Bearer $ApiKey"
            "Content-Type"  = "application/json"
        }

        $response = Invoke-RestMethod -Uri "$GrafanaUrl/api/dashboards/uid/$Id" -Headers $headers -Method Get

        # Clean up dashboard for export
        $cleanDashboard = $response.dashboard
        $cleanDashboard.id = $null
        $cleanDashboard.uid = $null
        $cleanDashboard.version = 1

        $exportData = @{
            dashboard = $cleanDashboard
            meta      = @{
                type        = "db"
                canSave     = $true
                canEdit     = $true
                canAdmin    = $true
                canStar     = $true
                slug        = $response.meta.slug
                expires     = "0001-01-01T00:00:00Z"
                created     = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
                updated     = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
                updatedBy   = "export-script"
                createdBy   = "export-script"
                version     = 1
                hasAcl      = $false
                isFolder    = $false
                folderId    = 0
                folderTitle = "General"
                folderUrl   = ""
                provisioned = $false
                provisionedExternalId = ""
            }
        }

        $json = $exportData | ConvertTo-Json -Depth 20
        Set-Content -Path $OutputPath -Value $json -Encoding UTF8

        Write-Log "Dashboard exported successfully to $OutputPath" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "Failed to export dashboard $Id`: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Import-OrleansDashboard {
    param([string]$FilePath)

    try {
        if (-not (Test-Path $FilePath)) {
            Write-Log "Dashboard file not found: $FilePath" "ERROR"
            return $false
        }

        $content = Get-Content -Path $FilePath -Raw | ConvertFrom-Json

        $headers = @{
            "Authorization" = "Bearer $ApiKey"
            "Content-Type"  = "application/json"
        }

        $importData = @{
            dashboard = $content.dashboard
            overwrite = $Force.IsPresent
        }

        $body = $importData | ConvertTo-Json -Depth 20
        $response = Invoke-RestMethod -Uri "$GrafanaUrl/api/dashboards/db" -Headers $headers -Method Post -Body $body

        Write-Log "Dashboard imported successfully. UID: $($response.uid)" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "Failed to import dashboard from $FilePath`: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Get-OrleanseDashboards {
    try {
        $headers = @{
            "Authorization" = "Bearer $ApiKey"
            "Content-Type"  = "application/json"
        }

        $response = Invoke-RestMethod -Uri "$GrafanaUrl/api/search?type=dash-db" -Headers $headers -Method Get

        $orleansDashboards = $response | Where-Object {
            $title = $_.title.ToLower()
            $OrleansKeywords | ForEach-Object { $title.Contains($_) } | Where-Object { $_ -eq $true }
        }

        return $orleansDashboards
    }
    catch {
        Write-Log "Failed to retrieve dashboards: $($_.Exception.Message)" "ERROR"
        return @()
    }
}

function Backup-OrleansDashboards {
    param([string]$BackupDir)

    if (-not (Test-Path $BackupDir)) {
        New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
        Write-Log "Created backup directory: $BackupDir" "INFO"
    }

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupPath = Join-Path $BackupDir "orleans-dashboards-$timestamp"
    New-Item -ItemType Directory -Path $backupPath -Force | Out-Null

    $dashboards = Get-OrleanseDashboards
    $successCount = 0

    foreach ($dashboard in $dashboards) {
        $filename = "$($dashboard.title -replace '[^\w\-]', '_').json"
        $outputPath = Join-Path $backupPath $filename

        if (Export-OrleanseDashboard -Id $dashboard.uid -OutputPath $outputPath) {
            $successCount++
        }
    }

    Write-Log "Backup completed: $successCount/$($dashboards.Count) dashboards backed up to $backupPath" "SUCCESS"
    return $backupPath
}

function Validate-DashboardTemplates {
    param([string]$TemplatePath)

    $validationErrors = @()
    $templateCount = 0

    foreach ($template in $DashboardTemplates.Values) {
        $fullPath = Join-Path $TemplatePath $template

        if (-not (Test-Path $fullPath)) {
            $validationErrors += "Template file missing: $template"
            continue
        }

        try {
            $content = Get-Content -Path $fullPath -Raw | ConvertFrom-Json

            # Validate required structure
            if (-not $content.dashboard) {
                $validationErrors += "$template`: Missing dashboard object"
            }
            elseif (-not $content.dashboard.title) {
                $validationErrors += "$template`: Missing dashboard title"
            }
            elseif (-not $content.dashboard.panels) {
                $validationErrors += "$template`: Missing dashboard panels"
            }
            else {
                $templateCount++
                Write-Log "✓ $template is valid" "SUCCESS"
            }
        }
        catch {
            $validationErrors += "$template`: Invalid JSON format - $($_.Exception.Message)"
        }
    }

    if ($validationErrors.Count -eq 0) {
        Write-Log "All $templateCount dashboard templates are valid" "SUCCESS"
        return $true
    }
    else {
        Write-Log "Validation failed with $($validationErrors.Count) errors:" "ERROR"
        foreach ($error in $validationErrors) {
            Write-Log "  - $error" "ERROR"
        }
        return $false
    }
}

# Main execution logic
function Main {
    Write-Log "Orleans Dashboard Management Script started" "INFO"
    Write-Log "Action: $Action" "INFO"

    switch ($Action) {
        "export" {
            if (-not (Test-GrafanaConnection)) {
                exit 1
            }

            if ($AllDashboards) {
                $backupPath = Backup-OrleansDashboards -BackupDir $BackupPath
                Write-Log "All Orleans dashboards exported to $backupPath" "SUCCESS"
            }
            elseif ($DashboardId) {
                $outputFile = Join-Path $DashboardPath "dashboard-$DashboardId.json"
                if (Export-OrleanseDashboard -Id $DashboardId -OutputPath $outputFile) {
                    Write-Log "Dashboard $DashboardId exported to $outputFile" "SUCCESS"
                }
                else {
                    exit 1
                }
            }
            else {
                Write-Log "Please specify -DashboardId or use -AllDashboards" "ERROR"
                exit 1
            }
        }

        "import" {
            if (-not (Test-GrafanaConnection)) {
                exit 1
            }

            if ($AllDashboards) {
                $imported = 0
                foreach ($template in $DashboardTemplates.Values) {
                    $templatePath = Join-Path $DashboardPath $template
                    if (Import-OrleansDashboard -FilePath $templatePath) {
                        $imported++
                    }
                }
                Write-Log "Import completed: $imported/$($DashboardTemplates.Count) templates imported" "SUCCESS"
            }
            elseif ($DashboardId) {
                $inputFile = Join-Path $DashboardPath "dashboard-$DashboardId.json"
                if (Import-OrleansDashboard -FilePath $inputFile) {
                    Write-Log "Dashboard imported from $inputFile" "SUCCESS"
                }
                else {
                    exit 1
                }
            }
            else {
                Write-Log "Please specify -DashboardId or use -AllDashboards" "ERROR"
                exit 1
            }
        }

        "list" {
            if (-not (Test-GrafanaConnection)) {
                exit 1
            }

            $dashboards = Get-OrleanseDashboards
            Write-Log "Found $($dashboards.Count) Orleans dashboards:" "INFO"

            foreach ($dashboard in $dashboards) {
                Write-Host "  UID: $($dashboard.uid)" -ForegroundColor Cyan
                Write-Host "  Title: $($dashboard.title)" -ForegroundColor White
                Write-Host "  URL: $($dashboard.url)" -ForegroundColor Gray
                Write-Host ""
            }
        }

        "validate" {
            $isValid = Validate-DashboardTemplates -TemplatePath $DashboardPath
            if (-not $isValid) {
                exit 1
            }
        }

        "backup" {
            if (-not (Test-GrafanaConnection)) {
                exit 1
            }

            $backupPath = Backup-OrleansDashboards -BackupDir $BackupPath
            Write-Log "Backup operation completed: $backupPath" "SUCCESS"
        }

        default {
            Write-Log "Invalid action: $Action" "ERROR"
            exit 1
        }
    }

    Write-Log "Orleans Dashboard Management Script completed successfully" "SUCCESS"
}

# Execute main function
Main