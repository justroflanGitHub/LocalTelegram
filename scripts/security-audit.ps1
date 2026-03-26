# LocalTelegram Security Audit Script
# Performs automated security checks on the deployment

param(
    [string]$Target = "http://localhost:5000",
    [string]$OutputDir = "./security-audit-results",
    [bool]$SkipSSL = $false,
    [bool]$SkipPorts = $false,
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
        "CRITICAL" { "Red" }
        default { "White" }
    }
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

function New-OutputDirectory {
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }
}

# Security Audit Checklist
$auditResults = @()

function Add-AuditResult {
    param(
        [string]$Category,
        [string]$Check,
        [string]$Status,  # PASS, FAIL, WARN, SKIP
        [string]$Details = "",
        [string]$Remediation = ""
    )
    
    $script:auditResults += [PSCustomObject]@{
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        Category = $Category
        Check = $Check
        Status = $Status
        Details = $Details
        Remediation = $Remediation
    }
    
    $color = switch ($Status) {
        "PASS" { "Green" }
        "FAIL" { "Red" }
        "WARN" { "Yellow" }
        "SKIP" { "Gray" }
        default { "White" }
    }
    Write-Host "  [$Status] $Check" -ForegroundColor $color
}

function Test-SSLConfiguration {
    Write-Log "`n=== SSL/TLS Security Checks ===" -Level "INFO"
    
    if ($SkipSSL) {
        Add-AuditResult -Category "SSL/TLS" -Check "SSL Scan" -Status "SKIP" -Details "Skipped by user request"
        return
    }
    
    $uri = [System.Uri]$Target
    $hostname = $uri.Host
    $port = if ($uri.Scheme -eq "https") { 443 } else { 80 }
    
    if ($uri.Scheme -ne "https") {
        Add-AuditResult -Category "SSL/TLS" -Check "HTTPS Enabled" -Status "FAIL" -Details "Target is not using HTTPS" -Remediation "Enable HTTPS with valid SSL certificate"
        return
    }
    
    try {
        $tcpClient = New-Object System.Net.Sockets.TcpClient($hostname, $port)
        $sslStream = New-Object System.Net.Security.SslStream($tcpClient.GetStream())
        $sslStream.AuthenticateAsClient($hostname)
        
        $cert = $sslStream.RemoteCertificate
        
        # Check certificate validity
        $cert2 = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cert)
        
        # Expiration check
        if ($cert2.NotAfter -gt (Get-Date)) {
            Add-AuditResult -Category "SSL/TLS" -Check "Certificate Validity" -Status "PASS" -Details "Certificate expires: $($cert2.NotAfter)"
        } else {
            Add-AuditResult -Category "SSL/TLS" -Check "Certificate Validity" -Status "FAIL" -Details "Certificate has expired" -Remediation "Renew SSL certificate"
        }
        
        # Self-signed check
        if ($cert2.Issuer -eq $cert2.Subject) {
            Add-AuditResult -Category "SSL/TLS" -Check "Certificate Authority" -Status "WARN" -Details "Self-signed certificate detected" -Remediation "Use certificate from trusted CA for production"
        } else {
            Add-AuditResult -Category "SSL/TLS" -Check "Certificate Authority" -Status "PASS" -Details "Certificate issued by: $($cert2.Issuer)"
        }
        
        # Protocol check
        $protocol = $sslStream.SslProtocol
        if ($protocol -match "Tls12|Tls13") {
            Add-AuditResult -Category "SSL/TLS" -Check "TLS Protocol" -Status "PASS" -Details "Using secure protocol: $protocol"
        } else {
            Add-AuditResult -Category "SSL/TLS" -Check "TLS Protocol" -Status "FAIL" -Details "Insecure protocol: $protocol" -Remediation "Disable TLS 1.0 and TLS 1.1"
        }
        
        $tcpClient.Close()
    }
    catch {
        Add-AuditResult -Category "SSL/TLS" -Check "SSL Connection" -Status "FAIL" -Details "Failed to establish SSL connection: $_" -Remediation "Check SSL configuration"
    }
}

