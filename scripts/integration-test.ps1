# LocalTelegram Integration Test Script
# Tests end-to-end functionality of all services

param(
    [string]$Target = "http://localhost:5000",
    [string]$TestUser = "testuser",
    [string]$TestPassword = "TestPassword123!",
    [string]$OutputDir = "./test-results",
    [bool]$Verbose = $false,
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
        "TEST" { "Cyan" }
        default { "White" }
    }
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

function Write-TestResult {
    param([string]$Test, [string]$Result, [string]$Details = "")
    $color = switch ($Result) {
        "PASS" { "Green" }
        "FAIL" { "Red" }
        "SKIP" { "Yellow" }
        default { "White" }
    }
    Write-Host "  [$Result] $Test" -ForegroundColor $color
    if ($Details -and $Verbose) {
        Write-Host "        $Details" -ForegroundColor "Gray"
    }
}

$script:testResults = @()
$script:authToken = $null
$script:testUserId = $null

function Add-TestResult {
    param([string]$Category, [string]$Test, [string]$Result, [string]$Details = "")
    $script:testResults += [PSCustomObject]@{
        Category = $Category
        Test = $Test
        Result = $Result
        Details = $Details
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    }
}

function Invoke-ApiRequest {
    param(
        [string]$Method = "GET",
        [string]$Endpoint,
        [object]$Body = $null,
        [bool]$AuthRequired = $false
    )
    
    $headers = @{
        "Content-Type" = "application/json"
    }
    
    if ($AuthRequired -and $script:authToken) {
        $headers["Authorization"] = "Bearer $($script:authToken)"
    }
    
    $params = @{
        Uri = "$Target$Endpoint"
        Method = $Method
        Headers = $headers
        UseBasicParsing = $true
        TimeoutSec = 30
    }
    
    if ($Body) {
        $params["Body"] = ($Body | ConvertTo-Json -Depth 10)
    }
    
    try {
        $response = Invoke-WebRequest @params
        return @{
            Success = $true
            StatusCode = $response.StatusCode
            Content = $response.Content | ConvertFrom-Json
            RawContent = $response.Content
        }
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $content = $reader.ReadToEnd()
        return @{
            Success = $false
            StatusCode = $statusCode
            Content = $content
            Error = $_.Exception.Message
        }
    }
}

function Test-HealthEndpoints {
    Write-Log "`n=== Testing Health Endpoints ===" -Level "TEST"
    
    $endpoints = @(
        "/health",
        "/health/ready",
        "/health/live"
    )
    
    foreach ($endpoint in $endpoints) {
        $result = Invoke-ApiRequest -Method GET -Endpoint $endpoint
        
        if ($result.Success -and $result.StatusCode -eq 200) {
            Write-TestResult -Test "GET $endpoint" -Result "PASS"
            Add-TestResult -Category "Health" -Test "GET $endpoint" -Result "PASS"
        } else {
            Write-TestResult -Test "GET $endpoint" -Result "FAIL" -Details $result.Error
            Add-TestResult -Category "Health" -Test "GET $endpoint" -Result "FAIL" -Details $result.Error
        }
    }
}

