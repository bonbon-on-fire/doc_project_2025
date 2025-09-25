# Orleans Troubleshooting Guide

## Overview

This guide provides systematic troubleshooting procedures for Orleans-based AI Chat system issues. It covers common problems, diagnostic procedures, and resolution steps with real-world scenarios and practical solutions.

## Quick Reference

### Emergency Actions
1. **System Down**: Check health endpoints, restart services if needed
2. **Performance Issues**: Review metrics, check memory usage, analyze logs
3. **Orleans Failures**: Enable direct service fallback via feature flag
4. **Data Issues**: Validate database connectivity and state consistency

### Key Diagnostic Commands
```powershell
# Quick system health check
Invoke-WebRequest -Uri "http://localhost:5099/health"
Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"

# Check running processes
Get-Process -Name "*AIChat*" -ErrorAction SilentlyContinue

# Review recent logs
Get-Content "logs\orleans-host-*.log" | Select-Object -Last 20
```

## Orleans Service Issues

### 1. Orleans Silo Not Starting

#### Symptoms
- Orleans Dashboard not accessible
- Health checks failing
- "Connection refused" errors
- Service startup failures

#### Diagnostic Steps
```powershell
# Check if Orleans Host process is running
Get-Process -Name "*Orleans*" -ErrorAction SilentlyContinue

# Verify port availability
netstat -an | findstr "11111 30000 8080 5100"

# Check Orleans Host logs
Get-Content "logs\orleans-host-*.log" | Select-String "ERROR|FATAL|EXCEPTION" | Select-Object -Last 10

# Test Orleans configuration
Get-Content "server\AIChat.Orleans.Host\appsettings.json" | ConvertFrom-Json | Select-Object -ExpandProperty Orleans
```

#### Common Causes and Solutions

**Port Conflicts**
```powershell
# Find processes using Orleans ports
netstat -ano | findstr ":11111 :30000 :8080 :5100"

# Kill conflicting processes
Get-Process -Id <PID> | Stop-Process -Force

# Start Orleans with alternative ports
$env:Orleans__SiloPort = "11112"
$env:Orleans__GatewayPort = "30001"
.\build-and-start-server.ps1 -UseOrleans
```

**Configuration Issues**
```powershell
# Validate Orleans configuration
$config = Get-Content "server\AIChat.Orleans.Host\appsettings.json" | ConvertFrom-Json
if ([string]::IsNullOrEmpty($config.Orleans.ClusterId)) {
    Write-Error "Orleans ClusterId not configured"
}

# Reset to default configuration
Copy-Item "server\AIChat.Orleans.Host\appsettings.json.template" "server\AIChat.Orleans.Host\appsettings.json"
```

**Dependency Issues**
```powershell
# Check .NET runtime
dotnet --list-runtimes | findstr "Microsoft.AspNetCore.App"

# Verify Orleans packages
dotnet list server\AIChat.Orleans\AIChat.Orleans.csproj package | findstr "Orleans"

# Clean and rebuild
dotnet clean && dotnet build --configuration Release
```

### 2. Grain Activation Failures

#### Symptoms
- Grain operations failing
- High activation times (>5000ms)
- "Grain not found" errors
- Memory usage spikes

#### Diagnostic Steps
```powershell
# Check grain activation metrics
$metrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"
Write-Host "Active Grains: $($metrics.ActiveGrains)"
Write-Host "Activation Time: $($metrics.AverageActivationTime)ms"
Write-Host "Memory Usage: $($metrics.MemoryUsageMB)MB"

# Review grain-specific logs
Get-Content "logs\orleans-host-*.log" | Select-String "grain.*activation|grain.*deactivation" | Select-Object -Last 10

# Check specific grain types
Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics/UserGrain"
Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics/ChatGrain"
Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics/ModeGrain"
```

#### Common Causes and Solutions

