# Orleans Recovery Procedures Guide

## Overview

This guide provides comprehensive recovery procedures for Orleans-based AI Chat system. It covers automatic state reconstruction, point-in-time recovery, snapshot management, and disaster recovery scenarios with step-by-step procedures for different failure situations.

## Recovery System Architecture

### Recovery Components
- **Automatic State Reconstruction**: Grain activation-time state recovery
- **Event Store**: SQLite-based event sourcing with replay capabilities
- **Snapshot Management**: Automated snapshots with restoration procedures
- **Point-in-Time Recovery**: Time-travel debugging and state recovery
- **Grain State Recovery**: Orleans native persistence recovery
- **Database Recovery**: SQLite database backup and restoration

### Recovery Levels
1. **Level 1**: Individual grain state recovery (minutes)
2. **Level 2**: Service-wide state recovery (10-30 minutes)
3. **Level 3**: Point-in-time system recovery (1-2 hours)
4. **Level 4**: Full disaster recovery (2-4 hours)

## Prerequisites

### Access Requirements
- **System Administrator Access**: Required for all recovery operations
- **Database Access**: SQLite database file access permissions
- **API Endpoints**: Access to Orleans recovery APIs
- **Backup Storage**: Access to snapshot and backup storage locations

### Tools and Scripts
```powershell
# Verify recovery prerequisites
Test-Path "data\aichat.db"                           # Main database
Test-Path "data\snapshots\"                          # Snapshot storage
Test-Path "data\events\"                            # Event store
Test-NetConnection -ComputerName localhost -Port 5099 # Server access
Test-NetConnection -ComputerName localhost -Port 5100 # Orleans Host access
```

### Backup Verification
```powershell
# Check backup availability
Get-ChildItem "data\snapshots\" | Sort-Object LastWriteTime -Descending | Select-Object -First 5
Get-ChildItem "backups\" -Filter "*.db" | Sort-Object LastWriteTime -Descending | Select-Object -First 5
```

## Level 1: Individual Grain State Recovery

### 1.1 Automatic State Reconstruction

The system automatically attempts state reconstruction during grain activation when state is missing or corrupted.

#### When It's Triggered
- Grain activates with null or default state
- State validation fails during grain activation
- Corruption detected in grain state
- State version mismatches during load

#### Manual Trigger (if needed)
```powershell
# Force grain state reconstruction for specific grain
Invoke-RestMethod -Uri "http://localhost:5099/api/admin/recovery/reconstruct" -Method POST -Body @{
    GrainType = "ChatGrain"
    GrainKey = "chat-123"
    RecoveryStrategy = "Automatic"
} -ContentType "application/json"

# Monitor reconstruction progress
Get-Content "logs\orleans-host-*.log" | Select-String "state.*reconstruction|recovery.*progress" | Select-Object -Last 10
```

#### Verification Steps
```powershell
# Verify grain state after reconstruction
$grainState = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/grains/ChatGrain/chat-123/state"
Write-Host "State Version: $($grainState.Version)"
Write-Host "Last Modified: $($grainState.LastModified)"
Write-Host "Reconstruction Status: $($grainState.ReconstructionStatus)"

# Check reconstruction metrics
$metrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"
Write-Host "Recent Reconstructions: $($metrics.StateReconstructionsToday)"
```

### 1.2 Event Store Recovery for Single Grain

#### Recovery Procedure
```powershell
# Step 1: Identify grain and recovery point
$grainType = "ChatGrain"
$grainKey = "chat-123"
$recoveryTimestamp = "2025-09-24T10:30:00Z"

# Step 2: Query available events for the grain
$events = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/eventstore/events" -Body @{
    GrainType = $grainType
    GrainKey = $grainKey
    StartTime = (Get-Date).AddDays(-7).ToString("yyyy-MM-ddTHH:mm:ssZ")
    EndTime = $recoveryTimestamp
} -ContentType "application/json" -Method POST

Write-Host "Found $($events.Count) events for recovery"

# Step 3: Replay events to reconstruct state
$recoveryRequest = @{
    GrainType = $grainType
    GrainKey = $grainKey
    RecoveryTimestamp = $recoveryTimestamp
    RecoveryStrategy = "EventReplay"
    ValidateState = $true
}

$recoveryResult = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/recovery/replay-events" -Method POST -Body ($recoveryRequest | ConvertTo-Json) -ContentType "application/json"

Write-Host "Recovery Status: $($recoveryResult.Status)"
Write-Host "Events Replayed: $($recoveryResult.EventsReplayed)"
```

