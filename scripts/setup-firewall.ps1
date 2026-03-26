# LocalTelegram Firewall Configuration Script
# Run as Administrator

param(
    [string]$Action = "configure",  # configure, status, reset
    [string]$Profile = "Domain",     # Domain, Private, Public, All
    [bool]$DryRun = $false
)

$ErrorActionPreference = "Stop"

# Required ports for LocalTelegram services
$InboundPorts = @(
    @{Port=80; Protocol="TCP"; Name="HTTP"},
    @{Port=443; Protocol="TCP"; Name="HTTPS"},
    @{Port=5001; Protocol="TCP"; Name="Auth Service"},
    @{Port=5002; Protocol="TCP"; Name="Message Service"},
    @{Port=5003; Protocol="TCP"; Name="User Service"},
    @{Port=5004; Protocol="TCP"; Name="File Service"},
    @{Port=5005; Protocol="TCP"; Name="Push Service (WebSocket)"},
    @{Port=5006; Protocol="TCP"; Name="Group Service"},
    @{Port=5007; Protocol="TCP"; Name="Media Service"},
    @{Port=5008; Protocol="TCP"; Name="Conference Service"},
    @{Port=5009; Protocol="TCP"; Name="Admin Service"},
    @{Port=5432; Protocol="TCP"; Name="PostgreSQL"; Internal=$true},
    @{Port=6379; Protocol="TCP"; Name="Redis"; Internal=$true},
    @{Port=27017; Protocol="TCP"; Name="MongoDB"; Internal=$true},
    @{Port=9000; Protocol="TCP"; Name="MinIO API"; Internal=$true},
    @{Port=9001; Protocol="TCP"; Name="MinIO Console"; Internal=$true},
    @{Port=7881; Protocol="TCP"; Name="LiveKit"; Internal=$true},
    @{Port=50000-60000; Protocol="UDP"; Name="WebRTC Media"; Internal=$true},
    @{Port=3478; Protocol="UDP"; Name="STUN/TURN"; Internal=$true},
    @{Port=5349; Protocol="TCP"; Name="TURN TLS"; Internal=$true}
)

$OutboundPorts = @(
    @{Port=80; Protocol="TCP"; Name="HTTP Outbound"},
    @{Port=443; Protocol="TCP"; Name="HTTPS Outbound"},
    @{Port=587; Protocol="TCP"; Name="SMTP"},
    @{Port=465; Protocol="TCP"; Name="SMTPS"},
    @{Port=389; Protocol="TCP"; Name="LDAP"},
    @{Port=636; Protocol="TCP"; Name="LDAPS"}
)

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

function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-FirewallStatus {
    Write-Log "Checking Windows Firewall status..." -Level "INFO"
    
    $status = Get-NetFirewallProfile -Name $Profile
    Write-Log "Firewall Profile: $Profile" -Level "INFO"
    Write-Log "Enabled: $($status.Enabled)" -Level "INFO"
    Write-Log "Default Inbound Action: $($status.DefaultInboundAction)" -Level "INFO"
    Write-Log "Default Outbound Action: $($status.DefaultOutboundAction)" -Level "INFO"
    
    Write-Log "`nExisting LocalTelegram rules:" -Level "INFO"
    $rules = Get-NetFirewallRule -DisplayName "LocalTelegram*" -ErrorAction SilentlyContinue
    if ($rules) {
        $rules | ForEach-Object {
            Write-Log "  - $($_.DisplayName): $($_.Enabled) ($($_.Action))" -Level "INFO"
        }
    } else {
        Write-Log "  No LocalTelegram rules found" -Level "WARNING"
    }
}

function New-FirewallRuleForPort {
    param(
        [int]$Port,
        [string]$Protocol,
        [string]$Name,
        [string]$Direction,
        [bool]$Internal = $false
    )
    
    $ruleName = "LocalTelegram - $Name"
    
    # Check if rule already exists
    $existingRule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    if ($existingRule) {
        Write-Log "Rule '$ruleName' already exists, skipping..." -Level "WARNING"
        return
    }
    
    if ($DryRun) {
        Write-Log "[DRY RUN] Would create rule: $ruleName ($Protocol/$Port)" -Level "INFO"
        return
    }
    
    try {
        $params = @{
            DisplayName = $ruleName
            Description = "LocalTelegram Messenger - $Name"
            Direction = $Direction
            Action = "Allow"
            Protocol = $Protocol
            Profile = $Profile
            Enabled = "True"
        }
        
        # Handle port ranges
        if ($Port -is [string] -and $Port -match "-") {
            $params.LocalPort = $Port
        } else {
            $params.LocalPort = $Port
        }
        
        New-NetFirewallRule @params | Out-Null
        Write-Log "Created rule: $ruleName ($Protocol/$Port)" -Level "SUCCESS"
    }
    catch {
        Write-Log "Failed to create rule '$ruleName': $_" -Level "ERROR"
    }
}

