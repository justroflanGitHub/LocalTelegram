# LocalTelegram Architecture Documentation

> Version: 1.0 | Date: March 2026

---

## Table of Contents

1. [Overview](#overview)
2. [System Architecture](#system-architecture)
3. [Microservices](#microservices)
4. [Data Flow](#data-flow)
5. [Infrastructure](#infrastructure)
6. [Security Architecture](#security-architecture)
7. [Scalability](#scalability)
8. [Deployment](#deployment)

---

## Overview

LocalTelegram is a self-hosted, closed messenger system designed for corporate environments. It provides Telegram-like functionality while maintaining complete data sovereignty within your infrastructure.

### Key Features

- **Self-hosted**: Complete control over data and infrastructure
- **Corporate-ready**: LDAP/Active Directory integration
- **Secure**: End-to-end encryption support, 2FA, device management
- **Scalable**: Microservices architecture with horizontal scaling
- **Observable**: Comprehensive monitoring, logging, and tracing

### Technology Stack

| Component | Technology |
|-----------|------------|
| Backend | .NET 8, ASP.NET Core |
| Database | PostgreSQL 16, MongoDB |
| Cache | Redis 7 |
| Message Queue | RabbitMQ |
| File Storage | MinIO (S3-compatible) |
| Media Processing | FFmpeg, ImageSharp |
| Real-time | SignalR (WebSocket) |
| Video Calls | WebRTC, LiveKit SFU |
| Monitoring | Prometheus, Grafana |
| Logging | Serilog, Loki |
| Tracing | OpenTelemetry, Jaeger |
| Container | Docker, Kubernetes |

---

## System Architecture

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              CLIENTS                                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐    │
│  │   Windows    │  │   Android    │  │     iOS      │  │   Web Admin  │    │
│  │   Desktop    │  │   Mobile     │  │   Mobile     │  │    Panel     │    │
│  └──────────────┘  └──────────────┘  └──────────────┘  └──────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           LOAD BALANCER (nginx)                              │
│                         SSL Termination, Rate Limiting                       │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           API GATEWAY (YARP)                                 │
│              Authentication, Routing, Rate Limiting, Health Checks           │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
          ┌───────────────────────────┼───────────────────────────┐
          ▼                           ▼                           ▼
┌─────────────────────┐   ┌─────────────────────┐   ┌─────────────────────┐
│    Auth Service     │   │   Message Service   │   │    User Service     │
│   (Port: 5001)      │   │   (Port: 5002)      │   │   (Port: 5003)      │
│                     │   │                     │   │                     │
│ • Registration      │   │ • Send/Receive      │   │ • Profiles          │
│ • Login/Logout      │   │ • History           │   │ • Contacts          │
│ • Token Management  │   │ • Edit/Delete       │   │ • Privacy Settings  │
│ • 2FA               │   │ • Reactions         │   │ • Blocking          │
│ • Device Management │   │ • Forwarding        │   │ • Status            │
│ • Session Handling  │   │ • Pinned Messages   │   │ • Devices           │
└─────────────────────┘   └─────────────────────┘   └─────────────────────┘
          │                           │                           │
          └───────────────────────────┼───────────────────────────┘
                                      │
          ┌───────────────────────────┼───────────────────────────┐
          ▼                           ▼                           ▼
┌─────────────────────┐   ┌─────────────────────┐   ┌─────────────────────┐
│    File Service     │   │    Push Service     │   │   Group Service     │
│   (Port: 5004)      │   │   (Port: 5005)      │   │   (Port: 5006)      │
│                     │   │                     │   │                     │
│ • File Upload       │   │ • WebSocket Server  │   │ • Group CRUD        │
│ • Chunked Upload    │   │ • Notifications     │   │ • Members           │
│ • Download          │   │ • Badge Count       │   │ • Roles             │
│ • Preview           │   │ • Mute/Unmute       │   │ • Invites           │
│ • Thumbnails        │   │ • Delivery Status   │   │ • Permissions       │
└─────────────────────┘   └─────────────────────┘   └─────────────────────┘
          │                           │                           │
          └───────────────────────────┼───────────────────────────┘
                                      │
          ┌───────────────────────────┼───────────────────────────┐
          ▼                           ▼                           ▼
┌─────────────────────┐   ┌─────────────────────┐   ┌─────────────────────┐
│   Media Service     │   │ Conference Service  │   │   Admin Service     │
│   (Port: 5007)      │   │   (Port: 5008)      │   │   (Port: 5009)      │
│                     │   │                     │   │                     │
│ • Video Transcode   │   │ • Room Management   │   │ • User Management   │
│ • Audio Transcode   │   │ • Call Signaling    │   │ • Moderation        │
│ • Streaming         │   │ • WebRTC SFU        │   │ • Reports           │
│ • Thumbnail Gen     │   │ • Screen Sharing    │   │ • Audit Log         │
│ • Format Convert    │   │ • Recording         │   │ • Analytics         │
└─────────────────────┘   └─────────────────────┘   └─────────────────────┘
```

---

## Microservices

### Auth Service (Port 5001)

**Responsibilities:**
- User registration and authentication
- JWT token generation and validation
- Two-factor authentication (TOTP, SMS)
- Device management and tracking
- Session management with Redis
- LDAP/Active Directory integration

**Database Tables:**
- `users` - User accounts
- `sessions` - Active sessions
- `devices` - Registered devices
- `two_factor_secrets` - 2FA configuration
- `ldap_mappings` - LDAP user mappings

**API Endpoints:**
```
POST   /api/auth/register
POST   /api/auth/login
POST   /api/auth/logout
POST   /api/auth/refresh
POST   /api/auth/2fa/enable
POST   /api/auth/2fa/verify
GET    /api/auth/devices
DELETE /api/auth/devices/{id}
POST   /api/auth/ldap/login
```

### Message Service (Port 5002)

**Responsibilities:**
- Send and receive messages
- Message history and search
- Edit and delete messages
- Message reactions
- Forwarding and replies
- Pinned messages

**Database Tables:**
- `messages` - Message content
- `conversations` - Chat conversations
- `message_reactions` - Reaction data
- `pinned_messages` - Pinned message tracking

**API Endpoints:**
```
POST   /api/messages
GET    /api/messages/{conversationId}
PUT    /api/messages/{id}
DELETE /api/messages/{id}
POST   /api/messages/{id}/react
POST   /api/messages/{id}/forward
POST   /api/conversations/{id}/pin/{messageId}
```

### User Service (Port 5003)

**Responsibilities:**
- User profiles
- Contact management
- Privacy settings
- User blocking
- Online status

**Database Tables:**
- `profiles` - User profile data
- `contacts` - Contact lists
- `blocked_users` - Block list
- `privacy_settings` - Privacy configuration

**API Endpoints:**
```
GET    /api/users/{id}
PUT    /api/users/{id}
GET    /api/users/{id}/contacts
POST   /api/users/{id}/contacts
DELETE /api/users/{id}/contacts/{contactId}
POST   /api/users/{id}/block/{targetId}
GET    /api/users/{id}/privacy
PUT    /api/users/{id}/privacy
```

### File Service (Port 5004)

**Responsibilities:**
- File upload and download
- Chunked upload for large files
- Thumbnail generation
- Preview generation
- File deduplication

**Storage:**
- MinIO (S3-compatible) for file storage
- PostgreSQL for metadata

**API Endpoints:**
```
POST   /api/files
POST   /api/files/chunked/init
POST   /api/files/chunked/{uploadId}
POST   /api/files/chunked/{uploadId}/complete
GET    /api/files/{id}
GET    /api/files/{id}/thumbnail
DELETE /api/files/{id}
```

### Push Service (Port 5005)

**Responsibilities:**
- WebSocket connections
- Real-time notifications
- Badge count management
- Delivery status tracking

**Technology:**
- SignalR for WebSocket
- Redis for connection distribution

**Hub Endpoints:**
```
/notificationHub - Main notification hub
```

**Events:**
```
MessageReceived
MessageRead
UserOnline
UserOffline
Typing
CallOffer
CallAnswer
```

### Group Service (Port 5006)

**Responsibilities:**
- Group creation and management
- Member management
- Role and permission system
- Group invites
- Group settings

**Database Tables:**
- `groups` - Group information
- `group_members` - Member list
- `group_roles` - Role definitions
- `group_invites` - Pending invites

**API Endpoints:**
```
POST   /api/groups
GET    /api/groups/{id}
PUT    /api/groups/{id}
DELETE /api/groups/{id}
POST   /api/groups/{id}/members
DELETE /api/groups/{id}/members/{userId}
PUT    /api/groups/{id}/roles/{userId}
POST   /api/groups/{id}/invite
```

### Media Service (Port 5007)

**Responsibilities:**
- Video transcoding
- Audio transcoding
- Streaming support
- Thumbnail generation
- Format conversion

**Technology:**
- FFmpeg for transcoding
- Background worker queue
- RabbitMQ for job distribution

**API Endpoints:**
```
POST   /api/media/transcode
GET    /api/media/{id}/stream
GET    /api/media/{id}/thumbnail
POST   /api/media/voice
```

### Conference Service (Port 5008)

**Responsibilities:**
- Video call rooms
- Call signaling
- Screen sharing
- Recording (optional)

**Technology:**
- WebRTC for media
- LiveKit SFU for routing
- SignalR for signaling

**API Endpoints:**
```
POST   /api/conferences
GET    /api/conferences/{id}
POST   /api/conferences/{id}/join
POST   /api/conferences/{id}/leave
POST   /api/conferences/{id}/signal
```

### Admin Service (Port 5009)

**Responsibilities:**
- User management
- Moderation tools
- Report handling
- Audit logging
- System analytics

**API Endpoints:**
```
GET    /api/admin/users
PUT    /api/admin/users/{id}/status
POST   /api/admin/users/{id}/warn
POST   /api/admin/users/{id}/ban
GET    /api/admin/reports
PUT    /api/admin/reports/{id}/resolve
GET    /api/admin/audit-log
GET    /api/admin/analytics
```

---

## Data Flow

### Message Send Flow

```
┌────────┐     ┌────────────┐     ┌───────────────┐     ┌──────────────┐
│ Client │────▶│ API Gateway│────▶│Message Service│────▶│  PostgreSQL  │
└────────┘     └────────────┘     └───────────────┘     └──────────────┘
     │                                    │
     │                                    ▼
     │                           ┌───────────────┐
     │                           │   RabbitMQ    │
     │                           └───────────────┘
     │                                    │
     │                                    ▼
     │                           ┌───────────────┐
     │                           │  Push Service │
     │                           └───────────────┘
     │                                    │
     ▼                                    ▼
┌────────┐                        ┌───────────────┐
│ Sender │                        │ Recipient(s)  │
│  ACK   │                        │  WebSocket    │
└────────┘                        └───────────────┘
```

### Authentication Flow

```
┌────────┐     ┌────────────┐     ┌───────────────┐     ┌──────────────┐
│ Client │────▶│ API Gateway│────▶│ Auth Service  │────▶│  PostgreSQL  │
└────────┘     └────────────┘     └───────────────┘     └──────────────┘
     │                                    │                     │
     │                                    ▼                     │
     │                           ┌───────────────┐             │
     │                           │     Redis     │◀────────────┘
     │                           │   (Sessions)  │
     │                           └───────────────┘
     │                                    │
     ▼                                    │
┌────────┐                               │
│  JWT   │◀──────────────────────────────┘
│ Token  │
└────────┘
```

### File Upload Flow

```
┌────────┐     ┌────────────┐     ┌───────────────┐     ┌──────────────┐
│ Client │────▶│ API Gateway│────▶│ File Service  │────▶│    MinIO     │
└────────┘     └────────────┘     └───────────────┘     └──────────────┘
     │                                    │                     │
     │                                    ▼                     │
     │                           ┌───────────────┐             │
     │                           │  PostgreSQL   │◀────────────┘
     │                           │  (Metadata)   │
     │                           └───────────────┘
     │                                    │
     │                                    ▼
     │                           ┌───────────────┐
     │                           │    Redis      │
     │                           │    (Cache)    │
     │                           └───────────────┘
     ▼
┌────────┐
│ File   │
│  URL   │
└────────┘
```

---

## Infrastructure

### Container Architecture

```yaml
services:
  # Core Services
  auth-service:
    image: localtelegram/auth-service
    ports: ["5001:80"]
    depends_on: [postgres, redis]
    
  message-service:
    image: localtelegram/message-service
    ports: ["5002:80"]
    depends_on: [postgres, redis, rabbitmq]
    
  # ... other services
  
  # Infrastructure
  postgres:
    image: postgres:16
    volumes: ["postgres_data:/var/lib/postgresql/data"]
    
  redis:
    image: redis:7-alpine
    volumes: ["redis_data:/data"]
    
  rabbitmq:
    image: rabbitmq:3-management
    ports: ["5672:5672", "15672:15672"]
    
  minio:
    image: minio/minio
    command: server /data --console-address ":9001"
    volumes: ["minio_data:/data"]
    
  # Monitoring
  prometheus:
    image: prom/prometheus
    ports: ["9090:9090"]
    
  grafana:
    image: grafana/grafana
    ports: ["3000:3000"]
    
  loki:
    image: grafana/loki
    ports: ["3100:3100"]
    
  jaeger:
    image: jaegertracing/all-in-one
    ports: ["16686:16686", "14250:14250"]
```

### Network Topology

```
┌─────────────────────────────────────────────────────────────────┐
│                        Docker Network                            │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    Application Tier                       │    │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐        │    │
│  │  │  Auth   │ │ Message │ │  User   │ │  File   │        │    │
│  │  │ :5001   │ │ :5002   │ │ :5003   │ │ :5004   │        │    │
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘        │    │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐        │    │
│  │  │  Push   │ │  Group  │ │  Media  │ │ Conf    │        │    │
│  │  │ :5005   │ │ :5006   │ │ :5007   │ │ :5008   │        │    │
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘        │    │
│  └─────────────────────────────────────────────────────────┘    │
│                              │                                   │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    Data Tier                              │    │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐        │    │
│  │  │Postgres │ │  Redis  │ │RabbitMQ │ │  MinIO  │        │    │
│  │  │  :5432  │ │  :6379  │ │  :5672  │ │  :9000  │        │    │
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘        │    │
│  └─────────────────────────────────────────────────────────┘    │
│                              │                                   │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                  Monitoring Tier                         │    │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐        │    │
│  │  │Prometh  │ │ Grafana │ │  Loki   │ │ Jaeger  │        │    │
│  │  │  :9090  │ │  :3000  │ │  :3100  │ │ :16686  │        │    │
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘        │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

---

## Security Architecture

### Authentication Layers

```
┌─────────────────────────────────────────────────────────────────┐
│                        Security Layers                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Layer 1: Network Security                                      │
│  ├── TLS/SSL Encryption (nginx)                                 │
│  ├── Firewall Rules                                             │
│  └── Rate Limiting                                              │
│                                                                  │
│  Layer 2: API Gateway                                           │
│  ├── JWT Validation                                             │
│  ├── Request Throttling                                         │
│  └── IP Whitelisting (optional)                                 │
│                                                                  │
│  Layer 3: Service Authentication                                │
│  ├── Service-to-Service Auth                                    │
│  ├── API Key Validation                                         │
│  └── Request Signing                                            │
│                                                                  │
│  Layer 4: Application Security                                  │
│  ├── Role-Based Access Control (RBAC)                          │
│  ├── Permission Checks                                          │
│  └── Data Isolation                                             │
│                                                                  │
│  Layer 5: Data Security                                         │
│  ├── Encryption at Rest                                         │
│  ├── Encryption in Transit                                      │
│  └── Sensitive Data Masking                                     │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### JWT Token Structure

```json
{
  "header": {
    "alg": "RS256",
    "typ": "JWT"
  },
  "payload": {
    "sub": "user-uuid",
    "email": "user@example.com",
    "role": "user",
    "permissions": ["send_messages", "upload_files"],
    "device_id": "device-uuid",
    "session_id": "session-uuid",
    "iat": 1709500000,
    "exp": 1709503600,
    "iss": "localtelegram",
    "aud": "localtelegram-api"
  }
}
```

### Data Encryption

| Data Type | Encryption | Key Management |
|-----------|------------|----------------|
| Passwords | bcrypt (cost 12) | N/A |
| JWT Secrets | RSA-256 | Environment variables |
| Database | AES-256 | Key vault |
| File Storage | Server-side encryption | MinIO KMS |
| Redis | TLS in transit | Certificate |

---

## Scalability

### Horizontal Scaling

```
┌─────────────────────────────────────────────────────────────────┐
│                      Load Balancer                               │
└─────────────────────────────────────────────────────────────────┘
                              │
          ┌───────────────────┼───────────────────┐
          ▼                   ▼                   ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│  Auth Service   │ │  Auth Service   │ │  Auth Service   │
│   Instance 1    │ │   Instance 2    │ │   Instance 3    │
└─────────────────┘ └─────────────────┘ └─────────────────┘
          │                   │                   │
          └───────────────────┼───────────────────┘
                              ▼
                    ┌─────────────────┐
                    │   Redis Cluster │
                    │  (Session Store)│
                    └─────────────────┘
```

### Database Scaling

```
┌─────────────────────────────────────────────────────────────────┐
│                     PostgreSQL Primary                           │
│                      (Read/Write)                                │
└─────────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
    ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
    │   Replica 1     │ │   Replica 2     │ │   Replica 3     │
    │   (Read Only)   │ │   (Read Only)   │ │   (Read Only)   │
    └─────────────────┘ └─────────────────┘ └─────────────────┘
```

### Caching Strategy

```
┌─────────────────────────────────────────────────────────────────┐
│                        Cache Layers                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  L1: In-Memory Cache (per service)                             │
│  ├── User sessions (5 min TTL)                                  │
│  ├── Rate limit counters (1 min TTL)                           │
│  └── Configuration data (15 min TTL)                           │
│                                                                  │
│  L2: Redis Cache (distributed)                                  │
│  ├── User profiles (15 min TTL)                                 │
│  ├── Contact lists (10 min TTL)                                 │
│  ├── Message metadata (30 min TTL)                              │
│  └── File metadata (60 min TTL)                                 │
│                                                                  │
│  L3: CDN Cache (static assets)                                  │
│  ├── Thumbnails (24 hour TTL)                                   │
│  ├── Avatars (24 hour TTL)                                      │
│  └── Static files (7 day TTL)                                   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Deployment

### Kubernetes Architecture

```yaml
# Namespace: localtelegram
apiVersion: v1
kind: Namespace
metadata:
  name: localtelegram

---
# Deployment Example
apiVersion: apps/v1
kind: Deployment
metadata:
  name: auth-service
  namespace: localtelegram
spec:
  replicas: 3
  selector:
    matchLabels:
      app: auth-service
  template:
    spec:
      containers:
      - name: auth-service
        image: localtelegram/auth-service:latest
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
```

### Resource Requirements

| Service | CPU Request | CPU Limit | Memory Request | Memory Limit |
|---------|-------------|-----------|----------------|--------------|
| Auth | 250m | 500m | 256Mi | 512Mi |
| Message | 500m | 1000m | 512Mi | 1Gi |
| User | 250m | 500m | 256Mi | 512Mi |
| File | 500m | 1000m | 512Mi | 2Gi |
| Push | 500m | 1000m | 512Mi | 1Gi |
| Group | 250m | 500m | 256Mi | 512Mi |
| Media | 1000m | 2000m | 1Gi | 4Gi |
| Conference | 1000m | 2000m | 1Gi | 2Gi |
| Admin | 250m | 500m | 256Mi | 512Mi |

### Auto-Scaling Configuration

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: message-service-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: message-service
  minReplicas: 2
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

---

## Monitoring & Observability

### Metrics Collection

```
┌─────────────────────────────────────────────────────────────────┐
│                     Prometheus Metrics                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Service Metrics:                                                │
│  ├── http_requests_total                                         │
│  ├── http_request_duration_seconds                              │
│  ├── http_requests_in_progress                                  │
│  └── http_response_size_bytes                                   │
│                                                                  │
│  Business Metrics:                                               │
│  ├── messages_sent_total                                        │
│  ├── messages_delivered_total                                   │
│  ├── active_users_count                                         │
│  ├── concurrent_connections                                     │
│  └── files_uploaded_bytes                                       │
│                                                                  │
│  Infrastructure Metrics:                                         │
│  ├── process_cpu_seconds_total                                  │
│  ├── process_resident_memory_bytes                              │
│  ├── dotnet_gc_heap_size_bytes                                  │
│  └── dotnet_threadpool_queue_length                             │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Alerting Rules

```yaml
groups:
- name: localtelegram-alerts
  rules:
  - alert: HighErrorRate
    expr: rate(http_requests_total{status=~"5.."}[5m]) > 0.1
    for: 5m
    labels:
      severity: critical
    annotations:
      summary: High error rate detected
      
  - alert: HighLatency
    expr: histogram_quantile(0.95, rate(http_request_duration_seconds_bucket[5m])) > 1
    for: 5m
    labels:
      severity: warning
      
  - alert: ServiceDown
    expr: up == 0
    for: 1m
    labels:
      severity: critical
```

---

*Document created: March 2026*  
*Last updated: March 2026*
