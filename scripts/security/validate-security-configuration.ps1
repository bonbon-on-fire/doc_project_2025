#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Comprehensive security configuration validation for Orleans services.

.DESCRIPTION
    This script performs comprehensive validation of all Orleans security configurations including
    enterprise authentication, certificate management, secrets management, network security,
    and security monitoring. Provides detailed security posture assessment.

.PARAMETER ConfigFile
    Path to the Orleans configuration file (appsettings.json)

.PARAMETER Environment
    Target environment (Development, Test, Production)

.PARAMETER KeyVaultName
    Azure Key Vault name for secrets validation

.PARAMETER Detailed
    Show detailed validation results

.PARAMETER OutputReport
    Generate detailed security validation report

.EXAMPLE
    .\validate-security-configuration.ps1 -ConfigFile "appsettings.Production.json" -Environment "Production" -KeyVaultName "orleans-prod-kv" -Detailed -OutputReport
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConfigFile,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Development", "Test", "Production")]
    [string]$Environment,

    [Parameter(Mandatory = $false)]
    [string]$KeyVaultName,

    [Parameter(Mandatory = $false)]
    [switch]$Detailed,

    [Parameter(Mandatory = $false)]
    [switch]$OutputReport,

    [Parameter(Mandatory = $false)]
    [string]$ReportPath = "reports"
)

# Script variables
$ErrorActionPreference = "Continue"
$ProgressPreference = "SilentlyContinue"

# Validation results
$ValidationResults = @{
    OverallScore = 0
    MaxScore = 0
    Categories = @{}
    Issues = @()
    Recommendations = @()
}

# Logging function
function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "ERROR" { "Red" }
        "WARN" { "Yellow" }
        "SUCCESS" { "Green" }
        "INFO" { "Cyan" }
        default { "White" }
    }
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

function Add-ValidationResult {
    param(
        [string]$Category,
        [string]$Test,
        [bool]$Passed,
        [string]$Message,
        [string]$Recommendation = ""
    )

    if (-not $ValidationResults.Categories[$Category]) {
        $ValidationResults.Categories[$Category] = @{
            Score = 0
            MaxScore = 0
            Tests = @()
        }
    }

    $ValidationResults.Categories[$Category].Tests += @{
        Test = $Test
        Passed = $Passed
        Message = $Message
        Recommendation = $Recommendation
    }

    $ValidationResults.Categories[$Category].MaxScore++
    $ValidationResults.MaxScore++

    if ($Passed) {
        $ValidationResults.Categories[$Category].Score++
        $ValidationResults.OverallScore++
    }
    else {
        $ValidationResults.Issues += "$Category - $Test`: $Message"
        if ($Recommendation) {
            $ValidationResults.Recommendations += "$Category - $Test`: $Recommendation"
        }
    }
}

function Test-Prerequisites {
    Write-Log "Validating prerequisites..." "INFO"
    $category = "Prerequisites"

    # Check if running as administrator
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Add-ValidationResult -Category $category -Test "Administrative Privileges" -Passed $isAdmin -Message $(if($isAdmin) { "Script running with admin privileges" } else { "Script not running as administrator" }) -Recommendation "Run PowerShell as Administrator"

    # Check configuration file
    $configExists = Test-Path $ConfigFile
    Add-ValidationResult -Category $category -Test "Configuration File" -Passed $configExists -Message $(if($configExists) { "Configuration file found" } else { "Configuration file missing: $ConfigFile" }) -Recommendation "Ensure configuration file exists and is accessible"

    if ($configExists) {
        try {
            $config = Get-Content $ConfigFile | ConvertFrom-Json
            Add-ValidationResult -Category $category -Test "Configuration Syntax" -Passed $true -Message "Configuration file syntax is valid"
        }
        catch {
            Add-ValidationResult -Category $category -Test "Configuration Syntax" -Passed $false -Message "Configuration file syntax error: $($_.Exception.Message)" -Recommendation "Fix JSON syntax errors in configuration file"
        }
    }

    # Check required PowerShell modules
    $requiredModules = @("Az.KeyVault", "ActiveDirectory")
    foreach ($module in $requiredModules) {
        $moduleAvailable = Get-Module -ListAvailable -Name $module -ErrorAction SilentlyContinue
        Add-ValidationResult -Category $category -Test "Module: $module" -Passed ($null -ne $moduleAvailable) -Message $(if($moduleAvailable) { "Module $module is available" } else { "Module $module is not installed" }) -Recommendation "Install module: Install-Module $module"
    }
}

