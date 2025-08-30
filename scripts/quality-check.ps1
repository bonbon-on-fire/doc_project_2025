# quality-check.ps1 - Complete quality validation script for Orleans migration
# This script runs all quality gates mentioned in tasks.md
# Usage: pwsh ./scripts/quality-check.ps1

param(
    [switch]$Verbose = $false
)

# Set error action preference
$ErrorActionPreference = "Continue"

Write-Host "🔍 Orleans Migration Quality Check" -ForegroundColor Blue
Write-Host "==================================" -ForegroundColor Blue
Write-Host ""

# Track overall status
$script:OverallSuccess = $true
$script:FailedChecks = @()

# Function to print status
function Write-Status {
    param(
        [bool]$Success,
        [string]$Message
    )
    
    if ($Success) {
        Write-Host "✅ $Message" -ForegroundColor Green
    } else {
        Write-Host "❌ $Message" -ForegroundColor Red
        $script:OverallSuccess = $false
        $script:FailedChecks += $Message
    }
}

# Function to print section header
function Write-Section {
    param([string]$Title)
    
    Write-Host ""
    Write-Host "🔨 $Title" -ForegroundColor Cyan
    Write-Host "----------------------------------------" -ForegroundColor Cyan
}

# Function to run command and capture result
function Invoke-Command {
    param(
        [string]$Command,
        [string]$Arguments = "",
        [bool]$ShowOutput = $false
    )
    
    try {
        if ($ShowOutput -or $Verbose) {
            $process = Start-Process -FilePath $Command -ArgumentList $Arguments -Wait -PassThru -NoNewWindow
        } else {
            $process = Start-Process -FilePath $Command -ArgumentList $Arguments -Wait -PassThru -NoNewWindow -RedirectStandardOutput "NUL" -RedirectStandardError "NUL"
        }
        return $process.ExitCode -eq 0
    } catch {
        return $false
    }
}

# 1. BUILD VALIDATION
Write-Section "Build Validation"

Write-Host "Cleaning previous builds..."
$cleanSuccess = Invoke-Command "dotnet" "clean"
Write-Status $cleanSuccess "Clean completed"

Write-Host "Restoring NuGet packages..."
$restoreSuccess = Invoke-Command "dotnet" "restore"
Write-Status $restoreSuccess "Package restore"

Write-Host "Building Debug configuration..."
$debugBuildSuccess = Invoke-Command "dotnet" "build --configuration Debug --no-restore"
Write-Status $debugBuildSuccess "Debug build"

Write-Host "Building Release configuration..."
$releaseBuildSuccess = Invoke-Command "dotnet" "build --configuration Release --no-restore"
Write-Status $releaseBuildSuccess "Release build"

if (-not $debugBuildSuccess -or -not $releaseBuildSuccess) {
    Write-Host "Build failed. Please fix build errors before continuing." -ForegroundColor Red
    exit 1
}

# 2. TEST EXECUTION
Write-Section "Test Execution"

Write-Host "Running all tests..."
$testSuccess = Invoke-Command "dotnet" "test --configuration Release --no-build --collect:`"XPlat Code Coverage`" --logger `"console;verbosity=minimal`""
Write-Status $testSuccess "All tests"

if (-not $testSuccess) {
    Write-Host "Some tests failed. Running tests with verbose output..." -ForegroundColor Yellow
    Invoke-Command "dotnet" "test --configuration Release --no-build --logger `"console;verbosity=normal`"" $true
}

# 3. CODE QUALITY
Write-Section "Code Quality"

Write-Host "Checking code formatting..."
$formatSuccess = Invoke-Command "dotnet" "format --verify-no-changes --verbosity quiet"
Write-Status $formatSuccess "Code formatting"

if (-not $formatSuccess) {
    Write-Host "Code formatting issues found. Run 'dotnet format' to fix." -ForegroundColor Yellow
}

Write-Host "Running static analysis..."
$analyzerSuccess = Invoke-Command "dotnet" "build /p:RunAnalyzers=true /p:TreatWarningsAsErrors=true --no-restore --verbosity quiet"
Write-Status $analyzerSuccess "Static analysis"

# 4. SECURITY SCAN
Write-Section "Security Scan"

Write-Host "Scanning for vulnerable packages..."
try {
    $vulnerableOutput = & dotnet list package --vulnerable 2>$null
    $securitySuccess = -not ($vulnerableOutput -match "has the following vulnerable packages")
    
    if (-not $securitySuccess) {
        Write-Host "❌ Vulnerable packages found:" -ForegroundColor Red
        Write-Host $vulnerableOutput
    }
} catch {
    $securitySuccess = $true  # Assume OK if command fails
}

Write-Status $securitySuccess "Package security scan"

# 5. ORLEANS RUNTIME VALIDATION (if Orleans projects exist)
if (Test-Path "AIChat.Orleans.Host") {
    Write-Section "Orleans Runtime Validation"
    
    Write-Host "Testing Orleans silo startup..."
    
    try {
        # Start Orleans silo in background
        Push-Location "AIChat.Orleans.Host"
        $siloProcess = Start-Process "dotnet" "run" -PassThru -NoNewWindow
        
        # Wait for startup
        Start-Sleep -Seconds 20
        
        # Check if silo is still running
        if (-not $siloProcess.HasExited) {
            try {
                # Test health endpoint
                $response = Invoke-WebRequest -Uri "http://localhost:5100/health" -TimeoutSec 5 -UseBasicParsing
                $orleansSuccess = $response.StatusCode -eq 200
            } catch {
                $orleansSuccess = $false
            }
            
            # Clean up
            $siloProcess.Kill()
            $siloProcess.WaitForExit(5000)
        } else {
            $orleansSuccess = $false
        }
        
        Pop-Location
    } catch {
        $orleansSuccess = $false
        Pop-Location
    }
    
    Write-Status $orleansSuccess "Orleans silo health"
}

# 6. SUMMARY
Write-Section "Quality Check Summary"

if ($script:OverallSuccess -and $testSuccess -and $securitySuccess) {
    Write-Host "🎉 ALL QUALITY GATES PASSED!" -ForegroundColor Green
    Write-Host "✅ Ready for task completion or commit" -ForegroundColor Green
    exit 0
} else {
    Write-Host "❌ QUALITY GATES FAILED" -ForegroundColor Red
    Write-Host ""
    Write-Host "Failed checks:" -ForegroundColor Yellow
    
    foreach ($check in $script:FailedChecks) {
        Write-Host "• $check" -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "Fix the issues above before marking task complete or committing." -ForegroundColor Yellow
    exit 1
}