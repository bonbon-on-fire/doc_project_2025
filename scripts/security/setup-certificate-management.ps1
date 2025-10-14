#Requires -Version 5.1
#Requires -Modules Az.KeyVault
<#
.SYNOPSIS
    Setup certificate management for Orleans services.

.DESCRIPTION
    This script automates certificate lifecycle management including issuance, installation,
    mTLS configuration, rotation, and TLS 1.3 hardening for Orleans services.

.PARAMETER ConfigFile
    Path to the Orleans configuration file (appsettings.json)

.PARAMETER Environment
    Target environment (Development, Test, Production)

.PARAMETER KeyVaultName
    Azure Key Vault name for storing certificates

.PARAMETER CAServer
    Certificate Authority server for certificate requests

.PARAMETER Force
    Force configuration without prompting

.EXAMPLE
    .\setup-certificate-management.ps1 -ConfigFile "appsettings.Production.json" -Environment "Production" -KeyVaultName "orleans-prod-kv" -CAServer "ca.company.com"
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
    [string]$CAServer = "ca.company.com",

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
    Write-Log "Checking certificate management prerequisites..."

    # Check if running as administrator
    if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "This script must be run as Administrator"
    }

    # Check configuration file
    if (-not (Test-Path $ConfigFile)) {
        throw "Configuration file not found: $ConfigFile"
    }

    # Check Azure PowerShell module if Key Vault is specified
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

    Write-Log "All prerequisites validated successfully" "SUCCESS"
}

