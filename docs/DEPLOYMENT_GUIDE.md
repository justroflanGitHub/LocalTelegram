# LocalTelegram Deployment Guide

> Complete guide for deploying LocalTelegram to production
> 
> Version: 1.0 | Last updated: March 2026

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Server Requirements](#server-requirements)
3. [Docker Deployment](#docker-deployment)
4. [Kubernetes Deployment](#kubernetes-deployment)
5. [SSL/TLS Configuration](#ssltls-configuration)
6. [TURN Server Setup](#turn-server-setup)
7. [LDAP/AD Integration](#ldapad-integration)
8. [Monitoring Setup](#monitoring-setup)
9. [Backup Strategy](#backup-strategy)
10. [Troubleshooting](#troubleshooting)

---

## Prerequisites

### Required Software

- Docker 24.0+
- Docker Compose 2.20+
- Kubernetes 1.28+ (for K8s deployment)
- Helm 3.12+ (for K8s deployment)
- OpenSSL (for certificate generation)

### Required Ports

| Port | Service | Description |
|------|---------|-------------|
| 80 | nginx | HTTP (redirects to HTTPS) |
| 443 | nginx | HTTPS |
| 5000-5010 | Services | Microservices (internal) |
| 27017 | MongoDB | Database |
| 6379 | Redis | Cache |
| 5672 | RabbitMQ | Message queue |
| 9000 | MinIO | Object storage |
| 3478 | coturn | STUN/TURN |
| 5349 | coturn | TURN TLS |
| 49152-65535 | coturn | TURN relay ports |
| 7880 | LiveKit | SFU |

---

## Server Requirements

### Minimum (Development/Testing)

| Component | Requirement |
|-----------|-------------|
| CPU | 4 cores |
| RAM | 8 GB |
| Storage | 100 GB SSD |
| Network | 100 Mbps |

### Recommended (Production - 1000 users)

| Component | Requirement |
|-----------|-------------|
| CPU | 8 cores |
| RAM | 16 GB |
| Storage | 500 GB SSD |
| Network | 1 Gbps |

### High-Scale (Production - 10000+ users)

| Component | Requirement |
|-----------|-------------|
| CPU | 16+ cores |
| RAM | 32+ GB |
| Storage | 2+ TB SSD (distributed) |
| Network | 10 Gbps |

---

## Docker Deployment

### 1. Clone Repository

```bash
git clone https://github.com/your-org/LocalTelegram.git
cd LocalTelegram
```

### 2. Configure Environment

```bash
cp .env.example .env
```

Edit `.env` with your settings:

```env
# Domain
DOMAIN=telegram.yourcompany.com

# MongoDB
MONGODB_URI=mongodb://mongodb:27017/localtelegram
MONGODB_USER=admin
MONGODB_PASSWORD=your_secure_password

# Redis
REDIS_URL=redis://redis:6379
REDIS_PASSWORD=your_redis_password

# RabbitMQ
RABBITMQ_URL=amqp://rabbitmq:5672
RABBITMQ_USER=admin
RABBITMQ_PASSWORD=your_rabbitmq_password

# MinIO
MINIO_ENDPOINT=minio:9000
MINIO_ACCESS_KEY=minioadmin
MINIO_SECRET_KEY=your_minio_secret

# JWT
JWT_SECRET=your_jwt_secret_minimum_32_characters
JWT_EXPIRATION=900

# TURN Server
TURN_SECRET=your_turn_secret
TURN_DOMAIN=telegram.yourcompany.com

# LiveKit
LIVEKIT_API_KEY=your_api_key
LIVEKIT_API_SECRET=your_api_secret

# LDAP (optional)
LDAP_ENABLED=false
LDAP_SERVER=ldap://ldap.yourcompany.com
LDAP_BASE_DN=dc=yourcompany,dc=com
LDAP_BIND_DN=cn=admin,dc=yourcompany,dc=com
LDAP_BIND_PASSWORD=your_ldap_password
```

### 3. Generate Secrets

```bash
# Generate JWT secret
openssl rand -base64 32

# Generate TURN secret
openssl rand -hex 16

# Generate MinIO secret
openssl rand -base64 32
```

### 4. Start Services

```bash
# Start all services
docker-compose up -d

# Check status
docker-compose ps

# View logs
docker-compose logs -f
```

### 5. Initialize Database

```bash
# Run migrations
docker-compose exec authservice dotnet ef database update
docker-compose exec messageservice dotnet ef database update
```

### 6. Create Admin User

```bash
docker-compose exec authservice dotnet run --seed-admin
```

---

## Kubernetes Deployment

### 1. Create Namespace

```bash
kubectl apply -f k8s/namespace.yaml
```

### 2. Create Secrets

```bash
# Update secrets file with your values
cp k8s/secrets.yaml k8s/secrets.local.yaml

# Edit secrets
nano k8s/secrets.local.yaml

# Apply secrets
kubectl apply -f k8s/secrets.local.yaml
```

### 3. Deploy Infrastructure

```bash
# Deploy MongoDB, Redis, RabbitMQ, MinIO
kubectl apply -f k8s/infrastructure.yaml

# Wait for pods to be ready
kubectl wait --for=condition=ready pod -l app=mongodb -n localtelegram --timeout=300s
kubectl wait --for=condition=ready pod -l app=redis -n localtelegram --timeout=300s
```

### 4. Deploy Services

```bash
# Deploy all microservices
kubectl apply -f k8s/services.yaml

# Wait for deployments
kubectl rollout status deployment/authservice -n localtelegram
kubectl rollout status deployment/messageservice -n localtelegram
kubectl rollout status deployment/userservice -n localtelegram
kubectl rollout status deployment/groupservice -n localtelegram
kubectl rollout status deployment/fileservice -n localtelegram
kubectl rollout status deployment/mediaservice -n localtelegram
kubectl rollout status deployment/conferenceservice -n localtelegram
kubectl rollout status deployment/adminservice -n localtelegram
kubectl rollout status deployment/apigateway -n localtelegram
kubectl rollout status deployment/pushservice -n localtelegram
```

### 5. Deploy Ingress

```bash
# Deploy ingress
kubectl apply -f k8s/ingress.yaml
```

### 6. Verify Deployment

```bash
# Check all pods
kubectl get pods -n localtelegram

# Check services
kubectl get services -n localtelegram

# Check ingress
kubectl get ingress -n localtelegram
```

### Horizontal Scaling

```bash
# Scale specific services
kubectl scale deployment messageservice --replicas=3 -n localtelegram
kubectl scale deployment fileservice --replicas=2 -n localtelegram
kubectl scale deployment conferenceservice --replicas=2 -n localtelegram
```

---

## SSL/TLS Configuration

### Option 1: Let's Encrypt (Recommended)

#### Install cert-manager

```bash
helm repo add jetstack https://charts.jetstack.io
helm install cert-manager jetstack/cert-manager --namespace cert-manager --create-namespace --set installCRDs=true
```

#### Create ClusterIssuer

```yaml
apiVersion: cert-manager.io/v1
kind: ClusterIssuer
metadata:
  name: letsencrypt-prod
spec:
  acme:
    server: https://acme-v02.api.letsencrypt.org/directory
    email: admin@yourcompany.com
    privateKeySecretRef:
      name: letsencrypt-prod
    solvers:
    - http01:
        ingress:
          class: nginx
```

#### Update Ingress

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  annotations:
    cert-manager.io/cluster-issuer: letsencrypt-prod
spec:
  tls:
  - hosts:
    - telegram.yourcompany.com
    secretName: localtelegram-tls
```

### Option 2: Self-Signed Certificates

```bash
# Generate private key
openssl genrsa -out server.key 2048

# Generate certificate
openssl req -new -x509 -key server.key -out server.crt -days 365 \
  -subj "/CN=telegram.yourcompany.com"

# Create Kubernetes secret
kubectl create secret tls localtelegram-tls \
  --cert=server.crt \
  --key=server.key \
  -n localtelegram
```

---

## TURN Server Setup

### coturn Configuration

Create `turnserver.conf`:

```conf
# Listener configuration
listening-port=3478
tls-listening-port=5349

# Relay ports
min-port=49152
max-port=65535

# Authentication
use-auth-secret
static-auth-secret=your_turn_secret
realm=telegram.yourcompany.com

# TLS certificates
cert=/etc/ssl/certs/server.crt
pkey=/etc/ssl/certs/server.key

# Logging
verbose
log-file=/var/log/turnserver.log

# Security
no-multicast-peers
no-cli

# Fingerprint for WebRTC
fingerprint

# Enable long-term credential mechanism
lt-cred-mech
```

### Docker Compose Service

```yaml
  coturn:
    image: coturn/coturn:latest
    container_name: coturn
    restart: unless-stopped
    network_mode: host
    volumes:
      - ./turnserver.conf:/etc/turnserver.conf:ro
      - ./certs:/etc/ssl/certs:ro
    environment:
      - REALM=telegram.yourcompany.com
```

### Test TURN Server

```bash
# Install turnutils
apt-get install coturn-utils

# Test connectivity
turnutils_uclient -v -t -u username -w password telegram.yourcompany.com
```

---

## LDAP/AD Integration

### Configuration

```env
LDAP_ENABLED=true
LDAP_SERVER=ldap://ldap.yourcompany.com:389
# Or for SSL:
# LDAP_SERVER=ldaps://ldap.yourcompany.com:636

LDAP_BASE_DN=dc=yourcompany,dc=com
LDAP_BIND_DN=cn=service_account,ou=Service Accounts,dc=yourcompany,dc=com
LDAP_BIND_PASSWORD=your_service_account_password

# User search
LDAP_USER_FILTER=(objectClass=user)
LDAP_USER_SEARCH_BASE=ou=Users,dc=yourcompany,dc=com

# Attribute mapping
LDAP_USERNAME_ATTRIBUTE=sAMAccountName
LDAP_EMAIL_ATTRIBUTE=mail
LDAP_FIRSTNAME_ATTRIBUTE=givenName
LDAP_LASTNAME_ATTRIBUTE=sn
LDAP_PHONE_ATTRIBUTE=telephoneNumber

# Group membership (optional)
LDAP_GROUP_SEARCH_BASE=ou=Groups,dc=yourcompany,dc=com
LDAP_ADMIN_GROUP=CN=TelegramAdmins,ou=Groups,dc=yourcompany,dc=com
```

### Active Directory Configuration

```env
LDAP_ENABLED=true
LDAP_SERVER=ldaps://ad.yourcompany.com:636
LDAP_BASE_DN=dc=yourcompany,dc=com
LDAP_BIND_DN=CN=Telegram Service,OU=Service Accounts,DC=yourcompany,DC=com
LDAP_BIND_PASSWORD=your_password

LDAP_USER_SEARCH_BASE=OU=Employees,DC=yourcompany,DC=com
LDAP_USER_FILTER=(&(objectClass=user)(objectCategory=person)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))

LDAP_USERNAME_ATTRIBUTE=sAMAccountName
LDAP_EMAIL_ATTRIBUTE=mail
LDAP_FIRSTNAME_ATTRIBUTE=givenName
LDAP_LASTNAME_ATTRIBUTE=sn
LDAP_PHONE_ATTRIBUTE=mobile

LDAP_GROUP_SEARCH_BASE=OU=Groups,DC=yourcompany,DC=com
LDAP_ADMIN_GROUP=CN=TelegramAdmins,OU=Groups,DC=yourcompany,DC=com
```

### OpenLDAP Configuration

```env
LDAP_ENABLED=true
LDAP_SERVER=ldap://ldap.yourcompany.com:389
LDAP_BASE_DN=dc=yourcompany,dc=com
LDAP_BIND_DN=cn=admin,dc=yourcompany,dc=com
LDAP_BIND_PASSWORD=your_password

LDAP_USER_SEARCH_BASE=ou=users,dc=yourcompany,dc=com
LDAP_USER_FILTER=(objectClass=inetOrgPerson)

LDAP_USERNAME_ATTRIBUTE=uid
LDAP_EMAIL_ATTRIBUTE=mail
LDAP_FIRSTNAME_ATTRIBUTE=givenName
LDAP_LASTNAME_ATTRIBUTE=sn
LDAP_PHONE_ATTRIBUTE=telephoneNumber
```

---

## Monitoring Setup

### Prometheus Configuration

```yaml
# prometheus.yml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: 'prometheus'
    static_configs:
      - targets: ['localhost:9090']

  - job_name: 'api-gateway'
    static_configs:
      - targets: ['apigateway:5000']
    metrics_path: /metrics

  - job_name: 'auth-service'
    static_configs:
      - targets: ['authservice:5001']
    metrics_path: /metrics

  - job_name: 'message-service'
    static_configs:
      - targets: ['messageservice:5002']
    metrics_path: /metrics

  - job_name: 'user-service'
    static_configs:
      - targets: ['userservice:5003']
    metrics_path: /metrics

  - job_name: 'group-service'
    static_configs:
      - targets: ['groupservice:5004']
    metrics_path: /metrics

  - job_name: 'file-service'
    static_configs:
      - targets: ['fileservice:5005']
    metrics_path: /metrics

  - job_name: 'media-service'
    static_configs:
      - targets: ['mediaservice:5006']
    metrics_path: /metrics

  - job_name: 'conference-service'
    static_configs:
      - targets: ['conferenceservice:5007']
    metrics_path: /metrics

  - job_name: 'push-service'
    static_configs:
      - targets: ['pushservice:5008']
    metrics_path: /metrics

  - job_name: 'mongodb'
    static_configs:
      - targets: ['mongodb-exporter:9216']

  - job_name: 'redis'
    static_configs:
      - targets: ['redis-exporter:9121']

  - job_name: 'rabbitmq'
    static_configs:
      - targets: ['rabbitmq:15692']

  - job_name: 'minio'
    static_configs:
      - targets: ['minio:9000']
    metrics_path: /minio/v2/metrics/cluster
    scheme: http
```

### Grafana Dashboards

Import dashboards:
1. .NET Metrics Dashboard
2. MongoDB Dashboard
3. Redis Dashboard
4. RabbitMQ Dashboard
5. MinIO Dashboard
6. Nginx Dashboard

### Alertmanager Configuration

```yaml
# alertmanager.yml
global:
  resolve_timeout: 5m
  smtp_smarthost: 'smtp.yourcompany.com:587'
  smtp_from: 'alerts@yourcompany.com'
  smtp_auth_username: 'alerts@yourcompany.com'
  smtp_auth_password: 'your_password'

route:
  group_by: ['alertname', 'severity']
  group_wait: 30s
  group_interval: 5m
  repeat_interval: 4h
  receiver: 'team-email'
  routes:
    - match:
        severity: critical
      receiver: 'team-pagerduty'

receivers:
  - name: 'team-email'
    email_configs:
      - to: 'team@yourcompany.com'
        send_resolved: true

  - name: 'team-pagerduty'
    pagerduty_configs:
      - service_key: 'your_pagerduty_key'
        severity: critical
```

---

## Backup Strategy

### Automated Backups

#### Cron Job (Linux)

```bash
# Add to crontab
crontab -e

# Daily backup at 2 AM
0 2 * * * /opt/localtelegram/scripts/backup.sh >> /var/log/backup.log 2>&1
```

#### Kubernetes CronJob

```yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: backup-job
  namespace: localtelegram
spec:
  schedule: "0 2 * * *"
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: backup
            image: backup-image:latest
            command: ["/scripts/backup.sh"]
            volumeMounts:
            - name: backup-storage
              mountPath: /backups
          volumes:
          - name: backup-storage
            persistentVolumeClaim:
              claimName: backup-pvc
          restartPolicy: OnFailure
```

### Backup Retention

| Backup Type | Retention | Frequency |
|-------------|-----------|-----------|
| Full | 30 days | Weekly |
| Incremental | 7 days | Daily |
| Transaction logs | 24 hours | Hourly |

### Disaster Recovery

1. **RPO (Recovery Point Objective):** 1 hour
2. **RTO (Recovery Time Objective):** 4 hours

#### Recovery Steps

```bash
# 1. Stop services
docker-compose down

# 2. Restore MongoDB
./scripts/restore.ps1 -BackupFile backup_20240101.tar.gz -Components mongodb

# 3. Restore Redis
./scripts/restore.ps1 -BackupFile backup_20240101.tar.gz -Components redis

# 4. Restore MinIO
./scripts/restore.ps1 -BackupFile backup_20240101.tar.gz -Components minio

# 5. Start services
docker-compose up -d
```

---

## Troubleshooting

### Common Issues

#### 1. Services won't start

```bash
# Check logs
docker-compose logs authservice
docker-compose logs messageservice

# Check resource usage
docker stats

# Check network
docker network ls
docker network inspect localtelegram_default
```

#### 2. Database connection errors

```bash
# Check MongoDB
docker-compose exec mongodb mongosh -u admin -p

# Check connection string
echo $MONGODB_URI

# Check network connectivity
docker-compose exec authservice ping mongodb
```

#### 3. WebSocket connection fails

```bash
# Check nginx configuration
docker-compose exec nginx nginx -t

# Check WebSocket upgrade headers
curl -i -N -H "Connection: Upgrade" -H "Upgrade: websocket" -H "Sec-WebSocket-Key: test" -H "Sec-WebSocket-Version: 13" https://telegram.yourcompany.com/hubs/notification
```

#### 4. TURN server not working

```bash
# Check TURN logs
docker-compose logs coturn

# Test TURN connectivity
turnutils_uclient -v telegram.yourcompany.com

# Check firewall
sudo ufw status
sudo ufw allow 3478/tcp
sudo ufw allow 3478/udp
sudo ufw allow 5349/tcp
sudo ufw allow 49152:65535/udp
```

#### 5. Video calls not connecting

```bash
# Check LiveKit
docker-compose logs livekit

# Verify LiveKit token
jwt decode <your_livekit_token>

# Check SFU connectivity
curl https://telegram.yourcompany.com:7880
```

#### 6. File uploads failing

```bash
# Check MinIO
docker-compose exec minio mc admin info local

# Check bucket permissions
docker-compose exec minio mc anonymous get local/messages

# Check storage space
df -h
```

#### 7. LDAP authentication failing

```bash
# Test LDAP connection
ldapsearch -x -H ldap://ldap.yourcompany.com -D "cn=admin,dc=yourcompany,dc=com" -W -b "dc=yourcompany,dc=com"

# Check LDAP logs
docker-compose logs authservice | grep -i ldap
```

### Health Check Endpoints

```bash
# API Gateway
curl https://telegram.yourcompany.com/health

# Individual services
curl https://telegram.yourcompany.com/api/auth/health
curl https://telegram.yourcompany.com/api/messages/health
curl https://telegram.yourcompany.com/api/users/health
curl https://telegram.yourcompany.com/api/groups/health
curl https://telegram.yourcompany.com/api/files/health
curl https://telegram.yourcompany.com/api/media/health
curl https://telegram.yourcompany.com/api/conferences/health
```

### Performance Tuning

#### MongoDB

```javascript
// Enable slow query logging
db.setProfilingLevel(1, 50)

// Create indexes
db.messages.createIndex({ "chatId": 1, "createdAt": -1 })
db.messages.createIndex({ "senderId": 1 })
db.users.createIndex({ "username": 1 }, { unique: true })
db.users.createIndex({ "phone": 1 }, { unique: true })
```

#### Redis

```conf
# redis.conf
maxmemory 2gb
maxmemory-policy allkeys-lru
save 900 1
save 300 10
save 60 10000
```

#### .NET Services

```json
// appsettings.json
{
  "Kestrel": {
    "Limits": {
      "MaxConcurrentConnections": 1000,
      "MaxConcurrentUpgradedConnections": 1000,
      "MaxRequestBodySize": 2147483648
    }
  }
}
```

---

## Security Checklist

- [ ] Change all default passwords
- [ ] Enable TLS for all services
- [ ] Configure firewall rules
- [ ] Enable rate limiting
- [ ] Set up fail2ban
- [ ] Configure CORS properly
- [ ] Enable audit logging
- [ ] Set up intrusion detection
- [ ] Regular security updates
- [ ] Enable HTTPS only
- [ ] Configure CSP headers
- [ ] Enable HSTS
- [ ] Set secure cookie flags
- [ ] Configure backup encryption

---

*Last updated: March 2026*