**Memory Pressure**
```powershell
# Check system memory
Get-WmiObject -Class Win32_OperatingSystem | Select-Object @{Name="MemoryUsage";Expression={[math]::round((($_.TotalVisibleMemorySize - $_.FreePhysicalMemory) / $_.TotalVisibleMemorySize) * 100, 2)}}

# Force garbage collection
[System.GC]::Collect()
[System.GC]::WaitForPendingFinalizers()

# Restart Orleans with memory limits
$env:DOTNET_GCHeapHardLimit = "4GB"
.\build-and-start-server.ps1 -UseOrleans
```

**State Persistence Issues**
```powershell
# Check database connectivity
Test-Path "data\aichat.db"
sqlite3 "data\aichat.db" ".tables"

# Validate grain state tables
sqlite3 "data\aichat.db" "SELECT COUNT(*) FROM GrainState;"

# Reset grain state (CAUTION: Data loss)
Remove-Item "data\aichat.db" -ErrorAction SilentlyContinue
.\build-and-start-server.ps1 -UseOrleans
```

**Grain Implementation Issues**
```powershell
# Check grain interface implementations
Get-Content "logs\orleans-host-*.log" | Select-String "interface.*not.*found|method.*not.*implemented"

# Verify grain registration
Get-Content "logs\orleans-host-*.log" | Select-String "grain.*registered|grain.*discovered"

# Test individual grain operations
# Use Orleans Dashboard or direct API calls for grain testing
```

## Dual-Mode Router Issues

### 3. Router Fallback Not Working

#### Symptoms
- Orleans failures not falling back to direct services
- Inconsistent behavior between Orleans and direct modes
- Feature flag changes not taking effect
- Circuit breaker not activating

#### Diagnostic Steps
```powershell
# Check dual-mode router status
Get-Content "logs\server\*.log" | Select-String "DualModeRouter|fallback|circuit.*breaker" | Select-Object -Last 10

# Verify feature flag configuration
$appSettings = Get-Content "server\AIChat.Server\appsettings.json" | ConvertFrom-Json
Write-Host "Orleans Enabled: $($appSettings.FeatureManagement.OrleansEnabled)"

# Test router behavior
Invoke-RestMethod -Uri "http://localhost:5099/api/chat" -Method GET
```

#### Common Causes and Solutions

**Feature Flag Issues**
```powershell
# Enable direct service fallback
$appSettings = Get-Content "server\AIChat.Server\appsettings.json" | ConvertFrom-Json
$appSettings.FeatureManagement.OrleansEnabled = $false
$appSettings | ConvertTo-Json -Depth 10 | Set-Content "server\AIChat.Server\appsettings.json"

# Restart server to apply changes
Get-Process -Name "*AIChat.Server*" | Stop-Process -Force
Start-Sleep 5
.\build-and-start-server.ps1 -Environment Production
```

**Circuit Breaker Configuration**
```powershell
# Check circuit breaker thresholds in logs
Get-Content "logs\server\*.log" | Select-String "circuit.*breaker.*threshold|failure.*rate"

# Manually trigger circuit breaker (for testing)
# This would be done through Orleans Dashboard or specific test endpoints
```

**Router Registration Issues**
```powershell
# Verify DualModeRouter registration
Get-Content "server\AIChat.Server\Program.cs" | Select-String "DualModeRouter|AddRouting"

# Check dependency injection container
Get-Content "logs\server\*.log" | Select-String "service.*registered|dependency.*injection" | Select-Object -Last 5
```

### 4. State Synchronization Issues

#### Symptoms
- Data inconsistency between Orleans and direct services
- Stale data being served
- Cache invalidation not working
- State recovery failures

#### Diagnostic Steps
```powershell
# Check state consistency
$orleansData = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/state/chat/123"
$directData = Invoke-RestMethod -Uri "http://localhost:5099/api/chat/123"

# Compare state timestamps
Write-Host "Orleans State Updated: $($orleansData.LastModified)"
Write-Host "Direct State Updated: $($directData.LastModified)"

# Review cache invalidation logs
Get-Content "logs\server\*.log" | Select-String "cache.*invalidation|state.*sync" | Select-Object -Last 10
```

