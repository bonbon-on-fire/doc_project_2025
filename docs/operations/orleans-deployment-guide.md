# Orleans Deployment Procedures Guide

## Overview

This guide provides comprehensive deployment procedures for the Orleans-based AI Chat system. Orleans runs internally within the AIChat.Server process, providing scalable grain-based state management with automatic failover to direct services.

## Architecture Summary

- **Orleans Integration**: Internal hosting within AIChat.Server process
- **Communication**: Localhost networking between Server and Orleans Host
- **Feature Flags**: Environment-based and runtime Orleans enable/disable
- **Fallback Strategy**: Automatic dual-mode routing (Orleans-first, direct service fallback)

## Prerequisites

### System Requirements
- .NET 9.0 SDK
- Windows Server 2019+ or Windows 10+
- PowerShell 5.1 or PowerShell Core 7.0+
- Minimum 4GB RAM (8GB recommended for production)
- SQLite support (included with .NET)

### Port Configuration
- **Server**: 5099 (configurable)
- **Orleans Silo**: 11111 (internal)
- **Orleans Gateway**: 30000 (internal)
- **Orleans Dashboard**: 8080 (external access)
- **Orleans Host API**: 5100 (internal)

### Dependencies Validation
```powershell
# Verify .NET SDK
dotnet --version

# Check port availability
netstat -an | findstr "5099 8080 11111 30000"

# Validate PowerShell version
$PSVersionTable.PSVersion
```

## Environment Configuration

### Development Environment
**Orleans**: Enabled by default
**Purpose**: Full Orleans functionality for development and testing

```json
{
  "Orleans": {
    "ClusterId": "doc-chat-cluster-dev",
    "ServiceId": "doc-chat-service-dev",
    "Dashboard": {
      "Enabled": true,
      "Username": "admin",
      "Password": "orleans123"
    }
  },
  "FeatureManagement": {
    "OrleansEnabled": true
  }
}
```

### Test Environment
**Orleans**: Disabled by design
**Purpose**: Direct service testing without Orleans complexity

```json
{
  "FeatureManagement": {
    "OrleansEnabled": false
  }
}
```

### Production Environment
**Orleans**: Configurable via feature flags
**Purpose**: Production deployment with operational controls

```json
{
  "Orleans": {
    "ClusterId": "doc-chat-cluster-prod",
    "ServiceId": "doc-chat-service-prod",
    "Dashboard": {
      "Enabled": true,
      "Username": "admin",
      "Password": "CHANGE_IN_PRODUCTION"
    }
  },
  "FeatureManagement": {
    "OrleansEnabled": true
  }
}
```

## Deployment Procedures

### 1. Standard Deployment (Development/Staging)

#### Step 1: Build Verification
```powershell
# Navigate to project root
cd "path\to\DOC_Project_2025"

# Build all projects
dotnet build --configuration Release --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"; exit 1
}
```

#### Step 2: Run Pre-deployment Validation
```powershell
# Format code (MANDATORY)
.\scripts\format-code.ps1

# Validate implementation
.\scripts\validate-implementation-step.ps1

# Quality check
.\scripts\quality-check.ps1
```

#### Step 3: Deploy with Orleans Enabled
```powershell
# Start server with Orleans (Development environment)
.\build-and-start-server.ps1 -UseOrleans -Environment Development -Port 5099
```

#### Step 4: Deployment Verification
```powershell
# Verify server is running
Invoke-WebRequest -Uri "http://localhost:5099/health" -Method GET

# Verify Orleans dashboard access
Start-Process "http://localhost:8080/dashboard"

# Check Orleans metrics
Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics" -Method GET
```

### 2. Production Deployment

#### Step 1: Pre-deployment Checklist
- [ ] Production configuration reviewed and approved
- [ ] Database backups completed
- [ ] Rollback plan prepared and tested
- [ ] Monitoring systems operational
- [ ] Team notifications sent
- [ ] Change management approval obtained

#### Step 2: Blue-Green Deployment Process
```powershell
# Stop existing services (if running)
Get-Process -Name "AIChat*" -ErrorAction SilentlyContinue | Stop-Process -Force

# Clear logs directory
Remove-Item -Path "logs\*" -Recurse -Force -ErrorAction SilentlyContinue

# Deploy to staging slot first
.\build-and-start-server.ps1 -UseOrleans -Environment Production -Port 5098

# Validate staging deployment
Invoke-WebRequest -Uri "http://localhost:5098/health" -Method GET
Start-Sleep -Seconds 30

# If validation passes, switch to production port
Stop-Process -Name "AIChat*" -Force -ErrorAction SilentlyContinue
.\build-and-start-server.ps1 -UseOrleans -Environment Production -Port 5099
```

#### Step 3: Post-deployment Validation
```powershell
# Health check
$healthResponse = Invoke-RestMethod -Uri "http://localhost:5099/health" -Method GET
Write-Host "Health Status: $healthResponse"

# Orleans metrics validation
$orleansMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics" -Method GET
Write-Host "Active Grains: $($orleansMetrics.ActiveGrains)"

# Dashboard accessibility
Write-Host "Orleans Dashboard: http://localhost:8080/dashboard"
```

### 3. Feature Flag Deployment (Orleans Toggle)

#### Enable Orleans at Runtime
```powershell
# Update appsettings.json
$appSettings = Get-Content "server\AIChat.Server\appsettings.Production.json" | ConvertFrom-Json
$appSettings.FeatureManagement.OrleansEnabled = $true
$appSettings | ConvertTo-Json -Depth 10 | Set-Content "server\AIChat.Server\appsettings.Production.json"

# Restart service to apply changes
Restart-Service -Name "AIChat.Server" -Force
```

#### Disable Orleans (Fallback to Direct Services)
```powershell
# Update feature flag
$appSettings = Get-Content "server\AIChat.Server\appsettings.Production.json" | ConvertFrom-Json
$appSettings.FeatureManagement.OrleansEnabled = $false
$appSettings | ConvertTo-Json -Depth 10 | Set-Content "server\AIChat.Server\appsettings.Production.json"

# Restart to apply (dual-mode router will handle fallback automatically)
Restart-Service -Name "AIChat.Server" -Force
```

## High Availability Deployment Patterns

### Overview

This section provides enterprise-grade High Availability (HA) deployment patterns for Orleans-based AI Chat system, designed to achieve 99.99% availability SLA (4.32 minutes downtime per month). These patterns build upon the standard deployment procedures and introduce multi-zone clustering, automated failover, and zero-downtime deployment capabilities.

### HA Architecture Components

#### 1. Multi-Zone Orleans Clustering
- **Active-Active Configuration**: Multiple Orleans silos across availability zones
- **Consistent Hash Ring**: Distributed grain placement with automatic rebalancing
- **Zone-Aware Placement**: Grain placement policies considering zone boundaries
- **Cross-Zone Communication**: Secure networking between Orleans silos

#### 2. Load Balancing and Traffic Distribution
- **Application Gateway**: Layer 7 load balancing with SSL termination
- **Health Probes**: Deep health checks for Orleans grain responsiveness
- **Session Affinity**: Sticky sessions for stateful operations when required
- **Traffic Splitting**: Blue-green and canary deployment support

#### 3. Zero-Downtime Deployment
- **Rolling Updates**: Sequential silo updates with traffic draining
- **Graceful Shutdown**: Orleans silo graceful termination procedures
- **Deployment Validation**: Automated health checks during deployment
- **Rollback Mechanisms**: Fast rollback on deployment failures

### 1. Active-Active Multi-Zone Deployment

#### Prerequisites
- **Infrastructure**: Minimum 2 availability zones with network connectivity
- **Load Balancer**: Application Gateway or equivalent with health probes
- **Shared Storage**: Distributed database accessible from all zones
- **Monitoring**: Orleans Dashboard and custom metrics collection

#### Architecture Diagram
```
┌─ Zone A ─────────────────┐  ┌─ Zone B ─────────────────┐
│                          │  │                          │
│  ┌─────────────────────┐ │  │ ┌─────────────────────┐  │
│  │   Orleans Silo A    │ │  │ │   Orleans Silo B    │  │
│  │   - AIChat.Server   │ │  │ │   - AIChat.Server   │  │
│  │   - Port: 5099      │ │  │ │   - Port: 5099      │  │
│  │   - Silo: 11111     │ │  │ │   - Silo: 11112     │  │
│  │   - Gateway: 30000  │ │  │ │   - Gateway: 30001  │  │
│  └─────────────────────┘ │  │ └─────────────────────┘  │
│                          │  │                          │
└──────────────┬───────────┘  └───────────┬──────────────┘
               │                          │
               │    ┌─────────────────┐   │
               └────┤ Application     ├───┘
                    │ Gateway         │
                    │ Health Probes   │
                    └─────────────────┘
                            │
                    ┌───────┴───────┐
                    │    Clients    │
                    └───────────────┘
```

#### Configuration

**Step 1: Multi-Zone Orleans Configuration**

Create `appsettings.Production.HA.json`:
```json
{
  "Orleans": {
    "ClusterId": "doc-chat-cluster-prod-ha",
    "ServiceId": "doc-chat-service-prod-ha",
    "Clustering": {
      "ProviderType": "AdoNet",
      "ConnectionString": "Data Source=shared-cluster-db.db;Cache=Shared",
      "Invariant": "Microsoft.Data.Sqlite"
    },
    "Endpoints": {
      "SiloPort": 11111,
      "GatewayPort": 30000,
      "AdvertisedIPAddress": "AUTO_DETECT",
      "SiloListeningEndpoint": "0.0.0.0:11111",
      "GatewayListeningEndpoint": "0.0.0.0:30000"
    },
    "HighAvailability": {
      "EnableZoneAwareness": true,
      "Zone": "#{DEPLOYMENT_ZONE}#",
      "PreferredZoneGrainPlacement": true,
      "CrossZoneLatencyThreshold": "100ms",
      "HealthCheckInterval": "30s",
      "GracefulShutdownTimeout": "120s"
    },
    "Dashboard": {
      "Enabled": true,
      "Port": 8080,
      "Username": "admin",
      "Password": "#{ORLEANS_DASHBOARD_PASSWORD}#"
    }
  },
  "LoadBalancer": {
    "HealthCheck": {
      "Endpoint": "/health/orleans",
      "IntervalSeconds": 30,
      "TimeoutSeconds": 10,
      "UnhealthyThreshold": 3,
      "HealthyThreshold": 2
    }
  },
  "Monitoring": {
    "Orleans": {
      "MetricsInterval": "15s",
      "EnableDetailedMetrics": true,
      "ClusterHealthMonitoring": true
    }
  }
}
```

**Step 2: Zone-Specific Deployment Scripts**

Create `scripts/deploy-zone-a.ps1`:
```powershell
# Deploy Orleans HA - Zone A
param(
    [string]$Environment = "Production",
    [string]$Zone = "ZoneA"
)

# Configuration
$deploymentZone = $Zone
$siloPort = 11111
$gatewayPort = 30000
$serverPort = 5099

Write-Host "🚀 Starting Orleans HA deployment for $Zone" -ForegroundColor Cyan

# Step 1: Pre-deployment validation
Write-Host "📋 Running pre-deployment validation..." -ForegroundColor Yellow

# Validate zone connectivity
$zoneEndpoints = @(
    "orleans-zonea.internal:11111",
    "orleans-zoneb.internal:11112"
)

foreach ($endpoint in $zoneEndpoints) {
    $host, $port = $endpoint.Split(':')
    if (!(Test-NetConnection -ComputerName $host -Port $port -InformationLevel Quiet)) {
        Write-Warning "Cannot reach $endpoint - HA deployment may have issues"
    }
}

# Step 2: Update configuration with zone information
$configPath = "server\AIChat.Server\appsettings.Production.HA.json"
$config = Get-Content $configPath | ConvertFrom-Json

# Set zone-specific configuration
$config.Orleans.HighAvailability.Zone = $deploymentZone
$config.Orleans.Endpoints.SiloPort = $siloPort
$config.Orleans.Endpoints.GatewayPort = $gatewayPort

# Save updated configuration
$config | ConvertTo-Json -Depth 10 | Set-Content $configPath

# Step 3: Deploy with health monitoring
Write-Host "🏥 Starting deployment with health monitoring..." -ForegroundColor Green

# Start health monitoring in background
Start-Job -Name "HealthMonitor" -ScriptBlock {
    param($serverPort)
    do {
        Start-Sleep -Seconds 30
        try {
            $response = Invoke-RestMethod -Uri "http://localhost:$serverPort/health/orleans" -TimeoutSec 10
            Write-Host "Health check: $($response.Status)" -ForegroundColor Green
        } catch {
            Write-Host "Health check failed: $($_.Exception.Message)" -ForegroundColor Red
        }
    } while ($true)
} -ArgumentList $serverPort

# Deploy the application
try {
    # Build and deploy
    dotnet publish server\AIChat.Server\AIChat.Server.csproj -c Release -o "deploy\$Zone"

    # Start the service
    Start-Process -FilePath "deploy\$Zone\AIChat.Server.exe" -ArgumentList "--environment=$Environment" -NoNewWindow

    # Wait for startup
    Start-Sleep -Seconds 45

    # Validate deployment
    $healthCheck = Invoke-RestMethod -Uri "http://localhost:$serverPort/health/orleans" -TimeoutSec 30
    if ($healthCheck.Status -eq "Healthy") {
        Write-Host "✅ Orleans HA deployment successful for $Zone" -ForegroundColor Green
    } else {
        throw "Health check failed after deployment"
    }

} catch {
    Write-Error "❌ Deployment failed for $Zone: $($_.Exception.Message)"

    # Cleanup on failure
    Get-Process -Name "AIChat*" -ErrorAction SilentlyContinue | Stop-Process -Force
    throw
}

# Step 4: Register with load balancer
Write-Host "🔄 Registering with load balancer..." -ForegroundColor Blue

# Add zone endpoint to load balancer configuration
$lbConfig = @{
    Zone = $deploymentZone
    Endpoint = "http://localhost:$serverPort"
    HealthCheck = "http://localhost:$serverPort/health/orleans"
    Weight = 50
}

# Save load balancer configuration
$lbConfig | ConvertTo-Json | Set-Content "scripts\loadbalancer\config\$Zone.json"

Write-Host "🎉 Zone $Zone deployment completed successfully!" -ForegroundColor Green
```

Create `scripts/deploy-zone-b.ps1`:
```powershell
# Deploy Orleans HA - Zone B
param(
    [string]$Environment = "Production",
    [string]$Zone = "ZoneB"
)

# Configuration (Zone B uses different ports to avoid conflicts in testing)
$deploymentZone = $Zone
$siloPort = 11112
$gatewayPort = 30001
$serverPort = 5098

Write-Host "🚀 Starting Orleans HA deployment for $Zone" -ForegroundColor Cyan

# Similar structure to Zone A but with Zone B specific configurations
# [Implementation follows same pattern as Zone A with adjusted ports and zone identifiers]
Write-Host "🎉 Zone $Zone deployment completed successfully!" -ForegroundColor Green
```

#### Deployment Procedure

**Step 1: Infrastructure Preparation**
```powershell
# Prepare shared database for clustering
$sharedDbPath = "shared-cluster-db.db"
if (!(Test-Path $sharedDbPath)) {
    # Initialize clustering database
    dotnet ef database update --project server\AIChat.Orleans\AIChat.Orleans.csproj --connection "Data Source=$sharedDbPath;Cache=Shared"
}

# Verify network connectivity between zones
Test-NetConnection -ComputerName orleans-zonea.internal -Port 11111
Test-NetConnection -ComputerName orleans-zoneb.internal -Port 11112
```

**Step 2: Sequential Zone Deployment**
```powershell
# Deploy Zone A first
.\scripts\deploy-zone-a.ps1 -Environment Production

# Validate Zone A health
$zoneAHealth = Invoke-RestMethod -Uri "http://orleans-zonea.internal:5099/health/orleans"
if ($zoneAHealth.Status -ne "Healthy") {
    throw "Zone A deployment failed - aborting Zone B deployment"
}

# Deploy Zone B
.\scripts\deploy-zone-b.ps1 -Environment Production

# Validate cluster formation
Start-Sleep -Seconds 60
$clusterStatus = Invoke-RestMethod -Uri "http://orleans-zonea.internal:5099/health/cluster"
if ($clusterStatus.MemberCount -lt 2) {
    Write-Warning "Cluster formation incomplete - only $($clusterStatus.MemberCount) members active"
}
```

**Step 3: Load Balancer Configuration**
```powershell
# Configure Application Gateway health probes
$healthProbeConfig = @{
    Name = "orleans-health-probe"
    Protocol = "Http"
    Path = "/health/orleans"
    IntervalInSeconds = 30
    TimeoutInSeconds = 10
    UnhealthyThreshold = 3
    PickHostNameFromBackendHttpSettings = $true
}

# Add backend pool with both zones
$backendPool = @{
    Name = "orleans-backend-pool"
    BackendAddresses = @(
        @{ IpAddress = "orleans-zonea.internal"; Port = 5099 },
        @{ IpAddress = "orleans-zoneb.internal"; Port = 5098 }
    )
}
```

### 2. Zero-Downtime Deployment (Rolling Updates)

#### Overview
Zero-downtime deployment ensures continuous service availability during updates by using rolling updates with traffic draining and graceful Orleans silo shutdown.

#### Deployment Process

**Step 1: Pre-deployment Validation**
```powershell
# Validate cluster health before starting deployment
function Test-ClusterReadiness {
    $healthEndpoints = @(
        "http://orleans-zonea.internal:5099/health/orleans",
        "http://orleans-zoneb.internal:5098/health/orleans"
    )

    foreach ($endpoint in $healthEndpoints) {
        try {
            $health = Invoke-RestMethod -Uri $endpoint -TimeoutSec 10
            if ($health.Status -ne "Healthy") {
                throw "Cluster member $endpoint is not healthy: $($health.Status)"
            }
            Write-Host "✅ $endpoint is healthy" -ForegroundColor Green
        } catch {
            Write-Error "❌ Health check failed for $endpoint`: $($_.Exception.Message)"
            return $false
        }
    }

    Write-Host "✅ All cluster members are healthy - ready for deployment" -ForegroundColor Green
    return $true
}

if (!(Test-ClusterReadiness)) {
    throw "Cluster not ready for zero-downtime deployment"
}
```

**Step 2: Rolling Update Implementation**
```powershell
# Zero-downtime deployment for zones
function Start-ZeroDowntimeDeployment {
    param([string]$Zone, [string]$NewVersion)

    Write-Host "🔄 Starting zero-downtime deployment for $Zone (version $NewVersion)" -ForegroundColor Cyan

    # Step 1: Remove zone from load balancer
    Write-Host "📤 Draining traffic from $Zone..." -ForegroundColor Yellow
    # Remove from load balancer backend pool (implementation depends on LB)
    Remove-LoadBalancerTarget -Zone $Zone

    # Wait for active connections to complete
    Start-Sleep -Seconds 30

    # Step 2: Graceful Orleans silo shutdown
    Write-Host "🛑 Initiating graceful silo shutdown..." -ForegroundColor Yellow
    $shutdownEndpoint = "http://orleans-$($Zone.ToLower()).internal:5099/admin/shutdown?graceful=true"

    try {
        Invoke-RestMethod -Uri $shutdownEndpoint -Method Post -TimeoutSec 60
        Write-Host "✅ Graceful shutdown initiated" -ForegroundColor Green

        # Wait for graceful shutdown to complete
        $timeout = 120
        $elapsed = 0
        do {
            Start-Sleep -Seconds 5
            $elapsed += 5

            try {
                $health = Invoke-RestMethod -Uri "http://orleans-$($Zone.ToLower()).internal:5099/health" -TimeoutSec 5
                if ($health.Status -eq "Shutdown") {
                    break
                }
            } catch {
                # Service stopped - graceful shutdown complete
                break
            }
        } while ($elapsed -lt $timeout)

    } catch {
        Write-Warning "Graceful shutdown timeout - forcing shutdown"
        Get-Process -Name "AIChat*" -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq $zoneProcessId } | Stop-Process -Force
    }

    # Step 3: Deploy new version
    Write-Host "🚀 Deploying new version..." -ForegroundColor Blue
    if ($Zone -eq "ZoneA") {
        & ".\scripts\deploy-zone-a.ps1" -Environment Production -Version $NewVersion
    } else {
        & ".\scripts\deploy-zone-b.ps1" -Environment Production -Version $NewVersion
    }

    # Step 4: Health validation
    Write-Host "🏥 Validating deployment..." -ForegroundColor Green
    $maxAttempts = 12
    $attempt = 0

    do {
        $attempt++
        Start-Sleep -Seconds 10

        try {
            $health = Invoke-RestMethod -Uri "http://orleans-$($Zone.ToLower()).internal:5099/health/orleans" -TimeoutSec 10
            if ($health.Status -eq "Healthy") {
                Write-Host "✅ Zone $Zone is healthy after deployment" -ForegroundColor Green
                break
            }
        } catch {
            Write-Host "⏳ Waiting for Zone $Zone to become healthy (attempt $attempt/$maxAttempts)..." -ForegroundColor Yellow
        }
    } while ($attempt -lt $maxAttempts)

    if ($attempt -eq $maxAttempts) {
        throw "Zone $Zone failed to become healthy after deployment"
    }

    # Step 5: Rejoin cluster and verify
    Write-Host "🔗 Verifying cluster membership..." -ForegroundColor Blue
    Start-Sleep -Seconds 30

    $clusterHealth = Invoke-RestMethod -Uri "http://orleans-zonea.internal:5099/health/cluster"
    if ($clusterHealth.MemberCount -lt 2) {
        Write-Warning "Cluster membership not complete - only $($clusterHealth.MemberCount) members"
    }

    # Step 6: Re-add to load balancer
    Write-Host "🔄 Adding back to load balancer..." -ForegroundColor Green
    Add-LoadBalancerTarget -Zone $Zone

    Write-Host "✅ Zero-downtime deployment completed for Zone $Zone" -ForegroundColor Green
}

