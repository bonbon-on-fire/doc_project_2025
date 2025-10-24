<#
.SYNOPSIS
    Enables Roslynator.Analyzers in all .NET projects for compile-time code analysis.

.DESCRIPTION
    This script adds the Roslynator.Analyzers NuGet package to all .csproj files in the solution.
    Roslynator provides 200+ code analyzers that run during build, producing warnings/errors
    for code quality issues, style violations, and potential bugs.

.PARAMETER RoslynatorVersion
    The version of Roslynator.Analyzers to install. Defaults to 4.14.1 (latest as of 2025).

.PARAMETER CheckOnly
    Only check which projects are missing Roslynator.Analyzers without making changes.

.PARAMETER RemoveAnalyzers
    Remove Roslynator.Analyzers from all projects (useful for reverting).

.PARAMETER ExcludeSubmodules
    Exclude projects in the submodules/ directory from processing. Default is $false.

.PARAMETER OutputFormat
    Output format: Console (default), Json, or Summary.

.PARAMETER SaveToFile
    Save the output to a file at the specified path.

.EXAMPLE
    .\enable-roslynator-analyzers.ps1
    Adds Roslynator.Analyzers 4.14.1 to all projects

.EXAMPLE
    .\enable-roslynator-analyzers.ps1 -CheckOnly
    Checks which projects are missing Roslynator.Analyzers

.EXAMPLE
    .\enable-roslynator-analyzers.ps1 -RoslynatorVersion 4.12.0
    Adds specific version of Roslynator.Analyzers

.EXAMPLE
    .\enable-roslynator-analyzers.ps1 -RemoveAnalyzers
    Removes Roslynator.Analyzers from all projects

.EXAMPLE
    .\enable-roslynator-analyzers.ps1 -ExcludeSubmodules
    Adds Roslynator.Analyzers only to main project files, excluding submodules

.EXAMPLE
    .\enable-roslynator-analyzers.ps1 -WhatIf
    Shows what would be changed without actually making modifications

.NOTES
    File Name      : enable-roslynator-analyzers.ps1
    Author         : Clean Builds Skill
    Prerequisite   : PowerShell 5.0+
    Exit Codes     : 0 = Success, 1 = Failure
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $false)]
    [string]$RoslynatorVersion = "4.14.1",  # Latest as of October 2025 - check https://www.nuget.org/packages/Roslynator.Analyzers

    [Parameter(Mandatory = $false)]
    [switch]$CheckOnly,

    [Parameter(Mandatory = $false)]
    [switch]$RemoveAnalyzers,

    [Parameter(Mandatory = $false)]
    [switch]$ExcludeSubmodules,

    [Parameter(Mandatory = $false)]
    [ValidateSet("Console", "Json", "Summary")]
    [string]$OutputFormat = "Console",

    [Parameter(Mandatory = $false)]
    [string]$SaveToFile
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

    if ($OutputFormat -eq "Console") {
        Write-Host $Message -ForegroundColor $Color
    }
}

function Find-ProjectFiles {
    param(
        [string]$RootPath = (Get-Location).Path
    )

    Write-ColorOutput "`n[INFO] Scanning for .csproj files..." $InfoColor

    $projectFiles = Get-ChildItem -Path $RootPath -Recurse -Filter "*.csproj" |
        Where-Object {
            # Exclude build output directories
            $_.FullName -notmatch '\\bin\\' -and
            $_.FullName -notmatch '\\obj\\' -and
            $_.FullName -notmatch '\\node_modules\\'
        }

    Write-ColorOutput "[INFO] Found $($projectFiles.Count) project files" $InfoColor

    return $projectFiles
}

function Filter-SubmoduleProjects {
    param(
        [array]$ProjectFiles,
        [bool]$ExcludeSubmodules
    )

    if ($ExcludeSubmodules) {
        $originalCount = $ProjectFiles.Count
        $ProjectFiles = $ProjectFiles | Where-Object {
            $_.FullName -notmatch '\\submodules\\'
        }
        $excludedCount = $originalCount - $ProjectFiles.Count
        Write-ColorOutput "[INFO] Excluding submodule projects ($excludedCount excluded, $($ProjectFiles.Count) remaining)" $InfoColor
    }

    return $ProjectFiles
}

function Test-HasRoslynatorAnalyzers {
    param(
        [string]$ProjectFilePath
    )

    [xml]$projectXml = Get-Content -Path $ProjectFilePath

    $packageReferences = $projectXml.SelectNodes("//PackageReference[@Include='Roslynator.Analyzers']")

    return ($null -ne $packageReferences -and $packageReferences.Count -gt 0)
}

