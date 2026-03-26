# LocalTelegram

> A fully autonomous, self-hosted messenger system for corporate and private use.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/Docker-Ready-2496ED)](https://www.docker.com/)
[![Kubernetes](https://img.shields.io/badge/Kubernetes-Ready-326CE5)](https://kubernetes.io/)

## Overview

**LocalTelegram** is a complete, self-hosted messaging platform that runs entirely on your own infrastructure. It provides full control over your data and communications without depending on external cloud services. The system supports unlimited file sizes, video conferencing, and cross-platform clients.

Based on [MyTelegram](https://github.com/loyldg/mytelegram) - a C# implementation of the Telegram server-side API.

## Key Features

### Core Messaging
- **Private & Group Chats** - Full-featured messaging with edit, delete, reply, forward
- **Reactions & Pinning** - Express reactions and pin important messages
- **Unlimited File Transfers** - No file size restrictions
- **Voice & Video Messages** - Built-in media recording and playback
- **Message History** - Searchable conversation archives

### Media & Conferencing
- **Video Streaming** - Adaptive bitrate streaming with HLS/DASH
- **Video Transcoding** - Automatic quality conversion via FFmpeg
- **Audio/Video Calls** - WebRTC-based real-time communication
- **Conference Rooms** - Multi-participant video meetings
- **Screen Sharing** - Collaborative screen broadcasting

### Security & Compliance
- **Two-Factor Authentication (2FA)** - TOTP-based additional security
- **Device Management** - Track and revoke connected devices
- **LDAP/Active Directory** - Enterprise user integration
- **GDPR Compliance** - Data export and deletion tools
- **Privacy Controls** - Granular user privacy settings
- **Rate Limiting** - Protection against abuse

### Administration
- **Admin Panel** - React-based management interface
- **Moderation Tools** - Reports, warnings, bans, content moderation
- **Audit Logging** - Comprehensive action tracking
- **Data Retention** - Configurable retention policies
- **Invite System** - Email and link-based invitations

### Infrastructure
- **High Availability** - Multi-instance deployment support
- **Horizontal Scaling** - Load-balanced services
- **Monitoring Stack** - Prometheus, Grafana, Alertmanager
- **Distributed Tracing** - OpenTelemetry with Jaeger
- **Centralized Logging** - Serilog with Loki integration
- **Backup & Restore** - Automated data protection

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              CLIENT LAYER                                     │
├─────────────────────────────┬───────────────────────────────────────────────┤
│      Windows Client         │              Android Client                     │
│      (tdesktop fork)        │          (Telegram-FOSS fork)                   │
│        C++ / Qt 6           │             Kotlin / Java                       │
└──────────────┬──────────────┴──────────────────┬─────────────────────────────┘
               │                                 │
               │        MTProto Protocol         │
               │                                 │
               ▼                                 ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           API GATEWAY (Nginx)                                 │
│                           Ports: 80, 443, 5000                                │
└─────────────────────────────────┬───────────────────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                          MICROSERVICES LAYER                                  │
├──────────────┬──────────────┬──────────────┬──────────────┬─────────────────┤
│ AuthService  │MessageService│ UserService  │ GroupService │  FileService    │
│   :5001      │    :5002     │    :5003     │    :5004     │     :5005       │
├──────────────┼──────────────┼──────────────┼──────────────┼─────────────────┤
│ MediaService │ConferenceSvc │ PushService  │  AdminService│   WebRTC Sign.  │
│   :5006      │    :5007     │    :5008     │    :5009     │     :5010       │
└──────────────┴──────────────┴──────────────┴──────────────┴─────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           DATA LAYER                                          │
├─────────────┬─────────────┬─────────────┬─────────────┬─────────────────────┤
│  PostgreSQL │    Redis    │   MongoDB   │    MinIO    │     RabbitMQ        │
│  (Users,    │  (Cache,    │  (Messages, │  (Files,    │    (Message         │
│   Auth)     │  Sessions)  │   Media)    │   Media)    │     Queues)         │
└─────────────┴─────────────┴─────────────┴─────────────┴─────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        INFRASTRUCTURE                                         │
├─────────────┬─────────────┬─────────────┬─────────────┬─────────────────────┤
│  LiveKit    │  Prometheus │   Grafana   │  Alertmanager│     Jaeger         │
│   (SFU)     │ (Monitoring)│ (Dashboard) │  (Alerts)    │    (Tracing)       │
└─────────────┴─────────────┴─────────────┴─────────────┴─────────────────────┘
```

## Quick Start

### Prerequisites

- [Docker](https://www.docker.com/) 20.10+
- [Docker Compose](https://docs.docker.com/compose/) 2.0+
- 8GB RAM minimum, 16GB recommended
- 50GB storage minimum

### Option 1: Using Pre-built MyTelegram Images (Fastest)

```bash
# 1. Clone the repository
git clone https://github.com/justroflanGitHub/LocalTelegram.git
cd LocalTelegram

# 2. Copy environment configuration
cp .env.mytelegram .env

# 3. Edit .env - Set your server IP
# SERVER_IP=192.168.1.100

# 4. Create data directories
mkdir -p data/redis data/rabbitmq data/mongo/db data/mongo/configdb data/minio

# 5. Start the services
docker-compose -f docker-compose.mytelegram.yml up -d

# 6. Verify services are running
docker-compose -f docker-compose.mytelegram.yml ps
```

### Option 2: Full Microservices Deployment

```bash
# 1. Clone the repository
git clone https://github.com/justroflanGitHub/LocalTelegram.git
cd LocalTelegram

# 2. Copy environment configuration
cp .env.example .env

# 3. Edit .env with your settings

# 4. Start all services
docker-compose up -d

# 5. Check service health
docker-compose ps
```

### First Login

For testing, use the default verification code: **22222**

1. Open the client application
2. Enter any phone number (e.g., +1234567890)
3. Enter verification code: **22222**
4. Complete profile setup

## Service Endpoints

| Service | Port | Description |
|---------|------|-------------|
| API Gateway | 5000 | Main entry point for all API requests |
| Auth Service | 5001 | Authentication and authorization |
| Message Service | 5002 | Chat and messaging operations |
| User Service | 5003 | User profiles and contacts |
| Group Service | 5004 | Group and channel management |
| File Service | 5005 | File upload and download |
| Media Service | 5006 | Video transcoding and streaming |
| Conference Service | 5007 | Video conferencing |
| Push Service | 5008 | Real-time notifications (WebSocket) |
| Admin Service | 5009 | Administration panel API |
| WebRTC Signaling | 5010 | WebRTC connection management |

### Management Interfaces

| Service | URL | Credentials |
|---------|-----|-------------|
| MinIO Console | http://localhost:9001 | localtelegram / localtelegram123456 |
| RabbitMQ Management | http://localhost:15672 | localtelegram / localtelegram123 |
| Grafana | http://localhost:3001 | admin / admin |
| Jaeger UI | http://localhost:16686 | - |
| Alertmanager | http://localhost:9093 | - |

## Client Setup

### Windows Client (Desktop)

#### Prerequisites
- Visual Studio 2022
- Qt 6.x
- CMake 3.20+
- vcpkg
- WebRTC native library

#### Build Steps

```powershell
# 1. Clone the modified client
git clone https://github.com/loyldg/mytelegram-tdesktop.git

# 2. Run configuration script
.\scripts\configure-desktop-client.ps1 -ServerIp "YOUR_SERVER_IP"

# 3. Build using Visual Studio or CMake
```

See [docs/CLIENT_CONFIGURATION.md](docs/CLIENT_CONFIGURATION.md) for detailed instructions.

### Android Client

#### Prerequisites
- Android Studio Arctic Fox or later
- Android NDK 23+
- JDK 17

#### Build Steps

```bash
# 1. Clone the modified client
git clone https://github.com/loyldg/mytelegram-android.git

# 2. Run configuration script
./scripts/configure-android-client.sh -s "YOUR_SERVER_IP"

# 3. Build APK using Android Studio
```

## Project Structure

```
LocalTelegram/
├── .github/
│   └── workflows/           # CI/CD pipelines
│       ├── build.yml
│       ├── test.yml
│       └── deploy.yml
├── docs/
│   ├── ARCHITECTURE.md      # System architecture
│   ├── API.md               # API documentation
│   ├── DATABASE_SCHEMA.md   # Database structure
│   ├── DEPLOYMENT.md        # Deployment guide
│   ├── TROUBLESHOOTING.md   # Troubleshooting guide
│   ├── USER_GUIDE.md        # End-user documentation
│   ├── ADMIN_GUIDE.md       # Administrator guide
│   └── FAQ.md               # Frequently asked questions
├── infra/
│   ├── k8s/                 # Kubernetes manifests
│   │   ├── namespace.yaml
│   │   ├── configs/
│   │   ├── secrets/
│   │   └── services/
│   ├── nginx/
│   │   └── conf.d/
│   │       └── default.conf
│   ├── postgres/
│   │   └── init/
│   │       └── 001_init_schema.sql
│   ├── prometheus/
│   │   ├── prometheus.yml
│   │   └── alerts.yml
│   ├── grafana/
│   │   └── dashboards/
│   └── livekit/
│       └── livekit.yaml
├── scripts/
│   ├── backup-restore.ps1   # Database backup/restore
│   ├── configure-*.ps1/sh   # Client configuration
│   ├── integration-test.ps1 # E2E testing
│   ├── load-test.ps1        # k6 load testing
│   ├── security-audit.ps1   # Security scanning
│   ├── setup-firewall.ps1   # Firewall configuration
│   └── setup-ssl.ps1        # SSL certificates
├── src/
│   └── server/              # .NET microservices
│       ├── ApiGateway/
│       ├── AuthService/
│       ├── MessageService/
│       ├── UserService/
│       ├── GroupService/
│       ├── FileService/
│       ├── MediaService/
│       ├── ConferenceService/
│       ├── PushService/
│       ├── AdminService/
│       └── WebRTCSignalingService/
├── tests/
│   ├── AuthService.Tests/
│   ├── MessageService.Tests/
│   ├── FileService.Tests/
│   └── run-tests.ps1
├── admin-panel/             # React admin interface
│   ├── src/
│   ├── public/
│   └── package.json
├── docker-compose.yml       # Full stack deployment
├── docker-compose.mytelegram.yml  # MyTelegram deployment
├── .env.example
├── .env.mytelegram
└── README.md
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `SERVER_IP` | Server IP address | `127.0.0.1` |
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | `Development` |
| `DB_CONNECTION_STRING` | PostgreSQL connection | - |
| `REDIS_CONNECTION` | Redis connection string | `localhost:6379` |
| `MONGODB_CONNECTION` | MongoDB connection string | - |
| `MINIO_ENDPOINT` | MinIO endpoint | `localhost:9000` |
| `RABBITMQ_HOST` | RabbitMQ hostname | `localhost` |
| `JWT_SECRET` | JWT signing key | - |
| `ACCESS_HASH_SECRET` | File access hash secret | - |

### Security Configuration

```bash
# Required: Change these before production deployment
JWT_SECRET=<your-256-bit-secret>
ACCESS_HASH_SECRET=<your-access-hash-secret>
MINIO_SECRET_KEY=<your-minio-secret>
RABBITMQ_PASSWORD=<your-rabbitmq-password>
```

## Testing

### Unit Tests

```powershell
# Run all unit tests
.\tests\run-tests.ps1

# Run specific service tests
dotnet test tests\AuthService.Tests
```

### Integration Tests

```powershell
# Run end-to-end tests
.\scripts\integration-test.ps1 -Target http://localhost:5000 -Verbose
```

### Load Testing

```powershell
# Install k6 first: winget install k6

# Run load tests
.\scripts\load-test.ps1 -Scenario auth -VUs 100 -Duration 5m
.\scripts\load-test.ps1 -Scenario messaging -VUs 50 -Duration 10m
.\scripts\load-test.ps1 -Scenario full -VUs 1000 -Duration 15m
```

### Security Audit

```powershell
# Run security checks
.\scripts\security-audit.ps1 -Target https://localhost:5000
```

## Deployment

### Docker Compose (Development/Testing)

```bash
# Start all services
docker-compose up -d

# View logs
docker-compose logs -f

# Stop services
docker-compose down
```

### Kubernetes (Production)

```bash
# Create namespace
kubectl apply -f infra/k8s/namespace.yaml

# Create secrets
kubectl create secret generic localtelegram-secrets --from-env-file=.env

# Deploy services
kubectl apply -f infra/k8s/

# Check deployment status
kubectl get pods -n localtelegram
```

### High Availability

For production deployments, see [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for:
- Multi-instance configuration
- Load balancing setup
- Database replication
- Redis clustering
- MinIO distributed mode

## Monitoring

### Prometheus Metrics

Access metrics at `http://localhost:9090`

Key metrics exposed:
- Request latency (p50, p95, p99)
- Error rates
- Active connections
- Message throughput
- File upload/download rates

### Grafana Dashboards

Access at `http://localhost:3001` (admin/admin)

Pre-configured dashboards:
- System Overview
- API Performance
- Database Health
- Message Flow
- Conference Stats

### Alerting

Alertmanager runs on port 9093. Configure alerts in `infra/prometheus/alerts.yml`.

### Distributed Tracing

Jaeger UI: `http://localhost:16686`

All services are instrumented with OpenTelemetry for request tracing.

## Backup & Restore

```powershell
# Create backup
.\scripts\backup-restore.ps1 -Action backup -BackupPath C:\backups

# Restore from backup
.\scripts\backup-restore.ps1 -Action restore -BackupPath C:\backups\backup-20240115
```

## Documentation

- [Architecture Guide](docs/ARCHITECTURE.md) - System design and components
- [API Documentation](docs/API.md) - REST API reference
- [Database Schema](docs/DATABASE_SCHEMA.md) - Database structure
- [Deployment Guide](docs/DEPLOYMENT.md) - Production deployment
- [Troubleshooting](docs/TROUBLESHOOTING.md) - Common issues and solutions
- [User Guide](docs/USER_GUIDE.md) - End-user documentation
- [Admin Guide](docs/ADMIN_GUIDE.md) - Administration manual
- [FAQ](docs/FAQ.md) - Frequently asked questions

## Contributing

We welcome contributions! Please follow these steps:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Development Setup

```bash
# Install .NET SDK 8.0
winget install Microsoft.DotNet.SDK.8

# Install dependencies
dotnet restore

# Run development servers
dotnet run --project src/server/ApiGateway
```

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [loyldg/mytelegram](https://github.com/loyldg/mytelegram) - Server implementation foundation
- [loyldg/mytelegram-tdesktop](https://github.com/loyldg/mytelegram-tdesktop) - Windows client
- [loyldg/mytelegram-android](https://github.com/loyldg/mytelegram-android) - Android client
- [telegramdesktop/tdesktop](https://github.com/telegramdesktop/tdesktop) - Original desktop client
- [Telegram-FOSS-Team/Telegram-FOSS](https://github.com/Telegram-FOSS-Team/Telegram-FOSS) - Original Android FOSS client
- [LiveKit](https://livekit.io/) - WebRTC SFU
- [Prometheus](https://prometheus.io/) - Monitoring
- [Grafana](https://grafana.com/) - Visualization
- [Jaeger](https://www.jaegertracing.io/) - Distributed tracing

## Support

- **Documentation**: [docs/](docs/)
- **Issues**: [GitHub Issues](https://github.com/justroflanGitHub/LocalTelegram/issues)
- **Discussions**: [GitHub Discussions](https://github.com/justroflanGitHub/LocalTelegram/discussions)

---

**Note**: This project is for educational and internal use. It is not affiliated with Telegram FZ-LLC.