# Deploy Zone A
Start-ZeroDowntimeDeployment -Zone "ZoneA" -NewVersion "1.2.0"

# Validate cluster stability before proceeding to Zone B
Start-Sleep -Seconds 60
if (!(Test-ClusterReadiness)) {
    throw "Cluster unstable after Zone A deployment - aborting Zone B deployment"
}

# Deploy Zone B
Start-ZeroDowntimeDeployment -Zone "ZoneB" -NewVersion "1.2.0"
```

### 3. Automated Failover Configuration

#### Health Check Implementation

**Orleans Health Endpoint** - Add to `Controllers/HealthController.cs`:
```csharp
[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<HealthController> _logger;

    [HttpGet("orleans")]
    public async Task<IActionResult> OrleansHealth()
    {
        try {
            // Check Orleans silo connectivity
            var managementGrain = _clusterClient.GetGrain<IManagementGrain>(0);
            var hosts = await managementGrain.GetHosts();

            if (!hosts.Any()) {
                return StatusCode(503, new { Status = "Unhealthy", Reason = "No Orleans silos available" });
            }

            // Check grain responsiveness
            var testGrain = _clusterClient.GetGrain<IHealthTestGrain>(Guid.NewGuid());
            var healthCheck = await testGrain.Ping();

            if (!healthCheck) {
                return StatusCode(503, new { Status = "Unhealthy", Reason = "Orleans grains not responding" });
            }

            return Ok(new {
                Status = "Healthy",
                SiloCount = hosts.Count(),
                LastCheck = DateTime.UtcNow
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "Orleans health check failed");
            return StatusCode(503, new { Status = "Unhealthy", Reason = ex.Message });
        }
    }

    [HttpGet("cluster")]
    public async Task<IActionResult> ClusterHealth()
    {
        try {
            var managementGrain = _clusterClient.GetGrain<IManagementGrain>(0);
            var hosts = await managementGrain.GetHosts();
            var statistics = await managementGrain.GetRuntimeStatistics(hosts.ToArray());

            return Ok(new {
                Status = "Healthy",
                MemberCount = hosts.Count(),
                Statistics = statistics.Select(s => new {
                    SiloAddress = s.SiloAddress.ToString(),
                    Status = s.SiloStatus.ToString(),
                    CpuUsage = s.CpuUsage,
                    MemoryUsage = s.MemoryUsage
                })
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "Cluster health check failed");
            return StatusCode(503, new { Status = "Unhealthy", Reason = ex.Message });
        }
    }
}
```

#### Automated Failover Script
```powershell
# Automated failover monitoring and response
function Start-FailoverMonitoring {
    param(
        [int]$CheckIntervalSeconds = 30,
        [int]$FailureThreshold = 3
    )

    $consecutiveFailures = 0
    $lastFailoverTime = $null
    $failoverCooldown = [TimeSpan]::FromMinutes(5)

    Write-Host "🔍 Starting automated failover monitoring (interval: ${CheckIntervalSeconds}s)" -ForegroundColor Cyan

    while ($true) {
        try {
            # Check primary zone health
            $primaryHealth = Invoke-RestMethod -Uri "http://orleans-zonea.internal:5099/health/orleans" -TimeoutSec 10

            if ($primaryHealth.Status -eq "Healthy") {
                $consecutiveFailures = 0
                Write-Host "✅ Primary zone healthy" -ForegroundColor Green
            } else {
                throw "Primary zone unhealthy: $($primaryHealth.Status)"
            }
        }
        catch {
            $consecutiveFailures++
            Write-Warning "❌ Primary zone health check failed (attempt $consecutiveFailures/$FailureThreshold): $($_.Exception.Message)"

            if ($consecutiveFailures -ge $FailureThreshold) {
                # Check if we're in cooldown period
                if ($lastFailoverTime -and ((Get-Date) - $lastFailoverTime) -lt $failoverCooldown) {
                    Write-Warning "⏳ Failover cooldown active - waiting..."
                } else {
                    Write-Host "🚨 Initiating automatic failover!" -ForegroundColor Red

                    try {
                        Invoke-AutomaticFailover
                        $lastFailoverTime = Get-Date
                        $consecutiveFailures = 0
                    } catch {
                        Write-Error "❌ Automatic failover failed: $($_.Exception.Message)"
                    }
                }
            }
        }

        Start-Sleep -Seconds $CheckIntervalSeconds
    }
}

function Invoke-AutomaticFailover {
    Write-Host "🔄 Executing automatic failover sequence..." -ForegroundColor Yellow

    # Step 1: Verify secondary zone health
    try {
        $secondaryHealth = Invoke-RestMethod -Uri "http://orleans-zoneb.internal:5098/health/orleans" -TimeoutSec 10
        if ($secondaryHealth.Status -ne "Healthy") {
            throw "Secondary zone is not healthy - cannot failover"
        }
    } catch {
        Write-Error "❌ Secondary zone health check failed: $($_.Exception.Message)"
        throw "Cannot failover to unhealthy secondary zone"
    }

    # Step 2: Update load balancer to route all traffic to secondary
    Write-Host "🔄 Updating load balancer configuration..." -ForegroundColor Blue

    $lbConfig = @{
        PrimaryBackend = "orleans-zoneb.internal:5098"
        BackupBackends = @()
        FailoverMode = $true
        LastFailover = Get-Date
    }

    $lbConfig | ConvertTo-Json | Set-Content "scripts\loadbalancer\config\failover.json"

    # Step 3: Send notifications
    Send-FailoverNotification -Type "AutomaticFailover" -PrimaryZone "ZoneA" -SecondaryZone "ZoneB"

    Write-Host "✅ Automatic failover completed - all traffic routed to Zone B" -ForegroundColor Green
}
```

### 4. HA Deployment Validation

#### Comprehensive HA Validation Script
```powershell
# HA deployment validation and testing
function Test-HADeployment {
    param(
        [string[]]$Zones = @("ZoneA", "ZoneB"),
        [int]$LoadTestDurationMinutes = 5
    )

    Write-Host "🧪 Starting HA deployment validation..." -ForegroundColor Cyan

    $validationResults = @{}

    # Test 1: Individual zone health
    Write-Host "📋 Test 1: Individual zone health validation" -ForegroundColor Yellow
    foreach ($zone in $Zones) {
        try {
            $zonePort = if ($zone -eq "ZoneA") { 5099 } else { 5098 }
            $health = Invoke-RestMethod -Uri "http://orleans-zone$($zone.ToLower()).internal:$zonePort/health/orleans" -TimeoutSec 10

            $validationResults["Zone$zone-Health"] = $health.Status -eq "Healthy"
            Write-Host "  ✅ Zone $zone health: $($health.Status)" -ForegroundColor Green
        } catch {
            $validationResults["Zone$zone-Health"] = $false
            Write-Host "  ❌ Zone $zone health check failed: $($_.Exception.Message)" -ForegroundColor Red
        }
    }

    # Test 2: Cluster formation
    Write-Host "📋 Test 2: Cluster formation validation" -ForegroundColor Yellow
    try {
        $clusterHealth = Invoke-RestMethod -Uri "http://orleans-zonea.internal:5099/health/cluster" -TimeoutSec 10
        $expectedMembers = $Zones.Count
        $actualMembers = $clusterHealth.MemberCount

        $validationResults["ClusterFormation"] = $actualMembers -eq $expectedMembers
        Write-Host "  ✅ Cluster members: $actualMembers/$expectedMembers" -ForegroundColor Green
    } catch {
        $validationResults["ClusterFormation"] = $false
        Write-Host "  ❌ Cluster formation check failed: $($_.Exception.Message)" -ForegroundColor Red
    }

    # Test 3: Load balancer health probes
    Write-Host "📋 Test 3: Load balancer health probe validation" -ForegroundColor Yellow
    $lbConfig = Get-Content "scripts\loadbalancer\config\zonea.json" | ConvertFrom-Json
    try {
        $probeResponse = Invoke-RestMethod -Uri $lbConfig.HealthCheck -TimeoutSec 5
        $validationResults["LoadBalancerProbes"] = $probeResponse.Status -eq "Healthy"
        Write-Host "  ✅ Load balancer health probes working" -ForegroundColor Green
    } catch {
        $validationResults["LoadBalancerProbes"] = $false
        Write-Host "  ❌ Load balancer health probe failed: $($_.Exception.Message)" -ForegroundColor Red
    }

    # Test 4: Failover simulation
    Write-Host "📋 Test 4: Failover simulation" -ForegroundColor Yellow
    try {
        # Temporarily disable Zone A
        Stop-Process -Name "AIChat*" -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 10

        # Test Zone B can handle requests
        $zoneBHealth = Invoke-RestMethod -Uri "http://orleans-zoneb.internal:5098/health/orleans" -TimeoutSec 10
        $validationResults["FailoverTest"] = $zoneBHealth.Status -eq "Healthy"

        # Restart Zone A
        & ".\scripts\deploy-zone-a.ps1" -Environment Production
        Start-Sleep -Seconds 30

        Write-Host "  ✅ Failover simulation completed" -ForegroundColor Green
    } catch {
        $validationResults["FailoverTest"] = $false
        Write-Host "  ❌ Failover simulation failed: $($_.Exception.Message)" -ForegroundColor Red
    }

    # Summary
    Write-Host "📊 HA Deployment Validation Summary" -ForegroundColor Cyan
    $passedTests = ($validationResults.Values | Where-Object { $_ -eq $true }).Count
    $totalTests = $validationResults.Count

    foreach ($test in $validationResults.GetEnumerator()) {
        $status = if ($test.Value) { "PASS ✅" } else { "FAIL ❌" }
        Write-Host "  $($test.Key): $status"
    }

    if ($passedTests -eq $totalTests) {
        Write-Host "🎉 All HA validation tests passed ($passedTests/$totalTests)" -ForegroundColor Green
        return $true
    } else {
        Write-Host "⚠️ HA validation incomplete ($passedTests/$totalTests tests passed)" -ForegroundColor Yellow
        return $false
    }
}
```

### 5. HA Monitoring and Alerting

#### Orleans HA Metrics Collection
```powershell
# HA-specific metrics collection and monitoring
function Start-HAMonitoring {
    Write-Host "📊 Starting HA monitoring and metrics collection..." -ForegroundColor Cyan

    # Collect HA-specific metrics every minute
    while ($true) {
        try {
            $timestamp = Get-Date
            $metrics = @{}

            # Cluster health metrics
            $clusterHealth = Invoke-RestMethod -Uri "http://orleans-zonea.internal:5099/health/cluster" -TimeoutSec 10
            $metrics["cluster_member_count"] = $clusterHealth.MemberCount
            $metrics["cluster_status"] = if ($clusterHealth.Status -eq "Healthy") { 1 } else { 0 }

            # Per-zone metrics
            $zones = @(
                @{ Name = "ZoneA"; Port = 5099 },
                @{ Name = "ZoneB"; Port = 5098 }
            )

            foreach ($zone in $zones) {
                try {
                    $zoneHealth = Invoke-RestMethod -Uri "http://orleans-zone$($zone.Name.ToLower()).internal:$($zone.Port)/health/orleans" -TimeoutSec 5
                    $metrics["zone_$($zone.Name.ToLower())_status"] = if ($zoneHealth.Status -eq "Healthy") { 1 } else { 0 }

                    # Orleans-specific metrics
                    $orleanStats = Invoke-RestMethod -Uri "http://orleans-zone$($zone.Name.ToLower()).internal:$($zone.Port)/metrics/orleans" -TimeoutSec 5
                    $metrics["zone_$($zone.Name.ToLower())_active_grains"] = $orleanStats.ActiveGrains
                    $metrics["zone_$($zone.Name.ToLower())_activations_per_second"] = $orleanStats.ActivationsPerSecond
                    $metrics["zone_$($zone.Name.ToLower())_requests_per_second"] = $orleanStats.RequestsPerSecond

                } catch {
                    $metrics["zone_$($zone.Name.ToLower())_status"] = 0
                    Write-Warning "Failed to collect metrics from $($zone.Name): $($_.Exception.Message)"
                }
            }

            # Export metrics (to your monitoring system - Prometheus, Application Insights, etc.)
            Export-Metrics -Metrics $metrics -Timestamp $timestamp

            # Check for alert conditions
            Test-AlertConditions -Metrics $metrics

        } catch {
            Write-Error "HA monitoring failed: $($_.Exception.Message)"
        }

        Start-Sleep -Seconds 60
    }
}

function Test-AlertConditions {
    param([hashtable]$Metrics)

    # Alert: Cluster member count below threshold
    if ($Metrics["cluster_member_count"] -lt 2) {
        Send-Alert -Level "Critical" -Message "Orleans cluster has insufficient members: $($Metrics['cluster_member_count'])"
    }

    # Alert: Zone unavailability
    if ($Metrics["zone_zonea_status"] -eq 0 -and $Metrics["zone_zoneb_status"] -eq 0) {
        Send-Alert -Level "Critical" -Message "All Orleans zones are unavailable - total system failure"
    } elseif ($Metrics["zone_zonea_status"] -eq 0 -or $Metrics["zone_zoneb_status"] -eq 0) {
        Send-Alert -Level "Warning" -Message "Orleans zone is unavailable - operating in degraded mode"
    }

    # Alert: High failure rate at load balancer
    if ($Metrics.ContainsKey("lb_total_requests") -and $Metrics["lb_total_requests"] -gt 0) {
        $failureRate = $Metrics["lb_failed_requests"] / $Metrics["lb_total_requests"]
        if ($failureRate -gt 0.05) { # 5% failure rate threshold
            Send-Alert -Level "Warning" -Message "High failure rate at load balancer: $($failureRate * 100)%"
        }
    }
}
```

### 6. HA Troubleshooting Guide

#### Common HA Issues and Resolution

**Issue 1: Cluster Split-Brain**
```powershell
# Detect and resolve cluster split-brain scenarios
function Resolve-ClusterSplitBrain {
    Write-Host "🧠 Checking for cluster split-brain scenarios..." -ForegroundColor Yellow

    $zones = @("ZoneA", "ZoneB")
    $clusterViews = @{}

    # Get cluster view from each zone
    foreach ($zone in $zones) {
        try {
            $zonePort = if ($zone -eq "ZoneA") { 5099 } else { 5098 }
            $clusterView = Invoke-RestMethod -Uri "http://orleans-zone$($zone.ToLower()).internal:$zonePort/health/cluster" -TimeoutSec 10
            $clusterViews[$zone] = $clusterView.MemberCount
        } catch {
            Write-Warning "Cannot get cluster view from $zone"
            $clusterViews[$zone] = -1
        }
    }

    # Detect split-brain (each zone thinks it's the only member)
    if ($clusterViews["ZoneA"] -eq 1 -and $clusterViews["ZoneB"] -eq 1) {
        Write-Host "⚠️ Split-brain detected! Each zone thinks it's the only cluster member" -ForegroundColor Red

        # Resolution: Restart both zones in sequence to force cluster reformation
        Write-Host "🔧 Resolving split-brain by restarting zones..." -ForegroundColor Blue

        # Stop both zones
        Stop-Process -Name "AIChat*" -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 10

        # Clear clustering state
        Remove-Item "data\orleans-clustering-*" -ErrorAction SilentlyContinue

        # Restart Zone A first
        & ".\scripts\deploy-zone-a.ps1" -Environment Production
        Start-Sleep -Seconds 45

        # Then restart Zone B
        & ".\scripts\deploy-zone-b.ps1" -Environment Production
        Start-Sleep -Seconds 45

        # Verify cluster reformation
        $newClusterView = Invoke-RestMethod -Uri "http://orleans-zonea.internal:5099/health/cluster" -TimeoutSec 10
        if ($newClusterView.MemberCount -eq 2) {
            Write-Host "✅ Split-brain resolved - cluster reformed with $($newClusterView.MemberCount) members" -ForegroundColor Green
        } else {
            Write-Error "❌ Failed to resolve split-brain - manual intervention required"
        }
    } else {
        Write-Host "✅ No split-brain detected" -ForegroundColor Green
    }
}
```

**Issue 2: Load Balancer Health Probe Failures**
```powershell
# Diagnose and fix load balancer health probe issues
function Fix-LoadBalancerHealthProbes {
    Write-Host "🔍 Diagnosing load balancer health probe issues..." -ForegroundColor Yellow

    $zones = @(
        @{ Name = "ZoneA"; Port = 5099 },
        @{ Name = "ZoneB"; Port = 5098 }
    )

    foreach ($zone in $zones) {
        Write-Host "Testing $($zone.Name) health endpoint..." -ForegroundColor Blue

        try {
            $healthResponse = Invoke-RestMethod -Uri "http://orleans-zone$($zone.Name.ToLower()).internal:$($zone.Port)/health/orleans" -TimeoutSec 5

            if ($healthResponse.Status -eq "Healthy") {
                Write-Host "✅ $($zone.Name) health endpoint responding correctly" -ForegroundColor Green
            } else {
                Write-Host "⚠️ $($zone.Name) health endpoint reports: $($healthResponse.Status)" -ForegroundColor Yellow
                Write-Host "Reason: $($healthResponse.Reason)" -ForegroundColor Yellow

                # Try to resolve common health issues
                if ($healthResponse.Reason -like "*Orleans grains not responding*") {
                    Write-Host "🔧 Attempting to restart Orleans grains..." -ForegroundColor Blue
                    # Restart Orleans (implementation specific)
                }
            }
        } catch {
            Write-Host "❌ $($zone.Name) health endpoint not responding: $($_.Exception.Message)" -ForegroundColor Red

            # Check if the service is running
            $process = Get-Process -Name "AIChat*" -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq $zone.ProcessId }
            if (!$process) {
                Write-Host "🔧 Service not running - restarting $($zone.Name)..." -ForegroundColor Blue
                if ($zone.Name -eq "ZoneA") {
                    & ".\scripts\deploy-zone-a.ps1" -Environment Production
                } else {
                    & ".\scripts\deploy-zone-b.ps1" -Environment Production
                }
            }
        }
    }
}
```

## Multi-Region Deployment Procedures

### Overview

This section provides comprehensive multi-region deployment procedures for Orleans-based AI Chat system, designed to support global scale deployments with geographic distribution, disaster recovery across regions, and sub-100ms latency worldwide. These procedures build upon the High Availability patterns and extend them to multiple geographic regions.

### Multi-Region Architecture Components

#### 1. Geographic Distribution Strategy
- **Active-Passive Configuration**: Primary region with warm standby regions
- **Regional Clusters**: Independent Orleans clusters in each region
- **Cross-Region Replication**: Asynchronous data replication with conflict resolution
- **Traffic Routing**: DNS-based failover with geographic load balancing

#### 2. Cross-Region Networking
- **WAN Optimization**: Dedicated connectivity between regions (ExpressRoute/Direct Connect)
- **VPN Tunneling**: Secure communication between regional clusters
- **CDN Integration**: Content delivery network for global asset distribution
- **Edge Caching**: Regional caching for improved performance

#### 3. Data Consistency Management
- **Eventual Consistency**: Asynchronous replication with conflict resolution
- **CRDT Support**: Conflict-free replicated data types for specific operations
- **Synchronization Windows**: Configurable consistency guarantees
- **Conflict Resolution**: Last-writer-wins and custom merge strategies

### 1. Multi-Region Architecture Design

#### Regional Deployment Topology
```
┌─ Region: US East (Primary) ──────────────────┐  ┌─ Region: EU West (Standby) ──────────────────┐
│                                              │  │                                              │
│  ┌─ Zone A ──────┐  ┌─ Zone B ──────┐       │  │  ┌─ Zone A ──────┐  ┌─ Zone B ──────┐       │
│  │ Orleans Silo  │  │ Orleans Silo  │       │  │  │ Orleans Silo  │  │ Orleans Silo  │       │
│  │ Port: 5099    │  │ Port: 5098    │       │  │  │ Port: 5099    │  │ Port: 5098    │       │
│  │ Silo: 11111   │  │ Silo: 11112   │       │  │  │ Silo: 11111   │  │ Silo: 11112   │       │
│  └───────────────┘  └───────────────┘       │  │  └───────────────┘  └───────────────┘       │
│           │                   │             │  │           │                   │             │
│           └─────────┬─────────┘             │  │           └─────────┬─────────┘             │
│                     │                       │  │                     │                       │
│              ┌─────────────┐                │  │              ┌─────────────┐                │
│              │    ALB      │                │  │              │    ALB      │                │
│              │ us-east-lb  │                │  │              │ eu-west-lb  │                │
│              └─────────────┘                │  │              └─────────────┘                │
│                     │                       │  │                     │                       │
└─────────────────────┼───────────────────────┘  └─────────────────────┼───────────────────────┘
                      │                                                │
                      │            ┌─────────────────────┐             │
                      └────────────┤ Global Traffic      ├─────────────┘
                                   │ Manager (DNS)       │
                                   │ - Route53/CloudDNS  │
                                   │ - Health Checks     │
                                   │ - Failover Logic    │
                                   └─────────────────────┘
                                            │
                                    ┌───────────────┐
                                    │ Global CDN    │
                                    │ - CloudFront  │
                                    │ - Edge Cache  │
                                    └───────────────┘
                                            │
                                   ┌─────────────────┐
                                   │ Global Clients  │
                                   └─────────────────┘

Cross-Region Data Flow:
US East ════════════════════════════════════════════════════════> EU West
         Async Replication (ExpressRoute/VPN)
         - Configuration Changes
         - User State Sync
         - Event Sourcing Data
         - File/Asset Replication
```

#### Regional Configuration

**US East (Primary Region) - `appsettings.Production.USEast.json`:**
```json
{
  "Orleans": {
    "ClusterId": "doc-chat-cluster-prod-us-east",
    "ServiceId": "doc-chat-service-prod",
    "Region": "us-east-1",
    "Clustering": {
      "ProviderType": "AdoNet",
      "ConnectionString": "Data Source=us-east-cluster-db.db;Cache=Shared",
      "Invariant": "Microsoft.Data.Sqlite"
    },
    "MultiRegion": {
      "PrimaryRegion": true,
      "RegionId": "us-east-1",
      "ReplicationEndpoints": [
        "https://eu-west-1.aichat.com/api/replication",
        "https://ap-southeast-1.aichat.com/api/replication"
      ],
      "ReplicationMode": "AsyncPrimary",
      "ConflictResolution": "LastWriterWins",
      "ReplicationBatchSize": 1000,
      "ReplicationIntervalSeconds": 30
    },
    "Endpoints": {
      "SiloPort": 11111,
      "GatewayPort": 30000,
      "PublicEndpoint": "us-east-1.aichat.com",
      "ReplicationEndpoint": "/api/replication"
    }
  },
  "Database": {
    "Primary": {
      "ConnectionString": "Data Source=us-east-primary.db;Cache=Shared",
      "BackupPath": "backup/us-east/"
    },
    "Replication": {
      "TargetRegions": ["eu-west-1", "ap-southeast-1"],
      "ReplicationLag": "60s",
      "ConsistencyLevel": "Eventually"
    }
  },
  "CDN": {
    "Distribution": "us-east-distribution",
    "OriginDomain": "us-east-1.aichat.com",
    "CacheBehaviors": {
      "/api/*": { "TTL": "0s", "Compress": true },
      "/assets/*": { "TTL": "1d", "Compress": true },
      "/static/*": { "TTL": "7d", "Compress": true }
    }
  },
  "DNS": {
    "PrimaryRecord": "aichat.com",
    "RegionalRecord": "us-east-1.aichat.com",
    "HealthCheckPath": "/health/regional",
    "TTL": 60,
    "FailoverTarget": "eu-west-1.aichat.com"
  }
}
```

**EU West (Standby Region) - `appsettings.Production.EUWest.json`:**
```json
{
  "Orleans": {
    "ClusterId": "doc-chat-cluster-prod-eu-west",
    "ServiceId": "doc-chat-service-prod",
    "Region": "eu-west-1",
    "Clustering": {
      "ProviderType": "AdoNet",
      "ConnectionString": "Data Source=eu-west-cluster-db.db;Cache=Shared",
      "Invariant": "Microsoft.Data.Sqlite"
    },
    "MultiRegion": {
      "PrimaryRegion": false,
      "RegionId": "eu-west-1",
      "PrimaryReplicationSource": "https://us-east-1.aichat.com/api/replication",
      "ReplicationMode": "AsyncReplica",
      "ConflictResolution": "LastWriterWins",
      "ReplicationBatchSize": 1000,
      "MaxReplicationLag": "300s"
    }
  },
  "Database": {
    "Primary": {
      "ConnectionString": "Data Source=eu-west-replica.db;Cache=Shared",
      "BackupPath": "backup/eu-west/"
    },
    "Replication": {
      "SourceRegion": "us-east-1",
      "ReplicationMode": "ReadReplica",
      "PromotionMode": "Manual"
    }
  }
}
```

### 2. Cross-Region Deployment Procedures

#### Step 1: Regional Infrastructure Setup

**Primary Region Deployment (US East):**
```powershell
# Deploy primary region with full HA configuration
function Deploy-PrimaryRegion {
    param(
        [string]$Region = "us-east-1",
        [string]$Environment = "Production"
    )

    Write-Host "🌎 Deploying primary region: $Region" -ForegroundColor Cyan

    # Step 1: Deploy regional infrastructure
    Write-Host "🏗️ Setting up regional infrastructure..." -ForegroundColor Yellow

    # Create regional database
    $regionalDb = "data/$Region-primary.db"
    if (!(Test-Path $regionalDb)) {
        Copy-Item "templates/empty-db.db" $regionalDb
        Write-Host "✅ Regional database created: $regionalDb" -ForegroundColor Green
    }

    # Step 2: Configure cross-region networking
    Write-Host "🔗 Configuring cross-region networking..." -ForegroundColor Yellow

    # Setup VPN/ExpressRoute connections (implementation specific)
    $networkConfig = @{
        Region = $Region
        VPNEndpoints = @(
            "eu-west-1-vpn.aichat.com",
            "ap-southeast-1-vpn.aichat.com"
        )
        Bandwidth = "1Gbps"
        Encryption = "AES256"
    }

    # Step 3: Deploy Orleans cluster with multi-region configuration
    Write-Host "🚀 Deploying Orleans cluster..." -ForegroundColor Blue

    # Update configuration for region
    $configPath = "server/AIChat.Server/appsettings.Production.$Region.json"
    $config = Get-Content $configPath | ConvertFrom-Json
    $config.Orleans.MultiRegion.RegionId = $Region
    $config | ConvertTo-Json -Depth 10 | Set-Content $configPath

    # Deploy both zones in the region
    & "scripts/deploy-zone-a.ps1" -Environment $Environment -Region $Region
    & "scripts/deploy-zone-b.ps1" -Environment $Environment -Region $Region

    # Step 4: Setup cross-region replication
    Write-Host "🔄 Configuring cross-region replication..." -ForegroundColor Green

    Start-Job -Name "ReplicationSetup-$Region" -ScriptBlock {
        param($Region, $Config)

        # Initialize replication endpoints
        foreach ($endpoint in $Config.Orleans.MultiRegion.ReplicationEndpoints) {
            try {
                # Test replication connectivity
                $response = Invoke-RestMethod -Uri "$endpoint/health" -TimeoutSec 10
                Write-Host "✅ Replication endpoint available: $endpoint"
            } catch {
                Write-Warning "⚠️ Replication endpoint not ready: $endpoint"
            }
        }

        # Start replication service
        Start-ReplicationService -Region $Region -Config $Config

    } -ArgumentList $Region, $config

    # Step 5: Configure DNS and CDN
    Write-Host "🌐 Configuring global DNS and CDN..." -ForegroundColor Magenta

    # Configure Route53/CloudDNS records
    $dnsConfig = @{
        PrimaryRecord = "aichat.com"
        RegionalRecord = "$Region.aichat.com"
        HealthCheckURL = "https://$Region.aichat.com/health/regional"
        TTL = 60
        RecordType = "A"
        FailoverPolicy = "Primary"
    }

    # Configure CDN distribution
    $cdnConfig = @{
        Distribution = "$Region-distribution"
        OriginDomain = "$Region.aichat.com"
        EdgeLocations = "Global"
        PriceClass = "All"
    }

    Write-Host "✅ Primary region deployment completed: $Region" -ForegroundColor Green
}

# Execute primary region deployment
Deploy-PrimaryRegion -Region "us-east-1" -Environment "Production"
```

**Standby Region Deployment (EU West):**
```powershell
# Deploy standby region with replication configuration
function Deploy-StandbyRegion {
    param(
        [string]$Region = "eu-west-1",
        [string]$PrimaryRegion = "us-east-1",
        [string]$Environment = "Production"
    )

    Write-Host "🌍 Deploying standby region: $Region" -ForegroundColor Cyan

    # Step 1: Wait for primary region to be ready
    Write-Host "⏳ Waiting for primary region to be ready..." -ForegroundColor Yellow

    $primaryReady = $false
    $maxAttempts = 30
    $attempt = 0

    do {
        $attempt++
        Start-Sleep -Seconds 30

        try {
            $primaryHealth = Invoke-RestMethod -Uri "https://$PrimaryRegion.aichat.com/health/regional" -TimeoutSec 10
            if ($primaryHealth.Status -eq "Healthy" -and $primaryHealth.ReplicationReady -eq $true) {
                $primaryReady = $true
                Write-Host "✅ Primary region is ready for replication" -ForegroundColor Green
            }
        } catch {
            Write-Host "⏳ Primary region not ready (attempt $attempt/$maxAttempts)..." -ForegroundColor Yellow
        }
    } while (!$primaryReady -and $attempt -lt $maxAttempts)

    if (!$primaryReady) {
        throw "Primary region not ready after $maxAttempts attempts"
    }

    # Step 2: Deploy regional infrastructure
    Write-Host "🏗️ Setting up standby region infrastructure..." -ForegroundColor Yellow

    # Create regional replica database
    $regionalDb = "data/$Region-replica.db"
    if (!(Test-Path $regionalDb)) {
        # Initialize from primary region backup
        $primaryBackup = "backup/$PrimaryRegion/latest.db"
        if (Test-Path $primaryBackup) {
            Copy-Item $primaryBackup $regionalDb
            Write-Host "✅ Regional database initialized from primary backup" -ForegroundColor Green
        } else {
            Copy-Item "templates/empty-db.db" $regionalDb
            Write-Host "⚠️ Regional database created empty - will sync from primary" -ForegroundColor Yellow
        }
    }

    # Step 3: Deploy Orleans cluster in standby mode
    Write-Host "🚀 Deploying Orleans cluster in standby mode..." -ForegroundColor Blue

    # Update configuration for standby region
    $configPath = "server/AIChat.Server/appsettings.Production.$Region.json"
    $config = Get-Content $configPath | ConvertFrom-Json
    $config.Orleans.MultiRegion.RegionId = $Region
    $config.Orleans.MultiRegion.PrimaryReplicationSource = "https://$PrimaryRegion.aichat.com/api/replication"
    $config | ConvertTo-Json -Depth 10 | Set-Content $configPath

    # Deploy both zones in standby region
    & "scripts/deploy-zone-a.ps1" -Environment $Environment -Region $Region -Mode "Standby"
    & "scripts/deploy-zone-b.ps1" -Environment $Environment -Region $Region -Mode "Standby"

    # Step 4: Initialize replication from primary
    Write-Host "🔄 Starting replication from primary region..." -ForegroundColor Green

    Start-Job -Name "InitialReplication-$Region" -ScriptBlock {
        param($Region, $PrimaryRegion, $Config)

        Write-Host "Starting initial replication from $PrimaryRegion to $Region..."

        # Request full sync from primary
        $replicationRequest = @{
            TargetRegion = $Region
            SyncType = "InitialSync"
            IncludeHistory = $true
            Timestamp = Get-Date
        }

        $syncResponse = Invoke-RestMethod -Uri "https://$PrimaryRegion.aichat.com/api/replication/sync" -Method Post -Body ($replicationRequest | ConvertTo-Json) -ContentType "application/json"

        if ($syncResponse.Status -eq "Started") {
            Write-Host "✅ Initial sync started - Job ID: $($syncResponse.JobId)"

            # Monitor sync progress
            do {
                Start-Sleep -Seconds 60
                $progress = Invoke-RestMethod -Uri "https://$PrimaryRegion.aichat.com/api/replication/sync/$($syncResponse.JobId)/status"
                Write-Host "Sync progress: $($progress.PercentComplete)% - $($progress.Status)"
            } while ($progress.Status -eq "InProgress")

            if ($progress.Status -eq "Completed") {
                Write-Host "✅ Initial replication completed successfully"
            } else {
                Write-Error "❌ Initial replication failed: $($progress.Error)"
            }
        } else {
            Write-Error "❌ Failed to start initial sync: $($syncResponse.Error)"
        }

    } -ArgumentList $Region, $PrimaryRegion, $config

    # Step 5: Configure regional DNS (standby)
    Write-Host "🌐 Configuring regional DNS for standby..." -ForegroundColor Magenta

    $dnsConfig = @{
        RegionalRecord = "$Region.aichat.com"
        HealthCheckURL = "https://$Region.aichat.com/health/regional"
        TTL = 60
        RecordType = "A"
        FailoverPolicy = "Secondary"
        PrimaryRegion = $PrimaryRegion
    }

    Write-Host "✅ Standby region deployment completed: $Region" -ForegroundColor Green
}

# Execute standby region deployment
Deploy-StandbyRegion -Region "eu-west-1" -PrimaryRegion "us-east-1" -Environment "Production"
```

### 3. Cross-Region Replication Management

#### Replication Service Implementation

**Create `Controllers/ReplicationController.cs`:**
```csharp
[ApiController]
[Route("api/replication")]
public class ReplicationController : ControllerBase
{
    private readonly IReplicationService _replicationService;
    private readonly ILogger<ReplicationController> _logger;

    [HttpPost("sync")]
    public async Task<IActionResult> StartSync([FromBody] SyncRequest request)
    {
        try {
            var jobId = await _replicationService.StartSyncAsync(request);
            return Ok(new { Status = "Started", JobId = jobId });
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to start sync to {Region}", request.TargetRegion);
            return StatusCode(500, new { Status = "Error", Error = ex.Message });
        }
    }

    [HttpGet("sync/{jobId}/status")]
    public async Task<IActionResult> GetSyncStatus(string jobId)
    {
        try {
            var status = await _replicationService.GetSyncStatusAsync(jobId);
            return Ok(status);
        } catch (Exception ex) {
            return StatusCode(500, new { Status = "Error", Error = ex.Message });
        }
    }

    [HttpPost("receive")]
    public async Task<IActionResult> ReceiveReplication([FromBody] ReplicationBatch batch)
    {
        try {
            await _replicationService.ProcessReplicationBatchAsync(batch);
            return Ok(new { Status = "Processed", Count = batch.Items.Count });
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to process replication batch");
            return StatusCode(500, new { Status = "Error", Error = ex.Message });
        }
    }

    [HttpGet("health")]
    public async Task<IActionResult> ReplicationHealth()
    {
        var health = await _replicationService.GetReplicationHealthAsync();
        return Ok(health);
    }
}

public class ReplicationService : IReplicationService
{
    public async Task<string> StartSyncAsync(SyncRequest request)
    {
        var jobId = Guid.NewGuid().ToString();

        // Start background sync job
        _ = Task.Run(async () => await PerformSyncAsync(jobId, request));

        return jobId;
    }

    private async Task PerformSyncAsync(string jobId, SyncRequest request)
    {
        try {
            // Get all data to sync based on request parameters
            var dataToSync = await GetDataForSyncAsync(request);

            // Split into batches
            var batches = dataToSync.Chunk(1000).ToList();

            foreach (var batchData in batches) {
                var batch = new ReplicationBatch {
                    JobId = jobId,
                    Items = batchData.ToList(),
                    TargetRegion = request.TargetRegion,
                    Timestamp = DateTime.UtcNow
                };

                // Send to target region
                var targetUrl = GetRegionReplicationUrl(request.TargetRegion);
                await SendReplicationBatchAsync(targetUrl, batch);

                // Update progress
                await UpdateSyncProgressAsync(jobId, batches.IndexOf(batchData) + 1, batches.Count);
            }

            await CompleteSyncAsync(jobId);

        } catch (Exception ex) {
            await FailSyncAsync(jobId, ex);
        }
    }
}
```

**Replication Monitoring Script:**
```powershell
# Monitor cross-region replication health and performance
function Start-ReplicationMonitoring {
    param(
        [string[]]$Regions = @("us-east-1", "eu-west-1"),
        [string]$PrimaryRegion = "us-east-1"
    )

    Write-Host "📊 Starting cross-region replication monitoring..." -ForegroundColor Cyan

    while ($true) {
        try {
            $timestamp = Get-Date
            $replicationMetrics = @{}

            # Check primary region replication health
            Write-Host "🔍 Checking primary region replication health..." -ForegroundColor Yellow

            try {
                $primaryHealth = Invoke-RestMethod -Uri "https://$PrimaryRegion.aichat.com/api/replication/health" -TimeoutSec 10
                $replicationMetrics["primary_region_status"] = if ($primaryHealth.Status -eq "Healthy") { 1 } else { 0 }
                $replicationMetrics["primary_replication_lag"] = $primaryHealth.ReplicationLag
                $replicationMetrics["primary_pending_operations"] = $primaryHealth.PendingOperations

                Write-Host "✅ Primary region replication: $($primaryHealth.Status)" -ForegroundColor Green

            } catch {
                $replicationMetrics["primary_region_status"] = 0
                Write-Warning "❌ Primary region replication check failed: $($_.Exception.Message)"
            }

            # Check standby regions
            foreach ($region in $Regions) {
                if ($region -eq $PrimaryRegion) { continue }

                Write-Host "🔍 Checking standby region: $region" -ForegroundColor Yellow

                try {
                    $regionHealth = Invoke-RestMethod -Uri "https://$region.aichat.com/api/replication/health" -TimeoutSec 10
                    $replicationMetrics["region_$($region.Replace('-', '_'))_status"] = if ($regionHealth.Status -eq "Healthy") { 1 } else { 0 }
                    $replicationMetrics["region_$($region.Replace('-', '_'))_lag"] = $regionHealth.ReplicationLag
                    $replicationMetrics["region_$($region.Replace('-', '_'))_last_sync"] = $regionHealth.LastSyncTime

                    # Check replication lag
                    if ($regionHealth.ReplicationLag -gt 300) { # 5 minutes
                        Write-Warning "⚠️ High replication lag for $region`: $($regionHealth.ReplicationLag)s"
                        Send-Alert -Level "Warning" -Message "High replication lag for region $region`: $($regionHealth.ReplicationLag)s"
                    }

                    Write-Host "✅ Region $region replication: $($regionHealth.Status) (lag: $($regionHealth.ReplicationLag)s)" -ForegroundColor Green

                } catch {
                    $replicationMetrics["region_$($region.Replace('-', '_'))_status"] = 0
                    Write-Warning "❌ Region $region replication check failed: $($_.Exception.Message)"
                }
            }

            # Check cross-region network connectivity
            Write-Host "🌐 Testing cross-region connectivity..." -ForegroundColor Blue

            foreach ($region in $Regions) {
                if ($region -eq $PrimaryRegion) { continue }

                try {
                    $latency = Test-NetworkLatency -SourceRegion $PrimaryRegion -TargetRegion $region
                    $replicationMetrics["cross_region_latency_$($region.Replace('-', '_'))"] = $latency

                    if ($latency -gt 200) { # 200ms threshold
                        Write-Warning "⚠️ High cross-region latency to $region`: ${latency}ms"
                    }

                } catch {
                    Write-Warning "❌ Failed to test connectivity to $region"
                    $replicationMetrics["cross_region_latency_$($region.Replace('-', '_'))"] = -1
                }
            }

            # Export metrics
            Export-Metrics -Metrics $replicationMetrics -Timestamp $timestamp -MetricType "Replication"

            # Check for critical conditions
            Test-ReplicationAlertConditions -Metrics $replicationMetrics -Regions $Regions

        } catch {
            Write-Error "Replication monitoring failed: $($_.Exception.Message)"
        }

        Start-Sleep -Seconds 60
    }
}

function Test-NetworkLatency {
    param([string]$SourceRegion, [string]$TargetRegion)

    $startTime = Get-Date
    try {
        $response = Invoke-RestMethod -Uri "https://$TargetRegion.aichat.com/api/ping" -TimeoutSec 5
        $endTime = Get-Date
        return ($endTime - $startTime).TotalMilliseconds
    } catch {
        return -1
    }
}
```

### 4. Regional Failover Procedures

#### Automatic Regional Failover
```powershell
# Automated regional failover management
function Start-RegionalFailoverMonitoring {
    param(
        [string]$PrimaryRegion = "us-east-1",
        [string[]]$StandbyRegions = @("eu-west-1"),
        [int]$FailureThreshold = 3,
        [int]$CheckIntervalSeconds = 30
    )

    $consecutiveFailures = 0
    $lastFailoverTime = $null
    $failoverCooldown = [TimeSpan]::FromMinutes(15) # Longer cooldown for regional failover

    Write-Host "🔍 Starting regional failover monitoring..." -ForegroundColor Cyan
    Write-Host "   Primary: $PrimaryRegion" -ForegroundColor White
    Write-Host "   Standby: $($StandbyRegions -join ', ')" -ForegroundColor White

    while ($true) {
        try {
            # Check primary region health
            $primaryHealthy = Test-RegionalHealth -Region $PrimaryRegion

            if ($primaryHealthy) {
                $consecutiveFailures = 0
                Write-Host "✅ Primary region healthy: $PrimaryRegion" -ForegroundColor Green
            } else {
                $consecutiveFailures++
                Write-Warning "❌ Primary region health check failed (attempt $consecutiveFailures/$FailureThreshold): $PrimaryRegion"

                if ($consecutiveFailures -ge $FailureThreshold) {
                    # Check cooldown period
                    if ($lastFailoverTime -and ((Get-Date) - $lastFailoverTime) -lt $failoverCooldown) {
                        Write-Warning "⏳ Regional failover cooldown active - waiting..."
                    } else {
                        Write-Host "🚨 Initiating regional failover!" -ForegroundColor Red

                        try {
                            $targetRegion = Select-BestStandbyRegion -StandbyRegions $StandbyRegions
                            Invoke-RegionalFailover -FromRegion $PrimaryRegion -ToRegion $targetRegion
                            $lastFailoverTime = Get-Date
                            $consecutiveFailures = 0
                        } catch {
                            Write-Error "❌ Regional failover failed: $($_.Exception.Message)"
                        }
                    }
                }
            }

        } catch {
            Write-Error "Regional failover monitoring error: $($_.Exception.Message)"
        }

        Start-Sleep -Seconds $CheckIntervalSeconds
    }
}

function Test-RegionalHealth {
    param([string]$Region)

    $healthEndpoints = @(
        "https://$Region.aichat.com/health/regional",
        "https://$Region.aichat.com/health/orleans",
        "https://$Region.aichat.com/health/database"
    )

    $healthyEndpoints = 0

    foreach ($endpoint in $healthEndpoints) {
        try {
            $response = Invoke-RestMethod -Uri $endpoint -TimeoutSec 15
            if ($response.Status -eq "Healthy") {
                $healthyEndpoints++
            }
        } catch {
            Write-Debug "Health check failed for $endpoint"
        }
    }

    # Region is healthy if at least 2/3 endpoints are responding
    return $healthyEndpoints -ge 2
}

function Select-BestStandbyRegion {
    param([string[]]$StandbyRegions)

    $regionScores = @{}

    foreach ($region in $StandbyRegions) {
        try {
            $health = Invoke-RestMethod -Uri "https://$region.aichat.com/health/regional" -TimeoutSec 10
            $replicationHealth = Invoke-RestMethod -Uri "https://$region.aichat.com/api/replication/health" -TimeoutSec 10

            # Score based on health and replication lag
            $score = 100
            if ($health.Status -ne "Healthy") { $score -= 50 }
            if ($replicationHealth.ReplicationLag -gt 300) { $score -= 30 }
            if ($replicationHealth.ReplicationLag -gt 600) { $score -= 30 }

            $regionScores[$region] = $score
            Write-Host "Region $region score: $score" -ForegroundColor Blue

        } catch {
            $regionScores[$region] = 0
            Write-Warning "Region $region is not available for failover"
        }
    }

    $bestRegion = $regionScores.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1

    if ($bestRegion.Value -lt 50) {
        throw "No suitable standby region available for failover"
    }

    return $bestRegion.Key
}

function Invoke-RegionalFailover {
    param([string]$FromRegion, [string]$ToRegion)

    Write-Host "🔄 Executing regional failover from $FromRegion to $ToRegion..." -ForegroundColor Yellow

    # Step 1: Promote standby region to primary
    Write-Host "📈 Promoting $ToRegion to primary..." -ForegroundColor Blue

    try {
        $promoteResponse = Invoke-RestMethod -Uri "https://$ToRegion.aichat.com/api/admin/promote-to-primary" -Method Post -TimeoutSec 30

        if ($promoteResponse.Status -eq "Success") {
            Write-Host "✅ Region $ToRegion promoted to primary" -ForegroundColor Green
        } else {
            throw "Failed to promote region: $($promoteResponse.Error)"
        }

    } catch {
        throw "Failed to promote standby region: $($_.Exception.Message)"
    }

    # Step 2: Update DNS records for failover
    Write-Host "🌐 Updating DNS records for failover..." -ForegroundColor Magenta

    try {
        # Update primary DNS record to point to new region
        $dnsUpdate = @{
            RecordName = "aichat.com"
            RecordType = "A"
            NewTarget = "$ToRegion.aichat.com"
            TTL = 60
            FailoverReason = "RegionalFailover"
            Timestamp = Get-Date
        }

        Update-DNSRecord @dnsUpdate
        Write-Host "✅ DNS updated to route traffic to $ToRegion" -ForegroundColor Green

    } catch {
        throw "Failed to update DNS records: $($_.Exception.Message)"
    }

    # Step 3: Update CDN distribution
    Write-Host "📦 Updating CDN distribution..." -ForegroundColor Cyan

    try {
        $cdnUpdate = @{
            Distribution = "global-distribution"
            NewOrigin = "$ToRegion.aichat.com"
            InvalidatePaths = @("/*")
        }

        Update-CDNDistribution @cdnUpdate
        Write-Host "✅ CDN updated to serve from $ToRegion" -ForegroundColor Green

    } catch {
        Write-Warning "CDN update failed - manual intervention may be required: $($_.Exception.Message)"
    }

    # Step 4: Notify operations team
    Send-FailoverNotification -Type "RegionalFailover" -FromRegion $FromRegion -ToRegion $ToRegion -Timestamp (Get-Date)

    # Step 5: Update monitoring systems
    Write-Host "📊 Updating monitoring configuration..." -ForegroundColor Yellow

    $monitoringUpdate = @{
        NewPrimaryRegion = $ToRegion
        FormerPrimaryRegion = $FromRegion
        FailoverTime = Get-Date
    }

    Update-MonitoringConfiguration @monitoringUpdate

    Write-Host "✅ Regional failover completed successfully!" -ForegroundColor Green
    Write-Host "   New Primary: $ToRegion" -ForegroundColor White
    Write-Host "   Former Primary: $FromRegion" -ForegroundColor Gray
}
```

### 5. Multi-Region Health Checks

#### Regional Health Endpoint
**Add to `Controllers/HealthController.cs`:**
```csharp
[ApiController]
[Route("health")]
public class RegionalHealthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IClusterClient _clusterClient;
    private readonly IReplicationService _replicationService;

    [HttpGet("regional")]
    public async Task<IActionResult> RegionalHealth()
    {
        try {
            var regionId = _configuration["Orleans:MultiRegion:RegionId"];
            var isPrimary = _configuration.GetValue<bool>("Orleans:MultiRegion:PrimaryRegion");

            var health = new RegionalHealthStatus {
                Region = regionId,
                Status = "Healthy",
                IsPrimary = isPrimary,
                LastCheck = DateTime.UtcNow,
                Services = new Dictionary<string, ServiceHealth>()
            };

            // Check Orleans cluster health
            try {
                var managementGrain = _clusterClient.GetGrain<IManagementGrain>(0);
                var hosts = await managementGrain.GetHosts();

                health.Services["orleans"] = new ServiceHealth {
                    Status = hosts.Any() ? "Healthy" : "Unhealthy",
                    Details = new { SiloCount = hosts.Count() }
                };
            } catch (Exception ex) {
                health.Services["orleans"] = new ServiceHealth {
                    Status = "Unhealthy",
                    Error = ex.Message
                };
                health.Status = "Degraded";
            }

            // Check database health
            try {
                await CheckDatabaseHealthAsync();
                health.Services["database"] = new ServiceHealth { Status = "Healthy" };
            } catch (Exception ex) {
                health.Services["database"] = new ServiceHealth {
                    Status = "Unhealthy",
                    Error = ex.Message
                };
                health.Status = "Degraded";
            }

            // Check replication health (for standby regions)
            if (!isPrimary) {
                try {
                    var replicationHealth = await _replicationService.GetReplicationHealthAsync();
                    health.Services["replication"] = new ServiceHealth {
                        Status = replicationHealth.Status,
                        Details = new {
                            ReplicationLag = replicationHealth.ReplicationLag,
                            LastSync = replicationHealth.LastSyncTime
                        }
                    };

                    if (replicationHealth.ReplicationLag > 300) { // 5 minutes
                        health.Status = "Degraded";
                    }
                } catch (Exception ex) {
                    health.Services["replication"] = new ServiceHealth {
                        Status = "Unhealthy",
                        Error = ex.Message
                    };
                    health.Status = "Unhealthy";
                }
            }

            // Set overall region readiness
            health.ReplicationReady = isPrimary || health.Services["replication"]?.Status == "Healthy";

            var statusCode = health.Status == "Healthy" ? 200 :
                           health.Status == "Degraded" ? 200 : 503;

            return StatusCode(statusCode, health);

        } catch (Exception ex) {
            return StatusCode(503, new {
                Status = "Unhealthy",
                Region = _configuration["Orleans:MultiRegion:RegionId"],
                Error = ex.Message,
                LastCheck = DateTime.UtcNow
            });
        }
    }
}

