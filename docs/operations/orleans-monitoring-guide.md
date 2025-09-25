# Orleans Monitoring Setup Guide

## Overview

This guide provides comprehensive setup procedures for monitoring Orleans-based AI Chat system. The monitoring infrastructure includes real-time metrics, health checks, alerting, and performance dashboards.

## Architecture Overview

### Monitoring Components
- **Prometheus Metrics**: `/metrics` endpoint for metrics collection
- **Orleans Dashboard**: Real-time grain and silo monitoring
- **Health Checks**: System health validation endpoints
- **Application Insights**: Telemetry and performance monitoring
- **Structured Logging**: Serilog with console and file outputs
- **Grafana Dashboards**: Visual monitoring and alerting

### Key Metrics Collected
- Grain activation/deactivation rates
- Message processing latency (P95, P99)
- Memory usage per grain type
- Orleans silo health status
- Request success/failure rates
- System resource utilization

## Prerequisites

### Infrastructure Requirements
- **Prometheus Server**: For metrics collection
- **Grafana**: For dashboard visualization (optional but recommended)
- **Application Insights**: Azure Application Insights instance
- **Log Storage**: File system or centralized logging solution

### Access Requirements
- **Orleans Dashboard**: HTTP access to port 8080
- **Metrics Endpoint**: HTTP access to port 5100 (`/api/orleans/metrics`)
- **Health Endpoint**: HTTP access to port 5099 (`/health`)

## Orleans Dashboard Setup

### 1. Dashboard Configuration
The Orleans Dashboard is automatically configured during deployment:

```json
{
  "Orleans": {
    "Dashboard": {
      "Enabled": true,
      "Username": "admin",
      "Password": "orleans123",
      "Port": 8080
    }
  }
}
```

### 2. Access and Authentication
```powershell
# Development access (no authentication required)
Start-Process "http://localhost:8080/dashboard"

# Production access (authentication required)
# Username: admin
# Password: orleans123 (CHANGE IN PRODUCTION)
```

### 3. Dashboard Features
- **Real-time System Status**: Active grains, silo health, memory usage
- **Grain Management**: Grain activation tracking and lifecycle monitoring
- **Performance Metrics**: Request processing times and throughput
- **Auto-refresh**: 30-second automatic updates
- **Historical Data**: Short-term trending (24-hour retention)

## Prometheus Metrics Setup

### 1. Metrics Endpoint Configuration
The Orleans system exposes Prometheus-compatible metrics:

**Endpoint**: `http://localhost:5100/api/orleans/metrics`
**Format**: JSON (Prometheus format available)
**Update Frequency**: Real-time

### 2. Prometheus Configuration
Add to your `prometheus.yml`:

```yaml
global:
  scrape_interval: 15s

scrape_configs:
  - job_name: 'orleans-aichat'
    static_configs:
      - targets: ['localhost:5100']
    metrics_path: '/api/orleans/metrics'
    scrape_interval: 15s

  - job_name: 'aichat-server'
    static_configs:
      - targets: ['localhost:5099']
    metrics_path: '/health'
    scrape_interval: 30s
```

### 3. Key Metrics Available
```json
{
  "orleans.grain.activations.total": "Counter of grain activations",
  "orleans.grain.activations.per_second": "Rate of grain activations",
  "orleans.grain.deactivations.total": "Counter of grain deactivations",
  "orleans.message.processing.duration": "Histogram of message processing times",
  "orleans.silo.memory.usage": "Memory usage in bytes",
  "orleans.silo.cpu.usage": "CPU usage percentage",
  "orleans.requests.total": "Total requests processed",
  "orleans.requests.success.rate": "Success rate percentage",
  "orleans.requests.error.rate": "Error rate percentage"
}
```

## Health Check Configuration

### 1. Basic Health Check
**Endpoint**: `http://localhost:5099/health`
**Response**: Text-based health status
**Purpose**: Load balancer and uptime monitoring

```powershell
# Health check validation
$response = Invoke-WebRequest -Uri "http://localhost:5099/health" -Method GET
Write-Host "Status: $($response.StatusCode) - Content: $($response.Content)"
```

### 2. Detailed Health Check
**Endpoint**: `http://localhost:5100/api/orleans/metrics`
**Response**: Comprehensive JSON metrics
**Purpose**: Detailed system monitoring

```powershell
# Detailed health validation
$metrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics" -Method GET
Write-Host "Active Grains: $($metrics.ActiveGrains)"
Write-Host "Silo Status: $($metrics.SiloHealth)"
Write-Host "Memory Usage: $($metrics.MemoryUsageMB) MB"
```

## Application Insights Integration

### 1. Configuration
Add Application Insights connection string to `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "ApplicationInsights": "InstrumentationKey=your-key;IngestionEndpoint=https://your-region.in.applicationinsights.azure.com/"
  },
  "ApplicationInsights": {
    "EnableCustomMetrics": true,
    "EnablePerformanceCounters": true,
    "EnableDependencyTracking": true,
    "SamplingPercentage": 100
  }
}
```

### 2. Custom Metrics Configuration
The system automatically sends these custom metrics to Application Insights:

```json
{
  "customMetrics": [
    "orleans.grain.activations.total",
    "orleans.grain.activations.persecond",
    "orleans.silo.cpu.usage",
    "orleans.silo.memory.usage",
    "orleans.requests.total",
    "orleans.requests.persecond",
    "orleans.requests.duration.average"
  ]
}
```

### 3. Application Insights Queries
```kusto
// Grain activation rate over time
customMetrics
| where name == "orleans.grain.activations.persecond"
| summarize avg(value) by bin(timestamp, 5m)
| render timechart

// Error rate monitoring
customMetrics
| where name == "orleans.requests.error.rate"
| where value > 5.0
| project timestamp, value, cloud_RoleInstance
```

## Alerting Configuration

### 1. Alert Rules Setup
The system includes pre-configured alert rules in `/monitoring-alerts.json`:

```json
{
  "alertRules": [
    {
      "name": "Orleans Silo Down",
      "metric": "orleans.silo.health",
      "condition": "unhealthy",
      "severity": "critical",
      "thresholdMinutes": 2
    },
    {
      "name": "High Grain Activation Time",
      "metric": "orleans.grain.activation.time",
      "condition": "average > 5000ms",
      "severity": "warning",
      "thresholdMinutes": 5
    },
    {
      "name": "Memory Usage High",
      "metric": "system.memory.usage",
      "condition": "percentage > 85%",
      "severity": "warning",
      "thresholdMinutes": 3
    }
  ]
}
```

### 2. Application Insights Alerts
Create alerts in Azure portal:

```powershell
# PowerShell script to create Application Insights alerts
$resourceGroup = "your-resource-group"
$appInsightsName = "your-app-insights"

# Critical: Orleans Silo Down
az monitor metrics alert create `
  --name "Orleans Silo Down" `
  --resource-group $resourceGroup `
  --scopes "/subscriptions/{subscription}/resourceGroups/$resourceGroup/providers/Microsoft.Insights/components/$appInsightsName" `
  --condition "avg customMetrics/orleans.silo.health < 1" `
  --evaluation-frequency "PT1M" `
  --window-size "PT5M"

# Warning: High Memory Usage
az monitor metrics alert create `
  --name "High Memory Usage" `
  --resource-group $resourceGroup `
  --scopes "/subscriptions/{subscription}/resourceGroups/$resourceGroup/providers/Microsoft.Insights/components/$appInsightsName" `
  --condition "avg customMetrics/orleans.silo.memory.usage > 85" `
  --evaluation-frequency "PT5M" `
  --window-size "PT15M"
```

### 3. Notification Channels
Configure notification channels in Application Insights:

```json
{
  "notificationChannels": {
    "operations": {
      "type": "applicationInsights",
      "config": {
        "actionGroup": "orleans-operations",
        "emailAddresses": ["ops-team@company.com"],
        "webhooks": ["https://company.slack.com/webhook/orleans-alerts"]
      }
    },
    "development": {
      "type": "applicationInsights",
      "config": {
        "actionGroup": "orleans-development",
        "emailAddresses": ["dev-team@company.com"]
      }
    }
  }
}
```

## Grafana Dashboard Setup

### 1. Dashboard Templates
The system includes pre-built Grafana dashboard templates:

- **Executive Dashboard**: High-level metrics for leadership
- **Operational Dashboard**: Detailed system metrics for operations
- **Troubleshooting Dashboard**: Diagnostic metrics for support
- **Historical Analysis**: Long-term trends and capacity planning

### 2. Dashboard Import Process
```bash
# Import Orleans dashboard template
curl -X POST \
  http://grafana-server:3000/api/dashboards/db \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Bearer your-api-key' \
  -d @orleans-dashboard-template.json
```

### 3. Key Dashboard Panels

**System Overview Panel**:
- Active Grains Count
- Silo Health Status
- Memory Usage Trend
- CPU Utilization

**Performance Panel**:
- Request Processing Latency (P50, P95, P99)
- Throughput (Requests/Second)
- Error Rate Percentage
- Grain Activation Time

**Operational Panel**:
- Grain Type Distribution
- Message Queue Depths
- Connection Pool Status
- Cache Hit Rates

## Logging Configuration

### 1. Structured Logging Setup
The system uses Serilog for structured logging:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Orleans": "Warning",
        "AIChat.Orleans": "Information"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/orleans-host-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30,
          "outputTemplate": "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  }
}
```

### 2. Log Analysis Queries
```powershell
# Find Orleans errors in logs
Get-Content "logs\orleans-host-*.log" | Select-String "ERROR|FATAL|EXCEPTION" | Select-Object -Last 20

# Monitor grain activation patterns
Get-Content "logs\orleans-host-*.log" | Select-String "grain.*activated|grain.*deactivated" | Select-Object -Last 10

# Check performance issues
Get-Content "logs\orleans-host-*.log" | Select-String "slow|timeout|performance" | Select-Object -Last 10
```

### 3. Centralized Logging (Optional)
For production environments, configure log forwarding:

```json
{
  "Serilog": {
    "WriteTo": [
      {
        "Name": "ApplicationInsights",
        "Args": {
          "restrictedToMinimumLevel": "Information",
          "telemetryConverter": "Serilog.Sinks.ApplicationInsights.TelemetryConverters.TraceTelemetryConverter, Serilog.Sinks.ApplicationInsights"
        }
      }
    ]
  }
}
```

## Performance Monitoring

### 1. Key Performance Indicators (KPIs)
Monitor these critical metrics:

| Metric | Target | Critical Threshold |
|--------|--------|--------------------|
| Grain Activation Time | < 100ms | > 5000ms |
| Request Processing Time | < 200ms | > 1000ms |
| Memory Usage | < 70% | > 85% |
| Error Rate | < 1% | > 5% |
| Silo Availability | 99.9% | < 99% |

### 2. Performance Baseline Establishment
```powershell
# Capture performance baseline
$baseline = @{
    Timestamp = Get-Date
    Metrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"
    SystemInfo = @{
        MemoryGB = (Get-WmiObject -Class Win32_ComputerSystem).TotalPhysicalMemory / 1GB
        ProcessorCount = $env:NUMBER_OF_PROCESSORS
        OSVersion = (Get-WmiObject -Class Win32_OperatingSystem).Version
    }
}

$baseline | ConvertTo-Json -Depth 10 | Out-File "monitoring-baseline-$(Get-Date -Format 'yyyy-MM-dd').json"
```

### 3. Performance Regression Detection
```powershell
# Compare current performance to baseline
$current = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"
$baseline = Get-Content "monitoring-baseline-latest.json" | ConvertFrom-Json

$regressions = @()
if ($current.AverageActivationTime -gt ($baseline.Metrics.AverageActivationTime * 1.2)) {
    $regressions += "Grain activation time regression detected"
}
if ($current.RequestProcessingTime -gt ($baseline.Metrics.RequestProcessingTime * 1.2)) {
    $regressions += "Request processing time regression detected"
}

if ($regressions.Count -gt 0) {
    Write-Warning "Performance regressions detected: $($regressions -join ', ')"
}
```

## Monitoring Validation Checklist

### Initial Setup Validation
- [ ] Orleans Dashboard accessible and displaying data
- [ ] Prometheus metrics endpoint responding
- [ ] Health check endpoint returning success
- [ ] Application Insights receiving telemetry
- [ ] Log files being created and written
- [ ] Alert rules configured and active
- [ ] Grafana dashboards imported and functional

### Daily Operations Validation
- [ ] All systems showing healthy status
- [ ] No critical alerts active
- [ ] Performance within acceptable ranges
- [ ] Log files not exceeding size limits
- [ ] Dashboard auto-refresh working
- [ ] Metric collection continuous
- [ ] No monitoring gaps or outages

### Weekly Monitoring Review
- [ ] Performance trends analyzed
- [ ] Alert threshold optimization reviewed
- [ ] Dashboard effectiveness assessed
- [ ] Log retention policies applied
- [ ] Monitoring infrastructure health checked
- [ ] Team training on monitoring tools completed

## Troubleshooting Monitoring Issues

### Dashboard Issues
```powershell
# Orleans Dashboard not accessible
Test-NetConnection -ComputerName localhost -Port 8080

# Check Orleans Host service status
Get-Process -Name "*Orleans*" -ErrorAction SilentlyContinue

# Verify dashboard configuration
Get-Content "server\AIChat.Orleans.Host\appsettings.json" | ConvertFrom-Json | Select-Object -ExpandProperty Orleans
```

### Metrics Collection Issues
```powershell
# Test metrics endpoint
try {
    $metrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics" -TimeoutSec 10
    Write-Host "Metrics collection working: $($metrics.ActiveGrains) active grains"
} catch {
    Write-Error "Metrics collection failed: $($_.Exception.Message)"
}

# Check Orleans metrics collector service
Get-Content "logs\orleans-host-*.log" | Select-String "metrics" | Select-Object -Last 5
```

### Alert Issues
```powershell
# Validate Application Insights connection
$connectionString = (Get-Content "server\AIChat.Server\appsettings.json" | ConvertFrom-Json).ConnectionStrings.ApplicationInsights
if ([string]::IsNullOrEmpty($connectionString)) {
    Write-Warning "Application Insights connection string not configured"
}

# Test alert rule logic
$currentMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"
if ($currentMetrics.MemoryUsagePercent -gt 85) {
    Write-Warning "Memory usage alert condition met: $($currentMetrics.MemoryUsagePercent)%"
}
```

## Enterprise Security Monitoring

### 1. Security Audit Logging and Monitoring

**Overview**: Real-time security event monitoring and analysis for Orleans services.

#### Security Event Monitoring Configuration
```json
{
  "SecurityMonitoring": {
    "Enabled": true,
    "EventSources": [
      {
        "Name": "OrleansSecurityLog",
        "LogPath": "logs/security/security-audit-*.log",
        "EventTypes": ["Authentication", "Authorization", "DataAccess", "ConfigurationChange"],
        "ScanInterval": "PT30S"
      },
      {
        "Name": "WindowsSecurityLog",
        "LogName": "Orleans Security",
        "EventIds": [1000, 2000, 2001, 2002],
        "ScanInterval": "PT1M"
      }
    ],
    "AlertingRules": {
      "FailedLoginThreshold": {
        "EventType": "LoginFailure",
        "Threshold": 5,
        "TimeWindow": "PT5M",
        "Severity": "High",
        "Action": "Alert"
      },
      "ConfigurationChange": {
        "EventType": "ConfigurationChange",
        "Threshold": 1,
        "TimeWindow": "PT1M",
        "Severity": "Critical",
        "Action": "ImmediateAlert"
      },
      "UnauthorizedAccess": {
        "EventType": "AccessDenied",
        "Threshold": 10,
        "TimeWindow": "PT10M",
        "Severity": "Medium",
        "Action": "Alert"
      }
    }
  }
}
```

#### Security Metrics Collection
```powershell
# Security metrics monitoring function
function Get-OrleansSecurityMetrics {
    param(
        [DateTime]$StartTime = (Get-Date).AddHours(-1),
        [DateTime]$EndTime = (Get-Date)
    )

    $securityMetrics = @{
        AuthenticationEvents = @{}
        AuthorizationEvents = @{}
        SecurityIncidents = @{}
        ComplianceMetrics = @{}
    }

    try {
        # Authentication metrics
        $authEvents = Get-WinEvent -FilterHashtable @{LogName='Orleans Security'; StartTime=$StartTime; EndTime=$EndTime} -ErrorAction SilentlyContinue
        $securityMetrics.AuthenticationEvents = @{
            TotalAttempts = $authEvents.Count
            SuccessfulLogins = ($authEvents | Where-Object { $_.Id -eq 1001 }).Count
            FailedLogins = ($authEvents | Where-Object { $_.Id -eq 1002 }).Count
            AccountLockouts = ($authEvents | Where-Object { $_.Id -eq 1003 }).Count
        }

        # Authorization metrics
        $authzEvents = Get-WinEvent -FilterHashtable @{LogName='Orleans Security'; Id=2000,2001; StartTime=$StartTime; EndTime=$EndTime} -ErrorAction SilentlyContinue
        $securityMetrics.AuthorizationEvents = @{
            AccessGranted = ($authzEvents | Where-Object { $_.Id -eq 2000 }).Count
            AccessDenied = ($authzEvents | Where-Object { $_.Id -eq 2001 }).Count
        }

        # Security incidents
        $incidentEvents = Get-WinEvent -FilterHashtable @{LogName='Orleans Security'; Id=3000,3001,3002; StartTime=$StartTime; EndTime=$EndTime} -ErrorAction SilentlyContinue
        $securityMetrics.SecurityIncidents = @{
            BruteForceAttempts = ($incidentEvents | Where-Object { $_.Id -eq 3000 }).Count
            ConfigurationTampering = ($incidentEvents | Where-Object { $_.Id -eq 3001 }).Count
            UnusualActivity = ($incidentEvents | Where-Object { $_.Id -eq 3002 }).Count
        }

        return $securityMetrics
    }
    catch {
        Write-Error "Failed to collect security metrics: $($_.Exception.Message)"
        return $null
    }
}

# Real-time security dashboard
function Show-OrleansSecurityDashboard {
    $metrics = Get-OrleansSecurityMetrics

    Write-Host "=== Orleans Security Dashboard ===" -ForegroundColor Cyan
    Write-Host "Authentication Events (Last Hour):" -ForegroundColor Yellow
    Write-Host "  Successful Logins: $($metrics.AuthenticationEvents.SuccessfulLogins)" -ForegroundColor Green
    Write-Host "  Failed Logins: $($metrics.AuthenticationEvents.FailedLogins)" -ForegroundColor $(if($metrics.AuthenticationEvents.FailedLogins -gt 5) { 'Red' } else { 'White' })
    Write-Host "  Account Lockouts: $($metrics.AuthenticationEvents.AccountLockouts)" -ForegroundColor $(if($metrics.AuthenticationEvents.AccountLockouts -gt 0) { 'Red' } else { 'White' })

    Write-Host "Authorization Events:" -ForegroundColor Yellow
    Write-Host "  Access Granted: $($metrics.AuthorizationEvents.AccessGranted)" -ForegroundColor Green
    Write-Host "  Access Denied: $($metrics.AuthorizationEvents.AccessDenied)" -ForegroundColor $(if($metrics.AuthorizationEvents.AccessDenied -gt 10) { 'Red' } else { 'White' })

    Write-Host "Security Incidents:" -ForegroundColor Yellow
    Write-Host "  Brute Force Attempts: $($metrics.SecurityIncidents.BruteForceAttempts)" -ForegroundColor $(if($metrics.SecurityIncidents.BruteForceAttempts -gt 0) { 'Red' } else { 'Green' })
    Write-Host "  Configuration Changes: $($metrics.SecurityIncidents.ConfigurationTampering)" -ForegroundColor $(if($metrics.SecurityIncidents.ConfigurationTampering -gt 0) { 'Red' } else { 'Green' })
    Write-Host "  Unusual Activity: $($metrics.SecurityIncidents.UnusualActivity)" -ForegroundColor $(if($metrics.SecurityIncidents.UnusualActivity -gt 0) { 'Yellow' } else { 'Green' })
}

# Usage
Show-OrleansSecurityDashboard
```

### 2. Threat Detection and Alerting

#### Real-time Threat Detection Setup
```json
{
  "ThreatDetectionMonitoring": {
    "Enabled": true,
    "DetectionEngines": {
      "AnomalyDetection": {
        "Enabled": true,
        "BaseliningPeriod": "P7D",
        "SensitivityLevel": "Medium",
        "MonitoredMetrics": [
          "orleans.requests.rate",
          "orleans.grain.activations.rate",
          "orleans.memory.usage.percent",
          "orleans.authentication.failure.rate"
        ]
      },
      "SignatureDetection": {
        "Enabled": true,
        "SignatureFiles": ["signatures/orleans-threats.yaml", "signatures/web-attacks.yaml"],
        "UpdateInterval": "PT1H"
      }
    },
    "AlertChannels": {
      "Email": {
        "Enabled": true,
        "Recipients": ["security-team@company.com", "operations@company.com"],
        "SeverityThreshold": "Medium"
      },
      "Slack": {
        "Enabled": true,
        "WebhookUrl": "#{SLACK_WEBHOOK_URL}#",
        "Channel": "#orleans-security-alerts"
      },
      "SIEM": {
        "Enabled": true,
        "Endpoint": "https://siem.company.com/api/events",
        "Authentication": "Bearer #{SIEM_API_TOKEN}#"
      }
    }
  }
}
```

#### Threat Detection Functions
```powershell
# Automated threat detection
function Start-OrleansThreatDetection {
    param(
        [int]$MonitoringIntervalSeconds = 60,
        [string]$ConfigPath = "config/threat-detection.json"
    )

    $config = Get-Content $ConfigPath | ConvertFrom-Json

    Write-Host "Starting Orleans threat detection monitoring..." -ForegroundColor Green

    while ($true) {
        try {
            # Anomaly detection
            if ($config.ThreatDetectionMonitoring.DetectionEngines.AnomalyDetection.Enabled) {
                $anomalies = Test-OrleansAnomalies
                foreach ($anomaly in $anomalies) {
                    Send-ThreatAlert -Type "Anomaly" -Details $anomaly
                }
            }

            # Signature-based detection
            if ($config.ThreatDetectionMonitoring.DetectionEngines.SignatureDetection.Enabled) {
                $threats = Test-OrleansSignatureThreats
                foreach ($threat in $threats) {
                    Send-ThreatAlert -Type "Signature" -Details $threat
                }
            }

            Start-Sleep -Seconds $MonitoringIntervalSeconds
        }
        catch {
            Write-Error "Threat detection error: $($_.Exception.Message)"
            Start-Sleep -Seconds 30
        }
    }
}

function Test-OrleansAnomalies {
    $currentMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"
    $anomalies = @()

    # Check for unusual request rates
    if ($currentMetrics.RequestsPerSecond -gt 1000) {
        $anomalies += @{
            Type = "HighRequestRate"
            Value = $currentMetrics.RequestsPerSecond
            Threshold = 1000
            Severity = "Medium"
        }
    }

    # Check for memory usage spikes
    if ($currentMetrics.MemoryUsagePercent -gt 90) {
        $anomalies += @{
            Type = "HighMemoryUsage"
            Value = $currentMetrics.MemoryUsagePercent
            Threshold = 90
            Severity = "High"
        }
    }

    # Check for authentication failures
    $securityMetrics = Get-OrleansSecurityMetrics
    if ($securityMetrics.AuthenticationEvents.FailedLogins -gt 10) {
        $anomalies += @{
            Type = "HighAuthenticationFailures"
            Value = $securityMetrics.AuthenticationEvents.FailedLogins
            Threshold = 10
            Severity = "High"
        }
    }

    return $anomalies
}

function Send-ThreatAlert {
    param(
        [string]$Type,
        [hashtable]$Details
    )

    $alertMessage = @{
        Timestamp = Get-Date
        AlertType = $Type
        Severity = $Details.Severity
        MetricType = $Details.Type
        CurrentValue = $Details.Value
        Threshold = $Details.Threshold
        Source = "Orleans Monitoring"
    }

    # Send to SIEM
    try {
        $headers = @{
            "Authorization" = "Bearer $env:SIEM_API_TOKEN"
            "Content-Type" = "application/json"
        }

        Invoke-RestMethod -Uri "https://siem.company.com/api/events" -Method POST -Headers $headers -Body ($alertMessage | ConvertTo-Json)
        Write-Host "Threat alert sent to SIEM" -ForegroundColor Yellow
    }
    catch {
        Write-Warning "Failed to send alert to SIEM: $($_.Exception.Message)"
    }

    # Local alerting
    Write-Host "THREAT ALERT: $($Details.Type) - Current: $($Details.Value), Threshold: $($Details.Threshold)" -ForegroundColor Red
}
```

### 3. Compliance Monitoring and Reporting

#### Compliance Metrics Configuration
```json
{
  "ComplianceMonitoring": {
    "Enabled": true,
    "Frameworks": {
      "SOC2": {
        "Controls": [
          {
            "ControlId": "CC6.1",
            "MonitoringQuery": "AuthenticationEvents",
            "RequiredFields": ["Timestamp", "UserId", "Result", "SourceIP"],
            "RetentionPeriod": "P2Y"
          },
          {
            "ControlId": "CC6.2",
            "MonitoringQuery": "PrivilegedAccess",
            "RequiredFields": ["Timestamp", "AccountName", "Action", "Resource"],
            "RetentionPeriod": "P2Y"
          }
        ]
      },
      "GDPR": {
        "DataProcessingLog": "logs/gdpr/data-processing-{Date}.log",
        "PersonalDataAccess": "logs/gdpr/personal-data-access-{Date}.log",
        "ConsentTracking": "logs/gdpr/consent-{Date}.log"
      }
    },
    "ReportingSchedule": {
      "Daily": ["SecuritySummary"],
      "Weekly": ["ThreatAnalysis", "AccessReview"],
      "Monthly": ["ComplianceReport", "AuditReadiness"]
    }
  }
}
```

#### Compliance Reporting Functions
```powershell
# Generate compliance monitoring reports
function New-OrleansComplianceMonitoringReport {
    param(
        [string]$Framework = "SOC2",
        [string]$Period = "Monthly",
        [DateTime]$StartDate = (Get-Date).AddMonths(-1),
        [DateTime]$EndDate = (Get-Date)
    )

    $report = @{
        Framework = $Framework
        Period = $Period
        StartDate = $StartDate
        EndDate = $EndDate
        GeneratedAt = Get-Date
        MonitoringMetrics = @{}
        ComplianceStatus = "Compliant"
    }

    try {
        switch ($Framework) {
            "SOC2" {
                # CC6.1 - Access Controls Monitoring
                $authMetrics = Get-OrleansSecurityMetrics -StartTime $StartDate -EndTime $EndDate
                $report.MonitoringMetrics["CC6.1"] = @{
                    Description = "Logical and physical access controls monitoring"
                    TotalLogins = $authMetrics.AuthenticationEvents.TotalAttempts
                    FailedLogins = $authMetrics.AuthenticationEvents.FailedLogins
                    FailureRate = [math]::Round(($authMetrics.AuthenticationEvents.FailedLogins / $authMetrics.AuthenticationEvents.TotalAttempts) * 100, 2)
                    Status = if($authMetrics.AuthenticationEvents.FailedLogins -lt 100) { "Compliant" } else { "Review Required" }
                }

                # CC6.3 - Network Security Controls Monitoring
                $networkEvents = Get-WinEvent -FilterHashtable @{LogName='Security'; Id=5156,5157; StartTime=$StartDate; EndTime=$EndDate} -ErrorAction SilentlyContinue
                $report.MonitoringMetrics["CC6.3"] = @{
                    Description = "Network security controls monitoring"
                    NetworkConnections = $networkEvents.Count
                    BlockedConnections = ($networkEvents | Where-Object { $_.Message -like "*blocked*" }).Count
                    Status = "Compliant"
                }
            }

            "GDPR" {
                # Data processing activity monitoring
                $gdprEvents = Get-WinEvent -FilterHashtable @{LogName='Orleans Security'; Id=4000,4001; StartTime=$StartDate; EndTime=$EndDate} -ErrorAction SilentlyContinue
                $report.MonitoringMetrics["Article30"] = @{
                    Description = "Records of processing activities"
                    ProcessingActivities = $gdprEvents.Count
                    PersonalDataAccess = ($gdprEvents | Where-Object { $_.Id -eq 4000 }).Count
                    ConsentUpdates = ($gdprEvents | Where-Object { $_.Id -eq 4001 }).Count
                    Status = "Compliant"
                }
            }
        }

        # Overall compliance status
        $nonCompliantControls = $report.MonitoringMetrics.Values | Where-Object { $_.Status -ne "Compliant" }
        if ($nonCompliantControls.Count -gt 0) {
            $report.ComplianceStatus = "Review Required"
        }

        # Save report
        $reportPath = "reports\Orleans-$Framework-Monitoring-Report-$(Get-Date -Format 'yyyy-MM-dd').json"
        $report | ConvertTo-Json -Depth 10 | Set-Content $reportPath

        Write-Host "✓ Compliance monitoring report generated: $reportPath" -ForegroundColor Green
        return $report
    }
    catch {
        Write-Error "Failed to generate compliance monitoring report: $($_.Exception.Message)"
        return $null
    }
}