function Set-FirewallConfiguration {
    Write-Log "Configuring Windows Firewall for LocalTelegram..." -Level "INFO"
    
    if (-not (Test-Administrator)) {
        Write-Log "This script must be run as Administrator!" -Level "ERROR"
        exit 1
    }
    
    # Enable firewall for the profile
    Write-Log "Enabling Windows Firewall for profile: $Profile" -Level "INFO"
    if (-not $DryRun) {
        Set-NetFirewallProfile -Name $Profile -Enabled True
        Set-NetFirewallProfile -Name $Profile -DefaultInboundAction Block
        Set-NetFirewallProfile -Name $Profile -DefaultOutboundAction Allow
    }
    
    # Create inbound rules
    Write-Log "`nCreating inbound rules..." -Level "INFO"
    foreach ($portInfo in $InboundPorts) {
        $internal = $portInfo.Internal -eq $true
        
        # For production, internal ports might be restricted
        if ($internal) {
            Write-Log "Skipping internal port $($portInfo.Port) ($($portInfo.Name)) - configure manually if needed" -Level "WARNING"
            continue
        }
        
        New-FirewallRuleForPort -Port $portInfo.Port -Protocol $portInfo.Protocol -Name $portInfo.Name -Direction "Inbound" -Internal $internal
    }
    
    # Create outbound rules (usually not needed if default is Allow)
    Write-Log "`nOutbound rules (default Allow - usually sufficient)..." -Level "INFO"
    
    Write-Log "`nFirewall configuration complete!" -Level "SUCCESS"
}

function Reset-FirewallConfiguration {
    Write-Log "Removing all LocalTelegram firewall rules..." -Level "WARNING"
    
    if (-not (Test-Administrator)) {
        Write-Log "This script must be run as Administrator!" -Level "ERROR"
        exit 1
    }
    
    $rules = Get-NetFirewallRule -DisplayName "LocalTelegram*" -ErrorAction SilentlyContinue
    if ($rules) {
        if ($DryRun) {
            Write-Log "[DRY RUN] Would remove $($rules.Count) rules" -Level "INFO"
            $rules | ForEach-Object {
                Write-Log "  - Would remove: $($_.DisplayName)" -Level "INFO"
            }
        } else {
            $rules | Remove-NetFirewallRule
            Write-Log "Removed $($rules.Count) LocalTelegram firewall rules" -Level "SUCCESS"
        }
    } else {
        Write-Log "No LocalTelegram rules found to remove" -Level "INFO"
    }
}

function Show-Help {
    Write-Host @"

LocalTelegram Firewall Configuration Script
============================================

Usage: .\setup-firewall.ps1 -Action <action> [-Profile <profile>] [-DryRun]

Actions:
    configure   - Create firewall rules for LocalTelegram services
    status      - Show current firewall status and LocalTelegram rules
    reset       - Remove all LocalTelegram firewall rules

Profiles:
    Domain      - Domain network (default)
    Private     - Private network
    Public      - Public network
    All         - All profiles

Examples:
    .\setup-firewall.ps1 -Action configure
    .\setup-firewall.ps1 -Action status
    .\setup-firewall.ps1 -Action reset
    .\setup-firewall.ps1 -Action configure -Profile Private
    .\setup-firewall.ps1 -Action configure -DryRun $true

Ports configured:
    Public ports: 80, 443, 5001-5009
    Internal ports: 5432 (PostgreSQL), 6379 (Redis), 27017 (MongoDB),
                   9000-9001 (MinIO), 7881 (LiveKit), 3478 (STUN),
                   5349 (TURN TLS), 50000-60000 (WebRTC)

"@
}

# Main execution
switch ($Action.ToLower()) {
    "configure" { Set-FirewallConfiguration }
    "status" { Get-FirewallStatus }
    "reset" { Reset-FirewallConfiguration }
    "help" { Show-Help }
    default { Show-Help }
}