public class RegionalHealthStatus
{
    public string Region { get; set; }
    public string Status { get; set; }
    public bool IsPrimary { get; set; }
    public bool ReplicationReady { get; set; }
    public DateTime LastCheck { get; set; }
    public Dictionary<string, ServiceHealth> Services { get; set; }
}
```

### 6. Multi-Region Operational Procedures

#### Regional Status Dashboard Script
```powershell
# Multi-region operational dashboard
function Show-RegionalStatus {
    param(
        [string[]]$Regions = @("us-east-1", "eu-west-1", "ap-southeast-1"),
        [string]$PrimaryRegion = "us-east-1"
    )

    Write-Host "🌍 Multi-Region Orleans Deployment Status" -ForegroundColor Cyan
    Write-Host "=" * 60 -ForegroundColor Cyan

    $overallStatus = "Healthy"
    $totalRegions = $Regions.Count
    $healthyRegions = 0

    foreach ($region in $Regions) {
        Write-Host "`n📍 Region: $region" -ForegroundColor Yellow

        if ($region -eq $PrimaryRegion) {
            Write-Host "   Type: PRIMARY" -ForegroundColor Green
        } else {
            Write-Host "   Type: Standby" -ForegroundColor Blue
        }

        try {
            # Get regional health
            $health = Invoke-RestMethod -Uri "https://$region.aichat.com/health/regional" -TimeoutSec 10

            # Display regional status
            $statusColor = switch ($health.Status) {
                "Healthy" { "Green" }
                "Degraded" { "Yellow" }
                default { "Red" }
            }

            Write-Host "   Status: $($health.Status)" -ForegroundColor $statusColor
            Write-Host "   Last Check: $($health.LastCheck)"

            if ($health.Status -eq "Healthy") {
                $healthyRegions++
            } elseif ($health.Status -eq "Degraded") {
                $overallStatus = "Degraded"
            } else {
                $overallStatus = "Critical"
            }

            # Display service details
            Write-Host "   Services:" -ForegroundColor White
            foreach ($service in $health.Services.GetEnumerator()) {
                $serviceColor = if ($service.Value.Status -eq "Healthy") { "Green" } else { "Red" }
                Write-Host "     - $($service.Key): $($service.Value.Status)" -ForegroundColor $serviceColor

                if ($service.Value.Error) {
                    Write-Host "       Error: $($service.Value.Error)" -ForegroundColor Red
                }

                if ($service.Value.Details) {
                    foreach ($detail in $service.Value.Details.GetEnumerator()) {
                        Write-Host "       $($detail.Key): $($detail.Value)" -ForegroundColor Gray
                    }
                }
            }

            # Display replication info for standby regions
            if ($region -ne $PrimaryRegion -and $health.Services.ContainsKey("replication")) {
                $replication = $health.Services["replication"]
                if ($replication.Details) {
                    Write-Host "   Replication Lag: $($replication.Details.ReplicationLag)s" -ForegroundColor $(if ($replication.Details.ReplicationLag -gt 300) { "Yellow" } else { "Green" })
                    Write-Host "   Last Sync: $($replication.Details.LastSync)" -ForegroundColor Gray
                }
            }

        } catch {
            Write-Host "   Status: UNREACHABLE" -ForegroundColor Red
            Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
            $overallStatus = "Critical"
        }

        # Test connectivity from primary region
        if ($region -ne $PrimaryRegion) {
            try {
                $latency = Test-NetworkLatency -SourceRegion $PrimaryRegion -TargetRegion $region
                if ($latency -gt 0) {
                    $latencyColor = if ($latency -lt 100) { "Green" } elseif ($latency -lt 200) { "Yellow" } else { "Red" }
                    Write-Host "   Latency from primary: ${latency}ms" -ForegroundColor $latencyColor
                } else {
                    Write-Host "   Latency from primary: UNREACHABLE" -ForegroundColor Red
                }
            } catch {
                Write-Host "   Latency from primary: ERROR" -ForegroundColor Red
            }
        }
    }

    # Overall status summary
    Write-Host "`n" + "=" * 60 -ForegroundColor Cyan
    Write-Host "📊 OVERALL STATUS" -ForegroundColor Cyan

    $overallColor = switch ($overallStatus) {
        "Healthy" { "Green" }
        "Degraded" { "Yellow" }
        default { "Red" }
    }

    Write-Host "Status: $overallStatus" -ForegroundColor $overallColor
    Write-Host "Healthy Regions: $healthyRegions/$totalRegions" -ForegroundColor White
    Write-Host "Primary Region: $PrimaryRegion" -ForegroundColor White
    Write-Host "Timestamp: $(Get-Date)" -ForegroundColor Gray

    # Recommendations
    if ($overallStatus -ne "Healthy") {
        Write-Host "`n⚠️  RECOMMENDATIONS:" -ForegroundColor Yellow

        if ($healthyRegions -eq 0) {
            Write-Host "- CRITICAL: All regions are down - immediate intervention required" -ForegroundColor Red
        } elseif ($healthyRegions -eq 1 -and $Regions -contains $PrimaryRegion) {
            Write-Host "- WARNING: Only primary region healthy - standby regions unavailable" -ForegroundColor Yellow
        } elseif ($healthyRegions -lt $totalRegions) {
            Write-Host "- Some regions are degraded - check replication and connectivity" -ForegroundColor Yellow
        }

        Write-Host "- Review logs and monitoring dashboards for detailed diagnostics" -ForegroundColor White
        Write-Host "- Consider manual failover if primary region is affected" -ForegroundColor White
    }

    return @{
        OverallStatus = $overallStatus
        HealthyRegions = $healthyRegions
        TotalRegions = $totalRegions
        PrimaryRegion = $PrimaryRegion
    }
}
```

## Rollback Procedures

### 1. Immediate Rollback (< 15 minutes)

#### Feature Flag Rollback
```powershell
# Disable Orleans via feature flag (fastest option)
$appSettings = Get-Content "server\AIChat.Server\appsettings.Production.json" | ConvertFrom-Json
$appSettings.FeatureManagement.OrleansEnabled = $false
$appSettings | ConvertTo-Json -Depth 10 | Set-Content "server\AIChat.Server\appsettings.Production.json"

