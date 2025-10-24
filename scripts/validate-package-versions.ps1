#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Validates NuGet package version consistency across all projects.

.DESCRIPTION
    This script scans all .csproj files in the solution and checks for version
    inconsistencies in package references. It identifies:
    - Packages with different versions across projects
    - Critical version mismatches that may cause compatibility issues
    - Recommendations for version consolidation

.PARAMETER CheckOnly
    If true, only report inconsistencies without making changes (default: $true)

.PARAMETER SaveToFile
    Optional file path to save the report as JSON

.PARAMETER OutputFormat
    Output format: 'Console' (default), 'Json', or 'Summary'

.EXAMPLE
    .\validate-package-versions.ps1
    Run package version validation and display results

.EXAMPLE
    .\validate-package-versions.ps1 -OutputFormat Json -SaveToFile version-report.json
    Export validation results to JSON file
#>

param(
    [switch]$CheckOnly = $true,
    [string]$SaveToFile = "",
    [ValidateSet("Console", "Json", "Summary")]
    [string]$OutputFormat = "Console"
)

# Get script directory and project root
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
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

    $projects = Get-ChildItem -Path $RootPath -Filter "*.csproj" -Recurse |
        Where-Object { $_.FullName -notlike "*\submodules\*" -and $_.FullName -notlike "*\obj\*" -and $_.FullName -notlike "*\bin\*" }

    return $projects
}

function Parse-PackageReferences {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content -Path $ProjectPath -Raw
    $packages = @()

    # Find all PackageReference elements
    $packageNodes = $projectXml.GetElementsByTagName("PackageReference")

    foreach ($node in $packageNodes) {
        $packageName = $node.GetAttribute("Include")
        $version = $node.GetAttribute("Version")

        if (-not [string]::IsNullOrEmpty($packageName) -and -not [string]::IsNullOrEmpty($version)) {
            $packages += [PSCustomObject]@{
                Name    = $packageName
                Version = $version
                File    = $ProjectPath
            }
        }
    }

    return $packages
}

function Analyze-VersionConsistency {
    param([object[]]$AllPackages)

    $groupedPackages = $AllPackages | Group-Object -Property Name

    $inconsistencies = @()
    $consistent = @()

    foreach ($group in $groupedPackages) {
        $packageName = $group.Name
        $versions = @($group.Group | Select-Object -ExpandProperty Version -Unique)

        if ($versions.Count -gt 1) {
            # Version inconsistency found
            $projects = $group.Group | ForEach-Object {
                [PSCustomObject]@{
                    Project = Split-Path -Leaf $_.File
                    Version = $_.Version
                    FilePath = $_.File
                }
            }

            $inconsistencies += [PSCustomObject]@{
                Package  = $packageName
                Versions = $versions
                Count    = $group.Count
                Projects = $projects
            }
        }
        else {
            $consistent += [PSCustomObject]@{
                Package = $packageName
                Version = $versions[0]
                Count   = $group.Count
            }
        }
    }

    return @{
        Inconsistencies = $inconsistencies
        Consistent      = $consistent
        TotalPackages   = $groupedPackages.Count
        TotalProjects   = ($AllPackages | Select-Object -ExpandProperty File -Unique).Count
    }
}

function Identify-KnownIssues {
    param([object[]]$Inconsistencies)

    $criticalIssues = @()
    $warningIssues = @()

    # Critical issues: Orleans framework version
    $orleanIssues = $Inconsistencies | Where-Object { $_.Package -like "*Orleans*" }
    if ($orleanIssues) {
        foreach ($issue in $orleanIssues) {
            $mainVersion = $issue.Versions | Sort-Object -Descending | Select-Object -First 1
            $oldVersions = $issue.Versions | Where-Object { $_ -ne $mainVersion }

            if ($oldVersions) {
                $criticalIssues += [PSCustomObject]@{
                    Severity = "CRITICAL"
                    Package  = $issue.Package
                    Message  = "Orleans framework version mismatch detected"
                    Current  = $oldVersions -join ", "
                    Expected = $mainVersion
                    Fix      = "Update Orleans packages to $mainVersion across all projects"
                    Projects = $issue.Projects
                }
            }
        }
    }

    # Warning issues: Minor version differences
    $microsoftExtensions = $Inconsistencies | Where-Object { $_.Package -like "Microsoft.Extensions*" }
    if ($microsoftExtensions) {
        foreach ($issue in $microsoftExtensions) {
            $warningIssues += [PSCustomObject]@{
                Severity = "WARNING"
                Package  = $issue.Package
                Message  = "Microsoft.Extensions package version variance"
                Versions = $issue.Versions -join ", "
                Fix      = "Verify if multiple versions are intentional or should be consolidated"
                Projects = $issue.Projects
            }
        }
    }

    return @{
        Critical = $criticalIssues
        Warnings = $warningIssues
    }
}