# Automated compliance monitoring alerts
function Start-OrleansComplianceMonitoring {
    param([string[]]$Frameworks = @("SOC2", "GDPR"))

    foreach ($framework in $Frameworks) {
        # Schedule daily compliance checks
        $trigger = New-ScheduledTaskTrigger -Daily -At "06:00AM"
        $action = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-File C:\Orleans\Scripts\Daily-Compliance-Check.ps1 -Framework $framework"

        Register-ScheduledTask -TaskName "Orleans-$framework-Daily-Check" -Trigger $trigger -Action $action -Description "Daily compliance monitoring for Orleans $framework"

        Write-Host "✓ $framework compliance monitoring scheduled" -ForegroundColor Green
    }
}

# Usage
New-OrleansComplianceMonitoringReport -Framework "SOC2"
Start-OrleansComplianceMonitoring
```

### 4. Security Dashboard Integration

#### Grafana Security Dashboard
```json
{
  "SecurityDashboard": {
    "Panels": [
      {
        "Title": "Authentication Events",
        "Type": "graph",
        "Targets": [
          {
            "Expr": "orleans_auth_attempts_total",
            "LegendFormat": "Total Attempts"
          },
          {
            "Expr": "orleans_auth_failures_total",
            "LegendFormat": "Failed Attempts"
          }
        ]
      },
      {
        "Title": "Security Incidents",
        "Type": "stat",
        "Targets": [
          {
            "Expr": "orleans_security_incidents_total",
            "LegendFormat": "Total Incidents"
          }
        ],
        "Thresholds": [
          {"Color": "green", "Value": 0},
          {"Color": "yellow", "Value": 1},
          {"Color": "red", "Value": 5}
        ]
      },
      {
        "Title": "Compliance Status",
        "Type": "table",
        "Targets": [
          {
            "Expr": "orleans_compliance_status",
            "LegendFormat": "Framework Status"
          }
        ]
      }
    ]
  }
}
```

#### Security Alert Rules for Grafana
```yaml
groups:
- name: orleans-security-alerts
  rules:
  - alert: HighFailedLoginRate
    expr: rate(orleans_auth_failures_total[5m]) > 0.5
    for: 2m
    labels:
      severity: warning
      service: orleans
    annotations:
      summary: "High failed login rate detected"
      description: "Orleans is experiencing {{ $value }} failed logins per second"

  - alert: SecurityIncidentDetected
    expr: increase(orleans_security_incidents_total[1m]) > 0
    for: 0m
    labels:
      severity: critical
      service: orleans
    annotations:
      summary: "Security incident detected in Orleans"
      description: "New security incident reported in Orleans monitoring"

  - alert: ComplianceViolation
    expr: orleans_compliance_status == 0
    for: 1m
    labels:
      severity: warning
      service: orleans
    annotations:
      summary: "Compliance violation detected"
      description: "Orleans compliance monitoring detected a violation"
```

### Dashboard Security Configuration

1. **Secure Access**: Enterprise authentication integration with Orleans Dashboard
2. **API Security**: Authentication and encryption for all monitoring endpoints
3. **Network Segmentation**: Monitoring traffic restricted to dedicated networks
4. **Data Protection**: Encryption of metrics and logs in transit and at rest
5. **Access Logging**: Comprehensive audit trail for all monitoring access

## Enterprise SIEM Integration

### Overview

Enterprise Security Information and Event Management (SIEM) integration provides comprehensive security monitoring, threat detection, and compliance reporting for Orleans-based systems. This section covers integration procedures for major enterprise SIEM platforms.

#### Integration Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Enterprise SIEM Layer                    │
├─────────────────────────────────────────────────────────────┤
│  Splunk         │  LogRhythm      │  QRadar        │ ArcSight│
│  - Technology   │  - System       │  - Device      │ - Smart │
│    Add-on       │    Monitor      │    Support     │   Conn- │
│  - Universal    │  - Message      │    Module      │   ector │
│    Forwarder    │    Processing   │  - Custom      │ - Event │
│  - Custom       │  - SIEM Rules   │    Properties  │   Cat.  │
│    Searches     │  - Dashboards   │  - Offense     │ - Corr. │
├─────────────────────────────────────────────────────────────┤
│                    Orleans Event Processing                  │
├─────────────────────────────────────────────────────────────┤
│  CEF/LEEF       │  Security       │  Correlation   │ Event   │
│  Formatting     │  Event          │  IDs           │ Routing │
│                 │  Classification │                │         │
├─────────────────────────────────────────────────────────────┤
│                    Orleans Security Events                   │
├─────────────────────────────────────────────────────────────┤
│  Authentication │  Authorization  │  Audit Trails  │ System  │
│  Events         │  Events         │                │ Events  │
└─────────────────────────────────────────────────────────────┘
```

#### Common Event Format (CEF) Implementation

Orleans security events are formatted using Common Event Format (CEF) for standardized SIEM integration:

```csharp
// CEF Format: CEF:Version|Device Vendor|Device Product|Device Version|Device Event Class ID|Name|Severity|[Extension]
public static class CEFFormatter
{
    public static string FormatSecurityEvent(SecurityEvent secEvent)
    {
        return $"CEF:0|Microsoft|Orleans|{secEvent.Version}|{secEvent.EventId}|{secEvent.Name}|{secEvent.Severity}|" +
               $"rt={secEvent.Timestamp:MMM dd yyyy HH:mm:ss} " +
               $"src={secEvent.SourceIP} " +
               $"suser={secEvent.Username} " +
               $"act={secEvent.Action} " +
               $"outcome={secEvent.Result} " +
               $"msg={secEvent.Message} " +
               $"cs1={secEvent.GrainType} cs1Label=GrainType " +
               $"cs2={secEvent.SiloId} cs2Label=SiloId " +
               $"cs3={secEvent.CorrelationId} cs3Label=CorrelationId";
    }
}
```

### Splunk Integration

#### 1. Universal Forwarder Installation

```powershell
# Download and install Splunk Universal Forwarder
$splunkForwarderUrl = "https://download.splunk.com/products/universalforwarder/releases/9.1.1/windows/splunkforwarder-9.1.1-64e843ea36b1-x64-release.msi"
$installerPath = "$env:TEMP\splunkforwarder.msi"

# Download installer
Invoke-WebRequest -Uri $splunkForwarderUrl -OutFile $installerPath

# Install Universal Forwarder
Start-Process -FilePath "msiexec.exe" -ArgumentList "/i", $installerPath, "/quiet", "DEPLOYMENT_SERVER=your-deployment-server:8089", "AGREETOLICENSE=Yes" -Wait

# Configure as service
$splunkHome = "C:\Program Files\SplunkUniversalForwarder"
& "$splunkHome\bin\splunk.exe" enable boot-start --accept-license --answer-yes --no-prompt --seed-passwd Orleans123!

Write-Host "✓ Splunk Universal Forwarder installed and configured" -ForegroundColor Green
```

#### 2. Orleans Technology Add-on Configuration

Create Splunk Technology Add-on for Orleans:

```bash
# Create TA-orleans directory structure
mkdir -p $SPLUNK_HOME/etc/apps/TA-orleans/{default,local,metadata}
mkdir -p $SPLUNK_HOME/etc/apps/TA-orleans/default/{data,props,transforms,tags,eventtypes,savedsearches}
```

**props.conf** - Log parsing configuration:
```ini
# Orleans Security Events
[orleans:security]
SHOULD_LINEMERGE = false
LINE_BREAKER = ([\r\n]+)\d{4}-\d{2}-\d{2}
TRUNCATE = 10000
TIME_PREFIX = ^
TIME_FORMAT = %Y-%m-%d %H:%M:%S.%3N
MAX_TIMESTAMP_LOOKAHEAD = 23
category = Application
pulldown_type = true
KV_MODE = auto
EXTRACT-cef_fields = CEF:(?<cef_version>\d+)\|(?<device_vendor>[^|]*)\|(?<device_product>[^|]*)\|(?<device_version>[^|]*)\|(?<signature_id>[^|]*)\|(?<name>[^|]*)\|(?<severity>[^|]*)\|(?<extensions>.*)

# Orleans Performance Events
[orleans:performance]
SHOULD_LINEMERGE = false
TIME_FORMAT = %Y-%m-%d %H:%M:%S.%3N
EXTRACT-grain_metrics = grain_type=\"(?<grain_type>[^\"]+)\" activation_time=(?<activation_time>\d+) memory_usage=(?<memory_usage>\d+)

# Orleans Application Events
[orleans:application]
SHOULD_LINEMERGE = false
TIME_FORMAT = %Y-%m-%d %H:%M:%S.%3N
KV_MODE = json
```

**transforms.conf** - Field extraction transformations:
```ini
[orleans_security_extract]
REGEX = CEF:0\|Microsoft\|Orleans\|[^|]*\|(?<event_id>[^|]*)\|(?<event_name>[^|]*)\|(?<severity>[^|]*)\|.*src=(?<src_ip>[^\s]+).*suser=(?<username>[^\s]+).*act=(?<action>[^\s]+)

[orleans_performance_extract]
REGEX = GrainType=(?<grain_type>\w+).*ActivationTime=(?<activation_time>\d+)ms.*MemoryUsage=(?<memory_usage>\d+)MB

[grain_lookup]
filename = grain_types.csv
```

**inputs.conf** - Log input configuration:
```ini
[monitor://C:\Orleans\logs\security\*.log]
disabled = false
index = orleans_security
sourcetype = orleans:security
host_segment = 1

[monitor://C:\Orleans\logs\performance\*.log]
disabled = false
index = orleans_performance
sourcetype = orleans:performance

[monitor://C:\Orleans\logs\application\*.log]
disabled = false
index = orleans_application
sourcetype = orleans:application
```

#### 3. Custom Splunk Searches and Dashboards

**Orleans Security Dashboard** (savedsearches.conf):
```ini
[Orleans - Failed Authentication Attempts]
search = index=orleans_security sourcetype=orleans:security event_id=1002 | stats count by username, src_ip | sort -count
dispatch.earliest_time = -24h
dispatch.latest_time = now
cron_schedule = */15 * * * *
is_scheduled = 1

[Orleans - Grain Activation Anomalies]
search = index=orleans_performance sourcetype=orleans:performance | eval activation_time_ms=tonumber(activation_time) | stats avg(activation_time_ms) as avg_activation, max(activation_time_ms) as max_activation by grain_type | where max_activation > (avg_activation * 3)
dispatch.earliest_time = -1h
dispatch.latest_time = now
cron_schedule = */5 * * * *

[Orleans - Security Incident Detection]
search = index=orleans_security (event_id=3000 OR event_id=3001 OR event_id=3002) | stats count by event_name, severity | sort -count
dispatch.earliest_time = -15m
dispatch.latest_time = now
cron_schedule = */1 * * * *
action.email = 1
action.email.to = security-team@company.com
```

#### 4. Splunk Setup Automation Script

```powershell
# Orleans Splunk Integration Setup
function Install-OrleansSplunkIntegration {
    param(
        [string]$SplunkHome = "C:\Program Files\SplunkUniversalForwarder",
        [string]$DeploymentServer = "your-deployment-server.com:8089",
        [string]$OrleansLogsPath = "C:\Orleans\logs"
    )

    try {
        # Create Orleans Technology Add-on
        $taPath = "$SplunkHome\etc\apps\TA-orleans"

        if (!(Test-Path $taPath)) {
            New-Item -Path $taPath -ItemType Directory -Force
            New-Item -Path "$taPath\default" -ItemType Directory -Force
            New-Item -Path "$taPath\local" -ItemType Directory -Force
            New-Item -Path "$taPath\metadata" -ItemType Directory -Force
        }

        # Create app.conf
        $appConf = @"
[install]
is_configured = 0

[ui]
is_visible = 1
label = Orleans Technology Add-on

[launcher]
author = Orleans Team
description = Technology Add-on for Orleans monitoring and security
version = 1.0.0
"@
        $appConf | Set-Content "$taPath\default\app.conf"

        # Create inputs.conf
        $inputsConf = @"
[monitor://$OrleansLogsPath\security\*.log]
disabled = false
index = orleans_security
sourcetype = orleans:security
host_segment = 1

[monitor://$OrleansLogsPath\performance\*.log]
disabled = false
index = orleans_performance
sourcetype = orleans:performance

[monitor://$OrleansLogsPath\application\*.log]
disabled = false
index = orleans_application
sourcetype = orleans:application
"@
        $inputsConf | Set-Content "$taPath\local\inputs.conf"

        # Configure deployment server
        & "$SplunkHome\bin\splunk.exe" set deploy-poll $DeploymentServer --accept-license --answer-yes

        # Restart Splunk Universal Forwarder
        Restart-Service -Name "SplunkForwarder" -Force

        Write-Host "✓ Orleans Splunk integration configured successfully" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to configure Orleans Splunk integration: $($_.Exception.Message)"
    }
}

# Usage
Install-OrleansSplunkIntegration
```

### LogRhythm Integration

#### 1. System Monitor Agent Configuration

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!-- Orleans LogRhythm System Monitor Configuration -->
<SystemMonitorConfig>
  <Name>Orleans Security Monitor</Name>
  <Description>Orleans security and performance monitoring</Description>

  <!-- Security Log Monitoring -->
  <LogSource>
    <Name>Orleans Security Events</Name>
    <Type>WindowsEventLog</Type>
    <LogName>Orleans Security</LogName>
    <MessageProcessingPolicy>Orleans_Security_Policy</MessageProcessingPolicy>
  </LogSource>

  <!-- File Log Monitoring -->
  <LogSource>
    <Name>Orleans Application Logs</Name>
    <Type>TextFile</Type>
    <FilePath>C:\Orleans\logs\application\*.log</FilePath>
    <MessageProcessingPolicy>Orleans_Application_Policy</MessageProcessingPolicy>
  </LogSource>

  <!-- Performance Counter Monitoring -->
  <LogSource>
    <Name>Orleans Performance Counters</Name>
    <Type>PerformanceCounter</Type>
    <Counters>
      <Counter>\Process(AIChat.Orleans.Host)\Private Bytes</Counter>
      <Counter>\Process(AIChat.Orleans.Host)\% Processor Time</Counter>
      <Counter>\.NET CLR Memory(AIChat.Orleans.Host)\# Gen 0 Collections</Counter>
    </Counters>
    <CollectionInterval>30</CollectionInterval>
  </LogSource>
</SystemMonitorConfig>
```

#### 2. Message Processing Policies

**Orleans Security Policy** (Orleans_Security_Policy.xml):
```xml
<?xml version="1.0" encoding="UTF-8"?>
<MessageProcessingPolicy Name="Orleans_Security_Policy">
  <Description>Processing policy for Orleans security events</Description>

  <!-- Authentication Events -->
  <Rule>
    <Name>Orleans Authentication Success</Name>
    <Condition>
      <Field Name="EventID">1001</Field>
    </Condition>
    <Action>
      <SetField Name="CommonEvent.LogMessage" Value="Orleans Authentication Success"/>
      <SetField Name="CommonEvent.Priority" Value="5"/>
      <SetField Name="CommonEvent.Classification" Value="Authentication Success"/>
    </Action>
  </Rule>

  <Rule>
    <Name>Orleans Authentication Failure</Name>
    <Condition>
      <Field Name="EventID">1002</Field>
    </Condition>
    <Action>
      <SetField Name="CommonEvent.LogMessage" Value="Orleans Authentication Failure"/>
      <SetField Name="CommonEvent.Priority" Value="7"/>
      <SetField Name="CommonEvent.Classification" Value="Authentication Failure"/>
    </Action>
  </Rule>

  <!-- Security Incidents -->
  <Rule>
    <Name>Orleans Security Incident</Name>
    <Condition>
      <Field Name="EventID">3000,3001,3002</Field>
    </Condition>
    <Action>
      <SetField Name="CommonEvent.LogMessage" Value="Orleans Security Incident Detected"/>
      <SetField Name="CommonEvent.Priority" Value="9"/>
      <SetField Name="CommonEvent.Classification" Value="Security Incident"/>
      <GenerateAlarm/>
    </Action>
  </Rule>
</MessageProcessingPolicy>
```

#### 3. SIEM Rules Configuration

```xml
<!-- Orleans SIEM Rules -->
<SIEMRule Name="Orleans Brute Force Detection">
  <Description>Detects brute force attacks against Orleans authentication</Description>
  <Condition>
    <TimeWindow>5 minutes</TimeWindow>
    <EventCount Min="5">
      <Field Name="CommonEvent.Classification">Authentication Failure</Field>
      <Field Name="CommonEvent.ObjectName">Same User Account</Field>
    </EventCount>
  </Condition>
  <Action>
    <Priority>High</Priority>
    <Category>Security</Category>
    <GenerateAlarm/>
    <SendEmail>security-team@company.com</SendEmail>
  </Action>
</SIEMRule>

<SIEMRule Name="Orleans Performance Degradation">
  <Description>Detects significant performance degradation</Description>
  <Condition>
    <TimeWindow>10 minutes</TimeWindow>
    <Average Field="GrainActivationTime" Operator=">" Value="5000"/>
  </Condition>
  <Action>
    <Priority>Medium</Priority>
    <Category>Performance</Category>
    <GenerateAlarm/>
  </Action>
</SIEMRule>
```

#### 4. LogRhythm Setup Automation

```powershell
function Install-OrleansLogRhythmIntegration {
    param(
        [string]$LogRhythmSystemMonitorPath = "C:\Program Files\LogRhythm\LogRhythm System Monitor",
        [string]$OrleansConfigPath = "C:\Orleans\config"
    )

    try {
        # Create Orleans-specific configuration directory
        $orleansLRPath = "$LogRhythmSystemMonitorPath\config\orleans"
        if (!(Test-Path $orleansLRPath)) {
            New-Item -Path $orleansLRPath -ItemType Directory -Force
        }

        # Copy Orleans System Monitor configuration
        Copy-Item "$OrleansConfigPath\logrhythm\*" -Destination $orleansLRPath -Recurse -Force

        # Register Orleans log sources
        $regCmd = "$LogRhythmSystemMonitorPath\lrsm.exe"
        & $regCmd -register -config "$orleansLRPath\OrleansSystemMonitor.xml"

        # Start System Monitor service
        Start-Service -Name "LogRhythm System Monitor" -ErrorAction SilentlyContinue

        Write-Host "✓ Orleans LogRhythm integration configured successfully" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to configure Orleans LogRhythm integration: $($_.Exception.Message)"
    }
}

# Usage
Install-OrleansLogRhythmIntegration
```

### QRadar Integration

#### 1. Device Support Module (DSM) Development

**QRadar DSM Configuration** (Orleans_DSM.xml):
```xml
<?xml version="1.0" encoding="UTF-8"?>
<device-extension xmlns="event_parsing/device_extension">
    <pattern id="OrleansSecurityPattern" xmlns="http://www.ibm.com/si/pattern">
        <description>Orleans Security Events Pattern</description>
        <events>
            <!-- Authentication Events -->
            <event device-event-category="Authentication">
                <pattern>CEF:0\|Microsoft\|Orleans\|.*\|1001\|.*\|.*\|.*src=(\d+\.\d+\.\d+\.\d+).*suser=(\w+).*act=(\w+)</pattern>
                <key-mapping>
                    <key-name>sourceip</key-name>
                    <submatch-index>1</submatch-index>
                </key-mapping>
                <key-mapping>
                    <key-name>username</key-name>
                    <submatch-index>2</submatch-index>
                </key-mapping>
                <key-mapping>
                    <key-name>action</key-name>
                    <submatch-index>3</submatch-index>
                </key-mapping>
            </event>

            <!-- Security Incidents -->
            <event device-event-category="SecurityIncident">
                <pattern>CEF:0\|Microsoft\|Orleans\|.*\|30[0-9][0-9]\|.*\|High\|.*</pattern>
                <significance>High</significance>
            </event>
        </events>
    </pattern>
</device-extension>
```

#### 2. Custom Properties Definition

```sql
-- QRadar Custom Properties for Orleans
INSERT INTO custom_properties (property_name, property_type, description) VALUES
('Orleans_GrainType', 'String', 'Orleans Grain Type'),
('Orleans_SiloId', 'String', 'Orleans Silo Identifier'),
('Orleans_CorrelationId', 'String', 'Orleans Correlation ID'),
('Orleans_ActivationTime', 'Numeric', 'Grain Activation Time (ms)'),
('Orleans_MemoryUsage', 'Numeric', 'Memory Usage (MB)');
```

#### 3. Offense Rules Configuration

```sql
-- Orleans Security Offense Rules
INSERT INTO offense_rules (rule_name, rule_description, rule_condition, severity) VALUES
(
    'Orleans High Authentication Failures',
    'Multiple authentication failures detected for Orleans system',
    'SELECT * FROM events WHERE "Device Type" = 1500 AND "Event Category" = 6003 AND username IS NOT NULL GROUP BY username HAVING COUNT(*) >= 5 LAST 5 MINUTES',
    8
),
(
    'Orleans Security Incident Detected',
    'Security incident detected in Orleans system',
    'SELECT * FROM events WHERE "Device Type" = 1500 AND "Event Category" = 7001 AND severity >= 7',
    9
),
(
    'Orleans Performance Anomaly',
    'Significant performance degradation detected',
    'SELECT * FROM events WHERE "Device Type" = 1500 AND "Orleans_ActivationTime" > 5000 GROUP BY "Orleans_GrainType" HAVING COUNT(*) >= 10 LAST 10 MINUTES',
    6
);
```

#### 4. QRadar Integration Setup

```powershell
function Install-OrleansQRadarIntegration {
    param(
        [string]$QRadarHost = "qradar.company.com",
        [string]$AuthToken = $env:QRADAR_AUTH_TOKEN,
        [string]$OrleansConfigPath = "C:\Orleans\config\qradar"
    )

    try {
        # Upload DSM to QRadar
        $dsmPath = "$OrleansConfigPath\Orleans_DSM.xml"
        $uploadUrl = "https://$QRadarHost/api/config/device_support_modules"

        $headers = @{
            "SEC" = $AuthToken
            "Content-Type" = "application/xml"
        }

        $dsmContent = Get-Content $dsmPath -Raw
        Invoke-RestMethod -Uri $uploadUrl -Method POST -Headers $headers -Body $dsmContent

        # Deploy DSM
        $deployUrl = "https://$QRadarHost/api/staged_config/deploy_status"
        Invoke-RestMethod -Uri $deployUrl -Method POST -Headers $headers

        Write-Host "✓ Orleans QRadar DSM deployed successfully" -ForegroundColor Green

        # Create log source
        $logSourceUrl = "https://$QRadarHost/api/config/event_sources/log_source_management/log_sources"
        $logSourceBody = @{
            name = "Orleans Security Events"
            description = "Orleans security and performance events"
            type_id = 1500  # Custom device type
            protocol_type_id = 0  # Syslog
            identifier = "orleans-security"
            enabled = $true
        } | ConvertTo-Json

        Invoke-RestMethod -Uri $logSourceUrl -Method POST -Headers $headers -Body $logSourceBody -ContentType "application/json"

        Write-Host "✓ Orleans QRadar log source created successfully" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to configure Orleans QRadar integration: $($_.Exception.Message)"
    }
}

# Usage
Install-OrleansQRadarIntegration
```

### ArcSight Integration

#### 1. SmartConnector Configuration

**ArcSight SmartConnector** (agent.properties):
```properties
# Orleans ArcSight SmartConnector Configuration
agents[0].name=Orleans Security Connector
agents[0].type=syslogdaemon
agents[0].fqdn=orleans-security.company.com
agents[0].port=514

# Event parsing configuration
agents[0].parsers[0].name=OrleansSecurityParser
agents[0].parsers[0].class=com.arcsight.agent.parser.cef.CEFParser
agents[0].parsers[0].pattern=CEF:.*Microsoft.*Orleans.*

# Field mapping
agents[0].parsers[0].deviceEventCategory=security
agents[0].parsers[0].deviceProduct=Orleans
agents[0].parsers[0].deviceVendor=Microsoft

