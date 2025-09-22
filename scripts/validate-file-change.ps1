# validate-file-change.ps1 - Level 0 validation after file changes
# Quick build check that must complete in < 30 seconds
# Usage: pwsh ./scripts/validate-file-change.ps1

param(
    [switch]$Verbose = $false
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Start timing
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "🚀 Level 0: Quick Build Validation (< 30 seconds)" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan

try {
    # Quick build check - minimal verbosity for speed
    Write-Host "Performing quick build check..." -ForegroundColor Yellow
    
    $buildProcess = Start-Process -FilePath "dotnet" -ArgumentList "build --verbosity minimal --no-restore" -Wait -PassThru -NoNewWindow
    
    $elapsed = $stopwatch.Elapsed.TotalSeconds
    
    if ($buildProcess.ExitCode -eq 0) {
        Write-Host "✅ LEVEL 0 VALIDATION PASSED" -ForegroundColor Green
        Write-Host "   ⏱️  Completed in $([math]::Round($elapsed, 1)) seconds" -ForegroundColor Green
        Write-Host "   📁 All projects build successfully" -ForegroundColor Green
        
        # Success exit
        exit 0
    }
    else {
        Write-Host "❌ LEVEL 0 VALIDATION FAILED" -ForegroundColor Red
        Write-Host "   ⚠️  Build failed with exit code $($buildProcess.ExitCode)" -ForegroundColor Red
        Write-Host "   ⏱️  Failed after $([math]::Round($elapsed, 1)) seconds" -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "🔧 REQUIRED ACTION:" -ForegroundColor Yellow
        Write-Host "   Fix compilation errors before continuing" -ForegroundColor Yellow
        Write-Host "   Run: dotnet build --verbosity normal" -ForegroundColor Yellow
        Write-Host "   For detailed error information" -ForegroundColor Yellow
        
        # Failure exit
        exit 1
    }
}
catch {
    $elapsed = $stopwatch.Elapsed.TotalSeconds
    Write-Host "❌ LEVEL 0 VALIDATION ERROR" -ForegroundColor Red
    Write-Host "   💥 Exception: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   ⏱️  Failed after $([math]::Round($elapsed, 1)) seconds" -ForegroundColor Red
    Write-Host "" -ForegroundColor Red
    Write-Host "🔧 REQUIRED ACTION:" -ForegroundColor Yellow
    Write-Host "   Check that dotnet is installed and accessible" -ForegroundColor Yellow
    Write-Host "   Verify you are in the correct directory" -ForegroundColor Yellow
    
    # Error exit
    exit 1
}
finally {
    $stopwatch.Stop()
}