function Format-ConsoleOutput {
    param(
        [object]$Analysis,
        [object]$KnownIssues
    )

    Write-ColorOutput ("=" * 80) $InfoColor
    Write-ColorOutput "PACKAGE VERSION VALIDATION REPORT" $InfoColor
    Write-ColorOutput ("=" * 80) $InfoColor
    Write-ColorOutput ""

    Write-ColorOutput "Summary:" $InfoColor
    Write-ColorOutput "  Total Packages Analyzed: $($Analysis.TotalPackages)" $NormalColor
    Write-ColorOutput "  Total Projects Scanned: $($Analysis.TotalProjects)" $NormalColor
    Write-ColorOutput "  Consistent Packages: $($Analysis.Consistent.Count)" $SuccessColor
    Write-ColorOutput "  Inconsistent Packages: $($Analysis.Inconsistencies.Count)" $WarningColor
    Write-ColorOutput ""

    if ($KnownIssues.Critical.Count -gt 0) {
        Write-ColorOutput ("=" * 80) $ErrorColor
        Write-ColorOutput "CRITICAL ISSUES (Must Fix)" $ErrorColor
        Write-ColorOutput ("=" * 80) $ErrorColor
        Write-ColorOutput ""

        foreach ($issue in $KnownIssues.Critical) {
            Write-ColorOutput $issue.Package $ErrorColor
            Write-ColorOutput "  Severity: $($issue.Severity)" $ErrorColor
            Write-ColorOutput "  Message: $($issue.Message)" $ErrorColor
            Write-ColorOutput "  Current Versions: $($issue.Current)" $ErrorColor
            Write-ColorOutput "  Expected Version: $($issue.Expected)" $SuccessColor
            Write-ColorOutput "  Fix: $($issue.Fix)" $NormalColor
            Write-ColorOutput "  Projects Affected:" $NormalColor

            foreach ($proj in $issue.Projects) {
                if ($proj.Version -ne $issue.Expected) {
                    Write-ColorOutput "    - $($proj.Project) [$($proj.Version)]" $ErrorColor
                    Write-ColorOutput "      File: $($proj.FilePath)" "Gray"
                }
            }
            Write-ColorOutput ""
        }
    }

    if ($KnownIssues.Warnings.Count -gt 0) {
        Write-ColorOutput ("=" * 80) $WarningColor
        Write-ColorOutput "WARNINGS (Review & Consider Consolidating)" $WarningColor
        Write-ColorOutput ("=" * 80) $WarningColor
        Write-ColorOutput ""

        foreach ($issue in $KnownIssues.Warnings) {
            Write-ColorOutput $issue.Package $WarningColor
            Write-ColorOutput "  Severity: $($issue.Severity)" $WarningColor
            Write-ColorOutput "  Message: $($issue.Message)" $WarningColor
            Write-ColorOutput "  Versions: $($issue.Versions)" $NormalColor
            Write-ColorOutput "  Fix: $($issue.Fix)" $NormalColor
            Write-ColorOutput "  Projects:" $NormalColor

            foreach ($proj in $issue.Projects) {
                Write-ColorOutput "    - $($proj.Project) [$($proj.Version)]" "Gray"
                Write-ColorOutput "      File: $($proj.FilePath)" "Gray"
            }
            Write-ColorOutput ""
        }
    }

    if ($Analysis.Inconsistencies.Count -gt 0 -and $KnownIssues.Critical.Count -eq 0) {
        Write-ColorOutput ("=" * 80) $WarningColor
        Write-ColorOutput "ALL INCONSISTENCIES" $WarningColor
        Write-ColorOutput ("=" * 80) $WarningColor
        Write-ColorOutput ""

        foreach ($issue in $Analysis.Inconsistencies) {
            Write-ColorOutput $issue.Package $WarningColor
            Write-ColorOutput "  Versions Found: $($issue.Versions -join ', ')" $NormalColor
            Write-ColorOutput "  Projects Using This Package: $($issue.Count)" $NormalColor
            Write-ColorOutput "  Details:" $NormalColor

            foreach ($proj in $issue.Projects) {
                Write-ColorOutput "    - $($proj.Project): $($proj.Version)" "Gray"
                Write-ColorOutput "      File: $($proj.FilePath)" "Gray"
            }
            Write-ColorOutput ""
        }
    }

    if ($Analysis.Inconsistencies.Count -eq 0) {
        Write-ColorOutput "✅ All packages have consistent versions!" $SuccessColor
        Write-ColorOutput ""
    }

    # Exit codes
    if ($KnownIssues.Critical.Count -gt 0) {
        Write-ColorOutput ""
        Write-ColorOutput "❌ VALIDATION FAILED: $($KnownIssues.Critical.Count) critical issue(s) found" $ErrorColor
        Write-ColorOutput "ACTION REQUIRED: Fix critical issues before proceeding" $ErrorColor
        return $false
    }
    elseif ($KnownIssues.Warnings.Count -gt 0) {
        Write-ColorOutput ""
        Write-ColorOutput "⚠️  VALIDATION WARNING: $($KnownIssues.Warnings.Count) warning(s) found" $WarningColor
        Write-ColorOutput "Consider consolidating package versions where possible" $WarningColor
        return $true
    }
    else {
        Write-ColorOutput ""
        Write-ColorOutput "✅ VALIDATION PASSED: No critical issues found" $SuccessColor
        return $true
    }
}