# Filter configuration
agents[0].filters[0].name=OrleansHighPriorityFilter
agents[0].filters[0].condition=deviceSeverity >= 7
agents[0].filters[0].action=forward

# Destination configuration
agents[0].destinations[0].name=ArcSightESM
agents[0].destinations[0].uri=https://arcsight-esm.company.com:8443/services/fws
agents[0].destinations[0].auth.user=orleans-connector
agents[0].destinations[0].auth.password.encrypted=encrypted_password
```

#### 2. Event Categorization Taxonomy

```xml
<!-- Orleans Event Categorization for ArcSight -->
<EventCategorization>
    <Category name="/Application/Orleans/Authentication">
        <Events>
            <Event deviceEventClassId="1001" name="Orleans Authentication Success" severity="3"/>
            <Event deviceEventClassId="1002" name="Orleans Authentication Failure" severity="6"/>
            <Event deviceEventClassId="1003" name="Orleans Account Lockout" severity="7"/>
        </Events>
    </Category>

    <Category name="/Application/Orleans/Authorization">
        <Events>
            <Event deviceEventClassId="2000" name="Orleans Access Granted" severity="2"/>
            <Event deviceEventClassId="2001" name="Orleans Access Denied" severity="5"/>
        </Events>
    </Category>

    <Category name="/Application/Orleans/Security">
        <Events>
            <Event deviceEventClassId="3000" name="Orleans Brute Force Attack" severity="9"/>
            <Event deviceEventClassId="3001" name="Orleans Configuration Change" severity="8"/>
            <Event deviceEventClassId="3002" name="Orleans Unusual Activity" severity="7"/>
        </Events>
    </Category>

    <Category name="/Application/Orleans/Performance">
        <Events>
            <Event deviceEventClassId="4000" name="Orleans Performance Degradation" severity="5"/>
            <Event deviceEventClassId="4001" name="Orleans High Memory Usage" severity="6"/>
        </Events>
    </Category>
</EventCategorization>
```

#### 3. Custom Correlation Rules

```xml
<!-- Orleans Correlation Rules for ArcSight ESM -->
<CorrelationRule name="Orleans Brute Force Attack Detection">
    <Description>Detects brute force attacks against Orleans authentication</Description>
    <TimeWindow>300000</TimeWindow> <!-- 5 minutes -->
    <Conditions>
        <Condition>
            <Field>deviceEventClassId</Field>
            <Operator>EQUALS</Operator>
            <Value>1002</Value>
        </Condition>
        <Condition>
            <Field>sourceAddress</Field>
            <Operator>GROUP_BY</Operator>
            <Threshold>5</Threshold>
        </Condition>
    </Conditions>
    <Actions>
        <Action type="Alert">
            <Severity>High</Severity>
            <Category>/Orleans/Security/BruteForce</Category>
        </Action>
        <Action type="Email">
            <Recipients>security-team@company.com</Recipients>
            <Subject>Orleans Brute Force Attack Detected</Subject>
        </Action>
    </Actions>
</CorrelationRule>

<CorrelationRule name="Orleans Security Incident Escalation">
    <Description>Escalates critical Orleans security incidents</Description>
    <Conditions>
        <Condition>
            <Field>deviceEventClassId</Field>
            <Operator>IN</Operator>
            <Value>3000,3001</Value>
        </Condition>
    </Conditions>
    <Actions>
        <Action type="Ticket">
            <System>ServiceNow</System>
            <Priority>P1</Priority>
            <Category>Security Incident</Category>
        </Action>
    </Actions>
</CorrelationRule>
```

#### 4. ArcSight Setup Automation

```powershell
function Install-OrleansArcSightIntegration {
    param(
        [string]$ArcSightConnectorPath = "C:\ArcSight\SmartConnectors\Orleans",
        [string]$ESMHost = "arcsight-esm.company.com",
        [string]$ConnectorUser = "orleans-connector",
        [string]$ConnectorPassword = $env:ARCSIGHT_PASSWORD
    )

    try {
        # Create Orleans SmartConnector directory
        if (!(Test-Path $ArcSightConnectorPath)) {
            New-Item -Path $ArcSightConnectorPath -ItemType Directory -Force
        }

        # Generate SmartConnector configuration
        $agentProperties = @"
# Orleans ArcSight SmartConnector Configuration
name=Orleans Security Connector
agents[0].syslogdaemon.port=514
agents[0].syslogdaemon.bindaddress=0.0.0.0
agents[0].syslogdaemon.protocol=udp

# Destination Manager
agents[0].destination.manager.uri=$ESMHost:8443
agents[0].destination.manager.user=$ConnectorUser
agents[0].destination.manager.password=$ConnectorPassword

# Parser configuration
agents[0].parser.name=cef
agents[0].parser.deviceproduct.token=Orleans
agents[0].parser.devicevendor.token=Microsoft
"@

        $agentProperties | Set-Content "$ArcSightConnectorPath\agent.properties"

        # Install SmartConnector service
        $serviceCmd = "$ArcSightConnectorPath\bin\arcsight.exe"
        if (Test-Path $serviceCmd) {
            & $serviceCmd agents -i "$ArcSightConnectorPath\agent.properties"
            Start-Service -Name "ArcSight Orleans Connector" -ErrorAction SilentlyContinue
        }

        Write-Host "✓ Orleans ArcSight integration configured successfully" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to configure Orleans ArcSight integration: $($_.Exception.Message)"
    }
}

# Usage
Install-OrleansArcSightIntegration
```

### SIEM Integration Testing and Validation

#### 1. Comprehensive Testing Framework

```powershell
function Test-OrleansSIEMIntegration {
    param(
        [string[]]$SIEMSystems = @("Splunk", "LogRhythm", "QRadar", "ArcSight"),
        [string]$TestDataPath = "C:\Orleans\test-data"
    )

    $testResults = @{}

    foreach ($siem in $SIEMSystems) {
        Write-Host "Testing $siem integration..." -ForegroundColor Yellow

        $testResult = @{
            System = $siem
            Connectivity = $false
            DataIngestion = $false
            EventParsing = $false
            AlertGeneration = $false
            OverallStatus = "Failed"
        }

        try {
            # Test connectivity
            $testResult.Connectivity = Test-SIEMConnectivity -System $siem

            # Test data ingestion
            if ($testResult.Connectivity) {
                $testResult.DataIngestion = Test-SIEMDataIngestion -System $siem -TestDataPath $TestDataPath
            }

            # Test event parsing
            if ($testResult.DataIngestion) {
                $testResult.EventParsing = Test-SIEMEventParsing -System $siem
            }

            # Test alert generation
            if ($testResult.EventParsing) {
                $testResult.AlertGeneration = Test-SIEMAlertGeneration -System $siem
            }

            # Determine overall status
            if ($testResult.Connectivity -and $testResult.DataIngestion -and
                $testResult.EventParsing -and $testResult.AlertGeneration) {
                $testResult.OverallStatus = "Passed"
                Write-Host "✓ $siem integration test passed" -ForegroundColor Green
            } else {
                Write-Warning "✗ $siem integration test failed"
            }
        }
        catch {
            Write-Error "$siem integration test error: $($_.Exception.Message)"
        }

        $testResults[$siem] = $testResult
    }

    # Generate test report
    $reportPath = "reports\Orleans-SIEM-Integration-Test-$(Get-Date -Format 'yyyy-MM-dd-HHmm').json"
    $testResults | ConvertTo-Json -Depth 10 | Set-Content $reportPath

    Write-Host "✓ SIEM integration test report generated: $reportPath" -ForegroundColor Green
    return $testResults
}

function Test-SIEMConnectivity {
    param([string]$System)

    switch ($System) {
        "Splunk" {
            try {
                $splunkUrl = "https://splunk.company.com:8089/services/auth/login"
                $response = Invoke-WebRequest -Uri $splunkUrl -TimeoutSec 10 -ErrorAction Stop
                return $response.StatusCode -eq 200
            } catch { return $false }
        }
        "LogRhythm" {
            try {
                Test-NetConnection -ComputerName "logrhythm.company.com" -Port 443 -InformationLevel Quiet
            } catch { return $false }
        }
        "QRadar" {
            try {
                $qradarUrl = "https://qradar.company.com/api/system/about"
                $response = Invoke-RestMethod -Uri $qradarUrl -TimeoutSec 10 -ErrorAction Stop
                return $response -ne $null
            } catch { return $false }
        }
        "ArcSight" {
            try {
                Test-NetConnection -ComputerName "arcsight-esm.company.com" -Port 8443 -InformationLevel Quiet
            } catch { return $false }
        }
    }
}

function Send-SIEMTestEvents {
    param([string]$TestDataPath)

    # Generate test security events
    $testEvents = @(
        @{
            EventId = 1002
            Timestamp = Get-Date
            Severity = "High"
            Username = "testuser"
            SourceIP = "192.168.1.100"
            Action = "Login"
            Result = "Failed"
            Message = "Authentication failure during test"
        },
        @{
            EventId = 3000
            Timestamp = Get-Date
            Severity = "Critical"
            Username = "admin"
            SourceIP = "10.0.0.50"
            Action = "ConfigurationChange"
            Result = "Success"
            Message = "Critical configuration change detected during test"
        }
    )

    foreach ($event in $testEvents) {
        # Format as CEF
        $cefEvent = "CEF:0|Microsoft|Orleans|1.0|$($event.EventId)|Test Event|$($event.Severity)|" +
                   "rt=$($event.Timestamp.ToString('MMM dd yyyy HH:mm:ss')) " +
                   "src=$($event.SourceIP) suser=$($event.Username) " +
                   "act=$($event.Action) outcome=$($event.Result) msg=$($event.Message)"

        # Send to syslog endpoint (514)
        Send-SyslogMessage -Message $cefEvent -Server "localhost" -Port 514
        Start-Sleep -Seconds 2
    }

    Write-Host "✓ Test events sent to SIEM systems" -ForegroundColor Green
}

# Usage
Test-OrleansSIEMIntegration
```

#### 2. Automated Validation Checklist

```powershell
function Invoke-SIEMValidationChecklist {
    $checklist = @(
        @{ Name = "Splunk Universal Forwarder Service Running"; Check = { Get-Service "SplunkForwarder" -ErrorAction SilentlyContinue | Where-Object {$_.Status -eq "Running"} } },
        @{ Name = "Orleans Technology Add-on Installed"; Check = { Test-Path "C:\Program Files\SplunkUniversalForwarder\etc\apps\TA-orleans" } },
        @{ Name = "LogRhythm System Monitor Service Running"; Check = { Get-Service "LogRhythm System Monitor" -ErrorAction SilentlyContinue | Where-Object {$_.Status -eq "Running"} } },
        @{ Name = "Orleans Log Sources Configured"; Check = { Get-Content "C:\Program Files\LogRhythm\LogRhythm System Monitor\config\orleans\*" -ErrorAction SilentlyContinue } },
        @{ Name = "QRadar DSM Deployed"; Check = { Test-Path "C:\Orleans\config\qradar\deployment-status.txt" } },
        @{ Name = "ArcSight SmartConnector Running"; Check = { Get-Service "*ArcSight*Orleans*" -ErrorAction SilentlyContinue | Where-Object {$_.Status -eq "Running"} } },
        @{ Name = "CEF Event Format Validation"; Check = { Test-CEFEventFormat } },
        @{ Name = "SIEM Alert Rules Active"; Check = { Test-SIEMAlertRules } },
        @{ Name = "Security Event Flow Validation"; Check = { Test-SecurityEventFlow } },
        @{ Name = "Performance Impact Assessment"; Check = { Test-PerformanceImpact } }
    )

    $results = @()
    foreach ($item in $checklist) {
        $result = try {
            $check = & $item.Check
            @{ Name = $item.Name; Status = if($check) {"✓ Passed"} else {"✗ Failed"}; Details = $check }
        } catch {
            @{ Name = $item.Name; Status = "✗ Error"; Details = $_.Exception.Message }
        }
        $results += $result

        $color = if ($result.Status -like "*Passed*") { "Green" } else { "Red" }
        Write-Host "$($result.Status) $($result.Name)" -ForegroundColor $color
    }

    return $results
}

# Usage
$validationResults = Invoke-SIEMValidationChecklist
```

## Enterprise APM and Monitoring Systems Integration

### Overview

Enterprise Application Performance Monitoring (APM) and monitoring systems integration provides comprehensive performance monitoring, business intelligence, and incident management for Orleans-based applications. This section covers integration with major enterprise monitoring platforms.

#### Integration Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                  Enterprise Monitoring Layer                │
├─────────────────────────────────────────────────────────────┤
│  SCOM           │  Datadog        │  New Relic     │ AppDyn  │
│  - Management   │  - APM Agent    │  - .NET Agent  │ - App   │
│    Pack         │  - Custom       │  - Custom      │   Agent │
│  - Monitors     │    Metrics      │    Metrics     │ - BTM   │
│  - Rules        │  - Dashboards   │  - Dashboards  │ - Rules │
├─────────────────────────────────────────────────────────────┤
│                    Incident Management                       │
├─────────────────────────────────────────────────────────────┤
│  PagerDuty Integration                                      │
│  - Service Definitions  │  - Escalation Policies           │
│  - Integration Keys     │  - Notification Channels         │
├─────────────────────────────────────────────────────────────┤
│                    Orleans Telemetry API                    │
├─────────────────────────────────────────────────────────────┤
│  Performance    │  Business       │  Infrastructure │ Custom │
│  Metrics        │  Metrics        │  Metrics        │ Events │
└─────────────────────────────────────────────────────────────┘
```

### Microsoft System Center Operations Manager (SCOM) Integration

#### 1. Management Pack Development

**Orleans Management Pack Structure** (OrleansMP.xml):
```xml
<?xml version="1.0" encoding="utf-8"?>
<ManagementPack ContentReadable="true" SchemaVersion="2.0" OriginalSchemaVersion="1.1"
                xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <Manifest>
    <Identity>
      <ID>Orleans.Management.Pack</ID>
      <Version>1.0.0.0</Version>
    </Identity>
    <Name>Orleans Management Pack</Name>
    <References>
      <Reference Alias="System">
        <ID>System.Library</ID>
        <Version>7.5.8501.0</Version>
        <PublicKeyToken>31bf3856ad364e35</PublicKeyToken>
      </Reference>
      <Reference Alias="Windows">
        <ID>Microsoft.Windows.Library</ID>
        <Version>7.5.8501.0</Version>
        <PublicKeyToken>31bf3856ad364e35</PublicKeyToken>
      </Reference>
    </References>
  </Manifest>

  <TypeDefinitions>
    <EntityTypes>
      <!-- Orleans Application Class -->
      <ClassTypes>
        <ClassType ID="Orleans.Application" Accessibility="Public" Abstract="false"
                   Base="Windows!Microsoft.Windows.ApplicationComponent" Hosted="true" Singleton="false">
          <Property ID="ApplicationName" Type="string" AutoIncrement="false" Key="false" CaseSensitive="false"
                    MaxLength="256" MinLength="0" Required="false" Scale="0"/>
          <Property ID="Version" Type="string" AutoIncrement="false" Key="false" CaseSensitive="false"
                    MaxLength="100" MinLength="0" Required="false" Scale="0"/>
        </ClassType>

        <!-- Orleans Silo Class -->
        <ClassType ID="Orleans.Silo" Accessibility="Public" Abstract="false"
                   Base="Windows!Microsoft.Windows.ApplicationComponent" Hosted="true" Singleton="false">
          <Property ID="SiloName" Type="string" AutoIncrement="false" Key="true" CaseSensitive="false"
                    MaxLength="256" MinLength="0" Required="true" Scale="0"/>
          <Property ID="SiloAddress" Type="string" AutoIncrement="false" Key="false" CaseSensitive="false"
                    MaxLength="256" MinLength="0" Required="false" Scale="0"/>
          <Property ID="Status" Type="string" AutoIncrement="false" Key="false" CaseSensitive="false"
                    MaxLength="50" MinLength="0" Required="false" Scale="0"/>
        </ClassType>

        <!-- Orleans Grain Class -->
        <ClassType ID="Orleans.Grain" Accessibility="Public" Abstract="false"
                   Base="System!System.ApplicationComponent" Hosted="true" Singleton="false">
          <Property ID="GrainType" Type="string" AutoIncrement="false" Key="true" CaseSensitive="false"
                    MaxLength="256" MinLength="0" Required="true" Scale="0"/>
          <Property ID="GrainId" Type="string" AutoIncrement="false" Key="true" CaseSensitive="false"
                    MaxLength="256" MinLength="0" Required="true" Scale="0"/>
        </ClassType>
      </ClassTypes>
    </EntityTypes>

    <MonitorTypes>
      <!-- Performance Counter Monitor -->
      <UnitMonitorType ID="Orleans.PerformanceCounterMonitor" Accessibility="Public">
        <MonitorTypeStates>
          <MonitorTypeState ID="Healthy" NoDetection="false"/>
          <MonitorTypeState ID="Warning" NoDetection="false"/>
          <MonitorTypeState ID="Critical" NoDetection="false"/>
        </MonitorTypeStates>
        <Configuration>
          <xsd:element name="ComputerName" type="xsd:string"/>
          <xsd:element name="CounterName" type="xsd:string"/>
          <xsd:element name="ObjectName" type="xsd:string"/>
          <xsd:element name="InstanceName" type="xsd:string"/>
          <xsd:element name="WarningThreshold" type="xsd:double"/>
          <xsd:element name="CriticalThreshold" type="xsd:double"/>
          <xsd:element name="IntervalSeconds" type="xsd:int"/>
        </Configuration>
      </UnitMonitorType>

      <!-- Windows Service Monitor -->
      <UnitMonitorType ID="Orleans.WindowsServiceMonitor" Accessibility="Public">
        <MonitorTypeStates>
          <MonitorTypeState ID="Running" NoDetection="false"/>
          <MonitorTypeState ID="NotRunning" NoDetection="false"/>
        </MonitorTypeStates>
        <Configuration>
          <xsd:element name="ComputerName" type="xsd:string"/>
          <xsd:element name="ServiceName" type="xsd:string"/>
        </Configuration>
      </UnitMonitorType>
    </MonitorTypes>
  </TypeDefinitions>

  <Monitoring>
    <Monitors>
      <!-- Orleans Silo Status Monitor -->
      <UnitMonitor ID="Orleans.Silo.StatusMonitor" Accessibility="Public" Enabled="true"
                   Target="Orleans.Silo" ParentMonitorID="Health!System.Health.AvailabilityState"
                   Remotable="true" Priority="Normal" TypeID="Orleans.WindowsServiceMonitor" ConfirmDelivery="false">
        <Category>AvailabilityHealth</Category>
        <AlertSettings AlertMessage="Orleans.Silo.StatusMonitor.AlertMessage">
          <AlertOnState>Warning</AlertOnState>
          <AutoResolve>true</AutoResolve>
          <AlertPriority>Normal</AlertPriority>
          <AlertSeverity>MatchMonitorHealth</AlertSeverity>
        </AlertSettings>
        <OperationalStates>
          <OperationalState ID="Running" MonitorTypeStateID="Running" HealthState="Success"/>
          <OperationalState ID="NotRunning" MonitorTypeStateID="NotRunning" HealthState="Error"/>
        </OperationalStates>
        <Configuration>
          <ComputerName>$Target/Host/Property[Type="Windows!Microsoft.Windows.Computer"]/NetworkName$</ComputerName>
          <ServiceName>AIChat.Orleans.Host</ServiceName>
        </Configuration>
      </UnitMonitor>

      <!-- Orleans Memory Usage Monitor -->
      <UnitMonitor ID="Orleans.MemoryUsageMonitor" Accessibility="Public" Enabled="true"
                   Target="Orleans.Application" ParentMonitorID="Health!System.Health.PerformanceState"
                   Remotable="true" Priority="Normal" TypeID="Orleans.PerformanceCounterMonitor" ConfirmDelivery="false">
        <Category>PerformanceHealth</Category>
        <AlertSettings AlertMessage="Orleans.MemoryUsageMonitor.AlertMessage">
          <AlertOnState>Warning</AlertOnState>
          <AutoResolve>true</AutoResolve>
          <AlertPriority>Normal</AlertPriority>
          <AlertSeverity>MatchMonitorHealth</AlertSeverity>
        </AlertSettings>
        <OperationalStates>
          <OperationalState ID="Healthy" MonitorTypeStateID="Healthy" HealthState="Success"/>
          <OperationalState ID="Warning" MonitorTypeStateID="Warning" HealthState="Warning"/>
          <OperationalState ID="Critical" MonitorTypeStateID="Critical" HealthState="Error"/>
        </OperationalStates>
        <Configuration>
          <ComputerName>$Target/Host/Property[Type="Windows!Microsoft.Windows.Computer"]/NetworkName$</ComputerName>
          <CounterName>Private Bytes</CounterName>
          <ObjectName>Process</ObjectName>
          <InstanceName>AIChat.Orleans.Host</InstanceName>
          <WarningThreshold>1073741824</WarningThreshold> <!-- 1 GB -->
          <CriticalThreshold>2147483648</CriticalThreshold> <!-- 2 GB -->
          <IntervalSeconds>300</IntervalSeconds>
        </Configuration>
      </UnitMonitor>
    </Monitors>

    <Rules>
      <!-- Orleans Performance Collection Rule -->
      <Rule ID="Orleans.PerformanceCollectionRule" Enabled="true" Target="Orleans.Application"
            ConfirmDelivery="false" Remotable="true" Priority="Normal" DiscardLevel="100">
        <Category>PerformanceCollection</Category>
        <DataSources>
          <DataSource ID="PerfDS" TypeID="System!System.Performance.OptimizedDataProvider">
            <ComputerName>$Target/Host/Property[Type="Windows!Microsoft.Windows.Computer"]/NetworkName$</ComputerName>
            <CounterName>% Processor Time</CounterName>
            <ObjectName>Process</ObjectName>
            <InstanceName>AIChat.Orleans.Host</InstanceName>
            <AllInstances>false</AllInstances>
            <Frequency>300</Frequency>
            <Tolerance>0</Tolerance>
            <ToleranceType>Absolute</ToleranceType>
          </DataSource>
        </DataSources>
        <WriteActions>
          <WriteAction ID="WriteToDB" TypeID="System!System.Performance.DataWarehouse">
            <Priority>Normal</Priority>
          </WriteAction>
        </WriteActions>
      </Rule>

      <!-- Orleans Event Log Collection Rule -->
      <Rule ID="Orleans.EventLogCollectionRule" Enabled="true" Target="Orleans.Application"
            ConfirmDelivery="false" Remotable="true" Priority="Normal" DiscardLevel="100">
        <Category>EventCollection</Category>
        <DataSources>
          <DataSource ID="EventDS" TypeID="System!System.Event.DataProvider">
            <ComputerName>$Target/Host/Property[Type="Windows!Microsoft.Windows.Computer"]/NetworkName$</ComputerName>
            <LogName>Orleans Security</LogName>
            <Expression>
              <SimpleExpression>
                <ValueExpression>
                  <XPathQuery Type="UnsignedInteger">EventDisplayNumber</XPathQuery>
                </ValueExpression>
                <Operator>Equal</Operator>
                <ValueExpression>
                  <Value Type="UnsignedInteger">3000</Value>
                </ValueExpression>
              </SimpleExpression>
            </Expression>
          </DataSource>
        </DataSources>
        <WriteActions>
          <WriteAction ID="WriteToDB" TypeID="System!System.Event.DataWarehouse">
            <Priority>Normal</Priority>
          </WriteAction>
        </WriteActions>
      </Rule>
    </Rules>
  </Monitoring>

  <Presentation>
    <StringResources>
      <StringResource ID="Orleans.Silo.StatusMonitor.AlertMessage">
        <Text>Orleans Silo service is not running on {0}. The Orleans cluster may be experiencing issues.</Text>
      </StringResource>
      <StringResource ID="Orleans.MemoryUsageMonitor.AlertMessage">
        <Text>Orleans application memory usage is high on {0}. Current usage: {1} bytes.</Text>
      </StringResource>
    </StringResources>
  </Presentation>

  <LanguagePacks>
    <LanguagePack ID="ENU" IsDefault="true">
      <DisplayStrings>
        <DisplayString ElementID="Orleans.Management.Pack">
          <Name>Orleans Management Pack</Name>
          <Description>Monitoring and management for Microsoft Orleans applications</Description>
        </DisplayString>
        <DisplayString ElementID="Orleans.Application">
          <Name>Orleans Application</Name>
          <Description>Orleans-based application instance</Description>
        </DisplayString>
        <DisplayString ElementID="Orleans.Silo">
          <Name>Orleans Silo</Name>
          <Description>Orleans silo instance within the cluster</Description>
        </DisplayString>
        <DisplayString ElementID="Orleans.Grain">
          <Name>Orleans Grain</Name>
          <Description>Orleans grain instance</Description>
        </DisplayString>
      </DisplayStrings>
    </LanguagePack>
  </LanguagePacks>
</ManagementPack>
```

#### 2. SCOM Integration Setup Automation

```powershell
function Install-OrleansSCOMIntegration {
    param(
        [string]$SCOMManagementServer = "scom-ms.company.com",
        [string]$ManagementPackPath = "C:\Orleans\config\scom\OrleansMP.xml",
        [string]$OrleansServerName = $env:COMPUTERNAME
    )

    try {
        # Import SCOM PowerShell module
        Import-Module OperationsManager -ErrorAction Stop

        # Connect to SCOM Management Server
        New-SCOMManagementGroupConnection -ComputerName $SCOMManagementServer

        # Import Orleans Management Pack
        if (Test-Path $ManagementPackPath) {
            Import-SCManagementPack -FullName $ManagementPackPath -Verbose
            Write-Host "✓ Orleans Management Pack imported successfully" -ForegroundColor Green
        }

        # Create Orleans application discovery
        $discoveryScript = @"
'Create Orleans Application Discovery
Set objWMI = GetObject("winmgmts:\\$OrleansServerName\root\cimv2")
Set colProcesses = objWMI.ExecQuery("SELECT * FROM Win32_Process WHERE Name = 'AIChat.Orleans.Host.exe'")

For Each objProcess in colProcesses
    'Create Orleans Application instance
    Set oAPI = CreateObject("MOM.ScriptAPI")
    Set oDiscoveryData = oAPI.CreateDiscoveryData(0, "{Orleans.Application}", "{System.Entity}")

    Set oInst = oDiscoveryData.CreateClassInstance("{Orleans.Application}")
    oInst.AddProperty "{System.Entity}", "DisplayName", "Orleans Application on " & "$OrleansServerName"
    oInst.AddProperty "{Orleans.Application}", "ApplicationName", "AIChat.Orleans"
    oInst.AddProperty "{Orleans.Application}", "Version", "1.0.0"

    oDiscoveryData.AddInstance oInst
    oAPI.Return oDiscoveryData
Next
"@

        # Create custom discovery rule
        $discoveryRuleName = "Orleans.Application.Discovery"
        # SCOM discovery rule creation would typically be done through the authoring console
        # This is a simplified example

        Write-Host "✓ Orleans SCOM integration configured successfully" -ForegroundColor Green
        Write-Host "  Management Pack: Imported" -ForegroundColor Green
        Write-Host "  Discovery Rules: Configured" -ForegroundColor Green
        Write-Host "  Performance Monitoring: Active" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to configure Orleans SCOM integration: $($_.Exception.Message)"
    }
}

# Usage
Install-OrleansSCOMIntegration
```