function Test-AuthFlow {
    Write-Log "`n=== Testing Authentication Flow ===" -Level "TEST"
    
    # Test 1: Register new user
    $randomSuffix = Get-Random -Minimum 1000 -Maximum 9999
    $testUsername = "$TestUser$randomSuffix"
    $testPhone = "+7999$randomSuffix"
    
    $registerBody = @{
        username = $testUsername
        phoneNumber = $testPhone
        password = $TestPassword
        firstName = "Test"
        lastName = "User"
    }
    
    $result = Invoke-ApiRequest -Method POST -Endpoint "/api/auth/register" -Body $registerBody
    
    if ($result.Success -or $result.StatusCode -eq 409) {
        Write-TestResult -Test "User Registration" -Result "PASS" -Details "User: $testUsername"
        Add-TestResult -Category "Auth" -Test "Registration" -Result "PASS"
    } else {
        Write-TestResult -Test "User Registration" -Result "FAIL" -Details $result.Error
        Add-TestResult -Category "Auth" -Test "Registration" -Result "FAIL" -Details $result.Error
    }
    
    # Test 2: Login
    $loginBody = @{
        username = $testUsername
        password = $TestPassword
    }
    
    $result = Invoke-ApiRequest -Method POST -Endpoint "/api/auth/login" -Body $loginBody
    
    if ($result.Success) {
        $script:authToken = $result.Content.token
        $script:testUserId = $result.Content.user.id
        Write-TestResult -Test "User Login" -Result "PASS" -Details "Token received"
        Add-TestResult -Category "Auth" -Test "Login" -Result "PASS"
        
        # Test 3: Get current user
        $userResult = Invoke-ApiRequest -Method GET -Endpoint "/api/auth/me" -AuthRequired $true
        
        if ($userResult.Success) {
            Write-TestResult -Test "Get Current User" -Result "PASS"
            Add-TestResult -Category "Auth" -Test "Get Current User" -Result "PASS"
        } else {
            Write-TestResult -Test "Get Current User" -Result "FAIL"
            Add-TestResult -Category "Auth" -Test "Get Current User" -Result "FAIL"
        }
        
        # Test 4: Logout
        $logoutResult = Invoke-ApiRequest -Method POST -Endpoint "/api/auth/logout" -AuthRequired $true
        
        if ($logoutResult.Success) {
            Write-TestResult -Test "Logout" -Result "PASS"
            Add-TestResult -Category "Auth" -Test "Logout" -Result "PASS"
        } else {
            Write-TestResult -Test "Logout" -Result "FAIL"
            Add-TestResult -Category "Auth" -Test "Logout" -Result "FAIL"
        }
        
        # Login again for subsequent tests
        $result = Invoke-ApiRequest -Method POST -Endpoint "/api/auth/login" -Body $loginBody
        if ($result.Success) {
            $script:authToken = $result.Content.token
        }
    } else {
        Write-TestResult -Test "User Login" -Result "FAIL" -Details $result.Error
        Add-TestResult -Category "Auth" -Test "Login" -Result "FAIL" -Details $result.Error
    }
    
    # Test 5: Invalid login
    $invalidLogin = @{
        username = "nonexistent"
        password = "wrongpassword"
    }
    $result = Invoke-ApiRequest -Method POST -Endpoint "/api/auth/login" -Body $invalidLogin
    
    if ($result.StatusCode -eq 401) {
        Write-TestResult -Test "Invalid Login Rejected" -Result "PASS"
        Add-TestResult -Category "Auth" -Test "Invalid Login Rejected" -Result "PASS"
    } else {
        Write-TestResult -Test "Invalid Login Rejected" -Result "FAIL" -Details "Should return 401"
        Add-TestResult -Category "Auth" -Test "Invalid Login Rejected" -Result "FAIL"
    }
}

function Test-MessagingFlow {
    Write-Log "`n=== Testing Messaging Flow ===" -Level "TEST"
    
    if (-not $script:authToken) {
        Write-TestResult -Test "Messaging Tests" -Result "SKIP" -Details "No auth token available"
        Add-TestResult -Category "Messaging" -Test "All" -Result "SKIP" -Details "No auth token"
        return
    }
    
    # Test 1: Send message
    $messageBody = @{
        recipientId = "test-recipient-id"
        content = "Test message from integration test"
        type = "text"
    }
    
    $result = Invoke-ApiRequest -Method POST -Endpoint "/api/messages/send" -Body $messageBody -AuthRequired $true
    
    if ($result.Success) {
        $messageId = $result.Content.id
        Write-TestResult -Test "Send Message" -Result "PASS" -Details "Message ID: $messageId"
        Add-TestResult -Category "Messaging" -Test "Send Message" -Result "PASS"
        
        # Test 2: Get message history
        $historyResult = Invoke-ApiRequest -Method GET -Endpoint "/api/messages/history?limit=10" -AuthRequired $true
        
        if ($historyResult.Success) {
            Write-TestResult -Test "Get Message History" -Result "PASS"
            Add-TestResult -Category "Messaging" -Test "Get History" -Result "PASS"
        } else {
            Write-TestResult -Test "Get Message History" -Result "FAIL"
            Add-TestResult -Category "Messaging" -Test "Get History" -Result "FAIL"
        }
        
        # Test 3: Edit message
        $editBody = @{
            content = "Edited test message"
        }
        $editResult = Invoke-ApiRequest -Method PUT -Endpoint "/api/messages/$messageId" -Body $editBody -AuthRequired $true
        
        if ($editResult.Success) {
            Write-TestResult -Test "Edit Message" -Result "PASS"
            Add-TestResult -Category "Messaging" -Test "Edit Message" -Result "PASS"
        } else {
            Write-TestResult -Test "Edit Message" -Result "FAIL"
            Add-TestResult -Category "Messaging" -Test "Edit Message" -Result "FAIL"
        }
        
        # Test 4: Delete message
        $deleteResult = Invoke-ApiRequest -Method DELETE -Endpoint "/api/messages/$messageId" -AuthRequired $true
        
        if ($deleteResult.Success) {
            Write-TestResult -Test "Delete Message" -Result "PASS"
            Add-TestResult -Category "Messaging" -Test "Delete Message" -Result "PASS"
        } else {
            Write-TestResult -Test "Delete Message" -Result "FAIL"
            Add-TestResult -Category "Messaging" -Test "Delete Message" -Result "FAIL"
        }
    } else {
        Write-TestResult -Test "Send Message" -Result "FAIL" -Details $result.Error
        Add-TestResult -Category "Messaging" -Test "Send Message" -Result "FAIL" -Details $result.Error
    }
}