function Add-RoslynatorAnalyzers {
    param(
        [string]$ProjectFilePath,
        [string]$Version
    )

    try {
        try {
            [xml]$projectXml = Get-Content -Path $ProjectFilePath -ErrorAction Stop
        }
        catch {
            Write-ColorOutput "  [ERROR] Failed to parse project file (malformed XML): $_" $ErrorColor
            return $false
        }

        # Check if already exists
        if (Test-HasRoslynatorAnalyzers -ProjectFilePath $ProjectFilePath) {
            Write-ColorOutput "  [SKIP] Already has Roslynator.Analyzers" $WarningColor
            return $false
        }

        # ShouldProcess check before making changes
        if (-not $PSCmdlet.ShouldProcess($ProjectFilePath, "Add Roslynator.Analyzers $Version")) {
            Write-ColorOutput "  [WHATIF] Would add Roslynator.Analyzers $Version" $InfoColor
            return $false
        }

        # Find or create ItemGroup for PackageReferences
        $itemGroup = $projectXml.SelectSingleNode("//ItemGroup[PackageReference]")

        if ($null -eq $itemGroup) {
            # Create new ItemGroup
            $itemGroup = $projectXml.CreateElement("ItemGroup")
            $projectXml.Project.AppendChild($itemGroup) | Out-Null
        }

        # Create PackageReference element
        $packageRef = $projectXml.CreateElement("PackageReference")
        $packageRef.SetAttribute("Include", "Roslynator.Analyzers")
        $packageRef.SetAttribute("Version", $Version)

        # Create PrivateAssets element
        $privateAssets = $projectXml.CreateElement("PrivateAssets")
        $privateAssets.InnerText = "all"
        $packageRef.AppendChild($privateAssets) | Out-Null

        # Create IncludeAssets element
        $includeAssets = $projectXml.CreateElement("IncludeAssets")
        $includeAssets.InnerText = "runtime; build; native; contentfiles; analyzers"
        $packageRef.AppendChild($includeAssets) | Out-Null

        # Add to ItemGroup
        $itemGroup.AppendChild($packageRef) | Out-Null

        # Save with proper formatting and encoding
        $settings = New-Object System.Xml.XmlWriterSettings
        $settings.Indent = $true
        $settings.IndentChars = "  "
        $settings.Encoding = New-Object System.Text.UTF8Encoding($false)  # UTF-8 without BOM
        $settings.OmitXmlDeclaration = $true

        $writer = [System.Xml.XmlWriter]::Create($ProjectFilePath, $settings)
        try {
            $projectXml.Save($writer)
        }
        finally {
            $writer.Close()
        }

        Write-ColorOutput "  [ADDED] Roslynator.Analyzers $Version" $SuccessColor
        return $true
    }
    catch {
        Write-ColorOutput "  [ERROR] Failed to add Roslynator.Analyzers: $_" $ErrorColor
        return $false
    }
}

function Remove-RoslynatorAnalyzers {
    param(
        [string]$ProjectFilePath
    )

    try {
        try {
            [xml]$projectXml = Get-Content -Path $ProjectFilePath -ErrorAction Stop
        }
        catch {
            Write-ColorOutput "  [ERROR] Failed to parse project file (malformed XML): $_" $ErrorColor
            return $false
        }

        # Find Roslynator.Analyzers PackageReference
        $packageReferences = $projectXml.SelectNodes("//PackageReference[@Include='Roslynator.Analyzers']")

        if ($null -eq $packageReferences -or $packageReferences.Count -eq 0) {
            Write-ColorOutput "  [SKIP] No Roslynator.Analyzers found" $WarningColor
            return $false
        }

        # ShouldProcess check before making changes
        if (-not $PSCmdlet.ShouldProcess($ProjectFilePath, "Remove Roslynator.Analyzers")) {
            Write-ColorOutput "  [WHATIF] Would remove Roslynator.Analyzers" $InfoColor
            return $false
        }

        foreach ($packageRef in $packageReferences) {
            $packageRef.ParentNode.RemoveChild($packageRef) | Out-Null
        }

        # Save with proper formatting and encoding
        $settings = New-Object System.Xml.XmlWriterSettings
        $settings.Indent = $true
        $settings.IndentChars = "  "
        $settings.Encoding = New-Object System.Text.UTF8Encoding($false)  # UTF-8 without BOM
        $settings.OmitXmlDeclaration = $true

        $writer = [System.Xml.XmlWriter]::Create($ProjectFilePath, $settings)
        try {
            $projectXml.Save($writer)
        }
        finally {
            $writer.Close()
        }

        Write-ColorOutput "  [REMOVED] Roslynator.Analyzers" $SuccessColor
        return $true
    }
    catch {
        Write-ColorOutput "  [ERROR] Failed to remove Roslynator.Analyzers: $_" $ErrorColor
        return $false
    }
}

