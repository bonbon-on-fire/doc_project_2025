<#
.SYNOPSIS
    Configures .editorconfig file with Roslynator analyzer severity settings.

.DESCRIPTION
    This script creates or updates the .editorconfig file at the solution root to configure
    Roslynator analyzer rules. You can set the global severity level for all Roslynator rules
    or configure specific rules individually.

.PARAMETER Severity
    Global severity level for all Roslynator rules: none, silent, suggestion, warning, or error.
    Default is 'warning'.

.PARAMETER ConfigFile
    Path to the .editorconfig file. Defaults to .editorconfig in the current directory.

.PARAMETER CreateIfMissing
    Create a new .editorconfig file if it doesn't exist. Default is $true.

.PARAMETER ShowPreview
    Show what changes would be made without modifying the file.

.PARAMETER EnableAnalyzers
    Explicitly enable Roslynator analyzers (default: true).

.EXAMPLE
    .\configure-roslynator-editorconfig.ps1
    Sets all Roslynator rules to 'warning' severity

.EXAMPLE
    .\configure-roslynator-editorconfig.ps1 -Severity error
    Sets all Roslynator rules to 'error' severity (build will fail on violations)

.EXAMPLE
    .\configure-roslynator-editorconfig.ps1 -ShowPreview
    Shows what would be added to .editorconfig without making changes

.EXAMPLE
    .\configure-roslynator-editorconfig.ps1 -Severity suggestion -ConfigFile "src\.editorconfig"
    Configures a specific .editorconfig file with 'suggestion' severity

.EXAMPLE
    .\configure-roslynator-editorconfig.ps1 -WhatIf
    Shows what changes would be made without actually modifying the file

.NOTES
    File Name      : configure-roslynator-editorconfig.ps1
    Author         : Clean Builds Skill
    Prerequisite   : PowerShell 5.0+
    Exit Codes     : 0 = Success, 1 = Failure
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("none", "silent", "suggestion", "warning", "error")]
    [string]$Severity = "warning",

    [Parameter(Mandatory = $false)]
    [string]$ConfigFile = ".editorconfig",

    [Parameter(Mandatory = $false)]
    [bool]$CreateIfMissing = $true,

    [Parameter(Mandatory = $false)]
    [switch]$ShowPreview,

    [Parameter(Mandatory = $false)]
    [bool]$EnableAnalyzers = $true
)

# Color definitions
$script:InfoColor = "Cyan"
$script:SuccessColor = "Green"
$script:WarningColor = "Yellow"
$script:ErrorColor = "Red"

function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    Write-Host $Message -ForegroundColor $Color
}