function Test-UserOperations {
    Write-Log "`n=== Testing User Operations ===" -Level "TEST"
    
    if (-not $script:authToken) {
        Write-TestResult -Test "User Tests" -Result "SKIP" -Details "No auth token available"
        Add-TestResult -Category "User" -Test "All" -Result "SKIP" -Details "No auth token"
        return
    }
    
    # Test 1: Get profile
    $result = Invoke-ApiRequest -Method GET -Endpoint "/api/users/profile" -AuthRequired $true
    
    if ($result.Success) {
        Write-TestResult -Test "Get Profile" -Result "PASS"
        Add-TestResult -Category "User" -Test "Get Profile" -Result "PASS"
        
        # Test 2: Update profile
        $updateBody = @{
            firstName = "Updated"
            lastName = "User"
            bio = "Integration test user"
        }
        $updateResult = Invoke-ApiRequest -Method PUT -Endpoint "/api/users/profile" -Body $updateBody -AuthRequired $true
        
        if ($updateResult.Success) {
            Write-TestResult -Test "Update Profile" -Result "PASS"
            Add-TestResult -Category "User" -Test "Update Profile" -Result "PASS"
        } else {
            Write-TestResult -Test "Update Profile" -Result "FAIL"
            Add-TestResult -Category "User" -Test "Update Profile" -Result "FAIL"
        }
    } else {
        Write-TestResult -Test "Get Profile" -Result "FAIL"
        Add-TestResult -Category "User" -Test "Get Profile" -Result "FAIL"
    }
    
    # Test 3: Search users
    $searchResult = Invoke-ApiRequest -Method GET -Endpoint "/api/users/search?q=test" -AuthRequired $true
    
    if ($searchResult.Success) {
        Write-TestResult -Test "Search Users" -Result "PASS"
        Add-TestResult -Category "User" -Test "Search Users" -Result "PASS"
    } else {
        Write-TestResult -Test "Search Users" -Result "FAIL"
        Add-TestResult -Category "User" -Test "Search Users" -Result "FAIL"
    }
    
    # Test 4: Get contacts
    $contactsResult = Invoke-ApiRequest -Method GET -Endpoint "/api/users/contacts" -AuthRequired $true
    
    if ($contactsResult.Success) {
        Write-TestResult -Test "Get Contacts" -Result "PASS"
        Add-TestResult -Category "User" -Test "Get Contacts" -Result "PASS"
    } else {
        Write-TestResult -Test "Get Contacts" -Result "FAIL"
        Add-TestResult -Category "User" -Test "Get Contacts" -Result "FAIL"
    }
}

