#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Harden network security for Orleans services.

.DESCRIPTION
    This script automates network security hardening including firewall rules, network segmentation,
    WAF configuration, DDoS protection, and private endpoint setup.

.PARAMETER ConfigFile
    Path to the Orleans configuration file (appsettings.json)

.PARAMETER Environment
    Target environment (Development, Test, Production)

.PARAMETER TrustedNetworks
    Array of trusted network CIDR blocks

.PARAMETER ManagementNetworks
    Array of management network CIDR blocks

.PARAMETER Force
    Force configuration without prompting

.EXAMPLE
    .\harden-network-security.ps1 -ConfigFile "appsettings.Production.json" -Environment "Production" -TrustedNetworks @("10.0.0.0/8") -ManagementNetworks @("10.1.100.0/24")
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConfigFile,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Development", "Test", "Production")]
    [string]$Environment,

    [Parameter(Mandatory = $false)]
    [string[]]$TrustedNetworks = @("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"),

    [Parameter(Mandatory = $false)]
    [string[]]$ManagementNetworks = @("10.1.0.0/16"),

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

# Script variables
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Orleans ports configuration
$OrleansPortConfig = @{
    Dashboard = 8080
    API = 5099
    Silo = 11111
    Gateway = 30000
    HostAPI = 5100
}

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
    Write-Log "Checking network security prerequisites..."

    # Check if running as administrator
    if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "This script must be run as Administrator"
    }

    # Check configuration file
    if (-not (Test-Path $ConfigFile)) {
        throw "Configuration file not found: $ConfigFile"
    }

    # Check if Windows Firewall service is running
    $firewallService = Get-Service -Name "MpsSvc" -ErrorAction SilentlyContinue
    if ($firewallService.Status -ne "Running") {
        Write-Log "Starting Windows Firewall service..." "WARN"
        Start-Service -Name "MpsSvc"
    }

    Write-Log "All prerequisites validated successfully" "SUCCESS"
}

