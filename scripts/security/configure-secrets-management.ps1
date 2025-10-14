#Requires -Version 5.1
#Requires -Modules Az.KeyVault, Az.Accounts
<#
.SYNOPSIS
    Configure secrets management for Orleans services.

.DESCRIPTION
    This script automates the setup of secrets management including Azure Key Vault integration,
    HashiCorp Vault configuration, secret rotation, and connection string security.

.PARAMETER ConfigFile
    Path to the Orleans configuration file (appsettings.json)

.PARAMETER Environment
    Target environment (Development, Test, Production)

.PARAMETER KeyVaultName
    Azure Key Vault name for storing secrets

.PARAMETER VaultProvider
    Secrets management provider (AzureKeyVault, HashiCorpVault)

.PARAMETER Force
    Force configuration without prompting

.EXAMPLE
    .\configure-secrets-management.ps1 -ConfigFile "appsettings.Production.json" -Environment "Production" -KeyVaultName "orleans-prod-kv" -VaultProvider "AzureKeyVault"
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
    [ValidateSet("AzureKeyVault", "HashiCorpVault")]
    [string]$VaultProvider = "AzureKeyVault",

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $false)]
    [string]$ServicePrincipalName = "orleans-service-principal",

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
    Write-Log "Checking secrets management prerequisites..."

    # Check if running as administrator
    if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "This script must be run as Administrator"
    }

    # Check configuration file
    if (-not (Test-Path $ConfigFile)) {
        throw "Configuration file not found: $ConfigFile"
    }

    # Check Azure PowerShell modules
    if ($VaultProvider -eq "AzureKeyVault") {
        try {
            Import-Module Az.KeyVault -ErrorAction Stop
            Import-Module Az.Accounts -ErrorAction Stop
            Write-Log "Azure PowerShell modules loaded successfully" "SUCCESS"
        }
        catch {
            Write-Log "Azure PowerShell modules not found. Please install: Install-Module Az" "ERROR"
            throw "Required Azure PowerShell modules not available"
        }
    }

    Write-Log "All prerequisites validated successfully" "SUCCESS"
}

