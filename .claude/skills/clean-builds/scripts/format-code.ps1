# Format Code PowerShell Script for DOC_Project_2025
# Formats the root project using ReSharper CLT, and submodules using CSharpier
#
# Roslynator CLI documentation: https://josefpihrt.github.io/docs/roslynator/cli/commands/fix/
# Note: Roslynator requires the project to be built before running fixes

param(
    [switch]$CheckOnly,    # Just check formatting without making changes
    [switch]$SubmodulesOnly, # Only format submodules
    [switch]$RootOnly,     # Only format root project
    [switch]$Help          # Show help information
)

function Show-Help {
    Write-Host "DOC_Project_2025 Code Formatting Script" -ForegroundColor Green
    Write-Host ""
    Write-Host "USAGE:"
    Write-Host "  .\format-code.ps1              # Format both root and submodules"
    Write-Host "  .\format-code.ps1 -CheckOnly   # Check formatting without changes"
    Write-Host "  .\format-code.ps1 -RootOnly    # Format only root project (multi-tool)"
    Write-Host "  .\format-code.ps1 -SubmodulesOnly # Format only submodules (CSharpier)"
    Write-Host "  .\format-code.ps1 -Help        # Show this help"
    Write-Host ""
    Write-Host "TOOLS USED (Root Project):"
    Write-Host "  1. dotnet format style         # Code style fixes (IDE0032, etc.)"
    Write-Host "  2. Roslynator CLI (optional)   # Advanced code analysis fixes, fixes code styles e.g. using `[]` vs. `Array.Empty<T>()` "
    Write-Host "  3. ReSharper CLT               # Comprehensive formatting"
    Write-Host ""
    Write-Host "TOOLS USED (Submodules):"
    Write-Host "  CSharpier                      # Fast, opinionated formatting"
    Write-Host ""
    Write-Host "PREREQUISITES:"
    Write-Host "  # Required:"
    Write-Host "  dotnet tool install -g JetBrains.ReSharper.GlobalTools"
    Write-Host "  dotnet tool install -g csharpier"
    Write-Host ""
    Write-Host "  # Optional (for advanced code analysis):"
    Write-Host "  dotnet tool install -g Roslynator.DotNet.Cli"
}

function Test-ToolInstalled {
    param($ToolCommand, $ToolName)

    try {
        & $ToolCommand --version > $null 2>&1
        return $true
    }
    catch {
        Write-Host "ERROR: $ToolName is not installed" -ForegroundColor Red
        return $false
    }
}