#### Solutions
```powershell
# Force state synchronization
Invoke-RestMethod -Uri "http://localhost:5099/api/admin/sync-state" -Method POST

# Clear all caches
Invoke-RestMethod -Uri "http://localhost:5099/api/admin/clear-cache" -Method POST

# Restart Orleans to reload state
Get-Process -Name "*Orleans*" | Stop-Process -Force
Start-Sleep 10
.\build-and-start-server.ps1 -UseOrleans
```

## Performance Issues

### 5. High Latency

#### Symptoms
- Request processing time >1000ms
- Grain activation time >5000ms
- Dashboard showing red performance indicators
- User-reported slow response times

#### Diagnostic Steps
```powershell
# Check current performance metrics
$metrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"
Write-Host "Average Request Time: $($metrics.AverageRequestTime)ms"
Write-Host "P95 Latency: $($metrics.P95Latency)ms"
Write-Host "P99 Latency: $($metrics.P99Latency)ms"

# Monitor CPU and memory
Get-Counter "\Processor(_Total)\% Processor Time" -SampleInterval 1 -MaxSamples 5
Get-Counter "\Memory\Available MBytes" -SampleInterval 1 -MaxSamples 5

# Check for slow operations in logs
Get-Content "logs\orleans-host-*.log" | Select-String "slow|timeout|exceeded" | Select-Object -Last 10
```

#### Solutions

**Memory Optimization**
```powershell
# Check memory usage per grain type
$userGrainMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics/UserGrain"
$chatGrainMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics/ChatGrain"
$modeGrainMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics/ModeGrain"

Write-Host "UserGrain Memory: $($userGrainMetrics.MemoryUsageMB)MB"
Write-Host "ChatGrain Memory: $($chatGrainMetrics.MemoryUsageMB)MB"
Write-Host "ModeGrain Memory: $($modeGrainMetrics.MemoryUsageMB)MB"

# Configure memory limits
$env:DOTNET_GCHeapHardLimit = "2GB"
$env:DOTNET_GCConserveMemory = "1"
```

**Database Optimization**
```powershell
# Check database performance
sqlite3 "data\aichat.db" ".timer on" "SELECT COUNT(*) FROM GrainState;"

# Analyze slow queries
sqlite3 "data\aichat.db" "EXPLAIN QUERY PLAN SELECT * FROM GrainState WHERE GrainType = 'ChatGrain';"

# Vacuum database if needed
sqlite3 "data\aichat.db" "VACUUM;"
```

**Grain Placement Optimization**
```powershell
# Check grain placement distribution
$placementMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/placement/metrics"
Write-Host "Placement Efficiency: $($placementMetrics.PlacementEfficiency)%"

# Review placement strategy in logs
Get-Content "logs\orleans-host-*.log" | Select-String "placement.*strategy|grain.*placement" | Select-Object -Last 5
```

### 6. High Error Rates

#### Symptoms
- Error rate >5%
- Frequent exceptions in logs
- Failed health checks
- User-reported failures

#### Diagnostic Steps
```powershell
# Check error rate metrics
$metrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"
Write-Host "Error Rate: $($metrics.ErrorRatePercent)%"
Write-Host "Failed Requests: $($metrics.FailedRequests)"

# Review recent errors
Get-Content "logs\orleans-host-*.log" | Select-String "ERROR|EXCEPTION" | Select-Object -Last 20

# Check specific error patterns
Get-Content "logs\orleans-host-*.log" | Select-String "TimeoutException|NullReferenceException|InvalidOperationException" | Select-Object -Last 10
```

#### Solutions

**Exception Analysis**
```powershell
# Group exceptions by type
$errors = Get-Content "logs\orleans-host-*.log" | Select-String "EXCEPTION"
$errorGroups = $errors | Group-Object { ($_.Line -split ':')[3] }
$errorGroups | Sort-Object Count -Descending | Select-Object Name, Count

# Address specific exception types based on frequency
```

