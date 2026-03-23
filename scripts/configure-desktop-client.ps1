# LocalTelegram Desktop Client Configuration Script
# This script configures the tdesktop client to connect to your LocalTelegram server

param(
    [Parameter(Mandatory=$true)]
    [string]$ServerIP,
    
    [Parameter(Mandatory=$false)]
    [int]$Port = 20443
)

$ErrorActionPreference = "Stop"

# Path to the configuration file
$ConfigFile = "external\mytelegram-tdesktop\Telegram\SourceFiles\mtproto\mtproto_dc_options.cpp"

Write-Host "=== LocalTelegram Desktop Client Configuration ===" -ForegroundColor Cyan
Write-Host ""

# Check if the file exists
if (-not (Test-Path $ConfigFile)) {
    Write-Host "Error: Configuration file not found at $ConfigFile" -ForegroundColor Red
    Write-Host "Make sure you have cloned the mytelegram-tdesktop repository:" -ForegroundColor Yellow
    Write-Host "  git clone https://github.com/loyldg/mytelegram-tdesktop.git external\mytelegram-tdesktop" -ForegroundColor Yellow
    exit 1
}

Write-Host "Configuring client to connect to server: $ServerIP`:$Port" -ForegroundColor Green
Write-Host ""

# Read the file content
$content = Get-Content $ConfigFile -Raw

# Replace IP address
$pattern = '#define IpV4 ".*"'
$replacement = "#define IpV4 `"$ServerIP`""
$content = $content -replace $pattern, $replacement

$pattern = '#define IpV6 ".*"'
$replacement = "#define IpV6 `"$ServerIP`""
$content = $content -replace $pattern, $replacement

# Replace port if different from default
if ($Port -ne 20443) {
    $pattern = '#define IpV4Port \d+'
    $replacement = "#define IpV4Port $Port"
    $content = $content -replace $pattern, $replacement
    
    $pattern = '#define IpV4Port2 \d+'
    $replacement = "#define IpV4Port2 $Port"
    $content = $content -replace $pattern, $replacement
    
    $pattern = '#define IpV6Port \d+'
    $replacement = "#define IpV6Port $Port"
    $content = $content -replace $pattern, $replacement
    
    $pattern = '#define IpV6Port2 \d+'
    $replacement = "#define IpV6Port2 $Port"
    $content = $content -replace $pattern, $replacement
}

# Write the file back
Set-Content $ConfigFile $content -NoNewline

Write-Host "Configuration updated successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "File modified: $ConfigFile" -ForegroundColor Gray
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Install prerequisites (Visual Studio 2022, CMake, vcpkg, Qt 6)" -ForegroundColor White
Write-Host "2. Build the client:" -ForegroundColor White
Write-Host "   cd external\mytelegram-tdesktop" -ForegroundColor Gray
Write-Host "   cmake -B build -DCMAKE_BUILD_TYPE=Release" -ForegroundColor Gray
Write-Host "   cmake --build build --config Release" -ForegroundColor Gray
Write-Host "3. Run the client from build\out\Release\Telegram.exe" -ForegroundColor White
Write-Host ""
Write-Host "Test verification code: 22222" -ForegroundColor Yellow