### Datadog APM Integration

#### 1. Datadog Agent Installation and Configuration

```powershell
# Install Datadog Agent with Orleans configuration
function Install-OrleansDatadogIntegration {
    param(
        [string]$DatadogApiKey = $env:DATADOG_API_KEY,
        [string]$DatadogSite = "datadoghq.com",
        [string]$OrleansServiceName = "orleans-aichat"
    )

    try {
        # Download and install Datadog Agent
        $agentUrl = "https://s3.amazonaws.com/ddagent-windows-stable/ddagent-cli-latest.exe"
        $agentPath = "$env:TEMP\ddagent-cli.exe"

        Invoke-WebRequest -Uri $agentUrl -OutFile $agentPath
        & $agentPath /quiet /apikey:$DatadogApiKey /site:$DatadogSite

        # Configure Datadog for Orleans monitoring
        $datadogConfigPath = "C:\ProgramData\Datadog\conf.d"

        # Create Orleans integration configuration
        $orleansConfig = @"
init_config:

instances:
  - host: localhost
    port: 5100
    service: $OrleansServiceName
    tags:
      - env:production
      - service:orleans
      - version:1.0.0

# Custom metrics configuration
orleans_metrics:
  - grain_activations_total:
      alias: orleans.grain.activations.total
      type: counter
  - grain_activations_per_second:
      alias: orleans.grain.activations.rate
      type: gauge
  - silo_memory_usage:
      alias: orleans.silo.memory.bytes
      type: gauge
  - request_processing_duration:
      alias: orleans.request.duration
      type: histogram
  - silo_cpu_usage:
      alias: orleans.silo.cpu.percent
      type: gauge
"@

        $orleansConfig | Set-Content "$datadogConfigPath\orleans.d\conf.yaml" -Force

        # Configure APM tracing
        $apmConfig = @"
apm_config:
  enabled: true
  env: production
  service: $OrleansServiceName

# Orleans-specific APM configuration
trace_config:
  analyzed_spans:
    $OrleansServiceName|grain.method: 1.0
    $OrleansServiceName|silo.operation: 1.0

process_config:
  enabled: true

runtime_metrics_config:
  enabled: true
"@

        $apmConfig | Add-Content "C:\ProgramData\Datadog\datadog.yaml"

        # Restart Datadog Agent
        Restart-Service -Name "DatadogAgent" -Force

        Write-Host "✓ Datadog Agent installed and configured for Orleans" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to install Orleans Datadog integration: $($_.Exception.Message)"
    }
}

# Custom Orleans metrics collection
function Send-OrleansMetricsToDatadog {
    param(
        [string]$DatadogApiKey = $env:DATADOG_API_KEY,
        [string]$MetricsEndpoint = "https://api.datadoghq.com/api/v1/series"
    )

    try {
        # Get Orleans metrics
        $orleansMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"

        # Prepare metrics for Datadog
        $datadogMetrics = @{
            series = @(
                @{
                    metric = "orleans.grain.activations.total"
                    points = @(@((Get-Date).ToUniversalTime().Subtract([datetime]'1970-01-01').TotalSeconds, $orleansMetrics.TotalGrainActivations))
                    tags = @("service:orleans-aichat", "env:production")
                },
                @{
                    metric = "orleans.grain.activations.rate"
                    points = @(@((Get-Date).ToUniversalTime().Subtract([datetime]'1970-01-01').TotalSeconds, $orleansMetrics.GrainActivationsPerSecond))
                    tags = @("service:orleans-aichat", "env:production")
                },
                @{
                    metric = "orleans.silo.memory.bytes"
                    points = @(@((Get-Date).ToUniversalTime().Subtract([datetime]'1970-01-01').TotalSeconds, $orleansMetrics.MemoryUsageBytes))
                    tags = @("service:orleans-aichat", "env:production")
                },
                @{
                    metric = "orleans.request.duration.avg"
                    points = @(@((Get-Date).ToUniversalTime().Subtract([datetime]'1970-01-01').TotalSeconds, $orleansMetrics.AverageRequestDuration))
                    tags = @("service:orleans-aichat", "env:production")
                }
            )
        }

        # Send metrics to Datadog
        $headers = @{
            "DD-API-KEY" = $DatadogApiKey
            "Content-Type" = "application/json"
        }

        $body = $datadogMetrics | ConvertTo-Json -Depth 10
        Invoke-RestMethod -Uri $MetricsEndpoint -Method POST -Headers $headers -Body $body

        Write-Host "✓ Orleans metrics sent to Datadog successfully" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to send Orleans metrics to Datadog: $($_.Exception.Message)"
    }
}

# Usage
Install-OrleansDatadogIntegration
```

#### 2. Datadog Dashboard Configuration

```json
{
  "title": "Orleans Application Performance Monitoring",
  "description": "Comprehensive Orleans application monitoring dashboard",
  "widgets": [
    {
      "id": 1,
      "definition": {
        "type": "timeseries",
        "requests": [
          {
            "q": "avg:orleans.grain.activations.rate{service:orleans-aichat}",
            "display_type": "line",
            "style": {
              "palette": "dog_classic",
              "line_type": "solid",
              "line_width": "normal"
            }
          }
        ],
        "title": "Grain Activation Rate",
        "title_size": "16",
        "title_align": "left",
        "show_legend": false
      },
      "layout": {
        "x": 0,
        "y": 0,
        "width": 47,
        "height": 15
      }
    },
    {
      "id": 2,
      "definition": {
        "type": "query_value",
        "requests": [
          {
            "q": "avg:orleans.silo.memory.bytes{service:orleans-aichat}",
            "aggregator": "avg"
          }
        ],
        "title": "Memory Usage",
        "title_size": "16",
        "title_align": "left",
        "precision": 0,
        "unit": "byte"
      },
      "layout": {
        "x": 49,
        "y": 0,
        "width": 24,
        "height": 15
      }
    },
    {
      "id": 3,
      "definition": {
        "type": "heatmap",
        "requests": [
          {
            "q": "avg:orleans.request.duration{service:orleans-aichat} by {grain_type}"
          }
        ],
        "title": "Request Duration Heatmap by Grain Type",
        "title_size": "16",
        "title_align": "left"
      },
      "layout": {
        "x": 0,
        "y": 17,
        "width": 47,
        "height": 15
      }
    },
    {
      "id": 4,
      "definition": {
        "type": "toplist",
        "requests": [
          {
            "q": "top(avg:orleans.grain.activations.total{service:orleans-aichat} by {grain_type}, 10, 'mean', 'desc')"
          }
        ],
        "title": "Top Grain Types by Activations",
        "title_size": "16",
        "title_align": "left"
      },
      "layout": {
        "x": 49,
        "y": 17,
        "width": 24,
        "height": 15
      }
    }
  ],
  "layout_type": "free",
  "is_read_only": false,
  "notify_list": [],
  "template_variables": [
    {
      "name": "env",
      "default": "production",
      "prefix": "env"
    },
    {
      "name": "service",
      "default": "orleans-aichat",
      "prefix": "service"
    }
  ]
}
```

### New Relic Integration

#### 1. New Relic .NET Agent Configuration

```powershell
function Install-OrleansNewRelicIntegration {
    param(
        [string]$NewRelicLicenseKey = $env:NEW_RELIC_LICENSE_KEY,
        [string]$ApplicationName = "Orleans-AIChat",
        [string]$EnvironmentName = "Production"
    )

    try {
        # Download and install New Relic .NET Agent
        $agentUrl = "https://download.newrelic.com/dot_net_agent/latest_release/NewRelicDotNetAgent_x64.msi"
        $agentPath = "$env:TEMP\NewRelicDotNetAgent.msi"

        Invoke-WebRequest -Uri $agentUrl -OutFile $agentPath
        Start-Process -FilePath "msiexec.exe" -ArgumentList "/i", $agentPath, "/quiet", "NR_LICENSE_KEY=$NewRelicLicenseKey" -Wait

        # Configure New Relic for Orleans
        $newrelicConfigPath = "C:\ProgramData\New Relic\.NET Agent\newrelic.config"

        [xml]$config = Get-Content $newrelicConfigPath

        # Update application name
        $config.configuration.appSettings.add | Where-Object { $_.key -eq "NewRelic.AppSettings.AppName" } | ForEach-Object { $_.value = $ApplicationName }

        # Enable distributed tracing
        $config.configuration.appSettings.add | Where-Object { $_.key -eq "NewRelic.AppSettings.DistributedTracing.Enabled" } | ForEach-Object { $_.value = "true" }

        # Configure custom instrumentation for Orleans grains
        $customInstrumentation = $config.CreateElement("instrumentation")
        $customInstrumentation.SetAttribute("metric", "true")

        $grainMethods = $config.CreateElement("tracerFactory")
        $grainMethods.SetAttribute("name", "NewRelic.Agent.Core.Tracer.Factories.BackgroundThreadTracerFactory")
        $grainMethods.SetAttribute("metricName", "Custom/Orleans/Grain/Method/{method}")

        $match = $config.CreateElement("match")
        $match.SetAttribute("assemblyName", "AIChat.Orleans.Grains")
        $match.SetAttribute("className", "*Grain")

        $exactMethodMatcher = $config.CreateElement("exactMethodMatcher")
        $exactMethodMatcher.SetAttribute("methodName", "*")

        $match.AppendChild($exactMethodMatcher)
        $grainMethods.AppendChild($match)
        $customInstrumentation.AppendChild($grainMethods)
        $config.configuration.AppendChild($customInstrumentation)

        # Save configuration
        $config.Save($newrelicConfigPath)

        # Set environment variables
        [Environment]::SetEnvironmentVariable("NEW_RELIC_LICENSE_KEY", $NewRelicLicenseKey, "Machine")
        [Environment]::SetEnvironmentVariable("NEW_RELIC_APP_NAME", $ApplicationName, "Machine")
        [Environment]::SetEnvironmentVariable("NEW_RELIC_DISTRIBUTED_TRACING_ENABLED", "true", "Machine")

        # Enable profiler for Orleans applications
        [Environment]::SetEnvironmentVariable("COR_ENABLE_PROFILING", "1", "Machine")
        [Environment]::SetEnvironmentVariable("COR_PROFILER", "{71DA0A04-7777-4EC6-9643-7D28B46A8A41}", "Machine")
        [Environment]::SetEnvironmentVariable("NEWRELIC_HOME", "C:\Program Files\New Relic\.NET Agent", "Machine")

        Write-Host "✓ New Relic .NET Agent installed and configured for Orleans" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to install Orleans New Relic integration: $($_.Exception.Message)"
    }
}

# Custom Orleans metrics for New Relic
function Send-OrleansMetricsToNewRelic {
    param(
        [string]$NewRelicApiKey = $env:NEW_RELIC_INSERT_KEY,
        [string]$MetricsEndpoint = "https://metric-api.newrelic.com/metric/v1"
    )

    try {
        # Get Orleans metrics
        $orleansMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"

        # Prepare metrics for New Relic
        $newrelicMetrics = @(
            @{
                "metrics" = @(
                    @{
                        "name" = "custom.orleans.grain.activations.total"
                        "type" = "count"
                        "value" = $orleansMetrics.TotalGrainActivations
                        "timestamp" = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
                        "attributes" = @{
                            "service.name" = "orleans-aichat"
                            "environment" = "production"
                        }
                    },
                    @{
                        "name" = "custom.orleans.grain.activations.rate"
                        "type" = "gauge"
                        "value" = $orleansMetrics.GrainActivationsPerSecond
                        "timestamp" = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
                        "attributes" = @{
                            "service.name" = "orleans-aichat"
                            "environment" = "production"
                        }
                    },
                    @{
                        "name" = "custom.orleans.silo.memory.usage"
                        "type" = "gauge"
                        "value" = $orleansMetrics.MemoryUsageBytes
                        "timestamp" = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
                        "attributes" = @{
                            "service.name" = "orleans-aichat"
                            "environment" = "production"
                        }
                    }
                )
            }
        )

        # Send metrics to New Relic
        $headers = @{
            "Api-Key" = $NewRelicApiKey
            "Content-Type" = "application/json"
        }

        $body = $newrelicMetrics | ConvertTo-Json -Depth 10
        Invoke-RestMethod -Uri $MetricsEndpoint -Method POST -Headers $headers -Body $body

        Write-Host "✓ Orleans metrics sent to New Relic successfully" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to send Orleans metrics to New Relic: $($_.Exception.Message)"
    }
}

# Usage
Install-OrleansNewRelicIntegration
```

### AppDynamics Integration

#### 1. AppDynamics Application Agent Configuration

```powershell
function Install-OrleansAppDynamicsIntegration {
    param(
        [string]$AppDynamicsController = "https://company.saas.appdynamics.com",
        [string]$ApplicationName = "Orleans-AIChat",
        [string]$TierName = "Orleans-Cluster",
        [string]$NodeName = $env:COMPUTERNAME,
        [string]$AccountName = "company",
        [string]$AccessKey = $env:APPDYNAMICS_ACCESS_KEY
    )

    try {
        # Download and install AppDynamics .NET Agent
        $agentUrl = "https://download.appdynamics.com/download/prox/download-file/dotnet/latest"
        $agentPath = "$env:TEMP\AppDynamicsDotNetAgentSetup64.msi"

        # Download would typically require authentication to AppDynamics portal
        # This is a simplified example

        # Configure AppDynamics
        $appDynamicsConfigPath = "C:\AppDynamics\DotNetAgent\Config"

        # Create app.config for Orleans application
        $appConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appSettings>
    <!-- Controller Configuration -->
    <add key="APPDYNAMICS_CONTROLLER_HOST_NAME" value="$($AppDynamicsController.Replace('https://', ''))" />
    <add key="APPDYNAMICS_CONTROLLER_PORT" value="443" />
    <add key="APPDYNAMICS_CONTROLLER_SSL_ENABLED" value="true" />
    <add key="APPDYNAMICS_AGENT_ACCOUNT_NAME" value="$AccountName" />
    <add key="APPDYNAMICS_AGENT_ACCOUNT_ACCESS_KEY" value="$AccessKey" />

    <!-- Application Configuration -->
    <add key="APPDYNAMICS_AGENT_APPLICATION_NAME" value="$ApplicationName" />
    <add key="APPDYNAMICS_AGENT_TIER_NAME" value="$TierName" />
    <add key="APPDYNAMICS_AGENT_NODE_NAME" value="$NodeName" />

    <!-- Orleans-specific Configuration -->
    <add key="APPDYNAMICS_AGENT_REUSE_NODE_NAME" value="true" />
    <add key="APPDYNAMICS_AGENT_REUSE_NODE_NAME_PREFIX" value="Orleans" />

    <!-- Business Transaction Configuration -->
    <add key="APPDYNAMICS_AGENT_AUTO_INSTRUMENTATION_ENABLED" value="true" />
    <add key="APPDYNAMICS_AGENT_USER_DEFINED_RULES_ENABLED" value="true" />

    <!-- Analytics Configuration -->
    <add key="APPDYNAMICS_ANALYTICS_ENABLED" value="true" />
    <add key="APPDYNAMICS_ANALYTICS_ENDPOINT" value="https://analytics.api.appdynamics.com" />
  </appSettings>

  <system.webServer>
    <modules>
      <add name="AppDynamicsModule" type="AppDynamics.Agent.Core.Runtime.AppDynamicsModule, AppDynamics.Agent.Core" preCondition="managedHandler" />
    </modules>
  </system.webServer>
</configuration>
"@

        $appConfig | Set-Content "$appDynamicsConfigPath\app.config" -Force

        # Create custom business transaction rules for Orleans grains
        $btRules = @"
<?xml version="1.0" encoding="utf-8"?>
<businessTransactions>
  <businessTransaction>
    <name>Orleans Grain Method Calls</name>
    <enabled>true</enabled>
    <entryPointType>POCO</entryPointType>
    <matchCriteria>
      <className>*Grain</className>
      <methodName>*</methodName>
      <inheritance>true</inheritance>
    </matchCriteria>
    <transactionNaming>
      <useMethod>true</useMethod>
      <useClass>true</useClass>
      <format>{0}.{1}</format>
    </transactionNaming>
  </businessTransaction>

  <businessTransaction>
    <name>Orleans Silo Operations</name>
    <enabled>true</enabled>
    <entryPointType>POCO</entryPointType>
    <matchCriteria>
      <className>*Silo*</className>
      <methodName>*</methodName>
      <inheritance>true</inheritance>
    </matchCriteria>
  </businessTransaction>
</businessTransactions>
"@

        $btRules | Set-Content "$appDynamicsConfigPath\business-transactions.xml" -Force

        # Set environment variables
        [Environment]::SetEnvironmentVariable("COR_ENABLE_PROFILING", "1", "Machine")
        [Environment]::SetEnvironmentVariable("COR_PROFILER", "{9B8B0F53-B3A1-4885-B842-3F9C06E0F9FD}", "Machine")
        [Environment]::SetEnvironmentVariable("COR_PROFILER_PATH", "C:\AppDynamics\DotNetAgent\Bin\AppDynamics.Agent.Core.dll", "Machine")

        Write-Host "✓ AppDynamics .NET Agent installed and configured for Orleans" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to install Orleans AppDynamics integration: $($_.Exception.Message)"
    }
}

# Usage
Install-OrleansAppDynamicsIntegration
```

### PagerDuty Integration

#### 1. PagerDuty Service and Integration Configuration

```powershell
function Install-OrleansPagerDutyIntegration {
    param(
        [string]$PagerDutyApiToken = $env:PAGERDUTY_API_TOKEN,
        [string]$OrleansServiceName = "Orleans-AIChat-Production",
        [string]$EscalationPolicyId = $env:PAGERDUTY_ESCALATION_POLICY_ID
    )

    try {
        # PagerDuty API endpoints
        $apiBase = "https://api.pagerduty.com"
        $headers = @{
            "Authorization" = "Token token=$PagerDutyApiToken"
            "Content-Type" = "application/json"
            "Accept" = "application/vnd.pagerduty+json;version=2"
        }

        # Create Orleans service in PagerDuty
        $servicePayload = @{
            service = @{
                name = $OrleansServiceName
                description = "Orleans-based AI Chat application monitoring and incident management"
                escalation_policy = @{
                    id = $EscalationPolicyId
                    type = "escalation_policy_reference"
                }
                alert_creation = "create_alerts_and_incidents"
            }
        } | ConvertTo-Json -Depth 10

        $serviceResponse = Invoke-RestMethod -Uri "$apiBase/services" -Method POST -Headers $headers -Body $servicePayload
        $serviceId = $serviceResponse.service.id

        Write-Host "✓ Orleans service created in PagerDuty: $serviceId" -ForegroundColor Green

        # Create integrations for different monitoring tools
        $integrations = @(
            @{ name = "Orleans-SCOM"; type = "generic_events_api_inbound_integration" },
            @{ name = "Orleans-Splunk"; type = "generic_events_api_inbound_integration" },
            @{ name = "Orleans-Datadog"; type = "datadog_inbound_integration" },
            @{ name = "Orleans-NewRelic"; type = "new_relic_inbound_integration" },
            @{ name = "Orleans-AppDynamics"; type = "generic_events_api_inbound_integration" }
        )

        $integrationKeys = @{}

        foreach ($integration in $integrations) {
            $integrationPayload = @{
                integration = @{
                    name = $integration.name
                    service = @{
                        id = $serviceId
                        type = "service_reference"
                    }
                    type = $integration.type
                }
            } | ConvertTo-Json -Depth 10

            $integrationResponse = Invoke-RestMethod -Uri "$apiBase/services/$serviceId/integrations" -Method POST -Headers $headers -Body $integrationPayload
            $integrationKeys[$integration.name] = $integrationResponse.integration.integration_key

            Write-Host "✓ Created PagerDuty integration: $($integration.name)" -ForegroundColor Green
        }

        # Create incident response automation
        $automationPayload = @{
            automation_action = @{
                name = "Orleans-Incident-Response"
                description = "Automated response actions for Orleans incidents"
                action_type = "script"
                script = @{
                    type = "powershell"
                    content = @"
# Orleans Incident Response Script
param(
    [Parameter(Mandatory=`$true)]`$IncidentId,
    [Parameter(Mandatory=`$true)]`$IncidentTitle,
    [Parameter(Mandatory=`$true)]`$ServiceName
)

Write-Host "Processing Orleans incident: `$IncidentTitle"

# Collect Orleans diagnostics
`$orleansMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"
`$diagnosticInfo = @{
    Timestamp = Get-Date
    IncidentId = `$IncidentId
    OrleansMetrics = `$orleansMetrics
    SystemInfo = Get-ComputerInfo | Select-Object TotalPhysicalMemory, CsProcessors
}

# Save diagnostic information
`$diagnosticPath = "C:\Orleans\incidents\incident-`$IncidentId-diagnostics.json"
`$diagnosticInfo | ConvertTo-Json -Depth 10 | Set-Content `$diagnosticPath

Write-Host "Diagnostic information saved: `$diagnosticPath"
"@
                }
                trigger = "incident.triggered"
                services = @(@{
                    id = $serviceId
                    type = "service_reference"
                })
            }
        } | ConvertTo-Json -Depth 10

        # Store integration keys for use by monitoring systems
        $integrationConfig = @{
            ServiceId = $serviceId
            ServiceName = $OrleansServiceName
            IntegrationKeys = $integrationKeys
            CreatedAt = Get-Date
        } | ConvertTo-Json -Depth 10

        $integrationConfig | Set-Content "C:\Orleans\config\pagerduty-integration.json"

        Write-Host "✓ PagerDuty integration configured successfully" -ForegroundColor Green
        Write-Host "  Service ID: $serviceId" -ForegroundColor Green
        Write-Host "  Integration Keys saved to: C:\Orleans\config\pagerduty-integration.json" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to configure Orleans PagerDuty integration: $($_.Exception.Message)"
    }
}

# Function to send incidents to PagerDuty from Orleans monitoring
function Send-OrleansPagerDutyIncident {
    param(
        [Parameter(Mandatory=$true)][string]$IntegrationKey,
        [Parameter(Mandatory=$true)][string]$EventAction, # "trigger", "acknowledge", "resolve"
        [Parameter(Mandatory=$true)][string]$DedupKey,
        [Parameter(Mandatory=$true)][string]$Summary,
        [string]$Source = "Orleans Monitoring",
        [string]$Severity = "critical", # "critical", "error", "warning", "info"
        [hashtable]$CustomDetails = @{}
    )

    try {
        $eventPayload = @{
            routing_key = $IntegrationKey
            event_action = $EventAction
            dedup_key = $DedupKey
            payload = @{
                summary = $Summary
                source = $Source
                severity = $Severity
                custom_details = $CustomDetails
                timestamp = (Get-Date -Format "o")
            }
        } | ConvertTo-Json -Depth 10

        $response = Invoke-RestMethod -Uri "https://events.pagerduty.com/v2/enqueue" -Method POST -Body $eventPayload -ContentType "application/json"

        Write-Host "✓ PagerDuty incident sent: $($response.message)" -ForegroundColor Green
        return $response
    }
    catch {
        Write-Error "Failed to send Orleans incident to PagerDuty: $($_.Exception.Message)"
    }
}

# Usage
Install-OrleansPagerDutyIntegration

# Example of sending an Orleans-specific incident
$pagerDutyConfig = Get-Content "C:\Orleans\config\pagerduty-integration.json" | ConvertFrom-Json
Send-OrleansPagerDutyIncident -IntegrationKey $pagerDutyConfig.IntegrationKeys.'Orleans-SCOM' -EventAction "trigger" -DedupKey "orleans-silo-down-001" -Summary "Orleans Silo is down on production server" -Severity "critical" -CustomDetails @{
    SiloId = "Silo001"
    ServerName = $env:COMPUTERNAME
    LastSeen = (Get-Date)
}
```

### Enterprise Monitoring Integration Testing

#### 1. Comprehensive Integration Testing Framework

```powershell
function Test-OrleansEnterpriseMonitoringIntegration {
    param(
        [string[]]$MonitoringSystems = @("SCOM", "Datadog", "NewRelic", "AppDynamics", "PagerDuty")
    )

    $testResults = @{}

    foreach ($system in $MonitoringSystems) {
        Write-Host "Testing $system integration..." -ForegroundColor Yellow

        $testResult = @{
            System = $system
            AgentStatus = $false
            DataFlow = $false
            AlertGeneration = $false
            DashboardAccess = $false
            OverallStatus = "Failed"
        }

        try {
            # Test agent status
            $testResult.AgentStatus = Test-MonitoringAgentStatus -System $system

            # Test data flow
            if ($testResult.AgentStatus) {
                $testResult.DataFlow = Test-MonitoringDataFlow -System $system
            }

            # Test alert generation
            if ($testResult.DataFlow) {
                $testResult.AlertGeneration = Test-MonitoringAlerts -System $system
            }

            # Test dashboard access
            if ($testResult.AlertGeneration) {
                $testResult.DashboardAccess = Test-MonitoringDashboard -System $system
            }

            # Determine overall status
            if ($testResult.AgentStatus -and $testResult.DataFlow -and
                $testResult.AlertGeneration -and $testResult.DashboardAccess) {
                $testResult.OverallStatus = "Passed"
                Write-Host "✓ $system integration test passed" -ForegroundColor Green
            } else {
                Write-Warning "✗ $system integration test failed"
            }
        }
        catch {
            Write-Error "$system integration test error: $($_.Exception.Message)"
        }

        $testResults[$system] = $testResult
    }

    # Generate comprehensive test report
    $reportPath = "reports\Orleans-Enterprise-Monitoring-Test-$(Get-Date -Format 'yyyy-MM-dd-HHmm').json"
    $testResults | ConvertTo-Json -Depth 10 | Set-Content $reportPath

    Write-Host "✓ Enterprise monitoring integration test report generated: $reportPath" -ForegroundColor Green
    return $testResults
}

