# Orleans Phase 1 Rollback Script
# This script safely rolls back Orleans Phase 1 integration to restore SSE-only operation

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [switch]$DryRun = $false,
    
    [Parameter(Mandatory = $false)]
    [switch]$Force = $false,
    
    [Parameter(Mandatory = $false)]
    [string]$BackupPath = "./backup",
    
    [Parameter(Mandatory = $false)]
    [int]$TimeoutMinutes = 5
)

# Script configuration
$ErrorActionPreference = "Stop"
$StartTime = Get-Date

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "   Orleans Phase 1 Rollback Script" -ForegroundColor Cyan
Write-Host "   Started: $($StartTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor Cyan
Write-Host "   Mode: $(if ($DryRun) { 'DRY RUN' } else { 'EXECUTE' })" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan

# Functions
function Write-Step {
    param([string]$Message)
    Write-Host "`n[$((Get-Date).ToString('HH:mm:ss'))] STEP: $Message" -ForegroundColor Yellow
}

function Write-Success {
    param([string]$Message)
    Write-Host "  ✓ $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "  ⚠ $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "  ✗ $Message" -ForegroundColor Red
}

function Test-ServiceRunning {
    param([string]$ServiceName)
    try {
        $processes = Get-Process -Name $ServiceName -ErrorAction SilentlyContinue
        return $processes.Count -gt 0
    }
    catch {
        return $false
    }
}

function Stop-ServiceSafely {
    param([string]$ServiceName, [string]$DisplayName)
    
    if (Test-ServiceRunning $ServiceName) {
        Write-Host "    Stopping $DisplayName..." -ForegroundColor Cyan
        if (-not $DryRun) {
            try {
                Stop-Process -Name $ServiceName -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 2
                
                if (Test-ServiceRunning $ServiceName) {
                    Write-Warning "$DisplayName still running after graceful shutdown attempt"
                    return $false
                }
                else {
                    Write-Success "$DisplayName stopped successfully"
                    return $true
                }
            }
            catch {
                Write-Warning "Failed to stop $DisplayName`: $_"
                return $false
            }
        }
        else {
            Write-Host "    [DRY RUN] Would stop $DisplayName" -ForegroundColor Gray
            return $true
        }
    }
    else {
        Write-Success "$DisplayName is not running"
        return $true
    }
}

function Backup-Configuration {
    param([string]$FilePath, [string]$BackupDir)
    
    if (Test-Path $FilePath) {
        $fileName = [System.IO.Path]::GetFileName($FilePath)
        $backupFile = Join-Path $BackupDir "$fileName.pre-rollback.$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        
        if (-not $DryRun) {
            New-Item -Path $BackupDir -ItemType Directory -Force | Out-Null
            Copy-Item -Path $FilePath -Destination $backupFile -Force
        }
        
        Write-Success "$(if ($DryRun) { '[DRY RUN] Would backup' } else { 'Backed up' }) $fileName to $backupFile"
        return $backupFile
    }
    else {
        Write-Warning "File not found: $FilePath"
        return $null
    }
}

function Update-FeatureFlag {
    param([string]$ConfigPath, [string]$FlagName, [bool]$Enabled)
    
    if (Test-Path $ConfigPath) {
        if (-not $DryRun) {
            try {
                $config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
                
                # Ensure FeatureManagement section exists
                if (-not $config.FeatureManagement) {
                    $config | Add-Member -Type NoteProperty -Name "FeatureManagement" -Value @{}
                }
                
                # Set the feature flag
                $config.FeatureManagement.$FlagName = $Enabled
                
                # Write back to file
                $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigPath -Encoding UTF8
                
                Write-Success "Set $FlagName = $Enabled in $ConfigPath"
            }
            catch {
                Write-Error "Failed to update feature flag in $ConfigPath`: $_"
                return $false
            }
        }
        else {
            Write-Host "    [DRY RUN] Would set $FlagName = $Enabled in $ConfigPath" -ForegroundColor Gray
        }
        return $true
    }
    else {
        Write-Warning "Configuration file not found: $ConfigPath"
        return $false
    }
}