function Format-RootProject {
    param([bool]$CheckOnly)

    Write-Host "Formatting root project with multiple tools..." -ForegroundColor Yellow

    # Check required tools
    if (-not (Test-ToolInstalled "jb" "ReSharper Command Line Tools")) {
        Write-Host "Please install with: dotnet tool install -g JetBrains.ReSharper.GlobalTools" -ForegroundColor Red
        return $false
    }

    try {
        if ($CheckOnly) {
            Write-Host "Checking root project formatting..."

            # Check dotnet format style issues
            Write-Host "Checking code style issues..."
            dotnet format style --verify-no-changes --verbosity minimal

            # Check Roslynator issues if available
            if (Test-ToolInstalled "roslynator" "Roslynator CLI") {
                Write-Host "Checking Roslynator issues..."
                roslynator analyze server/AIChat.Server/AIChat.Server.csproj --severity-level info --verbosity minimal
            }

            Write-Host "Note: ReSharper CLT doesn't support check-only mode. Use git diff after running format." -ForegroundColor Yellow
            return $true
        }
        else {
            # Step 1: Apply code style fixes (like IDE0032)
            Write-Host "Step 1/4: Applying dotnet format style fixes (IDE0032, etc.)..." -ForegroundColor Cyan
            dotnet format style --diagnostics "IDE0032 IDE0017 IDE0028 IDE0025" --verbosity minimal

            # Step 2: Apply Roslynator fixes (if available)
            if (Test-ToolInstalled "roslynator" "Roslynator CLI") {
                Write-Host "Step 2/4: Applying Roslynator code analysis fixes..." -ForegroundColor Cyan

                # Build projects first (required for Roslynator)
                Write-Host "  - Building projects (required for Roslynator)..."
                dotnet build server/AIChat.Server/AIChat.Server.csproj --verbosity minimal --nologo
                dotnet build server/AIChat.Server.Tests/AIChat.Server.Tests.csproj --verbosity minimal --nologo

                # Fix server project
                Write-Host "  - Fixing server project with Roslynator..."
                roslynator fix server/AIChat.Server/AIChat.Server.csproj --severity-level info --verbosity minimal --ignore-compiler-errors --fix-scope project

                # Fix test project
                Write-Host "  - Fixing test project with Roslynator..."
                roslynator fix server/AIChat.Server.Tests/AIChat.Server.Tests.csproj --severity-level info --verbosity minimal --ignore-compiler-errors --fix-scope project
            }
            else {
                Write-Host "Step 2/4: Roslynator not installed, skipping advanced fixes" -ForegroundColor Yellow
                Write-Host "  Install with: dotnet tool install -g Roslynator.DotNet.Cli" -ForegroundColor Gray
            }

            # Step 3: Format server project with ReSharper
            Write-Host "Step 3/4: Formatting server project with ReSharper CLT..." -ForegroundColor Cyan
            jb cleanupcode server/AIChat.Server/AIChat.Server.csproj

            # Step 4: Format test project with ReSharper
            Write-Host "Step 4/4: Formatting test project with ReSharper CLT..." -ForegroundColor Cyan
            jb cleanupcode server/AIChat.Server.Tests/AIChat.Server.Tests.csproj

            Write-Host "Root project formatting completed!" -ForegroundColor Green
            return $true
        }
    }
    catch {
        Write-Host "Error formatting root project: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Format-Submodules {
    param([bool]$CheckOnly)

    Write-Host "Formatting submodules with CSharpier..." -ForegroundColor Yellow

    if (-not (Test-ToolInstalled "csharpier" "CSharpier")) {
        Write-Host "Please install with: dotnet tool install -g csharpier" -ForegroundColor Red
        return $false
    }

    $submodulesPath = "submodules/LmDotnetTools"

    if (-not (Test-Path $submodulesPath)) {
        Write-Host "Submodules directory not found: $submodulesPath" -ForegroundColor Red
        return $false
    }

    try {
        Push-Location $submodulesPath

        if ($CheckOnly) {
            Write-Host "Checking submodules formatting..."
            csharpier --check .
            if ($LASTEXITCODE -eq 0) {
                Write-Host "Submodules formatting is correct!" -ForegroundColor Green
                return $true
            }
            else {
                Write-Host "Submodules need formatting!" -ForegroundColor Red
                return $false
            }
        }
        else {
            Write-Host "Formatting submodules..."
            csharpier .
            Write-Host "Submodules formatting completed!" -ForegroundColor Green
            return $true
        }
    }
    catch {
        Write-Host "Error formatting submodules: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
    finally {
        Pop-Location
    }
}

# Main script execution
if ($Help) {
    Show-Help
    exit 0
}

Write-Host "DOC_Project_2025 Code Formatting Script" -ForegroundColor Green
Write-Host "=======================================" -ForegroundColor Green

$success = $true

if ($RootOnly) {
    $success = Format-RootProject -CheckOnly $CheckOnly
}
elseif ($SubmodulesOnly) {
    $success = Format-Submodules -CheckOnly $CheckOnly
}
else {
    # Format both
    Write-Host "Formatting both root project and submodules..." -ForegroundColor Cyan

    $rootSuccess = Format-RootProject -CheckOnly $CheckOnly
    $submodulesSuccess = Format-Submodules -CheckOnly $CheckOnly

    $success = $rootSuccess -and $submodulesSuccess
}

Write-Host ""
if ($success) {
    if ($CheckOnly) {
        Write-Host "✓ Formatting check completed successfully!" -ForegroundColor Green
    }
    else {
        Write-Host "✓ Code formatting completed successfully!" -ForegroundColor Green
        Write-Host "Tip: Run 'git diff' to see what was changed." -ForegroundColor Gray

        # Post-formatting validation to ensure formatting didn't break anything
        Write-Host ""
        Write-Host "Running post-formatting validation..." -ForegroundColor Yellow
        $validationScript = "scripts/validate-file-change.ps1"
        if (Test-Path $validationScript) {
            & $validationScript
            if ($LASTEXITCODE -ne 0) {
                Write-Host "❌ Post-formatting validation failed - formatting may have introduced issues" -ForegroundColor Red
                Write-Host "Please review changes and fix any compilation errors" -ForegroundColor Yellow
                exit 1
            }
            else {
                Write-Host "✅ Post-formatting validation passed - code still builds correctly" -ForegroundColor Green
            }
        }
        else {
            Write-Host "⚠️ Validation script not found, skipping post-formatting validation" -ForegroundColor Yellow
        }

        # Check for critical warnings that must be fixed
        Write-Host ""
        Write-Host "Checking for critical code quality warnings..." -ForegroundColor Yellow
        $warningsScript = "scripts/check-critical-warnings.ps1"
        if (Test-Path $warningsScript) {
            & $warningsScript
            if ($LASTEXITCODE -ne 0) {
                Write-Host ""
                Write-Host "❌ CRITICAL WARNINGS DETECTED" -ForegroundColor Red
                Write-Host "=" * 70 -ForegroundColor Red
                Write-Host ""
                Write-Host "The code has critical quality issues that MUST be fixed before checkin." -ForegroundColor Yellow
                Write-Host "These warnings indicate:" -ForegroundColor Yellow
                Write-Host "  • Performance problems (CA1826, CA1859)" -ForegroundColor White
                Write-Host "  • Correctness issues (CA1310, CA1304, CA1305)" -ForegroundColor White
                Write-Host "  • Dead code (IDE0052)" -ForegroundColor White
                Write-Host "  • Modern pattern violations (CA1513)" -ForegroundColor White
                Write-Host ""
                Write-Host "ACTION REQUIRED:" -ForegroundColor Red
                Write-Host "  1. Review the warnings above" -ForegroundColor Cyan
                Write-Host "  2. Fix each warning manually (see HOW TO FIX section)" -ForegroundColor Cyan
                Write-Host "  3. Run this script again to verify all warnings are resolved" -ForegroundColor Cyan
                Write-Host ""
                Write-Host "For detailed information, run:" -ForegroundColor Gray
                Write-Host "  .\scripts\check-critical-warnings.ps1 -Detailed" -ForegroundColor White
                Write-Host ""
                exit 1
            }
            else {
                Write-Host "✅ No critical warnings found - code quality check passed" -ForegroundColor Green
            }
        }
    }
    exit 0
}
else {
    Write-Host "✗ Formatting failed!" -ForegroundColor Red
    exit 1
}