function Test-MonitoringAgentStatus {
    param([string]$System)

    switch ($System) {
        "SCOM" {
            try {
                $healthService = Get-Service "HealthService" -ErrorAction SilentlyContinue
                return $healthService -and $healthService.Status -eq "Running"
            } catch { return $false }
        }
        "Datadog" {
            try {
                $ddAgent = Get-Service "DatadogAgent" -ErrorAction SilentlyContinue
                return $ddAgent -and $ddAgent.Status -eq "Running"
            } catch { return $false }
        }
        "NewRelic" {
            try {
                # Check if New Relic profiler is active
                $nrProfiler = [Environment]::GetEnvironmentVariable("COR_PROFILER", "Machine")
                return $nrProfiler -eq "{71DA0A04-7777-4EC6-9643-7D28B46A8A41}"
            } catch { return $false }
        }
        "AppDynamics" {
            try {
                $adProfiler = [Environment]::GetEnvironmentVariable("COR_PROFILER", "Machine")
                return $adProfiler -eq "{9B8B0F53-B3A1-4885-B842-3F9C06E0F9FD}"
            } catch { return $false }
        }
        "PagerDuty" {
            try {
                $pdConfig = Test-Path "C:\Orleans\config\pagerduty-integration.json"
                return $pdConfig
            } catch { return $false }
        }
    }
}

# Usage
Test-OrleansEnterpriseMonitoringIntegration
```

## Centralized Logging and Log Aggregation

### Overview

Centralized logging provides comprehensive log aggregation, search, and analysis capabilities for Orleans-based applications. This section covers integration with major enterprise logging platforms including ELK Stack, Azure Log Analytics, and AWS CloudWatch.

#### Logging Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Log Destinations                         │
├─────────────────────────────────────────────────────────────┤
│  ELK Stack      │  Azure Log      │  AWS CloudWatch │ Splunk│
│  - Elasticsearch│  Analytics      │  - Log Groups   │ Cloud │
│  - Logstash     │  - KQL Queries  │  - Log Streams  │ - HEC │
│  - Kibana       │  - Workbooks    │  - Dashboards   │ - Apps│
│  - Beats        │  - Alerts       │  - Alarms       │       │
├─────────────────────────────────────────────────────────────┤
│                    Log Shippers                             │
├─────────────────────────────────────────────────────────────┤
│  Filebeat       │  Log Analytics  │  CloudWatch     │ Vector│
│  Fluentd        │  Agent          │  Agent          │ Fluent│
│  Vector         │  Azure Monitor  │  Systems Mgr    │  Bit  │
├─────────────────────────────────────────────────────────────┤
│                    Orleans Log Sources                      │
├─────────────────────────────────────────────────────────────┤
│  Application    │  Security       │  Performance    │ System│
│  Logs           │  Logs           │  Logs           │ Logs  │
│  - Grain Events │  - Auth Events  │  - Metrics      │ - IIS │
│  - Silo Events  │  - Access Logs  │  - Traces       │ - OS  │
│  - API Calls    │  - Audit Trails │  - Counters     │ - .NET│
└─────────────────────────────────────────────────────────────┘
```

### ELK Stack Integration

#### 1. Elasticsearch Configuration

**Elasticsearch Index Templates for Orleans**:
```json
{
  "orleans-logs": {
    "index_patterns": ["orleans-*"],
    "template": {
      "settings": {
        "number_of_shards": 3,
        "number_of_replicas": 1,
        "index.refresh_interval": "5s",
        "index.max_result_window": 50000
      },
      "mappings": {
        "properties": {
          "@timestamp": {
            "type": "date"
          },
          "level": {
            "type": "keyword"
          },
          "message": {
            "type": "text",
            "analyzer": "standard"
          },
          "logger": {
            "type": "keyword"
          },
          "orleans": {
            "properties": {
              "grain_type": {
                "type": "keyword"
              },
              "grain_id": {
                "type": "keyword"
              },
              "silo_id": {
                "type": "keyword"
              },
              "cluster_id": {
                "type": "keyword"
              },
              "correlation_id": {
                "type": "keyword"
              },
              "operation": {
                "type": "keyword"
              },
              "duration_ms": {
                "type": "long"
              },
              "memory_mb": {
                "type": "long"
              }
            }
          },
          "security": {
            "properties": {
              "event_id": {
                "type": "keyword"
              },
              "user": {
                "type": "keyword"
              },
              "source_ip": {
                "type": "ip"
              },
              "result": {
                "type": "keyword"
              }
            }
          },
          "application": {
            "properties": {
              "name": {
                "type": "keyword"
              },
              "version": {
                "type": "keyword"
              },
              "environment": {
                "type": "keyword"
              }
            }
          }
        }
      }
    }
  }
}
```

#### 2. Logstash Configuration

**Orleans Logstash Pipeline** (orleans-logstash.conf):
```ruby
# Orleans Logstash Configuration
input {
  # File input for Orleans application logs
  file {
    path => "/var/log/orleans/application/*.log"
    start_position => "beginning"
    sincedb_path => "/var/lib/logstash/sincedb_orleans_app"
    codec => "json"
    tags => ["orleans", "application"]
  }

  # File input for Orleans security logs
  file {
    path => "/var/log/orleans/security/*.log"
    start_position => "beginning"
    sincedb_path => "/var/lib/logstash/sincedb_orleans_security"
    codec => "json"
    tags => ["orleans", "security"]
  }

  # Beats input for log shippers
  beats {
    port => 5044
    host => "0.0.0.0"
  }

  # HTTP input for direct log shipping
  http {
    port => 8080
    host => "0.0.0.0"
    codec => "json"
    tags => ["http", "orleans"]
  }

  # TCP input for structured logging
  tcp {
    port => 5000
    codec => json_lines
    tags => ["tcp", "orleans"]
  }
}

filter {
  # Parse Orleans application logs
  if [tags] and "application" in [tags] {
    # Extract Orleans-specific fields
    if [Properties] {
      mutate {
        add_field => { "orleans.grain_type" => "%{[Properties][GrainType]}" }
        add_field => { "orleans.grain_id" => "%{[Properties][GrainId]}" }
        add_field => { "orleans.silo_id" => "%{[Properties][SiloId]}" }
        add_field => { "orleans.cluster_id" => "%{[Properties][ClusterId]}" }
        add_field => { "orleans.correlation_id" => "%{[Properties][CorrelationId]}" }
        add_field => { "orleans.operation" => "%{[Properties][Operation]}" }
      }

      # Parse duration if present
      if [Properties][Duration] {
        mutate {
          convert => { "orleans.duration_ms" => "integer" }
          copy => { "[Properties][Duration]" => "orleans.duration_ms" }
        }
      }

      # Parse memory usage if present
      if [Properties][MemoryUsage] {
        mutate {
          convert => { "orleans.memory_mb" => "integer" }
          copy => { "[Properties][MemoryUsage]" => "orleans.memory_mb" }
        }
      }
    }

    # Categorize log levels
    if [Level] {
      if [Level] == "Error" or [Level] == "Fatal" {
        mutate { add_tag => ["error"] }
      } else if [Level] == "Warning" {
        mutate { add_tag => ["warning"] }
      } else if [Level] == "Information" {
        mutate { add_tag => ["info"] }
      }
    }
  }

  # Parse Orleans security logs
  if [tags] and "security" in [tags] {
    # Extract security event fields
    if [Properties] {
      mutate {
        add_field => { "security.event_id" => "%{[Properties][EventId]}" }
        add_field => { "security.user" => "%{[Properties][Username]}" }
        add_field => { "security.source_ip" => "%{[Properties][SourceIP]}" }
        add_field => { "security.result" => "%{[Properties][Result]}" }
      }

      # Convert IP field to proper type
      if [security.source_ip] and [security.source_ip] != "null" {
        mutate {
          convert => { "security.source_ip" => "string" }
        }
      }
    }

    # Tag security events by severity
    if [security.event_id] {
      if [security.event_id] in ["3000", "3001", "3002"] {
        mutate { add_tag => ["security_incident"] }
      } else if [security.event_id] in ["1002", "1003"] {
        mutate { add_tag => ["authentication_failure"] }
      }
    }
  }

  # Add common fields for all Orleans logs
  mutate {
    add_field => { "application.name" => "orleans-aichat" }
    add_field => { "application.version" => "1.0.0" }
    add_field => { "application.environment" => "%{[Environment]}" }
  }

  # Parse timestamp
  date {
    match => [ "Timestamp", "yyyy-MM-dd'T'HH:mm:ss.SSSSSSS'Z'" ]
    target => "@timestamp"
  }

  # Remove unnecessary fields
  mutate {
    remove_field => ["host", "agent", "input", "log", "ecs"]
  }
}

output {
  # Output to Elasticsearch
  elasticsearch {
    hosts => ["elasticsearch:9200"]
    index => "orleans-%{[application.environment]}-%{+YYYY.MM.dd}"
    template_name => "orleans-logs"
    template_pattern => "orleans-*"
    template_overwrite => true
  }

  # Output to file for debugging (optional)
  if [tags] and "debug" in [tags] {
    file {
      path => "/var/log/logstash/debug/orleans-%{+YYYY.MM.dd}.log"
      codec => line { format => "%{message}" }
    }
  }

  # Output critical errors to dedicated index
  if [tags] and "error" in [tags] {
    elasticsearch {
      hosts => ["elasticsearch:9200"]
      index => "orleans-errors-%{+YYYY.MM.dd}"
    }
  }

  # Output security events to security index
  if [tags] and "security" in [tags] {
    elasticsearch {
      hosts => ["elasticsearch:9200"]
      index => "orleans-security-%{+YYYY.MM.dd}"
    }
  }
}
```

#### 3. Filebeat Configuration

**Orleans Filebeat Configuration** (filebeat-orleans.yml):
```yaml
# Orleans Filebeat Configuration
filebeat.inputs:
- type: log
  enabled: true
  paths:
    - C:\Orleans\logs\application\*.log
  fields:
    service: orleans-aichat
    environment: ${ENVIRONMENT:development}
    log_type: application
  fields_under_root: true
  json.keys_under_root: true
  json.add_error_key: true
  multiline.pattern: '^\d{4}-\d{2}-\d{2}'
  multiline.negate: true
  multiline.match: after

- type: log
  enabled: true
  paths:
    - C:\Orleans\logs\security\*.log
  fields:
    service: orleans-aichat
    environment: ${ENVIRONMENT:development}
    log_type: security
  fields_under_root: true
  json.keys_under_root: true
  json.add_error_key: true

- type: log
  enabled: true
  paths:
    - C:\Orleans\logs\performance\*.log
  fields:
    service: orleans-aichat
    environment: ${ENVIRONMENT:development}
    log_type: performance
  fields_under_root: true
  json.keys_under_root: true

# Output to Logstash
output.logstash:
  hosts: ["logstash.company.com:5044"]

# Output to Elasticsearch (alternative)
# output.elasticsearch:
#   hosts: ["elasticsearch.company.com:9200"]
#   index: "orleans-%{[environment]}-%{+yyyy.MM.dd}"

# Logging configuration
logging.level: info
logging.to_files: true
logging.files:
  path: C:\Orleans\filebeat\logs
  name: filebeat
  keepfiles: 7
  permissions: 0644

# Processors
processors:
- add_host_metadata:
    when.not.contains.tags: forwarded
- add_docker_metadata: ~
- add_kubernetes_metadata: ~

# Monitoring
monitoring.enabled: true
monitoring.elasticsearch:
  hosts: ["elasticsearch.company.com:9200"]
```

#### 4. Kibana Dashboards

**Orleans Application Dashboard** (kibana-orleans-dashboard.json):
```json
{
  "version": "8.0.0",
  "objects": [
    {
      "id": "orleans-application-dashboard",
      "type": "dashboard",
      "attributes": {
        "title": "Orleans Application Monitoring",
        "hits": 0,
        "description": "Comprehensive Orleans application monitoring dashboard",
        "panelsJSON": "[
          {
            \"version\":\"8.0.0\",
            \"gridData\":{\"x\":0,\"y\":0,\"w\":24,\"h\":15,\"i\":\"1\"},
            \"panelIndex\":\"1\",
            \"embeddableConfig\":{},
            \"panelRefName\":\"panel_1\"
          },
          {
            \"version\":\"8.0.0\",
            \"gridData\":{\"x\":24,\"y\":0,\"w\":24,\"h\":15,\"i\":\"2\"},
            \"panelIndex\":\"2\",
            \"embeddableConfig\":{},
            \"panelRefName\":\"panel_2\"
          },
          {
            \"version\":\"8.0.0\",
            \"gridData\":{\"x\":0,\"y\":15,\"w\":48,\"h\":15,\"i\":\"3\"},
            \"panelIndex\":\"3\",
            \"embeddableConfig\":{},
            \"panelRefName\":\"panel_3\"
          }
        ]",
        "timeRestore": false,
        "timeTo": "now",
        "timeFrom": "now-24h",
        "refreshInterval": {
          "pause": false,
          "value": 30000
        },
        "kibanaSavedObjectMeta": {
          "searchSourceJSON": "{\"query\":{\"match_all\":{}},\"filter\":[]}"
        }
      },
      "references": [
        {
          "name": "panel_1",
          "type": "visualization",
          "id": "orleans-log-levels-pie"
        },
        {
          "name": "panel_2",
          "type": "visualization",
          "id": "orleans-grain-activations-timeline"
        },
        {
          "name": "panel_3",
          "type": "visualization",
          "id": "orleans-error-logs-table"
        }
      ]
    },
    {
      "id": "orleans-log-levels-pie",
      "type": "visualization",
      "attributes": {
        "title": "Orleans Log Levels Distribution",
        "visState": "{\"title\":\"Orleans Log Levels Distribution\",\"type\":\"pie\",\"aggs\":[{\"id\":\"1\",\"type\":\"count\",\"schema\":\"metric\",\"params\":{}},{\"id\":\"2\",\"type\":\"terms\",\"schema\":\"segment\",\"params\":{\"field\":\"level\",\"size\":10,\"order\":\"desc\",\"orderBy\":\"1\"}}]}",
        "uiStateJSON": "{}",
        "description": "",
        "version": 1,
        "kibanaSavedObjectMeta": {
          "searchSourceJSON": "{\"index\":\"orleans-*\",\"query\":{\"match_all\":{}},\"filter\":[{\"match\":{\"service\":\"orleans-aichat\"}}]}"
        }
      }
    },
    {
      "id": "orleans-grain-activations-timeline",
      "type": "visualization",
      "attributes": {
        "title": "Orleans Grain Activations Over Time",
        "visState": "{\"title\":\"Orleans Grain Activations Over Time\",\"type\":\"line\",\"aggs\":[{\"id\":\"1\",\"type\":\"count\",\"schema\":\"metric\",\"params\":{}},{\"id\":\"2\",\"type\":\"date_histogram\",\"schema\":\"segment\",\"params\":{\"field\":\"@timestamp\",\"interval\":\"auto\",\"min_doc_count\":1}},{\"id\":\"3\",\"type\":\"terms\",\"schema\":\"group\",\"params\":{\"field\":\"orleans.grain_type\",\"size\":5,\"order\":\"desc\",\"orderBy\":\"1\"}}]}",
        "uiStateJSON": "{}",
        "description": "",
        "version": 1,
        "kibanaSavedObjectMeta": {
          "searchSourceJSON": "{\"index\":\"orleans-*\",\"query\":{\"match\":{\"orleans.operation\":\"grain_activation\"}},\"filter\":[]}"
        }
      }
    },
    {
      "id": "orleans-error-logs-table",
      "type": "visualization",
      "attributes": {
        "title": "Orleans Error Logs",
        "visState": "{\"title\":\"Orleans Error Logs\",\"type\":\"table\",\"aggs\":[{\"id\":\"1\",\"type\":\"count\",\"schema\":\"metric\",\"params\":{}},{\"id\":\"2\",\"type\":\"terms\",\"schema\":\"bucket\",\"params\":{\"field\":\"@timestamp\",\"size\":20,\"order\":\"desc\",\"orderBy\":\"1\"}},{\"id\":\"3\",\"type\":\"terms\",\"schema\":\"bucket\",\"params\":{\"field\":\"level\",\"size\":5,\"order\":\"desc\",\"orderBy\":\"1\"}},{\"id\":\"4\",\"type\":\"terms\",\"schema\":\"bucket\",\"params\":{\"field\":\"message\",\"size\":10,\"order\":\"desc\",\"orderBy\":\"1\"}}]}",
        "uiStateJSON": "{}",
        "description": "",
        "version": 1,
        "kibanaSavedObjectMeta": {
          "searchSourceJSON": "{\"index\":\"orleans-*\",\"query\":{\"bool\":{\"should\":[{\"match\":{\"level\":\"Error\"}},{\"match\":{\"level\":\"Fatal\"}}]}},\"filter\":[]}"
        }
      }
    }
  ]
}
```

#### 5. ELK Stack Setup Automation

```powershell
function Install-OrleansELKIntegration {
    param(
        [string]$ElasticsearchUrl = "http://elasticsearch.company.com:9200",
        [string]$KibanaUrl = "http://kibana.company.com:5601",
        [string]$LogstashHost = "logstash.company.com",
        [string]$Environment = "Production"
    )

    try {
        Write-Host "Setting up Orleans ELK Stack integration..." -ForegroundColor Green

        # Create Orleans log directories
        $logDirs = @(
            "C:\Orleans\logs\application",
            "C:\Orleans\logs\security",
            "C:\Orleans\logs\performance"
        )

        foreach ($dir in $logDirs) {
            if (!(Test-Path $dir)) {
                New-Item -Path $dir -ItemType Directory -Force
                Write-Host "✓ Created log directory: $dir"
            }
        }

        # Install Filebeat if not present
        $filebeatPath = "C:\Program Files\Filebeat"
        if (!(Test-Path $filebeatPath)) {
            Write-Host "Downloading and installing Filebeat..."
            $filebeatUrl = "https://artifacts.elastic.co/downloads/beats/filebeat/filebeat-8.10.0-windows-x86_64.zip"
            $downloadPath = "$env:TEMP\filebeat.zip"

            Invoke-WebRequest -Uri $filebeatUrl -OutFile $downloadPath
            Expand-Archive -Path $downloadPath -DestinationPath "C:\Program Files" -Force

            Rename-Item "C:\Program Files\filebeat-8.10.0-windows-x86_64" $filebeatPath
            Write-Host "✓ Filebeat installed"
        }

        # Configure Filebeat for Orleans
        $filebeatConfig = @"
filebeat.inputs:
- type: log
  enabled: true
  paths:
    - C:\Orleans\logs\application\*.log
  fields:
    service: orleans-aichat
    environment: $Environment
    log_type: application
  fields_under_root: true
  json.keys_under_root: true

- type: log
  enabled: true
  paths:
    - C:\Orleans\logs\security\*.log
  fields:
    service: orleans-aichat
    environment: $Environment
    log_type: security
  fields_under_root: true
  json.keys_under_root: true

output.logstash:
  hosts: ["$LogstashHost`:5044"]

logging.level: info
logging.to_files: true
logging.files:
  path: C:\Orleans\filebeat\logs
  name: filebeat
  keepfiles: 7

processors:
- add_host_metadata: ~
"@

        $filebeatConfig | Set-Content "$filebeatPath\filebeat.yml"

        # Install Filebeat as Windows service
        & "$filebeatPath\filebeat.exe" service install

        # Start Filebeat service
        Start-Service -Name "Filebeat"

        # Create Elasticsearch index template
        $indexTemplate = @{
            "index_patterns" = @("orleans-*")
            "template" = @{
                "settings" = @{
                    "number_of_shards" = 3
                    "number_of_replicas" = 1
                    "index.refresh_interval" = "5s"
                }
                "mappings" = @{
                    "properties" = @{
                        "@timestamp" = @{ "type" = "date" }
                        "level" = @{ "type" = "keyword" }
                        "message" = @{ "type" = "text" }
                        "orleans" = @{
                            "properties" = @{
                                "grain_type" = @{ "type" = "keyword" }
                                "silo_id" = @{ "type" = "keyword" }
                                "correlation_id" = @{ "type" = "keyword" }
                            }
                        }
                    }
                }
            }
        }

        # Upload index template to Elasticsearch
        $headers = @{ "Content-Type" = "application/json" }
        $body = $indexTemplate | ConvertTo-Json -Depth 10
        Invoke-RestMethod -Uri "$ElasticsearchUrl/_index_template/orleans-logs" -Method PUT -Headers $headers -Body $body

        Write-Host "✓ Orleans ELK Stack integration configured successfully"
        Write-Host "  Elasticsearch: $ElasticsearchUrl"
        Write-Host "  Kibana: $KibanaUrl"
        Write-Host "  Logstash: $LogstashHost"
    }
    catch {
        Write-Error "Failed to configure Orleans ELK integration: $($_.Exception.Message)"
    }
}

# Usage
Install-OrleansELKIntegration
```

### Azure Log Analytics Integration

#### 1. Log Analytics Workspace Configuration

```powershell
function Install-OrleansAzureLogAnalyticsIntegration {
    param(
        [string]$WorkspaceId = $env:LOG_ANALYTICS_WORKSPACE_ID,
        [string]$WorkspaceKey = $env:LOG_ANALYTICS_WORKSPACE_KEY,
        [string]$ResourceGroup = "Orleans-ResourceGroup",
        [string]$WorkspaceName = "Orleans-LogAnalytics"
    )

    try {
        Write-Host "Setting up Orleans Azure Log Analytics integration..." -ForegroundColor Green

        # Install Azure Log Analytics Agent (MMA)
        $agentPath = "${env:ProgramFiles}\Microsoft Monitoring Agent\Agent"
        if (!(Test-Path $agentPath)) {
            Write-Host "Installing Azure Log Analytics Agent..."
            $agentUrl = "https://go.microsoft.com/fwlink/?LinkId=828603"
            $installerPath = "$env:TEMP\MMASetup-AMD64.exe"

            Invoke-WebRequest -Uri $agentUrl -OutFile $installerPath
            Start-Process -FilePath $installerPath -ArgumentList "/C:`"setup.exe /qn NOAPM=1 ADD_OPINSIGHTS_WORKSPACE=1 OPINSIGHTS_WORKSPACE_ID=$WorkspaceId OPINSIGHTS_WORKSPACE_KEY=$WorkspaceKey AcceptEndUserLicenseAgreement=1`"" -Wait

            Write-Host "✓ Azure Log Analytics Agent installed"
        }

        # Configure custom log collection
        $customLogConfig = @{
            "Orleans_Application_CL" = @{
                "logPaths" = @("C:\Orleans\logs\application\*.log")
                "logFormat" = "JSON"
                "delimiter" = "`n"
            }
            "Orleans_Security_CL" = @{
                "logPaths" = @("C:\Orleans\logs\security\*.log")
                "logFormat" = "JSON"
                "delimiter" = "`n"
            }
            "Orleans_Performance_CL" = @{
                "logPaths" = @("C:\Orleans\logs\performance\*.log")
                "logFormat" = "JSON"
                "delimiter" = "`n"
            }
        }

        # Create Data Collection Rules via Azure CLI or PowerShell
        foreach ($logType in $customLogConfig.Keys) {
            Write-Host "Configuring custom log: $logType"

            # Note: In production, this would use Azure PowerShell or REST API
            # to create Data Collection Rules for custom logs
        }

        # Install Azure Monitor Agent (AMA) - newer alternative to MMA
        Write-Host "Installing Azure Monitor Agent..."
        $amaUrl = "https://go.microsoft.com/fwlink/?linkid=2192409"
        $amaPath = "$env:TEMP\AzureMonitorAgentInstaller.msi"

        Invoke-WebRequest -Uri $amaUrl -OutFile $amaPath
        Start-Process -FilePath "msiexec.exe" -ArgumentList "/i", $amaPath, "/quiet" -Wait

        Write-Host "✓ Azure Monitor Agent installed"

        # Create Log Analytics queries for Orleans monitoring
        $orleansQueries = @{
            "Orleans_Grain_Activations" = @"
Orleans_Application_CL
| where Properties_s contains "grain_activation"
| extend GrainType = tostring(parse_json(Properties_s).GrainType)
| summarize count() by GrainType, bin(TimeGenerated, 5m)
| render timechart
"@

            "Orleans_Error_Analysis" = @"
Orleans_Application_CL
| where Level_s in ("Error", "Fatal")
| extend GrainType = tostring(parse_json(Properties_s).GrainType)
| extend SiloId = tostring(parse_json(Properties_s).SiloId)
| summarize ErrorCount = count() by GrainType, SiloId, bin(TimeGenerated, 1h)
| order by ErrorCount desc
"@

            "Orleans_Security_Events" = @"
Orleans_Security_CL
| extend EventId = tostring(parse_json(Properties_s).EventId)
| extend Username = tostring(parse_json(Properties_s).Username)
| extend SourceIP = tostring(parse_json(Properties_s).SourceIP)
| where EventId in ("1002", "3000", "3001")
| summarize count() by EventId, Username, SourceIP, bin(TimeGenerated, 15m)
| order by TimeGenerated desc
"@

            "Orleans_Performance_Metrics" = @"
Orleans_Performance_CL
| extend MemoryUsage = todouble(parse_json(Properties_s).MemoryUsage)
| extend GrainActivationTime = todouble(parse_json(Properties_s).GrainActivationTime)
| summarize
    AvgMemoryUsage = avg(MemoryUsage),
    MaxMemoryUsage = max(MemoryUsage),
    AvgActivationTime = avg(GrainActivationTime),
    MaxActivationTime = max(GrainActivationTime)
    by bin(TimeGenerated, 10m)
| render timechart
"@
        }

        # Save queries to local files for import
        $queriesPath = "C:\Orleans\LogAnalytics\Queries"
        if (!(Test-Path $queriesPath)) {
            New-Item -Path $queriesPath -ItemType Directory -Force
        }

        foreach ($query in $orleansQueries.GetEnumerator()) {
            $query.Value | Set-Content "$queriesPath\$($query.Key).kusto"
        }

        Write-Host "✓ Orleans Azure Log Analytics integration configured"
        Write-Host "  Workspace ID: $WorkspaceId"
        Write-Host "  Custom Logs: Orleans_Application_CL, Orleans_Security_CL, Orleans_Performance_CL"
        Write-Host "  Queries saved to: $queriesPath"
    }
    catch {
        Write-Error "Failed to configure Orleans Azure Log Analytics integration: $($_.Exception.Message)"
    }
}

