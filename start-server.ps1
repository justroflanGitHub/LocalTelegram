# LocalTelegram Quick Start Script for Windows
# This script sets up and starts the LocalTelegram server

param(
    [Parameter(Mandatory=$false)]
    [string]$ServerIP = "",
    
    [Parameter(Mandatory=$false)]
    [switch]$Build = $false,
    
    [Parameter(Mandatory=$false)]
    [switch]$Stop = $false,
    
    [Parameter(Mandatory=$false)]
    [switch]$Logs = $false,
    
    [Parameter(Mandatory=$false)]
    [switch]$Status = $false
)

$ErrorActionPreference = "Stop"

# Colors
function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) {
        Write-Output $args
    }
    $host.UI.RawUI.ForegroundColor = $fc
}

Write-ColorOutput Cyan "=== LocalTelegram Server Management ==="
Write-Output ""

# Check Docker
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-ColorOutput Red "Error: Docker is not installed or not in PATH"
    Write-Output "Please install Docker Desktop from https://www.docker.com/products/docker-desktop"
    exit 1
}

# Check Docker Compose
$dockerCompose = "docker compose"
try {
    & docker compose version | Out-Null
} catch {
    $dockerCompose = "docker-compose"
    if (-not (Get-Command docker-compose -ErrorAction SilentlyContinue)) {
        Write-ColorOutput Red "Error: Docker Compose is not installed"
        exit 1
    }
}

# Handle stop command
if ($Stop) {
    Write-ColorOutput Yellow "Stopping LocalTelegram server..."
    Invoke-Expression "$dockerCompose -f docker-compose.mytelegram.yml down"
    Write-ColorOutput Green "Server stopped."
    exit 0
}

# Handle logs command
if ($Logs) {
    Write-ColorOutput Yellow "Showing server logs (Ctrl+C to exit)..."
    Invoke-Expression "$dockerCompose -f docker-compose.mytelegram.yml logs -f"
    exit 0
}

# Handle status command
if ($Status) {
    Write-ColorOutput Yellow "Server status:"
    Invoke-Expression "$dockerCompose -f docker-compose.mytelegram.yml ps"
    exit 0
}

# Check if .env file exists
if (-not (Test-Path ".env")) {
    Write-ColorOutput Yellow "Creating .env file from template..."
    Copy-Item ".env.mytelegram" ".env"
    Write-ColorOutput Green "Created .env file. Please edit it to set your server IP."
}

# Auto-detect server IP if not provided
if ([string]::IsNullOrEmpty($ServerIP)) {
    Write-ColorOutput Yellow "Auto-detecting server IP..."
    $ipAddresses = Get-NetIPAddress -AddressFamily IPv4 | Where-Object { 
        $_.IPAddress -notlike "127.*" -and 
        $_.IPAddress -notlike "169.254.*" -and
        $_.PrefixOrigin -ne "WellKnown"
    } | Select-Object -First 1
    
    if ($ipAddresses) {
        $ServerIP = $ipAddresses.IPAddress
        Write-ColorOutput Green "Detected IP: $ServerIP"
    } else {
        Write-ColorOutput Yellow "Could not auto-detect IP. Using localhost."
        $ServerIP = "127.0.0.1"
    }
}

# Update .env file with server IP
Write-ColorOutput Yellow "Updating server IP in .env file..."
(Get-Content ".env") -replace "SERVER_IP=.*", "SERVER_IP=$ServerIP" | Set-Content ".env"
Write-ColorOutput Green "Server IP set to: $ServerIP"

# Create data directories
Write-ColorOutput Yellow "Creating data directories..."
$directories = @(
    "data\redis",
    "data\rabbitmq",
    "data\mongo\db",
    "data\mongo\configdb",
    "data\minio"
)

foreach ($dir in $directories) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}
Write-ColorOutput Green "Data directories created."

# Build images if requested
if ($Build) {
    Write-ColorOutput Yellow "Building Docker images..."
    Invoke-Expression "$dockerCompose -f docker-compose.mytelegram.yml build"
}

# Start services
Write-ColorOutput Yellow "Starting LocalTelegram server..."
Invoke-Expression "$dockerCompose -f docker-compose.mytelegram.yml up -d"

Write-Output ""
Write-ColorOutput Green "=== LocalTelegram Server Started ==="
Write-Output ""
Write-ColorOutput Cyan "Server Configuration:"
Write-Output "  Server IP: $ServerIP"
Write-Output "  Auth Port: 20443"
Write-Output "  Gateway Port: 20543"
Write-Output "  File Port: 20643"
Write-Output ""
Write-ColorOutput Cyan "Management Consoles:"
Write-Output "  RabbitMQ: http://localhost:15672"
Write-Output "  MinIO: http://localhost:9001"
Write-Output ""
Write-ColorOutput Cyan "Useful Commands:"
Write-Output "  Check status: .\start-server.ps1 -Status"
Write-Output "  View logs:    .\start-server.ps1 -Logs"
Write-Output "  Stop server:  .\start-server.ps1 -Stop"
Write-Output ""
Write-ColorOutput Yellow "Test verification code: 22222"
Write-Output ""
Write-ColorOutput Cyan "Next Steps:"
Write-Output "1. Configure Windows client:"
Write-Output "   .\scripts\configure-desktop-client.ps1 -ServerIP $ServerIP"
Write-Output "2. Build and run the client (see docs/CLIENT_CONFIGURATION.md)"
Write-Output "3. Login with any phone number and verification code: 22222"
