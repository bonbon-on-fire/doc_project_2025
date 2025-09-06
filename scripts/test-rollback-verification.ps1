# Orleans Phase 1 Rollback Verification Tests
# This script verifies that the rollback was successful and the system is functioning correctly

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ServerUrl = "http://localhost:5000",
    
    [Parameter(Mandatory = $false)]
    [int]$TimeoutSeconds = 30,
    
    [Parameter(Mandatory = $false)]
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Continue" # Continue on errors to run all tests
$TestResults = @()
$StartTime = Get-Date

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "   Orleans Phase 1 Rollback Verification" -ForegroundColor Cyan
Write-Host "   Server: $ServerUrl" -ForegroundColor Cyan
Write-Host "   Started: $($StartTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan

function Test-Endpoint {
    param(
        [string]$TestName,
        [string]$Url,
        [string]$Method = "GET",
        [hashtable]$Headers = @{},
        [object]$Body = $null,
        [string]$ExpectedStatus = "200",
        [string]$ExpectedContent = $null,
        [string]$UnexpectedContent = $null
    )
    
    $result = @{
        TestName = $TestName
        Url = $Url
        Success = $false
        Status = $null
        ResponseTime = $null
        Error = $null
        Details = @()
    }
    
    try {
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        
        $requestParams = @{
            Uri = $Url
            Method = $Method
            Headers = $Headers
            TimeoutSec = $TimeoutSeconds
        }
        
        if ($Body) {
            $requestParams.Body = $Body | ConvertTo-Json
            $requestParams.ContentType = "application/json"
        }
        
        $response = Invoke-WebRequest @requestParams
        $stopwatch.Stop()
        
        $result.Status = $response.StatusCode
        $result.ResponseTime = $stopwatch.ElapsedMilliseconds
        
        # Check status code
        if ($response.StatusCode -eq $ExpectedStatus) {
            $result.Details += "✓ Status code $($response.StatusCode) as expected"
        }
        else {
            $result.Details += "✗ Expected status $ExpectedStatus, got $($response.StatusCode)"
            throw "Unexpected status code"
        }
        
        # Check expected content
        if ($ExpectedContent) {
            if ($response.Content -match $ExpectedContent) {
                $result.Details += "✓ Found expected content: $ExpectedContent"
            }
            else {
                $result.Details += "✗ Expected content not found: $ExpectedContent"
                throw "Expected content not found"
            }
        }
        
        # Check unexpected content (Orleans references)
        if ($UnexpectedContent) {
            if ($response.Content -match $UnexpectedContent) {
                $result.Details += "✗ Found unexpected content: $UnexpectedContent"
                throw "Unexpected content found - Orleans may still be active"
            }
            else {
                $result.Details += "✓ No unexpected content found: $UnexpectedContent"
            }
        }
        
        $result.Success = $true
    }
    catch {
        $result.Error = $_.Exception.Message
        $result.Details += "✗ Error: $($_.Exception.Message)"
    }
    
    # Display result
    $statusColor = if ($result.Success) { "Green" } else { "Red" }
    $statusSymbol = if ($result.Success) { "✓" } else { "✗" }
    
    Write-Host "`n$statusSymbol $TestName" -ForegroundColor $statusColor
    Write-Host "    URL: $Url" -ForegroundColor Gray
    
    if ($result.ResponseTime) {
        Write-Host "    Response Time: $($result.ResponseTime)ms" -ForegroundColor Gray
    }
    
    if ($Verbose -or -not $result.Success) {
        foreach ($detail in $result.Details) {
            Write-Host "    $detail" -ForegroundColor Gray
        }
    }
    
    return $result
}

Write-Host "`n🔍 Running rollback verification tests..." -ForegroundColor Yellow

# Test 1: Basic health check
$TestResults += Test-Endpoint -TestName "Basic Health Check" -Url "$ServerUrl/api/health"

# Test 2: Detailed health check (should not contain Orleans)
$TestResults += Test-Endpoint -TestName "Detailed Health Check (No Orleans)" `
    -Url "$ServerUrl/api/health/detailed" `
    -UnexpectedContent "orleans"

# Test 3: Orleans-specific endpoints should be gone or return 404
try {
    $orleansTest = Test-Endpoint -TestName "Orleans Metrics Endpoint (Should Fail)" `
        -Url "$ServerUrl/api/orleans/metrics" `
        -ExpectedStatus "404"
    $TestResults += $orleansTest
}
catch {
    # This is expected to fail, which is good
    $TestResults += @{
        TestName = "Orleans Metrics Endpoint (Should Fail)"
        Success = $true
        Details = @("✓ Orleans metrics endpoint properly unavailable")
    }
}

# Test 4: SSE endpoint should still work
$TestResults += Test-Endpoint -TestName "SSE Endpoint Available" -Url "$ServerUrl/api/chat-sse"

# Test 5: Main API endpoints still work
$TestResults += Test-Endpoint -TestName "Chat API Available" `
    -Url "$ServerUrl/api/chat" `
    -Method "GET"

# Test 6: SignalR hub should still be available
$TestResults += Test-Endpoint -TestName "SignalR Hub Available" `
    -Url "$ServerUrl/api/chat-hub" `
    -ExpectedStatus "400" # SignalR returns 400 for non-WebSocket requests

