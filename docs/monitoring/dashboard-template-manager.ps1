# Orleans Dashboard Template Management System
# This script provides comprehensive template sharing, versioning, and management

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("list", "install", "update", "remove", "validate", "package", "publish", "search")]
    [string]$Action,

    [Parameter(Mandatory = $false)]
    [string]$TemplateName = "",

    [Parameter(Mandatory = $false)]
    [string]$TemplateVersion = "latest",

    [Parameter(Mandatory = $false)]
    [string]$RepositoryPath = "./templates",

    [Parameter(Mandatory = $false)]
    [string]$SourcePath = ".",

    [Parameter(Mandatory = $false)]
    [string]$GrafanaUrl = "http://localhost:3000",

    [Parameter(Mandatory = $false)]
    [string]$ApiKey = "",

    [Parameter(Mandatory = $false)]
    [string]$SearchQuery = "",

    [Parameter(Mandatory = $false)]
    [switch]$Force,

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

# Template repository structure and metadata
$TemplateRegistry = @{
    "executive" = @{
        Name = "Executive Dashboard"
        Description = "High-level KPIs and business metrics for leadership"
        Version = "1.0.0"
        Author = "Orleans Monitoring Team"
        Tags = @("executive", "kpi", "business", "leadership")
        File = "executive-dashboard.json"
        Dependencies = @("prometheus", "grafana")
        MinGrafanaVersion = "8.0.0"
        Category = "Business"
    }
    "historical" = @{
        Name = "Historical Analysis Dashboard"
        Description = "Long-term trends and capacity planning insights"
        Version = "1.0.0"
        Author = "Orleans Monitoring Team"
        Tags = @("historical", "trends", "analysis", "capacity")
        File = "historical-analysis-dashboard.json"
        Dependencies = @("prometheus", "grafana")
        MinGrafanaVersion = "8.0.0"
        Category = "Analytics"
    }
    "troubleshooting" = @{
        Name = "Troubleshooting Dashboard"
        Description = "Deep-dive debugging and error analysis"
        Version = "1.0.0"
        Author = "Orleans Monitoring Team"
        Tags = @("troubleshooting", "debugging", "errors", "performance")
        File = "troubleshooting-dashboard.json"
        Dependencies = @("prometheus", "grafana")
        MinGrafanaVersion = "8.0.0"
        Category = "Debugging"
    }
    "capacity" = @{
        Name = "Capacity Planning Dashboard"
        Description = "Resource utilization and scaling insights"
        Version = "1.0.0"
        Author = "Orleans Monitoring Team"
        Tags = @("capacity", "planning", "scaling", "resources")
        File = "capacity-planning-dashboard.json"
        Dependencies = @("prometheus", "grafana")
        MinGrafanaVersion = "8.0.0"
        Category = "Infrastructure"
    }
    "operational" = @{
        Name = "Operational Dashboard"
        Description = "Real-time operational monitoring for DevOps teams"
        Version = "1.0.0"
        Author = "Orleans Monitoring Team"
        Tags = @("operational", "realtime", "devops", "monitoring")
        File = "orleans-metrics-dashboard.json"
        Dependencies = @("prometheus", "grafana")
        MinGrafanaVersion = "8.0.0"
        Category = "Operations"
    }
    "realtime" = @{
        Name = "Enhanced Real-time Dashboard"
        Description = "Advanced real-time monitoring with live updates"
        Version = "1.0.0"
        Author = "Orleans Monitoring Team"
        Tags = @("realtime", "enhanced", "live", "monitoring")
        File = "enhanced-operational-dashboard.json"
        Dependencies = @("prometheus", "grafana")
        MinGrafanaVersion = "8.0.0"
        Category = "Operations"
    }
}

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "ERROR" { "Red" }
        "WARN" { "Yellow" }
        "SUCCESS" { "Green" }
        "INFO" { "Cyan" }
        default { "White" }
    }
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

