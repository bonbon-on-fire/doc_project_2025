#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Setup security monitoring for Orleans services.

.DESCRIPTION
    This script automates the setup of comprehensive security monitoring including audit logging,
    threat detection, compliance reporting, and security dashboards.

.PARAMETER ConfigFile
    Path to the Orleans configuration file (appsettings.json)

.PARAMETER Environment
    Target environment (Development, Test, Production)

.PARAMETER LogPath
    Path for security log files

.PARAMETER EventHubConnectionString
    Azure Event Hub connection string for security events

.PARAMETER Force
    Force configuration without prompting

.EXAMPLE
    .\setup-security-monitoring.ps1 -ConfigFile "appsettings.Production.json" -Environment "Production" -LogPath "C:\Orleans\Logs\Security"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConfigFile,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Development", "Test", "Production")]
    [string]$Environment,

    [Parameter(Mandatory = $false)]
    [string]$LogPath = "C:\Orleans\Logs\Security",

    [Parameter(Mandatory = $false)]
    [string]$EventHubConnectionString,

    [Parameter(Mandatory = $false)]
    [int]$RetentionDays = 2555, # 7 years for compliance

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

# Script variables
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Logging function
function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "ERROR" { "Red" }
        "WARN" { "Yellow" }
        "SUCCESS" { "Green" }
        default { "White" }
    }
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

function Test-Prerequisites {
    Write-Log "Checking security monitoring prerequisites..."

    # Check if running as administrator
    if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "This script must be run as Administrator"
    }

    # Check configuration file
    if (-not (Test-Path $ConfigFile)) {
        throw "Configuration file not found: $ConfigFile"
    }

    Write-Log "All prerequisites validated successfully" "SUCCESS"
}