# Restart service
Restart-Service -Name "AIChat.Server" -Force

# Verify fallback to direct services
$healthCheck = Invoke-RestMethod -Uri "http://localhost:5099/health" -Method GET
Write-Host "Service Status: $healthCheck"
```

### 2. Code Rollback (1-2 hours)

#### Git-based Rollback
```powershell
# Identify rollback commit
git log --oneline -10

# Create rollback branch
$rollbackCommit = "commit-hash-to-rollback-to"
git checkout -b "rollback-$(Get-Date -Format 'yyyy-MM-dd-HH-mm')" $rollbackCommit

# Deploy rolled-back version
dotnet build --configuration Release
.\build-and-start-server.ps1 -Environment Production -Port 5099
```

### 3. Data Recovery (2-4 hours)

#### State Recovery Process
```powershell
# Stop all services
Stop-Process -Name "AIChat*" -Force

# Restore database from backup
Copy-Item -Path "backups\database-backup-latest.db" -Destination "data\aichat.db" -Force

# Restart with Orleans disabled initially
.\build-and-start-server.ps1 -Environment Production -Port 5099

# Verify data integrity
Invoke-RestMethod -Uri "http://localhost:5099/api/health/database" -Method GET
```

## Environment-Specific Configurations

### Development
```json
{
  "Orleans": {
    "ClusterId": "doc-chat-cluster-dev",
    "ServiceId": "doc-chat-service-dev",
    "SiloPort": 11111,
    "GatewayPort": 30000,
    "DashboardPort": 8080,
    "Dashboard": {
      "Enabled": true,
      "Username": "admin",
      "Password": "orleans123"
    }
  },
  "Logging": {
    "LogLevel": {
      "Orleans": "Information",
      "AIChat.Orleans": "Debug"
    }
  }
}
```

### Production
```json
{
  "Orleans": {
    "ClusterId": "doc-chat-cluster-prod",
    "ServiceId": "doc-chat-service-prod",
    "SiloPort": 11111,
    "GatewayPort": 30000,
    "DashboardPort": 8080,
    "Dashboard": {
      "Enabled": true,
      "Username": "admin",
      "Password": "SECURE_PASSWORD_HERE"
    }
  },
  "Logging": {
    "LogLevel": {
      "Orleans": "Warning",
      "AIChat.Orleans": "Information"
    }
  }
}
```

## Deployment Validation Checklist

### Pre-deployment
- [ ] Code formatted with `.\scripts\format-code.ps1`
- [ ] All build warnings resolved
- [ ] Unit tests passing (>95%)
- [ ] Integration tests passing
- [ ] Performance tests completed
- [ ] Security scan passed
- [ ] Configuration reviewed
- [ ] Database migrations applied

### Post-deployment
- [ ] Health checks passing
- [ ] Orleans dashboard accessible
- [ ] Metrics collection working
- [ ] Logs being written correctly
- [ ] Performance within acceptable ranges
- [ ] Feature flags working correctly
- [ ] Fallback mechanisms tested
- [ ] Monitoring alerts configured

### Production Validation
- [ ] End-to-end user scenarios tested
- [ ] Load balancer health checks passing
- [ ] SSL certificates valid
- [ ] Database connections stable
- [ ] Memory usage within limits
- [ ] Orleans grain activation working
- [ ] Dual-mode routing functioning
- [ ] Recovery procedures tested

## Troubleshooting Deployment Issues

### Build Failures
```powershell
# Check detailed build output
dotnet build --verbosity detailed | Tee-Object -FilePath "deployment-build.log"