function Get-TemplateMetadata {
    param([string]$TemplateFile)

    try {
        if (-not (Test-Path $TemplateFile)) {
            return $null
        }

        $content = Get-Content -Path $TemplateFile -Raw | ConvertFrom-Json

        return @{
            Title = $content.dashboard.title
            Description = $content.meta.slug
            Tags = $content.dashboard.tags
            Panels = $content.dashboard.panels.Count
            FileSize = (Get-Item $TemplateFile).Length
            LastModified = (Get-Item $TemplateFile).LastWriteTime
        }
    }
    catch {
        Write-Log "Failed to read template metadata from $TemplateFile`: $($_.Exception.Message)" "ERROR"
        return $null
    }
}

function Validate-Template {
    param([string]$TemplateFile, [string]$TemplateName)

    $validationErrors = @()
    $warnings = @()

    try {
        if (-not (Test-Path $TemplateFile)) {
            return @{ Valid = $false; Errors = @("Template file not found: $TemplateFile"); Warnings = @() }
        }

        $content = Get-Content -Path $TemplateFile -Raw | ConvertFrom-Json

        # Validate required structure
        if (-not $content.dashboard) {
            $validationErrors += "Missing 'dashboard' object"
        }
        else {
            # Validate dashboard properties
            if (-not $content.dashboard.title) {
                $validationErrors += "Missing dashboard title"
            }

            if (-not $content.dashboard.panels) {
                $validationErrors += "Missing dashboard panels"
            }
            elseif ($content.dashboard.panels.Count -eq 0) {
                $warnings += "Dashboard has no panels"
            }

            # Validate panel structure
            for ($i = 0; $i -lt $content.dashboard.panels.Count; $i++) {
                $panel = $content.dashboard.panels[$i]
                if (-not $panel.title) {
                    $warnings += "Panel $i is missing a title"
                }
                if (-not $panel.type) {
                    $validationErrors += "Panel $i is missing a type"
                }
                if (-not $panel.targets) {
                    $warnings += "Panel '$($panel.title)' has no data targets"
                }
            }

            # Validate tags
            if ($content.dashboard.tags -and "orleans" -notin $content.dashboard.tags) {
                $warnings += "Dashboard should include 'orleans' tag for better categorization"
            }
        }

        # Validate metadata
        if (-not $content.meta) {
            $warnings += "Missing metadata object"
        }

        # Check for Orleans-specific metrics
        $orleansMetrics = @("orleans_grain", "grain_type", "operation_type")
        $hasOrleansMetrics = $false

        foreach ($panel in $content.dashboard.panels) {
            if ($panel.targets) {
                foreach ($target in $panel.targets) {
                    if ($target.expr) {
                        foreach ($metric in $orleansMetrics) {
                            if ($target.expr -match $metric) {
                                $hasOrleansMetrics = $true
                                break
                            }
                        }
                    }
                }
            }
        }

        if (-not $hasOrleansMetrics) {
            $warnings += "Template does not appear to use Orleans-specific metrics"
        }

        $isValid = $validationErrors.Count -eq 0

        Write-Log "Template validation for '$TemplateName': $(if($isValid){'PASSED'}else{'FAILED'})" $(if($isValid){'SUCCESS'}else{'ERROR'})

        return @{
            Valid = $isValid
            Errors = $validationErrors
            Warnings = $warnings
            Metadata = Get-TemplateMetadata -TemplateFile $TemplateFile
        }
    }
    catch {
        return @{
            Valid = $false
            Errors = @("Template validation failed: $($_.Exception.Message)")
            Warnings = @()
        }
    }
}