function Get-RoslynatorConfigSection {
    param(
        [string]$Severity,
        [bool]$EnableAnalyzers
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

    return @"

# ========================================
# Roslynator Analyzer Configuration
# Auto-generated on $timestamp
# ========================================

[*.cs]

# Enable Roslynator analyzers
roslynator_analyzers.enabled_by_default = $($EnableAnalyzers.ToString().ToLower())

# Set global severity for all Roslynator rules
# Options: none, silent, suggestion, warning, error
dotnet_analyzer_diagnostic.category-roslynator.severity = $Severity

# Enable refactorings (code fixes)
roslynator_refactorings.enabled = true

# Enable compiler diagnostic fixes
roslynator_compiler_diagnostic_fixes.enabled = true

# ========================================
# Roslynator Code Style Options
# ========================================

# Accessibility modifiers
roslynator_accessibility_modifiers = explicit

# Array creation - prefer collection expressions in C# 12+
roslynator_array_creation_type_style = implicit

# Use var when type is obvious
roslynator_use_var = when_type_is_obvious

# Prefix field identifiers with underscore
roslynator_prefix_field_identifier_with_underscore = true

# Use block body for local functions
roslynator_body_style = block

# Blank line after file scoped namespace declaration
roslynator_blank_line_after_file_scoped_namespace_declaration = true

# Use collection expression for array, list, collection, etc. (C# 12+)
roslynator_use_collection_expression = true

# ========================================
# Common Roslynator Rules Configuration
# ========================================

# RCS1036: Remove unnecessary blank line
dotnet_diagnostic.rcs1036.severity = suggestion

# RCS1037: Remove trailing white-space
dotnet_diagnostic.rcs1037.severity = warning

# RCS1138: Add summary to documentation comment
dotnet_diagnostic.rcs1138.severity = suggestion

# RCS1163: Unused parameter
dotnet_diagnostic.rcs1163.severity = warning

# RCS1186: Use Regex instance instead of static method
dotnet_diagnostic.rcs1186.severity = suggestion

# RCS1090: Call 'ConfigureAwait(false)'
dotnet_diagnostic.rcs1090.severity = none

# Add more specific rules here as needed...

"@
}

function Test-HasRoslynatorConfig {
    param(
        [string]$FilePath
    )

    if (-not (Test-Path $FilePath)) {
        return $false
    }

    $content = Get-Content -Path $FilePath -Raw
    return $content -match "dotnet_analyzer_diagnostic\.category-roslynator\.severity"
}

function Add-RoslynatorConfig {
    param(
        [string]$FilePath,
        [string]$Severity,
        [bool]$EnableAnalyzers
    )

    try {
        $roslynatorSection = Get-RoslynatorConfigSection -Severity $Severity -EnableAnalyzers $EnableAnalyzers

        if (Test-Path $FilePath) {
            # File exists - check if Roslynator config already present
            if (Test-HasRoslynatorConfig -FilePath $FilePath) {
                Write-ColorOutput "[WARNING] Roslynator configuration already exists in $FilePath" $WarningColor
                Write-ColorOutput "[INFO] Please remove existing Roslynator section or update manually" $InfoColor
                return $false
            }

            # ShouldProcess check before modifying
            if (-not $PSCmdlet.ShouldProcess($FilePath, "Append Roslynator configuration")) {
                Write-ColorOutput "[WHATIF] Would append Roslynator configuration to $FilePath" $InfoColor
                return $false
            }

            # Append to existing file
            $existingContent = Get-Content -Path $FilePath -Raw
            $newContent = $existingContent.TrimEnd() + "`n" + $roslynatorSection

            Set-Content -Path $FilePath -Value $newContent -NoNewline
            Write-ColorOutput "[UPDATED] Added Roslynator configuration to existing $FilePath" $SuccessColor
        }
        else {
            # Create new file
            if (-not $CreateIfMissing) {
                Write-ColorOutput "[ERROR] File does not exist: $FilePath (use -CreateIfMissing to create)" $ErrorColor
                return $false
            }

            # ShouldProcess check before creating
            if (-not $PSCmdlet.ShouldProcess($FilePath, "Create .editorconfig with Roslynator configuration")) {
                Write-ColorOutput "[WHATIF] Would create $FilePath with Roslynator configuration" $InfoColor
                return $false
            }

            # Basic .editorconfig header
            $header = @"
# EditorConfig is awesome: https://EditorConfig.org

# top-most EditorConfig file
root = true

# All files
[*]
charset = utf-8
insert_final_newline = true
trim_trailing_whitespace = true
"@

            $newContent = $header + $roslynatorSection

            Set-Content -Path $FilePath -Value $newContent -NoNewline
            Write-ColorOutput "[CREATED] New .editorconfig with Roslynator configuration" $SuccessColor
        }

        return $true
    }
    catch {
        Write-ColorOutput "[ERROR] Failed to configure .editorconfig: $_" $ErrorColor
        return $false
    }
}

# Main execution
try {
    Write-ColorOutput "`n========================================" $InfoColor
    Write-ColorOutput "  Roslynator EditorConfig Setup" $InfoColor
    Write-ColorOutput "========================================`n" $InfoColor

    # Resolve file path
    $resolvedPath = Resolve-Path -Path $ConfigFile -ErrorAction SilentlyContinue
    if ($null -eq $resolvedPath) {
        $resolvedPath = Join-Path (Get-Location) $ConfigFile
    }
    else {
        $resolvedPath = $resolvedPath.Path
    }

    Write-ColorOutput "[INFO] Target file: $resolvedPath" $InfoColor
    Write-ColorOutput "[INFO] Severity level: $Severity" $InfoColor
    Write-ColorOutput "[INFO] Enable analyzers: $EnableAnalyzers`n" $InfoColor

    # Show preview if requested
    if ($ShowPreview) {
        Write-ColorOutput "[PREVIEW] The following configuration would be added:`n" $WarningColor
        $preview = Get-RoslynatorConfigSection -Severity $Severity -EnableAnalyzers $EnableAnalyzers
        Write-Host $preview
        Write-ColorOutput "`n[INFO] No changes were made (preview mode)" $InfoColor
        exit 0
    }

    # Check if file exists
    $fileExists = Test-Path $resolvedPath
    $hasExistingConfig = $false

    if ($fileExists) {
        Write-ColorOutput "[INFO] File exists, will append Roslynator configuration" $InfoColor
        $hasExistingConfig = Test-HasRoslynatorConfig -FilePath $resolvedPath

        if ($hasExistingConfig) {
            Write-ColorOutput "[WARNING] Roslynator configuration already exists!" $WarningColor
            Write-ColorOutput "[ACTION] Please manually update the existing configuration or remove it first." $WarningColor
            exit 1
        }
    }
    else {
        if (-not $CreateIfMissing) {
            Write-ColorOutput "[ERROR] File does not exist and -CreateIfMissing is false" $ErrorColor
            exit 1
        }
        Write-ColorOutput "[INFO] File does not exist, will create new .editorconfig" $InfoColor
    }

    # Add configuration
    $success = Add-RoslynatorConfig -FilePath $resolvedPath -Severity $Severity -EnableAnalyzers $EnableAnalyzers

    if ($success) {
        Write-ColorOutput "`n========================================" $InfoColor
        Write-ColorOutput "  Configuration Complete" $InfoColor
        Write-ColorOutput "========================================" $InfoColor
        Write-ColorOutput "[SUCCESS] Roslynator configuration added to .editorconfig" $SuccessColor
        Write-ColorOutput "`n[NEXT STEPS]" $InfoColor
        Write-ColorOutput "1. Review the .editorconfig file: $resolvedPath" $InfoColor
        Write-ColorOutput "2. Customize individual rule severities as needed" $InfoColor
        Write-ColorOutput "3. Restart your IDE for changes to take effect" $InfoColor
        Write-ColorOutput "4. Run 'dotnet build' to see Roslynator warnings/errors" $InfoColor
        exit 0
    }
    else {
        Write-ColorOutput "`n[ERROR] Failed to configure Roslynator in .editorconfig" $ErrorColor
        exit 1
    }
}
catch {
    Write-ColorOutput "`n[ERROR] Script failed: $_" $ErrorColor
    Write-ColorOutput $_.ScriptStackTrace $ErrorColor
    exit 1
}