**Timeout Issues**
```powershell
# Check timeout configuration
Get-Content "server\AIChat.Orleans.Host\appsettings.json" | ConvertFrom-Json | Select-Object -ExpandProperty Orleans

# Increase timeout values if needed
$config = Get-Content "server\AIChat.Orleans.Host\appsettings.json" | ConvertFrom-Json
$config.Orleans.ResponseTimeout = 60000  # 60 seconds
$config | ConvertTo-Json -Depth 10 | Set-Content "server\AIChat.Orleans.Host\appsettings.json"
```

## Network and Connectivity Issues

### 7. Connection Problems

#### Symptoms
- "Connection refused" errors
- Intermittent service availability
- SignalR disconnections
- WebSocket failures

#### Diagnostic Steps
```powershell
# Test network connectivity
Test-NetConnection -ComputerName localhost -Port 5099
Test-NetConnection -ComputerName localhost -Port 8080
Test-NetConnection -ComputerName localhost -Port 11111

# Check firewall settings
Get-NetFirewallRule | Where-Object {$_.DisplayName -like "*5099*" -or $_.DisplayName -like "*8080*"}

# Review connection logs
Get-Content "logs\server\*.log" | Select-String "connection|disconnect|refused" | Select-Object -Last 10
```

#### Solutions

**Port Conflicts**
```powershell
# Find and stop conflicting processes
$conflictingPorts = @(5099, 8080, 11111, 30000, 5100)
foreach ($port in $conflictingPorts) {
    $process = Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue
    if ($process) {
        Write-Host "Port $port is in use by process $($process.OwningProcess)"
        # Stop-Process -Id $process.OwningProcess -Force  # Uncomment if safe to stop
    }
}
```

**Firewall Configuration**
```powershell
# Allow Orleans ports through Windows Firewall
New-NetFirewallRule -DisplayName "Orleans Silo" -Direction Inbound -Port 11111 -Protocol TCP -Action Allow
New-NetFirewallRule -DisplayName "Orleans Gateway" -Direction Inbound -Port 30000 -Protocol TCP -Action Allow
New-NetFirewallRule -DisplayName "Orleans Dashboard" -Direction Inbound -Port 8080 -Protocol TCP -Action Allow
New-NetFirewallRule -DisplayName "AIChat Server" -Direction Inbound -Port 5099 -Protocol TCP -Action Allow
```

**Network Interface Issues**
```powershell
# Check network interface configuration
Get-NetAdapter | Where-Object {$_.Status -eq "Up"}
Get-NetIPAddress | Where-Object {$_.AddressFamily -eq "IPv4"}

# Test loopback connectivity
ping localhost
telnet localhost 5099
```

## Database and Storage Issues

### 8. Data Persistence Problems

#### Symptoms
- State not being saved
- Grain state reset on restart
- Database connectivity errors
- Data corruption warnings

#### Diagnostic Steps
```powershell
# Check database file existence and permissions
Test-Path "data\aichat.db"
Get-Acl "data\aichat.db" | Select-Object Owner, AccessToString

# Verify database schema
sqlite3 "data\aichat.db" ".schema"

# Check for database locks
sqlite3 "data\aichat.db" "BEGIN IMMEDIATE; ROLLBACK;"

# Review persistence errors
Get-Content "logs\orleans-host-*.log" | Select-String "database|persistence|sqlite" | Select-Object -Last 10
```

#### Solutions

**Database Repair**
```powershell
# Check database integrity
sqlite3 "data\aichat.db" "PRAGMA integrity_check;"

# Repair database if needed
sqlite3 "data\aichat.db" "PRAGMA wal_checkpoint(TRUNCATE);"
sqlite3 "data\aichat.db" "VACUUM;"

# Backup and recreate if corrupted
Copy-Item "data\aichat.db" "data\aichat-backup-$(Get-Date -Format 'yyyy-MM-dd-HH-mm').db"
Remove-Item "data\aichat.db"
# Database will be recreated on next startup
```