function Test-GroupOperations {
    Write-Log "`n=== Testing Group Operations ===" -Level "TEST"
    
    if (-not $script:authToken) {
        Write-TestResult -Test "Group Tests" -Result "SKIP" -Details "No auth token available"
        Add-TestResult -Category "Group" -Test "All" -Result "SKIP" -Details "No auth token"
        return
    }
    
    # Test 1: Create group
    $groupBody = @{
        name = "Test Group $(Get-Random)"
        description = "Integration test group"
        type = "private"
    }
    
    $result = Invoke-ApiRequest -Method POST -Endpoint "/api/groups" -Body $groupBody -AuthRequired $true
    
    if ($result.Success) {
        $groupId = $result.Content.id
        Write-TestResult -Test "Create Group" -Result "PASS" -Details "Group ID: $groupId"
        Add-TestResult -Category "Group" -Test "Create Group" -Result "PASS"
        
        # Test 2: Get group
        $getResult = Invoke-ApiRequest -Method GET -Endpoint "/api/groups/$groupId" -AuthRequired $true
        
        if ($getResult.Success) {
            Write-TestResult -Test "Get Group" -Result "PASS"
            Add-TestResult -Category "Group" -Test "Get Group" -Result "PASS"
        } else {
            Write-TestResult -Test "Get Group" -Result "FAIL"
            Add-TestResult -Category "Group" -Test "Get Group" -Result "FAIL"
        }
        
        # Test 3: Get group members
        $membersResult = Invoke-ApiRequest -Method GET -Endpoint "/api/groups/$groupId/members" -AuthRequired $true
        
        if ($membersResult.Success) {
            Write-TestResult -Test "Get Group Members" -Result "PASS"
            Add-TestResult -Category "Group" -Test "Get Members" -Result "PASS"
        } else {
            Write-TestResult -Test "Get Group Members" -Result "FAIL"
            Add-TestResult -Category "Group" -Test "Get Members" -Result "FAIL"
        }
        
        # Test 4: Delete group
        $deleteResult = Invoke-ApiRequest -Method DELETE -Endpoint "/api/groups/$groupId" -AuthRequired $true
        
        if ($deleteResult.Success) {
            Write-TestResult -Test "Delete Group" -Result "PASS"
            Add-TestResult -Category "Group" -Test "Delete Group" -Result "PASS"
        } else {
            Write-TestResult -Test "Delete Group" -Result "FAIL"
            Add-TestResult -Category "Group" -Test "Delete Group" -Result "FAIL"
        }
    } else {
        Write-TestResult -Test "Create Group" -Result "FAIL" -Details $result.Error
        Add-TestResult -Category "Group" -Test "Create Group" -Result "FAIL" -Details $result.Error
    }
}

function Test-FileOperations {
    Write-Log "`n=== Testing File Operations ===" -Level "TEST"
    
    if (-not $script:authToken) {
        Write-TestResult -Test "File Tests" -Result "SKIP" -Details "No auth token available"
        Add-TestResult -Category "File" -Test "All" -Result "SKIP" -Details "No auth token"
        return
    }
    
    # Test 1: Initiate upload
    $uploadBody = @{
        fileName = "test.txt"
        fileSize = 1024
        mimeType = "text/plain"
    }
    
    $result = Invoke-ApiRequest -Method POST -Endpoint "/api/files/upload/init" -Body $uploadBody -AuthRequired $true
    
    if ($result.Success) {
        $uploadId = $result.Content.uploadId
        Write-TestResult -Test "Initiate File Upload" -Result "PASS" -Details "Upload ID: $uploadId"
        Add-TestResult -Category "File" -Test "Init Upload" -Result "PASS"
        
        # Test 2: Get upload status
        $statusResult = Invoke-ApiRequest -Method GET -Endpoint "/api/files/upload/$uploadId/status" -AuthRequired $true
        
        if ($statusResult.Success) {
            Write-TestResult -Test "Get Upload Status" -Result "PASS"
            Add-TestResult -Category "File" -Test "Upload Status" -Result "PASS"
        } else {
            Write-TestResult -Test "Get Upload Status" -Result "FAIL"
            Add-TestResult -Category "File" -Test "Upload Status" -Result "FAIL"
        }
    } else {
        Write-TestResult -Test "Initiate File Upload" -Result "FAIL" -Details $result.Error
        Add-TestResult -Category "File" -Test "Init Upload" -Result "FAIL" -Details $result.Error
    }
    
    # Test 3: List files
    $listResult = Invoke-ApiRequest -Method GET -Endpoint "/api/files?limit=10" -AuthRequired $true
    
    if ($listResult.Success) {
        Write-TestResult -Test "List Files" -Result "PASS"
        Add-TestResult -Category "File" -Test "List Files" -Result "PASS"
    } else {
        Write-TestResult -Test "List Files" -Result "FAIL"
        Add-TestResult -Category "File" -Test "List Files" -Result "FAIL"
    }
}