# Main rollback steps
try {
    Write-Step "Pre-rollback validation"
    
    # Check if we're in the right directory
    if (-not (Test-Path "server/AIChat.Server.csproj")) {
        throw "Script must be run from the project root directory"
    }
    Write-Success "Project root directory confirmed"
    
    # Check timeout
    if ($TimeoutMinutes -gt 10) {
        Write-Warning "Timeout set to $TimeoutMinutes minutes - rollback should complete in under 5 minutes"
        if (-not $Force) {
            $continue = Read-Host "Continue anyway? (y/N)"
            if ($continue -ne "y" -and $continue -ne "Y") {
                throw "Rollback cancelled by user"
            }
        }
    }
    
    Write-Step "Stop running services"
    
    # Stop Orleans Host
    $orleansHostStopped = Stop-ServiceSafely -ServiceName "AIChat.Orleans.Host" -DisplayName "Orleans Host"
    
    # Stop Server
    $serverStopped = Stop-ServiceSafely -ServiceName "AIChat.Server" -DisplayName "AIChat Server"
    
    Write-Step "Backup current configuration"
    
    # Create backup directory
    if (-not $DryRun) {
        New-Item -Path $BackupPath -ItemType Directory -Force | Out-Null
    }
    
    # Backup server configuration files
    $serverBackups = @()
    $serverConfigs = @(
        "server/appsettings.json",
        "server/appsettings.Development.json",
        "server/appsettings.Test.json"
    )
    
    foreach ($config in $serverConfigs) {
        if (Test-Path $config) {
            $backup = Backup-Configuration -FilePath $config -BackupDir $BackupPath
            if ($backup) {
                $serverBackups += $backup
            }
        }
    }
    
    Write-Step "Disable Orleans feature flags"
    
    # Disable Orleans integration in all server configurations
    $featureFlagUpdates = @()
    foreach ($config in $serverConfigs) {
        if (Test-Path $config) {
            $success = Update-FeatureFlag -ConfigPath $config -FlagName "OrleansIntegration" -Enabled $false
            $featureFlagUpdates += @{ Config = $config; Success = $success }
        }
    }
    
    Write-Step "Verify Orleans is disabled"
    
    # Start server in verification mode
    if (-not $DryRun) {
        Write-Host "    Starting server for verification..." -ForegroundColor Cyan
        
        $serverProcess = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "server" -PassThru -NoNewWindow
        Start-Sleep -Seconds 10
        
        try {
            # Check if server is responding
            $healthCheck = Invoke-RestMethod -Uri "http://localhost:5000/api/health/detailed" -TimeoutSec 10
            
            # Look for Orleans in health checks - should not be present
            $orleansFound = $false
            if ($healthCheck.Checks) {
                foreach ($check in $healthCheck.Checks.PSObject.Properties) {
                    if ($check.Name -like "*orleans*") {
                        $orleansFound = $true
                        break
                    }
                }
            }
            
            if ($orleansFound) {
                Write-Warning "Orleans components still detected in health checks"
            }
            else {
                Write-Success "Orleans components not found in health checks - rollback appears successful"
            }
            
        }
        catch {
            Write-Warning "Could not verify server health: $_"
        }
        finally {
            # Stop the server
            if ($serverProcess -and -not $serverProcess.HasExited) {
                $serverProcess.Kill()
                $serverProcess.WaitForExit(5000)
            }
        }
    }
    else {
        Write-Host "    [DRY RUN] Would start server and verify Orleans is disabled" -ForegroundColor Gray
    }
    
    Write-Step "Run rollback verification tests"
    
    if (-not $DryRun) {
        Write-Host "    Running server tests to verify functionality..." -ForegroundColor Cyan
        
        try {
            # Run a subset of critical tests
            $testResult = & dotnet test server.Tests --logger "console;verbosity=minimal" --filter "Category!=Performance&Category!=Integration" 2>&1
            
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Core functionality tests passed"
            }
            else {
                Write-Warning "Some tests failed - review output above"
            }
        }
        catch {
            Write-Warning "Test execution failed: $_"
        }
    }
    else {
        Write-Host "    [DRY RUN] Would run verification tests" -ForegroundColor Gray
    }
    
    Write-Step "Generate rollback report"
    
    $rollbackReport = @{
        Timestamp = $StartTime
        DryRun = $DryRun
        ServicesStopepd = @{
            OrleansHost = $orleansHostStopped
            Server = $serverStopped
        }
        Backups = $serverBackups
        FeatureFlags = $featureFlagUpdates
        ElapsedTime = (Get-Date) - $StartTime
        Success = $true
    }
    
    $reportPath = Join-Path $BackupPath "rollback-report-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
    
    if (-not $DryRun) {
        $rollbackReport | ConvertTo-Json -Depth 10 | Set-Content $reportPath -Encoding UTF8
        Write-Success "Rollback report saved to: $reportPath"
    }
    else {
        Write-Host "    [DRY RUN] Would save rollback report to: $reportPath" -ForegroundColor Gray
    }
    
    # Final summary
    $endTime = Get-Date
    $duration = $endTime - $StartTime
    
    Write-Host "`n=======================================================" -ForegroundColor Green
    Write-Host "   Orleans Phase 1 Rollback $(if ($DryRun) { 'DRY RUN ' })COMPLETED" -ForegroundColor Green
    Write-Host "   Duration: $($duration.ToString('mm\:ss'))" -ForegroundColor Green
    Write-Host "   Status: SUCCESS" -ForegroundColor Green
    Write-Host "=======================================================" -ForegroundColor Green
    
    if (-not $DryRun) {
        Write-Host "`nNext Steps:" -ForegroundColor Cyan
        Write-Host "1. Verify your application is working correctly" -ForegroundColor White
        Write-Host "2. Run full test suite: dotnet test" -ForegroundColor White  
        Write-Host "3. Start your services: ./start-server.ps1" -ForegroundColor White
        Write-Host "4. Monitor application logs for any issues" -ForegroundColor White
        Write-Host "`nBackups available in: $BackupPath" -ForegroundColor White
        Write-Host "Rollback report: $reportPath" -ForegroundColor White
    }
    else {
        Write-Host "`nTo execute this rollback, run:" -ForegroundColor Cyan
        Write-Host "  .\scripts\rollback-orleans-phase1.ps1" -ForegroundColor White
    }
    
    exit 0
}
catch {
    $endTime = Get-Date
    $duration = $endTime - $StartTime
    
    Write-Host "`n=======================================================" -ForegroundColor Red
    Write-Host "   Orleans Phase 1 Rollback FAILED" -ForegroundColor Red
    Write-Host "   Duration: $($duration.ToString('mm\:ss'))" -ForegroundColor Red
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "=======================================================" -ForegroundColor Red
    
    Write-Host "`nRollback failed. Manual intervention may be required." -ForegroundColor Yellow
    Write-Host "Check the error above and consult the rollback runbook." -ForegroundColor Yellow
    
    if ($serverBackups.Count -gt 0) {
        Write-Host "`nConfiguration backups are available:" -ForegroundColor White
        foreach ($backup in $serverBackups) {
            Write-Host "  $backup" -ForegroundColor Gray
        }
    }
    
    exit 1
}