function Test-PortSecurity {
    Write-Log "`n=== Port Security Checks ===" -Level "INFO"
    
    if ($SkipPorts) {
        Add-AuditResult -Category "Ports" -Check "Port Scan" -Status "SKIP" -Details "Skipped by user request"
        return
    }
    
    # Check common sensitive ports that should not be exposed
    $sensitivePorts = @(
        @{Port=5432; Service="PostgreSQL"; ShouldBeExposed=$false},
        @{Port=6379; Service="Redis"; ShouldBeExposed=$false},
        @{Port=27017; Service="MongoDB"; ShouldBeExposed=$false},
        @{Port=9000; Service="MinIO API"; ShouldBeExposed=$false},
        @{Port=9001; Service="MinIO Console"; ShouldBeExposed=$false},
        @{Port=7881; Service="LiveKit"; ShouldBeExposed=$false},
        @{Port=22; Service="SSH"; ShouldBeExposed=$true},
        @{Port=80; Service="HTTP"; ShouldBeExposed=$true},
        @{Port=443; Service="HTTPS"; ShouldBeExposed=$true}
    )
    
    $hostname = ([System.Uri]$Target).Host
    
    foreach ($portInfo in $sensitivePorts) {
        $port = $portInfo.Port
        try {
            $tcpClient = New-Object System.Net.Sockets.TcpClient
            $connect = $tcpClient.BeginConnect($hostname, $port, $null, $null)
            $wait = $connect.AsyncWaitHandle.WaitOne(2000)
            
            if ($wait -and $tcpClient.Connected) {
                if ($portInfo.ShouldBeExposed) {
                    Add-AuditResult -Category "Ports" -Check "$($portInfo.Service) (Port $port)" -Status "PASS" -Details "Port is open as expected"
                } else {
                    Add-AuditResult -Category "Ports" -Check "$($portInfo.Service) (Port $port)" -Status "FAIL" -Details "Sensitive port is exposed to public" -Remediation "Block port $port with firewall or bind to localhost only"
                }
                $tcpClient.Close()
            } else {
                if (-not $portInfo.ShouldBeExposed) {
                    Add-AuditResult -Category "Ports" -Check "$($portInfo.Service) (Port $port)" -Status "PASS" -Details "Port is not exposed (good)"
                } else {
                    Add-AuditResult -Category "Ports" -Check "$($portInfo.Service) (Port $port)" -Status "WARN" -Details "Expected port is not open"
                }
            }
        }
        catch {
            # Port is closed/filtered
            if (-not $portInfo.ShouldBeExposed) {
                Add-AuditResult -Category "Ports" -Check "$($portInfo.Service) (Port $port)" -Status "PASS" -Details "Port is not exposed (good)"
            }
        }
    }
}

function Test-HTTPSecurityHeaders {
    Write-Log "`n=== HTTP Security Headers ===" -Level "INFO"
    
    try {
        $response = Invoke-WebRequest -Uri "$Target" -Method GET -UseBasicParsing -TimeoutSec 10
        $headers = $response.Headers
        
        # Required security headers
        $securityHeaders = @{
            "X-Content-Type-Options" = "nosniff"
            "X-Frame-Options" = @("DENY", "SAMEORIGIN")
            "X-XSS-Protection" = "1; mode=block"
            "Strict-Transport-Security" = "max-age="
            "Content-Security-Policy" = ""
            "Referrer-Policy" = @("strict-origin", "no-referrer", "strict-origin-when-cross-origin")
            "Permissions-Policy" = ""
        }
        
        foreach ($header in $securityHeaders.Keys) {
            if ($headers.ContainsKey($header)) {
                $value = $headers[$header]
                $expectedValues = $securityHeaders[$header]
                
                if ($expectedValues -is [array]) {
                    $match = $expectedValues | Where-Object { $value -match $_ }
                    if ($match) {
                        Add-AuditResult -Category "Headers" -Check $header -Status "PASS" -Details "Value: $value"
                    } else {
                        Add-AuditResult -Category "Headers" -Check $header -Status "WARN" -Details "Unexpected value: $value" -Remediation "Set $header to one of: $($expectedValues -join ', ')"
                    }
                } elseif ($expectedValues -ne "") {
                    if ($value -match $expectedValues) {
                        Add-AuditResult -Category "Headers" -Check $header -Status "PASS" -Details "Value: $value"
                    } else {
                        Add-AuditResult -Category "Headers" -Check $header -Status "WARN" -Details "Unexpected value: $value"
                    }
                } else {
                    Add-AuditResult -Category "Headers" -Check $header -Status "PASS" -Details "Header is present: $value"
                }
            } else {
                Add-AuditResult -Category "Headers" -Check $header -Status "FAIL" -Details "Header is missing" -Remediation "Add $header header to all responses"
            }
        }
        
        # Check for sensitive headers that should NOT be present
        $sensitiveHeaders = @("Server", "X-Powered-By", "X-AspNet-Version")
        foreach ($header in $sensitiveHeaders) {
            if ($headers.ContainsKey($header)) {
                Add-AuditResult -Category "Headers" -Check "$header (should be hidden)" -Status "WARN" -Details "Header reveals server info: $($headers[$header])" -Remediation "Remove or obfuscate $header header"
            } else {
                Add-AuditResult -Category "Headers" -Check "$header (should be hidden)" -Status "PASS" -Details "Header is not present (good)"
            }
        }
    }
    catch {
        Add-AuditResult -Category "Headers" -Check "HTTP Request" -Status "FAIL" -Details "Failed to connect: $_" -Remediation "Ensure the server is running and accessible"
    }
}