function Test-WebSocketConnection {
    Write-Log "`n=== Testing WebSocket Connection ===" -Level "TEST"
    
    # Note: Full WebSocket testing requires a WebSocket client
    # This is a basic connectivity test
    
    $wsUrl = $Target -replace "http", "ws" -replace "https", "wss"
    $hubUrl = "$wsUrl/hubs/notifications"
    
    Write-TestResult -Test "WebSocket Endpoint" -Result "SKIP" -Details "WebSocket URL: $hubUrl (manual test required)"
    Add-TestResult -Category "WebSocket" -Test "Connection" -Result "SKIP" -Details "Requires WebSocket client"
}

function Export-TestReport {
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }
    
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $reportPath = Join-Path $OutputDir "integration-test-$timestamp.csv"
    $summaryPath = Join-Path $OutputDir "test-summary-$timestamp.txt"
    
    # Export CSV
    $script:testResults | Export-Csv -Path $reportPath -NoTypeInformation -Encoding UTF8
    
    # Calculate summary
    $passCount = ($script:testResults | Where-Object { $_.Result -eq "PASS" }).Count
    $failCount = ($script:testResults | Where-Object { $_.Result -eq "FAIL" }).Count
    $skipCount = ($script:testResults | Where-Object { $_.Result -eq "SKIP" }).Count
    $totalCount = $script:testResults.Count
    
    $summary = @"
LocalTelegram Integration Test Report
=====================================
Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Target: $Target

Summary:
--------
Total Tests: $totalCount
Passed: $passCount
Failed: $failCount
Skipped: $skipCount

Pass Rate: $([math]::Round(($passCount / ($totalCount - $skipCount)) * 100, 1))%

Results by Category:
--------------------
$($script:testResults | Group-Object Category | ForEach-Object {
    $catPass = ($_.Group | Where-Object { $_.Result -eq "PASS" }).Count
    $catTotal = $_.Count
    "  $($_.Name): $catPass/$catTotal passed"
} | Out-String)

Failed Tests:
-------------
$($script:testResults | Where-Object { $_.Result -eq "FAIL" } | ForEach-Object {
    "  - [$($_.Category)] $($_.Test): $($_.Details)"
} | Out-String)

"@
    
    $summary | Out-File -FilePath $summaryPath -Encoding UTF8
    
    Write-Log "`n=== Test Summary ===" -Level "INFO"
    Write-Log "Total: $totalCount | Passed: $passCount | Failed: $failCount | Skipped: $skipCount" -Level "INFO"
    Write-Log "CSV Report: $reportPath" -Level "INFO"
    Write-Log "Summary: $summaryPath" -Level "INFO"
}

function Show-Help {
    Write-Host @"

LocalTelegram Integration Test Script
=====================================

Usage: .\integration-test.ps1 [options]

Options:
    -Target       Target URL (default: http://localhost:5000)
    -TestUser     Test username prefix (default: testuser)
    -TestPassword Test password (default: TestPassword123!)
    -OutputDir    Output directory for reports (default: ./test-results)
    -Verbose      Show detailed output
    -DryRun       Preview without executing

Tests performed:
    - Health endpoints
    - Authentication (register, login, logout)
    - Messaging (send, edit, delete, history)
    - User operations (profile, contacts, search)
    - Group operations (create, get, delete)
    - File operations (upload, list)
    - WebSocket connectivity

Examples:
    .\integration-test.ps1
    .\integration-test.ps1 -Target https://messenger.company.com -Verbose
    .\integration-test.ps1 -OutputDir ./reports

Prerequisites:
    - Server must be running
    - Database must be accessible
    - Test user will be created automatically

"@
}

# Main execution
if ($DryRun) {
    Write-Log "DRY RUN - No actual tests will be performed" -Level "WARNING"
    Show-Help
    exit 0
}

Write-Log "Starting LocalTelegram Integration Tests..." -Level "INFO"
Write-Log "Target: $Target" -Level "INFO"
Write-Log "Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Level "INFO"

Test-HealthEndpoints
Test-AuthFlow
Test-MessagingFlow
Test-UserOperations
Test-GroupOperations
Test-FileOperations
Test-WebSocketConnection

Export-TestReport

# Exit with error code if any tests failed
$failedCount = ($script:testResults | Where-Object { $_.Result -eq "FAIL" }).Count
if ($failedCount -gt 0) {
    exit 1
}