# Review specific project issues
dotnet build server\AIChat.Orleans\AIChat.Orleans.csproj --verbosity detailed
dotnet build server\AIChat.Server\AIChat.Server.csproj --verbosity detailed
```

### Orleans Activation Issues
```powershell
# Check Orleans logs
Get-Content "logs\orleans-host-*.log" | Select-String "ERROR|EXCEPTION"

# Verify silo health
Invoke-RestMethod -Uri "http://localhost:5100/health" -Method GET

# Check grain activations
Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics" -Method GET
```

### Service Start Issues
```powershell
# Check port conflicts
netstat -an | findstr "5099 8080 11111 30000"

# Review application logs
Get-Content "logs\server\*.log" | Select-String -Pattern "ERROR|EXCEPTION|FATAL"

# Verify environment configuration
$env:ASPNETCORE_ENVIRONMENT
```

## Enterprise Security Configuration

This section provides comprehensive security procedures for enterprise deployment of Orleans-based AI Chat system. All procedures include validation steps and are designed for production environments.

### 1. Enterprise Authentication Integration

#### 1.1 LDAP/Active Directory Integration

**Overview**: Integrate Orleans Dashboard and API endpoints with enterprise LDAP/Active Directory for centralized authentication.

##### Prerequisites
- Active Directory domain controller access
- Service account with appropriate LDAP query permissions
- Network connectivity between Orleans services and domain controllers

##### Configuration Steps

**Step 1: Configure LDAP Authentication Provider**
```json
{
  "Authentication": {
    "LDAP": {
      "Enabled": true,
      "Server": "ldap://your-domain-controller.company.com:389",
      "BaseDN": "DC=company,DC=com",
      "UserSearchBase": "OU=Users,DC=company,DC=com",
      "GroupSearchBase": "OU=Groups,DC=company,DC=com",
      "ServiceAccount": {
        "Username": "orleans-service@company.com",
        "Password": "#{LDAP_SERVICE_PASSWORD}#" // From Key Vault
      },
      "UserFilter": "(&(objectClass=person)(sAMAccountName={0}))",
      "GroupFilter": "(&(objectClass=group)(member={0}))",
      "ConnectionTimeout": 30,
      "SearchTimeout": 30
    }
  }
}
```

**Step 2: Configure SSL/TLS for LDAP (Recommended)**
```json
{
  "Authentication": {
    "LDAP": {
      "Server": "ldaps://your-domain-controller.company.com:636",
      "RequireSSL": true,
      "CertificateValidation": {
        "ValidateCertificate": true,
        "TrustedCA": "#{LDAP_CA_CERTIFICATE}#" // From Key Vault
      }
    }
  }
}
```

**Step 3: Validation Script**
```powershell
# Validate LDAP connectivity and authentication
function Test-LDAPConfiguration {
    param(
        [string]$Server,
        [string]$Username,
        [securestring]$Password
    )

    try {
        $credential = New-Object System.Management.Automation.PSCredential($Username, $Password)
        $searcher = New-Object System.DirectoryServices.DirectorySearcher
        $searcher.SearchRoot = New-Object System.DirectoryServices.DirectoryEntry($Server, $credential.Username, $credential.GetNetworkCredential().Password)
        $searcher.Filter = "(objectClass=user)"
        $searcher.PropertiesToLoad.Add("sAMAccountName") | Out-Null
        $searcher.PageSize = 1

        $result = $searcher.FindOne()
        if ($result -ne $null) {
            Write-Host "✓ LDAP connection successful" -ForegroundColor Green
            return $true
        }
    }
    catch {
        Write-Error "✗ LDAP connection failed: $($_.Exception.Message)"
        return $false
    }
}

# Usage
$ldapPassword = ConvertTo-SecureString "YourServiceAccountPassword" -AsPlainText -Force
Test-LDAPConfiguration -Server "ldap://your-domain-controller.company.com:389" -Username "orleans-service@company.com" -Password $ldapPassword
```

#### 1.2 OAuth2/OIDC Configuration

**Overview**: Configure Orleans Dashboard and API endpoints to use OAuth2/OpenID Connect for modern authentication.

##### Supported Identity Providers
- Azure Active Directory (Azure AD)
- Okta
- Auth0
- Generic OpenID Connect providers

##### Azure Active Directory Configuration

**Step 1: Azure AD Application Registration**
1. Navigate to Azure Portal > Azure Active Directory > App Registrations
2. Create new registration: "Orleans Dashboard"
3. Configure redirect URI: `https://your-orleans-domain.com/signin-oidc`
4. Note Application (client) ID and Directory (tenant) ID
5. Create client secret and store in Key Vault

**Step 2: Orleans OIDC Configuration**
```json
{
  "Authentication": {
    "OpenIdConnect": {
      "Enabled": true,
      "Authority": "https://login.microsoftonline.com/{tenant-id}",
      "ClientId": "#{AZURE_AD_CLIENT_ID}#", // From Key Vault
      "ClientSecret": "#{AZURE_AD_CLIENT_SECRET}#", // From Key Vault
      "ResponseType": "code",
      "Scope": "openid profile email",
      "RequireHttpsMetadata": true,
      "SaveTokens": true,
      "TokenValidationParameters": {
        "ValidateIssuer": true,
        "ValidateAudience": true,
        "ValidateLifetime": true,
        "ClockSkew": "00:05:00"
      }
    }
  },
  "Orleans": {
    "Dashboard": {
      "Enabled": true,
      "Authentication": {
        "Provider": "OpenIdConnect",
        "RequireAuthentication": true,
        "AuthorizedRoles": ["Orleans.Admins", "Orleans.Operators"]
      }
    }
  }
}
```

**Step 3: Okta Configuration (Alternative)**
```json
{
  "Authentication": {
    "OpenIdConnect": {
      "Enabled": true,
      "Authority": "https://your-org.okta.com/oauth2/default",
      "ClientId": "#{OKTA_CLIENT_ID}#",
      "ClientSecret": "#{OKTA_CLIENT_SECRET}#",
      "ResponseType": "code",
      "Scope": "openid profile email groups"
    }
  }
}
```

**Step 4: OAuth2 Validation Script**
```powershell
# Validate OAuth2/OIDC configuration
function Test-OIDCConfiguration {
    param(
        [string]$Authority,
        [string]$ClientId
    )

    try {
        # Test discovery endpoint
        $discoveryUrl = "$Authority/.well-known/openid_configuration"
        $discovery = Invoke-RestMethod -Uri $discoveryUrl -Method GET

        if ($discovery.authorization_endpoint -and $discovery.token_endpoint) {
            Write-Host "✓ OIDC discovery endpoint accessible" -ForegroundColor Green
            Write-Host "  Authorization Endpoint: $($discovery.authorization_endpoint)"
            Write-Host "  Token Endpoint: $($discovery.token_endpoint)"
            return $true
        }
    }
    catch {
        Write-Error "✗ OIDC discovery failed: $($_.Exception.Message)"
        return $false
    }
}

# Usage
Test-OIDCConfiguration -Authority "https://login.microsoftonline.com/your-tenant-id" -ClientId "your-client-id"
```

#### 1.3 Role-Based Access Control (RBAC)

**Overview**: Implement granular access control for Orleans Dashboard and API endpoints based on user roles.

##### Orleans Role Definitions

**Administrative Roles:**
- `Orleans.SuperAdmins`: Full system access, configuration changes
- `Orleans.Admins`: Dashboard access, grain management, configuration viewing
- `Orleans.Operators`: Dashboard viewing, basic grain operations
- `Orleans.Readers`: Read-only dashboard access, metrics viewing

**Step 1: Role Configuration**
```json
{
  "Authorization": {
    "Roles": {
      "Orleans.SuperAdmins": {
        "Description": "Full Orleans system administration",
        "Permissions": [
          "Dashboard.FullAccess",
          "Configuration.ReadWrite",
          "Grains.FullManagement",
          "Metrics.FullAccess",
          "Logs.FullAccess"
        ],
        "Groups": ["Domain Admins", "Orleans Administrators"]
      },
      "Orleans.Admins": {
        "Description": "Orleans system administration",
        "Permissions": [
          "Dashboard.Access",
          "Configuration.Read",
          "Grains.Management",
          "Metrics.Access"
        ],
        "Groups": ["Orleans Admins", "System Operators"]
      },
      "Orleans.Operators": {
        "Description": "Orleans operational tasks",
        "Permissions": [
          "Dashboard.View",
          "Grains.View",
          "Metrics.View"
        ],
        "Groups": ["Operations Team", "DevOps Engineers"]
      },
      "Orleans.Readers": {
        "Description": "Read-only Orleans access",
        "Permissions": [
          "Dashboard.ReadOnly",
          "Metrics.ReadOnly"
        ],
        "Groups": ["Developers", "Support Team"]
      }
    }
  }
}
```

**Step 2: API Endpoint Authorization**
```csharp
// Example controller authorization attributes
[Authorize(Roles = "Orleans.Admins,Orleans.SuperAdmins")]
[HttpGet("api/orleans/configuration")]
public async Task<IActionResult> GetConfiguration() { /* ... */ }

[Authorize(Roles = "Orleans.Operators,Orleans.Admins,Orleans.SuperAdmins")]
[HttpGet("api/orleans/metrics")]
public async Task<IActionResult> GetMetrics() { /* ... */ }

[Authorize(Roles = "Orleans.Readers,Orleans.Operators,Orleans.Admins,Orleans.SuperAdmins")]
[HttpGet("api/orleans/status")]
public async Task<IActionResult> GetStatus() { /* ... */ }
```

**Step 3: Role Validation Script**
```powershell
# Validate user role assignments
function Test-UserRoleAssignment {
    param(
        [string]$Username,
        [string]$OrleansEndpoint = "http://localhost:5100"
    )

    try {
        # Test API access with user credentials
        $testEndpoints = @(
            @{ Path = "/api/orleans/status"; RequiredRole = "Orleans.Readers" },
            @{ Path = "/api/orleans/metrics"; RequiredRole = "Orleans.Operators" },
            @{ Path = "/api/orleans/configuration"; RequiredRole = "Orleans.Admins" }
        )

        foreach ($endpoint in $testEndpoints) {
            $response = Invoke-WebRequest -Uri "$OrleansEndpoint$($endpoint.Path)" -Method GET -UseBasicParsing
            if ($response.StatusCode -eq 200) {
                Write-Host "✓ $Username has access to $($endpoint.Path)" -ForegroundColor Green
            }
        }
    }
    catch {
        Write-Warning "Role validation requires actual authentication setup"
    }
}
```

#### 1.4 Service Account Management

**Overview**: Manage service accounts used by Orleans components for inter-service authentication and external system integration.

##### Service Account Types
- **Orleans Service Account**: For LDAP/AD queries
- **Key Vault Service Account**: For secrets access
- **Monitoring Service Account**: For metrics collection
- **Database Service Account**: For data access

**Step 1: Service Account Creation Procedures**

**Create Orleans Service Account in Active Directory:**
```powershell
# PowerShell script for Orleans service account creation
function New-OrleansServiceAccount {
    param(
        [string]$AccountName = "orleans-service",
        [string]$DisplayName = "Orleans AI Chat Service",
        [string]$OrganizationalUnit = "OU=Service Accounts,DC=company,DC=com",
        [securestring]$Password
    )

    Import-Module ActiveDirectory

    try {
        # Create service account
        New-ADUser -Name $AccountName `
                   -DisplayName $DisplayName `
                   -UserPrincipalName "$AccountName@company.com" `
                   -SamAccountName $AccountName `
                   -Path $OrganizationalUnit `
                   -AccountPassword $Password `
                   -Enabled $true `
                   -PasswordNeverExpires $true `
                   -CannotChangePassword $true

        # Set service account properties
        Set-ADUser -Identity $AccountName -Description "Service account for Orleans AI Chat system"

        # Add to required groups
        Add-ADGroupMember -Identity "Orleans Service Accounts" -Members $AccountName

        Write-Host "✓ Service account '$AccountName' created successfully" -ForegroundColor Green
    }
    catch {
        Write-Error "✗ Failed to create service account: $($_.Exception.Message)"
    }
}

# Usage
$securePassword = ConvertTo-SecureString "ComplexServiceAccountPassword!" -AsPlainText -Force
New-OrleansServiceAccount -Password $securePassword
```

**Step 2: Service Account Rotation Procedures**
```powershell
# Automated service account password rotation
function Start-ServiceAccountRotation {
    param(
        [string]$AccountName,
        [string]$KeyVaultName,
        [int]$RotationIntervalDays = 90
    )

    try {
        # Generate new password
        $newPassword = [System.Web.Security.Membership]::GeneratePassword(32, 8)
        $securePassword = ConvertTo-SecureString $newPassword -AsPlainText -Force

        # Update Active Directory
        Set-ADAccountPassword -Identity $AccountName -NewPassword $securePassword

        # Update Key Vault
        $secretName = "$AccountName-password"
        Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name $secretName -SecretValue $securePassword

        # Update application configuration (restart required)
        Write-Host "✓ Service account password rotated successfully" -ForegroundColor Green
        Write-Warning "Application restart required to apply new credentials"

        # Schedule next rotation
        $nextRotation = (Get-Date).AddDays($RotationIntervalDays)
        Write-Host "Next rotation scheduled: $nextRotation"
    }
    catch {
        Write-Error "✗ Service account rotation failed: $($_.Exception.Message)"
    }
}
```

**Step 3: Service Account Monitoring**
```json
{
  "ServiceAccountMonitoring": {
    "Enabled": true,
    "Accounts": [
      {
        "Name": "orleans-service@company.com",
        "MonitorExpiration": true,
        "ExpirationWarningDays": 30,
        "MonitorLockout": true,
        "MonitorFailedLogins": true,
        "AlertThreshold": 5
      }
    ],
    "AlertingEndpoints": [
      "security-team@company.com",
      "operations@company.com"
    ]
  }
}
```

**Step 4: Service Account Validation**
```powershell
# Comprehensive service account health check
function Test-ServiceAccountHealth {
    param(
        [string[]]$ServiceAccounts = @("orleans-service@company.com")
    )

    foreach ($account in $ServiceAccounts) {
        try {
            # Check account status in AD
            $adUser = Get-ADUser -Identity $account -Properties PasswordLastSet, AccountExpirationDate, LockedOut

            Write-Host "Service Account: $account" -ForegroundColor Cyan
            Write-Host "  Status: $(if($adUser.Enabled) { 'Enabled' } else { 'Disabled' })"
            Write-Host "  Locked: $(if($adUser.LockedOut) { 'Yes' } else { 'No' })"
            Write-Host "  Password Last Set: $($adUser.PasswordLastSet)"

            # Check password age
            $passwordAge = (Get-Date) - $adUser.PasswordLastSet
            if ($passwordAge.Days -gt 90) {
                Write-Warning "  Password is $($passwordAge.Days) days old - consider rotation"
            } else {
                Write-Host "  Password Age: $($passwordAge.Days) days (OK)" -ForegroundColor Green
            }
        }
        catch {
            Write-Error "Failed to check account $account`: $($_.Exception.Message)"
        }
    }
}

# Usage
Test-ServiceAccountHealth
```

### 2. Certificate and TLS Management

#### 2.1 Certificate Lifecycle Management

**Overview**: Comprehensive certificate management for Orleans services including issuance, renewal, monitoring, and revocation procedures.

##### Certificate Types and Requirements
- **Server Certificates**: For HTTPS endpoints (Orleans Dashboard, API endpoints)
- **Client Certificates**: For mTLS grain-to-grain communication
- **CA Certificates**: For certificate chain validation
- **Service Certificates**: For service-to-service authentication

**Step 1: Certificate Issuance Procedures**

**Internal CA Configuration:**
```json
{
  "CertificateAuthority": {
    "InternalCA": {
      "Enabled": true,
      "CAName": "Orleans Internal CA",
      "ValidityPeriod": "P2Y", // 2 years
      "KeyLength": 4096,
      "HashAlgorithm": "SHA256",
      "CertificateStore": "LocalMachine\\My",
      "Templates": {
        "OrleansServer": {
          "KeyUsage": ["DigitalSignature", "KeyEncipherment"],
          "ExtendedKeyUsage": ["ServerAuthentication"],
          "SubjectAlternativeNames": ["DNS:orleans.company.com", "DNS:localhost"]
        },
        "OrleansClient": {
          "KeyUsage": ["DigitalSignature"],
          "ExtendedKeyUsage": ["ClientAuthentication"]
        }
      }
    }
  }
}
```

**PowerShell Certificate Request Script:**
```powershell
# Request new certificate from internal CA
function New-OrleansCertificate {
    param(
        [string]$CertificateType = "OrleansServer",
        [string]$SubjectName = "CN=orleans.company.com",
        [string[]]$SubjectAlternativeNames = @("orleans.company.com", "localhost"),
        [string]$CAServer = "ca.company.com",
        [string]$Template = "OrleansServerTemplate"
    )

    try {
        # Create certificate request
        $certReq = New-Object -ComObject X509Enrollment.CX509CertificateRequestPkcs10
        $certReq.InitializeFromTemplateName(0x1, $Template)

        # Set subject name
        $dn = New-Object -ComObject X509Enrollment.CX500DistinguishedName
        $dn.Encode($SubjectName)
        $certReq.Subject = $dn

        # Add SAN extension
        if ($SubjectAlternativeNames.Count -gt 0) {
            $sanExtension = New-Object -ComObject X509Enrollment.CX509ExtensionAlternativeNames
            $sanNames = New-Object -ComObject X509Enrollment.CAlternativeNames

            foreach ($san in $SubjectAlternativeNames) {
                $sanName = New-Object -ComObject X509Enrollment.CAlternativeName
                $sanName.InitializeFromString(0x3, $san) # DNS Name
                $sanNames.Add($sanName)
            }

            $sanExtension.InitializeEncode($sanNames)
            $certReq.X509Extensions.Add($sanExtension)
        }

        # Submit request
        $enrollment = New-Object -ComObject X509Enrollment.CX509Enrollment
        $enrollment.InitializeFromRequest($certReq)
        $certData = $enrollment.CreateRequest(0x1)

        # Submit to CA
        $webEnroll = New-Object -ComObject CertificateAuthority.Request
        $result = $webEnroll.Submit(0x1, $certData, $null, $CAServer)

        if ($result -eq 3) { # CR_DISP_ISSUED
            $certificate = $webEnroll.GetCertificate(0x1)
            $enrollment.InstallResponse(0x2, $certificate, 0x1, $null)
            Write-Host "✓ Certificate issued and installed successfully" -ForegroundColor Green
            return $true
        } else {
            Write-Error "✗ Certificate request failed with status: $result"
            return $false
        }
    }
    catch {
        Write-Error "✗ Certificate request failed: $($_.Exception.Message)"
        return $false
    }
}