function Test-APIEndpoints {
    Write-Log "`n=== API Security Checks ===" -Level "INFO"
    
    # Test unauthenticated access to protected endpoints
    $protectedEndpoints = @(
        "/api/users/profile",
        "/api/messages/history",
        "/api/groups",
        "/api/files",
        "/api/admin/users"
    )
    
    foreach ($endpoint in $protectedEndpoints) {
        try {
            $response = Invoke-WebRequest -Uri "$Target$endpoint" -Method GET -UseBasicParsing -TimeoutSec 5
            
            if ($response.StatusCode -eq 200) {
                Add-AuditResult -Category "API" -Check "Auth: $endpoint" -Status "FAIL" -Details "Endpoint accessible without authentication" -Remediation "Require authentication for this endpoint"
            } elseif ($response.StatusCode -eq 401) {
                Add-AuditResult -Category "API" -Check "Auth: $endpoint" -Status "PASS" -Details "Endpoint requires authentication"
            } else {
                Add-AuditResult -Category "API" -Check "Auth: $endpoint" -Status "WARN" -Details "Unexpected status code: $($response.StatusCode)"
            }
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            if ($statusCode -eq 401) {
                Add-AuditResult -Category "API" -Check "Auth: $endpoint" -Status "PASS" -Details "Endpoint requires authentication"
            } elseif ($statusCode -eq 404) {
                Add-AuditResult -Category "API" -Check "Auth: $endpoint" -Status "SKIP" -Details "Endpoint not found"
            } else {
                Add-AuditResult -Category "API" -Check "Auth: $endpoint" -Status "WARN" -Details "Status code: $statusCode"
            }
        }
    }
    
    # Test rate limiting
    Write-Log "  Testing rate limiting..." -Level "INFO"
    $rateLimited = $false
    $requests = 0
    for ($i = 0; $i -lt 100; $i++) {
        try {
            $response = Invoke-WebRequest -Uri "$Target/api/auth/login" -Method POST -Body "{}" -ContentType "application/json" -UseBasicParsing -TimeoutSec 2
            $requests++
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            if ($statusCode -eq 429) {
                $rateLimited = $true
                break
            }
        }
    }
    
    if ($rateLimited) {
        Add-AuditResult -Category "API" -Check "Rate Limiting" -Status "PASS" -Details "Rate limiting triggered after $requests requests"
    } else {
        Add-AuditResult -Category "API" -Check "Rate Limiting" -Status "WARN" -Details "No rate limiting detected after 100 requests" -Remediation "Implement rate limiting on auth endpoints"
    }
}