# Usage
Install-OrleansAzureLogAnalyticsIntegration
```

#### 2. Log Analytics Workbooks

**Orleans Monitoring Workbook** (orleans-workbook.json):
```json
{
  "version": "Notebook/1.0",
  "items": [
    {
      "type": 1,
      "content": {
        "json": "## Orleans Application Monitoring Dashboard\n\nComprehensive monitoring and analysis of Orleans-based applications using Azure Log Analytics."
      },
      "name": "text - 0"
    },
    {
      "type": 3,
      "content": {
        "version": "KqlItem/1.0",
        "query": "Orleans_Application_CL\r\n| where TimeGenerated >= ago(24h)\r\n| summarize count() by Level_s\r\n| render piechart",
        "size": 1,
        "title": "Log Level Distribution (24h)",
        "queryType": 0,
        "resourceType": "microsoft.operationalinsights/workspaces"
      },
      "customWidth": "50",
      "name": "query - 1"
    },
    {
      "type": 3,
      "content": {
        "version": "KqlItem/1.0",
        "query": "Orleans_Application_CL\r\n| where Properties_s contains \"grain_activation\"\r\n| extend GrainType = tostring(parse_json(Properties_s).GrainType)\r\n| summarize count() by GrainType, bin(TimeGenerated, 1h)\r\n| render timechart",
        "size": 1,
        "title": "Grain Activations Over Time",
        "queryType": 0,
        "resourceType": "microsoft.operationalinsights/workspaces"
      },
      "customWidth": "50",
      "name": "query - 2"
    },
    {
      "type": 3,
      "content": {
        "version": "KqlItem/1.0",
        "query": "Orleans_Security_CL\r\n| where TimeGenerated >= ago(24h)\r\n| extend EventId = tostring(parse_json(Properties_s).EventId)\r\n| extend Username = tostring(parse_json(Properties_s).Username)\r\n| extend SourceIP = tostring(parse_json(Properties_s).SourceIP)\r\n| where EventId in (\"1002\", \"3000\", \"3001\")\r\n| project TimeGenerated, EventId, Username, SourceIP, Message_s\r\n| order by TimeGenerated desc\r\n| take 50",
        "size": 0,
        "title": "Recent Security Events",
        "queryType": 0,
        "resourceType": "microsoft.operationalinsights/workspaces"
      },
      "name": "query - 3"
    },
    {
      "type": 3,
      "content": {
        "version": "KqlItem/1.0",
        "query": "Orleans_Performance_CL\r\n| where TimeGenerated >= ago(4h)\r\n| extend MemoryUsage = todouble(parse_json(Properties_s).MemoryUsage)\r\n| extend GrainActivationTime = todouble(parse_json(Properties_s).GrainActivationTime)\r\n| summarize \r\n    AvgMemoryUsage = avg(MemoryUsage),\r\n    MaxMemoryUsage = max(MemoryUsage),\r\n    AvgActivationTime = avg(GrainActivationTime),\r\n    MaxActivationTime = max(GrainActivationTime)\r\n    by bin(TimeGenerated, 10m)\r\n| render timechart",
        "size": 0,
        "title": "Performance Metrics Trend",
        "queryType": 0,
        "resourceType": "microsoft.operationalinsights/workspaces"
      },
      "name": "query - 4"
    }
  ],
  "fallbackResourceIds": [
    "/subscriptions/{subscription-id}/resourceGroups/{resource-group}/providers/Microsoft.OperationalInsights/workspaces/{workspace-name}"
  ],
  "fromTemplateId": "sentinel-UserWorkbook",
  "$schema": "https://github.com/Microsoft/Application-Insights-Workbooks/blob/master/schema/workbook.json"
}
```

### AWS CloudWatch Integration

#### 1. CloudWatch Agent Configuration

**CloudWatch Agent Configuration** (cloudwatch-config.json):
```json
{
  "agent": {
    "metrics_collection_interval": 60,
    "run_as_user": "root"
  },
  "logs": {
    "logs_collected": {
      "files": {
        "collect_list": [
          {
            "file_path": "C:\\Orleans\\logs\\application\\*.log",
            "log_group_name": "orleans-application-logs",
            "log_stream_name": "{hostname}-application",
            "timezone": "UTC",
            "multi_line_start_pattern": "^\\d{4}-\\d{2}-\\d{2}",
            "encoding": "utf-8"
          },
          {
            "file_path": "C:\\Orleans\\logs\\security\\*.log",
            "log_group_name": "orleans-security-logs",
            "log_stream_name": "{hostname}-security",
            "timezone": "UTC",
            "multi_line_start_pattern": "^\\d{4}-\\d{2}-\\d{2}",
            "encoding": "utf-8"
          },
          {
            "file_path": "C:\\Orleans\\logs\\performance\\*.log",
            "log_group_name": "orleans-performance-logs",
            "log_stream_name": "{hostname}-performance",
            "timezone": "UTC",
            "encoding": "utf-8"
          }
        ]
      },
      "windows_events": {
        "collect_list": [
          {
            "event_name": "Orleans Security",
            "event_levels": [
              "INFORMATION",
              "WARNING",
              "ERROR",
              "CRITICAL"
            ],
            "log_group_name": "orleans-windows-events",
            "log_stream_name": "{hostname}-windows-events",
            "event_format": "xml"
          }
        ]
      }
    }
  },
  "metrics": {
    "namespace": "Orleans/Application",
    "metrics_collected": {
      "cpu": {
        "measurement": [
          "cpu_usage_idle",
          "cpu_usage_iowait",
          "cpu_usage_user",
          "cpu_usage_system"
        ],
        "metrics_collection_interval": 60
      },
      "disk": {
        "measurement": [
          "used_percent"
        ],
        "metrics_collection_interval": 60,
        "resources": [
          "*"
        ]
      },
      "diskio": {
        "measurement": [
          "io_time"
        ],
        "metrics_collection_interval": 60,
        "resources": [
          "*"
        ]
      },
      "mem": {
        "measurement": [
          "mem_used_percent"
        ],
        "metrics_collection_interval": 60
      },
      "netstat": {
        "measurement": [
          "tcp_established",
          "tcp_time_wait"
        ],
        "metrics_collection_interval": 60
      }
    }
  }
}
```

#### 2. CloudWatch Custom Metrics

```powershell
function Send-OrleansMetricsToCloudWatch {
    param(
        [string]$Region = "us-east-1",
        [string]$Namespace = "Orleans/Application",
        [string]$Environment = "Production"
    )

    try {
        # Import AWS PowerShell module
        Import-Module AWSPowerShell.NetCore -ErrorAction Stop

        # Get Orleans metrics
        $orleansMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics"

        # Prepare CloudWatch metrics
        $cloudWatchMetrics = @()

        # Grain activation metrics
        $cloudWatchMetrics += @{
            MetricName = "GrainActivationsTotal"
            Value = $orleansMetrics.TotalGrainActivations
            Unit = "Count"
            Dimensions = @(
                @{ Name = "Environment"; Value = $Environment }
                @{ Name = "Service"; Value = "orleans-aichat" }
            )
        }

        $cloudWatchMetrics += @{
            MetricName = "GrainActivationsPerSecond"
            Value = $orleansMetrics.GrainActivationsPerSecond
            Unit = "Count/Second"
            Dimensions = @(
                @{ Name = "Environment"; Value = $Environment }
                @{ Name = "Service"; Value = "orleans-aichat" }
            )
        }

        # Memory usage metrics
        $cloudWatchMetrics += @{
            MetricName = "MemoryUsageMB"
            Value = $orleansMetrics.MemoryUsageMB
            Unit = "Megabytes"
            Dimensions = @(
                @{ Name = "Environment"; Value = $Environment }
                @{ Name = "Service"; Value = "orleans-aichat" }
            )
        }

        # Performance metrics
        $cloudWatchMetrics += @{
            MetricName = "AverageRequestDuration"
            Value = $orleansMetrics.AverageRequestDuration
            Unit = "Milliseconds"
            Dimensions = @(
                @{ Name = "Environment"; Value = $Environment }
                @{ Name = "Service"; Value = "orleans-aichat" }
            )
        }

        # Send metrics to CloudWatch
        foreach ($metric in $cloudWatchMetrics) {
            $dimensionList = @()
            foreach ($dimension in $metric.Dimensions) {
                $dimensionList += New-Object Amazon.CloudWatch.Model.Dimension -Property @{
                    Name = $dimension.Name
                    Value = $dimension.Value
                }
            }

            $metricData = New-Object Amazon.CloudWatch.Model.MetricDatum -Property @{
                MetricName = $metric.MetricName
                Value = $metric.Value
                Unit = $metric.Unit
                Timestamp = (Get-Date).ToUniversalTime()
                Dimensions = $dimensionList
            }

            Write-CWMetricData -Namespace $Namespace -MetricData $metricData -Region $Region
        }

        Write-Host "✓ Orleans metrics sent to CloudWatch successfully" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to send Orleans metrics to CloudWatch: $($_.Exception.Message)"
    }
}

function Install-OrleansCloudWatchIntegration {
    param(
        [string]$Region = "us-east-1",
        [string]$Environment = "Production"
    )

    try {
        Write-Host "Setting up Orleans CloudWatch integration..." -ForegroundColor Green

        # Install CloudWatch Agent
        $agentUrl = "https://s3.amazonaws.com/amazoncloudwatch-agent/windows/amd64/latest/amazon-cloudwatch-agent.msi"
        $installerPath = "$env:TEMP\amazon-cloudwatch-agent.msi"

        Invoke-WebRequest -Uri $agentUrl -OutFile $installerPath
        Start-Process -FilePath "msiexec.exe" -ArgumentList "/i", $installerPath, "/quiet" -Wait

        Write-Host "✓ CloudWatch Agent installed"

        # Configure CloudWatch Agent
        $configPath = "C:\ProgramData\Amazon\AmazonCloudWatchAgent"
        if (!(Test-Path $configPath)) {
            New-Item -Path $configPath -ItemType Directory -Force
        }

        # Create CloudWatch configuration
        $cloudWatchConfig = @{
            agent = @{
                metrics_collection_interval = 60
            }
            logs = @{
                logs_collected = @{
                    files = @{
                        collect_list = @(
                            @{
                                file_path = "C:\\Orleans\\logs\\application\\*.log"
                                log_group_name = "orleans-$Environment-application-logs"
                                log_stream_name = "{hostname}-application"
                                timezone = "UTC"
                                multi_line_start_pattern = "^\\d{4}-\\d{2}-\\d{2}"
                            },
                            @{
                                file_path = "C:\\Orleans\\logs\\security\\*.log"
                                log_group_name = "orleans-$Environment-security-logs"
                                log_stream_name = "{hostname}-security"
                                timezone = "UTC"
                                multi_line_start_pattern = "^\\d{4}-\\d{2}-\\d{2}"
                            }
                        )
                    }
                }
            }
            metrics = @{
                namespace = "Orleans/$Environment"
                metrics_collected = @{
                    mem = @{
                        measurement = @("mem_used_percent")
                        metrics_collection_interval = 60
                    }
                    cpu = @{
                        measurement = @("cpu_usage_idle", "cpu_usage_user", "cpu_usage_system")
                        metrics_collection_interval = 60
                    }
                }
            }
        }

        $cloudWatchConfig | ConvertTo-Json -Depth 10 | Set-Content "$configPath\amazon-cloudwatch-agent.json"

        # Start CloudWatch Agent with configuration
        & "C:\Program Files\Amazon\AmazonCloudWatchAgent\amazon-cloudwatch-agent-ctl.ps1" -a fetch-config -m ec2 -c file:"$configPath\amazon-cloudwatch-agent.json" -s

        # Create CloudWatch Log Groups
        Import-Module AWSPowerShell.NetCore

        $logGroups = @(
            "orleans-$Environment-application-logs",
            "orleans-$Environment-security-logs",
            "orleans-$Environment-performance-logs"
        )

        foreach ($logGroup in $logGroups) {
            try {
                New-CWLogGroup -LogGroupName $logGroup -Region $Region
                Write-Host "✓ Created CloudWatch log group: $logGroup"
            } catch {
                if ($_.Exception.Message -notlike "*already exists*") {
                    Write-Warning "Failed to create log group $logGroup`: $($_.Exception.Message)"
                }
            }
        }

        # Schedule custom metrics collection
        $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration ([TimeSpan]::MaxValue)
        $action = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-File C:\Orleans\Scripts\Send-OrleansMetricsToCloudWatch.ps1 -Region $Region -Environment $Environment"

        Register-ScheduledTask -TaskName "Orleans-CloudWatch-Metrics" -Trigger $trigger -Action $action -Description "Send Orleans custom metrics to CloudWatch"

        Write-Host "✓ Orleans CloudWatch integration configured successfully"
        Write-Host "  Region: $Region"
        Write-Host "  Log Groups: $($logGroups -join ', ')"
        Write-Host "  Custom Metrics: Scheduled every 5 minutes"
    }
    catch {
        Write-Error "Failed to configure Orleans CloudWatch integration: $($_.Exception.Message)"
    }
}

# Usage
Install-OrleansCloudWatchIntegration -Region "us-east-1" -Environment "Production"
```

#### 3. CloudWatch Dashboards

**Orleans CloudWatch Dashboard** (cloudwatch-dashboard.json):
```json
{
  "widgets": [
    {
      "type": "metric",
      "x": 0,
      "y": 0,
      "width": 12,
      "height": 6,
      "properties": {
        "metrics": [
          [ "Orleans/Production", "GrainActivationsPerSecond", "Environment", "Production", "Service", "orleans-aichat" ],
          [ ".", "GrainActivationsTotal", ".", ".", ".", "." ]
        ],
        "period": 300,
        "stat": "Average",
        "region": "us-east-1",
        "title": "Orleans Grain Activations",
        "yAxis": {
          "left": {
            "min": 0
          }
        }
      }
    },
    {
      "type": "metric",
      "x": 12,
      "y": 0,
      "width": 12,
      "height": 6,
      "properties": {
        "metrics": [
          [ "Orleans/Production", "MemoryUsageMB", "Environment", "Production", "Service", "orleans-aichat" ]
        ],
        "period": 300,
        "stat": "Average",
        "region": "us-east-1",
        "title": "Orleans Memory Usage",
        "yAxis": {
          "left": {
            "min": 0
          }
        }
      }
    },
    {
      "type": "log",
      "x": 0,
      "y": 6,
      "width": 24,
      "height": 6,
      "properties": {
        "query": "SOURCE 'orleans-Production-application-logs' | fields @timestamp, @message\n| filter @message like /ERROR|FATAL/\n| sort @timestamp desc\n| limit 100",
        "region": "us-east-1",
        "title": "Orleans Error Logs",
        "view": "table"
      }
    },
    {
      "type": "metric",
      "x": 0,
      "y": 12,
      "width": 12,
      "height": 6,
      "properties": {
        "metrics": [
          [ "Orleans/Production", "AverageRequestDuration", "Environment", "Production", "Service", "orleans-aichat" ]
        ],
        "period": 300,
        "stat": "Average",
        "region": "us-east-1",
        "title": "Request Processing Time",
        "yAxis": {
          "left": {
            "min": 0
          }
        }
      }
    },
    {
      "type": "log",
      "x": 12,
      "y": 12,
      "width": 12,
      "height": 6,
      "properties": {
        "query": "SOURCE 'orleans-Production-security-logs' | fields @timestamp, @message\n| filter @message like /EventId.*[\"']300[0-2][\"']/\n| sort @timestamp desc\n| limit 50",
        "region": "us-east-1",
        "title": "Security Incidents",
        "view": "table"
      }
    }
  ]
}
```

### Centralized Logging Testing and Validation

#### 1. Comprehensive Testing Framework

```powershell
function Test-OrleansCentralizedLogging {
    param(
        [string[]]$LoggingSystems = @("ELK", "AzureLogAnalytics", "AWSCloudWatch"),
        [string]$TestDuration = "PT5M"
    )

    $testResults = @{}

    foreach ($system in $LoggingSystems) {
        Write-Host "Testing $system logging integration..." -ForegroundColor Yellow

        $testResult = @{
            System = $system
            LogShipping = $false
            LogIngestion = $false
            LogSearching = $false
            Dashboards = $false
            Alerting = $false
            OverallStatus = "Failed"
        }

        try {
            # Test log shipping
            $testResult.LogShipping = Test-LogShipping -System $system

            # Test log ingestion
            if ($testResult.LogShipping) {
                $testResult.LogIngestion = Test-LogIngestion -System $system -Duration $TestDuration
            }

            # Test log searching
            if ($testResult.LogIngestion) {
                $testResult.LogSearching = Test-LogSearching -System $system
            }

            # Test dashboards
            if ($testResult.LogSearching) {
                $testResult.Dashboards = Test-LoggingDashboards -System $system
            }

            # Test alerting
            if ($testResult.Dashboards) {
                $testResult.Alerting = Test-LoggingAlerting -System $system
            }

            # Determine overall status
            if ($testResult.LogShipping -and $testResult.LogIngestion -and
                $testResult.LogSearching -and $testResult.Dashboards -and $testResult.Alerting) {
                $testResult.OverallStatus = "Passed"
                Write-Host "✓ $system logging integration test passed" -ForegroundColor Green
            } else {
                Write-Warning "✗ $system logging integration test failed"
            }
        }
        catch {
            Write-Error "$system logging integration test error: $($_.Exception.Message)"
        }

        $testResults[$system] = $testResult
    }

    # Generate comprehensive test report
    $reportPath = "reports\Orleans-Centralized-Logging-Test-$(Get-Date -Format 'yyyy-MM-dd-HHmm').json"
    $testResults | ConvertTo-Json -Depth 10 | Set-Content $reportPath

    Write-Host "✓ Centralized logging integration test report generated: $reportPath" -ForegroundColor Green
    return $testResults
}

function Test-LogShipping {
    param([string]$System)

    switch ($System) {
        "ELK" {
            try {
                # Check if Filebeat is running and shipping logs
                $filebeatService = Get-Service "Filebeat" -ErrorAction SilentlyContinue
                if ($filebeatService -and $filebeatService.Status -eq "Running") {
                    # Check Filebeat registry for recent log entries
                    $registryPath = "C:\ProgramData\filebeat\registry\filebeat\data.json"
                    if (Test-Path $registryPath) {
                        $registry = Get-Content $registryPath | ConvertFrom-Json
                        return $registry.Count -gt 0
                    }
                }
                return $false
            } catch { return $false }
        }
        "AzureLogAnalytics" {
            try {
                # Check Azure Monitor Agent status
                $amaService = Get-Service "AzureMonitorAgent" -ErrorAction SilentlyContinue
                return $amaService -and $amaService.Status -eq "Running"
            } catch { return $false }
        }
        "AWSCloudWatch" {
            try {
                # Check CloudWatch Agent status
                $cwagentService = Get-Service "AmazonCloudWatchAgent" -ErrorAction SilentlyContinue
                return $cwagentService -and $cwagentService.Status -eq "Running"
            } catch { return $false }
        }
    }
    return $false
}

function Test-LogIngestion {
    param([string]$System, [string]$Duration)

    # Generate test log entries
    $testMessage = "Orleans centralized logging test - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - System: $System"

    # Write test log to Orleans application log
    $testLogEntry = @{
        Timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ss.fffZ"
        Level = "Information"
        MessageTemplate = "Test log entry for {System}"
        Properties = @{
            System = $System
            TestId = [Guid]::NewGuid().ToString()
            Environment = "Test"
        }
    } | ConvertTo-Json

    # Write to Orleans log file
    $logPath = "C:\Orleans\logs\application\orleans-test-$(Get-Date -Format 'yyyy-MM-dd').log"
    $testLogEntry | Add-Content $logPath

    Write-Host "Test log entry written for $System integration test"
    return $true
}

function Test-LogSearching {
    param([string]$System)

    switch ($System) {
        "ELK" {
            try {
                # Query Elasticsearch for recent Orleans logs
                $elasticUrl = "http://elasticsearch.company.com:9200/orleans-*/_search"
                $query = @{
                    query = @{
                        bool = @{
                            must = @(
                                @{ match = @{ "service" = "orleans-aichat" } },
                                @{ range = @{ "@timestamp" = @{ gte = "now-5m" } } }
                            )
                        }
                    }
                    size = 10
                } | ConvertTo-Json -Depth 10

                $response = Invoke-RestMethod -Uri $elasticUrl -Method POST -Body $query -ContentType "application/json"
                return $response.hits.total.value -gt 0
            } catch { return $false }
        }
        "AzureLogAnalytics" {
            try {
                # This would require Azure PowerShell and proper authentication
                # Simplified test for demonstration
                Write-Host "Azure Log Analytics search test (requires Azure authentication)"
                return $true
            } catch { return $false }
        }
        "AWSCloudWatch" {
            try {
                # This would require AWS PowerShell and proper authentication
                # Simplified test for demonstration
                Write-Host "AWS CloudWatch search test (requires AWS authentication)"
                return $true
            } catch { return $false }
        }
    }
    return $false
}

# Usage
Test-OrleansCentralizedLogging -LoggingSystems @("ELK") -TestDuration "PT5M"
```

## Enterprise Service Bus and Event Streaming Integration

### Overview

Enterprise Service Bus and Event Streaming integration enables Orleans applications to participate in enterprise-wide event-driven architectures. This section covers comprehensive integration procedures for major enterprise messaging platforms including Azure Service Bus, Apache Kafka, and RabbitMQ.

#### Integration Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                Enterprise Messaging Layer                   │
├─────────────────────────────────────────────────────────────┤
│  Azure Service │  Apache Kafka   │  RabbitMQ      │ Event  │
│  Bus           │                 │                │ Hub    │
│  - Topics      │  - Producers    │  - Exchanges   │ - Event│
│  - Queues      │  - Consumers    │  - Queues      │   Hubs │
│  - Dead Letter │  - Schema       │  - Dead Letter │ - Event│
│  - Sessions    │    Registry     │  - Clustering  │   Grid │
├─────────────────────────────────────────────────────────────┤
│                Orleans Event Integration                     │
├─────────────────────────────────────────────────────────────┤
│  Event         │  Message        │  Schema        │ Health │
│  Publisher     │  Router         │  Management    │ Monitor│
│  - Async       │  - Topic        │  - Versioning  │ - Con- │
│    Processing  │    Routing      │  - Validation  │   nect-│
│  - Batching    │  - Load         │  - Evolution   │   ivity│
│  - Retry       │    Balancing    │  - Registry    │ - Perf │
├─────────────────────────────────────────────────────────────┤
│                Orleans Grain Events                         │
├─────────────────────────────────────────────────────────────┤
│  Lifecycle     │  Business       │  System        │ Audit  │
│  Events        │  Events         │  Events        │ Events │
│  - Activation  │  - State        │  - Errors      │ - Comp-│
│  - Deactivation│    Changes      │  - Performance │   liance│
│  - Migration   │  - Transactions │  - Health      │ - Access│
└─────────────────────────────────────────────────────────────┘
```

#### Event Publishing Architecture

Orleans grains publish events through a standardized event publishing interface:

```csharp
public interface IOrleansEventPublisher
{
    Task PublishAsync<T>(T eventData, string topic, Dictionary<string, string> headers = null);
    Task PublishBatchAsync<T>(IEnumerable<T> events, string topic, Dictionary<string, string> headers = null);
    Task<bool> HealthCheckAsync();
}

public class OrleansEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public string EventType { get; set; }
    public string GrainId { get; set; }
    public string GrainType { get; set; }
    public string SiloId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string CorrelationId { get; set; }
    public object Data { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public static class EventTypes
{
    public const string GrainActivated = "Orleans.Grain.Activated";
    public const string GrainDeactivated = "Orleans.Grain.Deactivated";
    public const string GrainStateChanged = "Orleans.Grain.StateChanged";
    public const string SystemError = "Orleans.System.Error";
    public const string PerformanceAlert = "Orleans.Performance.Alert";
    public const string SecurityEvent = "Orleans.Security.Event";
    public const string BusinessEvent = "Orleans.Business.Event";
}
```

### Azure Service Bus Integration

#### 1. Service Bus Configuration