function Test-EnterpriseAuthentication {
    Write-Log "Validating enterprise authentication configuration..." "INFO"
    $category = "Enterprise Authentication"

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Test LDAP configuration
        $ldapEnabled = $config.Authentication.LDAP.Enabled -eq $true
        Add-ValidationResult -Category $category -Test "LDAP Configuration" -Passed $ldapEnabled -Message $(if($ldapEnabled) { "LDAP authentication is enabled" } else { "LDAP authentication is not configured" }) -Recommendation "Configure LDAP authentication for enterprise integration"

        if ($ldapEnabled) {
            $ldapSSL = $config.Authentication.LDAP.RequireSSL -eq $true
            Add-ValidationResult -Category $category -Test "LDAP SSL" -Passed $ldapSSL -Message $(if($ldapSSL) { "LDAP SSL/TLS is enabled" } else { "LDAP SSL/TLS is not enabled" }) -Recommendation "Enable SSL/TLS for secure LDAP communication"

            # Test LDAP connectivity
            $ldapServer = $config.Authentication.LDAP.Server
            if ($ldapServer) {
                $serverParts = $ldapServer -replace "ldaps?://", "" -split ":"
                $server = $serverParts[0]
                $port = if ($serverParts[1]) { [int]$serverParts[1] } else { if ($ldapSSL) { 636 } else { 389 } }

                try {
                    $tcpTest = Test-NetConnection -ComputerName $server -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue
                    Add-ValidationResult -Category $category -Test "LDAP Connectivity" -Passed $tcpTest -Message $(if($tcpTest) { "LDAP server connectivity successful" } else { "Cannot connect to LDAP server" }) -Recommendation "Verify network connectivity to LDAP server"
                }
                catch {
                    Add-ValidationResult -Category $category -Test "LDAP Connectivity" -Passed $false -Message "LDAP connectivity test failed: $($_.Exception.Message)" -Recommendation "Check LDAP server configuration and network connectivity"
                }
            }
        }

        # Test OIDC configuration
        $oidcEnabled = $config.Authentication.OpenIdConnect.Enabled -eq $true
        Add-ValidationResult -Category $category -Test "OIDC Configuration" -Passed $oidcEnabled -Message $(if($oidcEnabled) { "OpenID Connect is enabled" } else { "OpenID Connect is not configured" }) -Recommendation "Configure OAuth2/OIDC for modern authentication"

        if ($oidcEnabled) {
            $httpsMetadata = $config.Authentication.OpenIdConnect.RequireHttpsMetadata -eq $true
            Add-ValidationResult -Category $category -Test "OIDC HTTPS Metadata" -Passed $httpsMetadata -Message $(if($httpsMetadata) { "HTTPS metadata validation enabled" } else { "HTTPS metadata validation disabled" }) -Recommendation "Enable HTTPS metadata validation for security"
        }

        # Test RBAC configuration
        $rbacConfigured = $null -ne $config.Authorization.Roles
        Add-ValidationResult -Category $category -Test "RBAC Configuration" -Passed $rbacConfigured -Message $(if($rbacConfigured) { "RBAC roles are configured" } else { "RBAC is not configured" }) -Recommendation "Configure Role-Based Access Control (RBAC)"

        if ($rbacConfigured) {
            $roleCount = $config.Authorization.Roles.PSObject.Properties.Count
            $minRolesConfigured = $roleCount -ge 3
            Add-ValidationResult -Category $category -Test "RBAC Role Definitions" -Passed $minRolesConfigured -Message $(if($minRolesConfigured) { "$roleCount roles defined" } else { "Insufficient role definitions ($roleCount)" }) -Recommendation "Define at least 3 roles (Admin, Operator, Reader)"
        }
    }
    catch {
        Add-ValidationResult -Category $category -Test "Configuration Parsing" -Passed $false -Message "Failed to parse authentication configuration: $($_.Exception.Message)" -Recommendation "Check configuration file syntax and structure"
    }
}

