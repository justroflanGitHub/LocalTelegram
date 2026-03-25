#!/bin/bash
# LocalTelegram Backup Script
# Usage: ./backup.sh [--type full|db|files] [--output path]

set -e

# Default values
BACKUP_TYPE="full"
OUTPUT_PATH="./backups"
RETENTION_DAYS=30

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --type)
            BACKUP_TYPE="$2"
            shift 2
            ;;
        --output)
            OUTPUT_PATH="$2"
            shift 2
            ;;
        --retention)
            RETENTION_DAYS="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_DIR="${OUTPUT_PATH}/${TIMESTAMP}"
LOG_FILE="${OUTPUT_PATH}/backup.log"

log() {
    local message="[$(date '+%Y-%m-%d %H:%M:%S')] $1"
    echo "$message"
    echo "$message" >> "$LOG_FILE"
}

backup_mongodb() {
    log "Starting MongoDB backup..."
    
    local mongo_container=$(docker ps --filter "name=mongodb" --format "{{.Names}}" | head -1)
    
    if [ -z "$mongo_container" ]; then
        log "MongoDB container not found. Attempting local mongodump..."
        local mongo_backup_path="${BACKUP_DIR}/mongodb"
        mkdir -p "$mongo_backup_path"
        
        if mongodump --out "$mongo_backup_path" 2>/dev/null; then
            log "MongoDB backup completed: $mongo_backup_path"
            return 0
        else
            log "ERROR: MongoDB backup failed"
            return 1
        fi
    fi
    
    local mongo_backup_path="${BACKUP_DIR}/mongodb"
    mkdir -p "$mongo_backup_path"
    
    if docker exec "$mongo_container" mongodump --archive > "${mongo_backup_path}/dump.archive" 2>/dev/null; then
        log "MongoDB backup completed: $mongo_backup_path"
        return 0
    else
        log "ERROR: MongoDB backup failed"
        return 1
    fi
}

backup_redis() {
    log "Starting Redis backup..."
    
    local redis_container=$(docker ps --filter "name=redis" --format "{{.Names}}" | head -1)
    
    if [ -z "$redis_container" ]; then
        log "Redis container not found. Skipping Redis backup."
        return 0
    fi
    
    local redis_backup_path="${BACKUP_DIR}/redis"
    mkdir -p "$redis_backup_path"
    
    docker exec "$redis_container" redis-cli BGSAVE >/dev/null 2>&1
    sleep 2
    
    if docker cp "${redis_container}:/data/dump.rdb" "${redis_backup_path}/dump.rdb" 2>/dev/null; then
        log "Redis backup completed: $redis_backup_path"
        return 0
    else
        log "ERROR: Redis backup failed"
        return 1
    fi
}

backup_minio() {
    log "Starting MinIO backup..."
    
    local minio_container=$(docker ps --filter "name=minio" --format "{{.Names}}" | head -1)
    
    if [ -z "$minio_container" ]; then
        log "MinIO container not found. Skipping MinIO backup."
        return 0
    fi
    
    local minio_backup_path="${BACKUP_DIR}/minio"
    mkdir -p "$minio_backup_path"
    
    # Get list of buckets and backup each
    local buckets=$(docker exec "$minio_container" mc ls local --json 2>/dev/null | jq -r '.key' 2>/dev/null)
    
    if [ -n "$buckets" ]; then
        echo "$buckets" | while read -r bucket; do
            if [ -n "$bucket" ]; then
                log "Backing up bucket: $bucket"
                local bucket_path="${minio_backup_path}/${bucket}"
                mkdir -p "$bucket_path"
                docker exec "$minio_container" mc mirror "local/${bucket}" "/backup/${bucket}" >/dev/null 2>&1 || true
            fi
        done
    fi
    
    log "MinIO backup completed: $minio_backup_path"
    return 0
}

backup_config() {
    log "Starting configuration backup..."
    
    local config_backup_path="${BACKUP_DIR}/config"
    mkdir -p "$config_backup_path"
    
    # Backup Docker Compose files
    cp docker-compose*.yml "$config_backup_path" 2>/dev/null || true
    
    # Backup environment files
    cp .env* "$config_backup_path" 2>/dev/null || true
    
    # Backup Prometheus config
    if [ -d "monitoring/prometheus" ]; then
        cp -r monitoring/prometheus "$config_backup_path" 2>/dev/null || true
    fi
    
    # Backup Grafana dashboards
    if [ -d "monitoring/grafana" ]; then
        cp -r monitoring/grafana "$config_backup_path" 2>/dev/null || true
    fi
    
    log "Configuration backup completed: $config_backup_path"
    return 0
}

compress_backup() {
    log "Compressing backup..."
    
    local zip_path="${BACKUP_DIR}.zip"
    
    if command -v zip &> /dev/null; then
        zip -r -q "$zip_path" "$BACKUP_DIR"
    else
        tar -czf "${BACKUP_DIR}.tar.gz" -C "$(dirname "$BACKUP_DIR")" "$(basename "$BACKUP_DIR")"
        zip_path="${BACKUP_DIR}.tar.gz"
    fi
    
    # Remove uncompressed directory
    rm -rf "$BACKUP_DIR"
    
    local size=$(du -h "$zip_path" | cut -f1)
    log "Backup compressed: $zip_path ($size)"
    
    return 0
}

remove_old_backups() {
    log "Cleaning up old backups (older than $RETENTION_DAYS days)..."
    
    find "$OUTPUT_PATH" -name "*.zip" -type f -mtime +$RETENTION_DAYS -delete 2>/dev/null || true
    find "$OUTPUT_PATH" -name "*.tar.gz" -type f -mtime +$RETENTION_DAYS -delete 2>/dev/null || true
}

# Main execution
main() {
    # Create output directory
    mkdir -p "$OUTPUT_PATH"
    
    # Create backup directory
    mkdir -p "$BACKUP_DIR"
    
    log "Starting $BACKUP_TYPE backup..."
    
    local success=0
    
    case $BACKUP_TYPE in
        full)
            backup_mongodb || success=1
            backup_redis || success=1
            backup_minio || success=1
            backup_config || success=1
            ;;
        db)
            backup_mongodb || success=1
            backup_redis || success=1
            ;;
        files)
            backup_minio || success=1
            backup_config || success=1
            ;;
        *)
            log "ERROR: Unknown backup type: $BACKUP_TYPE"
            exit 1
            ;;
    esac
    
    if [ $success -eq 0 ]; then
        compress_backup
        remove_old_backups
        log "Backup completed successfully!"
        exit 0
    else
        log "Backup completed with errors!"
        exit 1
    fi
}

main