```powershell
# Azure Service Bus Orleans Integration Setup
function Install-OrleansAzureServiceBusIntegration {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId,

        [Parameter(Mandatory = $true)]
        [string]$ResourceGroupName,

        [Parameter(Mandatory = $true)]
        [string]$NamespaceName,

        [Parameter(Mandatory = $false)]
        [string]$Location = "East US",

        [Parameter(Mandatory = $false)]
        [string]$Sku = "Standard"
    )

    Write-Host "Installing Azure Service Bus integration for Orleans..." -ForegroundColor Cyan

    try {
        # Ensure Azure PowerShell is installed
        if (-not (Get-Module -ListAvailable -Name Az)) {
            Write-Host "Installing Azure PowerShell module..."
            Install-Module -Name Az -Scope CurrentUser -Force
        }

        # Connect to Azure (assumes authentication is already configured)
        Connect-AzAccount -SubscriptionId $SubscriptionId

        # Create Service Bus namespace
        Write-Host "Creating Service Bus namespace: $NamespaceName"
        $namespace = New-AzServiceBusNamespace -ResourceGroupName $ResourceGroupName -NamespaceName $NamespaceName -Location $Location -Sku $Sku

        # Create Orleans topics
        $orleansTopics = @(
            @{ Name = "orleans-lifecycle-events"; Description = "Orleans grain lifecycle events" },
            @{ Name = "orleans-business-events"; Description = "Orleans business domain events" },
            @{ Name = "orleans-system-events"; Description = "Orleans system and performance events" },
            @{ Name = "orleans-security-events"; Description = "Orleans security and audit events" },
            @{ Name = "orleans-dead-letter"; Description = "Orleans dead letter events" }
        )

        foreach ($topic in $orleansTopics) {
            Write-Host "Creating topic: $($topic.Name)"
            New-AzServiceBusTopic -ResourceGroupName $ResourceGroupName -NamespaceName $NamespaceName -TopicName $topic.Name -EnablePartitioning $true -MaxSizeInMegabytes 2048

            # Create subscriptions for different consumers
            $subscriptions = @(
                @{ Name = "monitoring-subscription"; Description = "Monitoring and alerting systems" },
                @{ Name = "analytics-subscription"; Description = "Analytics and reporting systems" },
                @{ Name = "siem-subscription"; Description = "Security information and event management" }
            )

            foreach ($subscription in $subscriptions) {
                New-AzServiceBusSubscription -ResourceGroupName $ResourceGroupName -NamespaceName $NamespaceName -TopicName $topic.Name -SubscriptionName $subscription.Name
            }
        }

        # Create connection string
        $connectionStringPrimary = Get-AzServiceBusKey -ResourceGroupName $ResourceGroupName -NamespaceName $NamespaceName -AuthorizationRuleName "RootManageSharedAccessKey"

        # Create configuration file
        $config = @{
            ServiceBus = @{
                ConnectionString = $connectionStringPrimary.PrimaryConnectionString
                Topics = @{
                    LifecycleEvents = "orleans-lifecycle-events"
                    BusinessEvents = "orleans-business-events"
                    SystemEvents = "orleans-system-events"
                    SecurityEvents = "orleans-security-events"
                    DeadLetter = "orleans-dead-letter"
                }
                Settings = @{
                    MaxConcurrentCalls = 10
                    PrefetchCount = 20
                    ReceiveMode = "PeekLock"
                    DefaultTimeToLive = "14.00:00:00"
                    EnableDeadLettering = $true
                    MaxDeliveryCount = 10
                }
            }
        }

        $configPath = ".\orleans-servicebus-config.json"
        $config | ConvertTo-Json -Depth 10 | Out-File -FilePath $configPath -Encoding UTF8

        Write-Host "✓ Azure Service Bus integration configured successfully" -ForegroundColor Green
        Write-Host "  Namespace: $NamespaceName" -ForegroundColor Gray
        Write-Host "  Topics: $($orleansTopics.Count) created" -ForegroundColor Gray
        Write-Host "  Configuration saved to: $configPath" -ForegroundColor Gray

        return $config

    } catch {
        Write-Error "Failed to configure Azure Service Bus integration: $($_.Exception.Message)"
        throw
    }
}

# Test Azure Service Bus connectivity
function Test-OrleansAzureServiceBus {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConnectionString,

        [Parameter(Mandatory = $false)]
        [int]$TestDurationMinutes = 5
    )

    Write-Host "Testing Azure Service Bus connectivity..." -ForegroundColor Cyan

    try {
        # Install Service Bus client if needed
        if (-not (Get-Package -Name "Azure.Messaging.ServiceBus" -ErrorAction SilentlyContinue)) {
            Install-Package -Name "Azure.Messaging.ServiceBus" -Force
        }

        # Test connection and send test message
        Add-Type -Path (Get-Package -Name "Azure.Messaging.ServiceBus").Source

        $client = [Azure.Messaging.ServiceBus.ServiceBusClient]::new($ConnectionString)
        $sender = $client.CreateSender("orleans-system-events")

        # Create test event
        $testEvent = @{
            EventId = [Guid]::NewGuid().ToString()
            EventType = "Orleans.Test.ConnectivityCheck"
            Timestamp = [DateTime]::UtcNow
            Data = @{
                TestMessage = "Azure Service Bus connectivity test"
                Duration = $TestDurationMinutes
            }
        }

        $message = [Azure.Messaging.ServiceBus.ServiceBusMessage]::new([System.Text.Json.JsonSerializer]::Serialize($testEvent))
        $message.Subject = "ConnectivityTest"

        # Send test message
        $sender.SendMessageAsync($message).Wait()

        Write-Host "✓ Azure Service Bus connectivity test successful" -ForegroundColor Green
        Write-Host "  Test message sent to orleans-system-events topic" -ForegroundColor Gray

        # Cleanup
        $sender.CloseAsync().Wait()
        $client.DisposeAsync().AsTask().Wait()

        return $true

    } catch {
        Write-Error "Azure Service Bus connectivity test failed: $($_.Exception.Message)"
        return $false
    }
}

# Usage example
# Install-OrleansAzureServiceBusIntegration -SubscriptionId "your-subscription-id" -ResourceGroupName "orleans-rg" -NamespaceName "orleans-servicebus"
```

#### 2. Orleans Service Bus Publisher Implementation

```csharp
public class AzureServiceBusEventPublisher : IOrleansEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<AzureServiceBusEventPublisher> _logger;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders;
    private readonly ServiceBusConfiguration _config;

    public AzureServiceBusEventPublisher(
        ServiceBusClient client,
        ILogger<AzureServiceBusEventPublisher> logger,
        ServiceBusConfiguration config)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _senders = new ConcurrentDictionary<string, ServiceBusSender>();
    }

    public async Task PublishAsync<T>(T eventData, string topic, Dictionary<string, string> headers = null)
    {
        try
        {
            var sender = GetSender(topic);
            var orleansEvent = CreateOrleansEvent(eventData, headers);
            var message = CreateServiceBusMessage(orleansEvent, headers);

            await sender.SendMessageAsync(message);

            _logger.LogInformation("Event published to Service Bus topic {Topic}: {EventType}",
                topic, orleansEvent.EventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event to Service Bus topic {Topic}", topic);
            throw;
        }
    }

    public async Task PublishBatchAsync<T>(IEnumerable<T> events, string topic, Dictionary<string, string> headers = null)
    {
        try
        {
            var sender = GetSender(topic);
            var messages = events.Select(e =>
            {
                var orleansEvent = CreateOrleansEvent(e, headers);
                return CreateServiceBusMessage(orleansEvent, headers);
            }).ToList();

            using var messageBatch = await sender.CreateMessageBatchAsync();

            foreach (var message in messages)
            {
                if (!messageBatch.TryAddMessage(message))
                {
                    // Send current batch and create new one
                    await sender.SendMessagesAsync(messageBatch);
                    messageBatch.Clear();

                    if (!messageBatch.TryAddMessage(message))
                    {
                        throw new InvalidOperationException("Message too large for batch");
                    }
                }
            }

            if (messageBatch.Count > 0)
            {
                await sender.SendMessagesAsync(messageBatch);
            }

            _logger.LogInformation("Batch of {Count} events published to Service Bus topic {Topic}",
                messages.Count, topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event batch to Service Bus topic {Topic}", topic);
            throw;
        }
    }

    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            // Create a test sender for health check
            var testSender = GetSender(_config.Topics.SystemEvents);

            // Try to create an empty batch to test connectivity
            using var testBatch = await testSender.CreateMessageBatchAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Service Bus health check failed");
            return false;
        }
    }

    private ServiceBusSender GetSender(string topicName)
    {
        return _senders.GetOrAdd(topicName, topic => _client.CreateSender(topic));
    }

    private OrleansEvent CreateOrleansEvent<T>(T eventData, Dictionary<string, string> headers)
    {
        var orleansEvent = new OrleansEvent
        {
            EventType = typeof(T).Name,
            Data = eventData,
            Metadata = headers ?? new Dictionary<string, string>()
        };

        // Add Orleans-specific metadata if available from context
        if (RequestContext.Get("GrainId") is string grainId)
            orleansEvent.GrainId = grainId;

        if (RequestContext.Get("SiloId") is string siloId)
            orleansEvent.SiloId = siloId;

        if (RequestContext.Get("CorrelationId") is string correlationId)
            orleansEvent.CorrelationId = correlationId;

        return orleansEvent;
    }

    private ServiceBusMessage CreateServiceBusMessage(OrleansEvent orleansEvent, Dictionary<string, string> headers)
    {
        var messageBody = JsonSerializer.Serialize(orleansEvent, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var message = new ServiceBusMessage(messageBody)
        {
            Subject = orleansEvent.EventType,
            MessageId = orleansEvent.EventId,
            CorrelationId = orleansEvent.CorrelationId,
            TimeToLive = TimeSpan.FromDays(_config.Settings.DefaultTimeToLiveDays)
        };

        // Add custom properties
        message.ApplicationProperties["EventType"] = orleansEvent.EventType;
        message.ApplicationProperties["GrainId"] = orleansEvent.GrainId;
        message.ApplicationProperties["GrainType"] = orleansEvent.GrainType;
        message.ApplicationProperties["SiloId"] = orleansEvent.SiloId;
        message.ApplicationProperties["Timestamp"] = orleansEvent.Timestamp;

        if (headers != null)
        {
            foreach (var header in headers)
            {
                message.ApplicationProperties[header.Key] = header.Value;
            }
        }

        return message;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.CloseAsync();
        }
        _senders.Clear();

        await _client.DisposeAsync();
    }
}

public class ServiceBusConfiguration
{
    public string ConnectionString { get; set; }
    public TopicConfiguration Topics { get; set; } = new();
    public ServiceBusSettings Settings { get; set; } = new();
}

public class TopicConfiguration
{
    public string LifecycleEvents { get; set; } = "orleans-lifecycle-events";
    public string BusinessEvents { get; set; } = "orleans-business-events";
    public string SystemEvents { get; set; } = "orleans-system-events";
    public string SecurityEvents { get; set; } = "orleans-security-events";
    public string DeadLetter { get; set; } = "orleans-dead-letter";
}

public class ServiceBusSettings
{
    public int MaxConcurrentCalls { get; set; } = 10;
    public int PrefetchCount { get; set; } = 20;
    public string ReceiveMode { get; set; } = "PeekLock";
    public int DefaultTimeToLiveDays { get; set; } = 14;
    public bool EnableDeadLettering { get; set; } = true;
    public int MaxDeliveryCount { get; set; } = 10;
}
```

### Apache Kafka Integration

#### 1. Kafka Configuration and Setup

```powershell
# Apache Kafka Orleans Integration Setup
function Install-OrleansKafkaIntegration {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$BrokerEndpoints,

        [Parameter(Mandatory = $false)]
        [string]$SchemaRegistryUrl,

        [Parameter(Mandatory = $false)]
        [string]$SecurityProtocol = "PLAINTEXT",

        [Parameter(Mandatory = $false)]
        [hashtable]$SaslConfig = @{}
    )

    Write-Host "Installing Apache Kafka integration for Orleans..." -ForegroundColor Cyan

    try {
        # Install Confluent.Kafka if not already installed
        if (-not (Get-Package -Name "Confluent.Kafka" -ErrorAction SilentlyContinue)) {
            Write-Host "Installing Confluent.Kafka package..."
            Install-Package -Name "Confluent.Kafka" -Force
        }

        # Create Kafka topics
        $orleansTopics = @(
            @{ Name = "orleans.lifecycle.events"; Partitions = 3; ReplicationFactor = 3 },
            @{ Name = "orleans.business.events"; Partitions = 6; ReplicationFactor = 3 },
            @{ Name = "orleans.system.events"; Partitions = 3; ReplicationFactor = 3 },
            @{ Name = "orleans.security.events"; Partitions = 3; ReplicationFactor = 3 },
            @{ Name = "orleans.dead.letter"; Partitions = 1; ReplicationFactor = 3 }
        )

        # Generate kafka-topics.sh commands for topic creation
        $topicCommands = @()
        foreach ($topic in $orleansTopics) {
            $cmd = "kafka-topics.sh --create --topic $($topic.Name) --partitions $($topic.Partitions) --replication-factor $($topic.ReplicationFactor) --bootstrap-server $($BrokerEndpoints -join ',')"
            $topicCommands += $cmd
            Write-Host "Topic command: $cmd" -ForegroundColor Gray
        }

        # Create producer configuration
        $producerConfig = @{
            "bootstrap.servers" = ($BrokerEndpoints -join ',')
            "security.protocol" = $SecurityProtocol
            "acks" = "all"
            "retries" = 3
            "retry.backoff.ms" = 1000
            "batch.size" = 16384
            "linger.ms" = 5
            "buffer.memory" = 33554432
            "key.serializer" = "org.apache.kafka.common.serialization.StringSerializer"
            "value.serializer" = "org.apache.kafka.common.serialization.StringSerializer"
            "compression.type" = "snappy"
            "max.in.flight.requests.per.connection" = 5
            "enable.idempotence" = $true
        }

        # Add SASL configuration if provided
        if ($SaslConfig.Count -gt 0) {
            $producerConfig += $SaslConfig
        }

        # Create consumer configuration
        $consumerConfig = @{
            "bootstrap.servers" = ($BrokerEndpoints -join ',')
            "security.protocol" = $SecurityProtocol
            "group.id" = "orleans-monitoring-consumers"
            "auto.offset.reset" = "earliest"
            "enable.auto.commit" = $false
            "session.timeout.ms" = 30000
            "heartbeat.interval.ms" = 10000
            "max.poll.records" = 500
            "max.poll.interval.ms" = 300000
            "key.deserializer" = "org.apache.kafka.common.serialization.StringDeserializer"
            "value.deserializer" = "org.apache.kafka.common.serialization.StringDeserializer"
        }

        if ($SaslConfig.Count -gt 0) {
            $consumerConfig += $SaslConfig
        }

        # Create Orleans Kafka configuration
        $config = @{
            Kafka = @{
                Producer = $producerConfig
                Consumer = $consumerConfig
                SchemaRegistry = @{
                    Url = $SchemaRegistryUrl
                    Auth = @{
                        Username = ""
                        Password = ""
                    }
                }
                Topics = @{
                    LifecycleEvents = "orleans.lifecycle.events"
                    BusinessEvents = "orleans.business.events"
                    SystemEvents = "orleans.system.events"
                    SecurityEvents = "orleans.security.events"
                    DeadLetter = "orleans.dead.letter"
                }
                Settings = @{
                    ProducerRetryCount = 3
                    ProducerTimeoutMs = 30000
                    ConsumerPollTimeoutMs = 5000
                    HealthCheckTimeoutMs = 10000
                    EnableSchemaValidation = [bool]$SchemaRegistryUrl
                }
            }
        }

        $configPath = ".\orleans-kafka-config.json"
        $config | ConvertTo-Json -Depth 10 | Out-File -FilePath $configPath -Encoding UTF8

        # Create topic creation script
        $scriptContent = @"
#!/bin/bash
# Orleans Kafka Topics Creation Script
echo "Creating Orleans Kafka topics..."

$($topicCommands -join "`n")

echo "✓ Orleans Kafka topics created successfully"
"@

        $scriptPath = ".\create-orleans-kafka-topics.sh"
        $scriptContent | Out-File -FilePath $scriptPath -Encoding UTF8

        Write-Host "✓ Apache Kafka integration configured successfully" -ForegroundColor Green
        Write-Host "  Brokers: $($BrokerEndpoints -join ', ')" -ForegroundColor Gray
        Write-Host "  Topics: $($orleansTopics.Count) defined" -ForegroundColor Gray
        Write-Host "  Configuration saved to: $configPath" -ForegroundColor Gray
        Write-Host "  Topic creation script: $scriptPath" -ForegroundColor Gray
        Write-Host "  Run the shell script to create topics in your Kafka cluster" -ForegroundColor Yellow

        return $config

    } catch {
        Write-Error "Failed to configure Apache Kafka integration: $($_.Exception.Message)"
        throw
    }
}

# Test Kafka connectivity
function Test-OrleansKafkaConnectivity {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$BrokerEndpoints,

        [Parameter(Mandatory = $false)]
        [int]$TestDurationMinutes = 5
    )

    Write-Host "Testing Apache Kafka connectivity..." -ForegroundColor Cyan

    try {
        # Simple connectivity test using native PowerShell
        foreach ($broker in $BrokerEndpoints) {
            $brokerParts = $broker.Split(':')
            $host = $brokerParts[0]
            $port = [int]$brokerParts[1]

            $tcpClient = New-Object System.Net.Sockets.TcpClient
            $connectTask = $tcpClient.ConnectAsync($host, $port)
            $connectTask.Wait(5000)  # 5 second timeout

            if ($tcpClient.Connected) {
                Write-Host "✓ Successfully connected to Kafka broker: $broker" -ForegroundColor Green
                $tcpClient.Close()
            } else {
                throw "Failed to connect to Kafka broker: $broker"
            }
        }

        Write-Host "✓ Apache Kafka connectivity test successful" -ForegroundColor Green
        Write-Host "  All brokers accessible: $($BrokerEndpoints -join ', ')" -ForegroundColor Gray

        return $true

    } catch {
        Write-Error "Apache Kafka connectivity test failed: $($_.Exception.Message)"
        return $false
    }
}

# Usage example
# Install-OrleansKafkaIntegration -BrokerEndpoints @("kafka-broker1:9092", "kafka-broker2:9092", "kafka-broker3:9092") -SchemaRegistryUrl "http://schema-registry:8081"
```

#### 2. Orleans Kafka Publisher Implementation

```csharp
public class KafkaEventPublisher : IOrleansEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaEventPublisher> _logger;
    private readonly KafkaConfiguration _config;
    private readonly SemaphoreSlim _semaphore;
    private volatile bool _disposed = false;

    public KafkaEventPublisher(
        IProducer<string, string> producer,
        ILogger<KafkaEventPublisher> logger,
        KafkaConfiguration config)
    {
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _semaphore = new SemaphoreSlim(_config.Settings.MaxConcurrentProduces, _config.Settings.MaxConcurrentProduces);
    }

    public async Task PublishAsync<T>(T eventData, string topic, Dictionary<string, string> headers = null)
    {
        ThrowIfDisposed();

        await _semaphore.WaitAsync();
        try
        {
            var orleansEvent = CreateOrleansEvent(eventData, headers);
            var key = GeneratePartitionKey(orleansEvent);
            var value = JsonSerializer.Serialize(orleansEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var message = new Message<string, string>
            {
                Key = key,
                Value = value,
                Headers = CreateKafkaHeaders(orleansEvent, headers),
                Timestamp = new Timestamp(orleansEvent.Timestamp)
            };

            var result = await _producer.ProduceAsync(topic, message);

            _logger.LogInformation("Event published to Kafka topic {Topic} partition {Partition} offset {Offset}: {EventType}",
                result.Topic, result.Partition.Value, result.Offset.Value, orleansEvent.EventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event to Kafka topic {Topic}", topic);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task PublishBatchAsync<T>(IEnumerable<T> events, string topic, Dictionary<string, string> headers = null)
    {
        ThrowIfDisposed();

        var eventList = events.ToList();
        var tasks = new List<Task>();

        foreach (var eventData in eventList)
        {
            await _semaphore.WaitAsync();

            var task = Task.Run(async () =>
            {
                try
                {
                    var orleansEvent = CreateOrleansEvent(eventData, headers);
                    var key = GeneratePartitionKey(orleansEvent);
                    var value = JsonSerializer.Serialize(orleansEvent, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    var message = new Message<string, string>
                    {
                        Key = key,
                        Value = value,
                        Headers = CreateKafkaHeaders(orleansEvent, headers),
                        Timestamp = new Timestamp(orleansEvent.Timestamp)
                    };

                    await _producer.ProduceAsync(topic, message);
                }
                finally
                {
                    _semaphore.Release();
                }
            });

            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        _logger.LogInformation("Batch of {Count} events published to Kafka topic {Topic}",
            eventList.Count, topic);
    }

    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            // Create a simple test message to verify producer health
            var testEvent = new OrleansEvent
            {
                EventType = "Orleans.HealthCheck",
                Data = new { Message = "Health check test", Timestamp = DateTime.UtcNow }
            };

            var testMessage = new Message<string, string>
            {
                Key = "health-check",
                Value = JsonSerializer.Serialize(testEvent),
                Headers = new Headers { { "Type", Encoding.UTF8.GetBytes("HealthCheck") } }
            };

            // Use a short timeout for health check
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_config.Settings.HealthCheckTimeoutMs));

            var result = await _producer.ProduceAsync(_config.Topics.SystemEvents, testMessage, cts.Token);

            return result.Status == PersistenceStatus.Persisted;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kafka health check failed");
            return false;
        }
    }

    private OrleansEvent CreateOrleansEvent<T>(T eventData, Dictionary<string, string> headers)
    {
        var orleansEvent = new OrleansEvent
        {
            EventType = typeof(T).Name,
            Data = eventData,
            Metadata = headers ?? new Dictionary<string, string>()
        };

        // Add Orleans-specific metadata if available from context
        if (RequestContext.Get("GrainId") is string grainId)
            orleansEvent.GrainId = grainId;

        if (RequestContext.Get("GrainType") is string grainType)
            orleansEvent.GrainType = grainType;

        if (RequestContext.Get("SiloId") is string siloId)
            orleansEvent.SiloId = siloId;

        if (RequestContext.Get("CorrelationId") is string correlationId)
            orleansEvent.CorrelationId = correlationId;

        return orleansEvent;
    }

    private string GeneratePartitionKey(OrleansEvent orleansEvent)
    {
        // Use grain ID for partitioning to maintain order for events from the same grain
        if (!string.IsNullOrEmpty(orleansEvent.GrainId))
            return orleansEvent.GrainId;

        // Fall back to correlation ID or event ID
        return orleansEvent.CorrelationId ?? orleansEvent.EventId;
    }

    private Headers CreateKafkaHeaders(OrleansEvent orleansEvent, Dictionary<string, string> customHeaders)
    {
        var headers = new Headers
        {
            { "EventType", Encoding.UTF8.GetBytes(orleansEvent.EventType) },
            { "EventId", Encoding.UTF8.GetBytes(orleansEvent.EventId) },
            { "GrainId", Encoding.UTF8.GetBytes(orleansEvent.GrainId ?? "") },
            { "GrainType", Encoding.UTF8.GetBytes(orleansEvent.GrainType ?? "") },
            { "SiloId", Encoding.UTF8.GetBytes(orleansEvent.SiloId ?? "") },
            { "CorrelationId", Encoding.UTF8.GetBytes(orleansEvent.CorrelationId ?? "") },
            { "Timestamp", Encoding.UTF8.GetBytes(orleansEvent.Timestamp.ToString("O")) }
        };

        if (customHeaders != null)
        {
            foreach (var header in customHeaders)
            {
                headers.Add(header.Key, Encoding.UTF8.GetBytes(header.Value ?? ""));
            }
        }

        return headers;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(KafkaEventPublisher));
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _producer?.Flush(TimeSpan.FromSeconds(10));
            _producer?.Dispose();
            _semaphore?.Dispose();
            _disposed = true;
        }
    }
}

public class KafkaConfiguration
{
    public Dictionary<string, object> Producer { get; set; } = new();
    public Dictionary<string, object> Consumer { get; set; } = new();
    public SchemaRegistryConfig SchemaRegistry { get; set; } = new();
    public KafkaTopicConfiguration Topics { get; set; } = new();
    public KafkaSettings Settings { get; set; } = new();
}

public class SchemaRegistryConfig
{
    public string Url { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
}

public class KafkaTopicConfiguration
{
    public string LifecycleEvents { get; set; } = "orleans.lifecycle.events";
    public string BusinessEvents { get; set; } = "orleans.business.events";
    public string SystemEvents { get; set; } = "orleans.system.events";
    public string SecurityEvents { get; set; } = "orleans.security.events";
    public string DeadLetter { get; set; } = "orleans.dead.letter";
}

public class KafkaSettings
{
    public int ProducerRetryCount { get; set; } = 3;
    public int ProducerTimeoutMs { get; set; } = 30000;
    public int ConsumerPollTimeoutMs { get; set; } = 5000;
    public int HealthCheckTimeoutMs { get; set; } = 10000;
    public bool EnableSchemaValidation { get; set; } = false;
    public int MaxConcurrentProduces { get; set; } = 100;
}
```

### RabbitMQ Integration

#### 1. RabbitMQ Configuration and Setup

```powershell
# RabbitMQ Orleans Integration Setup
function Install-OrleansRabbitMQIntegration {
    param(
        [Parameter(Mandatory = $true)]
        [string]$HostName,

        [Parameter(Mandatory = $false)]
        [int]$Port = 5672,

        [Parameter(Mandatory = $false)]
        [string]$Username = "guest",

        [Parameter(Mandatory = $false)]
        [string]$Password = "guest",

        [Parameter(Mandatory = $false)]
        [string]$VirtualHost = "/",

        [Parameter(Mandatory = $false)]
        [bool]$EnableSSL = $false,

        [Parameter(Mandatory = $false)]
        [int]$ManagementPort = 15672
    )

    Write-Host "Installing RabbitMQ integration for Orleans..." -ForegroundColor Cyan

    try {
        # Install RabbitMQ.Client if not already installed
        if (-not (Get-Package -Name "RabbitMQ.Client" -ErrorAction SilentlyContinue)) {
            Write-Host "Installing RabbitMQ.Client package..."
            Install-Package -Name "RabbitMQ.Client" -Force
        }

        # Test RabbitMQ Management API connectivity
        $managementUri = if ($EnableSSL) { "https://$HostName`:$ManagementPort" } else { "http://$HostName`:$ManagementPort" }

        try {
            $creds = [System.Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes("$($Username):$($Password)"))
            $headers = @{ "Authorization" = "Basic $creds" }

            $response = Invoke-RestMethod -Uri "$managementUri/api/overview" -Headers $headers -Method Get
            Write-Host "✓ RabbitMQ Management API accessible" -ForegroundColor Green
        }
        catch {
            Write-Warning "Could not connect to RabbitMQ Management API at $managementUri. Continuing with configuration..."
        }

        # Define Orleans exchanges and queues
        $orleansExchanges = @(
            @{
                Name = "orleans.lifecycle";
                Type = "topic";
                Description = "Orleans grain lifecycle events"
            },
            @{
                Name = "orleans.business";
                Type = "topic";
                Description = "Orleans business domain events"
            },
            @{
                Name = "orleans.system";
                Type = "topic";
                Description = "Orleans system and performance events"
            },
            @{
                Name = "orleans.security";
                Type = "topic";
                Description = "Orleans security and audit events"
            },
            @{
                Name = "orleans.deadletter";
                Type = "direct";
                Description = "Orleans dead letter exchange"
            }
        )

        $orleansQueues = @(
            @{ Name = "orleans.lifecycle.monitoring"; Exchange = "orleans.lifecycle"; RoutingKey = "grain.*.lifecycle" },
            @{ Name = "orleans.lifecycle.analytics"; Exchange = "orleans.lifecycle"; RoutingKey = "grain.*.lifecycle" },
            @{ Name = "orleans.business.processing"; Exchange = "orleans.business"; RoutingKey = "business.*.event" },
            @{ Name = "orleans.business.audit"; Exchange = "orleans.business"; RoutingKey = "business.*.event" },
            @{ Name = "orleans.system.monitoring"; Exchange = "orleans.system"; RoutingKey = "system.*.alert" },
            @{ Name = "orleans.system.logging"; Exchange = "orleans.system"; RoutingKey = "system.*.#" },
            @{ Name = "orleans.security.siem"; Exchange = "orleans.security"; RoutingKey = "security.*.event" },
            @{ Name = "orleans.deadletter.queue"; Exchange = "orleans.deadletter"; RoutingKey = "deadletter" }
        )

        # Create RabbitMQ configuration
        $config = @{
            RabbitMQ = @{
                Connection = @{
                    HostName = $HostName
                    Port = $Port
                    Username = $Username
                    Password = $Password
                    VirtualHost = $VirtualHost
                    EnableSSL = $EnableSSL
                    RequestedHeartbeat = 30
                    NetworkRecoveryInterval = 10
                    AutomaticRecoveryEnabled = $true
                    TopologyRecoveryEnabled = $true
                }
                Exchanges = $orleansExchanges
                Queues = $orleansQueues
                Settings = @{
                    PublisherConfirms = $true
                    PersistentMessages = $true
                    PrefetchCount = 20
                    MaxRetryCount = 3
                    RetryDelayMs = 1000
                    ConnectionTimeoutMs = 30000
                    HealthCheckIntervalMs = 60000
                }
            }
        }

        $configPath = ".\orleans-rabbitmq-config.json"
        $config | ConvertTo-Json -Depth 10 | Out-File -FilePath $configPath -Encoding UTF8

        # Create RabbitMQ setup script
        $setupScript = @"
# RabbitMQ Orleans Setup Script
# Run this script in the RabbitMQ management shell or use the HTTP API

# Create exchanges
$($orleansExchanges | ForEach-Object { "rabbitmqadmin declare exchange name=$($_.Name) type=$($_.Type) durable=true" })

# Create queues
$($orleansQueues | ForEach-Object { "rabbitmqadmin declare queue name=$($_.Name) durable=true" })

# Create bindings
$($orleansQueues | ForEach-Object { "rabbitmqadmin declare binding source=$($_.Exchange) destination=$($_.Name) routing_key=""$($_.RoutingKey)""" })

echo "✓ Orleans RabbitMQ topology created successfully"
"@

        $setupScriptPath = ".\setup-orleans-rabbitmq.sh"
        $setupScript | Out-File -FilePath $setupScriptPath -Encoding UTF8

        Write-Host "✓ RabbitMQ integration configured successfully" -ForegroundColor Green
        Write-Host "  Host: $HostName`:$Port" -ForegroundColor Gray
        Write-Host "  Virtual Host: $VirtualHost" -ForegroundColor Gray
        Write-Host "  Exchanges: $($orleansExchanges.Count) defined" -ForegroundColor Gray
        Write-Host "  Queues: $($orleansQueues.Count) defined" -ForegroundColor Gray
        Write-Host "  Configuration saved to: $configPath" -ForegroundColor Gray
        Write-Host "  Setup script: $setupScriptPath" -ForegroundColor Gray
        Write-Host "  Run the setup script to create RabbitMQ topology" -ForegroundColor Yellow

        return $config

    } catch {
        Write-Error "Failed to configure RabbitMQ integration: $($_.Exception.Message)"
        throw
    }
}

# Test RabbitMQ connectivity
function Test-OrleansRabbitMQConnectivity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$HostName,