function Test-CertificateManagement {
    Write-Log "Validating certificate and TLS management..." "INFO"
    $category = "Certificate Management"

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Test certificate configuration
        $dashboardCertConfigured = $null -ne $config.Orleans.Dashboard.Certificate
        Add-ValidationResult -Category $category -Test "Dashboard Certificate" -Passed $dashboardCertConfigured -Message $(if($dashboardCertConfigured) { "Dashboard certificate is configured" } else { "Dashboard certificate is not configured" }) -Recommendation "Configure SSL/TLS certificate for Orleans Dashboard"

        if ($dashboardCertConfigured) {
            $certThumbprint = $config.Orleans.Dashboard.Certificate.Thumbprint
            if ($certThumbprint) {
                $cert = Get-ChildItem -Path "Cert:\LocalMachine\My" | Where-Object { $_.Thumbprint -eq $certThumbprint }
                $certExists = $null -ne $cert
                Add-ValidationResult -Category $category -Test "Certificate Existence" -Passed $certExists -Message $(if($certExists) { "Certificate found in store" } else { "Certificate not found in certificate store" }) -Recommendation "Ensure certificate is properly installed in certificate store"

                if ($certExists) {
                    $certValid = $cert.NotAfter -gt (Get-Date)
                    Add-ValidationResult -Category $category -Test "Certificate Validity" -Passed $certValid -Message $(if($certValid) { "Certificate is valid until $($cert.NotAfter)" } else { "Certificate expired on $($cert.NotAfter)" }) -Recommendation "Renew expired certificate"

                    $daysUntilExpiry = ($cert.NotAfter - (Get-Date)).Days
                    $expiryWarning = $daysUntilExpiry -gt 30
                    Add-ValidationResult -Category $category -Test "Certificate Expiry Warning" -Passed $expiryWarning -Message $(if($expiryWarning) { "Certificate expires in $daysUntilExpiry days" } else { "Certificate expires soon ($daysUntilExpiry days)" }) -Recommendation "Plan certificate renewal before expiration"
                }
            }
        }

        # Test mTLS configuration
        $mtlsEnabled = $config.Orleans.Security.mTLS.Enabled -eq $true
        Add-ValidationResult -Category $category -Test "mTLS Configuration" -Passed $mtlsEnabled -Message $(if($mtlsEnabled) { "mTLS is enabled for grain communication" } else { "mTLS is not configured" }) -Recommendation "Enable mTLS for secure grain-to-grain communication"

        # Test TLS hardening
        $tlsHardening = $config.Orleans.Security.TLS.MinimumVersion -eq "Tls13"
        Add-ValidationResult -Category $category -Test "TLS 1.3 Hardening" -Passed $tlsHardening -Message $(if($tlsHardening) { "TLS 1.3 minimum version configured" } else { "TLS hardening not configured" }) -Recommendation "Configure TLS 1.3 as minimum version for security"

        # Test certificate monitoring
        $certMonitoring = $null -ne $config.CertificateMonitoring
        Add-ValidationResult -Category $category -Test "Certificate Monitoring" -Passed $certMonitoring -Message $(if($certMonitoring) { "Certificate monitoring is configured" } else { "Certificate monitoring is not configured" }) -Recommendation "Configure certificate expiry monitoring and alerting"
    }
    catch {
        Add-ValidationResult -Category $category -Test "Configuration Parsing" -Passed $false -Message "Failed to parse certificate configuration: $($_.Exception.Message)" -Recommendation "Check certificate configuration syntax"
    }
}