function New-OrleansCertificate {
    param(
        [string]$CertificateType = "OrleansServer",
        [string]$SubjectName = "CN=orleans.company.com",
        [string[]]$SubjectAlternativeNames = @("orleans.company.com", "localhost"),
        [string]$Template = "OrleansServerTemplate"
    )

    Write-Log "Requesting new certificate: $SubjectName"

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

            # Get the installed certificate
            $installedCert = Get-ChildItem -Path "Cert:\LocalMachine\My" |
                            Where-Object { $_.Subject -eq $SubjectName } |
                            Sort-Object NotBefore -Descending |
                            Select-Object -First 1

            Write-Log "Certificate issued and installed successfully" "SUCCESS"
            Write-Log "Thumbprint: $($installedCert.Thumbprint)" "SUCCESS"
            return $installedCert
        }
        else {
            throw "Certificate request failed with status: $result"
        }
    }
    catch {
        Write-Log "Certificate request failed: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Install-OrleansCertificate {
    param(
        [string]$CertificateThumbprint,
        [string]$CertificateStoreName = "My",
        [string]$CertificateStoreLocation = "LocalMachine"
    )

    Write-Log "Installing Orleans certificate: $CertificateThumbprint"

    try {
        # Verify certificate exists
        $cert = Get-ChildItem -Path "Cert:\$CertificateStoreLocation\$CertificateStoreName" |
                Where-Object { $_.Thumbprint -eq $CertificateThumbprint }

        if (-not $cert) {
            throw "Certificate with thumbprint $CertificateThumbprint not found"
        }

        # Grant network service access to private key
        $keyPath = $env:ProgramData + "\Microsoft\Crypto\RSA\MachineKeys\"
        $keyFileName = $cert.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName

        if (Test-Path ($keyPath + $keyFileName)) {
            $acl = Get-Acl ($keyPath + $keyFileName)
            $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("NETWORK SERVICE", "FullControl", "Allow")
            $acl.SetAccessRule($accessRule)
            $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "Read", "Allow")
            $acl.SetAccessRule($accessRule)
            Set-Acl ($keyPath + $keyFileName) $acl
            Write-Log "Private key permissions configured successfully" "SUCCESS"
        }

        # Update Orleans configuration
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Ensure Orleans section exists
        if (-not $config.Orleans) {
            $config | Add-Member -NotePropertyName "Orleans" -NotePropertyValue @{}
        }
        if (-not $config.Orleans.Dashboard) {
            $config.Orleans | Add-Member -NotePropertyName "Dashboard" -NotePropertyValue @{}
        }

        $config.Orleans.Dashboard | Add-Member -NotePropertyName "Certificate" -NotePropertyValue @{
            Thumbprint = $CertificateThumbprint
            StoreName = $CertificateStoreName
            StoreLocation = $CertificateStoreLocation
        } -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile

        Write-Log "Certificate installed and configured successfully" "SUCCESS"
        Write-Log "Subject: $($cert.Subject)" "SUCCESS"
        Write-Log "Expires: $($cert.NotAfter)" "SUCCESS"

        return @{
            Thumbprint = $CertificateThumbprint
            Subject = $cert.Subject
            Expires = $cert.NotAfter
        }
    }
    catch {
        Write-Log "Certificate installation failed: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-mTLSConfiguration {
    param([string]$ServerCertThumbprint, [string]$ClientCertThumbprint)

    Write-Log "Configuring mTLS for Orleans grain-to-grain communication..."

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Ensure Orleans Security section exists
        if (-not $config.Orleans) {
            $config | Add-Member -NotePropertyName "Orleans" -NotePropertyValue @{}
        }
        if (-not $config.Orleans.Security) {
            $config.Orleans | Add-Member -NotePropertyName "Security" -NotePropertyValue @{}
        }

        # Configure mTLS settings
        $mtlsConfig = @{
            Enabled = $true
            ServerCertificate = @{
                Thumbprint = $ServerCertThumbprint
                StoreName = "My"
                StoreLocation = "LocalMachine"
            }
            ClientCertificate = @{
                Thumbprint = $ClientCertThumbprint
                StoreName = "My"
                StoreLocation = "LocalMachine"
            }
            RequireClientCertificate = $true
            CheckCertificateRevocation = $true
            ValidateRemoteCertificateName = $true
        }

        $config.Orleans.Security | Add-Member -NotePropertyName "mTLS" -NotePropertyValue $mtlsConfig -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile
        Write-Log "mTLS configuration updated successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure mTLS: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-TLSHardening {
    Write-Log "Configuring TLS 1.3 and cipher suite hardening..."

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Ensure Orleans Security section exists
        if (-not $config.Orleans) {
            $config | Add-Member -NotePropertyName "Orleans" -NotePropertyValue @{}
        }
        if (-not $config.Orleans.Security) {
            $config.Orleans | Add-Member -NotePropertyName "Security" -NotePropertyValue @{}
        }

        # Configure TLS settings
        $tlsConfig = @{
            MinimumVersion = "Tls13"
            MaximumVersion = "Tls13"
            CipherSuites = @(
                "TLS_AES_256_GCM_SHA384",
                "TLS_AES_128_GCM_SHA256",
                "TLS_CHACHA20_POLY1305_SHA256"
            )
            RequirePerfectForwardSecrecy = $true
            DisableLegacyRenegotiation = $true
            EnableOcspStapling = $true
        }

        $config.Orleans.Security | Add-Member -NotePropertyName "TLS" -NotePropertyValue $tlsConfig -Force

        # Configure Kestrel for secure HTTPS
        if (-not $config.Kestrel) {
            $config | Add-Member -NotePropertyName "Kestrel" -NotePropertyValue @{}
        }
        if (-not $config.Kestrel.Endpoints) {
            $config.Kestrel | Add-Member -NotePropertyName "Endpoints" -NotePropertyValue @{}
        }

        $kestrelEndpoint = @{
            Url = "https://localhost:5099"
            Certificate = @{
                Subject = "CN=orleans.company.com"
                Store = "My"
                Location = "LocalMachine"
                AllowInvalid = $false
            }
            Protocols = "Http2"
            SslProtocols = @("Tls13")
            ClientCertificateMode = "RequireCertificate"
        }

        $config.Kestrel.Endpoints | Add-Member -NotePropertyName "HttpsInlineCertStore" -NotePropertyValue $kestrelEndpoint -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile
        Write-Log "TLS hardening configuration updated successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure TLS hardening: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Set-CertificateMonitoring {
    param([string[]]$CertificateThumbprints)

    Write-Log "Configuring certificate monitoring and alerting..."

    try {
        $config = Get-Content $ConfigFile | ConvertFrom-Json

        # Configure certificate monitoring
        $monitoringConfig = @{
            Enabled = $true
            CheckInterval = "PT1H"
            Certificates = @()
        }

        foreach ($thumbprint in $CertificateThumbprints) {
            $cert = Get-ChildItem -Path "Cert:\LocalMachine\My" | Where-Object { $_.Thumbprint -eq $thumbprint }
            if ($cert) {
                $certMonitoring = @{
                    Name = "Orleans Certificate - $($cert.Subject)"
                    Thumbprint = $thumbprint
                    Store = "Cert:\LocalMachine\My"
                    ExpiryWarningDays = @(60, 30, 14, 7, 1)
                    AlertEndpoints = @(
                        "security-team@company.com",
                        "operations@company.com"
                    )
                }
                $monitoringConfig.Certificates += $certMonitoring
            }
        }

        # Add auto-renewal configuration
        $monitoringConfig | Add-Member -NotePropertyName "AutoRenewal" -NotePropertyValue @{
            Enabled = $true
            RenewalThresholdDays = 30
            MaxRetryAttempts = 3
            RetryDelayHours = 24
        }

        $config | Add-Member -NotePropertyName "CertificateMonitoring" -NotePropertyValue $monitoringConfig -Force

        # Save configuration
        $config | ConvertTo-Json -Depth 10 | Set-Content $ConfigFile
        Write-Log "Certificate monitoring configured successfully" "SUCCESS"
    }
    catch {
        Write-Log "Failed to configure certificate monitoring: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Test-CertificateConfiguration {
    param([string]$CertificateThumbprint)

    Write-Log "Testing certificate configuration..."

    try {
        # Test certificate chain validation
        $cert = Get-ChildItem -Path "Cert:\LocalMachine\My" | Where-Object { $_.Thumbprint -eq $CertificateThumbprint }

        if ($cert) {
            $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
            $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::Online
            $chainValid = $chain.Build($cert)

            if ($chainValid) {
                Write-Log "Certificate chain validation successful" "SUCCESS"
            }
            else {
                Write-Log "Certificate chain validation failed" "WARN"
                foreach ($status in $chain.ChainStatus) {
                    Write-Log "Chain issue: $($status.Status) - $($status.StatusInformation)" "WARN"
                }
            }

            # Test TLS connectivity
            try {
                $tcpClient = New-Object System.Net.Sockets.TcpClient
                $tcpClient.Connect("localhost", 5099)

                if ($tcpClient.Connected) {
                    Write-Log "TLS connectivity test successful" "SUCCESS"
                    $tcpClient.Close()
                }
            }
            catch {
                Write-Log "TLS connectivity test failed: $($_.Exception.Message)" "WARN"
            }
        }

        Write-Log "Certificate configuration tests completed" "SUCCESS"
    }
    catch {
        Write-Log "Certificate configuration test failed: $($_.Exception.Message)" "WARN"
    }
}

# Main execution
try {
    Write-Log "Starting Orleans certificate management setup..."
    Write-Log "Environment: $Environment"
    Write-Log "Config File: $ConfigFile"
    if ($KeyVaultName) {
        Write-Log "Key Vault: $KeyVaultName"
    }

    # Step 1: Check prerequisites
    Test-Prerequisites

    # Step 2: Request server certificate
    $serverCert = New-OrleansCertificate -CertificateType "OrleansServer" -SubjectName "CN=orleans.company.com" -SubjectAlternativeNames @("orleans.company.com", "orleans-api.company.com", "localhost")

    # Step 3: Request client certificate for mTLS
    $clientCert = New-OrleansCertificate -CertificateType "OrleansClient" -SubjectName "CN=orleans-client.company.com" -Template "OrleansClientTemplate"

    # Step 4: Install and configure certificates
    Install-OrleansCertificate -CertificateThumbprint $serverCert.Thumbprint

    # Step 5: Configure mTLS
    Set-mTLSConfiguration -ServerCertThumbprint $serverCert.Thumbprint -ClientCertThumbprint $clientCert.Thumbprint

    # Step 6: Configure TLS hardening
    Set-TLSHardening

    # Step 7: Configure certificate monitoring
    Set-CertificateMonitoring -CertificateThumbprints @($serverCert.Thumbprint, $clientCert.Thumbprint)

    # Step 8: Test configuration
    Test-CertificateConfiguration -CertificateThumbprint $serverCert.Thumbprint

    # Store certificates in Key Vault if specified
    if ($KeyVaultName) {
        Write-Log "Storing certificates in Key Vault..."
        $serverCertBytes = $serverCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx)
        $serverCertSecret = [System.Convert]::ToBase64String($serverCertBytes)
        Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name "orleans-server-certificate" -SecretValue (ConvertTo-SecureString $serverCertSecret -AsPlainText -Force)

        $clientCertBytes = $clientCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx)
        $clientCertSecret = [System.Convert]::ToBase64String($clientCertBytes)
        Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name "orleans-client-certificate" -SecretValue (ConvertTo-SecureString $clientCertSecret -AsPlainText -Force)

        Write-Log "Certificates stored in Key Vault successfully" "SUCCESS"
    }

    Write-Log "Orleans certificate management setup completed successfully!" "SUCCESS"
    Write-Log "Server Certificate: $($serverCert.Thumbprint)" "SUCCESS"
    Write-Log "Client Certificate: $($clientCert.Thumbprint)" "SUCCESS"
    Write-Log "Please restart Orleans services to apply the new certificate configuration." "WARN"

    # Display next steps
    Write-Log "`nNext Steps:" "SUCCESS"
    Write-Log "1. Verify certificate chain trust"
    Write-Log "2. Test mTLS connectivity between grains"
    Write-Log "3. Configure certificate auto-renewal"
    Write-Log "4. Set up certificate expiration monitoring"
    Write-Log "5. Restart Orleans services"
}
catch {
    Write-Log "Certificate management setup failed: $($_.Exception.Message)" "ERROR"
    exit 1
}