#### Verification and Rollback
```powershell
# Verify recovered state
$recoveredState = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/grains/$grainType/$grainKey/state"
Write-Host "State after recovery: Version $($recoveredState.Version), Modified $($recoveredState.LastModified)"

# If recovery failed, rollback to latest snapshot
if ($recoveryResult.Status -eq "Failed") {
    Write-Warning "Recovery failed, rolling back to latest snapshot"
    $rollbackResult = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/recovery/rollback-to-snapshot" -Method POST -Body @{
        GrainType = $grainType
        GrainKey = $grainKey
    } -ContentType "application/json"

    Write-Host "Rollback Status: $($rollbackResult.Status)"
}
```

## Level 2: Service-Wide State Recovery

### 2.1 Multiple Grain Recovery

#### Scenario: Multiple grains need state recovery
```powershell
# Step 1: Identify affected grains
$affectedGrains = @(
    @{ Type = "ChatGrain"; Key = "chat-123" },
    @{ Type = "ChatGrain"; Key = "chat-456" },
    @{ Type = "UserGrain"; Key = "user-789" }
)

# Step 2: Batch recovery operation
$batchRecoveryRequest = @{
    Grains = $affectedGrains
    RecoveryStrategy = "SnapshotFirst"  # Try snapshots first, then event replay
    MaxConcurrentRecoveries = 5
    TimeoutMinutes = 30
}

$batchResult = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/recovery/batch-recover" -Method POST -Body ($batchRecoveryRequest | ConvertTo-Json -Depth 10) -ContentType "application/json"

Write-Host "Batch Recovery ID: $($batchResult.RecoveryId)"
Write-Host "Status: $($batchResult.Status)"
```

#### Monitor Batch Recovery Progress
```powershell
# Monitor recovery progress
$recoveryId = $batchResult.RecoveryId
do {
    Start-Sleep 10
    $progress = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/recovery/$recoveryId/status"
    Write-Host "Progress: $($progress.CompletedCount)/$($progress.TotalCount) - $($progress.Status)"

    if ($progress.Errors.Count -gt 0) {
        Write-Warning "Errors detected:"
        $progress.Errors | ForEach-Object { Write-Warning "  $($_.GrainKey): $($_.Error)" }
    }
} while ($progress.Status -eq "InProgress")

Write-Host "Batch recovery completed with status: $($progress.Status)"
```

### 2.2 Service Restart with State Recovery

#### Controlled Service Recovery
```powershell
# Step 1: Stop services gracefully
Write-Host "Stopping Orleans services..."
Get-Process -Name "*Orleans*" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "*AIChat*" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 10

# Step 2: Verify state consistency before restart
$stateValidation = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/validation/state-consistency" -Method POST -Body @{
    CheckAllGrains = $true
    RepairInconsistencies = $true
} -ContentType "application/json"

if ($stateValidation.Status -ne "Healthy") {
    Write-Warning "State inconsistencies detected and repaired: $($stateValidation.IssuesFound)"
}

# Step 3: Start services with recovery mode
$env:ORLEANS_STARTUP_MODE = "Recovery"
.\build-and-start-server.ps1 -UseOrleans -Environment Production

# Step 4: Verify service health after recovery
Start-Sleep 30
$healthCheck = Invoke-RestMethod -Uri "http://localhost:5099/health"
$orleansMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"

Write-Host "Service Health: $healthCheck"
Write-Host "Active Grains: $($orleansMetrics.ActiveGrains)"
Write-Host "Recovery Status: $($orleansMetrics.RecoveryStatus)"
```