function Test-SecretsManagement {
    Write-Log "Validating secrets management configuration..." "INFO"
    $category = "Secrets Management"

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Test Azure Key Vault configuration
        $akvEnabled = $config.AzureKeyVault.Enabled -eq $true
        Add-ValidationResult -Category $category -Test "Azure Key Vault" -Passed $akvEnabled -Message $(if($akvEnabled) { "Azure Key Vault integration is enabled" } else { "Azure Key Vault is not configured" }) -Recommendation "Configure Azure Key Vault for centralized secrets management"

        if ($akvEnabled -and $KeyVaultName) {
            try {
                $vault = Get-AzKeyVault -VaultName $KeyVaultName -ErrorAction SilentlyContinue
                $vaultExists = $null -ne $vault
                Add-ValidationResult -Category $category -Test "Key Vault Accessibility" -Passed $vaultExists -Message $(if($vaultExists) { "Key Vault is accessible" } else { "Key Vault is not accessible" }) -Recommendation "Verify Key Vault name and access permissions"

                if ($vaultExists) {
                    $requiredSecrets = @("database-connection-string", "ldap-service-password", "oidc-client-secret")
                    foreach ($secretName in $requiredSecrets) {
                        $secret = Get-AzKeyVaultSecret -VaultName $KeyVaultName -Name $secretName -ErrorAction SilentlyContinue
                        $secretExists = $null -ne $secret
                        Add-ValidationResult -Category $category -Test "Secret: $secretName" -Passed $secretExists -Message $(if($secretExists) { "Secret '$secretName' exists in Key Vault" } else { "Secret '$secretName' not found in Key Vault" }) -Recommendation "Create required secret in Key Vault"
                    }
                }
            }
            catch {
                Add-ValidationResult -Category $category -Test "Key Vault Connection" -Passed $false -Message "Failed to connect to Key Vault: $($_.Exception.Message)" -Recommendation "Check Azure authentication and Key Vault permissions"
            }
        }

        # Test secret rotation configuration
        $rotationEnabled = $config.SecretRotation.Enabled -eq $true
        Add-ValidationResult -Category $category -Test "Secret Rotation" -Passed $rotationEnabled -Message $(if($rotationEnabled) { "Secret rotation is configured" } else { "Secret rotation is not configured" }) -Recommendation "Configure automated secret rotation for security"

        # Test connection string security
        $connectionStringSecurity = $config.SecureConnectionStrings.EncryptionEnabled -eq $true
        Add-ValidationResult -Category $category -Test "Connection String Security" -Passed $connectionStringSecurity -Message $(if($connectionStringSecurity) { "Connection string encryption is enabled" } else { "Connection strings are not encrypted" }) -Recommendation "Enable connection string encryption"
    }
    catch {
        Add-ValidationResult -Category $category -Test "Configuration Parsing" -Passed $false -Message "Failed to parse secrets configuration: $($_.Exception.Message)" -Recommendation "Check secrets management configuration syntax"
    }
}