# Test 7: Configuration verification
try {
    $configTest = Test-Endpoint -TestName "Feature Flags Check" `
        -Url "$ServerUrl/api/health/detailed"
        
    if ($configTest.Success) {
        # Parse the health check response to verify Orleans is disabled
        $healthResponse = Invoke-RestMethod -Uri "$ServerUrl/api/health/detailed" -TimeoutSec $TimeoutSeconds
        $orleansHealthFound = $false
        
        if ($healthResponse.Checks) {
            foreach ($checkName in $healthResponse.Checks.PSObject.Properties.Name) {
                if ($checkName -like "*orleans*") {
                    $orleansHealthFound = $true
                    break
                }
            }
        }
        
        if ($orleansHealthFound) {
            $configTest.Success = $false
            $configTest.Details += "✗ Orleans health checks still present"
        }
        else {
            $configTest.Details += "✓ No Orleans health checks found"
        }
    }
    
    $TestResults += $configTest
}
catch {
    $TestResults += @{
        TestName = "Feature Flags Check"
        Success = $false
        Error = $_.Exception.Message
        Details = @("✗ Could not verify feature flag configuration")
    }
}

# Test 8: Performance baseline check
Write-Host "`n⚡ Performance baseline verification..." -ForegroundColor Yellow

$performanceTests = @(
    @{ Name = "Health Check Performance"; Url = "$ServerUrl/api/health"; MaxMs = 1000 },
    @{ Name = "SSE Endpoint Performance"; Url = "$ServerUrl/api/chat-sse"; MaxMs = 2000 }
)

foreach ($perfTest in $performanceTests) {
    try {
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $response = Invoke-WebRequest -Uri $perfTest.Url -TimeoutSec $TimeoutSeconds
        $stopwatch.Stop()
        
        $responseTime = $stopwatch.ElapsedMilliseconds
        $success = $responseTime -lt $perfTest.MaxMs
        
        $TestResults += @{
            TestName = $perfTest.Name
            Success = $success
            ResponseTime = $responseTime
            Details = @(
                if ($success) { "✓ Response time $($responseTime)ms (< $($perfTest.MaxMs)ms)" }
                else { "✗ Response time $($responseTime)ms (> $($perfTest.MaxMs)ms)" }
            )
        }
        
        $color = if ($success) { "Green" } else { "Red" }
        $symbol = if ($success) { "✓" } else { "✗" }
        Write-Host "$symbol $($perfTest.Name): $($responseTime)ms" -ForegroundColor $color
    }
    catch {
        $TestResults += @{
            TestName = $perfTest.Name
            Success = $false
            Error = $_.Exception.Message
        }
        Write-Host "✗ $($perfTest.Name): Failed" -ForegroundColor Red
    }
}

# Generate summary
$totalTests = $TestResults.Count
$passedTests = ($TestResults | Where-Object { $_.Success }).Count
$failedTests = $totalTests - $passedTests
$endTime = Get-Date
$duration = $endTime - $StartTime

Write-Host "`n=======================================================" -ForegroundColor Cyan
Write-Host "   Rollback Verification Results" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan

Write-Host "Total Tests: $totalTests" -ForegroundColor White
Write-Host "Passed: $passedTests" -ForegroundColor Green
Write-Host "Failed: $failedTests" -ForegroundColor $(if ($failedTests -eq 0) { "Green" } else { "Red" })
Write-Host "Duration: $($duration.ToString('mm\:ss'))" -ForegroundColor White

if ($failedTests -gt 0) {
    Write-Host "`nFailed Tests:" -ForegroundColor Red
    foreach ($test in ($TestResults | Where-Object { -not $_.Success })) {
        Write-Host "  • $($test.TestName)" -ForegroundColor Red
        if ($test.Error) {
            Write-Host "    Error: $($test.Error)" -ForegroundColor Gray
        }
    }
}

# Overall status
$overallSuccess = $failedTests -eq 0

if ($overallSuccess) {
    Write-Host "`n🎉 ROLLBACK VERIFICATION PASSED" -ForegroundColor Green
    Write-Host "Orleans Phase 1 has been successfully rolled back." -ForegroundColor Green
    Write-Host "The system is operating in SSE-only mode." -ForegroundColor Green
    
    Write-Host "`nNext Steps:" -ForegroundColor Cyan
    Write-Host "1. Monitor application logs for any residual issues" -ForegroundColor White
    Write-Host "2. Run full test suite: dotnet test" -ForegroundColor White
    Write-Host "3. Verify user functionality works correctly" -ForegroundColor White
    Write-Host "4. Update monitoring dashboards to reflect rollback" -ForegroundColor White
}
else {
    Write-Host "`n❌ ROLLBACK VERIFICATION FAILED" -ForegroundColor Red
    Write-Host "Some tests failed. Manual intervention may be required." -ForegroundColor Red
    
    Write-Host "`nRecommended Actions:" -ForegroundColor Yellow
    Write-Host "1. Review failed tests above" -ForegroundColor White
    Write-Host "2. Check application logs for errors" -ForegroundColor White
    Write-Host "3. Verify configuration changes were applied" -ForegroundColor White
    Write-Host "4. Consider re-running rollback script" -ForegroundColor White
    Write-Host "5. Contact development team if issues persist" -ForegroundColor White
}

# Save detailed report
$reportPath = "./logs/rollback-verification-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
$report = @{
    Timestamp = $StartTime
    ServerUrl = $ServerUrl
    Duration = $duration
    Summary = @{
        TotalTests = $totalTests
        PassedTests = $passedTests
        FailedTests = $failedTests
        OverallSuccess = $overallSuccess
    }
    TestResults = $TestResults
}

try {
    New-Item -Path "logs" -ItemType Directory -Force | Out-Null
    $report | ConvertTo-Json -Depth 10 | Set-Content $reportPath -Encoding UTF8
    Write-Host "`nDetailed report saved: $reportPath" -ForegroundColor Gray
}
catch {
    Write-Host "Could not save detailed report: $_" -ForegroundColor Yellow
}

exit $(if ($overallSuccess) { 0 } else { 1 })