# ============================================================
# BUILD AND START SERVER (Quick Development Mode)
# ============================================================
# This script ONLY builds and starts the server service.
# It does NOT run any tests.
#
# FOR FULL BUILD AND TEST VERIFICATION, USE:
#   ./build-and-test-all.ps1
# ============================================================

param(
    [int]$Port = 5099,
    [string]$Environment = "Test",
    [switch]$UseOrleans
)

Write-Host "============================================================" -ForegroundColor Yellow
Write-Host " QUICK START: Server Build & Run (No Tests)" -ForegroundColor Yellow
Write-Host " For full build and test verification, use:" -ForegroundColor Yellow
Write-Host " ./build-and-test-all.ps1" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Yellow
Write-Host ""

# Handle Orleans flag - automatically switch to Development environment if UseOrleans is set
if ($UseOrleans) {
    if ($Environment -eq "Test") {
        Write-Host "Orleans requested: Switching from Test to Development environment" -ForegroundColor Magenta
        Write-Host "Note: Orleans is disabled in Test environment by design" -ForegroundColor Yellow
        $Environment = "Development"
    }
    Write-Host "Orleans Integration: ENABLED (via -UseOrleans flag)" -ForegroundColor Cyan
    Write-Host "Environment: $Environment" -ForegroundColor Cyan
    Write-Host "Orleans Dashboard will be available at: http://localhost:8081" -ForegroundColor Cyan
}
else {
    if ($Environment -eq "Development" -or $Environment -eq "Production") {
        Write-Host "Note: Orleans may be enabled based on $Environment environment settings" -ForegroundColor Yellow
        Write-Host "To ensure Orleans is disabled, use Test environment or check appsettings.$Environment.json" -ForegroundColor Yellow
    }
    else {
        Write-Host "Orleans Integration: DISABLED (Test environment)" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Building and starting server on port $Port with environment $Environment..." -ForegroundColor Green

# 1. Take port that server is going to listen to (defaults to 5099), then search for it, and kill any process that may be
# listening on it.
Write-Host "Checking for processes listening on port $Port..." -ForegroundColor Yellow

# If Orleans is enabled, also check Orleans-specific ports
if ($UseOrleans) {
    Write-Host "Checking Orleans ports (30000, 11111, 8081)..." -ForegroundColor Yellow
    $orleansPort = @(30000, 11111, 8081)
    foreach ($oPort in $orleansPort) {
        $orleansProcesses = netstat -ano | findstr ":$oPort" | findstr LISTENING
        if ($orleansProcesses) {
            Write-Host "Found processes on Orleans port ${oPort}:" -ForegroundColor Yellow
            $orleansProcesses | ForEach-Object {
                Write-Host $_ -ForegroundColor Gray
                $processid = ($_ -split '\s+')[-1]
                if ($processid -match '^\d+$') {
                    try {
                        Write-Host "Killing Orleans-related process with PID ${processid}..." -ForegroundColor Red
                        Stop-Process -Id $processid -Force -ErrorAction Stop
                        Write-Host "Successfully killed process ${processid}" -ForegroundColor Green
                    }
                    catch {
                        Write-Warning "Failed to kill process ${processid}: $($_.Exception.Message)"
                    }
                }
            }
        }
    }
}

$processes = netstat -ano | findstr ":$Port" | findstr LISTENING
if ($processes) {
    Write-Host "Found processes listening on port ${Port}:" -ForegroundColor Yellow
    $processes | ForEach-Object {
        Write-Host $_ -ForegroundColor Gray
        # Extract PID (last column)
        $server_pid = ($_ -split '\s+')[-1]
        if ($server_pid -match '^\d+$') {
            try {
                Write-Host "Killing process with PID ${server_pid}..." -ForegroundColor Red
                Stop-Process -Id $server_pid -Force -ErrorAction Stop
                Write-Host "Successfully killed process ${server_pid}" -ForegroundColor Green
            }
            catch {
                Write-Warning "Failed to kill process ${server_pid}: $($_.Exception.Message)"
            }
        }
    }

    # Wait a moment for ports to be released
    Start-Sleep -Seconds 2
}
else {
    Write-Host "No processes found listening on port ${Port}" -ForegroundColor Green
}

# Ensure logs directories exist
$serverLogsDir = "logs/server"
$clientLogsDir = "logs/client"
if (!(Test-Path $serverLogsDir)) {
    Write-Host "Creating server logs directory: $serverLogsDir" -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $serverLogsDir -Force | Out-Null
}
if (!(Test-Path $clientLogsDir)) {
    Write-Host "Creating client logs directory: $clientLogsDir" -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $clientLogsDir -Force | Out-Null
}

# Clean existing log files (both server and client)
Write-Host "Cleaning existing server and client log files..." -ForegroundColor Yellow
Get-ChildItem -Path $serverLogsDir -Filter "*.log" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $serverLogsDir -Filter "*.jsonl" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $clientLogsDir -Filter "*.log" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $clientLogsDir -Filter "*.jsonl" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

# 2. Build the server project (no tests)
Write-Host "Building server project..." -ForegroundColor Yellow
try {
    # Build and log output to build.log
    dotnet build server/AIChat.Server/AIChat.Server.csproj --configuration Debug --verbosity minimal 2>&1 | Tee-Object -FilePath "$serverLogsDir/build.log"
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
    Write-Host "Server build completed successfully" -ForegroundColor Green
}
catch {
    Write-Error "Failed to build server: $($_.Exception.Message)"
    return 1
}

# 3. Orleans Host is now started internally by AIChat.Server for all environments
$orleansProcess = $null
if ($UseOrleans) {
    Write-Host ""
    Write-Host "Orleans will be started internally by AIChat.Server (new architecture)" -ForegroundColor Cyan
    Write-Host "Orleans Host runs in separate task within the same process" -ForegroundColor Green
    Write-Host "This provides true DI container isolation with localhost communication" -ForegroundColor Green
}

# 4. Start the server
Write-Host "Starting server on http://localhost:$Port..." -ForegroundColor Yellow

# Set environment variables for the server
$env:ASPNETCORE_ENVIRONMENT = $Environment
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:LLM_API_KEY = "DUMMY"

# Orleans Host shutdown is now handled internally by AIChat.Server's Console.CancelKeyPress handler

# Change to server directory and run the server with logging
Set-Location server/AIChat.Server
try {
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host "Server Configuration:" -ForegroundColor Cyan
    Write-Host "  Environment: $Environment" -ForegroundColor Cyan
    Write-Host "  Server URL: http://localhost:$Port" -ForegroundColor Cyan
    if ($UseOrleans) {
        Write-Host "  Orleans: ENABLED (Internal Host with isolated DI)" -ForegroundColor Green
        Write-Host "  Orleans Architecture: Separate task/DI, localhost communication" -ForegroundColor Green
        Write-Host "  Orleans Dashboard: http://localhost:8081" -ForegroundColor Green
        Write-Host "  Orleans Gateway Port: 30000" -ForegroundColor Cyan
        Write-Host "  Orleans Silo Port: 11111" -ForegroundColor Cyan
    }
    else {
        if ($Environment -eq "Test") {
            Write-Host "  Orleans: DISABLED (Test environment)" -ForegroundColor Yellow
        }
        else {
            Write-Host "  Orleans: Check appsettings.$Environment.json for status" -ForegroundColor Yellow
        }
    }
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Build output logged to: logs/server/build.log" -ForegroundColor Cyan
    Write-Host "Server runtime logs will be appended to: logs/server/build.log" -ForegroundColor Cyan
    Write-Host "Application logs: logs/server/app-${Environment}.jsonl" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Press Ctrl+C to stop the server" -ForegroundColor Yellow
    Write-Host ""

    # Run the server and append output to the same log file (after build output)
    # Using launch profiles for proper environment configuration
    if ($UseOrleans) {
        # Use DEV profile when Orleans is enabled (Orleans requires Development environment)
        dotnet run --project AIChat.Server.csproj --launch-profile DEV --urls "http://localhost:$Port" 2>&1 | Tee-Object -FilePath "../../logs/server/build.log" -Append
    }
    else {
        # Use TEST profile by default (or specify based on Environment parameter)
        $profile = if ($Environment -eq "Development") { "DEV" } else { "TEST" }
        dotnet run --project AIChat.Server.csproj --launch-profile $profile --urls "http://localhost:$Port" 2>&1 | Tee-Object -FilePath "../../logs/server/build.log" -Append
    }
}
catch {
    Write-Error "Failed to start server: $($_.Exception.Message)"
    return 1
}
finally {
    # Orleans Host cleanup is now handled internally by AIChat.Server
    # No external process management needed

    # Return to original directory (project root)
    Set-Location ../..
}