function List-Templates {
    Write-Log "Available Orleans Dashboard Templates:" "INFO"
    Write-Host ""

    foreach ($templateId in $TemplateRegistry.Keys | Sort-Object) {
        $template = $TemplateRegistry[$templateId]
        $templateFile = Join-Path $SourcePath $template.File

        Write-Host "📊 " -NoNewline -ForegroundColor Blue
        Write-Host "$($template.Name)" -ForegroundColor White -NoNewline
        Write-Host " (v$($template.Version))" -ForegroundColor Gray

        Write-Host "   Description: " -NoNewline -ForegroundColor Gray
        Write-Host "$($template.Description)" -ForegroundColor White

        Write-Host "   Category: " -NoNewline -ForegroundColor Gray
        Write-Host "$($template.Category)" -ForegroundColor Cyan

        Write-Host "   Tags: " -NoNewline -ForegroundColor Gray
        Write-Host ($template.Tags -join ", ") -ForegroundColor Yellow

        Write-Host "   File: " -NoNewline -ForegroundColor Gray
        Write-Host "$($template.File)" -ForegroundColor White

        # Check if file exists and get metadata
        if (Test-Path $templateFile) {
            $metadata = Get-TemplateMetadata -TemplateFile $templateFile
            if ($metadata) {
                Write-Host "   Status: " -NoNewline -ForegroundColor Gray
                Write-Host "✅ Available" -ForegroundColor Green
                Write-Host "   Panels: " -NoNewline -ForegroundColor Gray
                Write-Host "$($metadata.Panels)" -ForegroundColor White
                Write-Host "   Size: " -NoNewline -ForegroundColor Gray
                Write-Host "$([math]::Round($metadata.FileSize / 1KB, 1)) KB" -ForegroundColor White
            }
        }
        else {
            Write-Host "   Status: " -NoNewline -ForegroundColor Gray
            Write-Host "❌ Missing" -ForegroundColor Red
        }

        Write-Host ""
    }
}

function Install-Template {
    param([string]$TemplateId)

    if (-not $TemplateRegistry.ContainsKey($TemplateId)) {
        Write-Log "Template '$TemplateId' not found in registry" "ERROR"
        return $false
    }

    $template = $TemplateRegistry[$TemplateId]
    $templateFile = Join-Path $SourcePath $template.File

    Write-Log "Installing template: $($template.Name)" "INFO"

    # Validate template
    $validation = Validate-Template -TemplateFile $templateFile -TemplateName $template.Name
    if (-not $validation.Valid) {
        Write-Log "Template validation failed:" "ERROR"
        foreach ($error in $validation.Errors) {
            Write-Log "  - $error" "ERROR"
        }
        return $false
    }

    if ($validation.Warnings.Count -gt 0) {
        Write-Log "Template has warnings:" "WARN"
        foreach ($warning in $validation.Warnings) {
            Write-Log "  - $warning" "WARN"
        }
    }

    if ($DryRun) {
        Write-Log "DRY RUN: Would install template '$($template.Name)'" "INFO"
        return $true
    }

    # Install template to Grafana (if API key provided)
    if ($ApiKey -and $GrafanaUrl) {
        try {
            $headers = @{
                "Authorization" = "Bearer $ApiKey"
                "Content-Type" = "application/json"
            }

            $content = Get-Content -Path $templateFile -Raw | ConvertFrom-Json
            $importData = @{
                dashboard = $content.dashboard
                overwrite = $Force.IsPresent
            }

            $body = $importData | ConvertTo-Json -Depth 20
            $response = Invoke-RestMethod -Uri "$GrafanaUrl/api/dashboards/db" -Headers $headers -Method Post -Body $body

            Write-Log "Template installed successfully to Grafana. UID: $($response.uid)" "SUCCESS"
        }
        catch {
            Write-Log "Failed to install template to Grafana: $($_.Exception.Message)" "ERROR"
            return $false
        }
    }

    # Copy to template repository
    $repoPath = Join-Path $RepositoryPath $TemplateId
    if (-not (Test-Path $repoPath)) {
        New-Item -ItemType Directory -Path $repoPath -Force | Out-Null
    }

    $targetFile = Join-Path $repoPath "$TemplateId-v$($template.Version).json"
    Copy-Item -Path $templateFile -Destination $targetFile -Force

    # Create metadata file
    $metadataFile = Join-Path $repoPath "metadata.json"
    $template | ConvertTo-Json -Depth 5 | Set-Content -Path $metadataFile -Encoding UTF8

    Write-Log "Template '$($template.Name)' installed successfully" "SUCCESS"
    return $true
}