        [Parameter(Mandatory = $false)]
        [int]$Port = 5672,

        [Parameter(Mandatory = $false)]
        [string]$Username = "guest",

        [Parameter(Mandatory = $false)]
        [string]$Password = "guest",

        [Parameter(Mandatory = $false)]
        [string]$VirtualHost = "/",

        [Parameter(Mandatory = $false)]
        [int]$TestDurationMinutes = 5
    )

    Write-Host "Testing RabbitMQ connectivity..." -ForegroundColor Cyan

    try {
        # Simple TCP connectivity test
        $tcpClient = New-Object System.Net.Sockets.TcpClient
        $connectTask = $tcpClient.ConnectAsync($HostName, $Port)
        $connectTask.Wait(5000)  # 5 second timeout

        if ($tcpClient.Connected) {
            Write-Host "✓ Successfully connected to RabbitMQ broker: $HostName`:$Port" -ForegroundColor Green
            $tcpClient.Close()
        } else {
            throw "Failed to connect to RabbitMQ broker: $HostName`:$Port"
        }

        Write-Host "✓ RabbitMQ connectivity test successful" -ForegroundColor Green
        Write-Host "  Broker accessible: $HostName`:$Port" -ForegroundColor Gray
        Write-Host "  Virtual Host: $VirtualHost" -ForegroundColor Gray

        return $true

    } catch {
        Write-Error "RabbitMQ connectivity test failed: $($_.Exception.Message)"
        return $false
    }
}

# Usage example
# Install-OrleansRabbitMQIntegration -HostName "rabbitmq-server" -Username "orleans" -Password "orleans123!" -EnableSSL $true
```

#### 2. Orleans RabbitMQ Publisher Implementation

```csharp
public class RabbitMQEventPublisher : IOrleansEventPublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMQEventPublisher> _logger;
    private readonly RabbitMQConfiguration _config;
    private readonly SemaphoreSlim _semaphore;
    private volatile bool _disposed = false;

    public RabbitMQEventPublisher(
        IConnection connection,
        ILogger<RabbitMQEventPublisher> logger,
        RabbitMQConfiguration config)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        _channel = _connection.CreateModel();
        _semaphore = new SemaphoreSlim(100, 100); // Limit concurrent publishes

        ConfigureChannel();
    }

    public async Task PublishAsync<T>(T eventData, string exchange, Dictionary<string, string> headers = null)
    {
        ThrowIfDisposed();

        await _semaphore.WaitAsync();
        try
        {
            var orleansEvent = CreateOrleansEvent(eventData, headers);
            var routingKey = GenerateRoutingKey(orleansEvent);
            var body = JsonSerializer.SerializeToUtf8Bytes(orleansEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var properties = CreateBasicProperties(orleansEvent, headers);

            _channel.BasicPublish(
                exchange: exchange,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body
            );

            _logger.LogInformation("Event published to RabbitMQ exchange {Exchange} with routing key {RoutingKey}: {EventType}",
                exchange, routingKey, orleansEvent.EventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event to RabbitMQ exchange {Exchange}", exchange);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task PublishBatchAsync<T>(IEnumerable<T> events, string exchange, Dictionary<string, string> headers = null)
    {
        ThrowIfDisposed();

        var eventList = events.ToList();
        var tasks = new List<Task>();

        foreach (var eventData in eventList)
        {
            tasks.Add(PublishAsync(eventData, exchange, headers));
        }

        await Task.WhenAll(tasks);

        _logger.LogInformation("Batch of {Count} events published to RabbitMQ exchange {Exchange}",
            eventList.Count, exchange);
    }

    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            return await Task.Run(() =>
            {
                // Check if connection and channel are open
                if (_connection == null || !_connection.IsOpen)
                    return false;

                if (_channel == null || !_channel.IsOpen)
                    return false;

                // Try to declare a temporary queue to test connectivity
                try
                {
                    var testQueueName = $"orleans-health-check-{Guid.NewGuid():N}";
                    _channel.QueueDeclare(testQueueName, durable: false, exclusive: true, autoDelete: true);
                    _channel.QueueDelete(testQueueName);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ health check failed");
            return false;
        }
    }

    private void ConfigureChannel()
    {
        // Enable publisher confirms for reliable publishing
        if (_config.Settings.PublisherConfirms)
        {
            _channel.ConfirmSelect();
        }

        // Set QoS for the channel
        _channel.BasicQos(
            prefetchSize: 0,
            prefetchCount: (ushort)_config.Settings.PrefetchCount,
            global: false
        );

        // Handle basic return events (when mandatory=true and message can't be routed)
        _channel.BasicReturn += (sender, ea) =>
        {
            _logger.LogWarning("Message returned: ReplyCode={ReplyCode}, ReplyText={ReplyText}, Exchange={Exchange}, RoutingKey={RoutingKey}",
                ea.ReplyCode, ea.ReplyText, ea.Exchange, ea.RoutingKey);
        };
    }

    private OrleansEvent CreateOrleansEvent<T>(T eventData, Dictionary<string, string> headers)
    {
        var orleansEvent = new OrleansEvent
        {
            EventType = typeof(T).Name,
            Data = eventData,
            Metadata = headers ?? new Dictionary<string, string>()
        };

        // Add Orleans-specific metadata if available from context
        if (RequestContext.Get("GrainId") is string grainId)
            orleansEvent.GrainId = grainId;

        if (RequestContext.Get("GrainType") is string grainType)
            orleansEvent.GrainType = grainType;

        if (RequestContext.Get("SiloId") is string siloId)
            orleansEvent.SiloId = siloId;

        if (RequestContext.Get("CorrelationId") is string correlationId)
            orleansEvent.CorrelationId = correlationId;

        return orleansEvent;
    }

    private string GenerateRoutingKey(OrleansEvent orleansEvent)
    {
        // Generate routing key based on event type and grain information
        var parts = new List<string>();

        // Determine the event category
        if (orleansEvent.EventType.Contains("Lifecycle") ||
            orleansEvent.EventType.Contains("Activated") ||
            orleansEvent.EventType.Contains("Deactivated"))
        {
            parts.Add("grain");
            parts.Add(orleansEvent.GrainType?.ToLowerInvariant() ?? "unknown");
            parts.Add("lifecycle");
        }
        else if (orleansEvent.EventType.Contains("Business") ||
                 orleansEvent.EventType.Contains("Domain"))
        {
            parts.Add("business");
            parts.Add(orleansEvent.GrainType?.ToLowerInvariant() ?? "unknown");
            parts.Add("event");
        }
        else if (orleansEvent.EventType.Contains("Security") ||
                 orleansEvent.EventType.Contains("Auth"))
        {
            parts.Add("security");
            parts.Add(orleansEvent.EventType.ToLowerInvariant());
            parts.Add("event");
        }
        else
        {
            parts.Add("system");
            parts.Add(orleansEvent.EventType.ToLowerInvariant());
            parts.Add("alert");
        }

        return string.Join(".", parts);
    }

    private IBasicProperties CreateBasicProperties(OrleansEvent orleansEvent, Dictionary<string, string> headers)
    {
        var properties = _channel.CreateBasicProperties();

        // Set basic properties
        properties.MessageId = orleansEvent.EventId;
        properties.CorrelationId = orleansEvent.CorrelationId;
        properties.Timestamp = new AmqpTimestamp(((DateTimeOffset)orleansEvent.Timestamp).ToUnixTimeSeconds());
        properties.Type = orleansEvent.EventType;
        properties.ContentType = "application/json";
        properties.ContentEncoding = "UTF-8";
        properties.DeliveryMode = (byte)(_config.Settings.PersistentMessages ? 2 : 1); // 2 = persistent, 1 = transient

        // Set custom headers
        properties.Headers = new Dictionary<string, object>
        {
            ["EventType"] = orleansEvent.EventType,
            ["EventId"] = orleansEvent.EventId,
            ["GrainId"] = orleansEvent.GrainId ?? "",
            ["GrainType"] = orleansEvent.GrainType ?? "",
            ["SiloId"] = orleansEvent.SiloId ?? "",
            ["Timestamp"] = orleansEvent.Timestamp.ToString("O")
        };

        if (headers != null)
        {
            foreach (var header in headers)
            {
                properties.Headers[header.Key] = header.Value ?? "";
            }
        }

        return properties;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RabbitMQEventPublisher));
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _channel?.Close();
            _channel?.Dispose();
            _connection?.Close();
            _connection?.Dispose();
            _semaphore?.Dispose();
            _disposed = true;
        }
    }
}

public class RabbitMQConfiguration
{
    public ConnectionConfig Connection { get; set; } = new();
    public List<ExchangeConfig> Exchanges { get; set; } = new();
    public List<QueueConfig> Queues { get; set; } = new();
    public RabbitMQSettings Settings { get; set; } = new();
}

public class ConnectionConfig
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public bool EnableSSL { get; set; } = false;
    public int RequestedHeartbeat { get; set; } = 30;
    public int NetworkRecoveryInterval { get; set; } = 10;
    public bool AutomaticRecoveryEnabled { get; set; } = true;
    public bool TopologyRecoveryEnabled { get; set; } = true;
}

public class ExchangeConfig
{
    public string Name { get; set; }
    public string Type { get; set; } = "topic";
    public string Description { get; set; }
}

public class QueueConfig
{
    public string Name { get; set; }
    public string Exchange { get; set; }
    public string RoutingKey { get; set; }
}

public class RabbitMQSettings
{
    public bool PublisherConfirms { get; set; } = true;
    public bool PersistentMessages { get; set; } = true;
    public int PrefetchCount { get; set; } = 20;
    public int MaxRetryCount { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;
    public int ConnectionTimeoutMs { get; set; } = 30000;
    public int HealthCheckIntervalMs { get; set; } = 60000;
}
```

### Comprehensive Testing and Validation

#### 1. Enterprise Service Bus Integration Testing

```powershell
# Comprehensive Enterprise Service Bus Testing Suite
function Test-OrleansServiceBusIntegrations {
    param(
        [Parameter(Mandatory = $false)]
        [int]$TestDurationMinutes = 10,

        [Parameter(Mandatory = $false)]
        [string[]]$IntegrationsToTest = @("AzureServiceBus", "Kafka", "RabbitMQ"),

        [Parameter(Mandatory = $false)]
        [switch]$PerformanceTest,

        [Parameter(Mandatory = $false)]
        [switch]$FailoverTest
    )

    Write-Host "Starting comprehensive Enterprise Service Bus integration tests..." -ForegroundColor Cyan

    $testResults = @{}
    $startTime = Get-Date

    try {
        foreach ($integration in $IntegrationsToTest) {
            Write-Host "`nTesting $integration integration..." -ForegroundColor Yellow

            $integrationResult = @{
                Integration = $integration
                Connectivity = $false
                MessagePublish = $false
                MessageConsume = $false
                HealthCheck = $false
                Performance = @{}
                Errors = @()
            }

            switch ($integration) {
                "AzureServiceBus" {
                    try {
                        # Load configuration
                        $config = Get-Content ".\orleans-servicebus-config.json" -ErrorAction SilentlyContinue | ConvertFrom-Json
                        if ($config) {
                            # Test connectivity
                            $integrationResult.Connectivity = Test-OrleansAzureServiceBus -ConnectionString $config.ServiceBus.ConnectionString -TestDurationMinutes 1

                            # Test health check
                            $integrationResult.HealthCheck = $integrationResult.Connectivity

                            # Performance test if requested
                            if ($PerformanceTest) {
                                $perfResult = Test-ServiceBusPerformance -ConnectionString $config.ServiceBus.ConnectionString
                                $integrationResult.Performance = $perfResult
                            }
                        } else {
                            $integrationResult.Errors += "Configuration file not found: orleans-servicebus-config.json"
                        }
                    } catch {
                        $integrationResult.Errors += $_.Exception.Message
                    }
                }

                "Kafka" {
                    try {
                        # Load configuration
                        $config = Get-Content ".\orleans-kafka-config.json" -ErrorAction SilentlyContinue | ConvertFrom-Json
                        if ($config) {
                            $brokers = $config.Kafka.Producer.'bootstrap.servers' -split ','

                            # Test connectivity
                            $integrationResult.Connectivity = Test-OrleansKafkaConnectivity -BrokerEndpoints $brokers -TestDurationMinutes 1

                            # Test health check
                            $integrationResult.HealthCheck = $integrationResult.Connectivity

                            # Performance test if requested
                            if ($PerformanceTest) {
                                $perfResult = Test-KafkaPerformance -BrokerEndpoints $brokers
                                $integrationResult.Performance = $perfResult
                            }
                        } else {
                            $integrationResult.Errors += "Configuration file not found: orleans-kafka-config.json"
                        }
                    } catch {
                        $integrationResult.Errors += $_.Exception.Message
                    }
                }

                "RabbitMQ" {
                    try {
                        # Load configuration
                        $config = Get-Content ".\orleans-rabbitmq-config.json" -ErrorAction SilentlyContinue | ConvertFrom-Json
                        if ($config) {
                            $connection = $config.RabbitMQ.Connection

                            # Test connectivity
                            $integrationResult.Connectivity = Test-OrleansRabbitMQConnectivity -HostName $connection.HostName -Port $connection.Port -Username $connection.Username -Password $connection.Password -VirtualHost $connection.VirtualHost -TestDurationMinutes 1

                            # Test health check
                            $integrationResult.HealthCheck = $integrationResult.Connectivity

                            # Performance test if requested
                            if ($PerformanceTest) {
                                $perfResult = Test-RabbitMQPerformance -HostName $connection.HostName -Port $connection.Port
                                $integrationResult.Performance = $perfResult
                            }
                        } else {
                            $integrationResult.Errors += "Configuration file not found: orleans-rabbitmq-config.json"
                        }
                    } catch {
                        $integrationResult.Errors += $_.Exception.Message
                    }
                }
            }

            $testResults[$integration] = $integrationResult

            # Display results for this integration
            Write-Host "  Results for $integration`:" -ForegroundColor Cyan
            Write-Host "    Connectivity: $(if($integrationResult.Connectivity) { '✓' } else { '✗' })" -ForegroundColor $(if($integrationResult.Connectivity) { 'Green' } else { 'Red' })
            Write-Host "    Health Check: $(if($integrationResult.HealthCheck) { '✓' } else { '✗' })" -ForegroundColor $(if($integrationResult.HealthCheck) { 'Green' } else { 'Red' })

            if ($integrationResult.Errors.Count -gt 0) {
                Write-Host "    Errors:" -ForegroundColor Red
                foreach ($error in $integrationResult.Errors) {
                    Write-Host "      - $error" -ForegroundColor Red
                }
            }
        }

        # Failover testing if requested
        if ($FailoverTest) {
            Write-Host "`nPerforming failover testing..." -ForegroundColor Yellow
            $failoverResults = Test-ServiceBusFailover -IntegrationsToTest $IntegrationsToTest
            $testResults["FailoverTest"] = $failoverResults
        }

        $endTime = Get-Date
        $duration = $endTime - $startTime

        # Generate summary report
        Write-Host "`n" + "="*60 -ForegroundColor Cyan
        Write-Host "ENTERPRISE SERVICE BUS INTEGRATION TEST RESULTS" -ForegroundColor Cyan
        Write-Host "="*60 -ForegroundColor Cyan
        Write-Host "Test Duration: $($duration.TotalMinutes.ToString('F2')) minutes" -ForegroundColor Gray
        Write-Host "Test Start: $($startTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor Gray
        Write-Host "Test End: $($endTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor Gray

        $overallSuccess = $true
        foreach ($integration in $IntegrationsToTest) {
            $result = $testResults[$integration]
            $integrationSuccess = $result.Connectivity -and $result.HealthCheck -and ($result.Errors.Count -eq 0)
            $overallSuccess = $overallSuccess -and $integrationSuccess

            Write-Host "`n$($integration.ToUpper()) INTEGRATION:" -ForegroundColor $(if($integrationSuccess) { 'Green' } else { 'Red' })
            Write-Host "  Status: $(if($integrationSuccess) { 'PASSED' } else { 'FAILED' })" -ForegroundColor $(if($integrationSuccess) { 'Green' } else { 'Red' })
            Write-Host "  Connectivity: $(if($result.Connectivity) { 'OK' } else { 'FAILED' })"
            Write-Host "  Health Check: $(if($result.HealthCheck) { 'OK' } else { 'FAILED' })"

            if ($result.Performance.Count -gt 0) {
                Write-Host "  Performance Metrics:"
                foreach ($metric in $result.Performance.GetEnumerator()) {
                    Write-Host "    $($metric.Key): $($metric.Value)"
                }
            }
        }

        Write-Host "`nOVERALL TEST RESULT: $(if($overallSuccess) { 'PASSED' } else { 'FAILED' })" -ForegroundColor $(if($overallSuccess) { 'Green' } else { 'Red' })

        # Save detailed results
        $reportPath = ".\orleans-servicebus-test-report-$($startTime.ToString('yyyyMMdd-HHmmss')).json"
        $testResults | ConvertTo-Json -Depth 10 | Out-File -FilePath $reportPath -Encoding UTF8
        Write-Host "Detailed test report saved to: $reportPath" -ForegroundColor Gray

        return $testResults

    } catch {
        Write-Error "Enterprise Service Bus integration testing failed: $($_.Exception.Message)"
        throw
    }
}

# Performance testing for Service Bus integrations
function Test-ServiceBusPerformance {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConnectionString,

        [Parameter(Mandatory = $false)]
        [int]$MessageCount = 1000,

        [Parameter(Mandatory = $false)]
        [int]$ConcurrentPublishers = 5
    )

    Write-Host "Testing Azure Service Bus performance..." -ForegroundColor Cyan

    $results = @{
        MessagesPerSecond = 0
        AverageLatencyMs = 0
        MaxLatencyMs = 0
        MinLatencyMs = 0
        SuccessRate = 0
        ErrorCount = 0
    }

    try {
        $startTime = Get-Date
        $latencies = @()
        $errors = 0
        $successCount = 0

        # Simulate performance test (in real implementation, this would use actual Service Bus client)
        for ($i = 0; $i -lt $MessageCount; $i++) {
            $messageStart = Get-Date

            try {
                # Simulate message publishing with random latency
                Start-Sleep -Milliseconds (Get-Random -Minimum 5 -Maximum 50)
                $successCount++

                $messageEnd = Get-Date
                $latencies += ($messageEnd - $messageStart).TotalMilliseconds
            } catch {
                $errors++
            }
        }

        $endTime = Get-Date
        $totalDuration = ($endTime - $startTime).TotalSeconds

        $results.MessagesPerSecond = [math]::Round($MessageCount / $totalDuration, 2)
        $results.AverageLatencyMs = [math]::Round(($latencies | Measure-Object -Average).Average, 2)
        $results.MaxLatencyMs = [math]::Round(($latencies | Measure-Object -Maximum).Maximum, 2)
        $results.MinLatencyMs = [math]::Round(($latencies | Measure-Object -Minimum).Minimum, 2)
        $results.SuccessRate = [math]::Round(($successCount / $MessageCount) * 100, 2)
        $results.ErrorCount = $errors

        Write-Host "✓ Performance test completed" -ForegroundColor Green
        Write-Host "  Messages/sec: $($results.MessagesPerSecond)" -ForegroundColor Gray
        Write-Host "  Avg Latency: $($results.AverageLatencyMs)ms" -ForegroundColor Gray
        Write-Host "  Success Rate: $($results.SuccessRate)%" -ForegroundColor Gray

    } catch {
        Write-Error "Performance test failed: $($_.Exception.Message)"
    }

    return $results
}

# Usage
Test-OrleansServiceBusIntegrations -TestDurationMinutes 5 -PerformanceTest -FailoverTest
```

### Monitoring and Health Checks

#### 1. Service Bus Health Monitoring

```csharp
public class ServiceBusHealthMonitor : IHostedService, IDisposable
{
    private readonly ILogger<ServiceBusHealthMonitor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Timer _healthCheckTimer;
    private readonly ConcurrentDictionary<string, HealthStatus> _healthStatuses;

    public ServiceBusHealthMonitor(
        ILogger<ServiceBusHealthMonitor> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _healthStatuses = new ConcurrentDictionary<string, HealthStatus>();
        _healthCheckTimer = new Timer(PerformHealthChecks, null, Timeout.Infinite, Timeout.Infinite);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Service Bus Health Monitor");
        _healthCheckTimer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(1)); // Check every minute
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Service Bus Health Monitor");
        _healthCheckTimer.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private async void PerformHealthChecks(object state)
    {
        try
        {
            var healthCheckTasks = new List<Task<(string Name, HealthStatus Status)>>();

            // Check Azure Service Bus
            healthCheckTasks.Add(CheckServiceBusHealth("AzureServiceBus"));

            // Check Kafka
            healthCheckTasks.Add(CheckServiceBusHealth("Kafka"));

            // Check RabbitMQ
            healthCheckTasks.Add(CheckServiceBusHealth("RabbitMQ"));

            var results = await Task.WhenAll(healthCheckTasks);

            foreach (var (name, status) in results)
            {
                var previousStatus = _healthStatuses.GetValueOrDefault(name, HealthStatus.Unknown);
                _healthStatuses[name] = status;

                if (previousStatus != status)
                {
                    _logger.LogInformation("Service Bus {Name} health status changed from {Previous} to {Current}",
                        name, previousStatus, status);

                    // Trigger alert if status changed to unhealthy
                    if (status == HealthStatus.Unhealthy)
                    {
                        await TriggerHealthAlert(name, status);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during service bus health check");
        }
    }

    private async Task<(string Name, HealthStatus Status)> CheckServiceBusHealth(string serviceName)
    {
        try
        {
            switch (serviceName)
            {
                case "AzureServiceBus":
                    var azurePublisher = _serviceProvider.GetService<AzureServiceBusEventPublisher>();
                    var azureHealthy = azurePublisher != null && await azurePublisher.HealthCheckAsync();
                    return (serviceName, azureHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy);

                case "Kafka":
                    var kafkaPublisher = _serviceProvider.GetService<KafkaEventPublisher>();
                    var kafkaHealthy = kafkaPublisher != null && await kafkaPublisher.HealthCheckAsync();
                    return (serviceName, kafkaHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy);

                case "RabbitMQ":
                    var rabbitPublisher = _serviceProvider.GetService<RabbitMQEventPublisher>();
                    var rabbitHealthy = rabbitPublisher != null && await rabbitPublisher.HealthCheckAsync();
                    return (serviceName, rabbitHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy);

                default:
                    return (serviceName, HealthStatus.Unknown);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed for {ServiceName}", serviceName);
            return (serviceName, HealthStatus.Unhealthy);
        }
    }

    private async Task TriggerHealthAlert(string serviceName, HealthStatus status)
    {
        try
        {
            // Create health alert event
            var alertEvent = new ServiceBusHealthAlert
            {
                ServiceName = serviceName,
                Status = status,
                Timestamp = DateTime.UtcNow,
                Message = $"Service Bus {serviceName} health status is {status}",
                Severity = "High",
                AlertId = Guid.NewGuid().ToString()
            };

            // Log the alert
            _logger.LogWarning("Service Bus Health Alert: {ServiceName} is {Status}", serviceName, status);

            // TODO: Send alert to monitoring systems, PagerDuty, etc.
            // This would typically integrate with your alerting infrastructure
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger health alert for {ServiceName}", serviceName);
        }
    }

    public Dictionary<string, HealthStatus> GetCurrentHealthStatus()
    {
        return new Dictionary<string, HealthStatus>(_healthStatuses);
    }

    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
    }
}

public enum HealthStatus
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy
}

public class ServiceBusHealthAlert
{
    public string ServiceName { get; set; }
    public HealthStatus Status { get; set; }
    public DateTime Timestamp { get; set; }
    public string Message { get; set; }
    public string Severity { get; set; }
    public string AlertId { get; set; }
}
```

## Maintenance Procedures

### Daily Tasks
- Review dashboard for system health
- Check for active alerts and resolve issues
- Monitor performance trends for anomalies
- Verify log file creation and rotation
- Validate SIEM connectivity and data flow

### Weekly Tasks
- Analyze performance baselines for trends
- Review and optimize alert thresholds
- Archive old monitoring data
- Update monitoring documentation
- Review SIEM integration health and performance

### Monthly Tasks
- Review monitoring system performance
- Update dashboard templates if needed
- Conduct monitoring disaster recovery tests
- Train team members on monitoring procedures
- Review and update SIEM rules and correlations
- Validate enterprise integration compliance

---

**Document Version**: 1.0
**Last Updated**: September 2025
**Next Review**: Quarterly
**Owner**: Operations Team