function New-AzureKeyVault {
    param(
        [string]$VaultName,
        [string]$ResourceGroup,
        [string]$Location = "East US"
    )

    Write-Log "Creating Azure Key Vault: $VaultName"

    try {
        # Create resource group if it doesn't exist
        $rg = Get-AzResourceGroup -Name $ResourceGroup -ErrorAction SilentlyContinue
        if (-not $rg) {
            $rg = New-AzResourceGroup -Name $ResourceGroup -Location $Location
            Write-Log "Created resource group: $ResourceGroup" "SUCCESS"
        }

        # Create Key Vault
        $keyVault = Get-AzKeyVault -VaultName $VaultName -ErrorAction SilentlyContinue
        if (-not $keyVault) {
            $keyVault = New-AzKeyVault -Name $VaultName -ResourceGroupName $ResourceGroup -Location $Location -EnabledForDeployment -EnabledForTemplateDeployment -EnableSoftDelete -SoftDeleteRetentionInDays 90
            Write-Log "Created Key Vault: $VaultName" "SUCCESS"
        }
        else {
            Write-Log "Key Vault already exists: $VaultName" "WARN"
        }

        # Create service principal for Orleans
        $sp = Get-AzADServicePrincipal -DisplayName $ServicePrincipalName -ErrorAction SilentlyContinue
        if (-not $sp) {
            $sp = New-AzADServicePrincipal -DisplayName $ServicePrincipalName
            Write-Log "Created service principal: $ServicePrincipalName" "SUCCESS"
        }

        # Grant Key Vault access to service principal
        Set-AzKeyVaultAccessPolicy -VaultName $VaultName -ServicePrincipalName $sp.AppId -PermissionsToSecrets Get,List,Set,Delete -PermissionsToCertificates Get,List,Import,Update

        Write-Log "Key Vault created and configured successfully" "SUCCESS"

        return @{
            VaultName = $VaultName
            ServicePrincipalId = $sp.AppId
            ServicePrincipalSecret = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($sp.PasswordCredentials[0].SecretText))
            TenantId = (Get-AzContext).Tenant.Id
        }
    }
    catch {
        Write-Log "Failed to create Azure Key Vault: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-AzureKeyVaultConfiguration {
    param([hashtable]$VaultInfo)

    Write-Log "Configuring Azure Key Vault integration..."

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Configure Azure Key Vault settings
        $keyVaultConfig = @{
            Enabled = $true
            VaultName = $VaultInfo.VaultName
            Authentication = @{
                Type = "ServicePrincipal"
                TenantId = $VaultInfo.TenantId
                ClientId = $VaultInfo.ServicePrincipalId
                ClientSecret = "#{AZURE_CLIENT_SECRET}#"
            }
            CacheEnabled = $true
            CacheTTL = "PT15M"
            Secrets = @{
                DatabaseConnectionString = "database-connection-string"
                LDAPServicePassword = "ldap-service-password"
                OrleansServerCertificate = "orleans-server-certificate"
                OrleansClientCertificate = "orleans-client-certificate"
                OIDCClientSecret = "oidc-client-secret"
            }
        }

        $config | Add-Member -NotePropertyName "AzureKeyVault" -NotePropertyValue $keyVaultConfig -Force

        # Update connection strings to use Key Vault references
        if (-not $config.ConnectionStrings) {
            $config | Add-Member -NotePropertyName "ConnectionStrings" -NotePropertyValue @{}
        }

        $config.ConnectionStrings | Add-Member -NotePropertyName "Database" -NotePropertyValue "#{database-connection-string}#" -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile

        Write-Log "Azure Key Vault configuration updated successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure Azure Key Vault: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-HashiCorpVaultConfiguration {
    param(
        [string]$VaultAddress = "https://vault.company.com:8200"
    )

    Write-Log "Configuring HashiCorp Vault integration..."

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Configure HashiCorp Vault settings
        $vaultConfig = @{
            Enabled = $true
            Address = $VaultAddress
            Authentication = @{
                Type = "AppRole"
                RoleId = "#{VAULT_ROLE_ID}#"
                SecretId = "#{VAULT_SECRET_ID}#"
            }
            KvVersion = "v2"
            SecretsPath = "secret/orleans/"
            CertificatesPath = "pki/orleans/"
            TTL = "24h"
            MaxTTL = "720h"
        }

        $config | Add-Member -NotePropertyName "HashiCorpVault" -NotePropertyValue $vaultConfig -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile

        Write-Log "HashiCorp Vault configuration updated successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure HashiCorp Vault: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Initialize-OrleansSecrets {
    param(
        [string]$VaultName,
        [hashtable]$VaultInfo
    )

    Write-Log "Initializing Orleans secrets in vault..."

    try {
        $secrets = @{
            "database-connection-string" = "Data Source=aichat.db;Cache=Shared;Connection Timeout=30;"
            "ldap-service-password" = [System.Web.Security.Membership]::GeneratePassword(32, 8)
            "oidc-client-secret" = [System.Web.Security.Membership]::GeneratePassword(48, 12)
            "orleans-api-key" = [System.Guid]::NewGuid().ToString("N").ToUpper()
        }

        foreach ($secretName in $secrets.Keys) {
            $secretValue = ConvertTo-SecureString $secrets[$secretName] -AsPlainText -Force

            # Add to Key Vault
            Set-AzKeyVaultSecret -VaultName $VaultName -Name $secretName -SecretValue $secretValue -Tags @{
                Environment = $Environment
                Component = "Orleans"
                CreatedBy = "SecretManagementScript"
                CreatedDate = (Get-Date).ToString("yyyy-MM-dd")
            }

            Write-Log "Secret '$secretName' stored successfully" "SUCCESS"
        }

        # Store service principal secret
        Set-AzKeyVaultSecret -VaultName $VaultName -Name "azure-client-secret" -SecretValue (ConvertTo-SecureString $VaultInfo.ServicePrincipalSecret -AsPlainText -Force)

        Write-Log "Orleans secrets initialized successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to initialize secrets: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-SecretRotationConfiguration {
    param([string]$VaultName)

    Write-Log "Configuring secret rotation automation..."

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Configure secret rotation settings
        $rotationConfig = @{
            Enabled = $true
            RotationSchedule = "0 2 * * 0" # Weekly on Sunday at 2 AM
            Secrets = @{
                "ldap-service-password" = @{
                    RotationInterval = 90
                    Type = "Password"
                    NotifyOnRotation = $true
                }
                "oidc-client-secret" = @{
                    RotationInterval = 60
                    Type = "ApiKey"
                    NotifyOnRotation = $true
                }
                "orleans-api-key" = @{
                    RotationInterval = 30
                    Type = "ApiKey"
                    NotifyOnRotation = $false
                }
            }
            NotificationEndpoints = @(
                "security-team@company.com",
                "operations@company.com"
            )
        }

        $config | Add-Member -NotePropertyName "SecretRotation" -NotePropertyValue $rotationConfig -Force

        # Configure secure connection strings
        $connectionStringConfig = @{
            EncryptionEnabled = $true
            EncryptionKey = "#{connection-string-encryption-key}#"
            RotationEnabled = $true
            ValidationEnabled = $true
            ValidationInterval = "PT1H"
        }

        $config | Add-Member -NotePropertyName "SecureConnectionStrings" -NotePropertyValue $connectionStringConfig -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile

        Write-Log "Secret rotation configuration updated successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure secret rotation: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function New-SecretRotationScript {
    Write-Log "Creating secret rotation automation script..."

    $rotationScript = @'
# Automated secret rotation script for Orleans
param(
    [string]$VaultName,
    [string]$SecretName,
    [string]$SecretType = "Password",
    [int]$RotationInterval = 90
)

function Invoke-SecretRotation {
    param(
        [string]$VaultName,
        [string]$SecretName,
        [string]$SecretType,
        [int]$RotationInterval
    )

    try {
        # Check current secret age
        $currentSecret = Get-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName
        $secretAge = (Get-Date) - $currentSecret.Attributes.Created

        if ($secretAge.Days -lt $RotationInterval) {
            Write-Host "Secret '$SecretName' is only $($secretAge.Days) days old - rotation not needed"
            return $true
        }

        Write-Host "Starting rotation for secret '$SecretName'..."

        # Generate new secret based on type
        $newSecretValue = switch ($SecretType) {
            "Password" { [System.Web.Security.Membership]::GeneratePassword(32, 8) }
            "ApiKey" { [System.Guid]::NewGuid().ToString("N").ToUpper() }
            default { throw "Unsupported secret type: $SecretType" }
        }

        # Store new secret version
        $secureNewValue = ConvertTo-SecureString $newSecretValue -AsPlainText -Force
        $newSecret = Set-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName -SecretValue $secureNewValue -Tags @{
            RotatedDate = (Get-Date).ToString("yyyy-MM-dd")
            PreviousVersion = $currentSecret.Version
            RotationType = "Automated"
        }

        Write-Host "Secret '$SecretName' rotated successfully"
        Write-Host "Previous Version: $($currentSecret.Version)"
        Write-Host "New Version: $($newSecret.Version)"

        return $true
    }
    catch {
        Write-Error "Secret rotation failed for '$SecretName': $($_.Exception.Message)"
        return $false
    }
}

# Execute rotation
Invoke-SecretRotation -VaultName $VaultName -SecretName $SecretName -SecretType $SecretType -RotationInterval $RotationInterval
'@

    $rotationScriptPath = Join-Path $PSScriptRoot "rotate-orleans-secrets.ps1"
    $rotationScript | Set-Content $rotationScriptPath
    Write-Log "Secret rotation script created: $rotationScriptPath" "SUCCESS"
}

function Test-SecretsConfiguration {
    param([string]$VaultName)

    Write-Log "Testing secrets management configuration..."

    try {
        # Test Key Vault connectivity
        $vault = Get-AzKeyVault -VaultName $VaultName
        if ($vault) {
            Write-Log "Key Vault connectivity test successful" "SUCCESS"
        }

        # Test secret retrieval
        $testSecrets = @("database-connection-string", "ldap-service-password")
        foreach ($secretName in $testSecrets) {
            $secret = Get-AzKeyVaultSecret -VaultName $VaultName -Name $secretName -ErrorAction SilentlyContinue
            if ($secret) {
                Write-Log "Secret '$secretName' retrieval test successful" "SUCCESS"
            }
            else {
                Write-Log "Secret '$secretName' not found in vault" "WARN"
            }
        }

        # Test configuration file syntax
        $config = Get-Content $ConfigFile | ConvertFrom-Json
        if ($config.AzureKeyVault -or $config.HashiCorpVault) {
            Write-Log "Configuration file syntax validation successful" "SUCCESS"
        }

        Write-Log "Secrets management configuration tests completed successfully" "SUCCESS"
    }
    catch {
        Write-Log "Secrets configuration test failed: $($_.Exception.Message)" "WARN"
    }
}

# Main execution
try {
    Write-Log "Starting Orleans secrets management configuration..."
    Write-Log "Environment: $Environment"
    Write-Log "Config File: $ConfigFile"
    Write-Log "Vault Provider: $VaultProvider"
    if ($KeyVaultName) {
        Write-Log "Key Vault: $KeyVaultName"
    }

    # Step 1: Check prerequisites
    Test-Prerequisites

    # Step 2: Configure secrets management based on provider
    switch ($VaultProvider) {
        "AzureKeyVault" {
            if (-not $KeyVaultName) {
                $KeyVaultName = "orleans-$Environment-kv".ToLower()
            }
            if (-not $ResourceGroupName) {
                $ResourceGroupName = "orleans-$Environment-rg".ToLower()
            }

            # Create Azure Key Vault
            $vaultInfo = New-AzureKeyVault -VaultName $KeyVaultName -ResourceGroup $ResourceGroupName

            # Configure Orleans to use Azure Key Vault
            Set-AzureKeyVaultConfiguration -VaultInfo $vaultInfo

            # Initialize secrets
            Initialize-OrleansSecrets -VaultName $KeyVaultName -VaultInfo $vaultInfo
        }

        "HashiCorpVault" {
            # Configure HashiCorp Vault
            Set-HashiCorpVaultConfiguration
        }
    }

    # Step 3: Configure secret rotation
    Set-SecretRotationConfiguration -VaultName $KeyVaultName

    # Step 4: Create rotation automation script
    New-SecretRotationScript

    # Step 5: Test configuration
    if ($VaultProvider -eq "AzureKeyVault") {
        Test-SecretsConfiguration -VaultName $KeyVaultName
    }

    Write-Log "Orleans secrets management configuration completed successfully!" "SUCCESS"
    if ($VaultProvider -eq "AzureKeyVault") {
        Write-Log "Key Vault: $KeyVaultName" "SUCCESS"
        Write-Log "Service Principal: $ServicePrincipalName" "SUCCESS"
    }
    Write-Log "Please restart Orleans services to apply the new secrets configuration." "WARN"

    # Display next steps
    Write-Log "`nNext Steps:" "SUCCESS"
    Write-Log "1. Verify secret rotation schedule configuration"
    Write-Log "2. Test secret retrieval from application"
    Write-Log "3. Configure secret access monitoring"
    Write-Log "4. Set up secret rotation notifications"
    Write-Log "5. Restart Orleans services"
}
catch {
    Write-Log "Secrets management configuration failed: $($_.Exception.Message)" "ERROR"
    exit 1
}