function Search-Templates {
    param([string]$Query)

    Write-Log "Searching templates for: '$Query'" "INFO"
    $results = @()

    foreach ($templateId in $TemplateRegistry.Keys) {
        $template = $TemplateRegistry[$templateId]
        $searchText = "$($template.Name) $($template.Description) $($template.Tags -join ' ') $($template.Category)".ToLower()

        if ($searchText -match $Query.ToLower()) {
            $results += @{
                Id = $templateId
                Template = $template
                Relevance = ($searchText -split $Query.ToLower()).Count - 1
            }
        }
    }

    if ($results.Count -eq 0) {
        Write-Log "No templates found matching '$Query'" "WARN"
        return
    }

    # Sort by relevance
    $sortedResults = $results | Sort-Object -Property Relevance -Descending

    Write-Host "Search Results ($($results.Count) found):" -ForegroundColor Cyan
    Write-Host ""

    foreach ($result in $sortedResults) {
        $template = $result.Template
        Write-Host "🔍 " -NoNewline -ForegroundColor Yellow
        Write-Host "$($template.Name)" -ForegroundColor White
        Write-Host "   $($template.Description)" -ForegroundColor Gray
        Write-Host "   Category: $($template.Category) | Tags: $($template.Tags -join ', ')" -ForegroundColor DarkGray
        Write-Host ""
    }
}

function Package-Template {
    param([string]$TemplateId)

    if (-not $TemplateRegistry.ContainsKey($TemplateId)) {
        Write-Log "Template '$TemplateId' not found in registry" "ERROR"
        return $false
    }

    $template = $TemplateRegistry[$TemplateId]
    $templateFile = Join-Path $SourcePath $template.File

    if (-not (Test-Path $templateFile)) {
        Write-Log "Template file not found: $templateFile" "ERROR"
        return $false
    }

    # Create package directory
    $packageName = "$TemplateId-v$($template.Version)"
    $packagePath = Join-Path $RepositoryPath "packages" $packageName
    if (Test-Path $packagePath) {
        Remove-Item -Path $packagePath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $packagePath -Force | Out-Null

    # Copy template file
    Copy-Item -Path $templateFile -Destination (Join-Path $packagePath "dashboard.json") -Force

    # Create package metadata
    $packageInfo = @{
        Name = $template.Name
        Id = $TemplateId
        Version = $template.Version
        Description = $template.Description
        Author = $template.Author
        Tags = $template.Tags
        Category = $template.Category
        Dependencies = $template.Dependencies
        MinGrafanaVersion = $template.MinGrafanaVersion
        PackageDate = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
        Files = @("dashboard.json", "README.md")
    }

    $packageInfo | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $packagePath "package.json") -Encoding UTF8

    # Create README
    $readme = @"
# $($template.Name)

$($template.Description)

## Installation

1. Import the dashboard.json file into Grafana
2. Ensure Prometheus is configured with Orleans metrics
3. Configure data source to point to your Prometheus instance

## Requirements

- Grafana $($template.MinGrafanaVersion) or later
- Prometheus with Orleans metrics endpoint

## Dependencies

$($template.Dependencies -join ', ')

## Tags

$($template.Tags -join ', ')

## Version

$($template.Version)

## Author

$($template.Author)
"@

    Set-Content -Path (Join-Path $packagePath "README.md") -Value $readme -Encoding UTF8

    # Create package archive
    $packageArchive = "$packagePath.zip"
    Compress-Archive -Path "$packagePath\*" -DestinationPath $packageArchive -Force

    Write-Log "Template package created: $packageArchive" "SUCCESS"
    return $true
}