function Get-RelativePath {
    param(
        [string]$FullPath,
        [string]$BasePath = (Get-Location).Path
    )

    $fullPathUri = New-Object System.Uri($FullPath)
    $basePathUri = New-Object System.Uri($BasePath + "\")

    return $basePathUri.MakeRelativeUri($fullPathUri).ToString().Replace('/', '\')
}

# Main execution
try {
    Write-ColorOutput "`n========================================" $InfoColor
    Write-ColorOutput "  Roslynator Analyzers Management" $InfoColor
    Write-ColorOutput "========================================" $InfoColor

    if ($RemoveAnalyzers) {
        Write-ColorOutput "`n[MODE] Remove Roslynator.Analyzers from all projects" $WarningColor
    }
    elseif ($CheckOnly) {
        Write-ColorOutput "`n[MODE] Check-only mode (no changes will be made)" $InfoColor
    }
    else {
        Write-ColorOutput "`n[MODE] Add Roslynator.Analyzers v$RoslynatorVersion to all projects" $InfoColor
    }

    # Find all project files
    $projectFiles = Find-ProjectFiles

    # Filter submodules if requested
    $projectFiles = Filter-SubmoduleProjects -ProjectFiles $projectFiles -ExcludeSubmodules $ExcludeSubmodules

    if ($projectFiles.Count -eq 0) {
        Write-ColorOutput "`n[ERROR] No project files found" $ErrorColor
        exit 1
    }

    # Process each project
    $results = @()
    $modifiedCount = 0
    $alreadyHasCount = 0
    $missingCount = 0

    Write-ColorOutput "`n[INFO] Processing projects..." $InfoColor

    foreach ($projectFile in $projectFiles) {
        $relativePath = Get-RelativePath -FullPath $projectFile.FullName
        Write-ColorOutput "`nProject: $relativePath" $InfoColor

        $hasAnalyzers = Test-HasRoslynatorAnalyzers -ProjectFilePath $projectFile.FullName

        if ($RemoveAnalyzers) {
            if ($hasAnalyzers) {
                if (-not $CheckOnly) {
                    $removed = Remove-RoslynatorAnalyzers -ProjectFilePath $projectFile.FullName
                    if ($removed) {
                        $modifiedCount++
                    }
                }
                else {
                    Write-ColorOutput "  [WOULD REMOVE] Roslynator.Analyzers" $WarningColor
                    $modifiedCount++
                }
            }
            else {
                Write-ColorOutput "  [SKIP] No Roslynator.Analyzers to remove" $InfoColor
            }
        }
        else {
            if ($hasAnalyzers) {
                Write-ColorOutput "  [OK] Already has Roslynator.Analyzers" $SuccessColor
                $alreadyHasCount++
            }
            else {
                if (-not $CheckOnly) {
                    $added = Add-RoslynatorAnalyzers -ProjectFilePath $projectFile.FullName -Version $RoslynatorVersion
                    if ($added) {
                        $modifiedCount++
                    }
                }
                else {
                    Write-ColorOutput "  [MISSING] Would add Roslynator.Analyzers" $WarningColor
                    $missingCount++
                }
            }
        }

        $results += [PSCustomObject]@{
            ProjectPath = $relativePath
            FullPath = $projectFile.FullName
            HadAnalyzers = $hasAnalyzers
            Modified = ($modifiedCount -gt 0)
        }
    }

    # Summary
    Write-ColorOutput "`n========================================" $InfoColor
    Write-ColorOutput "  Summary" $InfoColor
    Write-ColorOutput "========================================" $InfoColor
    Write-ColorOutput "Total Projects: $($projectFiles.Count)" $InfoColor

    if ($RemoveAnalyzers) {
        Write-ColorOutput "Removed from: $modifiedCount project(s)" $SuccessColor
    }
    elseif ($CheckOnly) {
        Write-ColorOutput "Already have analyzers: $alreadyHasCount project(s)" $SuccessColor
        Write-ColorOutput "Missing analyzers: $missingCount project(s)" $WarningColor
    }
    else {
        Write-ColorOutput "Already had analyzers: $alreadyHasCount project(s)" $SuccessColor
        Write-ColorOutput "Added to: $modifiedCount project(s)" $SuccessColor
    }

    # Output formats
    if ($OutputFormat -eq "Json" -or $SaveToFile) {
        $jsonOutput = @{
            Timestamp = Get-Date -Format "o"
            RoslynatorVersion = $RoslynatorVersion
            Mode = if ($RemoveAnalyzers) { "Remove" } elseif ($CheckOnly) { "Check" } else { "Add" }
            TotalProjects = $projectFiles.Count
            ModifiedProjects = $modifiedCount
            AlreadyHasAnalyzers = $alreadyHasCount
            MissingAnalyzers = $missingCount
            Projects = $results
        } | ConvertTo-Json -Depth 10

        if ($SaveToFile) {
            $jsonOutput | Out-File -FilePath $SaveToFile -Encoding UTF8
            Write-ColorOutput "`n[INFO] Results saved to: $SaveToFile" $SuccessColor
        }
        else {
            Write-Output $jsonOutput
        }
    }

    # Exit code
    if ($CheckOnly -and $missingCount -gt 0) {
        Write-ColorOutput "`n[WARNING] Some projects are missing Roslynator.Analyzers" $WarningColor
        exit 1
    }
    elseif (-not $CheckOnly -and $modifiedCount -eq 0 -and -not $RemoveAnalyzers -and $alreadyHasCount -eq 0) {
        Write-ColorOutput "`n[ERROR] No projects were modified" $ErrorColor
        exit 1
    }
    else {
        Write-ColorOutput "`n[SUCCESS] Operation completed successfully" $SuccessColor
        exit 0
    }
}
catch {
    Write-ColorOutput "`n[ERROR] Script failed: $_" $ErrorColor
    Write-ColorOutput $_.ScriptStackTrace $ErrorColor
    exit 1
}