function Test-NetworkSecurity {
    Write-Log "Validating network security configuration..." "INFO"
    $category = "Network Security"

    # Test firewall rules
    $firewallRules = Get-NetFirewallRule | Where-Object { $_.DisplayName -like "Orleans*" }
    $rulesConfigured = $firewallRules.Count -gt 0
    Add-ValidationResult -Category $category -Test "Firewall Rules" -Passed $rulesConfigured -Message $(if($rulesConfigured) { "$($firewallRules.Count) Orleans firewall rules configured" } else { "No Orleans firewall rules found" }) -Recommendation "Configure Windows Firewall rules for Orleans ports"

    if ($rulesConfigured) {
        $requiredRules = @("Orleans Dashboard HTTPS", "Orleans API HTTPS", "Orleans Silo Communication")
        foreach ($ruleName in $requiredRules) {
            $ruleExists = $firewallRules | Where-Object { $_.DisplayName -eq $ruleName }
            Add-ValidationResult -Category $category -Test "Rule: $ruleName" -Passed ($null -ne $ruleExists) -Message $(if($ruleExists) { "Firewall rule '$ruleName' is configured" } else { "Required firewall rule '$ruleName' is missing" }) -Recommendation "Configure required firewall rule: $ruleName"
        }
    }

    # Test WAF configuration
    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json
        $wafEnabled = $config.ApplicationGateway.WAF.Enabled -eq $true
        Add-ValidationResult -Category $category -Test "Web Application Firewall" -Passed $wafEnabled -Message $(if($wafEnabled) { "WAF configuration is enabled" } else { "WAF is not configured" }) -Recommendation "Configure Web Application Firewall for protection against web attacks"

        # Test DDoS protection
        $ddosEnabled = $config.DDoSProtection.Enabled -eq $true
        Add-ValidationResult -Category $category -Test "DDoS Protection" -Passed $ddosEnabled -Message $(if($ddosEnabled) { "DDoS protection is configured" } else { "DDoS protection is not configured" }) -Recommendation "Configure DDoS protection and rate limiting"

        # Test rate limiting
        $rateLimitingEnabled = $null -ne $config.RateLimiting
        Add-ValidationResult -Category $category -Test "Rate Limiting" -Passed $rateLimitingEnabled -Message $(if($rateLimitingEnabled) { "Rate limiting is configured" } else { "Rate limiting is not configured" }) -Recommendation "Configure rate limiting for API endpoints"
    }
    catch {
        Add-ValidationResult -Category $category -Test "Network Config Parsing" -Passed $false -Message "Failed to parse network configuration: $($_.Exception.Message)" -Recommendation "Check network security configuration syntax"
    }

    # Test port connectivity
    $orleansPortConfig = @{
        "Orleans Dashboard" = 8080
        "Orleans API" = 5099
        "Orleans Host API" = 5100
    }

    foreach ($portName in $orleansPortConfig.Keys) {
        $port = $orleansPortConfig[$portName]
        try {
            $connection = Test-NetConnection -ComputerName "localhost" -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue
            Add-ValidationResult -Category $category -Test "Port Connectivity: $portName" -Passed $connection -Message $(if($connection) { "$portName port $port is accessible" } else { "$portName port $port is not accessible" }) -Recommendation "Ensure Orleans service is running and port is not blocked"
        }
        catch {
            Add-ValidationResult -Category $category -Test "Port Connectivity: $portName" -Passed $false -Message "Failed to test $portName connectivity: $($_.Exception.Message)" -Recommendation "Check network configuration and service status"
        }
    }
}