**Permissions Issues**
```powershell
# Fix database permissions
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
icacls "data\aichat.db" /grant "${currentUser}:F"

# Ensure data directory exists and is writable
if (!(Test-Path "data")) {
    New-Item -ItemType Directory -Path "data"
}
```

## Log Analysis Procedures

### 9. Systematic Log Analysis

#### Key Log Locations
- **Orleans Host**: `logs\orleans-host-*.log`
- **Server**: `logs\server\*.log`
- **Application**: Application Insights (if configured)

#### Common Log Analysis Commands
```powershell
# Find all errors in the last hour
$oneHourAgo = (Get-Date).AddHours(-1)
Get-Content "logs\orleans-host-*.log" | Where-Object {
    $line = $_
    try {
        $timestamp = [DateTime]::ParseExact($line.Substring(1, 23), "yyyy-MM-dd HH:mm:ss.fff", $null)
        return $timestamp -gt $oneHourAgo -and $line -match "ERROR|FATAL|EXCEPTION"
    } catch {
        return $false
    }
} | Select-Object -Last 20

# Performance-related log entries
Get-Content "logs\orleans-host-*.log" | Select-String "slow|timeout|performance|latency" | Select-Object -Last 10

# Grain lifecycle events
Get-Content "logs\orleans-host-*.log" | Select-String "grain.*activated|grain.*deactivated|grain.*created" | Select-Object -Last 15

# Memory and resource issues
Get-Content "logs\orleans-host-*.log" | Select-String "memory|OutOfMemory|GC|garbage" | Select-Object -Last 10
```

#### Log Pattern Analysis
```powershell
# Create log analysis report
$logFile = "logs\orleans-host-$(Get-Date -Format 'yyyy-MM-dd').log"
$errors = Get-Content $logFile | Select-String "ERROR|EXCEPTION"
$warnings = Get-Content $logFile | Select-String "WARNING|WARN"
$performance = Get-Content $logFile | Select-String "slow|timeout|performance"

$report = @{
    Date = Get-Date
    TotalErrors = $errors.Count
    TotalWarnings = $warnings.Count
    PerformanceIssues = $performance.Count
    TopErrors = $errors | Group-Object { ($_.Line -split ']')[2].Trim() } | Sort-Object Count -Descending | Select-Object -First 5
}

$report | ConvertTo-Json -Depth 5 | Out-File "log-analysis-$(Get-Date -Format 'yyyy-MM-dd').json"
```

## Escalation Procedures

### 10. When to Escalate

#### Immediate Escalation (Critical)
- System completely down for >5 minutes
- Data corruption detected
- Security breach indicators
- Memory usage >95% sustained

#### Standard Escalation (High Priority)
- Error rate >10% for >15 minutes
- Performance degradation >50% for >30 minutes
- Orleans silo repeatedly failing
- Multiple grain types failing

#### Escalation Contacts
```powershell
# Escalation script template
$escalationInfo = @{
    Timestamp = Get-Date
    Issue = "Brief description of the issue"
    Impact = "User impact assessment"
    SystemStatus = @{
        OrleansUp = (Test-NetConnection -ComputerName localhost -Port 8080 -InformationLevel Quiet)
        ServerUp = (Test-NetConnection -ComputerName localhost -Port 5099 -InformationLevel Quiet)
        DatabaseAccessible = (Test-Path "data\aichat.db")
    }
    RecentLogs = Get-Content "logs\orleans-host-*.log" | Select-Object -Last 10
    Metrics = try { Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics" -TimeoutSec 5 } catch { "Not available" }
}

# Send escalation information
$escalationInfo | ConvertTo-Json -Depth 10 | Out-File "escalation-report-$(Get-Date -Format 'yyyy-MM-dd-HH-mm').json"
Write-Host "Escalation report created. Send to: operations@company.com"
```

## Diagnostic Scripts

### 11. Automated Diagnostic Tools

