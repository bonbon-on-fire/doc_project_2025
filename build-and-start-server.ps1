param(
    [int]$Port = 5099,
    [string]$Environment = "Test"
)

Write-Host "Building and starting server on port $Port with environment $Environment..." -ForegroundColor Green

# 1. Take port that server is going to listen to (defaults to 5099), then search for it, and kill any process that may be
# listening on it.
Write-Host "Checking for processes listening on port $Port..." -ForegroundColor Yellow

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

# 2. Pre-flight validation
Write-Host "Running pre-flight validation..." -ForegroundColor Yellow
try {
    $validationScript = "scripts/validate-implementation-step.ps1"
    if (Test-Path $validationScript) {
        & $validationScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Pre-flight validation failed - cannot start server" -ForegroundColor Red
            Write-Host "Fix all validation issues before starting server" -ForegroundColor Yellow
            return 1
        }
        Write-Host "✅ Pre-flight validation passed" -ForegroundColor Green
    }
    else {
        Write-Host "⚠️ Validation script not found, skipping pre-flight validation" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "❌ Pre-flight validation error: $($_.Exception.Message)" -ForegroundColor Red
    return 1
}

# 3. Build the server project
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

# 4. Start the server
Write-Host "Starting server on http://localhost:$Port..." -ForegroundColor Yellow

# Set environment variables for the server
$env:ASPNETCORE_ENVIRONMENT = $Environment
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:LLM_API_KEY = "DUMMY"

# Change to server directory and run the server with logging
Set-Location server
try {
    Write-Host "Server starting with environment: $Environment" -ForegroundColor Cyan
    Write-Host "Server URL: http://localhost:$Port" -ForegroundColor Cyan
    Write-Host "Build output logged to: ../logs/server/build.log" -ForegroundColor Cyan
    Write-Host "Server runtime logs will be appended to: ../logs/server/build.log" -ForegroundColor Cyan
    Write-Host "Press Ctrl+C to stop the server" -ForegroundColor Yellow

    # Run the server and append output to the same log file (after build output)
    dotnet run --project AIChat.Server.csproj --urls "http://localhost:$Port" 2>&1 | Tee-Object -FilePath "../logs/server/build.log" -Append
}
catch {
    Write-Error "Failed to start server: $($_.Exception.Message)"
    return 1
}
finally {
    # Return to original directory
    Set-Location ..
}