function Test-SecurityMonitoring {
    Write-Log "Validating security monitoring configuration..." "INFO"
    $category = "Security Monitoring"

    # Test Windows Event Log
    $eventLogExists = Get-WinEvent -ListLog "Orleans Security" -ErrorAction SilentlyContinue
    Add-ValidationResult -Category $category -Test "Security Event Log" -Passed ($null -ne $eventLogExists) -Message $(if($eventLogExists) { "Orleans Security event log is configured" } else { "Orleans Security event log not found" }) -Recommendation "Configure Windows Event Log for Orleans security events"

    # Test security log directory
    $logPath = "C:\Orleans\Logs\Security"
    $logDirExists = Test-Path $logPath
    Add-ValidationResult -Category $category -Test "Security Log Directory" -Passed $logDirExists -Message $(if($logDirExists) { "Security log directory exists" } else { "Security log directory not found" }) -Recommendation "Create security log directory with appropriate permissions"

    if ($logDirExists) {
        try {
            $acl = Get-Acl $logPath
            $hasSecurePermissions = $acl.AccessRuleProtection -eq $true
            Add-ValidationResult -Category $category -Test "Log Directory Permissions" -Passed $hasSecurePermissions -Message $(if($hasSecurePermissions) { "Security log directory has secure permissions" } else { "Security log directory permissions not hardened" }) -Recommendation "Configure secure permissions for security log directory"
        }
        catch {
            Add-ValidationResult -Category $category -Test "Log Directory Permissions" -Passed $false -Message "Failed to check log directory permissions: $($_.Exception.Message)" -Recommendation "Verify log directory access and permissions"
        }
    }

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Test security logging configuration
        $securityLogging = $config.SecurityLogging.Enabled -eq $true
        Add-ValidationResult -Category $category -Test "Security Logging" -Passed $securityLogging -Message $(if($securityLogging) { "Security logging is enabled" } else { "Security logging is not configured" }) -Recommendation "Enable comprehensive security event logging"

        # Test threat detection
        $threatDetection = $config.ThreatDetection.Enabled -eq $true
        Add-ValidationResult -Category $category -Test "Threat Detection" -Passed $threatDetection -Message $(if($threatDetection) { "Threat detection is enabled" } else { "Threat detection is not configured" }) -Recommendation "Configure automated threat detection and response"

        # Test compliance monitoring
        $complianceMonitoring = $config.ComplianceReporting.Enabled -eq $true
        Add-ValidationResult -Category $category -Test "Compliance Monitoring" -Passed $complianceMonitoring -Message $(if($complianceMonitoring) { "Compliance monitoring is enabled" } else { "Compliance monitoring is not configured" }) -Recommendation "Configure compliance monitoring for SOC 2, GDPR requirements"
    }
    catch {
        Add-ValidationResult -Category $category -Test "Monitoring Config Parsing" -Passed $false -Message "Failed to parse monitoring configuration: $($_.Exception.Message)" -Recommendation "Check security monitoring configuration syntax"
    }

    # Test scheduled tasks
    $scheduledTasks = Get-ScheduledTask | Where-Object { $_.TaskName -like "Orleans-*Security*" -or $_.TaskName -like "Orleans-*Compliance*" }
    $tasksConfigured = $scheduledTasks.Count -gt 0
    Add-ValidationResult -Category $category -Test "Scheduled Security Tasks" -Passed $tasksConfigured -Message $(if($tasksConfigured) { "$($scheduledTasks.Count) security-related scheduled tasks found" } else { "No security scheduled tasks configured" }) -Recommendation "Configure automated security monitoring and reporting tasks"
}