function Export-ToJson {
    param(
        [object]$Analysis,
        [object]$KnownIssues,
        [object[]]$AllPackages,
        [string]$FilePath
    )

    $output = @{
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        Summary   = @{
            TotalPackages          = $Analysis.TotalPackages
            TotalProjects          = $Analysis.TotalProjects
            ConsistentPackages     = $Analysis.Consistent.Count
            InconsistentPackages   = $Analysis.Inconsistencies.Count
            CriticalIssues         = $KnownIssues.Critical.Count
            Warnings               = $KnownIssues.Warnings.Count
        }
        Issues    = @{
            Critical      = $KnownIssues.Critical
            Warnings      = $KnownIssues.Warnings
            AllInconsistencies = $Analysis.Inconsistencies
        }
        AllPackages = $AllPackages | Group-Object -Property Name | ForEach-Object {
            @{
                Name     = $_.Name
                Versions = ($_.Group | Select-Object -ExpandProperty Version -Unique)
                Projects = $_.Group | Select-Object -Property @{ Name = "Project"; Expression = { Split-Path -Leaf $_.File } }, Version
            }
        }
    }

    $output | ConvertTo-Json -Depth 10 | Out-File -FilePath $FilePath -Encoding UTF8
    Write-ColorOutput "Report exported to: $FilePath" $SuccessColor
}

# Main execution
try {
    Write-ColorOutput "Starting NuGet Package Version Validation..." $InfoColor
    Write-ColorOutput ""

    # Find all project files
    Write-ColorOutput "Scanning for project files..." $InfoColor
    $projects = Find-ProjectFiles -RootPath $projectRoot
    Write-ColorOutput "Found $($projects.Count) projects" $InfoColor
    Write-ColorOutput ""

    if ($projects.Count -eq 0) {
        Write-ColorOutput "No projects found!" $ErrorColor
        exit 1
    }

    # Parse all package references
    Write-ColorOutput "Parsing package references..." $InfoColor
    $allPackages = @()
    foreach ($project in $projects) {
        $packages = Parse-PackageReferences -ProjectPath $project.FullName
        $allPackages += $packages
        Write-ColorOutput "  - $($project.Name): $($packages.Count) packages" "Gray"
    }
    Write-ColorOutput "Total packages found: $($allPackages.Count)" $InfoColor
    Write-ColorOutput ""

    # Analyze consistency
    Write-ColorOutput "Analyzing version consistency..." $InfoColor
    $analysis = Analyze-VersionConsistency -AllPackages $allPackages
    Write-ColorOutput ""

    # Identify known issues
    Write-ColorOutput "Checking for known issues..." $InfoColor
    $knownIssues = Identify-KnownIssues -Inconsistencies $analysis.Inconsistencies
    Write-ColorOutput ""

    # Format output
    switch ($OutputFormat) {
        "Console" {
            $success = Format-ConsoleOutput -Analysis $analysis -KnownIssues $knownIssues
        }
        "Json" {
            if ($SaveToFile) {
                Export-ToJson -Analysis $analysis -KnownIssues $knownIssues -AllPackages $allPackages -FilePath $SaveToFile
                $success = $knownIssues.Critical.Count -eq 0
            }
            else {
                $analysis | ConvertTo-Json -Depth 10
                $success = $knownIssues.Critical.Count -eq 0
            }
        }
        "Summary" {
            Write-ColorOutput "Package Version Summary:" $InfoColor
            Write-ColorOutput "  Total Packages: $($analysis.TotalPackages)" $NormalColor
            Write-ColorOutput "  Total Projects: $($analysis.TotalProjects)" $NormalColor
            Write-ColorOutput "  Consistent: $($analysis.Consistent.Count)" $SuccessColor
            Write-ColorOutput "  Inconsistent: $($analysis.Inconsistencies.Count)" $WarningColor
            Write-ColorOutput "  Critical Issues: $($knownIssues.Critical.Count)" $ErrorColor
            Write-ColorOutput "  Warnings: $($knownIssues.Warnings.Count)" $WarningColor
            $success = $knownIssues.Critical.Count -eq 0
        }
    }

    # Exit with appropriate code
    if ($knownIssues.Critical.Count -gt 0) {
        exit 1
    }
    else {
        exit 0
    }
}
catch {
    Write-ColorOutput "Error: $($_.Exception.Message)" $ErrorColor
    Write-ColorOutput $_.ScriptStackTrace "Gray"
    exit 1
}
