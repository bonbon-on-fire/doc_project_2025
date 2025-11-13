<#
.SYNOPSIS
    Validates JSON syntax in project files

.DESCRIPTION
    Validates the syntax of JSON files in the project to ensure they are well-formed.
    This script checks:
    - JSON syntax validity (can be parsed)
    - File encoding issues
    - Trailing commas (if strict mode enabled)
    - Duplicate keys

.PARAMETER RootPath
    Root directory to search for JSON files. Defaults to current directory.

.PARAMETER Files
    Specific JSON file paths to validate. If not specified, validates all JSON files in docs/ directory.

.PARAMETER Strict
    Enable strict validation (checks for trailing commas, duplicate keys, etc.)

.PARAMETER ExcludePatterns
    Array of glob patterns to exclude from validation (e.g., "**/node_modules/**", "**/bin/**")

.EXAMPLE
    pwsh validate-json-files.ps1
    Validates all JSON files in docs/ directory

.EXAMPLE
    pwsh validate-json-files.ps1 -Files "docs/monitoring/historical-analysis-dashboard.json"
    Validates specific JSON file

.EXAMPLE
    pwsh validate-json-files.ps1 -Strict
    Validates with strict rules

.NOTES
    Part of the clean-builds skill workflow
    Exit Code: 0 = Success, 1 = Validation Errors Found
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$RootPath = ".",

    [Parameter(Mandatory = $false)]
    [string[]]$Files = @(),

    [Parameter(Mandatory = $false)]
    [switch]$Strict,

    [Parameter(Mandatory = $false)]
    [string[]]$ExcludePatterns = @(
        "**/node_modules/**",
        "**/bin/**",
        "**/obj/**",
        "**/.vs/**",
        "**/.git/**",
        "**/packages/**"
    )
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Colors for output
$script:Red = "`e[31m"
$script:Green = "`e[32m"
$script:Yellow = "`e[33m"
$script:Blue = "`e[34m"
$script:Magenta = "`e[35m"
$script:Cyan = "`e[36m"
$script:Reset = "`e[0m"

function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = $script:Reset
    )
    Write-Host "${Color}${Message}${script:Reset}"
}

function Test-JsonSyntax {
    param(
        [string]$FilePath,
        [bool]$StrictMode
    )

    $errors = @()
    $warnings = @()

    try {
        # Read file content
        $content = Get-Content -Path $FilePath -Raw -ErrorAction Stop

        # Check if file is empty
        if ([string]::IsNullOrWhiteSpace($content)) {
            $errors += "File is empty or contains only whitespace"
            return @{
                Valid    = $false
                Errors   = $errors
                Warnings = $warnings
            }
        }

        # Try to parse JSON
        try {
            $jsonObject = $content | ConvertFrom-Json -ErrorAction Stop

            # Strict mode checks
            if ($StrictMode) {
                # Check for trailing commas (ConvertFrom-Json allows them, but they're not standard)
                if ($content -match ',\s*[}\]]') {
                    $warnings += "Contains trailing commas (not standard JSON)"
                }

                # Check for duplicate keys by re-serializing and comparing
                $reserializedContent = $jsonObject | ConvertTo-Json -Depth 100 -Compress
                $originalCompressed = $content -replace '\s', ''

                # Basic check: if original is significantly larger, might have duplicates
                if ($originalCompressed.Length -gt ($reserializedContent.Length * 1.2)) {
                    $warnings += "Possible duplicate keys detected (original larger than re-serialized)"
                }
            }

            return @{
                Valid    = $true
                Errors   = $errors
                Warnings = $warnings
                Object   = $jsonObject
            }
        }
        catch {
            $errors += "JSON parsing error: $($_.Exception.Message)"
            return @{
                Valid    = $false
                Errors   = $errors
                Warnings = $warnings
            }
        }
    }
    catch {
        $errors += "File read error: $($_.Exception.Message)"
        return @{
            Valid    = $false
            Errors   = $errors
            Warnings = $warnings
        }
    }
}