function Show-ValidationSummary {
    Write-Host "`n" + "="*80 -ForegroundColor Cyan
    Write-Host " ORLEANS SECURITY CONFIGURATION VALIDATION SUMMARY" -ForegroundColor Cyan
    Write-Host "="*80 -ForegroundColor Cyan

    $overallPercentage = [math]::Round(($ValidationResults.OverallScore / $ValidationResults.MaxScore) * 100, 1)
    $scoreColor = if ($overallPercentage -ge 90) { "Green" } elseif ($overallPercentage -ge 70) { "Yellow" } else { "Red" }

    Write-Host "`nOverall Security Score: $($ValidationResults.OverallScore)/$($ValidationResults.MaxScore) ($overallPercentage%)" -ForegroundColor $scoreColor

    # Category breakdown
    Write-Host "`nCategory Breakdown:" -ForegroundColor Cyan
    foreach ($category in $ValidationResults.Categories.Keys) {
        $categoryResult = $ValidationResults.Categories[$category]
        $categoryPercentage = [math]::Round(($categoryResult.Score / $categoryResult.MaxScore) * 100, 1)
        $categoryColor = if ($categoryPercentage -ge 90) { "Green" } elseif ($categoryPercentage -ge 70) { "Yellow" } else { "Red" }

        Write-Host "  $category`: $($categoryResult.Score)/$($categoryResult.MaxScore) ($categoryPercentage%)" -ForegroundColor $categoryColor

        if ($Detailed) {
            foreach ($test in $categoryResult.Tests) {
                $testColor = if ($test.Passed) { "Green" } else { "Red" }
                $status = if ($test.Passed) { "✓" } else { "✗" }
                Write-Host "    $status $($test.Test): $($test.Message)" -ForegroundColor $testColor
            }
        }
    }

    # Issues and recommendations
    if ($ValidationResults.Issues.Count -gt 0) {
        Write-Host "`nSecurity Issues Found:" -ForegroundColor Red
        foreach ($issue in $ValidationResults.Issues) {
            Write-Host "  • $issue" -ForegroundColor Red
        }
    }

    if ($ValidationResults.Recommendations.Count -gt 0) {
        Write-Host "`nRecommendations:" -ForegroundColor Yellow
        foreach ($recommendation in $ValidationResults.Recommendations) {
            Write-Host "  • $recommendation" -ForegroundColor Yellow
        }
    }

    # Security posture assessment
    Write-Host "`nSecurity Posture Assessment:" -ForegroundColor Cyan
    if ($overallPercentage -ge 95) {
        Write-Host "  EXCELLENT - Your Orleans deployment has enterprise-grade security" -ForegroundColor Green
    }
    elseif ($overallPercentage -ge 85) {
        Write-Host "  GOOD - Your Orleans deployment is well-secured with minor improvements needed" -ForegroundColor Green
    }
    elseif ($overallPercentage -ge 70) {
        Write-Host "  MODERATE - Your Orleans deployment has basic security but needs improvement" -ForegroundColor Yellow
    }
    else {
        Write-Host "  CRITICAL - Your Orleans deployment has significant security gaps that must be addressed" -ForegroundColor Red
    }

    Write-Host "`n" + "="*80 -ForegroundColor Cyan
}

function Export-ValidationReport {
    if (-not $OutputReport) { return }

    Write-Log "Generating detailed security validation report..." "INFO"

    if (-not (Test-Path $ReportPath)) {
        New-Item -Path $ReportPath -ItemType Directory -Force
    }

    $report = @{
        ValidationDate = Get-Date
        Environment = $Environment
        ConfigFile = $ConfigFile
        OverallScore = $ValidationResults.OverallScore
        MaxScore = $ValidationResults.MaxScore
        ScorePercentage = [math]::Round(($ValidationResults.OverallScore / $ValidationResults.MaxScore) * 100, 1)
        Categories = $ValidationResults.Categories
        Issues = $ValidationResults.Issues
        Recommendations = $ValidationResults.Recommendations
        SecurityPosture = if ($report.ScorePercentage -ge 95) { "Excellent" } elseif ($report.ScorePercentage -ge 85) { "Good" } elseif ($report.ScorePercentage -ge 70) { "Moderate" } else { "Critical" }
    }

    $reportFile = Join-Path $ReportPath "Orleans-Security-Validation-Report-$(Get-Date -Format 'yyyy-MM-dd-HH-mm-ss').json"
    $report | ConvertTo-Json -Depth 10 | Set-Content $reportFile

    Write-Log "Security validation report saved: $reportFile" "SUCCESS"
}

# Main execution
try {
    Write-Log "Starting comprehensive Orleans security configuration validation..." "INFO"
    Write-Log "Environment: $Environment" "INFO"
    Write-Log "Config File: $ConfigFile" "INFO"

    # Run validation tests
    Test-Prerequisites
    Test-EnterpriseAuthentication
    Test-CertificateManagement
    Test-SecretsManagement
    Test-NetworkSecurity
    Test-SecurityMonitoring

    # Show results
    Show-ValidationSummary

    # Export report if requested
    Export-ValidationReport

    Write-Log "Security validation completed!" "SUCCESS"

    # Exit with appropriate code based on results
    $overallPercentage = [math]::Round(($ValidationResults.OverallScore / $ValidationResults.MaxScore) * 100, 1)
    if ($overallPercentage -lt 70) {
        exit 1
    }
    else {
        exit 0
    }
}
catch {
    Write-Log "Security validation failed: $($_.Exception.Message)" "ERROR"
    exit 1
}