function Validate-AllTemplates {
    Write-Log "Validating all templates..." "INFO"

    $validCount = 0
    $totalCount = 0

    foreach ($templateId in $TemplateRegistry.Keys) {
        $template = $TemplateRegistry[$templateId]
        $templateFile = Join-Path $SourcePath $template.File
        $totalCount++

        Write-Host ""
        Write-Host "Validating: $($template.Name)" -ForegroundColor Cyan

        $validation = Validate-Template -TemplateFile $templateFile -TemplateName $template.Name

        if ($validation.Valid) {
            $validCount++
            Write-Host "  ✅ Valid" -ForegroundColor Green
        }
        else {
            Write-Host "  ❌ Invalid" -ForegroundColor Red
            foreach ($error in $validation.Errors) {
                Write-Host "    - $error" -ForegroundColor Red
            }
        }

        if ($validation.Warnings.Count -gt 0) {
            foreach ($warning in $validation.Warnings) {
                Write-Host "    ⚠️ $warning" -ForegroundColor Yellow
            }
        }
    }

    Write-Host ""
    if ($validCount -eq $totalCount) {
        Write-Log "All $totalCount templates are valid! ✅" "SUCCESS"
    }
    else {
        Write-Log "$validCount/$totalCount templates are valid" "WARN"
    }

    return $validCount -eq $totalCount
}

function Main {
    Write-Log "Orleans Dashboard Template Manager" "INFO"
    Write-Log "Action: $Action" "INFO"

    # Create repository directory if it doesn't exist
    if (-not (Test-Path $RepositoryPath)) {
        New-Item -ItemType Directory -Path $RepositoryPath -Force | Out-Null
        Write-Log "Created repository directory: $RepositoryPath" "INFO"
    }

    switch ($Action) {
        "list" {
            List-Templates
        }

        "install" {
            if (-not $TemplateName) {
                Write-Log "Please specify -TemplateName for installation" "ERROR"
                exit 1
            }

            $success = Install-Template -TemplateId $TemplateName
            if (-not $success) {
                exit 1
            }
        }

        "update" {
            if (-not $TemplateName) {
                Write-Log "Please specify -TemplateName for update" "ERROR"
                exit 1
            }

            Write-Log "Updating template: $TemplateName" "INFO"
            $success = Install-Template -TemplateId $TemplateName
            if (-not $success) {
                exit 1
            }
        }

        "remove" {
            if (-not $TemplateName) {
                Write-Log "Please specify -TemplateName for removal" "ERROR"
                exit 1
            }

            $templatePath = Join-Path $RepositoryPath $TemplateName
            if (Test-Path $templatePath) {
                if ($DryRun) {
                    Write-Log "DRY RUN: Would remove template '$TemplateName'" "INFO"
                }
                else {
                    Remove-Item -Path $templatePath -Recurse -Force
                    Write-Log "Template '$TemplateName' removed successfully" "SUCCESS"
                }
            }
            else {
                Write-Log "Template '$TemplateName' not found in repository" "ERROR"
                exit 1
            }
        }

        "validate" {
            if ($TemplateName) {
                if (-not $TemplateRegistry.ContainsKey($TemplateName)) {
                    Write-Log "Template '$TemplateName' not found in registry" "ERROR"
                    exit 1
                }

                $template = $TemplateRegistry[$TemplateName]
                $templateFile = Join-Path $SourcePath $template.File
                $validation = Validate-Template -TemplateFile $templateFile -TemplateName $template.Name

                if (-not $validation.Valid) {
                    exit 1
                }
            }
            else {
                $allValid = Validate-AllTemplates
                if (-not $allValid) {
                    exit 1
                }
            }
        }

        "package" {
            if (-not $TemplateName) {
                Write-Log "Please specify -TemplateName for packaging" "ERROR"
                exit 1
            }

            $success = Package-Template -TemplateId $TemplateName
            if (-not $success) {
                exit 1
            }
        }

        "publish" {
            Write-Log "Publish functionality not implemented in this version" "WARN"
            Write-Log "Consider creating a central template repository or using Git for sharing" "INFO"
        }

        "search" {
            if (-not $SearchQuery) {
                Write-Log "Please specify -SearchQuery for search" "ERROR"
                exit 1
            }

            Search-Templates -Query $SearchQuery
        }

        default {
            Write-Log "Invalid action: $Action" "ERROR"
            exit 1
        }
    }

    Write-Log "Template management operation completed" "SUCCESS"
}

# Execute main function
Main