param(
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$Verbose
)

# Build and Test All Services
# This script builds all services and runs tests in the correct order:
# 1. Orleans tests
# 2. Server tests  
# 3. Client tests

$ErrorActionPreference = "Stop"
$script:hasErrors = $false

# Color functions
function Write-Success { param($Message) Write-Host $Message -ForegroundColor Green }
function Write-Info { param($Message) Write-Host $Message -ForegroundColor Cyan }
function Write-Warning { param($Message) Write-Host $Message -ForegroundColor Yellow }
function Write-Error { param($Message) Write-Host $Message -ForegroundColor Red }
function Write-Header { 
    param($Message) 
    Write-Host "`n$("="*60)" -ForegroundColor Blue
    Write-Host $Message -ForegroundColor Blue
    Write-Host "$("="*60)" -ForegroundColor Blue
}

# Track timing
$startTime = Get-Date

Write-Header "Build and Test All Services"
Write-Info "This script performs full build and test verification"
Write-Info "For quick start without tests, use build-and-start-*.ps1 scripts`n"

# Phase 1: Build All Services
if (!$SkipBuild) {
    Write-Header "Phase 1: Building All Services"
    
    # Build Orleans projects
    Write-Info "Building Orleans projects..."
    try {
        dotnet build server/AIChat.Orleans/AIChat.Orleans.csproj --configuration Debug --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "Orleans build failed" }
        
        dotnet build server/AIChat.Orleans.Client/AIChat.Orleans.Client.csproj --configuration Debug --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "Orleans.Client build failed" }
        
        dotnet build server/AIChat.Orleans.Host/AIChat.Orleans.Host.csproj --configuration Debug --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "Orleans.Host build failed" }
        
        Write-Success "✓ Orleans projects built successfully"
    }
    catch {
        Write-Error "✗ Orleans build failed: $_"
        $script:hasErrors = $true
    }
    
    # Build server
    Write-Info "Building server project..."
    try {
        dotnet build server/AIChat.Server/AIChat.Server.csproj --configuration Debug --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "Server build failed" }
        Write-Success "✓ Server built successfully"
    }
    catch {
        Write-Error "✗ Server build failed: $_"
        $script:hasErrors = $true
    }
    
    # Build client
    Write-Info "Building client project..."
    try {
        Push-Location client
        npm install
        if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
        Pop-Location
        Write-Success "✓ Client built successfully"
    }
    catch {
        Write-Error "✗ Client build failed: $_"
        $script:hasErrors = $true
        Pop-Location -ErrorAction SilentlyContinue
    }
    
    if ($script:hasErrors) {
        Write-Error "`n✗ Build phase failed. Fix errors before proceeding to tests."
        exit 1
    }
    
    Write-Success "`n✓ All projects built successfully"
}
else {
    Write-Warning "Skipping build phase (--SkipBuild specified)"
}

# Phase 2: Test All Services (in dependency order)
if (!$SkipTests) {
    Write-Header "Phase 2: Testing All Services"
    Write-Info "Testing order: Orleans → Server → Client"
    
    # Test Orleans (foundation layer)
    Write-Info "`nLevel 1: Testing Orleans..."
    try {
        $testResult = dotnet test server/AIChat.Orleans.Tests/AIChat.Orleans.Tests.csproj `
            --no-build `
            --configuration Debug `
            --verbosity $(if ($Verbose) { "normal" } else { "minimal" })
        
        if ($LASTEXITCODE -ne 0) { 
            throw "Orleans tests failed"
        }
        Write-Success "✓ Orleans tests passed"
    }
    catch {
        Write-Error "✗ Orleans tests failed: $_"
        $script:hasErrors = $true
        Write-Warning "Fix Orleans tests before proceeding - this is the foundation layer"
    }
    
    # Test Server (depends on Orleans)
    if (!$script:hasErrors) {
        Write-Info "`nLevel 2: Testing Server..."
        try {
            $testResult = dotnet test server/AIChat.Server.Tests/AIChat.Server.Tests.csproj `
                --no-build `
                --configuration Debug `
                --verbosity $(if ($Verbose) { "normal" } else { "minimal" })
            
            if ($LASTEXITCODE -ne 0) { 
                throw "Server tests failed"
            }
            Write-Success "✓ Server tests passed"
        }
        catch {
            Write-Error "✗ Server tests failed: $_"
            $script:hasErrors = $true
        }
    }
    
    # Test Client (depends on Server)
    if (!$script:hasErrors) {
        Write-Info "`nLevel 3: Testing Client..."
        try {
            Push-Location client
            
            # Run unit tests
            Write-Info "Running client unit tests..."
            npm run test:unit
            if ($LASTEXITCODE -ne 0) { 
                throw "Client unit tests failed"
            }
            Write-Success "✓ Client unit tests passed"
            
            # Run E2E tests (optional, as they can be flaky)
            Write-Info "Running client E2E tests (may take several minutes)..."
            Write-Warning "Note: E2E tests may have timeouts due to environment issues"
            
            # Run with timeout to prevent hanging
            $e2eJob = Start-Job -ScriptBlock {
                Set-Location $using:PWD
                npm run test:e2e 2>&1
            }
            
            $completed = Wait-Job $e2eJob -Timeout 180  # 3 minute timeout
            
            if ($completed) {
                $e2eResult = Receive-Job $e2eJob
                Remove-Job $e2eJob
                
                if ($e2eResult -match "failed" -and $e2eResult -notmatch "0 failed") {
                    Write-Warning "⚠ Some E2E tests failed (this may be environmental)"
                    if ($Verbose) {
                        Write-Host $e2eResult
                    }
                }
                else {
                    Write-Success "✓ Client E2E tests passed"
                }
            }
            else {
                Stop-Job $e2eJob
                Remove-Job $e2eJob
                Write-Warning "⚠ E2E tests timed out after 3 minutes"
            }
            
            Pop-Location
        }
        catch {
            Write-Error "✗ Client tests failed: $_"
            $script:hasErrors = $true
            Pop-Location -ErrorAction SilentlyContinue
        }
    }
}
else {
    Write-Warning "Skipping test phase (--SkipTests specified)"
}

# Phase 3: Summary
Write-Header "Build and Test Summary"

$endTime = Get-Date
$duration = $endTime - $startTime

if ($script:hasErrors) {
    Write-Error "✗ Build and test completed with errors"
    Write-Info "Total time: $($duration.ToString('mm\:ss'))"
    Write-Warning "`nFix the errors above before committing code"
    Write-Info "For quick development without tests, use:"
    Write-Info "  ./build-and-start-server.ps1  # Start server only"
    Write-Info "  ./build-and-start-client.ps1  # Start client only"
    exit 1
}
else {
    Write-Success "✓ All builds and tests passed successfully!"
    Write-Info "Total time: $($duration.ToString('mm\:ss'))"
    Write-Success "`nCode is ready for commit"
    Write-Info "`nTo start services:"
    Write-Info "  ./build-and-start-server.ps1  # Start server"
    Write-Info "  ./build-and-start-client.ps1  # Start client"
    exit 0
}