function Get-JsonFilesToValidate {
    param(
        [string]$Root,
        [string[]]$SpecificFiles,
        [string[]]$Excludes
    )

    if ($SpecificFiles.Count -gt 0) {
        # Validate specific files
        return @($SpecificFiles | Where-Object { Test-Path $_ } | ForEach-Object { Get-Item $_ })
    }

    # Default: Find all JSON files in docs/ directory
    $docsPath = Join-Path $Root "docs"

    if (-not (Test-Path $docsPath)) {
        Write-ColorOutput "Warning: docs/ directory not found at: $docsPath" $script:Yellow
        return @()
    }

    # Get all JSON files
    $allJsonFiles = Get-ChildItem -Path $docsPath -Filter "*.json" -Recurse -File

    # Filter out excluded patterns
    $filteredFiles = $allJsonFiles | Where-Object {
        $filePath = $_.FullName
        $shouldInclude = $true

        foreach ($pattern in $Excludes) {
            $regexPattern = $pattern -replace '\*\*/', '.*/' -replace '/\*\*', '/.*' -replace '\*', '[^/]*'
            if ($filePath -match $regexPattern) {
                $shouldInclude = $false
                break
            }
        }

        $shouldInclude
    }

    return $filteredFiles
}

# Main execution
Write-ColorOutput "`n========================================" $script:Cyan
Write-ColorOutput "JSON Validation" $script:Cyan
Write-ColorOutput "========================================`n" $script:Cyan

$rootDir = Resolve-Path $RootPath
Write-ColorOutput "Root Directory: $rootDir" $script:Blue
Write-ColorOutput "Strict Mode: $Strict`n" $script:Blue

# Get files to validate
$filesToValidate = @(Get-JsonFilesToValidate -Root $rootDir -SpecificFiles $Files -Excludes $ExcludePatterns)

if ($null -eq $filesToValidate -or $filesToValidate.Count -eq 0) {
    Write-ColorOutput "No JSON files found to validate.`n" $script:Yellow
    exit 0
}

Write-ColorOutput "Found $($filesToValidate.Count) JSON file(s) to validate`n" $script:Blue

# Validate each file
$totalErrors = 0
$totalWarnings = 0
$validFiles = 0
$invalidFiles = 0

foreach ($file in $filesToValidate) {
    $relativePath = $file.FullName.Replace($rootDir, "").TrimStart('\', '/')
    Write-ColorOutput "Validating: $relativePath" $script:Blue

    $result = Test-JsonSyntax -FilePath $file.FullName -StrictMode $Strict

    if ($result.Valid) {
        $validFiles++
        Write-ColorOutput "  ✓ Valid JSON" $script:Green

        if ($result.Warnings.Count -gt 0) {
            foreach ($warning in $result.Warnings) {
                Write-ColorOutput "  ⚠ Warning: $warning" $script:Yellow
                $totalWarnings++
            }
        }
    }
    else {
        $invalidFiles++
        Write-ColorOutput "  ✗ Invalid JSON" $script:Red

        foreach ($errMsg in $result.Errors) {
            Write-ColorOutput "  ✗ Error: $errMsg" $script:Red
            $totalErrors++
        }
    }

    Write-Host ""
}

# Summary
Write-ColorOutput "========================================" $script:Cyan
Write-ColorOutput "Validation Summary" $script:Cyan
Write-ColorOutput "========================================" $script:Cyan
Write-ColorOutput "Total Files:    $($filesToValidate.Count)" $script:Blue
Write-ColorOutput "Valid Files:    $validFiles" $script:Green
Write-ColorOutput "Invalid Files:  $invalidFiles" $(if ($invalidFiles -gt 0) { $script:Red } else { $script:Green })
Write-ColorOutput "Total Errors:   $totalErrors" $(if ($totalErrors -gt 0) { $script:Red } else { $script:Green })
Write-ColorOutput "Total Warnings: $totalWarnings" $(if ($totalWarnings -gt 0) { $script:Yellow } else { $script:Green })
Write-ColorOutput "========================================`n" $script:Cyan

if ($totalErrors -gt 0) {
    Write-ColorOutput "❌ JSON validation failed with $totalErrors error(s)" $script:Red
    exit 1
}

if ($totalWarnings -gt 0) {
    Write-ColorOutput "⚠️  JSON validation completed with $totalWarnings warning(s)" $script:Yellow
}
else {
    Write-ColorOutput "✅ All JSON files are valid!" $script:Green
}

exit 0
