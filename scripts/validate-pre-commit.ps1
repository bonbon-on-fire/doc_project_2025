# validate-pre-commit.ps1 - Level 3 validation before commits
# Full validation including integration tests - comprehensive quality gates
# Usage: pwsh ./scripts/validate-pre-commit.ps1

param(
    [switch]$Verbose = $false
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Start timing
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "🛡️  Level 3: Pre-Commit Full Validation" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan

try {
    # Step 1: Run Level 2 quality gates (existing quality-check.ps1)
    Write-Host "Step 1/2: Running comprehensive quality gates..." -ForegroundColor Yellow
    
    $qualityCheckPath = Join-Path $PSScriptRoot "quality-check.ps1"
    
    if (-not (Test-Path $qualityCheckPath)) {
        Write-Host "❌ QUALITY CHECK SCRIPT NOT FOUND" -ForegroundColor Red
        Write-Host "   📁 Expected at: $qualityCheckPath" -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "🔧 REQUIRED ACTION:" -ForegroundColor Yellow
        Write-Host "   Ensure quality-check.ps1 exists in scripts/ directory" -ForegroundColor Yellow
        exit 1
    }
    
    # Execute quality check script
    $qualityArgs = @()
    if ($Verbose) {
        $qualityArgs += "-Verbose"
    }
    
    Write-Host "   🔍 Executing quality-check.ps1..." -ForegroundColor Gray
    
    & $qualityCheckPath @qualityArgs
    $qualityExitCode = $LASTEXITCODE
    
    if ($qualityExitCode -ne 0) {
        Write-Host "❌ QUALITY GATES FAILED" -ForegroundColor Red
        Write-Host "   ⚠️  quality-check.ps1 failed with exit code $qualityExitCode" -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "🔧 REQUIRED ACTION:" -ForegroundColor Yellow
        Write-Host "   Fix all quality gate failures before committing" -ForegroundColor Yellow
        Write-Host "   Review quality-check.ps1 output for details" -ForegroundColor Yellow
        Write-Host "   All issues must be resolved to proceed" -ForegroundColor Yellow
        exit 1
    }
    
    Write-Host "✅ Quality gates passed" -ForegroundColor Green
    
    # Step 2: Run integration tests (if they exist)
    Write-Host "Step 2/2: Running integration tests..." -ForegroundColor Yellow
    
    # Check for integration test projects
    $integrationTestProjects = Get-ChildItem -Path "." -Name "*Integration*Tests*.csproj" -Recurse
    $e2eTestProjects = Get-ChildItem -Path "." -Name "*E2E*Tests*.csproj" -Recurse
    
    $hasIntegrationTests = ($integrationTestProjects.Count -gt 0) -or ($e2eTestProjects.Count -gt 0)
    
    if ($hasIntegrationTests) {
        Write-Host "   🧪 Found integration test projects, executing..." -ForegroundColor Gray
        
        $integrationTestArgs = "test --configuration Release --no-build --filter Category=Integration --verbosity minimal"
        if ($Verbose) {
            $integrationTestArgs = "test --configuration Release --no-build --filter Category=Integration --verbosity normal"
        }
        
        $integrationProcess = Start-Process -FilePath "dotnet" -ArgumentList $integrationTestArgs -Wait -PassThru -NoNewWindow
        
        if ($integrationProcess.ExitCode -ne 0) {
            Write-Host "❌ INTEGRATION TESTS FAILED" -ForegroundColor Red
            Write-Host "   ⚠️  Integration tests failed with exit code $($integrationProcess.ExitCode)" -ForegroundColor Red
            Write-Host "" -ForegroundColor Red
            Write-Host "🔧 REQUIRED ACTION:" -ForegroundColor Yellow
            Write-Host "   Fix failing integration tests before committing" -ForegroundColor Yellow
            Write-Host "   Run: dotnet test --filter Category=Integration --verbosity normal" -ForegroundColor Yellow
            exit 1
        }
        
        Write-Host "✅ Integration tests passed" -ForegroundColor Green
    }
    else {
        Write-Host "   ⏭️  No integration test projects found, skipping" -ForegroundColor Gray
    }
    
    $elapsed = $stopwatch.Elapsed.TotalMinutes
    
    # Success summary
    Write-Host "" -ForegroundColor Green
    Write-Host "🎉 LEVEL 3 VALIDATION PASSED - READY TO COMMIT" -ForegroundColor Green
    Write-Host "================================================" -ForegroundColor Green
    Write-Host "   ⏱️  Completed in $([math]::Round($elapsed, 1)) minutes" -ForegroundColor Green
    Write-Host "   🔨 All builds successful" -ForegroundColor Green
    Write-Host "   🧪 All tests pass" -ForegroundColor Green
    Write-Host "   ✨ Code quality validated" -ForegroundColor Green
    Write-Host "   🔒 Security checks passed" -ForegroundColor Green
    
    if ($hasIntegrationTests) {
        Write-Host "   🌐 Integration tests passed" -ForegroundColor Green
    }
    
    Write-Host "" -ForegroundColor Green
    Write-Host "✅ COMMIT APPROVED - All quality gates satisfied" -ForegroundColor Green
    
    # Success exit
    exit 0
}
catch {
    $elapsed = $stopwatch.Elapsed.TotalMinutes
    Write-Host "❌ LEVEL 3 VALIDATION ERROR" -ForegroundColor Red
    Write-Host "   💥 Exception: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   ⏱️  Failed after $([math]::Round($elapsed, 1)) minutes" -ForegroundColor Red
    Write-Host "" -ForegroundColor Red
    Write-Host "🔧 REQUIRED ACTION:" -ForegroundColor Yellow
    Write-Host "   Review error details and fix all issues" -ForegroundColor Yellow
    Write-Host "   Re-run validation before attempting to commit" -ForegroundColor Yellow
    Write-Host "" -ForegroundColor Red
    Write-Host "🚨 COMMIT BLOCKED - Must pass all quality gates" -ForegroundColor Red
    
    # Error exit
    exit 1
}
finally {
    $stopwatch.Stop()
}

# Additional commit preparation steps
Write-Host "" -ForegroundColor Cyan
Write-Host "📋 COMMIT PREPARATION CHECKLIST:" -ForegroundColor Cyan
Write-Host "   📝 Run git status to review changes" -ForegroundColor Gray
Write-Host "   📝 Write meaningful commit message" -ForegroundColor Gray
Write-Host "   📝 Consider if this is appropriate for the current branch" -ForegroundColor Gray
Write-Host "   📝 Ensure all related changes are staged" -ForegroundColor Gray