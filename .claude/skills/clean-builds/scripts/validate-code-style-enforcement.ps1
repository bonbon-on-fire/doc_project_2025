#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Validates and enforces code style build enforcement (IDE0005 and related rules).

.DESCRIPTION
    This script checks all .csproj files to ensure they have EnforceCodeStyleInBuild enabled.
    This enables detection of style violations like IDE0005 (unused imports) during build.

    The script:
    - Finds all project files (.csproj)
    - Checks if EnforceCodeStyleInBuild is set to 'true'
    - Reports which projects are missing this setting
    - Optionally enables it for all projects

.PARAMETER Enforce
    If true, automatically add EnforceCodeStyleInBuild to projects that don't have it.
    Default: $false (report only)

.PARAMETER CheckOnly
    If true, only report without making changes. Ignored if -Enforce is set.
    Default: $true

.PARAMETER OutputFormat
    Output format: 'Console' (default), 'Json', or 'Summary'

.PARAMETER SaveToFile
    Optional file path to save the report

.EXAMPLE
    .\validate-code-style-enforcement.ps1
    Check which projects need EnforceCodeStyleInBuild

.EXAMPLE
    .\validate-code-style-enforcement.ps1 -Enforce
    Automatically enable EnforceCodeStyleInBuild in all projects

.EXAMPLE
    .\validate-code-style-enforcement.ps1 -OutputFormat Json -SaveToFile style-report.json
    Export findings to JSON
#>

param(
    [switch]$Enforce = $false,
    [switch]$CheckOnly = $true,
    [ValidateSet("Console", "Json", "Summary")]
    [string]$OutputFormat = "Console",
    [string]$SaveToFile = ""
)

# Get script directory and project root
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
# Try to find project root by looking for the .git directory
$currentDir = $scriptDir
$projectRoot = $null

while ($currentDir) {
    if (Test-Path (Join-Path $currentDir ".git")) {
        $projectRoot = $currentDir
        break
    }
    $parentDir = Split-Path -Parent $currentDir
    if ($parentDir -eq $currentDir) {
        # We've reached the root directory
        break
    }
    $currentDir = $parentDir
}

# If we couldn't find .git, assume current directory is project root
if (-not $projectRoot) {
    $projectRoot = Get-Location | Select-Object -ExpandProperty Path
}

Set-Location $projectRoot

# Colors for console output
$ErrorColor = "Red"
$WarningColor = "Yellow"
$InfoColor = "Cyan"
$SuccessColor = "Green"
$NormalColor = "White"

function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    Write-Host $Message -ForegroundColor $Color
}

function Find-ProjectFiles {
    param([string]$RootPath)

    # Look in server/ and client/ directories, excluding build/bin/obj folders and submodules
    $searchPaths = @(
        (Join-Path $RootPath "server"),
        (Join-Path $RootPath "client")
    )

    $projects = @()
    foreach ($path in $searchPaths) {
        if (Test-Path $path) {
            $projects += Get-ChildItem -Path $path -Filter "*.csproj" -Recurse |
                Where-Object {
                    $_.FullName -notlike "*\submodules\*" -and
                    $_.FullName -notlike "*\obj\*" -and
                    $_.FullName -notlike "*\bin\*"
                }
        }
    }

    return $projects
}

function Check-CodeStyleEnforcement {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content -Path $ProjectPath -Raw

    # Look for EnforceCodeStyleInBuild in PropertyGroup
    $propertyGroups = $projectXml.GetElementsByTagName("PropertyGroup")
    $enforceCodeStyle = $null

    foreach ($group in $propertyGroups) {
        $styleNode = $group.SelectSingleNode("EnforceCodeStyleInBuild")
        if ($null -ne $styleNode) {
            $enforceCodeStyle = $styleNode.InnerText
            break
        }
    }

    return @{
        HasProperty = $null -ne $enforceCodeStyle
        IsEnabled   = $enforceCodeStyle -eq "true"
        CurrentValue = $enforceCodeStyle
    }
}

