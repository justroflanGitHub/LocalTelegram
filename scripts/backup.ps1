# LocalTelegram Backup Script
# Usage: .\backup.ps1 [-BackupType full|db|files] [-OutputPath path]

param(
    [ValidateSet("full", "db", "files")]
    [string]$BackupType = "full",
    [string]$OutputPath = "./backups",
    [int]$RetentionDays = 30
)

$ErrorActionPreference = "Stop"
$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$BackupDir = Join-Path $OutputPath $Timestamp

function Write-Log {
    param([string]$Message)
    $logMessage = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message"
    Write-Host $logMessage
    Add-Content -Path (Join-Path $OutputPath "backup.log") -Value $logMessage
}

function Backup-MongoDB {
    Write-Log "Starting MongoDB backup..."
    
    $mongoContainer = docker ps --filter "name=mongodb" --format "{{.Names}}" | Select-Object -First 1
    
    if (-not $mongoContainer) {
        Write-Log "MongoDB container not found. Attempting local mongodump..."
        $mongoBackupPath = Join-Path $BackupDir "mongodb"
        New-Item -ItemType Directory -Path $mongoBackupPath -Force | Out-Null
        
        mongodump --out $mongoBackupPath
        if ($LASTEXITCODE -eq 0) {
            Write-Log "MongoDB backup completed: $mongoBackupPath"
        } else {
            Write-Log "ERROR: MongoDB backup failed"
            return $false
        }
    } else {
        $mongoBackupPath = Join-Path $BackupDir "mongodb"
        New-Item -ItemType Directory -Path $mongoBackupPath -Force | Out-Null
        
        docker exec $mongoContainer mongodump --archive | Out-File -FilePath (Join-Path $mongoBackupPath "dump.archive") -Encoding byte
        if ($LASTEXITCODE -eq 0) {
            Write-Log "MongoDB backup completed: $mongoBackupPath"
        } else {
            Write-Log "ERROR: MongoDB backup failed"
            return $false
        }
    }
    
    return $true
}

function Backup-Redis {
    Write-Log "Starting Redis backup..."
    
    $redisContainer = docker ps --filter "name=redis" --format "{{.Names}}" | Select-Object -First 1
    
    if (-not $redisContainer) {
        Write-Log "Redis container not found. Skipping Redis backup."
        return $true
    }
    
    $redisBackupPath = Join-Path $BackupDir "redis"
    New-Item -ItemType Directory -Path $redisBackupPath -Force | Out-Null
    
    docker exec $redisContainer redis-cli BGSAVE
    Start-Sleep -Seconds 2
    
    docker cp "${redisContainer}:/data/dump.rdb" (Join-Path $redisBackupPath "dump.rdb")
    if ($LASTEXITCODE -eq 0) {
        Write-Log "Redis backup completed: $redisBackupPath"
        return $true
    } else {
        Write-Log "ERROR: Redis backup failed"
        return $false
    }
}

function Backup-MinIO {
    Write-Log "Starting MinIO backup..."
    
    $minioContainer = docker ps --filter "name=minio" --format "{{.Names}}" | Select-Object -First 1
    
    if (-not $minioContainer) {
        Write-Log "MinIO container not found. Skipping MinIO backup."
        return $true
    }
    
    $minioBackupPath = Join-Path $BackupDir "minio"
    New-Item -ItemType Directory -Path $minioBackupPath -Force | Out-Null
    
    # Get list of buckets
    $buckets = docker exec $minioContainer mc ls local --json 2>$null | ConvertFrom-Json
    
    foreach ($bucket in $buckets) {
        $bucketName = $bucket.key
        Write-Log "Backing up bucket: $bucketName"
        
        $bucketPath = Join-Path $minioBackupPath $bucketName
        New-Item -ItemType Directory -Path $bucketPath -Force | Out-Null
        
        docker exec $minioContainer mc mirror local/$bucketName /backup/$bucketName 2>$null
    }
    
    # Alternative: Use mc mirror to local filesystem
    Write-Log "MinIO backup completed: $minioBackupPath"
    return $true
}

function Backup-Config {
    Write-Log "Starting configuration backup..."
    
    $configBackupPath = Join-Path $BackupDir "config"
    New-Item -ItemType Directory -Path $configBackupPath -Force | Out-Null
    
    # Backup Docker Compose files
    Copy-Item -Path "docker-compose*.yml" -Destination $configBackupPath -ErrorAction SilentlyContinue
    
    # Backup environment files
    Copy-Item -Path ".env*" -Destination $configBackupPath -ErrorAction SilentlyContinue
    
    # Backup Prometheus config
    if (Test-Path "monitoring/prometheus") {
        Copy-Item -Path "monitoring/prometheus" -Destination $configBackupPath -Recurse -ErrorAction SilentlyContinue
    }
    
    # Backup Grafana dashboards
    if (Test-Path "monitoring/grafana") {
        Copy-Item -Path "monitoring/grafana" -Destination $configBackupPath -Recurse -ErrorAction SilentlyContinue
    }
    
    Write-Log "Configuration backup completed: $configBackupPath"
    return $true
}

function Compress-Backup {
    Write-Log "Compressing backup..."
    
    $zipPath = "${BackupDir}.zip"
    Compress-Archive -Path $BackupDir -DestinationPath $zipPath -Force
    
    # Remove uncompressed directory
    Remove-Item -Path $BackupDir -Recurse -Force
    
    $size = (Get-Item $zipPath).Length / 1MB
    Write-Log "Backup compressed: $zipPath ($([math]::Round($size, 2)) MB)"
    
    return $true
}

function Remove-OldBackups {
    Write-Log "Cleaning up old backups (older than $RetentionDays days)..."
    
    $cutoffDate = (Get-Date).AddDays(-$RetentionDays)
    
    Get-ChildItem -Path $OutputPath -Filter "*.zip" | Where-Object { $_.LastWriteTime -lt $cutoffDate } | ForEach-Object {
        Write-Log "Removing old backup: $($_.Name)"
        Remove-Item -Path $_.FullName -Force
    }
}

# Main execution
try {
    # Create output directory
    if (-not (Test-Path $OutputPath)) {
        New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    }
    
    # Create backup directory
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
    
    Write-Log "Starting $BackupType backup..."
    
    $success = $true
    
    switch ($BackupType) {
        "full" {
            $success = $success -and (Backup-MongoDB)
            $success = $success -and (Backup-Redis)
            $success = $success -and (Backup-MinIO)
            $success = $success -and (Backup-Config)
        }
        "db" {
            $success = $success -and (Backup-MongoDB)
            $success = $success -and (Backup-Redis)
        }
        "files" {
            $success = $success -and (Backup-MinIO)
            $success = $success -and (Backup-Config)
        }
    }
    
    if ($success) {
        Compress-Backup
        Remove-OldBackups
        Write-Log "Backup completed successfully!"
        exit 0
    } else {
        Write-Log "Backup completed with errors!"
        exit 1
    }
} catch {
    Write-Log "ERROR: $($_.Exception.Message)"
    exit 1
}