## Level 3: Point-in-Time System Recovery

### 3.1 Time-Travel Recovery

The system supports point-in-time recovery for debugging and state restoration to specific timestamps.

#### Recovery to Specific Timestamp
```powershell
# Step 1: Identify recovery timestamp
$targetTimestamp = "2025-09-24T10:30:00Z"

# Step 2: Validate recovery feasibility
$feasibilityCheck = Invoke-RestMethod -Uri "http://localhost:5099/api/recovery/point-in-time/validate" -Method POST -Body @{
    TargetTimestamp = $targetTimestamp
    ScopeType = "System"  # Options: System, Service, Grain
    ValidateDataIntegrity = $true
} -ContentType "application/json"

if ($feasibilityCheck.Status -ne "Feasible") {
    Write-Error "Recovery not feasible: $($feasibilityCheck.Reason)"
    return
}

Write-Host "Recovery feasible. Estimated time: $($feasibilityCheck.EstimatedDurationMinutes) minutes"
Write-Host "Data availability: $($feasibilityCheck.DataAvailabilityPercent)%"

# Step 3: Create recovery checkpoint (backup current state)
$checkpointResult = Invoke-RestMethod -Uri "http://localhost:5099/api/recovery/checkpoint/create" -Method POST -Body @{
    CheckpointName = "pre-recovery-$(Get-Date -Format 'yyyy-MM-dd-HH-mm')"
    Description = "Checkpoint before point-in-time recovery to $targetTimestamp"
} -ContentType "application/json"

Write-Host "Checkpoint created: $($checkpointResult.CheckpointId)"

# Step 4: Execute point-in-time recovery
$recoveryRequest = @{
    TargetTimestamp = $targetTimestamp
    RecoveryScope = "System"
    RecoveryStrategy = "SnapshotFirstWithEventReplay"
    ValidateRecovery = $true
    NotificationEnabled = $true
}

$recoveryOperation = Invoke-RestMethod -Uri "http://localhost:5099/api/recovery/point-in-time/execute" -Method POST -Body ($recoveryRequest | ConvertTo-Json) -ContentType "application/json"

Write-Host "Point-in-time recovery started. Operation ID: $($recoveryOperation.OperationId)"
```

#### Monitor Point-in-Time Recovery
```powershell
# Monitor recovery progress with real-time updates
$operationId = $recoveryOperation.OperationId
do {
    Start-Sleep 15
    $status = Invoke-RestMethod -Uri "http://localhost:5099/api/recovery/point-in-time/$operationId/status"

    Write-Host "[$($status.ElapsedMinutes)m] $($status.CurrentPhase): $($status.ProgressPercent)%"
    Write-Host "  Grains Processed: $($status.GrainsProcessed)/$($status.TotalGrains)"
    Write-Host "  Events Replayed: $($status.EventsReplayed)"

    if ($status.Warnings.Count -gt 0) {
        $status.Warnings | ForEach-Object { Write-Warning "  $_" }
    }

    if ($status.Errors.Count -gt 0) {
        $status.Errors | ForEach-Object { Write-Error "  $_" }
    }

} while ($status.Status -eq "InProgress")

Write-Host "Point-in-time recovery completed with status: $($status.Status)"
```

#### Recovery Validation and Rollback
```powershell
# Validate recovery results
$validationResult = Invoke-RestMethod -Uri "http://localhost:5099/api/recovery/point-in-time/$operationId/validate"

Write-Host "Validation Status: $($validationResult.Status)"
Write-Host "Data Integrity: $($validationResult.DataIntegrityPercent)%"
Write-Host "State Consistency: $($validationResult.StateConsistencyPercent)%"

# If validation fails, rollback to checkpoint
if ($validationResult.Status -ne "Success" -or $validationResult.DataIntegrityPercent -lt 95) {
    Write-Warning "Recovery validation failed. Rolling back to checkpoint..."

    $rollbackResult = Invoke-RestMethod -Uri "http://localhost:5099/api/recovery/checkpoint/$($checkpointResult.CheckpointId)/restore" -Method POST

    Write-Host "Rollback Status: $($rollbackResult.Status)"
    Write-Host "System restored to pre-recovery state"
}
```