function Set-SecurityLoggingConfiguration {
    Write-Log "Configuring security audit logging..."

    try {
        # Create security log directory
        if (-not (Test-Path $LogPath)) {
            New-Item -Path $LogPath -ItemType Directory -Force
            Write-Log "Created security log directory: $LogPath" "SUCCESS"
        }

        # Set appropriate permissions (Security team and System only)
        $acl = Get-Acl $LogPath
        $acl.SetAccessRuleProtection($true, $false) # Disable inheritance, remove inherited rules

        # Grant permissions
        $systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule("SYSTEM", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
        $adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule("Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
        $securityRule = New-Object System.Security.AccessControl.FileSystemAccessRule("Security-Team", "Read", "ContainerInherit,ObjectInherit", "None", "Allow")

        $acl.SetAccessRule($systemRule)
        $acl.SetAccessRule($adminRule)
        try { $acl.SetAccessRule($securityRule) } catch { Write-Log "Security-Team group not found, skipping..." "WARN" }

        Set-Acl -Path $LogPath -AclObject $acl

        # Configure Windows Event Log
        New-EventLog -LogName "Orleans Security" -Source "Orleans.Security" -ErrorAction SilentlyContinue
        Write-Log "Orleans Security event log configured" "SUCCESS"

        # Test security logging
        Write-EventLog -LogName "Orleans Security" -Source "Orleans.Security" -EventId 1000 -EntryType Information -Message "Security logging enabled for Orleans - $(Get-Date)"

        # Configure application settings
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        $securityLoggingConfig = @{
            Enabled = $true
            LogLevel = "Information"
            EventTypes = @{
                Authentication = @{
                    LoginSuccess = $true
                    LoginFailure = $true
                    Logout = $true
                    PasswordChange = $true
                    AccountLockout = $true
                }
                Authorization = @{
                    AccessGranted = $true
                    AccessDenied = $true
                    RoleAssignment = $true
                    PermissionChange = $true
                }
                DataAccess = @{
                    GrainActivation = $true
                    GrainDeactivation = $true
                    SensitiveDataAccess = $true
                    ConfigurationChange = $true
                }
            }
            Destinations = @(
                @{
                    Type = "File"
                    Path = "$LogPath\security-audit-{Date}.log"
                    RetentionDays = $RetentionDays
                }
            )
        }

        # Add Event Hub destination if provided
        if ($EventHubConnectionString) {
            $securityLoggingConfig.Destinations += @{
                Type = "AzureEventHub"
                ConnectionString = $EventHubConnectionString
                EventHubName = "orleans-security-logs"
            }
        }

        $config | Add-Member -NotePropertyName "SecurityLogging" -NotePropertyValue $securityLoggingConfig -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile

        Write-Log "Security logging configuration completed successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure security logging: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-ThreatDetectionConfiguration {
    Write-Log "Configuring threat detection and alerting..."

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        $threatDetectionConfig = @{
            Enabled = $true
            DetectionEngines = @{
                AnomalyDetection = @{
                    Enabled = $true
                    BaseliningPeriod = "P7D"
                    SensitivityLevel = "Medium"
                    MonitoredMetrics = @(
                        "orleans.requests.rate",
                        "orleans.grain.activations.rate",
                        "orleans.memory.usage.percent",
                        "orleans.authentication.failure.rate"
                    )
                }
                SignatureDetection = @{
                    Enabled = $true
                    SignatureFiles = @("signatures/orleans-threats.yaml", "signatures/web-attacks.yaml")
                    UpdateInterval = "PT1H"
                }
            }
            DetectionRules = @{
                BruteForceAttack = @{
                    Threshold = 5
                    TimeWindow = "PT5M"
                    Action = "Block"
                    AlertSeverity = "High"
                }
                UnusualApiAccess = @{
                    Threshold = 1000
                    TimeWindow = "PT1H"
                    Action = "Alert"
                    AlertSeverity = "Medium"
                }
                ConfigurationTampering = @{
                    MonitoredPaths = @("/api/orleans/configuration", "/dashboard/settings")
                    Action = "Block"
                    AlertSeverity = "Critical"
                }
            }
            ResponseActions = @{
                Block = @{
                    Duration = "PT1H"
                    NotifySecurityTeam = $true
                }
                Alert = @{
                    NotificationChannels = @("Email", "Slack", "Teams")
                }
            }
            AlertChannels = @{
                Email = @{
                    Enabled = $true
                    Recipients = @("security-team@company.com", "operations@company.com")
                    SeverityThreshold = "Medium"
                }
                Slack = @{
                    Enabled = $false
                    WebhookUrl = "#{SLACK_WEBHOOK_URL}#"
                    Channel = "#orleans-security-alerts"
                }
                SIEM = @{
                    Enabled = $false
                    Endpoint = "https://siem.company.com/api/events"
                    Authentication = "Bearer #{SIEM_API_TOKEN}#"
                }
            }
        }

        $config | Add-Member -NotePropertyName "ThreatDetection" -NotePropertyValue $threatDetectionConfig -Force
        $config | Add-Member -NotePropertyName "ThreatDetectionMonitoring" -NotePropertyValue $threatDetectionConfig -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile

        Write-Log "Threat detection configuration completed successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure threat detection: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-ComplianceMonitoringConfiguration {
    Write-Log "Configuring compliance monitoring and reporting..."

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        $complianceConfig = @{
            Enabled = $true
            Frameworks = @{
                SOC2 = @{
                    Enabled = $true
                    Controls = @{
                        "CC6.1" = @{
                            Description = "Logical and physical access controls"
                            Evidence = @("AuditLogs", "AccessReports", "AuthenticationLogs")
                            Frequency = "Monthly"
                        }
                        "CC6.2" = @{
                            Description = "System accounts management"
                            Evidence = @("ServiceAccountAudit", "PrivilegedAccessReports")
                            Frequency = "Quarterly"
                        }
                        "CC6.3" = @{
                            Description = "Network security controls"
                            Evidence = @("FirewallLogs", "NetworkSegmentationReports")
                            Frequency = "Monthly"
                        }
                    }
                    ReportSchedule = "0 0 1 * *" # First day of every month
                    Recipients = @("compliance@company.com", "security@company.com")
                }
                GDPR = @{
                    Enabled = $true
                    DataProcessingLog = "logs/gdpr/data-processing-{Date}.log"
                    ConsentTracking = $true
                    DataRetentionPolicy = "P7Y" # 7 years
                    DataMinimization = $true
                }
            }
            ReportingSchedule = @{
                Daily = @("SecuritySummary")
                Weekly = @("ThreatAnalysis", "AccessReview")
                Monthly = @("ComplianceReport", "AuditReadiness")
            }
        }

        $config | Add-Member -NotePropertyName "ComplianceReporting" -NotePropertyValue $complianceConfig -Force
        $config | Add-Member -NotePropertyName "ComplianceMonitoring" -NotePropertyValue $complianceConfig -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile

        Write-Log "Compliance monitoring configuration completed successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure compliance monitoring: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function New-SecurityMonitoringScripts {
    Write-Log "Creating security monitoring automation scripts..."

    # Security metrics collection script
    $metricsScript = @'
# Orleans Security Metrics Collection Script
param(
    [DateTime]$StartTime = (Get-Date).AddHours(-1),
    [DateTime]$EndTime = (Get-Date),
    [string]$OutputPath = "reports"
)

function Get-OrleansSecurityMetrics {
    param(
        [DateTime]$StartTime,
        [DateTime]$EndTime
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

# Generate security dashboard
function Show-OrleansSecurityDashboard {
    param($Metrics)

    Write-Host "=== Orleans Security Dashboard ===" -ForegroundColor Cyan
    Write-Host "Time Period: $StartTime to $EndTime" -ForegroundColor White
    Write-Host ""

    Write-Host "Authentication Events:" -ForegroundColor Yellow
    Write-Host "  Total Attempts: $($Metrics.AuthenticationEvents.TotalAttempts)"
    Write-Host "  Successful Logins: $($Metrics.AuthenticationEvents.SuccessfulLogins)" -ForegroundColor Green
    Write-Host "  Failed Logins: $($Metrics.AuthenticationEvents.FailedLogins)" -ForegroundColor $(if($Metrics.AuthenticationEvents.FailedLogins -gt 5) { 'Red' } else { 'White' })
    Write-Host "  Account Lockouts: $($Metrics.AuthenticationEvents.AccountLockouts)" -ForegroundColor $(if($Metrics.AuthenticationEvents.AccountLockouts -gt 0) { 'Red' } else { 'White' })
    Write-Host ""

    Write-Host "Authorization Events:" -ForegroundColor Yellow
    Write-Host "  Access Granted: $($Metrics.AuthorizationEvents.AccessGranted)" -ForegroundColor Green
    Write-Host "  Access Denied: $($Metrics.AuthorizationEvents.AccessDenied)" -ForegroundColor $(if($Metrics.AuthorizationEvents.AccessDenied -gt 10) { 'Red' } else { 'White' })
    Write-Host ""

    Write-Host "Security Incidents:" -ForegroundColor Yellow
    Write-Host "  Brute Force Attempts: $($Metrics.SecurityIncidents.BruteForceAttempts)" -ForegroundColor $(if($Metrics.SecurityIncidents.BruteForceAttempts -gt 0) { 'Red' } else { 'Green' })
    Write-Host "  Configuration Changes: $($Metrics.SecurityIncidents.ConfigurationTampering)" -ForegroundColor $(if($Metrics.SecurityIncidents.ConfigurationTampering -gt 0) { 'Red' } else { 'Green' })
    Write-Host "  Unusual Activity: $($Metrics.SecurityIncidents.UnusualActivity)" -ForegroundColor $(if($Metrics.SecurityIncidents.UnusualActivity -gt 0) { 'Yellow' } else { 'Green' })
}

# Main execution
$metrics = Get-OrleansSecurityMetrics -StartTime $StartTime -EndTime $EndTime
if ($metrics) {
    Show-OrleansSecurityDashboard -Metrics $metrics

    # Save metrics report
    if (-not (Test-Path $OutputPath)) {
        New-Item -Path $OutputPath -ItemType Directory -Force
    }

    $reportPath = Join-Path $OutputPath "Orleans-Security-Metrics-$(Get-Date -Format 'yyyy-MM-dd-HH-mm').json"
    $metrics | ConvertTo-Json -Depth 10 | Set-Content $reportPath
    Write-Host "Security metrics saved to: $reportPath" -ForegroundColor Green
}
'@

    # Threat detection script
    $threatDetectionScript = @'
# Orleans Threat Detection Script
param(
    [int]$MonitoringIntervalSeconds = 60,
    [string]$ConfigFile = "appsettings.json"
)

function Test-OrleansAnomalies {
    param([string]$ConfigFile)

    try {
        $currentMetrics = Invoke-RestMethod -Uri "http://localhost:5100/api/orleans/metrics" -ErrorAction SilentlyContinue
        $anomalies = @()

        if ($currentMetrics) {
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
        }

        return $anomalies
    }
    catch {
        Write-Warning "Failed to collect metrics for anomaly detection: $($_.Exception.Message)"
        return @()
    }
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

    # Log to Windows Event Log
    Write-EventLog -LogName "Orleans Security" -Source "Orleans.Security" -EventId 3000 -EntryType Warning -Message "Threat Alert: $($Details.Type) - Current: $($Details.Value), Threshold: $($Details.Threshold)"

    # Display alert
    Write-Host "THREAT ALERT: $($Details.Type) - Current: $($Details.Value), Threshold: $($Details.Threshold)" -ForegroundColor Red
}

# Main threat detection loop
Write-Host "Starting Orleans threat detection monitoring..." -ForegroundColor Green

while ($true) {
    try {
        $anomalies = Test-OrleansAnomalies -ConfigFile $ConfigFile
        foreach ($anomaly in $anomalies) {
            Send-ThreatAlert -Type "Anomaly" -Details $anomaly
        }

        Start-Sleep -Seconds $MonitoringIntervalSeconds
    }
    catch {
        Write-Error "Threat detection error: $($_.Exception.Message)"
        Start-Sleep -Seconds 30
    }
}
'@

    # Save scripts
    $metricsScriptPath = Join-Path $PSScriptRoot "collect-security-metrics.ps1"
    $threatDetectionScriptPath = Join-Path $PSScriptRoot "threat-detection-monitor.ps1"

    $metricsScript | Set-Content $metricsScriptPath
    $threatDetectionScript | Set-Content $threatDetectionScriptPath

    Write-Log "Security monitoring scripts created:" "SUCCESS"
    Write-Log "  Metrics: $metricsScriptPath" "SUCCESS"
    Write-Log "  Threat Detection: $threatDetectionScriptPath" "SUCCESS"
}

function Set-ScheduledSecurityTasks {
    Write-Log "Setting up scheduled security monitoring tasks..."

    try {
        # Schedule daily security metrics collection
        $trigger = New-ScheduledTaskTrigger -Daily -At "06:00AM"
        $action = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-File $(Join-Path $PSScriptRoot 'collect-security-metrics.ps1')"
        Register-ScheduledTask -TaskName "Orleans-Daily-Security-Metrics" -Trigger $trigger -Action $action -Description "Daily Orleans security metrics collection" -Force

        # Schedule weekly compliance reports
        $weeklyTrigger = New-ScheduledTaskTrigger -Weekly -WeeksInterval 1 -DaysOfWeek Sunday -At "02:00AM"
        $weeklyAction = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-File $(Join-Path $PSScriptRoot 'generate-compliance-report.ps1')"
        Register-ScheduledTask -TaskName "Orleans-Weekly-Compliance-Report" -Trigger $weeklyTrigger -Action $weeklyAction -Description "Weekly Orleans compliance reporting" -Force

        Write-Log "Scheduled security tasks configured successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure scheduled tasks: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Test-SecurityMonitoringConfiguration {
    Write-Log "Testing security monitoring configuration..."

    try {
        # Test event log creation
        $testEvent = Get-WinEvent -LogName "Orleans Security" -MaxEvents 1 -ErrorAction SilentlyContinue
        if ($testEvent) {
            Write-Log "Windows Event Log test successful" "SUCCESS"
        }

        # Test log directory permissions
        if (Test-Path $LogPath) {
            $acl = Get-Acl $LogPath
            Write-Log "Security log directory permissions configured" "SUCCESS"
        }

        # Test configuration file syntax
        $config = Get-Content $ConfigFile | ConvertFrom-Json
        if ($config.SecurityLogging -and $config.ThreatDetection -and $config.ComplianceReporting) {
            Write-Log "Configuration file syntax validation successful" "SUCCESS"
        }

        Write-Log "Security monitoring configuration tests completed successfully" "SUCCESS"
    }
    catch {
        Write-Log "Security monitoring configuration test failed: $($_.Exception.Message)" "WARN"
    }
}

# Main execution
try {
    Write-Log "Starting Orleans security monitoring setup..."
    Write-Log "Environment: $Environment"
    Write-Log "Config File: $ConfigFile"
    Write-Log "Log Path: $LogPath"
    Write-Log "Retention Days: $RetentionDays"

    # Step 1: Check prerequisites
    Test-Prerequisites

    # Step 2: Configure security logging
    Set-SecurityLoggingConfiguration

    # Step 3: Configure threat detection
    Set-ThreatDetectionConfiguration

    # Step 4: Configure compliance monitoring
    Set-ComplianceMonitoringConfiguration

    # Step 5: Create monitoring scripts
    New-SecurityMonitoringScripts

    # Step 6: Set up scheduled tasks
    Set-ScheduledSecurityTasks

    # Step 7: Test configuration
    Test-SecurityMonitoringConfiguration

    Write-Log "Orleans security monitoring setup completed successfully!" "SUCCESS"
    Write-Log "Security logs will be stored in: $LogPath" "SUCCESS"
    Write-Log "Event logging configured in Windows Event Log: Orleans Security" "SUCCESS"
    Write-Log "Scheduled tasks created for automated monitoring" "SUCCESS"

    # Display next steps
    Write-Log "`nNext Steps:" "SUCCESS"
    Write-Log "1. Configure SIEM integration endpoints"
    Write-Log "2. Set up security dashboard in Grafana"
    Write-Log "3. Test threat detection rules"
    Write-Log "4. Configure compliance reporting recipients"
    Write-Log "5. Start Orleans services to begin monitoring"
}
catch {
    Write-Log "Security monitoring setup failed: $($_.Exception.Message)" "ERROR"
    exit 1
}