#### System Health Check Script
```powershell
# Save as: scripts/orleans-health-check.ps1
function Test-OrleansHealth {
    $results = @{
        Timestamp = Get-Date
        Tests = @{}
    }

    # Test Orleans Dashboard
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:8080" -TimeoutSec 10
        $results.Tests.OrleansDashboard = @{ Status = "Pass"; Response = $response.StatusCode }
    } catch {
        $results.Tests.OrleansDashboard = @{ Status = "Fail"; Error = $_.Exception.Message }
    }

    # Test Server Health
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:5099/health" -TimeoutSec 10
        $results.Tests.ServerHealth = @{ Status = "Pass"; Response = $response }
    } catch {
        $results.Tests.ServerHealth = @{ Status = "Fail"; Error = $_.Exception.Message }
    }

    # Test Orleans Metrics
    try {
        $metrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics" -TimeoutSec 10
        $results.Tests.OrleansMetrics = @{
            Status = "Pass"
            ActiveGrains = $metrics.ActiveGrains
            MemoryUsage = $metrics.MemoryUsageMB
            ErrorRate = $metrics.ErrorRatePercent
        }
    } catch {
        $results.Tests.OrleansMetrics = @{ Status = "Fail"; Error = $_.Exception.Message }
    }

    return $results
}

# Run health check and display results
$healthCheck = Test-OrleansHealth
$healthCheck | ConvertTo-Json -Depth 5
```

#### Performance Analysis Script
```powershell
# Save as: scripts/orleans-performance-analysis.ps1
function Analyze-OrleansPerformance {
    param(
        [int]$SampleMinutes = 10
    )

    $samples = @()
    $endTime = (Get-Date).AddMinutes($SampleMinutes)

    while ((Get-Date) -lt $endTime) {
        try {
            $metrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"
            $sample = @{
                Timestamp = Get-Date
                ActiveGrains = $metrics.ActiveGrains
                AverageActivationTime = $metrics.AverageActivationTime
                RequestsPerSecond = $metrics.RequestsPerSecond
                ErrorRate = $metrics.ErrorRatePercent
                MemoryUsageMB = $metrics.MemoryUsageMB
            }
            $samples += $sample
            Write-Host "Sample collected: $($sample.Timestamp) - RPS: $($sample.RequestsPerSecond), Memory: $($sample.MemoryUsageMB)MB"
        } catch {
            Write-Warning "Failed to collect sample: $($_.Exception.Message)"
        }
        Start-Sleep 30
    }

    # Calculate averages
    $analysis = @{
        SampleCount = $samples.Count
        AverageActiveGrains = ($samples | Measure-Object -Property ActiveGrains -Average).Average
        AverageActivationTime = ($samples | Measure-Object -Property AverageActivationTime -Average).Average
        AverageRPS = ($samples | Measure-Object -Property RequestsPerSecond -Average).Average
        AverageErrorRate = ($samples | Measure-Object -Property ErrorRate -Average).Average
        AverageMemoryUsage = ($samples | Measure-Object -Property MemoryUsageMB -Average).Average
        MaxMemoryUsage = ($samples | Measure-Object -Property MemoryUsageMB -Maximum).Maximum
    }

    return @{
        Analysis = $analysis
        Samples = $samples
    }
}

# Run performance analysis
$perfAnalysis = Analyze-OrleansPerformance -SampleMinutes 5
$perfAnalysis.Analysis | ConvertTo-Json -Depth 3
```

## Prevention and Best Practices

### 12. Preventive Measures

#### Daily Monitoring Tasks
- Review Orleans Dashboard for anomalies
- Check error rates and performance metrics
- Monitor memory usage trends
- Verify log file rotation and cleanup

#### Weekly Maintenance
- Analyze performance baselines
- Review and optimize grain placement
- Check database integrity and performance
- Update monitoring thresholds based on trends

#### Monthly Reviews
- Review escalation incidents and root causes
- Update troubleshooting procedures based on new issues
- Conduct disaster recovery drills
- Train team on new diagnostic procedures

---

**Document Version**: 1.0
**Last Updated**: September 2025
**Next Review**: Quarterly
**Owner**: Operations Team