### 3.2 Version-Based Recovery

#### Recovery to Specific Version
```powershell
# Recovery to a specific system version
$targetVersion = "1.2.3"

$versionRecoveryRequest = @{
    TargetVersion = $targetVersion
    RecoveryScope = "System"
    IncludeSchema = $true
    IncludeData = $true
}

$versionRecovery = Invoke-RestMethod -Uri "http://localhost:5099/api/recovery/version/execute" -Method POST -Body ($versionRecoveryRequest | ConvertTo-Json) -ContentType "application/json"

Write-Host "Version recovery started: $($versionRecovery.OperationId)"
```

## Level 4: Full Disaster Recovery

### 4.1 Complete System Recovery

#### Scenario: Complete system failure requiring full recovery

```powershell
# Step 1: System Assessment
Write-Host "=== DISASTER RECOVERY PROCEDURE ===" -ForegroundColor Red
Write-Host "Starting full system recovery..." -ForegroundColor Yellow

# Check system status
$systemStatus = @{
    DatabaseAccessible = Test-Path "data\aichat.db"
    BackupsAvailable = (Get-ChildItem "backups\" -ErrorAction SilentlyContinue).Count -gt 0
    SnapshotsAvailable = (Get-ChildItem "data\snapshots\" -ErrorAction SilentlyContinue).Count -gt 0
    EventStoreAccessible = Test-Path "data\events\"
    ServicesRunning = (Get-Process -Name "*AIChat*" -ErrorAction SilentlyContinue).Count -gt 0
}

Write-Host "System Status Assessment:"
$systemStatus | ConvertTo-Json | Write-Host

# Step 2: Stop all services
Write-Host "Stopping all services..." -ForegroundColor Yellow
Get-Process -Name "*AIChat*" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "*Orleans*" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 10

# Step 3: Database Recovery
if (-not $systemStatus.DatabaseAccessible) {
    Write-Host "Database not accessible. Attempting recovery..." -ForegroundColor Yellow

    # Find latest database backup
    $latestBackup = Get-ChildItem "backups\" -Filter "*.db" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if ($latestBackup) {
        Write-Host "Restoring database from backup: $($latestBackup.Name)"
        Copy-Item $latestBackup.FullName "data\aichat.db" -Force

        # Verify database integrity
        $integrityCheck = & sqlite3 "data\aichat.db" "PRAGMA integrity_check;"
        if ($integrityCheck -ne "ok") {
            Write-Error "Database integrity check failed after restore"
            return
        }
        Write-Host "Database restored and verified" -ForegroundColor Green
    } else {
        Write-Error "No database backups available for restoration"
        return
    }
}

# Step 4: Event Store Recovery
if (-not $systemStatus.EventStoreAccessible) {
    Write-Host "Event Store not accessible. Recreating..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path "data\events" -Force

    # Initialize empty event store schema
    & sqlite3 "data\events\eventstore.db" "CREATE TABLE IF NOT EXISTS Events (Id TEXT PRIMARY KEY, Timestamp TEXT, GrainType TEXT, GrainKey TEXT, EventType TEXT, EventData TEXT);"
}

# Step 5: Snapshot Recovery
if (-not $systemStatus.SnapshotsAvailable) {
    Write-Host "Snapshots not available. Will rely on event replay..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path "data\snapshots" -Force
}

# Step 6: Start services in recovery mode
Write-Host "Starting services in recovery mode..." -ForegroundColor Yellow
$env:ORLEANS_STARTUP_MODE = "DisasterRecovery"
$env:ORLEANS_RECOVERY_LEVEL = "Full"

.\build-and-start-server.ps1 -UseOrleans -Environment Production

# Step 7: Wait for services to stabilize
Write-Host "Waiting for services to stabilize..."
$maxWaitMinutes = 10
$waitStart = Get-Date
do {
    Start-Sleep 30
    try {
        $healthCheck = Invoke-RestMethod -Uri "http://localhost:5099/health" -TimeoutSec 10
        $orleansCheck = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics" -TimeoutSec 10
        $servicesHealthy = $true
        Write-Host "Services responding normally"
        break
    } catch {
        $servicesHealthy = $false
        Write-Host "Services still starting..."
    }
} while (((Get-Date) - $waitStart).TotalMinutes -lt $maxWaitMinutes)

if (-not $servicesHealthy) {
    Write-Error "Services failed to start within timeout period"
    return
}
```

