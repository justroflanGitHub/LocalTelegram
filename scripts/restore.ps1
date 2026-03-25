# LocalTelegram Restore Script
# Usage: .\restore.ps1 -BackupFile path\to\backup.zip [-Components db,files,config]

param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFile,
    
    [ValidateSet("db", "files", "config", "all")]
    [string[]]$Components = @("all"),
    
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$TempDir = Join-Path $env:TEMP "LocalTelegram_Restore_$(Get-Random)"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $color = switch ($Level) {
        "ERROR" { "Red" }
        "WARN" { "Yellow" }
        default { "White" }
    }
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] [$Level] $Message" -ForegroundColor $color
}

function Expand-Backup {
    Write-Log "Extracting backup file: $BackupFile"
    
    if (-not (Test-Path $BackupFile)) {
        Write-Log "Backup file not found: $BackupFile" "ERROR"
        exit 1
    }
    
    New-Item -ItemType Directory -Path $TempDir -Force | Out-Null
    
    if ($BackupFile -like "*.zip") {
        Expand-Archive -Path $BackupFile -DestinationPath $TempDir -Force
    } elseif ($BackupFile -like "*.tar.gz") {
        tar -xzf $BackupFile -C $TempDir
    } else {
        Write-Log "Unsupported backup format" "ERROR"
        exit 1
    }
    
    # Find the extracted directory
    $extractedDir = Get-ChildItem -Path $TempDir -Directory | Select-Object -First 1
    return $extractedDir.FullName
}

function Restore-MongoDB {
    param([string]$BackupPath)
    
    Write-Log "Starting MongoDB restore..."
    
    $mongoBackupPath = Join-Path $BackupPath "mongodb"
    if (-not (Test-Path $mongoBackupPath)) {
        Write-Log "MongoDB backup not found in archive" "WARN"
        return
    }
    
    $mongoContainer = docker ps --filter "name=mongodb" --format "{{.Names}}" | Select-Object -First 1
    
    $archiveFile = Join-Path $mongoBackupPath "dump.archive"
    $dumpDir = Join-Path $mongoBackupPath "dump"
    
    if ($mongoContainer) {
        if (Test-Path $archiveFile) {
            Get-Content $archiveFile -Raw | docker exec -i $mongoContainer mongorestore --archive
        } elseif (Test-Path $dumpDir) {
            docker cp $dumpDir "${mongoContainer}:/tmp/dump"
            docker exec $mongoContainer mongorestore /tmp/dump
        }
        Write-Log "MongoDB restore completed"
    } else {
        if (Test-Path $archiveFile) {
            mongorestore --archive=$archiveFile
        } elseif (Test-Path $dumpDir) {
            mongorestore $dumpDir
        }
        Write-Log "MongoDB restore completed (local)"
    }
}

function Restore-Redis {
    param([string]$BackupPath)
    
    Write-Log "Starting Redis restore..."
    
    $redisBackupPath = Join-Path $BackupPath "redis"
    if (-not (Test-Path $redisBackupPath)) {
        Write-Log "Redis backup not found in archive" "WARN"
        return
    }
    
    $redisContainer = docker ps --filter "name=redis" --format "{{.Names}}" | Select-Object -First 1
    
    if ($redisContainer) {
        $rdbFile = Join-Path $redisBackupPath "dump.rdb"
        if (Test-Path $rdbFile) {
            # Stop Redis, copy file, start Redis
            docker exec $redisContainer redis-cli SHUTDOWN NOSAVE 2>$null || true
            Start-Sleep -Seconds 2
            
            docker cp $rdbFile "${redisContainer}:/data/dump.rdb"
            
            docker restart $redisContainer
            Write-Log "Redis restore completed"
        }
    } else {
        Write-Log "Redis container not found. Skipping Redis restore." "WARN"
    }
}

function Restore-MinIO {
    param([string]$BackupPath)
    
    Write-Log "Starting MinIO restore..."
    
    $minioBackupPath = Join-Path $BackupPath "minio"
    if (-not (Test-Path $minioBackupPath)) {
        Write-Log "MinIO backup not found in archive" "WARN"
        return
    }
    
    $minioContainer = docker ps --filter "name=minio" --format "{{.Names}}" | Select-Object -First 1
    
    if ($minioContainer) {
        # Restore each bucket
        Get-ChildItem -Path $minioBackupPath -Directory | ForEach-Object {
            $bucketName = $_.Name
            Write-Log "Restoring bucket: $bucketName"
            
            # Create bucket if not exists
            docker exec $minioContainer mc mb "local/${bucketName}" 2>$null || true
            
            # Copy files back
            docker cp $_.FullName "${minioContainer}:/tmp/${bucketName}"
            docker exec $minioContainer mc mirror "/tmp/${bucketName}" "local/${bucketName}"
        }
        Write-Log "MinIO restore completed"
    } else {
        Write-Log "MinIO container not found. Skipping MinIO restore." "WARN"
    }
}

function Restore-Config {
    param([string]$BackupPath)
    
    Write-Log "Starting configuration restore..."
    
    $configBackupPath = Join-Path $BackupPath "config"
    if (-not (Test-Path $configBackupPath)) {
        Write-Log "Config backup not found in archive" "WARN"
        return
    }
    
    if (-not $Force) {
        $response = Read-Host "This will overwrite current configuration files. Continue? (y/N)"
        if ($response -ne "y" -and $response -ne "Y") {
            Write-Log "Configuration restore cancelled"
            return
        }
    }
    
    # Restore Docker Compose files
    Copy-Item -Path "$configBackupPath/docker-compose*.yml" -Destination "." -Force -ErrorAction SilentlyContinue
    
    # Restore environment files
    Copy-Item -Path "$configBackupPath/.env*" -Destination "." -Force -ErrorAction SilentlyContinue
    
    # Restore Prometheus config
    if (Test-Path "$configBackupPath/prometheus") {
        if (-not (Test-Path "monitoring/prometheus")) {
            New-Item -ItemType Directory -Path "monitoring/prometheus" -Force | Out-Null
        }
        Copy-Item -Path "$configBackupPath/prometheus/*" -Destination "monitoring/prometheus" -Recurse -Force
    }
    
    # Restore Grafana dashboards
    if (Test-Path "$configBackupPath/grafana") {
        if (-not (Test-Path "monitoring/grafana")) {
            New-Item -ItemType Directory -Path "monitoring/grafana" -Force | Out-Null
        }
        Copy-Item -Path "$configBackupPath/grafana/*" -Destination "monitoring/grafana" -Recurse -Force
    }
    
    Write-Log "Configuration restore completed"
}

function Cleanup {
    if (Test-Path $TempDir) {
        Remove-Item -Path $TempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Main execution
try {
    Write-Log "Starting restore process..."
    Write-Log "Backup file: $BackupFile"
    Write-Log "Components: $($Components -join ', ')"
    
    $extractedPath = Expand-Backup
    Write-Log "Backup extracted to: $extractedPath"
    
    $shouldRestoreAll = $Components -contains "all"
    
    if ($shouldRestoreAll -or $Components -contains "db") {
        Restore-MongoDB -BackupPath $extractedPath
        Restore-Redis -BackupPath $extractedPath
    }
    
    if ($shouldRestoreAll -or $Components -contains "files") {
        Restore-MinIO -BackupPath $extractedPath
    }
    
    if ($shouldRestoreAll -or $Components -contains "config") {
        Restore-Config -BackupPath $extractedPath
    }
    
    Write-Log "Restore completed successfully!"
} catch {
    Write-Log "Restore failed: $($_.Exception.Message)" "ERROR"
    exit 1
} finally {
    Cleanup
}