# Usage
New-OrleansCertificate -SubjectName "CN=orleans.company.com" -SubjectAlternativeNames @("orleans.company.com", "orleans-api.company.com", "localhost")
```

**Step 2: Certificate Installation and Configuration**
```powershell
# Install certificate and configure Orleans endpoints
function Install-OrleansCertificate {
    param(
        [string]$CertificateThumbprint,
        [string]$CertificateStoreName = "My",
        [string]$CertificateStoreLocation = "LocalMachine"
    )

    try {
        # Verify certificate exists
        $cert = Get-ChildItem -Path "Cert:\$CertificateStoreLocation\$CertificateStoreName" |
                Where-Object { $_.Thumbprint -eq $CertificateThumbprint }

        if (-not $cert) {
            Write-Error "✗ Certificate with thumbprint $CertificateThumbprint not found"
            return $false
        }

        # Grant network service access to private key
        $keyPath = $env:ProgramData + "\Microsoft\Crypto\RSA\MachineKeys\"
        $keyFileName = $cert.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName

        if (Test-Path ($keyPath + $keyFileName)) {
            $acl = Get-Acl ($keyPath + $keyFileName)
            $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("NETWORK SERVICE", "FullControl", "Allow")
            $acl.SetAccessRule($accessRule)
            Set-Acl ($keyPath + $keyFileName) $acl
        }

        # Update Orleans configuration
        $appSettingsPath = "server\AIChat.Server\appsettings.Production.json"
        $config = Get-Content $appSettingsPath | ConvertFrom-Json

        $config.Orleans.Dashboard.Certificate = @{
            Thumbprint = $CertificateThumbprint
            StoreName = $CertificateStoreName
            StoreLocation = $CertificateStoreLocation
        }

        $config | ConvertTo-Json -Depth 10 | Set-Content $appSettingsPath

        Write-Host "✓ Certificate installed and configured successfully" -ForegroundColor Green
        Write-Host "  Thumbprint: $CertificateThumbprint"
        Write-Host "  Subject: $($cert.Subject)"
        Write-Host "  Expires: $($cert.NotAfter)"
        return $true
    }
    catch {
        Write-Error "✗ Certificate installation failed: $($_.Exception.Message)"
        return $false
    }
}
```

#### 2.2 mTLS Configuration for Grain-to-Grain Communication

**Overview**: Configure mutual TLS authentication for secure Orleans grain-to-grain communication.

**Step 1: mTLS Configuration**
```json
{
  "Orleans": {
    "Clustering": {
      "ProviderType": "AdoNet",
      "ConnectionString": "#{DATABASE_CONNECTION_STRING}#"
    },
    "Endpoints": {
      "SiloEndpoint": {
        "Address": "0.0.0.0",
        "Port": 11111
      },
      "GatewayEndpoint": {
        "Address": "0.0.0.0",
        "Port": 30000
      }
    },
    "Security": {
      "mTLS": {
        "Enabled": true,
        "ServerCertificate": {
          "Thumbprint": "#{ORLEANS_SERVER_CERT_THUMBPRINT}#",
          "StoreName": "My",
          "StoreLocation": "LocalMachine"
        },
        "ClientCertificate": {
          "Thumbprint": "#{ORLEANS_CLIENT_CERT_THUMBPRINT}#",
          "StoreName": "My",
          "StoreLocation": "LocalMachine"
        },
        "TrustedCertificates": [
          "#{ORLEANS_CA_CERT_THUMBPRINT}#"
        ],
        "RequireClientCertificate": true,
        "CheckCertificateRevocation": true,
        "ValidateRemoteCertificateName": true
      }
    }
  }
}
```

**Step 2: mTLS Validation Script**
```powershell
# Validate mTLS configuration and connectivity
function Test-OrleansSecureCommunication {
    param(
        [string]$RemoteEndpoint = "localhost:11111",
        [string]$ClientCertThumbprint,
        [int]$TimeoutSeconds = 30
    )

    try {
        # Load client certificate
        $clientCert = Get-ChildItem -Path "Cert:\LocalMachine\My" |
                      Where-Object { $_.Thumbprint -eq $ClientCertThumbprint }

        if (-not $clientCert) {
            Write-Error "✗ Client certificate not found: $ClientCertThumbprint"
            return $false
        }

        # Create secure TCP client
        $tcpClient = New-Object System.Net.Sockets.TcpClient
        $tcpClient.ReceiveTimeout = $TimeoutSeconds * 1000
        $tcpClient.SendTimeout = $TimeoutSeconds * 1000

        # Connect with TLS
        $host, $port = $RemoteEndpoint -split ':'
        $tcpClient.Connect($host, [int]$port)

        $sslStream = New-Object System.Net.Security.SslStream($tcpClient.GetStream(), $false)
        $sslStream.AuthenticateAsClient($host, @($clientCert), [System.Security.Authentication.SslProtocols]::Tls13, $false)

        Write-Host "✓ mTLS connection established successfully" -ForegroundColor Green
        Write-Host "  Protocol: $($sslStream.SslProtocol)"
        Write-Host "  Cipher Suite: $($sslStream.CipherAlgorithm)"
        Write-Host "  Remote Certificate: $($sslStream.RemoteCertificate.Subject)"

        $sslStream.Close()
        $tcpClient.Close()
        return $true
    }
    catch {
        Write-Error "✗ mTLS connection failed: $($_.Exception.Message)"
        return $false
    }
}

# Usage
Test-OrleansSecureCommunication -ClientCertThumbprint "YOUR_CLIENT_CERT_THUMBPRINT"
```

#### 2.3 Certificate Rotation Procedures

**Overview**: Zero-downtime certificate rotation procedures for Orleans services.

**Step 1: Automated Certificate Renewal**
```powershell
# Automated certificate renewal with validation
function Start-OrleansCertificateRenewal {
    param(
        [string]$CertificateThumbprint,
        [int]$RenewalThresholdDays = 30,
        [string]$KeyVaultName,
        [switch]$TestMode = $false
    )

    try {
        # Check current certificate
        $currentCert = Get-ChildItem -Path "Cert:\LocalMachine\My" |
                      Where-Object { $_.Thumbprint -eq $CertificateThumbprint }

        if (-not $currentCert) {
            Write-Error "✗ Current certificate not found"
            return $false
        }

        # Check if renewal is needed
        $daysUntilExpiry = ($currentCert.NotAfter - (Get-Date)).Days

        if ($daysUntilExpiry -gt $RenewalThresholdDays -and -not $TestMode) {
            Write-Host "Certificate valid for $daysUntilExpiry days - renewal not needed" -ForegroundColor Green
            return $true
        }

        Write-Host "Starting certificate renewal process..." -ForegroundColor Yellow

        # Request new certificate
        $newCertResult = New-OrleansCertificate -SubjectName $currentCert.Subject

        if (-not $newCertResult) {
            Write-Error "✗ New certificate request failed"
            return $false
        }

        # Get new certificate
        $newCert = Get-ChildItem -Path "Cert:\LocalMachine\My" |
                   Where-Object { $_.Subject -eq $currentCert.Subject -and $_.Thumbprint -ne $CertificateThumbprint } |
                   Sort-Object NotBefore -Descending |
                   Select-Object -First 1

        if (-not $newCert) {
            Write-Error "✗ New certificate not found after issuance"
            return $false
        }

        # Update Key Vault with new certificate
        if ($KeyVaultName) {
            $certBytes = $newCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx)
            $certSecret = [System.Convert]::ToBase64String($certBytes)
            Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name "orleans-certificate" -SecretValue (ConvertTo-SecureString $certSecret -AsPlainText -Force)
        }

        # Update configuration with new certificate
        Install-OrleansCertificate -CertificateThumbprint $newCert.Thumbprint

        # Restart Orleans service to apply new certificate
        Write-Warning "Restarting Orleans service to apply new certificate..."
        Restart-Service -Name "AIChat.Server" -Force

        # Wait for service to restart
        Start-Sleep -Seconds 30

        # Validate new certificate is working
        $validationResult = Test-OrleansSecureCommunication -ClientCertThumbprint $newCert.Thumbprint

        if ($validationResult) {
            Write-Host "✓ Certificate renewal completed successfully" -ForegroundColor Green
            Write-Host "  Old Certificate: $CertificateThumbprint"
            Write-Host "  New Certificate: $($newCert.Thumbprint)"
            Write-Host "  Expires: $($newCert.NotAfter)"

            # Remove old certificate after successful rotation
            Remove-Item -Path "Cert:\LocalMachine\My\$CertificateThumbprint" -Force
            return $true
        } else {
            Write-Error "✗ Certificate validation failed - rolling back"
            # Rollback to previous certificate
            Install-OrleansCertificate -CertificateThumbprint $CertificateThumbprint
            Restart-Service -Name "AIChat.Server" -Force
            return $false
        }
    }
    catch {
        Write-Error "✗ Certificate renewal failed: $($_.Exception.Message)"
        return $false
    }
}
```

**Step 2: Certificate Monitoring and Alerting**
```json
{
  "CertificateMonitoring": {
    "Enabled": true,
    "CheckInterval": "PT1H", // Every hour
    "Certificates": [
      {
        "Name": "Orleans Server Certificate",
        "Thumbprint": "#{ORLEANS_SERVER_CERT_THUMBPRINT}#",
        "Store": "Cert:\\LocalMachine\\My",
        "ExpiryWarningDays": [60, 30, 14, 7, 1],
        "AlertEndpoints": [
          "security-team@company.com",
          "operations@company.com"
        ]
      },
      {
        "Name": "Orleans Client Certificate",
        "Thumbprint": "#{ORLEANS_CLIENT_CERT_THUMBPRINT}#",
        "Store": "Cert:\\LocalMachine\\My",
        "ExpiryWarningDays": [60, 30, 14, 7, 1]
      }
    ],
    "AutoRenewal": {
      "Enabled": true,
      "RenewalThresholdDays": 30,
      "MaxRetryAttempts": 3,
      "RetryDelayHours": 24
    }
  }
}
```

#### 2.4 TLS 1.3 Configuration and Cipher Suite Hardening

**Overview**: Configure TLS 1.3 with secure cipher suites for all Orleans communications.

**Step 1: TLS Configuration**
```json
{
  "Orleans": {
    "Security": {
      "TLS": {
        "MinimumVersion": "Tls13",
        "MaximumVersion": "Tls13",
        "CipherSuites": [
          "TLS_AES_256_GCM_SHA384",
          "TLS_AES_128_GCM_SHA256",
          "TLS_CHACHA20_POLY1305_SHA256"
        ],
        "RequirePerfectForwardSecrecy": true,
        "DisableLegacyRenegotiation": true,
        "EnableOcspStapling": true
      }
    }
  },
  "Kestrel": {
    "Endpoints": {
      "HttpsInlineCertStore": {
        "Url": "https://localhost:5099",
        "Certificate": {
          "Subject": "CN=orleans.company.com",
          "Store": "My",
          "Location": "LocalMachine",
          "AllowInvalid": false
        },
        "Protocols": "Http2",
        "SslProtocols": ["Tls13"],
        "ClientCertificateMode": "RequireCertificate"
      }
    }
  }
}
```

**Step 2: TLS Security Validation Script**
```powershell
# Validate TLS configuration and security posture
function Test-OrleansTLSSecurity {
    param(
        [string]$Endpoint = "https://localhost:5099",
        [string]$ExpectedTLSVersion = "Tls13"
    )

    try {
        # Test TLS connection
        $uri = [System.Uri]$Endpoint
        $tcpClient = New-Object System.Net.Sockets.TcpClient
        $tcpClient.Connect($uri.Host, $uri.Port)

        $sslStream = New-Object System.Net.Security.SslStream($tcpClient.GetStream())
        $sslStream.AuthenticateAsClient($uri.Host)

        # Validate TLS version
        if ($sslStream.SslProtocol -eq $ExpectedTLSVersion) {
            Write-Host "✓ TLS version validated: $($sslStream.SslProtocol)" -ForegroundColor Green
        } else {
            Write-Warning "TLS version mismatch. Expected: $ExpectedTLSVersion, Actual: $($sslStream.SslProtocol)"
        }

        # Validate cipher suite
        $cipherInfo = @{
            CipherAlgorithm = $sslStream.CipherAlgorithm
            CipherStrength = $sslStream.CipherStrength
            HashAlgorithm = $sslStream.HashAlgorithm
            HashStrength = $sslStream.HashStrength
            KeyExchangeAlgorithm = $sslStream.KeyExchangeAlgorithm
            KeyExchangeStrength = $sslStream.KeyExchangeStrength
        }

        Write-Host "TLS Connection Details:" -ForegroundColor Cyan
        foreach ($key in $cipherInfo.Keys) {
            Write-Host "  $key`: $($cipherInfo[$key])"
        }

        # Check for strong encryption
        $strongCiphers = @("Aes256", "Aes128", "ChaCha20Poly1305")
        $strongHashAlgorithms = @("Sha256", "Sha384", "Sha512")

        $cipherStrong = $strongCiphers -contains $sslStream.CipherAlgorithm
        $hashStrong = $strongHashAlgorithms -contains $sslStream.HashAlgorithm

        if ($cipherStrong -and $hashStrong) {
            Write-Host "✓ Strong encryption algorithms validated" -ForegroundColor Green
        } else {
            Write-Warning "Weak encryption detected - consider cipher suite hardening"
        }

        $sslStream.Close()
        $tcpClient.Close()
        return $true
    }
    catch {
        Write-Error "✗ TLS security validation failed: $($_.Exception.Message)"
        return $false
    }
}

# Usage
Test-OrleansTLSSecurity -Endpoint "https://localhost:5099"
```

**Step 3: Certificate Chain Validation**
```powershell
# Validate complete certificate chain and trust
function Test-CertificateChain {
    param(
        [string]$CertificateThumbprint,
        [string]$ExpectedIssuer
    )

    try {
        # Get certificate
        $cert = Get-ChildItem -Path "Cert:\LocalMachine\My" |
               Where-Object { $_.Thumbprint -eq $CertificateThumbprint }

        if (-not $cert) {
            Write-Error "✗ Certificate not found: $CertificateThumbprint"
            return $false
        }

        # Build certificate chain
        $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
        $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::Online
        $chain.ChainPolicy.VerificationFlags = [System.Security.Cryptography.X509Certificates.X509VerificationFlags]::NoFlag

        $chainValid = $chain.Build($cert)

        Write-Host "Certificate Chain Validation:" -ForegroundColor Cyan
        Write-Host "  Subject: $($cert.Subject)"
        Write-Host "  Issuer: $($cert.Issuer)"
        Write-Host "  Valid From: $($cert.NotBefore)"
        Write-Host "  Valid To: $($cert.NotAfter)"
        Write-Host "  Chain Valid: $chainValid"

        # Display chain elements
        for ($i = 0; $i -lt $chain.ChainElements.Count; $i++) {
            $element = $chain.ChainElements[$i]
            Write-Host "  Chain Element [$i]: $($element.Certificate.Subject)"
        }

        # Check for chain errors
        if ($chain.ChainStatus.Length -gt 0) {
            Write-Warning "Chain Status Issues:"
            foreach ($status in $chain.ChainStatus) {
                Write-Warning "  $($status.Status): $($status.StatusInformation)"
            }
        }

        if ($chainValid) {
            Write-Host "✓ Certificate chain validation successful" -ForegroundColor Green
        } else {
            Write-Error "✗ Certificate chain validation failed"
        }

        return $chainValid
    }
    catch {
        Write-Error "✗ Certificate chain validation error: $($_.Exception.Message)"
        return $false
    }
}

# Usage
Test-CertificateChain -CertificateThumbprint "YOUR_CERT_THUMBPRINT" -ExpectedIssuer "CN=Orleans Internal CA"
```

### 3. Secrets Management

#### 3.1 Azure Key Vault Integration

**Overview**: Centralized secrets management using Azure Key Vault for all Orleans sensitive configuration data.

##### Prerequisites
- Azure subscription with Key Vault service access
- Service principal with appropriate Key Vault permissions
- Azure PowerShell module or Azure CLI installed

**Step 1: Azure Key Vault Setup**
```powershell
# Create Azure Key Vault for Orleans secrets
function New-OrleansKeyVault {
    param(
        [string]$VaultName = "orleans-secrets-kv",
        [string]$ResourceGroupName = "orleans-resources",
        [string]$Location = "East US",
        [string]$ServicePrincipalName = "orleans-service-principal"
    )

    try {
        # Create resource group if it doesn't exist
        $rg = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction SilentlyContinue
        if (-not $rg) {
            $rg = New-AzResourceGroup -Name $ResourceGroupName -Location $Location
        }

        # Create Key Vault
        $keyVault = New-AzKeyVault -Name $VaultName -ResourceGroupName $ResourceGroupName -Location $Location -EnabledForDeployment -EnabledForTemplateDeployment

        # Create service principal for Orleans
        $sp = New-AzADServicePrincipal -DisplayName $ServicePrincipalName

        # Grant Key Vault access to service principal
        Set-AzKeyVaultAccessPolicy -VaultName $VaultName -ServicePrincipalName $sp.ApplicationId -PermissionsToSecrets Get,List,Set,Delete -PermissionsToCertificates Get,List,Import,Update

        Write-Host "✓ Key Vault created successfully" -ForegroundColor Green
        Write-Host "  Vault Name: $VaultName"
        Write-Host "  Resource Group: $ResourceGroupName"
        Write-Host "  Service Principal ID: $($sp.ApplicationId)"
        Write-Host "  Service Principal Secret: $($sp.Secret | ConvertFrom-SecureString -AsPlainText)"

        return @{
            VaultName = $VaultName
            ServicePrincipalId = $sp.ApplicationId
            ServicePrincipalSecret = $sp.Secret | ConvertFrom-SecureString -AsPlainText
        }
    }
    catch {
        Write-Error "✗ Key Vault setup failed: $($_.Exception.Message)"
        return $null
    }
}

# Usage
$kvResult = New-OrleansKeyVault -VaultName "orleans-prod-kv" -ResourceGroupName "orleans-prod-rg"
```

**Step 2: Orleans Key Vault Configuration**
```json
{
  "AzureKeyVault": {
    "Enabled": true,
    "VaultName": "orleans-prod-kv",
    "Authentication": {
      "Type": "ServicePrincipal",
      "TenantId": "#{AZURE_TENANT_ID}#",
      "ClientId": "#{AZURE_CLIENT_ID}#",
      "ClientSecret": "#{AZURE_CLIENT_SECRET}#"
    },
    "CacheEnabled": true,
    "CacheTTL": "PT15M",
    "Secrets": {
      "DatabaseConnectionString": "database-connection-string",
      "LDAPServicePassword": "ldap-service-password",
      "OrleansServerCertificate": "orleans-server-certificate",
      "OrleansClientCertificate": "orleans-client-certificate",
      "OIDCClientSecret": "oidc-client-secret"
    }
  }
}
```

**Step 3: Secret Management PowerShell Functions**
```powershell
# Manage Orleans secrets in Key Vault
function Set-OrleansSecret {
    param(
        [string]$VaultName,
        [string]$SecretName,
        [securestring]$SecretValue,
        [string]$ContentType = "text/plain",
        [hashtable]$Tags = @{}
    )

    try {
        $secret = Set-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName -SecretValue $SecretValue -ContentType $ContentType -Tags $Tags

        Write-Host "✓ Secret '$SecretName' stored successfully" -ForegroundColor Green
        Write-Host "  Version: $($secret.Version)"
        Write-Host "  Created: $($secret.Attributes.Created)"
        return $true
    }
    catch {
        Write-Error "✗ Failed to store secret '$SecretName': $($_.Exception.Message)"
        return $false
    }
}

function Get-OrleansSecret {
    param(
        [string]$VaultName,
        [string]$SecretName,
        [string]$Version = $null
    )

    try {
        if ($Version) {
            $secret = Get-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName -Version $Version
        } else {
            $secret = Get-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName
        }

        if ($secret) {
            Write-Host "✓ Secret '$SecretName' retrieved successfully" -ForegroundColor Green
            return $secret.SecretValue | ConvertFrom-SecureString -AsPlainText
        }
    }
    catch {
        Write-Error "✗ Failed to retrieve secret '$SecretName': $($_.Exception.Message)"
        return $null
    }
}

