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
} else {
    if ($Environment -eq "Development" -or $Environment -eq "Production") {
        Write-Host "Note: Orleans may be enabled based on $Environment environment settings" -ForegroundColor Yellow
        Write-Host "To ensure Orleans is disabled, use Test environment or check appsettings.$Environment.json" -ForegroundColor Yellow
    } else {
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
                $pid = ($_ -split '\s+')[-1]
                if ($pid -match '^\d+$') {
                    try {
                        Write-Host "Killing Orleans-related process with PID ${pid}..." -ForegroundColor Red
                        Stop-Process -Id $pid -Force -ErrorAction Stop
                        Write-Host "Successfully killed process ${pid}" -ForegroundColor Green
                    }
                    catch {
                        Write-Warning "Failed to kill process ${pid}: $($_.Exception.Message)"
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

# Ensure logs directory exists
$logsDir = "logs/server"
if (!(Test-Path $logsDir)) {
    Write-Host "Creating logs directory: $logsDir" -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
}

# Clean existing log files
Write-Host "Cleaning existing log files..." -ForegroundColor Yellow
Get-ChildItem -Path $logsDir -Filter "*.log" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $logsDir -Filter "*.jsonl" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

# 2. Build the server project (no tests)
Write-Host "Building server project..." -ForegroundColor Yellow
try {
    # Build and log output to build.log
    dotnet build server/AIChat.Server.csproj --configuration Debug --verbosity minimal 2>&1 | Tee-Object -FilePath "$logsDir/build.log"
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
    Write-Host "Server build completed successfully" -ForegroundColor Green
}
catch {
    Write-Error "Failed to build server: $($_.Exception.Message)"
    return 1
}

# 3. If Orleans is enabled, build and start Orleans Host
$orleansProcess = $null
if ($UseOrleans) {
    Write-Host ""
    Write-Host "Building Orleans Host..." -ForegroundColor Yellow
    try {
        # Build Orleans Host project
        dotnet build AIChat.Orleans.Host/AIChat.Orleans.Host.csproj --configuration Debug --verbosity minimal 2>&1 | Tee-Object -FilePath "$logsDir/orleans-build.log"
        if ($LASTEXITCODE -ne 0) {
            throw "Orleans Host build failed with exit code $LASTEXITCODE"
        }
        Write-Host "Orleans Host build completed successfully" -ForegroundColor Green
        
        # Start Orleans Host in background
        Write-Host "Starting Orleans Host in background..." -ForegroundColor Yellow
        $orleansProcess = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "AIChat.Orleans.Host/AIChat.Orleans.Host.csproj", "--no-build" -WorkingDirectory (Get-Location) -PassThru -NoNewWindow -RedirectStandardOutput "$logsDir/orleans-output.log" -RedirectStandardError "$logsDir/orleans-error.log"
        
        # Wait a moment for Orleans to start
        Write-Host "Waiting for Orleans Silo to initialize..." -ForegroundColor Yellow
        Start-Sleep -Seconds 5
        
        # Check if Orleans Host is running
        if ($orleansProcess.HasExited) {
            Write-Error "Orleans Host failed to start. Check logs/server/orleans-error.log for details"
            return 1
        }
        
        Write-Host "Orleans Host started successfully (PID: $($orleansProcess.Id))" -ForegroundColor Green
        Write-Host "Orleans Dashboard: http://localhost:8081" -ForegroundColor Cyan
    }
    catch {
        Write-Error "Failed to build/start Orleans Host: $($_.Exception.Message)"
        return 1
    }
}

# 4. Start the server
Write-Host "Starting server on http://localhost:$Port..." -ForegroundColor Yellow

# Set environment variables for the server
$env:ASPNETCORE_ENVIRONMENT = $Environment
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:LLM_API_KEY = "DUMMY"

# Set up trap for Ctrl+C to ensure Orleans Host is stopped
$script:orleansProcessGlobal = $orleansProcess
trap {
    if ($script:orleansProcessGlobal -and !$script:orleansProcessGlobal.HasExited) {
        Write-Host "`nStopping Orleans Host..." -ForegroundColor Yellow
        Stop-Process -Id $script:orleansProcessGlobal.Id -Force -ErrorAction SilentlyContinue
    }
    exit
}

# Change to server directory and run the server with logging
Set-Location server
try {
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host "Server Configuration:" -ForegroundColor Cyan
    Write-Host "  Environment: $Environment" -ForegroundColor Cyan
    Write-Host "  Server URL: http://localhost:$Port" -ForegroundColor Cyan
    if ($UseOrleans) {
        Write-Host "  Orleans: ENABLED" -ForegroundColor Green
        Write-Host "  Orleans Dashboard: http://localhost:8081" -ForegroundColor Green
        Write-Host "  Orleans Gateway Port: 30000" -ForegroundColor Cyan
        Write-Host "  Orleans Silo Port: 11111" -ForegroundColor Cyan
    } else {
        if ($Environment -eq "Test") {
            Write-Host "  Orleans: DISABLED (Test environment)" -ForegroundColor Yellow
        } else {
            Write-Host "  Orleans: Check appsettings.$Environment.json for status" -ForegroundColor Yellow
        }
    }
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Build output logged to: ../logs/server/build.log" -ForegroundColor Cyan
    Write-Host "Server runtime logs will be appended to: ../logs/server/build.log" -ForegroundColor Cyan
    Write-Host "Application logs: ../logs/server/app-${Environment}.jsonl" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Press Ctrl+C to stop the server" -ForegroundColor Yellow
    Write-Host ""

    # Run the server and append output to the same log file (after build output)
    dotnet run --project AIChat.Server.csproj --urls "http://localhost:$Port" 2>&1 | Tee-Object -FilePath "../logs/server/build.log" -Append
}
catch {
    Write-Error "Failed to start server: $($_.Exception.Message)"
    return 1
}
finally {
    # Cleanup: Stop Orleans Host if it was started
    if ($orleansProcess -and !$orleansProcess.HasExited) {
        Write-Host ""
        Write-Host "Stopping Orleans Host (PID: $($orleansProcess.Id))..." -ForegroundColor Yellow
        Stop-Process -Id $orleansProcess.Id -Force -ErrorAction SilentlyContinue
        Write-Host "Orleans Host stopped" -ForegroundColor Green
    }
    
    # Return to original directory
    Set-Location ..
}
