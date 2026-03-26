# LocalTelegram SSL Certificate Setup Script
# Supports Let's Encrypt and self-signed certificates

param(
    [string]$Action = "self-signed",  # self-signed, letsencrypt, renew, status
    [string]$Domain = "localhost",
    [string]$Email = "",
    [string]$OutputPath = "./certs",
    [int]$Days = 365,
    [bool]$DryRun = $false
)

$ErrorActionPreference = "Stop"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "INFO" { "White" }
        "SUCCESS" { "Green" }
        "WARNING" { "Yellow" }
        "ERROR" { "Red" }
        default { "White" }
    }
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

function Test-Command {
    param([string]$Command)
    $null = Get-Command $Command -ErrorAction SilentlyContinue
    return $?
}

function New-OutputDirectory {
    if (-not (Test-Path $OutputPath)) {
        Write-Log "Creating output directory: $OutputPath" -Level "INFO"
        if (-not $DryRun) {
            New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
        }
    }
}

function New-SelfSignedCertificateSet {
    Write-Log "Generating self-signed SSL certificate..." -Level "INFO"
    Write-Log "Domain: $Domain" -Level "INFO"
    Write-Log "Validity: $Days days" -Level "INFO"
    
    New-OutputDirectory
    
    $certPath = Join-Path $OutputPath "$Domain.crt"
    $keyPath = Join-Path $OutputPath "$Domain.key"
    $pfxPath = Join-Path $OutputPath "$Domain.pfx"
    
    if ($DryRun) {
        Write-Log "[DRY RUN] Would generate:" -Level "INFO"
        Write-Log "  - $certPath" -Level "INFO"
        Write-Log "  - $keyPath" -Level "INFO"
        Write-Log "  - $pfxPath" -Level "INFO"
        return
    }
    
    # Generate using OpenSSL if available, otherwise use PowerShell
    if (Test-Command "openssl") {
        Write-Log "Using OpenSSL for certificate generation..." -Level "INFO"
        
        # Generate private key
        openssl genrsa -out $keyPath 2048 2>$null
        
        # Generate CSR config
        $configContent = @"
[req]
default_bits = 2048
prompt = no
default_md = sha256
distinguished_name = dn
x509_extensions = v3_req

[dn]
C = RU
ST = Moscow
L = Moscow
O = LocalTelegram
OU = IT
CN = $Domain

[v3_req]
subjectAltName = @alt_names

[alt_names]
DNS.1 = $Domain
DNS.2 = localhost
DNS.3 = *.localtelegram.local
IP.1 = 127.0.0.1
"@
        
        $configPath = Join-Path $OutputPath "openssl.cnf"
        $configContent | Out-File -FilePath $configPath -Encoding ASCII
        
        # Generate self-signed certificate
        openssl req -new -x509 -days $Days -key $keyPath -out $certPath -config $configPath 2>$null
        
        # Generate PFX for Windows/IIS
        openssl pkcs12 -export -out $pfxPath -inkey $keyPath -in $certPath -passout pass: 2>$null
        
        # Cleanup
        Remove-Item $configPath -ErrorAction SilentlyContinue
    }
    else {
        Write-Log "Using PowerShell for certificate generation..." -Level "INFO"
        
        # Generate using PowerShell
        $cert = New-SelfSignedCertificate `
            -DnsName @($Domain, "localhost", "*.localtelegram.local") `
            -CertStoreLocation "Cert:\LocalMachine\My" `
            -FriendlyName "LocalTelegram SSL Certificate" `
            -NotAfter (Get-Date).AddDays($Days) `
            -KeyExportPolicy Exportable
        
        # Export to PFX
        $pwd = ConvertTo-SecureString -String "LocalTelegram123" -Force -AsPlainText
        Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pwd
        
        # Export public key
        $certPath = Join-Path $OutputPath "$Domain.cer"
        Export-Certificate -Cert $cert -FilePath $certPath -Type CERT
        
        Write-Log "Certificate thumbprint: $($cert.Thumbprint)" -Level "INFO"
        Write-Log "PFX Password: LocalTelegram123" -Level "WARNING"
    }
    
    Write-Log "Certificate generated successfully!" -Level "SUCCESS"
    Write-Log "  Certificate: $certPath" -Level "INFO"
    Write-Log "  Private Key: $keyPath" -Level "INFO"
    Write-Log "  PFX (Windows): $pfxPath" -Level "INFO"
}

function Get-LetsEncryptCertificate {
    Write-Log "Requesting Let's Encrypt certificate..." -Level "INFO"
    Write-Log "Domain: $Domain" -Level "INFO"
    Write-Log "Email: $Email" -Level "INFO"
    
    if ([string]::IsNullOrEmpty($Email)) {
        Write-Log "Email is required for Let's Encrypt certificates!" -Level "ERROR"
        Write-Log "Usage: .\setup-ssl.ps1 -Action letsencrypt -Domain your.domain.com -Email admin@your.domain.com" -Level "ERROR"
        exit 1
    }
    
    New-OutputDirectory
    
    # Check for certbot
    if (-not (Test-Command "certbot")) {
        Write-Log "certbot not found. Installing via winget..." -Level "WARNING"
        if (-not $DryRun) {
            winget install -e --id Certbot.Certbot
        }
    }
    
    if ($DryRun) {
        Write-Log "[DRY RUN] Would request Let's Encrypt certificate for $Domain" -Level "INFO"
        return
    }
    
    # Request certificate using certbot
    $certbotArgs = @(
        "certonly",
        "--standalone",
        "-d", $Domain,
        "--email", $Email,
        "--agree-tos",
        "--no-eff-email",
        "--cert-name", "localtelegram"
    )
    
    & certbot $certbotArgs
    
    if ($LASTEXITCODE -eq 0) {
        Write-Log "Certificate obtained successfully!" -Level "SUCCESS"
        
        # Copy certificates to output path
        $letsencryptPath = "C:\etc\letsencrypt\live\$Domain"
        if (Test-Path $letsencryptPath) {
            Copy-Item "$letsencryptPath\fullchain.pem" (Join-Path $OutputPath "$Domain.crt")
            Copy-Item "$letsencryptPath\privkey.pem" (Join-Path $OutputPath "$Domain.key")
            Write-Log "Certificates copied to: $OutputPath" -Level "INFO"
        }
    }
    else {
        Write-Log "Failed to obtain certificate!" -Level "ERROR"
        exit 1
    }
}

function Renew-Certificates {
    Write-Log "Renewing Let's Encrypt certificates..." -Level "INFO"
    
    if (-not (Test-Command "certbot")) {
        Write-Log "certbot not found!" -Level "ERROR"
        exit 1
    }
    
    if ($DryRun) {
        Write-Log "[DRY RUN] Would renew certificates" -Level "INFO"
        certbot renew --dry-run
        return
    }
    
    certbot renew
    
    # Reload nginx if running
    $nginx = Get-Process -Name "nginx" -ErrorAction SilentlyContinue
    if ($nginx) {
        Write-Log "Reloading nginx..." -Level "INFO"
        nginx -s reload
    }
    
    Write-Log "Certificate renewal complete!" -Level "SUCCESS"
}

function Get-CertificateStatus {
    Write-Log "Checking SSL certificate status..." -Level "INFO"
    
    # Check self-signed certificates
    $certFiles = Get-ChildItem -Path $OutputPath -Filter "*.crt" -ErrorAction SilentlyContinue
    if ($certFiles) {
        Write-Log "`nSelf-signed/Custom certificates in $OutputPath`:" -Level "INFO"
        foreach ($file in $certFiles) {
            Write-Log "  - $($file.Name)" -Level "INFO"
        }
    }
    
    # Check Let's Encrypt certificates
    $letsencryptPath = "C:\etc\letsencrypt\live"
    if (Test-Path $letsencryptPath) {
        Write-Log "`nLet's Encrypt certificates:" -Level "INFO"
        Get-ChildItem -Path $letsencryptPath -Directory | ForEach-Object {
            $certPath = Join-Path $_.FullName "cert.pem"
            if (Test-Path $certPath) {
                Write-Log "  - $($_.Name)" -Level "INFO"
            }
        }
    }
    
    # Check Windows certificate store
    Write-Log "`nCertificates in Windows store (LocalMachine\My):" -Level "INFO"
    Get-ChildItem -Path "Cert:\LocalMachine\My" | Where-Object {
        $_.Subject -like "*LocalTelegram*" -or $_.Subject -like "*$Domain*"
    } | ForEach-Object {
        Write-Log "  - $($_.Subject)" -Level "INFO"
        Write-Log "    Expires: $($_.NotAfter)" -Level "INFO"
        Write-Log "    Thumbprint: $($_.Thumbprint)" -Level "INFO"
    }
}