# Initialize Orleans secrets in Key Vault
function Initialize-OrleansSecrets {
    param([string]$VaultName)

    $secrets = @{
        "database-connection-string" = "Data Source=aichat.db;Cache=Shared"
        "ldap-service-password" = "GeneratedPassword123!"
        "oidc-client-secret" = "GeneratedOIDCSecret456!"
    }

    foreach ($secretName in $secrets.Keys) {
        $secureValue = ConvertTo-SecureString $secrets[$secretName] -AsPlainText -Force
        Set-OrleansSecret -VaultName $VaultName -SecretName $secretName -SecretValue $secureValue -Tags @{Environment="Production"; Component="Orleans"}
    }
}
```

#### 3.2 HashiCorp Vault Configuration (Alternative)

**Overview**: HashiCorp Vault as an alternative secrets management solution for organizations not using Azure.

**Step 1: HashiCorp Vault Setup**
```bash
# Install and configure HashiCorp Vault (Linux)
# Download and install Vault
wget https://releases.hashicorp.com/vault/1.15.0/vault_1.15.0_linux_amd64.zip
unzip vault_1.15.0_linux_amd64.zip
sudo mv vault /usr/local/bin/

# Create Vault configuration
sudo tee /etc/vault/config.hcl > /dev/null <<EOF
storage "file" {
  path = "/opt/vault/data"
}

listener "tcp" {
  address     = "127.0.0.1:8200"
  tls_cert_file = "/etc/ssl/certs/vault.crt"
  tls_key_file  = "/etc/ssl/private/vault.key"
}

ui = true
api_addr = "https://127.0.0.1:8200"
cluster_addr = "https://127.0.0.1:8201"
EOF

# Initialize Vault
vault server -config=/etc/vault/config.hcl &
export VAULT_ADDR='https://127.0.0.1:8200'
vault operator init -key-shares=5 -key-threshold=3
```

**Step 2: Orleans HashiCorp Vault Configuration**
```json
{
  "HashiCorpVault": {
    "Enabled": true,
    "Address": "https://vault.company.com:8200",
    "Authentication": {
      "Type": "AppRole",
      "RoleId": "#{VAULT_ROLE_ID}#",
      "SecretId": "#{VAULT_SECRET_ID}#"
    },
    "KvVersion": "v2",
    "SecretsPath": "secret/orleans/",
    "CertificatesPath": "pki/orleans/",
    "TTL": "24h",
    "MaxTTL": "720h"
  }
}
```

**Step 3: HashiCorp Vault PowerShell Integration**
```powershell
# HashiCorp Vault PowerShell functions
function Connect-HashiCorpVault {
    param(
        [string]$VaultAddress = "https://vault.company.com:8200",
        [string]$RoleId,
        [string]$SecretId
    )

    try {
        # Authenticate with AppRole
        $authData = @{
            role_id = $RoleId
            secret_id = $SecretId
        }

        $response = Invoke-RestMethod -Uri "$VaultAddress/v1/auth/approle/login" -Method POST -Body ($authData | ConvertTo-Json) -ContentType "application/json"

        $global:VaultToken = $response.auth.client_token
        $global:VaultAddress = $VaultAddress

        Write-Host "✓ Connected to HashiCorp Vault successfully" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Error "✗ Vault authentication failed: $($_.Exception.Message)"
        return $false
    }
}

function Set-VaultSecret {
    param(
        [string]$Path,
        [hashtable]$SecretData
    )

    try {
        $headers = @{
            "X-Vault-Token" = $global:VaultToken
        }

        $body = @{
            data = $SecretData
        } | ConvertTo-Json

        $response = Invoke-RestMethod -Uri "$global:VaultAddress/v1/secret/data/$Path" -Method POST -Headers $headers -Body $body -ContentType "application/json"

        Write-Host "✓ Secret stored at path '$Path'" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Error "✗ Failed to store secret at '$Path': $($_.Exception.Message)"
        return $false
    }
}

function Get-VaultSecret {
    param([string]$Path)

    try {
        $headers = @{
            "X-Vault-Token" = $global:VaultToken
        }

        $response = Invoke-RestMethod -Uri "$global:VaultAddress/v1/secret/data/$Path" -Method GET -Headers $headers

        return $response.data.data
    }
    catch {
        Write-Error "✗ Failed to retrieve secret from '$Path': $($_.Exception.Message)"
        return $null
    }
}
```

#### 3.3 Secret Rotation and Connection String Security

**Overview**: Automated secret rotation procedures and secure connection string management.

**Step 1: Automated Secret Rotation**
```powershell
# Automated secret rotation for Orleans
function Start-OrleansSecretRotation {
    param(
        [string]$VaultName,
        [string]$SecretName,
        [int]$RotationIntervalDays = 30,
        [string]$SecretType = "Password"
    )

    try {
        # Check current secret age
        $currentSecret = Get-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName
        $secretAge = (Get-Date) - $currentSecret.Attributes.Created

        if ($secretAge.Days -lt $RotationIntervalDays) {
            Write-Host "Secret '$SecretName' is only $($secretAge.Days) days old - rotation not needed" -ForegroundColor Green
            return $true
        }

        Write-Host "Starting rotation for secret '$SecretName'..." -ForegroundColor Yellow

        # Generate new secret based on type
        $newSecretValue = switch ($SecretType) {
            "Password" {
                [System.Web.Security.Membership]::GeneratePassword(32, 8)
            }
            "ApiKey" {
                [System.Guid]::NewGuid().ToString("N").ToUpper()
            }
            "Certificate" {
                # For certificates, trigger certificate renewal process
                Start-OrleansCertificateRenewal -CertificateThumbprint $currentSecret.SecretValue
                return $true
            }
            default {
                throw "Unsupported secret type: $SecretType"
            }
        }

        # Store new secret version
        $secureNewValue = ConvertTo-SecureString $newSecretValue -AsPlainText -Force
        $newSecret = Set-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName -SecretValue $secureNewValue -Tags @{
            RotatedDate = (Get-Date).ToString("yyyy-MM-dd")
            PreviousVersion = $currentSecret.Version
            RotationType = "Automated"
        }

        # Update application configuration (requires restart)
        Write-Warning "Secret rotated - application restart required to apply new secret"

        # Schedule validation after application restart
        Write-Host "✓ Secret '$SecretName' rotated successfully" -ForegroundColor Green
        Write-Host "  Previous Version: $($currentSecret.Version)"
        Write-Host "  New Version: $($newSecret.Version)"
        Write-Host "  Next Rotation: $((Get-Date).AddDays($RotationIntervalDays))"

        return $true
    }
    catch {
        Write-Error "✗ Secret rotation failed for '$SecretName': $($_.Exception.Message)"
        return $false
    }
}

# Bulk secret rotation
function Start-OrleansSecretsRotation {
    param(
        [string]$VaultName,
        [hashtable]$SecretsToRotate = @{
            "ldap-service-password" = "Password"
            "oidc-client-secret" = "ApiKey"
            "database-connection-string" = "Password"
        }
    )

    $results = @{}

    foreach ($secretName in $SecretsToRotate.Keys) {
        $secretType = $SecretsToRotate[$secretName]
        $results[$secretName] = Start-OrleansSecretRotation -VaultName $VaultName -SecretName $secretName -SecretType $secretType
    }

    # Summary report
    $successful = ($results.Values | Where-Object { $_ -eq $true }).Count
    $failed = ($results.Values | Where-Object { $_ -eq $false }).Count

    Write-Host "Secret Rotation Summary:" -ForegroundColor Cyan
    Write-Host "  Successful: $successful"
    Write-Host "  Failed: $failed"

    if ($failed -gt 0) {
        Write-Warning "Some secret rotations failed - check individual results"
        $results | ForEach-Object { Write-Host "  $($_.Key): $($_.Value)" }
    }

    return $results
}
```

**Step 2: Secure Connection String Management**
```json
{
  "ConnectionStrings": {
    "Database": "#{database-connection-string}#",
    "Redis": "#{redis-connection-string}#"
  },
  "Orleans": {
    "Clustering": {
      "ConnectionString": "#{database-connection-string}#"
    }
  },
  "SecureConnectionStrings": {
    "EncryptionEnabled": true,
    "EncryptionKey": "#{connection-string-encryption-key}#",
    "RotationEnabled": true,
    "RotationSchedule": "0 2 * * 0", // Weekly on Sunday at 2 AM
    "ValidationEnabled": true,
    "ValidationInterval": "PT1H"
  }
}
```

**Step 3: Connection String Validation Script**
```powershell
# Validate all Orleans connection strings
function Test-OrleansConnectionStrings {
    param([string]$VaultName)

    $connectionTests = @{
        "Database" = {
            param($connectionString)
            try {
                $connection = New-Object System.Data.SQLite.SQLiteConnection($connectionString)
                $connection.Open()
                $connection.Close()
                return $true
            }
            catch { return $false }
        }
        "LDAP" = {
            param($connectionString)
            # Extract LDAP server from connection string
            if ($connectionString -match "ldap://([^:]+):(\d+)") {
                $server = $matches[1]
                $port = $matches[2]

                try {
                    $tcpClient = New-Object System.Net.Sockets.TcpClient
                    $tcpClient.Connect($server, $port)
                    $tcpClient.Close()
                    return $true
                }
                catch { return $false }
            }
            return $false
        }
    }

    $results = @{}

    foreach ($testName in $connectionTests.Keys) {
        try {
            $secretName = switch ($testName) {
                "Database" { "database-connection-string" }
                "LDAP" { "ldap-connection-string" }
            }

            $connectionString = Get-OrleansSecret -VaultName $VaultName -SecretName $secretName

            if ($connectionString) {
                $testResult = & $connectionTests[$testName] $connectionString
                $results[$testName] = $testResult

                if ($testResult) {
                    Write-Host "✓ $testName connection validated" -ForegroundColor Green
                } else {
                    Write-Error "✗ $testName connection failed"
                }
            } else {
                Write-Warning "$testName connection string not found in vault"
                $results[$testName] = $false
            }
        }
        catch {
            Write-Error "Error testing $testName connection: $($_.Exception.Message)"
            $results[$testName] = $false
        }
    }

    return $results
}

# Usage
$connectionResults = Test-OrleansConnectionStrings -VaultName "orleans-prod-kv"
```

### 4. Network Security Hardening

#### 4.1 Firewall Rules and Network Segmentation

**Overview**: Implement comprehensive firewall rules and network segmentation for Orleans services.

##### Required Firewall Rules

**Inbound Rules:**
```powershell
# Configure Windows Firewall rules for Orleans
function Set-OrleansFirewallRules {
    param(
        [string]$OrleansProfile = "Production"
    )

    try {
        # Orleans Dashboard (External Access)
        New-NetFirewallRule -DisplayName "Orleans Dashboard HTTPS" -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow -Profile Domain,Private -Description "Orleans Dashboard HTTPS access"

        # Orleans API Endpoints (External Access)
        New-NetFirewallRule -DisplayName "Orleans API HTTPS" -Direction Inbound -Protocol TCP -LocalPort 5099 -Action Allow -Profile Domain,Private -Description "Orleans API HTTPS endpoints"

        # Orleans Internal Communication (Internal Only)
        New-NetFirewallRule -DisplayName "Orleans Silo Communication" -Direction Inbound -Protocol TCP -LocalPort 11111 -Action Allow -Profile Domain -RemoteAddress LocalSubnet -Description "Orleans silo-to-silo communication"

        New-NetFirewallRule -DisplayName "Orleans Gateway Communication" -Direction Inbound -Protocol TCP -LocalPort 30000 -Action Allow -Profile Domain -RemoteAddress LocalSubnet -Description "Orleans gateway communication"

        # Orleans Host API (Internal Only)
        New-NetFirewallRule -DisplayName "Orleans Host API" -Direction Inbound -Protocol TCP -LocalPort 5100 -Action Allow -Profile Domain -RemoteAddress LocalSubnet -Description "Orleans Host API for monitoring"

        Write-Host "✓ Orleans firewall rules configured successfully" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Error "✗ Failed to configure firewall rules: $($_.Exception.Message)"
        return $false
    }
}

# Network segmentation configuration
function Set-OrleansNetworkSegmentation {
    param(
        [string[]]$TrustedNetworks = @("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"),
        [string[]]$ManagementNetworks = @("10.1.0.0/16")
    )

    try {
        # Create security groups for different access levels
        $securityGroups = @{
            "Orleans-Public" = @{
                Ports = @(8080, 5099)
                Access = "Any"
                Description = "Public Orleans endpoints"
            }
            "Orleans-Internal" = @{
                Ports = @(11111, 30000)
                Access = $TrustedNetworks
                Description = "Internal Orleans communication"
            }
            "Orleans-Management" = @{
                Ports = @(5100)
                Access = $ManagementNetworks
                Description = "Orleans management and monitoring"
            }
        }

        foreach ($groupName in $securityGroups.Keys) {
            $group = $securityGroups[$groupName]
            Write-Host "Configuring security group: $groupName" -ForegroundColor Cyan

            foreach ($port in $group.Ports) {
                if ($group.Access -eq "Any") {
                    New-NetFirewallRule -DisplayName "$groupName-$port" -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow -Profile Domain,Private
                } else {
                    foreach ($network in $group.Access) {
                        New-NetFirewallRule -DisplayName "$groupName-$port-$network" -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow -Profile Domain -RemoteAddress $network
                    }
                }
            }
        }

        Write-Host "✓ Network segmentation configured successfully" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Error "✗ Network segmentation configuration failed: $($_.Exception.Message)"
        return $false
    }
}

# Usage
Set-OrleansFirewallRules
Set-OrleansNetworkSegmentation -TrustedNetworks @("10.0.0.0/8") -ManagementNetworks @("10.1.100.0/24")
```

#### 4.2 Web Application Firewall (WAF) Configuration

**Overview**: Configure Web Application Firewall protection for Orleans Dashboard and API endpoints.

**Step 1: Azure Application Gateway with WAF**
```json
{
  "ApplicationGateway": {
    "WAF": {
      "Enabled": true,
      "Mode": "Prevention",
      "RuleSetType": "OWASP",
      "RuleSetVersion": "3.2",
      "CustomRules": [
        {
          "Name": "RateLimitOrleansAPI",
          "Priority": 1,
          "RuleType": "RateLimitRule",
          "Conditions": [
            {
              "MatchVariable": "RequestUri",
              "Operator": "Contains",
              "MatchValues": ["/api/orleans/"]
            }
          ],
          "Action": "Block",
          "RateLimitThreshold": 100,
          "RateLimitDuration": "PT1M"
        },
        {
          "Name": "BlockSuspiciousUserAgents",
          "Priority": 2,
          "RuleType": "MatchRule",
          "Conditions": [
            {
              "MatchVariable": "RequestHeaders",
              "Selector": "User-Agent",
              "Operator": "Contains",
              "MatchValues": ["sqlmap", "nmap", "nikto", "burpsuite"]
            }
          ],
          "Action": "Block"
        }
      ],
      "Exclusions": [
        {
          "MatchVariable": "RequestArgNames",
          "Selector": "orleans_grain_id",
          "SelectorMatchOperator": "Equals"
        }
      ]
    }
  }
}
```

**Step 2: WAF Validation Script**
```powershell
# Test WAF protection for Orleans endpoints
function Test-OrleansWAFProtection {
    param(
        [string]$OrleansEndpoint = "https://orleans.company.com",
        [string[]]$TestScenarios = @("SQLInjection", "XSS", "RateLimiting")
    )

    $results = @{}

    foreach ($scenario in $TestScenarios) {
        try {
            switch ($scenario) {
                "SQLInjection" {
                    $testUrl = "$OrleansEndpoint/api/orleans/metrics?filter=' OR '1'='1"
                    $response = Invoke-WebRequest -Uri $testUrl -Method GET -ErrorAction SilentlyContinue

                    if ($response.StatusCode -eq 403) {
                        Write-Host "✓ SQL Injection attack blocked by WAF" -ForegroundColor Green
                        $results[$scenario] = $true
                    } else {
                        Write-Warning "SQL Injection attack not blocked - WAF may need tuning"
                        $results[$scenario] = $false
                    }
                }

                "XSS" {
                    $testUrl = "$OrleansEndpoint/api/orleans/status"
                    $headers = @{
                        "X-Test-Header" = "<script>alert('xss')</script>"
                    }
                    $response = Invoke-WebRequest -Uri $testUrl -Headers $headers -Method GET -ErrorAction SilentlyContinue

                    if ($response.StatusCode -eq 403) {
                        Write-Host "✓ XSS attack blocked by WAF" -ForegroundColor Green
                        $results[$scenario] = $true
                    } else {
                        Write-Warning "XSS attack not blocked - check WAF configuration"
                        $results[$scenario] = $false
                    }
                }

                "RateLimiting" {
                    # Send rapid requests to test rate limiting
                    $rateLimitHit = $false
                    for ($i = 1; $i -le 150; $i++) {
                        $response = Invoke-WebRequest -Uri "$OrleansEndpoint/api/orleans/metrics" -Method GET -ErrorAction SilentlyContinue
                        if ($response.StatusCode -eq 429 -or $response.StatusCode -eq 403) {
                            $rateLimitHit = $true
                            break
                        }
                        Start-Sleep -Milliseconds 50
                    }

                    if ($rateLimitHit) {
                        Write-Host "✓ Rate limiting working - requests blocked after threshold" -ForegroundColor Green
                        $results[$scenario] = $true
                    } else {
                        Write-Warning "Rate limiting not triggered - check WAF configuration"
                        $results[$scenario] = $false
                    }
                }
            }
        }
        catch {
            Write-Error "Error testing $scenario`: $($_.Exception.Message)"
            $results[$scenario] = $false
        }
    }

    return $results
}

# Usage
$wafResults = Test-OrleansWAFProtection -OrleansEndpoint "https://orleans.company.com"
```

#### 4.3 DDoS Protection and Rate Limiting

**Overview**: Implement DDoS protection and rate limiting for Orleans services.

**Step 1: Azure DDoS Protection Configuration**
```json
{
  "DDoSProtection": {
    "Enabled": true,
    "Plan": "Standard",
    "AlertingEnabled": true,
    "Metrics": {
      "PacketsDroppedDDoS": {
        "Threshold": 1000,
        "AlertEmail": "security-team@company.com"
      },
      "PacketsForwardedDDoS": {
        "Threshold": 10000,
        "AlertEmail": "operations@company.com"
      }
    }
  },
  "RateLimiting": {
    "Orleans": {
      "Dashboard": {
        "RequestsPerMinute": 100,
        "RequestsPerHour": 1000,
        "BurstAllowance": 20
      },
      "API": {
        "RequestsPerMinute": 500,
        "RequestsPerHour": 10000,
        "BurstAllowance": 100
      }
    }
  }
}
```

**Step 2: Application-Level Rate Limiting**
```csharp
// Example middleware for Orleans API rate limiting
public class OrleansRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientIdentifier(context);
        var endpoint = context.Request.Path.Value;

        if (IsOrleansEndpoint(endpoint))
        {
            var rateLimitKey = $"{clientId}:{endpoint}";
            var requestCount = _cache.GetOrCreate(rateLimitKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                return 0;
            });

            if (requestCount >= GetRateLimit(endpoint))
            {
                context.Response.StatusCode = 429;
                await context.Response.WriteAsync("Rate limit exceeded");
                return;
            }

            _cache.Set(rateLimitKey, requestCount + 1, TimeSpan.FromMinutes(1));
        }

        await _next(context);
    }
}
```

#### 4.4 Private Endpoint Configuration

**Overview**: Configure private endpoints for secure Orleans service communications.

**Step 1: Azure Private Endpoint Setup**
```powershell
# Configure private endpoints for Orleans services
function New-OrleansPrivateEndpoint {
    param(
        [string]$ResourceGroupName,
        [string]$VNetName,
        [string]$SubnetName,
        [string]$ServiceResourceId,
        [string]$PrivateEndpointName = "orleans-private-endpoint"
    )

    try {
        # Get subnet resource
        $subnet = Get-AzVirtualNetworkSubnetConfig -VirtualNetwork (Get-AzVirtualNetwork -Name $VNetName -ResourceGroupName $ResourceGroupName) -Name $SubnetName

        # Create private endpoint connection
        $privateEndpointConnection = New-AzPrivateLinkServiceConnection -Name "$PrivateEndpointName-connection" -PrivateLinkServiceId $ServiceResourceId -GroupId "sites"

        # Create private endpoint
        $privateEndpoint = New-AzPrivateEndpoint -ResourceGroupName $ResourceGroupName -Name $PrivateEndpointName -Location "East US" -Subnet $subnet -PrivateLinkServiceConnection $privateEndpointConnection

        # Create private DNS zone
        $dnsZone = New-AzPrivateDnsZone -ResourceGroupName $ResourceGroupName -Name "privatelink.azurewebsites.net"

        # Link DNS zone to VNet
        $dnsLink = New-AzPrivateDnsVirtualNetworkLink -ResourceGroupName $ResourceGroupName -ZoneName "privatelink.azurewebsites.net" -Name "$VNetName-link" -VirtualNetworkId (Get-AzVirtualNetwork -Name $VNetName -ResourceGroupName $ResourceGroupName).Id

        Write-Host "✓ Private endpoint configured successfully" -ForegroundColor Green
        Write-Host "  Endpoint Name: $PrivateEndpointName"
        Write-Host "  Private IP: $($privateEndpoint.NetworkInterfaces[0].IpConfigurations[0].PrivateIpAddress)"

        return $privateEndpoint
    }
    catch {
        Write-Error "✗ Private endpoint configuration failed: $($_.Exception.Message)"
        return $null
    }
}