function Enable-CodeStyleEnforcement {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content -Path $ProjectPath -Raw

    # Find or create first PropertyGroup
    $propertyGroup = $projectXml.SelectSingleNode("//PropertyGroup")

    if ($null -eq $propertyGroup) {
        Write-ColorOutput "ERROR: No PropertyGroup found in $ProjectPath" $ErrorColor
        return $false
    }

    # Check if property already exists
    $styleNode = $propertyGroup.SelectSingleNode("EnforceCodeStyleInBuild")

    if ($null -eq $styleNode) {
        # Create new element
        $newElement = $projectXml.CreateElement("EnforceCodeStyleInBuild")
        $newElement.InnerText = "true"
        $propertyGroup.AppendChild($newElement) | Out-Null

        # Save the file
        $projectXml.Save($ProjectPath)
        return $true
    }
    else {
        # Update existing element if needed
        if ($styleNode.InnerText -ne "true") {
            $styleNode.InnerText = "true"
            $projectXml.Save($ProjectPath)
            return $true
        }
    }

    return $false
}

# Main script
$projects = Find-ProjectFiles -RootPath $projectRoot
$report = @{
    TotalProjects = $projects.Count
    ProjectsWithEnforcement = 0
    ProjectsWithoutEnforcement = @()
    ProjectsUpdated = 0
    ProcessedAt = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
}

Write-ColorOutput "`n════════════════════════════════════════════════════════════════" $InfoColor
Write-ColorOutput "Code Style Enforcement Validation (IDE0005 and related rules)" $InfoColor
Write-ColorOutput "════════════════════════════════════════════════════════════════`n" $InfoColor

foreach ($project in $projects) {
    $check = Check-CodeStyleEnforcement -ProjectPath $project.FullName

    if ($check.IsEnabled) {
        $report.ProjectsWithEnforcement++
    }
    else {
        $report.ProjectsWithoutEnforcement += @{
            Path = $project.FullName
            CurrentValue = $check.CurrentValue
            RelativePath = $project.FullName -replace [regex]::Escape($projectRoot + "\"), ""
        }

        # Enable if requested
        if ($Enforce) {
            $updated = Enable-CodeStyleEnforcement -ProjectPath $project.FullName
            if ($updated) {
                $report.ProjectsUpdated++
                Write-ColorOutput "✓ Enabled EnforceCodeStyleInBuild in: $($project.Name)" $SuccessColor
            }
        }
    }
}

# Console output
if ($OutputFormat -eq "Console" -or $OutputFormat -eq "Summary") {
    Write-ColorOutput "`n════════════════════════════════════════════════════════════════" $InfoColor
    Write-ColorOutput "CODE STYLE ENFORCEMENT SUMMARY" $InfoColor
    Write-ColorOutput "════════════════════════════════════════════════════════════════" $InfoColor

    Write-ColorOutput "`nTotal Projects Scanned: $($report.TotalProjects)" $NormalColor
    Write-ColorOutput "✓ Projects with enforcement enabled: $($report.ProjectsWithEnforcement)" $SuccessColor
    Write-ColorOutput "✗ Projects without enforcement: $($report.ProjectsWithoutEnforcement.Count)" $WarningColor

    if ($report.ProjectsUpdated -gt 0) {
        Write-ColorOutput "✓ Projects updated: $($report.ProjectsUpdated)" $SuccessColor
    }

    if ($report.ProjectsWithoutEnforcement.Count -gt 0 -and -not $Enforce) {
        Write-ColorOutput "`n⚠  Projects missing EnforceCodeStyleInBuild:" $WarningColor
        foreach ($proj in $report.ProjectsWithoutEnforcement) {
            Write-ColorOutput "  • $($proj.RelativePath)" $WarningColor
        }

        Write-ColorOutput "`n💡 Run with -Enforce flag to automatically enable:" $InfoColor
        Write-ColorOutput "   .\validate-code-style-enforcement.ps1 -Enforce`n" $InfoColor
    }
    elseif ($report.ProjectsWithoutEnforcement.Count -eq 0) {
        Write-ColorOutput "`n✓ All projects have EnforceCodeStyleInBuild enabled!" $SuccessColor
    }
}

# JSON output
if ($OutputFormat -eq "Json") {
    $jsonReport = $report | ConvertTo-Json -Depth 10

    if ($SaveToFile) {
        $jsonReport | Out-File -FilePath $SaveToFile -Encoding UTF8
        Write-ColorOutput "`nReport saved to: $SaveToFile" $SuccessColor
    }
    else {
        Write-Output $jsonReport
    }
}

# Set exit code based on enforcement status
$exitCode = 0
if ($report.ProjectsWithoutEnforcement.Count -gt 0 -and -not $Enforce) {
    $exitCode = 1  # Fail if projects are missing enforcement and not fixed
}

exit $exitCode
