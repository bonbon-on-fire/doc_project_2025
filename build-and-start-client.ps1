# ============================================================
# BUILD AND START CLIENT (Quick Development Mode)
# ============================================================
# This script ONLY builds and starts the client service.
# It does NOT run any tests.
#
# FOR FULL BUILD AND TEST VERIFICATION, USE:
#   ./build-and-test-all.ps1
# ============================================================

param(
    [int]$Port = 5173,
    [string]$Environment = "Test"
)

Write-Host "============================================================" -ForegroundColor Yellow
Write-Host " QUICK START: Client Build & Run (No Tests)" -ForegroundColor Yellow
Write-Host " For full build and test verification, use:" -ForegroundColor Yellow
Write-Host " ./build-and-test-all.ps1" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Yellow
Write-Host ""
Write-Host "Building and starting client on port $Port with environment $Environment..." -ForegroundColor Green

# 1. Take port that client is going to listen to (defaults to 5173), then search for it, and kill any process that may be
# listening on it.
Write-Host "Checking for processes listening on port $Port..." -ForegroundColor Yellow

$processes = netstat -ano | findstr ":$Port" | findstr LISTENING
if ($processes) {
    Write-Host "Found processes listening on port ${Port}:" -ForegroundColor Yellow
    $processes | ForEach-Object {
        Write-Host $_ -ForegroundColor Gray
        # Extract PID (last column)
        $client_pid = ($_ -split '\s+')[-1]
        if ($client_pid -match '^\d+$') {
            try {
                Write-Host "Killing process with PID ${client_pid}..." -ForegroundColor Red
                Stop-Process -Id $client_pid -Force -ErrorAction Stop
                Write-Host "Successfully killed process ${client_pid}" -ForegroundColor Green
            }
            catch {
                Write-Warning "Failed to kill process ${client_pid}: $($_.Exception.Message)"
            }
        }
    }

    # Wait a moment for ports to be released
    Start-Sleep -Seconds 2
}
else {
    Write-Host "No processes found listening on port ${Port}" -ForegroundColor Green
}

# Ensure logs directory exists
$logsDir = "logs/client"
if (!(Test-Path $logsDir)) {
    Write-Host "Creating logs directory: $logsDir" -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
}

# Clean existing log files
Write-Host "Cleaning existing log files..." -ForegroundColor Yellow
Get-ChildItem -Path $logsDir -Filter "*.log" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $logsDir -Filter "*.jsonl" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

$fail = $false

# 2. Build client only (no tests)
Write-Host "Building client project..." -ForegroundColor Yellow

if (!$fail) {
    # 3. Install dependencies and build the client project
    Write-Host "Installing client dependencies..." -ForegroundColor Yellow
    try {
        # Install npm dependencies and log output to build.log
        Set-Location client
        npm install 2>&1 | Tee-Object -FilePath "../$logsDir/build.log"
        if ($LASTEXITCODE -ne 0) {
            throw "npm install failed with exit code $LASTEXITCODE"
        }

        Write-Host "Client dependencies installed successfully" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to install client dependencies: $($_.Exception.Message)"
        Set-Location ..
        $fail = $true
    }
}

if (!$fail) {
    # 4. Start the client
    Write-Host "Starting client on http://localhost:$Port..." -ForegroundColor Yellow

    # Determine which npm script to run based on environment
    $npmScript = if ($Environment -eq "Test") { "dev:test" } else { "dev" }

    # We're already in the client directory from the previous step
    try {
        Write-Host "Client starting with environment: $Environment" -ForegroundColor Cyan
        Write-Host "Client URL: http://localhost:$Port" -ForegroundColor Cyan
        Write-Host "NPM script: $npmScript" -ForegroundColor Cyan
        Write-Host "Dependencies logged to: ../$logsDir/build.log" -ForegroundColor Cyan
        Write-Host "Client runtime logs will be appended to: ../$logsDir/build.log" -ForegroundColor Cyan
        Write-Host "Press Ctrl+C to stop the client" -ForegroundColor Yellow

        # Run the client and append output to the same log file (after npm install output)
        npm run $npmScript 2>&1 | Tee-Object -FilePath "../$logsDir/build.log" -Append
    }
    catch {
        Write-Error "Failed to start client: $($_.Exception.Message)"
    }
    finally {
        # Return to original directory
        Set-Location ..
    }
}

