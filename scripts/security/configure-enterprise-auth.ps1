#Requires -Version 5.1
#Requires -Modules ActiveDirectory, Az.KeyVault
<#
.SYNOPSIS
    Configure enterprise authentication for Orleans services.

.DESCRIPTION
    This script automates the setup of enterprise authentication including LDAP/AD integration,
    OAuth2/OIDC configuration, RBAC setup, and service account management.

.PARAMETER ConfigFile
    Path to the Orleans configuration file (appsettings.json)

.PARAMETER Environment
    Target environment (Development, Test, Production)

.PARAMETER KeyVaultName
    Azure Key Vault name for storing secrets

.PARAMETER Force
    Force configuration without prompting

.EXAMPLE
    .\configure-enterprise-auth.ps1 -ConfigFile "appsettings.Production.json" -Environment "Production" -KeyVaultName "orleans-prod-kv"
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
    Write-Log "Checking prerequisites..."

    # Check if running as administrator
    if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "This script must be run as Administrator"
    }

    # Check Active Directory module
    try {
        Import-Module ActiveDirectory -ErrorAction Stop
        Write-Log "Active Directory module loaded successfully" "SUCCESS"
    }
    catch {
        Write-Log "Active Directory module not available. Installing RSAT-AD-PowerShell..." "WARN"
        Enable-WindowsOptionalFeature -Online -FeatureName RSATClient-Roles-AD-Powershell -All
    }

    # Check Azure PowerShell module
    if ($KeyVaultName) {
        try {
            Import-Module Az.KeyVault -ErrorAction Stop
            Write-Log "Azure PowerShell module loaded successfully" "SUCCESS"
        }
        catch {
            Write-Log "Azure PowerShell module not found. Please install: Install-Module Az.KeyVault" "ERROR"
            throw "Required Azure PowerShell module not available"
        }
    }

    # Check configuration file
    if (-not (Test-Path $ConfigFile)) {
        throw "Configuration file not found: $ConfigFile"
    }

    Write-Log "All prerequisites validated successfully" "SUCCESS"
}

