# validate-implementation-step.ps1 - Level 1 validation after implementation steps
# Build + test validation that must complete in < 5 minutes
# Usage: pwsh ./scripts/validate-implementation-step.ps1

param(
    [switch]$Verbose = $false
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Start timing
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "🧪 Level 1: Build + Test Validation (< 5 minutes)" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan

$buildSuccess = $false
$testSuccess = $false

try {
    # Step 1: Build validation
    Write-Host "Step 1/2: Building projects..." -ForegroundColor Yellow
    
    $buildProcess = Start-Process -FilePath "dotnet" -ArgumentList "build --configuration Release --verbosity minimal" -Wait -PassThru -NoNewWindow
    
    if ($buildProcess.ExitCode -ne 0) {
        Write-Host "❌ BUILD FAILED" -ForegroundColor Red
        Write-Host "   ⚠️  Build failed with exit code $($buildProcess.ExitCode)" -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "🔧 REQUIRED ACTION:" -ForegroundColor Yellow
        Write-Host "   Fix build errors before continuing" -ForegroundColor Yellow
        Write-Host "   Run: dotnet build --verbosity normal" -ForegroundColor Yellow
        exit 1
    }
    
    $buildSuccess = $true
    Write-Host "✅ Build completed successfully" -ForegroundColor Green
    
    # Step 2: Test execution
    Write-Host "Step 2/2: Running tests..." -ForegroundColor Yellow
    
    $testArgs = "test --configuration Release --no-build --verbosity minimal"
    if ($Verbose) {
        $testArgs = "test --configuration Release --no-build --verbosity normal"
    }
    
    $testProcess = Start-Process -FilePath "dotnet" -ArgumentList $testArgs -Wait -PassThru -NoNewWindow
    
    if ($testProcess.ExitCode -ne 0) {
        Write-Host "❌ TESTS FAILED" -ForegroundColor Red
        Write-Host "   ⚠️  Tests failed with exit code $($testProcess.ExitCode)" -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "🔧 REQUIRED ACTION:" -ForegroundColor Yellow
        Write-Host "   Fix failing tests before continuing" -ForegroundColor Yellow
        Write-Host "   Run: dotnet test --verbosity normal" -ForegroundColor Yellow
        Write-Host "   For detailed test failure information" -ForegroundColor Yellow
        exit 1
    }
    
    $testSuccess = $true
    $elapsed = $stopwatch.Elapsed.TotalSeconds
    
    # Success summary
    Write-Host "" -ForegroundColor Green
    Write-Host "✅ LEVEL 1 VALIDATION PASSED" -ForegroundColor Green
    Write-Host "   ⏱️  Completed in $([math]::Round($elapsed, 1)) seconds" -ForegroundColor Green
    Write-Host "   🏗️  All projects build successfully" -ForegroundColor Green
    Write-Host "   🧪 All tests pass" -ForegroundColor Green
    Write-Host "" -ForegroundColor Green
    Write-Host "🎯 Ready for next implementation step" -ForegroundColor Green
    
    # Success exit
    exit 0
}
catch {
    $elapsed = $stopwatch.Elapsed.TotalSeconds
    Write-Host "❌ LEVEL 1 VALIDATION ERROR" -ForegroundColor Red
    Write-Host "   💥 Exception: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   ⏱️  Failed after $([math]::Round($elapsed, 1)) seconds" -ForegroundColor Red
    
    if ($buildSuccess -and -not $testSuccess) {
        Write-Host "   📊 Build: ✅ Tests: ❌" -ForegroundColor Red
    }
    elseif (-not $buildSuccess) {
        Write-Host "   📊 Build: ❌ Tests: ⏭️" -ForegroundColor Red
    }
    
    Write-Host "" -ForegroundColor Red
    Write-Host "🔧 REQUIRED ACTION:" -ForegroundColor Yellow
    Write-Host "   Check build and test output for detailed errors" -ForegroundColor Yellow
    Write-Host "   Ensure dotnet is properly configured" -ForegroundColor Yellow
    
    # Error exit
    exit 1
}
finally {
    $stopwatch.Stop()
    
    # Show timing warning if approaching limit
    $elapsed = $stopwatch.Elapsed.TotalSeconds
    if ($elapsed -gt 240) { # 4 minutes = warning
        Write-Host "⚠️  Performance Warning: Validation took $([math]::Round($elapsed, 1))s (approaching 5-minute limit)" -ForegroundColor Yellow
    }
}