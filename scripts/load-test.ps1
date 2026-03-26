# LocalTelegram Load Testing Script
# Requires k6 (https://k6.io/) for load testing

param(
    [string]$Target = "http://localhost:5000",
    [string]$Scenario = "mixed",     # auth, messaging, mixed, full
    [int]$VUs = 100,                 # Virtual Users
    [int]$Duration = "5m",           # Test duration
    [string]$OutputDir = "./load-test-results",
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

function Test-K6Installed {
    $null = Get-Command "k6" -ErrorAction SilentlyContinue
    return $?
}

function New-OutputDirectory {
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }
}

# Auth scenario test script
$authScenario = @"
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

const errorRate = new Rate('errors');
const loginDuration = new Trend('login_duration');

export const options = {
    stages: [
        { duration: '30s', target: $VUs },
        { duration: '$Duration', target: $VUs },
        { duration: '30s', target: 0 },
    ],
    thresholds: {
        http_req_duration: ['p(95)<500', 'p(99)<1000'],
        errors: ['rate<0.1'],
        login_duration: ['p(95)<300'],
    },
};

const BASE_URL = '$Target';

export default function() {
    // Register user
    const registerPayload = JSON.stringify({
        phoneNumber: \`+7999\${__VU}\${__ITER.toString().padStart(7, '0')}\`,
        username: \`testuser_\${__VU}_\${__ITER}\`,
        password: 'TestPassword123!',
        firstName: 'Test',
        lastName: 'User'
    });

    const registerRes = http.post(\`\${BASE_URL}/api/auth/register\`, registerPayload, {
        headers: { 'Content-Type': 'application/json' },
    });

    check(registerRes, {
        'register status 200 or 409': (r) => r.status === 200 || r.status === 409,
    });

    // Login
    const loginPayload = JSON.stringify({
        username: \`testuser_\${__VU}_\${__ITER}\`,
        password: 'TestPassword123!'
    });

    const loginStart = new Date();
    const loginRes = http.post(\`\${BASE_URL}/api/auth/login\`, loginPayload, {
        headers: { 'Content-Type': 'application/json' },
    });
    loginDuration.add(new Date() - loginStart);

    check(loginRes, {
        'login status 200': (r) => r.status === 200,
        'has token': (r) => r.json('token') !== undefined,
    });

    errorRate.add(loginRes.status !== 200);

    sleep(1);
}
"@

# Messaging scenario test script
$messagingScenario = @"
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';

const errorRate = new Rate('errors');
const messageLatency = new Trend('message_latency');
const messagesSent = new Counter('messages_sent');

export const options = {
    stages: [
        { duration: '30s', target: $VUs },
        { duration: '$Duration', target: $VUs },
        { duration: '30s', target: 0 },
    ],
    thresholds: {
        http_req_duration: ['p(95)<300', 'p(99)<500'],
        errors: ['rate<0.05'],
        message_latency: ['p(95)<200'],
    },
};

const BASE_URL = '$Target';

// Pre-authenticate and get token
let authToken = '';

export function setup() {
    // Create test users and get tokens
    const tokens = [];
    for (let i = 0; i < 10; i++) {
        const loginRes = http.post(\`\${BASE_URL}/api/auth/login\`, JSON.stringify({
            username: \`loadtest_user_\${i}\`,
            password: 'LoadTest123!'
        }), { headers: { 'Content-Type': 'application/json' } });
        
        if (loginRes.status === 200) {
            tokens.push(loginRes.json('token'));
        }
    }
    return { tokens };
}

export default function(data) {
    const token = data.tokens[__VU % data.tokens.length];
    if (!token) {
        errorRate.add(1);
        return;
    }

    const headers = {
        'Authorization': \`Bearer \${token}\`,
        'Content-Type': 'application/json'
    };

    // Send message
    const messagePayload = JSON.stringify({
        recipientId: \`user_\${(__VU + 1) % 10}\`,
        content: \`Load test message from VU \${__VU} iteration \${__ITER}\`,
        type: 'text'
    });

    const msgStart = new Date();
    const msgRes = http.post(\`\${BASE_URL}/api/messages/send\`, messagePayload, { headers });
    messageLatency.add(new Date() - msgStart);
    messagesSent.add(1);

    check(msgRes, {
        'message sent': (r) => r.status === 200,
    });

    errorRate.add(msgRes.status !== 200);

    // Get messages
    const getMsgRes = http.get(\`\${BASE_URL}/api/messages/history?limit=50\`, { headers });
    
    check(getMsgRes, {
        'got history': (r) => r.status === 200,
    });

    sleep(Math.random() * 2 + 0.5);  // Random sleep 0.5-2.5s
}
"@

# Mixed scenario (auth + messaging + groups)
$mixedScenario = @"
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { randomIntBetween, randomItem } from 'k6/utils';

const errorRate = new Rate('errors');
const apiLatency = new Trend('api_latency');
const requestsCount = new Counter('total_requests');

export const options = {
    stages: [
        { duration: '1m', target: $VUs },
        { duration: '$Duration', target: $VUs },
        { duration: '1m', target: 0 },
    ],
    thresholds: {
        http_req_duration: ['p(95)<500', 'p(99)<1000'],
        errors: ['rate<0.1'],
    },
};

const BASE_URL = '$Target';

export function setup() {
    // Setup test data
    return { startTime: Date.now() };
}

export default function(data) {
    const actions = ['login', 'sendMessage', 'getHistory', 'getProfile', 'searchUsers'];
    const action = randomItem(actions);
    
    let res;
    const start = new Date();

    switch (action) {
        case 'login':
            res = http.post(\`\${BASE_URL}/api/auth/login\`, JSON.stringify({
                username: \`user_\${__VU % 100}\`,
                password: 'TestPassword123!'
            }), { headers: { 'Content-Type': 'application/json' } });
            break;

        case 'sendMessage':
            res = http.post(\`\${BASE_URL}/api/messages/send\`, JSON.stringify({
                recipientId: \`user_\${(__VU + 1) % 100}\`,
                content: \`Test message \${Date.now()}\`,
                type: 'text'
            }), { headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer test_token' } });
            break;

        case 'getHistory':
            res = http.get(\`\${BASE_URL}/api/messages/history?limit=20\`, {
                headers: { 'Authorization': 'Bearer test_token' }
            });
            break;

        case 'getProfile':
            res = http.get(\`\${BASE_URL}/api/users/profile\`, {
                headers: { 'Authorization': 'Bearer test_token' }
            });
            break;

        case 'searchUsers':
            res = http.get(\`\${BASE_URL}/api/users/search?q=test\`, {
                headers: { 'Authorization': 'Bearer test_token' }
            });
            break;
    }

    apiLatency.add(new Date() - start);
    requestsCount.add(1);
    errorRate.add(res.status >= 400);

    check(res, {
        'status OK': (r) => r.status < 400,
    });

    sleep(randomIntBetween(1, 3));
}
"@

# Full stress test scenario
$fullScenario = @"
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { randomIntBetween, randomItem } from 'k6/utils';

const errorRate = new Rate('errors');
const apiLatency = new Trend('api_latency');
const requestsCount = new Counter('total_requests');

export const options = {
    // Stress test: ramp up beyond expected capacity
    stages: [
        { duration: '2m', target: 50 },
        { duration: '2m', target: 100 },
        { duration: '2m', target: 250 },
        { duration: '2m', target: 500 },
        { duration: '2m', target: 1000 },
        { duration: '5m', target: 1000 },  // Sustained high load
        { duration: '2m', target: 0 },
    ],
    thresholds: {
        http_req_duration: ['p(95)<1000', 'p(99)<2000'],
        errors: ['rate<0.2'],  // Allow higher error rate under stress
    },
};

const BASE_URL = '$Target';

export default function() {
    const actions = [
        { weight: 30, fn: () => http.post(\`\${BASE_URL}/api/auth/login\`, '{}', { headers: { 'Content-Type': 'application/json' } }) },
        { weight: 40, fn: () => http.get(\`\${BASE_URL}/api/messages/history\`) },
        { weight: 15, fn: () => http.post(\`\${BASE_URL}/api/messages/send\`, '{}', { headers: { 'Content-Type': 'application/json' } }) },
        { weight: 10, fn: () => http.get(\`\${BASE_URL}/api/users/profile\`) },
        { weight: 5, fn: () => http.get(\`\${BASE_URL}/api/groups\`) },
    ];

    // Weighted random selection
    const totalWeight = actions.reduce((sum, a) => sum + a.weight, 0);
    let random = Math.random() * totalWeight;
    let selectedAction = actions[0];
    for (const action of actions) {
        random -= action.weight;
        if (random <= 0) {
            selectedAction = action;
            break;
        }
    }

    const start = new Date();
    const res = selectedAction.fn();
    apiLatency.add(new Date() - start);
    requestsCount.add(1);
    errorRate.add(res.status >= 400);

    check(res, {
        'status < 500': (r) => r.status < 500,
    });

    sleep(randomIntBetween(0.1, 1));  // Aggressive timing for stress test
}
"@

function Invoke-LoadTest {
    param([string]$ScriptContent, [string]$ScenarioName)
    
    New-OutputDirectory
    
    $scriptPath = Join-Path $OutputDir "test-$ScenarioName.js"
    $resultPath = Join-Path $OutputDir "results-$ScenarioName.json"
    
    # Save test script
    $ScriptContent | Out-File -FilePath $scriptPath -Encoding UTF8
    Write-Log "Test script saved to: $scriptPath" -Level "INFO"
    
    if ($DryRun) {
        Write-Log "[DRY RUN] Would run: k6 run $scriptPath --out json=$resultPath" -Level "INFO"
        return
    }
    
    Write-Log "Starting load test: $ScenarioName" -Level "INFO"
    Write-Log "Target: $Target" -Level "INFO"
    Write-Log "Virtual Users: $VUs" -Level "INFO"
    Write-Log "Duration: $Duration" -Level "INFO"
    
    # Run k6
    $env:K6_WEB_DASHBOARD = "true"
    $env:K6_WEB_DASHBOARD_HOST = "localhost"
    $env:K6_WEB_DASHBOARD_PORT = "5665"
    
    k6 run $scriptPath --out json=$resultPath
    
    if ($LASTEXITCODE -eq 0) {
        Write-Log "Load test completed successfully!" -Level "SUCCESS"
        Write-Log "Results saved to: $resultPath" -Level "INFO"
    }
    else {
        Write-Log "Load test failed with exit code: $LASTEXITCODE" -Level "ERROR"
    }
}

function Show-Results {
    param([string]$ScenarioName)
    
    $resultPath = Join-Path $OutputDir "results-$ScenarioName.json"
    
    if (-not (Test-Path $resultPath)) {
        Write-Log "No results found for scenario: $ScenarioName" -Level "WARNING"
        return
    }
    
    Write-Log "`n========== Load Test Results ==========" -Level "INFO"
    Write-Log "Scenario: $ScenarioName" -Level "INFO"
    
    # Parse and display summary
    # k6 outputs JSON lines, we'll read the summary
    $content = Get-Content $resultPath -Raw
    Write-Log "Results file: $resultPath" -Level "INFO"
    Write-Log "View detailed results at: http://localhost:5665 (when k6 is running)" -Level "INFO"
}

function Show-Help {
    Write-Host @"

LocalTelegram Load Testing Script
=================================

Usage: .\load-test.ps1 -Scenario <scenario> [options]

Scenarios:
    auth       - Test authentication endpoints (register, login)
    messaging  - Test message sending and retrieval
    mixed      - Mixed operations (auth + messaging + users)
    full       - Full stress test (1000+ users)

Options:
    -Target      Target URL (default: http://localhost:5000)
    -VUs         Number of virtual users (default: 100)
    -Duration    Test duration (default: 5m)
    -OutputDir   Output directory for results (default: ./load-test-results)
    -DryRun      Preview without executing

Prerequisites:
    Install k6: winget install k6

Examples:
    # Basic auth load test
    .\load-test.ps1 -Scenario auth -VUs 50 -Duration 2m

    # Full stress test
    .\load-test.ps1 -Scenario full -VUs 500 -Duration 10m

    # Test production server
    .\load-test.ps1 -Scenario mixed -Target https://messenger.company.com

Thresholds:
    - p(95) latency < 500ms for normal operations
    - p(99) latency < 1000ms
    - Error rate < 10%

"@
}

# Main execution
if (-not (Test-K6Installed) -and -not $DryRun) {
    Write-Log "k6 is not installed. Installing via winget..." -Level "WARNING"
    winget install k6
}

switch ($Scenario.ToLower()) {
    "auth" { Invoke-LoadTest -ScriptContent $authScenario -ScenarioName "auth" }
    "messaging" { Invoke-LoadTest -ScriptContent $messagingScenario -ScenarioName "messaging" }
    "mixed" { Invoke-LoadTest -ScriptContent $mixedScenario -ScenarioName "mixed" }
    "full" { Invoke-LoadTest -ScriptContent $fullScenario -ScenarioName "full" }
    "help" { Show-Help }
    default { Show-Help }
}