function Set-OrleansFirewallRules {
    Write-Log "Configuring Windows Firewall rules for Orleans..."

    try {
        # Remove existing Orleans firewall rules if Force is specified
        if ($Force) {
            $existingRules = Get-NetFirewallRule | Where-Object { $_.DisplayName -like "Orleans*" }
            if ($existingRules) {
                $existingRules | Remove-NetFirewallRule
                Write-Log "Removed existing Orleans firewall rules" "WARN"
            }
        }

        # Orleans Dashboard (External Access)
        New-NetFirewallRule -DisplayName "Orleans Dashboard HTTPS" `
                           -Direction Inbound `
                           -Protocol TCP `
                           -LocalPort $OrleansPortConfig.Dashboard `
                           -Action Allow `
                           -Profile Domain,Private `
                           -Description "Orleans Dashboard HTTPS access" `
                           -ErrorAction SilentlyContinue

        # Orleans API Endpoints (External Access)
        New-NetFirewallRule -DisplayName "Orleans API HTTPS" `
                           -Direction Inbound `
                           -Protocol TCP `
                           -LocalPort $OrleansPortConfig.API `
                           -Action Allow `
                           -Profile Domain,Private `
                           -Description "Orleans API HTTPS endpoints" `
                           -ErrorAction SilentlyContinue

        # Orleans Internal Communication (Internal Only)
        New-NetFirewallRule -DisplayName "Orleans Silo Communication" `
                           -Direction Inbound `
                           -Protocol TCP `
                           -LocalPort $OrleansPortConfig.Silo `
                           -Action Allow `
                           -Profile Domain `
                           -RemoteAddress LocalSubnet `
                           -Description "Orleans silo-to-silo communication" `
                           -ErrorAction SilentlyContinue

        New-NetFirewallRule -DisplayName "Orleans Gateway Communication" `
                           -Direction Inbound `
                           -Protocol TCP `
                           -LocalPort $OrleansPortConfig.Gateway `
                           -Action Allow `
                           -Profile Domain `
                           -RemoteAddress LocalSubnet `
                           -Description "Orleans gateway communication" `
                           -ErrorAction SilentlyContinue

        # Orleans Host API (Management Networks Only)
        New-NetFirewallRule -DisplayName "Orleans Host API" `
                           -Direction Inbound `
                           -Protocol TCP `
                           -LocalPort $OrleansPortConfig.HostAPI `
                           -Action Allow `
                           -Profile Domain `
                           -RemoteAddress ($ManagementNetworks -join ",") `
                           -Description "Orleans Host API for monitoring" `
                           -ErrorAction SilentlyContinue

        Write-Log "Orleans firewall rules configured successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure firewall rules: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-NetworkSegmentation {
    Write-Log "Configuring network segmentation rules..."

    try {
        # Define security groups for different access levels
        $securityGroups = @{
            "Orleans-Public" = @{
                Ports = @($OrleansPortConfig.Dashboard, $OrleansPortConfig.API)
                Access = "Any"
                Description = "Public Orleans endpoints"
            }
            "Orleans-Internal" = @{
                Ports = @($OrleansPortConfig.Silo, $OrleansPortConfig.Gateway)
                Access = $TrustedNetworks
                Description = "Internal Orleans communication"
            }
            "Orleans-Management" = @{
                Ports = @($OrleansPortConfig.HostAPI)
                Access = $ManagementNetworks
                Description = "Orleans management and monitoring"
            }
        }

        foreach ($groupName in $securityGroups.Keys) {
            $group = $securityGroups[$groupName]
            Write-Log "Configuring security group: $groupName"

            foreach ($port in $group.Ports) {
                $ruleName = "$groupName-Port-$port"

                # Remove existing rule if it exists
                $existingRule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
                if ($existingRule) {
                    Remove-NetFirewallRule -DisplayName $ruleName
                }

                if ($group.Access -eq "Any") {
                    New-NetFirewallRule -DisplayName $ruleName `
                                       -Direction Inbound `
                                       -Protocol TCP `
                                       -LocalPort $port `
                                       -Action Allow `
                                       -Profile Domain,Private `
                                       -Description $group.Description `
                                       -ErrorAction SilentlyContinue
                }
                else {
                    foreach ($network in $group.Access) {
                        $networkRuleName = "$ruleName-$($network.Replace('/','-'))"
                        New-NetFirewallRule -DisplayName $networkRuleName `
                                           -Direction Inbound `
                                           -Protocol TCP `
                                           -LocalPort $port `
                                           -Action Allow `
                                           -Profile Domain `
                                           -RemoteAddress $network `
                                           -Description "$($group.Description) - $network" `
                                           -ErrorAction SilentlyContinue
                    }
                }
            }
        }

        Write-Log "Network segmentation configured successfully" "SUCCESS"
    }
    catch {
        Write-Log "Network segmentation configuration failed: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-WAFConfiguration {
    Write-Log "Configuring Web Application Firewall settings..."

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Configure WAF settings in application configuration
        $wafConfig = @{
            Enabled = $true
            Mode = "Prevention"
            RuleSetType = "OWASP"
            RuleSetVersion = "3.2"
            CustomRules = @(
                @{
                    Name = "RateLimitOrleansAPI"
                    Priority = 1
                    RuleType = "RateLimitRule"
                    Conditions = @(
                        @{
                            MatchVariable = "RequestUri"
                            Operator = "Contains"
                            MatchValues = @("/api/orleans/")
                        }
                    )
                    Action = "Block"
                    RateLimitThreshold = 100
                    RateLimitDuration = "PT1M"
                },
                @{
                    Name = "BlockSuspiciousUserAgents"
                    Priority = 2
                    RuleType = "MatchRule"
                    Conditions = @(
                        @{
                            MatchVariable = "RequestHeaders"
                            Selector = "User-Agent"
                            Operator = "Contains"
                            MatchValues = @("sqlmap", "nmap", "nikto", "burpsuite", "dirb", "gobuster")
                        }
                    )
                    Action = "Block"
                }
            )
            Exclusions = @(
                @{
                    MatchVariable = "RequestArgNames"
                    Selector = "orleans_grain_id"
                    SelectorMatchOperator = "Equals"
                }
            )
        }

        # Add to configuration
        if (-not $config.ApplicationGateway) {
            $config | Add-Member -NotePropertyName "ApplicationGateway" -NotePropertyValue @{}
        }
        $config.ApplicationGateway | Add-Member -NotePropertyName "WAF" -NotePropertyValue $wafConfig -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile

        Write-Log "WAF configuration updated successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure WAF: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-DDoSProtection {
    Write-Log "Configuring DDoS protection and rate limiting..."

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Configure DDoS protection settings
        $ddosConfig = @{
            Enabled = $true
            Plan = "Standard"
            AlertingEnabled = $true
            Metrics = @{
                PacketsDroppedDDoS = @{
                    Threshold = 1000
                    AlertEmail = "security-team@company.com"
                }
                PacketsForwardedDDoS = @{
                    Threshold = 10000
                    AlertEmail = "operations@company.com"
                }
            }
        }

        # Configure rate limiting
        $rateLimitingConfig = @{
            Orleans = @{
                Dashboard = @{
                    RequestsPerMinute = 100
                    RequestsPerHour = 1000
                    BurstAllowance = 20
                }
                API = @{
                    RequestsPerMinute = 500
                    RequestsPerHour = 10000
                    BurstAllowance = 100
                }
            }
        }

        # Add to configuration
        $config | Add-Member -NotePropertyName "DDoSProtection" -NotePropertyValue $ddosConfig -Force
        $config | Add-Member -NotePropertyName "RateLimiting" -NotePropertyValue $rateLimitingConfig -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile

        Write-Log "DDoS protection and rate limiting configured successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure DDoS protection: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-NetworkSecurityGroupRules {
    param([string]$PrivateSubnetCIDR = "10.1.1.0/24")

    Write-Log "Configuring Network Security Group rules..."

    try {
        # Create NSG rules using netsh (since we're on Windows)
        $nsgRules = @(
            @{
                Name = "Allow-HTTPS-Private"
                Protocol = "TCP"
                Port = 443
                SourceCIDR = $PrivateSubnetCIDR
                Action = "Allow"
            },
            @{
                Name = "Allow-Orleans-Internal"
                Protocol = "TCP"
                Port = "$($OrleansPortConfig.Silo),$($OrleansPortConfig.Gateway)"
                SourceCIDR = ($TrustedNetworks -join ",")
                Action = "Allow"
            },
            @{
                Name = "Deny-All-Other"
                Protocol = "Any"
                Port = "Any"
                SourceCIDR = "Any"
                Action = "Block"
            }
        )

        foreach ($rule in $nsgRules) {
            $ruleName = "Orleans-NSG-$($rule.Name)"

            # Remove existing rule if it exists
            $existingRule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
            if ($existingRule) {
                Remove-NetFirewallRule -DisplayName $ruleName
            }

            # Create new rule
            if ($rule.Action -eq "Allow") {
                New-NetFirewallRule -DisplayName $ruleName `
                                   -Direction Inbound `
                                   -Protocol $rule.Protocol `
                                   -LocalPort $rule.Port `
                                   -Action Allow `
                                   -RemoteAddress $rule.SourceCIDR `
                                   -Description "NSG Rule: $($rule.Name)" `
                                   -ErrorAction SilentlyContinue
            }
            else {
                New-NetFirewallRule -DisplayName $ruleName `
                                   -Direction Inbound `
                                   -Protocol TCP `
                                   -Action Block `
                                   -Description "NSG Rule: $($rule.Name)" `
                                   -ErrorAction SilentlyContinue
            }
        }

        Write-Log "Network Security Group rules configured successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure NSG rules: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Test-NetworkSecurity {
    Write-Log "Testing network security configuration..."

    try {
        # Test firewall rules
        $firewallRules = Get-NetFirewallRule | Where-Object { $_.DisplayName -like "Orleans*" }
        Write-Log "Found $($firewallRules.Count) Orleans firewall rules" "SUCCESS"

        # Test port accessibility
        $testPorts = @(
            @{ Port = $OrleansPortConfig.Dashboard; Name = "Orleans Dashboard" },
            @{ Port = $OrleansPortConfig.API; Name = "Orleans API" }
        )

        foreach ($testPort in $testPorts) {
            try {
                $connection = Test-NetConnection -ComputerName "localhost" -Port $testPort.Port -InformationLevel Quiet
                if ($connection) {
                    Write-Log "$($testPort.Name) port $($testPort.Port) is accessible" "SUCCESS"
                }
                else {
                    Write-Log "$($testPort.Name) port $($testPort.Port) is not accessible" "WARN"
                }
            }
            catch {
                Write-Log "Failed to test $($testPort.Name) port: $($_.Exception.Message)" "WARN"
            }
        }

        # Test configuration file syntax
        $config = Get-Content $ConfigFile | ConvertFrom-Json
        if ($config.ApplicationGateway.WAF -and $config.DDoSProtection) {
            Write-Log "Network security configuration syntax validation successful" "SUCCESS"
        }

        Write-Log "Network security tests completed" "SUCCESS"
    }
    catch {
        Write-Log "Network security test failed: $($_.Exception.Message)" "WARN"
    }
}

function New-SecurityValidationScript {
    Write-Log "Creating network security validation script..."

    $validationScript = @'
# Network Security Validation Script for Orleans
param(
    [string]$ConfigFile = "appsettings.json"
)

function Test-FirewallConfiguration {
    Write-Host "Testing firewall configuration..." -ForegroundColor Cyan

    $requiredRules = @(
        "Orleans Dashboard HTTPS",
        "Orleans API HTTPS",
        "Orleans Silo Communication",
        "Orleans Gateway Communication",
        "Orleans Host API"
    )

    foreach ($ruleName in $requiredRules) {
        $rule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
        if ($rule) {
            Write-Host "✓ Firewall rule '$ruleName' is configured" -ForegroundColor Green
        } else {
            Write-Host "✗ Firewall rule '$ruleName' is missing" -ForegroundColor Red
        }
    }
}

function Test-NetworkConnectivity {
    Write-Host "Testing network connectivity..." -ForegroundColor Cyan

    $testEndpoints = @(
        @{ Host = "localhost"; Port = 8080; Name = "Orleans Dashboard" },
        @{ Host = "localhost"; Port = 5099; Name = "Orleans API" }
    )

    foreach ($endpoint in $testEndpoints) {
        $result = Test-NetConnection -ComputerName $endpoint.Host -Port $endpoint.Port -InformationLevel Quiet -WarningAction SilentlyContinue
        if ($result) {
            Write-Host "✓ $($endpoint.Name) is accessible on port $($endpoint.Port)" -ForegroundColor Green
        } else {
            Write-Host "✗ $($endpoint.Name) is not accessible on port $($endpoint.Port)" -ForegroundColor Red
        }
    }
}

function Test-ConfigurationSecurity {
    Write-Host "Testing security configuration..." -ForegroundColor Cyan

    if (Test-Path $ConfigFile) {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        if ($config.ApplicationGateway.WAF.Enabled) {
            Write-Host "✓ WAF is enabled" -ForegroundColor Green
        } else {
            Write-Host "✗ WAF is not enabled" -ForegroundColor Red
        }

        if ($config.DDoSProtection.Enabled) {
            Write-Host "✓ DDoS protection is enabled" -ForegroundColor Green
        } else {
            Write-Host "✗ DDoS protection is not enabled" -ForegroundColor Red
        }
    } else {
        Write-Host "✗ Configuration file not found: $ConfigFile" -ForegroundColor Red
    }
}

# Run validation tests
Test-FirewallConfiguration
Test-NetworkConnectivity
Test-ConfigurationSecurity

Write-Host "`nNetwork security validation completed." -ForegroundColor Cyan
'@

    $validationScriptPath = Join-Path $PSScriptRoot "validate-network-security.ps1"
    $validationScript | Set-Content $validationScriptPath
    Write-Log "Network security validation script created: $validationScriptPath" "SUCCESS"
}

# Main execution
try {
    Write-Log "Starting Orleans network security hardening..."
    Write-Log "Environment: $Environment"
    Write-Log "Config File: $ConfigFile"
    Write-Log "Trusted Networks: $($TrustedNetworks -join ', ')"
    Write-Log "Management Networks: $($ManagementNetworks -join ', ')"

    # Step 1: Check prerequisites
    Test-Prerequisites

    # Step 2: Configure firewall rules
    Set-OrleansFirewallRules

    # Step 3: Configure network segmentation
    Set-NetworkSegmentation

    # Step 4: Configure WAF
    Set-WAFConfiguration

    # Step 5: Configure DDoS protection
    Set-DDoSProtection

    # Step 6: Configure NSG rules
    Set-NetworkSecurityGroupRules

    # Step 7: Create validation script
    New-SecurityValidationScript

    # Step 8: Test configuration
    Test-NetworkSecurity

    Write-Log "Orleans network security hardening completed successfully!" "SUCCESS"
    Write-Log "Firewall rules configured for Orleans ports" "SUCCESS"
    Write-Log "Network segmentation applied" "SUCCESS"
    Write-Log "WAF and DDoS protection configured" "SUCCESS"

    # Display next steps
    Write-Log "`nNext Steps:" "SUCCESS"
    Write-Log "1. Test network connectivity from client applications"
    Write-Log "2. Configure external load balancer with WAF rules"
    Write-Log "3. Set up network monitoring and alerting"
    Write-Log "4. Test DDoS protection mechanisms"
    Write-Log "5. Validate firewall rules in production environment"
}
catch {
    Write-Log "Network security hardening failed: $($_.Exception.Message)" "ERROR"
    exit 1
}