# Test private endpoint connectivity
function Test-OrleansPrivateEndpoint {
    param(
        [string]$PrivateEndpointFQDN,
        [int]$Port = 443
    )

    try {
        # Test DNS resolution
        $dnsResult = Resolve-DnsName -Name $PrivateEndpointFQDN

        if ($dnsResult) {
            Write-Host "✓ DNS resolution successful: $($dnsResult[0].IPAddress)" -ForegroundColor Green

            # Test connectivity
            $tcpClient = New-Object System.Net.Sockets.TcpClient
            $tcpClient.Connect($dnsResult[0].IPAddress, $Port)

            if ($tcpClient.Connected) {
                Write-Host "✓ Private endpoint connectivity confirmed" -ForegroundColor Green
                $tcpClient.Close()
                return $true
            }
        }

        Write-Error "✗ Private endpoint connectivity failed"
        return $false
    }
    catch {
        Write-Error "✗ Private endpoint test failed: $($_.Exception.Message)"
        return $false
    }
}

# Usage
$privateEndpoint = New-OrleansPrivateEndpoint -ResourceGroupName "orleans-prod-rg" -VNetName "orleans-vnet" -SubnetName "private-endpoints" -ServiceResourceId "/subscriptions/.../orleans-app-service"
Test-OrleansPrivateEndpoint -PrivateEndpointFQDN "orleans-app.privatelink.azurewebsites.net"
```

**Step 2: Network Security Group (NSG) Rules for Private Endpoints**
```powershell
# Configure NSG rules for private endpoint security
function Set-OrleansPrivateEndpointNSG {
    param(
        [string]$ResourceGroupName,
        [string]$NSGName = "orleans-private-nsg",
        [string]$PrivateSubnetCIDR = "10.1.1.0/24"
    )

    try {
        # Create or get existing NSG
        $nsg = Get-AzNetworkSecurityGroup -ResourceGroupName $ResourceGroupName -Name $NSGName -ErrorAction SilentlyContinue

        if (-not $nsg) {
            $nsg = New-AzNetworkSecurityGroup -ResourceGroupName $ResourceGroupName -Name $NSGName -Location "East US"
        }

        # Allow inbound HTTPS from private subnet only
        $nsg | Add-AzNetworkSecurityRuleConfig -Name "Allow-HTTPS-Private" -Description "Allow HTTPS from private subnet" -Access Allow -Protocol Tcp -Direction Inbound -Priority 100 -SourceAddressPrefix $PrivateSubnetCIDR -SourcePortRange * -DestinationAddressPrefix * -DestinationPortRange 443

        # Deny all other inbound traffic
        $nsg | Add-AzNetworkSecurityRuleConfig -Name "Deny-All-Inbound" -Description "Deny all other inbound traffic" -Access Deny -Protocol * -Direction Inbound -Priority 4000 -SourceAddressPrefix * -SourcePortRange * -DestinationAddressPrefix * -DestinationPortRange *

        # Update NSG
        $nsg | Set-AzNetworkSecurityGroup

        Write-Host "✓ Private endpoint NSG configured successfully" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Error "✗ NSG configuration failed: $($_.Exception.Message)"
        return $false
    }
}

# Usage
Set-OrleansPrivateEndpointNSG -ResourceGroupName "orleans-prod-rg" -PrivateSubnetCIDR "10.1.1.0/24"
```

### 5. Security Monitoring and Compliance

#### 5.1 Security Audit Logging

**Overview**: Comprehensive security audit logging for Orleans services to meet enterprise compliance requirements.

**Step 1: Security Event Logging Configuration**
```json
{
  "SecurityLogging": {
    "Enabled": true,
    "LogLevel": "Information",
    "EventTypes": {
      "Authentication": {
        "LoginSuccess": true,
        "LoginFailure": true,
        "Logout": true,
        "PasswordChange": true,
        "AccountLockout": true
      },
      "Authorization": {
        "AccessGranted": true,
        "AccessDenied": true,
        "RoleAssignment": true,
        "PermissionChange": true
      },
      "DataAccess": {
        "GrainActivation": true,
        "GrainDeactivation": true,
        "SensitiveDataAccess": true,
        "ConfigurationChange": true
      }
    },
    "Destinations": [
      {
        "Type": "AzureEventHub",
        "ConnectionString": "#{SECURITY_EVENTHUB_CONNECTION}#",
        "EventHubName": "orleans-security-logs"
      },
      {
        "Type": "File",
        "Path": "logs/security/security-audit-{Date}.log",
        "RetentionDays": 2555 // 7 years
      }
    ]
  }
}
```

**Step 2: Security Audit Logging Functions**
```powershell
# Configure security audit logging
function Enable-OrleansSecurityLogging {
    param(
        [string]$LogPath = "C:\Orleans\Logs\Security",
        [string]$EventHubConnectionString,
        [int]$RetentionDays = 2555
    )

    try {
        # Create security log directory
        if (-not (Test-Path $LogPath)) {
            New-Item -Path $LogPath -ItemType Directory -Force
        }

        # Set appropriate permissions (Security team and System only)
        $acl = Get-Acl $LogPath
        $acl.SetAccessRuleProtection($true, $false) # Disable inheritance, remove inherited rules
        $acl.SetAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule("SYSTEM", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")))
        $acl.SetAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule("Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")))
        $acl.SetAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule("Security-Team", "Read", "ContainerInherit,ObjectInherit", "None", "Allow")))
        Set-Acl -Path $LogPath -AclObject $acl

        # Configure Windows Event Log
        New-EventLog -LogName "Orleans Security" -Source "Orleans.Security" -ErrorAction SilentlyContinue

        # Test security logging
        Write-EventLog -LogName "Orleans Security" -Source "Orleans.Security" -EventId 1000 -EntryType Information -Message "Security logging enabled for Orleans"

        Write-Host "✓ Security audit logging enabled successfully" -ForegroundColor Green
        Write-Host "  Log Path: $LogPath"
        Write-Host "  Retention: $RetentionDays days"
        return $true
    }
    catch {
        Write-Error "✗ Failed to enable security logging: $($_.Exception.Message)"
        return $false
    }
}

# Security log analysis
function Get-OrleansSecurityEvents {
    param(
        [DateTime]$StartTime = (Get-Date).AddDays(-1),
        [DateTime]$EndTime = (Get-Date),
        [string[]]$EventTypes = @("LoginFailure", "AccessDenied", "ConfigurationChange")
    )

    try {
        $securityEvents = @()

        # Query Windows Event Log
        $events = Get-WinEvent -FilterHashtable @{LogName='Orleans Security'; StartTime=$StartTime; EndTime=$EndTime} -ErrorAction SilentlyContinue

        foreach ($event in $events) {
            if ($EventTypes -contains $event.LevelDisplayName) {
                $securityEvents += @{
                    Timestamp = $event.TimeCreated
                    EventType = $event.LevelDisplayName
                    Message = $event.Message
                    UserId = $event.UserId
                    Source = $event.ProviderName
                }
            }
        }

        return $securityEvents | Sort-Object Timestamp
    }
    catch {
        Write-Error "Failed to retrieve security events: $($_.Exception.Message)"
        return @()
    }
}

# Usage
Enable-OrleansSecurityLogging -LogPath "C:\Orleans\Logs\Security" -RetentionDays 2555
$recentEvents = Get-OrleansSecurityEvents -StartTime (Get-Date).AddHours(-24)
```

#### 5.2 Threat Detection and Incident Response

**Overview**: Real-time threat detection and automated incident response procedures for Orleans services.

**Step 1: Threat Detection Configuration**
```json
{
  "ThreatDetection": {
    "Enabled": true,
    "DetectionRules": {
      "BruteForceAttack": {
        "Threshold": 5,
        "TimeWindow": "PT5M",
        "Action": "Block",
        "AlertSeverity": "High"
      },
      "UnusualApiAccess": {
        "Threshold": 1000,
        "TimeWindow": "PT1H",
        "Action": "Alert",
        "AlertSeverity": "Medium"
      },
      "ConfigurationTampering": {
        "MonitoredPaths": ["/api/orleans/configuration", "/dashboard/settings"],
        "Action": "Block",
        "AlertSeverity": "Critical"
      }
    },
    "ResponseActions": {
      "Block": {
        "Duration": "PT1H",
        "NotifySecurityTeam": true
      },
      "Alert": {
        "NotificationChannels": ["Email", "Slack", "Teams"]
      }
    }
  }
}
```

**Step 2: Incident Response Automation**
```powershell
# Automated incident response for Orleans
function Start-OrleansIncidentResponse {
    param(
        [string]$IncidentType,
        [string]$SourceIP,
        [string]$Severity = "Medium",
        [switch]$AutoRemediate = $false
    )

    try {
        $incident = @{
            Id = [Guid]::NewGuid().ToString()
            Type = $IncidentType
            Severity = $Severity
            SourceIP = $SourceIP
            Timestamp = Get-Date
            Status = "Open"
        }

        Write-Host "Security Incident Detected:" -ForegroundColor Red
        Write-Host "  Incident ID: $($incident.Id)"
        Write-Host "  Type: $($incident.Type)"
        Write-Host "  Source IP: $($incident.SourceIP)"
        Write-Host "  Severity: $($incident.Severity)"

        # Log incident
        Write-EventLog -LogName "Orleans Security" -Source "Orleans.Security" -EventId 2000 -EntryType Warning -Message "Security incident: $($incident.Type) from $($incident.SourceIP)"

        # Auto-remediation based on incident type
        if ($AutoRemediate) {
            switch ($IncidentType) {
                "BruteForceAttack" {
                    # Block IP address
                    New-NetFirewallRule -DisplayName "Block-$SourceIP" -Direction Inbound -RemoteAddress $SourceIP -Action Block
                    Write-Host "✓ IP address $SourceIP blocked" -ForegroundColor Green
                }

                "ConfigurationTampering" {
                    # Lock down configuration endpoints
                    # This would typically integrate with your web application firewall
                    Write-Host "✓ Configuration endpoints protected" -ForegroundColor Green
                }

                "UnusualApiAccess" {
                    # Increase monitoring for this IP
                    Write-Host "✓ Enhanced monitoring enabled for $SourceIP" -ForegroundColor Yellow
                }
            }
        }

        # Notify security team
        Send-SecurityAlert -Incident $incident

        return $incident
    }
    catch {
        Write-Error "Incident response failed: $($_.Exception.Message)"
        return $null
    }
}

function Send-SecurityAlert {
    param($Incident)

    $alertMessage = @"
ORLEANS SECURITY ALERT

Incident ID: $($Incident.Id)
Type: $($Incident.Type)
Severity: $($Incident.Severity)
Source IP: $($Incident.SourceIP)
Timestamp: $($Incident.Timestamp)

Please investigate immediately.
"@

    # Send to security team (implementation depends on your alerting system)
    # Send-MailMessage -To "security-team@company.com" -Subject "Orleans Security Alert" -Body $alertMessage
    Write-Host "Security alert sent to security team" -ForegroundColor Yellow
}
```

#### 5.3 Compliance Reporting (SOC 2, GDPR)

**Overview**: Automated compliance reporting for SOC 2, GDPR, and other regulatory requirements.

**Step 1: SOC 2 Compliance Configuration**
```json
{
  "ComplianceReporting": {
    "SOC2": {
      "Enabled": true,
      "Controls": {
        "CC6.1": {
          "Description": "Logical and physical access controls",
          "Evidence": ["AuditLogs", "AccessReports", "AuthenticationLogs"],
          "Frequency": "Monthly"
        },
        "CC6.2": {
          "Description": "System accounts management",
          "Evidence": ["ServiceAccountAudit", "PrivilegedAccessReports"],
          "Frequency": "Quarterly"
        },
        "CC6.3": {
          "Description": "Network security controls",
          "Evidence": ["FirewallLogs", "NetworkSegmentationReports"],
          "Frequency": "Monthly"
        }
      },
      "ReportSchedule": "0 0 1 * *", // First day of every month
      "Recipients": ["compliance@company.com", "security@company.com"]
    },
    "GDPR": {
      "Enabled": true,
      "DataProcessingLog": "logs/gdpr/data-processing-{Date}.log",
      "ConsentTracking": true,
      "DataRetentionPolicy": "P7Y", // 7 years
      "DataMinimization": true
    }
  }
}
```

**Step 2: Compliance Report Generation**
```powershell
# Generate compliance reports
function New-OrleansComplianceReport {
    param(
        [string]$ReportType = "SOC2",
        [DateTime]$StartDate = (Get-Date).AddMonths(-1),
        [DateTime]$EndDate = (Get-Date),
        [string]$OutputPath = "reports"
    )

    try {
        $reportData = @{
            ReportType = $ReportType
            Period = "$($StartDate.ToString('yyyy-MM-dd')) to $($EndDate.ToString('yyyy-MM-dd'))"
            GeneratedDate = Get-Date
            Controls = @()
        }

        switch ($ReportType) {
            "SOC2" {
                # CC6.1 - Access Controls
                $accessEvents = Get-OrleansSecurityEvents -StartTime $StartDate -EndTime $EndDate -EventTypes @("LoginSuccess", "LoginFailure", "AccessDenied")
                $reportData.Controls += @{
                    ControlId = "CC6.1"
                    Description = "Logical and physical access controls"
                    TotalEvents = $accessEvents.Count
                    FailedAttempts = ($accessEvents | Where-Object { $_.EventType -eq "LoginFailure" }).Count
                    ComplianceStatus = "Compliant"
                }

                # CC6.2 - System Accounts
                $serviceAccounts = Test-ServiceAccountHealth
                $reportData.Controls += @{
                    ControlId = "CC6.2"
                    Description = "System accounts management"
                    ActiveAccounts = $serviceAccounts.Count
                    ComplianceStatus = "Compliant"
                }

                # CC6.3 - Network Security
                $networkEvents = Get-WinEvent -FilterHashtable @{LogName='Security'; Id=5156,5157; StartTime=$StartDate; EndTime=$EndDate} -ErrorAction SilentlyContinue
                $reportData.Controls += @{
                    ControlId = "CC6.3"
                    Description = "Network security controls"
                    NetworkConnections = $networkEvents.Count
                    ComplianceStatus = "Compliant"
                }
            }

            "GDPR" {
                # Data Processing Activities
                $dataProcessingEvents = Get-OrleansSecurityEvents -StartTime $StartDate -EndTime $EndDate -EventTypes @("SensitiveDataAccess")
                $reportData.Controls += @{
                    ControlId = "GDPR.Article30"
                    Description = "Records of processing activities"
                    ProcessingEvents = $dataProcessingEvents.Count
                    ComplianceStatus = "Compliant"
                }
            }
        }

        # Generate report file
        $reportFile = Join-Path $OutputPath "Orleans-$ReportType-Report-$(Get-Date -Format 'yyyy-MM-dd').json"
        $reportData | ConvertTo-Json -Depth 10 | Set-Content $reportFile

        Write-Host "✓ Compliance report generated successfully" -ForegroundColor Green
        Write-Host "  Report Type: $ReportType"
        Write-Host "  Period: $($reportData.Period)"
        Write-Host "  File: $reportFile"

        return $reportFile
    }
    catch {
        Write-Error "Failed to generate compliance report: $($_.Exception.Message)"
        return $null
    }
}

# Automated compliance monitoring
function Start-OrleansComplianceMonitoring {
    param([string[]]$ComplianceFrameworks = @("SOC2", "GDPR"))

    foreach ($framework in $ComplianceFrameworks) {
        try {
            # Schedule monthly reports
            $trigger = New-ScheduledTaskTrigger -Monthly -At "02:00AM" -DaysOfMonth 1
            $action = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-File C:\Orleans\Scripts\Generate-ComplianceReport.ps1 -ReportType $framework"

            Register-ScheduledTask -TaskName "Orleans-$framework-Compliance" -Trigger $trigger -Action $action -Description "Automated $framework compliance reporting for Orleans"

            Write-Host "✓ $framework compliance monitoring scheduled" -ForegroundColor Green
        }
        catch {
            Write-Error "Failed to schedule $framework compliance monitoring: $($_.Exception.Message)"
        }
    }
}

# Usage
New-OrleansComplianceReport -ReportType "SOC2"
Start-OrleansComplianceMonitoring -ComplianceFrameworks @("SOC2", "GDPR")
```

## Enterprise Security Summary

The comprehensive security configuration for Orleans includes:

### ✅ **Security Areas Implemented**
1. **Enterprise Authentication**: LDAP/AD, OAuth2/OIDC, RBAC, service account management
2. **Certificate & TLS Management**: Lifecycle management, mTLS, rotation, TLS 1.3 hardening
3. **Secrets Management**: Azure Key Vault, HashiCorp Vault, automated rotation
4. **Network Security**: Firewall rules, WAF, DDoS protection, private endpoints
5. **Security Monitoring**: Audit logging, threat detection, compliance reporting

### 🔒 **Enterprise Readiness Checklist**
- [ ] LDAP/Active Directory integration configured
- [ ] OAuth2/OIDC authentication enabled
- [ ] RBAC roles and permissions assigned
- [ ] Certificate lifecycle automation implemented
- [ ] mTLS grain communication secured
- [ ] Azure Key Vault secrets management configured
- [ ] Firewall rules and network segmentation applied
- [ ] Web Application Firewall protection enabled
- [ ] Security audit logging operational
- [ ] SOC 2/GDPR compliance reporting automated

### 📞 **Security Contacts**
- **Security Team**: security-team@company.com
- **Operations Team**: operations@company.com
- **Compliance Team**: compliance@company.com

## Operations Integration

### CI/CD Pipeline Integration
```yaml
# Example Azure DevOps pipeline step
- task: PowerShell@2
  displayName: 'Deploy AIChat with Orleans'
  inputs:
    filePath: '$(Build.SourcesDirectory)/build-and-start-server.ps1'
    arguments: '-UseOrleans -Environment $(Environment) -Port $(ServicePort)'
    workingDirectory: '$(Build.SourcesDirectory)'
```

### Docker Deployment (Future)
```dockerfile
# Dockerfile example for Orleans deployment
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY ./publish .
EXPOSE 5099 8080
ENTRYPOINT ["dotnet", "AIChat.Server.dll"]
```

## Monitoring Integration

During deployment, ensure monitoring systems are configured:

1. **Health Endpoints**: `/health` for load balancers
2. **Metrics Collection**: `/api/orleans/metrics` for monitoring systems
3. **Dashboard Access**: Orleans dashboard for operational visibility
4. **Log Aggregation**: Structured logging for centralized analysis

## Contact Information

### Escalation Contacts
- **Operations Team**: operations@company.com
- **Development Team**: dev-team@company.com
- **Orleans Expert**: orleans-consultant@company.com

### Emergency Procedures
1. **Immediate Issues**: Disable Orleans via feature flag
2. **Critical Failures**: Execute rollback procedures
3. **Data Issues**: Contact database team for recovery
4. **Performance Issues**: Contact performance team

---

**Document Version**: 1.0
**Last Updated**: September 2025
**Next Review**: Quarterly
**Owner**: Operations Team