function New-NginxSSLConfig {
    Write-Log "Generating nginx SSL configuration..." -Level "INFO"
    
    $nginxSSLConfig = @"
# SSL Configuration for LocalTelegram
# Include this in your nginx server block

ssl_certificate /etc/nginx/ssl/$Domain.crt;
ssl_certificate_key /etc/nginx/ssl/$Domain.key;

# Modern SSL configuration
ssl_protocols TLSv1.2 TLSv1.3;
ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384;
ssl_prefer_server_ciphers off;
ssl_session_cache shared:SSL:10m;
ssl_session_timeout 1d;
ssl_session_tickets off;

# HSTS
add_header Strict-Transport-Security "max-age=63072000" always;

# OCSP Stapling
ssl_stapling on;
ssl_stapling_verify on;
"@
    
    $configPath = Join-Path $OutputPath "nginx-ssl.conf"
    if (-not $DryRun) {
        $nginxSSLConfig | Out-File -FilePath $configPath -Encoding ASCII
        Write-Log "Nginx SSL config saved to: $configPath" -Level "SUCCESS"
    }
    else {
        Write-Log "[DRY RUN] Would create: $configPath" -Level "INFO"
    }
}

function Show-Help {
    Write-Host @"

LocalTelegram SSL Certificate Setup Script
===========================================

Usage: .\setup-ssl.ps1 -Action <action> [options]

Actions:
    self-signed    Generate a self-signed certificate (default)
    letsencrypt    Request a Let's Encrypt certificate
    renew          Renew Let's Encrypt certificates
    status         Show certificate status

Options:
    -Domain        Domain name (default: localhost)
    -Email         Email for Let's Encrypt (required for letsencrypt)
    -OutputPath    Output directory for certificates (default: ./certs)
    -Days          Validity in days for self-signed (default: 365)
    -DryRun        Preview changes without executing

Examples:
    # Generate self-signed certificate for localhost
    .\setup-ssl.ps1 -Action self-signed

    # Generate self-signed certificate for custom domain
    .\setup-ssl.ps1 -Action self-signed -Domain messenger.company.local -Days 730

    # Request Let's Encrypt certificate
    .\setup-ssl.ps1 -Action letsencrypt -Domain messenger.company.com -Email admin@company.com

    # Renew Let's Encrypt certificates
    .\setup-ssl.ps1 -Action renew

    # Check certificate status
    .\setup-ssl.ps1 -Action status

Output files:
    - <domain>.crt  - SSL certificate (PEM format)
    - <domain>.key  - Private key (PEM format)
    - <domain>.pfx  - PFX format for Windows/IIS
    - nginx-ssl.conf - Nginx SSL configuration snippet

"@
}

# Main execution
switch ($Action.ToLower()) {
    "self-signed" { 
        New-SelfSignedCertificateSet
        New-NginxSSLConfig
    }
    "letsencrypt" { 
        Get-LetsEncryptCertificate
        New-NginxSSLConfig
    }
    "renew" { Renew-Certificates }
    "status" { Get-CertificateStatus }
    "help" { Show-Help }
    default { Show-Help }
}