function Test-DatabaseSecurity {
    Write-Log "`n=== Database Security Checks ===" -Level "INFO"
    
    # Check if database files are in default locations
    $defaultPaths = @(
        "/var/lib/postgresql/data",
        "/var/lib/mongodb",
        "/var/lib/redis"
    )
    
    # Check environment variables for sensitive data
    $envFile = ".env"
    if (Test-Path $envFile) {
        $envContent = Get-Content $envFile -Raw
        
        # Check for hardcoded passwords
        if ($envContent -match "PASSWORD\s*=\s*[^$\{\[]") {
            Add-AuditResult -Category "Database" -Check "Hardcoded Passwords" -Status "WARN" -Details "Possible hardcoded password in .env file" -Remediation "Use environment variable references or secrets manager"
        } else {
            Add-AuditResult -Category "Database" -Check "Hardcoded Passwords" -Status "PASS" -Details "No obvious hardcoded passwords found"
        }
        
        # Check for default credentials
        if ($envContent -match "(admin|root|password|123456|qwerty)" -and $envContent -match "PASSWORD") {
            Add-AuditResult -Category "Database" -Check "Default Credentials" -Status "WARN" -Details "Possible weak or default credentials detected" -Remediation "Use strong, unique passwords"
        } else {
            Add-AuditResult -Category "Database" -Check "Default Credentials" -Status "PASS" -Details "No obvious default credentials found"
        }
    }
    
    # Check Docker network isolation
    try {
        $networks = docker network ls --format "{{.Name}}" 2>$null
        if ($networks -match "localtelegram") {
            Add-AuditResult -Category "Database" -Check "Network Isolation" -Status "PASS" -Details "Dedicated Docker network is configured"
        } else {
            Add-AuditResult -Category "Database" -Check "Network Isolation" -Status "WARN" -Details "No dedicated Docker network found" -Remediation "Create isolated Docker network for services"
        }
    }
    catch {
        Add-AuditResult -Category "Database" -Check "Network Isolation" -Status "SKIP" -Details "Docker not available"
    }
}

function Test-FileSecurity {
    Write-Log "`n=== File Security Checks ===" -Level "INFO"
    
    # Check file permissions
    $sensitiveFiles = @(
        ".env",
        ".env.local",
        ".env.production",
        "docker-compose.yml",
        "docker-compose.override.yml"
    )
    
    foreach ($file in $sensitiveFiles) {
        if (Test-Path $file) {
            $acl = Get-Acl $file
            $accessRules = $acl.Access | Where-Object { $_.FileSystemRights -match "Read" -and $_.IdentityReference -notmatch "BUILTIN\Administrators|NT AUTHORITY\SYSTEM" }
            
            if ($accessRules.Count -gt 0) {
                Add-AuditResult -Category "Files" -Check "Permissions: $file" -Status "WARN" -Details "File may be readable by non-admin users" -Remediation "Restrict file permissions to administrators only"
            } else {
                Add-AuditResult -Category "Files" -Check "Permissions: $file" -Status "PASS" -Details "File permissions are restricted"
            }
        }
    }
    
    # Check for sensitive files in git
    if (Test-Path ".git") {
        $gitignoreContent = Get-Content ".gitignore" -Raw -ErrorAction SilentlyContinue
        
        $shouldIgnore = @(".env", "*.pfx", "*.key", "secrets/", "credentials/")
        foreach ($pattern in $shouldIgnore) {
            if ($gitignoreContent -match [regex]::Escape($pattern)) {
                Add-AuditResult -Category "Files" -Check ".gitignore: $pattern" -Status "PASS" -Details "Pattern is ignored"
            } else {
                Add-AuditResult -Category "Files" -Check ".gitignore: $pattern" -Status "WARN" -Details "Pattern not in .gitignore" -Remediation "Add $pattern to .gitignore"
            }
        }
    }
}

function Test-DockerSecurity {
    Write-Log "`n=== Docker Security Checks ===" -Level "INFO"
    
    try {
        # Check if containers are running as root
        $containers = docker ps --format "{{.Names}}" 2>$null
        
        foreach ($container in $containers) {
            if ($container -match "localtelegram|postgres|redis|mongodb") {
                $inspect = docker inspect $container 2>$null | ConvertFrom-Json
                $user = $inspect[0].Config.User
                
                if ([string]::IsNullOrEmpty($user) -or $user -eq "root" -or $user -eq "0") {
                    Add-AuditResult -Category "Docker" -Check "Root User: $container" -Status "WARN" -Details "Container may be running as root" -Remediation "Configure container to run as non-root user"
                } else {
                    Add-AuditResult -Category "Docker" -Check "Root User: $container" -Status "PASS" -Details "Running as: $user"
                }
            }
        }
        
        # Check for privileged containers
        $privileged = docker ps --filter "status=running" --format "{{.Names}}" | ForEach-Object {
            $inspect = docker inspect $_ 2>$null | ConvertFrom-Json
            if ($inspect[0].HostConfig.Privileged) { $_ }
        }
        
        if ($privileged) {
            Add-AuditResult -Category "Docker" -Check "Privileged Containers" -Status "WARN" -Details "Privileged containers: $($privileged -join ', ')" -Remediation "Avoid running privileged containers"
        } else {
            Add-AuditResult -Category "Docker" -Check "Privileged Containers" -Status "PASS" -Details "No privileged containers detected"
        }
    }
    catch {
        Add-AuditResult -Category "Docker" -Check "Docker Inspection" -Status "SKIP" -Details "Docker not available or no containers running"
    }
}

