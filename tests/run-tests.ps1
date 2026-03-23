# LocalTelegram Test Runner Script
# This script runs all unit tests and generates a coverage report

param(
    [Parameter(Mandatory=$false)]
    [string]$Filter = "",

    [Parameter(Mandatory=$false)]
    [switch]$Coverage = $false,

    [Parameter(Mandatory=$false)]
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Stop"

$testResults = @()
$failedTests = 0
$passedTests = 0
$totalTests = 0

function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) {
        Write-Output $args
    }
    $host.UI.RawUI.ForegroundColor = $fc
}

function Test-Service($serviceName, $projectPath) {
    Write-ColorOutput Cyan "`n=== Testing $serviceName ===" -ForegroundColor Cyan
    
    $filterArg = if ($Filter) { "--filter `"$Filter`"" } else { "" }
    $verbosityArg = if ($Verbose) { "--verbosity normal" } else { "" }
    
    $command = "dotnet test $projectPath --logger `" + ("console" + $verbosityArg + $filterArg + ")"
    
    if ($Coverage) {
        $command += " --collect:`"XPlat Code Coverage`""
    }
    
    $startTime = Get-Date
    $output = Invoke-Expression $command | Out-String
    $endTime = Get-Date
    $duration = $endTime - $startTime
    
    # Parse results
    if ($output -match "Passed!\s+(\d+)") {
        $passed = [int]$Matches[1]
        $script:passedTests += $passed
        $totalTests += $passed
        Write-ColorOutput Green "  Passed: $passed tests in $([math]::Round($duration.TotalSeconds, 2))s"
    }
    
    if ($output -match "Failed!\s+(\d+)") {
        $failed = [int]$Matches[1]
        $script:failedTests += $failed
        $totalTests += $failed
        Write-ColorOutput Red "  Failed: $failed tests"
    }
    
    # Check for errors
    if ($LASTEXITCODE -ne 0) {
        Write-ColorOutput Red "  Test execution failed with exit code: $LASTEXITCODE"
        $script:testResults += "`n$serviceName`: FAILED`n"
        return $false
    }
    
    $script:testResults += "`n$serviceName`: PASSED`n"
    return $true
}

# Main execution
Write-ColorOutput Cyan "========================================" -ForegroundColor Cyan
Write-ColorOutput Cyan "LocalTelegram Unit Tests" -ForegroundColor Cyan
Write-ColorOutput Cyan "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if dotnet is installed
try {
    $dotnetVersion = & dotnet --version 2>&1
    Write-ColorOutput Green "Found: $dotnetVersion"
} catch {
    Write-ColorOutput Red "ERROR: .NET SDK is not installed!"
    Write-ColorOutput Yellow "`nPlease install .NET 8 SDK from:" -ForegroundColor Yellow
    Write-ColorOutput Yellow "  https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    Write-ColorOutput Yellow "`nAfter installation, restart this script." -ForegroundColor Yellow
    exit 1
}

Write-Host ""

# Test projects
$testProjects = @(
    @{ Name = "AuthService"; Path = "tests/AuthService.Tests" },
    @{ Name = "MessageService"; Path = "tests/MessageService.Tests" },
    @{ Name = "FileService"; Path = "tests/FileService.Tests" }
)

$allPassed = $true

foreach ($project in $testProjects) {
    $result = Test-Service $project.Name $project.Path
    if (-not $result) {
        $allPassed = $false
    }
    Write-Host ""
}

# Summary
Write-ColorOutput Cyan "========================================" -ForegroundColor Cyan
Write-ColorOutput Cyan "Test Summary" -ForegroundColor Cyan
Write-ColorOutput Cyan "========================================" -ForegroundColor Cyan
Write-Host ""

if ($allPassed) {
    Write-ColorOutput Green "All tests passed!" -ForegroundColor Green
    Write-ColorOutput Green "Total: $totalTests tests" -ForegroundColor Green
    Write-ColorOutput Green "  Passed: $passedTests" -ForegroundColor Green
    Write-ColorOutput Green "  Failed: $failedTests" -ForegroundColor Green
    
    if ($Coverage) {
        Write-ColorOutput Yellow "`nCoverage report generated in:" -ForegroundColor Yellow
        Write-ColorOutput Yellow "  tests/AuthService.Tests/TestResults/" -ForegroundColor Yellow
        Write-ColorOutput Yellow "  tests/MessageService.Tests/TestResults/" -ForegroundColor Yellow
        Write-ColorOutput Yellow "  tests/FileService.Tests/TestResults/" -ForegroundColor Yellow
    }
    
    exit 0
} else {
    Write-ColorOutput Red "Some tests failed!" -ForegroundColor Red
    Write-ColorOutput Red "Total: $totalTests tests" -ForegroundColor Red
    Write-ColorOutput Red "  Passed: $passedTests" -ForegroundColor Red
    Write-ColorOutput Red "  Failed: $failedTests" -ForegroundColor Red
    exit 1
}