function New-OrleansServiceAccount {
    param(
        [string]$AccountName = "orleans-service",
        [string]$DisplayName = "Orleans AI Chat Service",
        [string]$OrganizationalUnit = "OU=Service Accounts,DC=company,DC=com"
    )

    Write-Log "Creating Orleans service account: $AccountName"

    try {
        # Generate secure password
        $password = [System.Web.Security.Membership]::GeneratePassword(32, 8)
        $securePassword = ConvertTo-SecureString $password -AsPlainText -Force

        # Check if account already exists
        $existingAccount = Get-ADUser -Filter "SamAccountName -eq '$AccountName'" -ErrorAction SilentlyContinue

        if ($existingAccount) {
            if ($Force) {
                Write-Log "Service account exists. Resetting password..." "WARN"
                Set-ADAccountPassword -Identity $AccountName -NewPassword $securePassword -Reset
            }
            else {
                Write-Log "Service account already exists. Use -Force to reset password." "WARN"
                return $existingAccount
            }
        }
        else {
            # Create service account
            New-ADUser -Name $AccountName `
                       -DisplayName $DisplayName `
                       -UserPrincipalName "$AccountName@company.com" `
                       -SamAccountName $AccountName `
                       -Path $OrganizationalUnit `
                       -AccountPassword $securePassword `
                       -Enabled $true `
                       -PasswordNeverExpires $true `
                       -CannotChangePassword $true

            Write-Log "Service account '$AccountName' created successfully" "SUCCESS"
        }

        # Set service account properties
        Set-ADUser -Identity $AccountName -Description "Service account for Orleans AI Chat system"

        # Add to required groups
        $groupName = "Orleans Service Accounts"
        try {
            Add-ADGroupMember -Identity $groupName -Members $AccountName
            Write-Log "Added service account to group: $groupName" "SUCCESS"
        }
        catch {
            Write-Log "Failed to add to group '$groupName' - group may not exist" "WARN"
        }

        # Store password in Key Vault if specified
        if ($KeyVaultName) {
            Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name "ldap-service-password" -SecretValue $securePassword
            Write-Log "Service account password stored in Key Vault" "SUCCESS"
        }

        return @{
            AccountName = $AccountName
            Password = $password
            Created = $true
        }
    }
    catch {
        Write-Log "Failed to create service account: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-LDAPConfiguration {
    param([hashtable]$ServiceAccount)

    Write-Log "Configuring LDAP authentication..."

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Ensure Authentication section exists
        if (-not $config.Authentication) {
            $config | Add-Member -NotePropertyName "Authentication" -NotePropertyValue @{}
        }

        # Configure LDAP settings
        $ldapConfig = @{
            Enabled = $true
            Server = "ldaps://your-domain-controller.company.com:636"
            BaseDN = "DC=company,DC=com"
            UserSearchBase = "OU=Users,DC=company,DC=com"
            GroupSearchBase = "OU=Groups,DC=company,DC=com"
            ServiceAccount = @{
                Username = $ServiceAccount.AccountName + "@company.com"
                Password = if ($KeyVaultName) { "#{LDAP_SERVICE_PASSWORD}#" } else { $ServiceAccount.Password }
            }
            UserFilter = "(&(objectClass=person)(sAMAccountName={0}))"
            GroupFilter = "(&(objectClass=group)(member={0}))"
            ConnectionTimeout = 30
            SearchTimeout = 30
            RequireSSL = $true
            CertificateValidation = @{
                ValidateCertificate = $true
                TrustedCA = "#{LDAP_CA_CERTIFICATE}#"
            }
        }

        $config.Authentication | Add-Member -NotePropertyName "LDAP" -NotePropertyValue $ldapConfig -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile
        Write-Log "LDAP configuration updated successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure LDAP: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-OIDCConfiguration {
    Write-Log "Configuring OAuth2/OIDC authentication..."

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Ensure Authentication section exists
        if (-not $config.Authentication) {
            $config | Add-Member -NotePropertyName "Authentication" -NotePropertyValue @{}
        }

        # Configure OIDC settings
        $oidcConfig = @{
            Enabled = $true
            Authority = "https://login.microsoftonline.com/{tenant-id}"
            ClientId = "#{AZURE_AD_CLIENT_ID}#"
            ClientSecret = "#{AZURE_AD_CLIENT_SECRET}#"
            ResponseType = "code"
            Scope = "openid profile email"
            RequireHttpsMetadata = $true
            SaveTokens = $true
            TokenValidationParameters = @{
                ValidateIssuer = $true
                ValidateAudience = $true
                ValidateLifetime = $true
                ClockSkew = "00:05:00"
            }
        }

        $config.Authentication | Add-Member -NotePropertyName "OpenIdConnect" -NotePropertyValue $oidcConfig -Force

        # Update Orleans Dashboard authentication
        if (-not $config.Orleans) {
            $config | Add-Member -NotePropertyName "Orleans" -NotePropertyValue @{}
        }
        if (-not $config.Orleans.Dashboard) {
            $config.Orleans | Add-Member -NotePropertyName "Dashboard" -NotePropertyValue @{}
        }

        $config.Orleans.Dashboard | Add-Member -NotePropertyName "Authentication" -NotePropertyValue @{
            Provider = "OpenIdConnect"
            RequireAuthentication = $true
            AuthorizedRoles = @("Orleans.Admins", "Orleans.Operators")
        } -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile
        Write-Log "OAuth2/OIDC configuration updated successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure OIDC: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-RBACConfiguration {
    Write-Log "Configuring Role-Based Access Control (RBAC)..."

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Ensure Authorization section exists
        if (-not $config.Authorization) {
            $config | Add-Member -NotePropertyName "Authorization" -NotePropertyValue @{}
        }

        # Configure RBAC roles
        $rolesConfig = @{
            "Orleans.SuperAdmins" = @{
                Description = "Full Orleans system administration"
                Permissions = @(
                    "Dashboard.FullAccess",
                    "Configuration.ReadWrite",
                    "Grains.FullManagement",
                    "Metrics.FullAccess",
                    "Logs.FullAccess"
                )
                Groups = @("Domain Admins", "Orleans Administrators")
            }
            "Orleans.Admins" = @{
                Description = "Orleans system administration"
                Permissions = @(
                    "Dashboard.Access",
                    "Configuration.Read",
                    "Grains.Management",
                    "Metrics.Access"
                )
                Groups = @("Orleans Admins", "System Operators")
            }
            "Orleans.Operators" = @{
                Description = "Orleans operational tasks"
                Permissions = @(
                    "Dashboard.View",
                    "Grains.View",
                    "Metrics.View"
                )
                Groups = @("Operations Team", "DevOps Engineers")
            }
            "Orleans.Readers" = @{
                Description = "Read-only Orleans access"
                Permissions = @(
                    "Dashboard.ReadOnly",
                    "Metrics.ReadOnly"
                )
                Groups = @("Developers", "Support Team")
            }
        }

        $config.Authorization | Add-Member -NotePropertyName "Roles" -NotePropertyValue $rolesConfig -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile
        Write-Log "RBAC configuration updated successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure RBAC: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Test-AuthenticationConfiguration {
    Write-Log "Testing authentication configuration..."

    try {
        # Test LDAP connectivity
        Write-Log "Testing LDAP connectivity..."
        $ldapServer = "your-domain-controller.company.com"
        $ldapPort = 636

        $tcpClient = New-Object System.Net.Sockets.TcpClient
        $tcpClient.Connect($ldapServer, $ldapPort)

        if ($tcpClient.Connected) {
            Write-Log "LDAP connectivity test successful" "SUCCESS"
            $tcpClient.Close()
        }

        # Test configuration file syntax
        $config = Get-Content $ConfigFile | ConvertFrom-Json
        if ($config.Authentication -and $config.Authorization) {
            Write-Log "Configuration file syntax validation successful" "SUCCESS"
        }

        Write-Log "Authentication configuration tests completed successfully" "SUCCESS"
    }
    catch {
        Write-Log "Authentication configuration test failed: $($_.Exception.Message)" "WARN"
    }
}

# Main execution
try {
    Write-Log "Starting Orleans enterprise authentication configuration..."
    Write-Log "Environment: $Environment"
    Write-Log "Config File: $ConfigFile"
    if ($KeyVaultName) {
        Write-Log "Key Vault: $KeyVaultName"
    }

    # Step 1: Check prerequisites
    Test-Prerequisites

    # Step 2: Create service account
    $serviceAccount = New-OrleansServiceAccount

    # Step 3: Configure LDAP authentication
    Set-LDAPConfiguration -ServiceAccount $serviceAccount

    # Step 4: Configure OAuth2/OIDC authentication
    Set-OIDCConfiguration

    # Step 5: Configure RBAC
    Set-RBACConfiguration

    # Step 6: Test configuration
    Test-AuthenticationConfiguration

    Write-Log "Orleans enterprise authentication configuration completed successfully!" "SUCCESS"
    Write-Log "Service Account: $($serviceAccount.AccountName)" "SUCCESS"
    Write-Log "Please restart Orleans services to apply the new authentication configuration." "WARN"

    # Display next steps
    Write-Log "`nNext Steps:" "SUCCESS"
    Write-Log "1. Update tenant-id in OIDC configuration"
    Write-Log "2. Create Azure AD application registration"
    Write-Log "3. Configure client ID and secret in Key Vault"
    Write-Log "4. Test authentication with different user roles"
    Write-Log "5. Restart Orleans services"
}
catch {
    Write-Log "Enterprise authentication configuration failed: $($_.Exception.Message)" "ERROR"
    exit 1
}