#### Post-Recovery Validation
```powershell
# Step 8: Comprehensive System Validation
Write-Host "Performing comprehensive system validation..." -ForegroundColor Yellow

$validationResults = @{}

# Test database connectivity and integrity
try {
    $dbRowCount = & sqlite3 "data\aichat.db" "SELECT COUNT(*) FROM GrainState;"
    $validationResults.DatabaseIntegrity = @{ Status = "Pass"; RowCount = $dbRowCount }
} catch {
    $validationResults.DatabaseIntegrity = @{ Status = "Fail"; Error = $_.Exception.Message }
}

# Test Orleans grain activation
try {
    $orleansMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"
    $validationResults.OrleansGrains = @{
        Status = "Pass"
        ActiveGrains = $orleansMetrics.ActiveGrains
        HealthStatus = $orleansMetrics.SiloHealth
    }
} catch {
    $validationResults.OrleansGrains = @{ Status = "Fail"; Error = $_.Exception.Message }
}

# Test API functionality
try {
    $apiTest = Invoke-RestMethod -Uri "http://localhost:5099/api/health/detailed" -TimeoutSec 10
    $validationResults.APIFunctionality = @{ Status = "Pass"; Response = $apiTest }
} catch {
    $validationResults.APIFunctionality = @{ Status = "Fail"; Error = $_.Exception.Message }
}

# Test recovery system functionality
try {
    $recoveryTest = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/recovery/health" -TimeoutSec 10
    $validationResults.RecoverySystem = @{ Status = "Pass"; RecoveryCapable = $recoveryTest.IsOperational }
} catch {
    $validationResults.RecoverySystem = @{ Status = "Fail"; Error = $_.Exception.Message }
}

# Generate validation report
$validationReport = @{
    Timestamp = Get-Date
    RecoveryType = "Full Disaster Recovery"
    ValidationResults = $validationResults
    OverallStatus = if (($validationResults.Values | Where-Object { $_.Status -eq "Fail" }).Count -eq 0) { "Success" } else { "Partial Success" }
}

Write-Host "=== DISASTER RECOVERY VALIDATION REPORT ===" -ForegroundColor Green
$validationReport | ConvertTo-Json -Depth 10 | Write-Host

# Save validation report
$validationReport | ConvertTo-Json -Depth 10 | Out-File "disaster-recovery-validation-$(Get-Date -Format 'yyyy-MM-dd-HH-mm').json"

Write-Host "Disaster recovery procedure completed." -ForegroundColor Green
Write-Host "System Status: $($validationReport.OverallStatus)" -ForegroundColor $(if ($validationReport.OverallStatus -eq "Success") { "Green" } else { "Yellow" })
```

### 4.2 Data-Only Recovery