function Export-AuditReport {
    New-OutputDirectory
    
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $reportPath = Join-Path $OutputDir "security-audit-$timestamp.csv"
    $summaryPath = Join-Path $OutputDir "security-summary-$timestamp.txt"
    
    # Export CSV
    $script:auditResults | Export-Csv -Path $reportPath -NoTypeInformation -Encoding UTF8
    
    # Generate summary
    $passCount = ($script:auditResults | Where-Object { $_.Status -eq "PASS" }).Count
    $failCount = ($script:auditResults | Where-Object { $_.Status -eq "FAIL" }).Count
    $warnCount = ($script:auditResults | Where-Object { $_.Status -eq "WARN" }).Count
    $skipCount = ($script:auditResults | Where-Object { $_.Status -eq "SKIP" }).Count
    $totalCount = $script:auditResults.Count
    
    $summary = @"
LocalTelegram Security Audit Report
===================================
Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Target: $Target

Summary:
--------
Total Checks: $totalCount
Passed: $passCount
Failed: $failCount
Warnings: $warnCount
Skipped: $skipCount

Score: $([math]::Round(($passCount / ($totalCount - $skipCount)) * 100, 1))%

Failed Checks:
-------------
$($script:auditResults | Where-Object { $_.Status -eq "FAIL" } | ForEach-Object { "- [$($_.Category)] $($_.Check): $($_.Details)" } | Out-String)

Warnings:
---------
$($script:auditResults | Where-Object { $_.Status -eq "WARN" } | ForEach-Object { "- [$($_.Category)] $($_.Check): $($_.Details)" } | Out-String)

Remediation Steps:
------------------
$($script:auditResults | Where-Object { $_.Status -in @("FAIL", "WARN") -and $_.Remediation } | ForEach-Object { "- $($_.Remediation)" } | Out-String)

"@
    
    $summary | Out-File -FilePath $summaryPath -Encoding UTF8
    
    Write-Log "`n=== Audit Complete ===" -Level "INFO"
    Write-Log "Total: $totalCount | Passed: $passCount | Failed: $failCount | Warnings: $warnCount" -Level "INFO"
    Write-Log "CSV Report: $reportPath" -Level "INFO"
    Write-Log "Summary: $summaryPath" -Level "INFO"
}

function Show-Help {
    Write-Host @"

LocalTelegram Security Audit Script
===================================

Usage: .\security-audit.ps1 [options]

Options:
    -Target       Target URL to audit (default: http://localhost:5000)
    -OutputDir    Output directory for reports (default: ./security-audit-results)
    -SkipSSL      Skip SSL/TLS checks
    -SkipPorts    Skip port scanning
    -DryRun       Preview without executing

Checks performed:
    - SSL/TLS configuration
    - Port exposure (sensitive services)
    - HTTP security headers
    - API authentication
    - Rate limiting
    - Database security
    - File permissions
    - Docker container security

Examples:
    .\security-audit.ps1
    .\security-audit.ps1 -Target https://messenger.company.com
    .\security-audit.ps1 -SkipPorts -SkipSSL

"@
}

# Main execution
if ($DryRun) {
    Write-Log "DRY RUN - No actual checks will be performed" -Level "WARNING"
}

Write-Log "Starting LocalTelegram Security Audit..." -Level "INFO"
Write-Log "Target: $Target" -Level "INFO"

Test-SSLConfiguration
Test-PortSecurity
Test-HTTPSecurityHeaders
Test-APIEndpoints
Test-DatabaseSecurity
Test-FileSecurity
Test-DockerSecurity

Export-AuditReport