#### Scenario: System running but data corrupted
```powershell
# Data-only recovery without service restart
Write-Host "Performing data-only recovery..." -ForegroundColor Yellow

# Step 1: Create data checkpoint
$dataCheckpoint = Invoke-RestMethod -Uri "http://localhost:5099/api/recovery/data/checkpoint" -Method POST -Body @{
    CheckpointType = "Data"
    IncludeSnapshots = $true
    IncludeEventStore = $true
} -ContentType "application/json"

Write-Host "Data checkpoint created: $($dataCheckpoint.CheckpointId)"

# Step 2: Validate current data integrity
$integrityCheck = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/validation/data-integrity" -Method POST -Body @{
    CheckLevel = "Deep"
    RepairMinorIssues = $false
} -ContentType "application/json"

if ($integrityCheck.IntegrityScore -lt 80) {
    Write-Warning "Data integrity score: $($integrityCheck.IntegrityScore)%. Proceeding with recovery..."

    # Step 3: Execute data recovery
    $dataRecovery = Invoke-RestMethod -Uri "http://localhost:5099/api/recovery/data/restore" -Method POST -Body @{
        RecoveryStrategy = "LatestConsistentState"
        UseSnapshots = $true
        UseEventReplay = $true
        ValidateAfterRecovery = $true
    } -ContentType "application/json"

    Write-Host "Data recovery initiated: $($dataRecovery.OperationId)"

    # Monitor data recovery progress
    do {
        Start-Sleep 10
        $progress = Invoke-RestMethod -Uri "http://localhost:5099/api/recovery/data/$($dataRecovery.OperationId)/status"
        Write-Host "Data recovery progress: $($progress.ProgressPercent)% - $($progress.CurrentPhase)"
    } while ($progress.Status -eq "InProgress")

    Write-Host "Data recovery completed: $($progress.Status)"
}
```

## Snapshot Management

### 5.1 Manual Snapshot Creation

#### Create System Snapshot
```powershell
# Create comprehensive system snapshot
$snapshotRequest = @{
    SnapshotType = "System"
    IncludeAllGrains = $true
    Compression = $true
    ValidationLevel = "Full"
    RetentionDays = 30
    Description = "Manual system snapshot - $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
}

$snapshotResult = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/snapshots/create" -Method POST -Body ($snapshotRequest | ConvertTo-Json) -ContentType "application/json"

Write-Host "Snapshot created: $($snapshotResult.SnapshotId)"
Write-Host "Size: $($snapshotResult.SizeMB) MB"
Write-Host "Grain Count: $($snapshotResult.GrainCount)"
```

#### List Available Snapshots
```powershell
# Get list of available snapshots
$snapshots = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/snapshots" -Method GET

Write-Host "Available Snapshots:"
$snapshots | ForEach-Object {
    Write-Host "  ID: $($_.SnapshotId)"
    Write-Host "  Created: $($_.CreatedTimestamp)"
    Write-Host "  Type: $($_.SnapshotType)"
    Write-Host "  Size: $($_.SizeMB) MB"
    Write-Host "  Status: $($_.Status)"
    Write-Host "  ---"
}
```

### 5.2 Snapshot Restoration

#### Restore from Specific Snapshot
```powershell
# Restore system from specific snapshot
$snapshotId = "snapshot-id-to-restore"

$restoreRequest = @{
    SnapshotId = $snapshotId
    RestoreScope = "System"  # Options: System, Service, Grain
    ValidateBeforeRestore = $true
    BackupCurrentState = $true
}

$restoreResult = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/snapshots/$snapshotId/restore" -Method POST -Body ($restoreRequest | ConvertTo-Json) -ContentType "application/json"

Write-Host "Snapshot restoration started: $($restoreResult.OperationId)"

# Monitor restoration progress
do {
    Start-Sleep 10
    $progress = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/snapshots/restore/$($restoreResult.OperationId)/status"
    Write-Host "Restoration progress: $($progress.ProgressPercent)% - Phase: $($progress.CurrentPhase)"
} while ($progress.Status -eq "InProgress")

Write-Host "Snapshot restoration completed: $($progress.Status)"
```

## Recovery Audit and Compliance

### 6.1 Recovery Audit Trail

#### View Recovery History
```powershell
# Get recovery audit trail
$auditTrail = Invoke-RestMethod -Uri "http://localhost:5099/api/recovery/audit" -Method GET -Headers @{
    'X-Days-Back' = '30'
    'X-Include-Details' = 'true'
}

Write-Host "Recovery Operations (Last 30 days):"
$auditTrail | ForEach-Object {
    Write-Host "  Operation: $($_.OperationType)"
    Write-Host "  Started: $($_.StartTime)"
    Write-Host "  Duration: $($_.DurationMinutes) minutes"
    Write-Host "  Status: $($_.Status)"
    Write-Host "  Operator: $($_.OperatorId)"
    Write-Host "  ---"
}
```

#### Export Recovery Audit
```powershell
# Export audit trail for compliance
$exportRequest = @{
    StartDate = (Get-Date).AddDays(-90).ToString("yyyy-MM-ddTHH:mm:ssZ")
    EndDate = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")
    Format = "CSV"  # Options: CSV, JSON, XML
    IncludeDetails = $true
    IncludeMetrics = $true
}

$exportResult = Invoke-RestMethod -Uri "http://localhost:5099/api/recovery/audit/export" -Method POST -Body ($exportRequest | ConvertTo-Json) -ContentType "application/json"

# Download export file
Invoke-WebRequest -Uri $exportResult.DownloadUrl -OutFile "recovery-audit-export-$(Get-Date -Format 'yyyy-MM-dd').csv"

Write-Host "Recovery audit exported to: recovery-audit-export-$(Get-Date -Format 'yyyy-MM-dd').csv"
```

## Recovery Testing and Validation

### 7.1 Recovery Drill Procedures

#### Monthly Recovery Drill
```powershell
# Execute monthly recovery drill
Write-Host "=== MONTHLY RECOVERY DRILL ===" -ForegroundColor Cyan

$drillId = "drill-$(Get-Date -Format 'yyyy-MM-dd-HH-mm')"

# Step 1: Create test environment
$testEnv = @{
    DrillId = $drillId
    DrillType = "PointInTimeRecovery"
    TargetTimestamp = (Get-Date).AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ssZ")
    TestDataSet = "Synthetic"
    ValidationLevel = "Full"
}

$drillResult = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/recovery/drill" -Method POST -Body ($testEnv | ConvertTo-Json) -ContentType "application/json"

Write-Host "Recovery drill started: $($drillResult.DrillId)"
Write-Host "Estimated duration: $($drillResult.EstimatedMinutes) minutes"

# Step 2: Monitor drill progress
do {
    Start-Sleep 15
    $progress = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/recovery/drill/$($drillResult.DrillId)/status"
    Write-Host "Drill Progress: $($progress.ProgressPercent)% - $($progress.CurrentPhase)"
} while ($progress.Status -eq "InProgress")

# Step 3: Validate drill results
$drillReport = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/recovery/drill/$($drillResult.DrillId)/report"

Write-Host "=== DRILL RESULTS ===" -ForegroundColor Green
Write-Host "Overall Status: $($drillReport.OverallStatus)"
Write-Host "Duration: $($drillReport.ActualDurationMinutes) minutes"
Write-Host "Data Integrity: $($drillReport.DataIntegrityScore)%"
Write-Host "Performance Impact: $($drillReport.PerformanceImpactScore)%"

if ($drillReport.Issues.Count -gt 0) {
    Write-Warning "Issues identified during drill:"
    $drillReport.Issues | ForEach-Object { Write-Warning "  $_" }
}

# Save drill report
$drillReport | ConvertTo-Json -Depth 10 | Out-File "recovery-drill-report-$drillId.json"
```

### 7.2 Recovery Performance Benchmarking

#### Benchmark Recovery Operations
```powershell
# Benchmark different recovery scenarios
$benchmarkScenarios = @(
    @{ Name = "SingleGrainRecovery"; Type = "Grain"; Count = 1 },
    @{ Name = "SmallBatchRecovery"; Type = "Batch"; Count = 10 },
    @{ Name = "LargeBatchRecovery"; Type = "Batch"; Count = 100 },
    @{ Name = "PointInTimeRecovery"; Type = "PointInTime"; Count = 1 }
)

$benchmarkResults = @()

foreach ($scenario in $benchmarkScenarios) {
    Write-Host "Benchmarking: $($scenario.Name)"

    $benchmarkRequest = @{
        ScenarioName = $scenario.Name
        ScenarioType = $scenario.Type
        ItemCount = $scenario.Count
        MeasurePerformance = $true
        UseTestData = $true
    }

    $startTime = Get-Date
    $result = Invoke-RestMethod -Uri "http://localhost:5099/api/admin/recovery/benchmark" -Method POST -Body ($benchmarkRequest | ConvertTo-Json) -ContentType "application/json"
    $endTime = Get-Date

    $benchmarkResults += @{
        Scenario = $scenario.Name
        Duration = ($endTime - $startTime).TotalSeconds
        ItemsProcessed = $result.ItemsProcessed
        ThroughputPerSecond = $result.ItemsProcessed / ($endTime - $startTime).TotalSeconds
        MemoryUsedMB = $result.MemoryUsedMB
        Status = $result.Status
    }

    Write-Host "  Duration: $(($endTime - $startTime).TotalSeconds) seconds"
    Write-Host "  Throughput: $([math]::Round($result.ItemsProcessed / ($endTime - $startTime).TotalSeconds, 2)) items/sec"
}

# Generate benchmark report
$benchmarkReport = @{
    Timestamp = Get-Date
    Results = $benchmarkResults
    SystemConfiguration = @{
        MemoryGB = (Get-WmiObject -Class Win32_ComputerSystem).TotalPhysicalMemory / 1GB
        ProcessorCount = $env:NUMBER_OF_PROCESSORS
        OrleansVersion = "9.0"
    }
}

$benchmarkReport | ConvertTo-Json -Depth 10 | Out-File "recovery-benchmark-$(Get-Date -Format 'yyyy-MM-dd').json"
Write-Host "Benchmark results saved to recovery-benchmark-$(Get-Date -Format 'yyyy-MM-dd').json"
```

## Emergency Procedures

### 8.1 Critical Failure Response

#### Emergency Checklist
```powershell
# EMERGENCY RECOVERY CHECKLIST
Write-Host "=== EMERGENCY RECOVERY CHECKLIST ===" -ForegroundColor Red

$emergencySteps = @(
    "Assess system status and identify failure scope",
    "Stop all Orleans services to prevent data corruption",
    "Create emergency backup of current state",
    "Identify latest good state (snapshot or backup)",
    "Execute recovery procedure appropriate to failure type",
    "Validate recovery and system functionality",
    "Document incident and lessons learned",
    "Notify stakeholders of status"
)

foreach ($i in 0..($emergencySteps.Count - 1)) {
    Write-Host "[$($i + 1)] $($emergencySteps[$i])" -ForegroundColor Yellow
    $response = Read-Host "Press Enter when step completed, or 'skip' to skip"
    if ($response -eq "skip") {
        Write-Host "Step skipped" -ForegroundColor Magenta
    } else {
        Write-Host "Step completed" -ForegroundColor Green
    }
}

Write-Host "Emergency recovery checklist completed" -ForegroundColor Green
```

## Recovery Contact Information

### Escalation Procedures
- **Level 1 (Service Issues)**: Operations Team (ops@company.com)
- **Level 2 (Data Recovery)**: Database Team (dba@company.com)
- **Level 3 (System Recovery)**: Architecture Team (architecture@company.com)
- **Level 4 (Disaster Recovery)**: Executive Team (emergency@company.com)

### Recovery Support Contacts
- **Orleans Consultant**: Available 24/7 for critical issues
- **Database Specialist**: Available during business hours + on-call
- **System Administrator**: 24/7 availability for production issues

## Maintenance and Best Practices

### Daily Recovery Readiness
- Verify backup integrity and accessibility
- Test recovery endpoint availability
- Check snapshot creation and retention
- Review recovery audit trail for anomalies

### Weekly Recovery Maintenance
- Execute small-scale recovery drills
- Analyze recovery performance trends
- Update recovery procedures based on learnings
- Validate recovery infrastructure health

### Monthly Recovery Reviews
- Conduct comprehensive recovery testing
- Review and update disaster recovery plans
- Benchmark recovery performance
- Train team on new recovery procedures

---

**Document Version**: 1.0
**Last Updated**: September 2025
**Next Review**: Quarterly
**Owner**: Operations Team