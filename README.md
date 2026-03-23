# LocalTelegram

> A fully autonomous closed messenger for corporate or private use.

## Overview

**LocalTelegram** is a self-hosted messaging system that runs on your own infrastructure, providing complete control over your data and communications. The system is independent of external cloud services and supports unlimited file sizes.

## Features

- **Autonomy**: Complete independence from external servers and cloud services
- **Data Control**: All data stored on your organization's servers
- **No Limits**: Send files of any size
- **Conferences**: Audio and video calls, screen sharing
- **Cross-platform**: Windows and Android clients

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         CLIENTS                                  │
├─────────────────────────┬───────────────────────────────────────┤
│     Windows Client      │           Android Client               │
│     (tdesktop fork)     │       (Telegram-FOSS fork)             │
│       C++ / Qt 6        │          Kotlin / Java                 │
└────────────┬────────────┴───────────────┬───────────────────────┘
             │                            │
             │     MTProto / WebSocket     │
             │                            │
             ▼                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                      API GATEWAY                                 │
│                  (Reverse Proxy + Auth)                          │
└────────────────────────────┬────────────────────────────────────┘
                             │
         ┌───────────────────┼───────────────────┐
         │                   │                   │
         ▼                   ▼                   ▼
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│Auth Service │     │Msg Service  │     │File Service │
│             │     │             │     │             │
│ - Register  │     │ - Send      │     │ - Upload    │
│ - Login     │     │ - Receive   │     │ - Download  │
│ - Sessions  │     │ - History   │     │ - Storage   │
└──────┬──────┘     └──────┬──────┘     └──────┬──────┘
       │                   │                   │
       ▼                   ▼                   ▼
┌─────────────────────────────────────────────────────────────────┐
│                       DATA LAYER                                 │
├─────────────┬─────────────┬─────────────┬───────────────────────┤
│ PostgreSQL  │    Redis    │   MinIO     │      RabbitMQ         │
│  (Main DB)  │   (Cache)   │  (Files)    │     (Queues)          │
└─────────────┴─────────────┴─────────────┴───────────────────────┘
```

## Quick Start

### Prerequisites

- Docker and Docker Compose
- .NET 8 SDK (for development)
- Git

### Development Setup

1. **Clone the repository**
   ```bash
   git clone https://github.com/your-org/localtelegram.git
   cd localtelegram
   ```

2. **Copy environment configuration**
   ```bash
   cp .env.example .env
   ```

3. **Edit `.env` with your settings**
   - Change default passwords
   - Set JWT secret
   - Configure other settings as needed

4. **Start infrastructure services**
   ```bash
   docker-compose up -d postgres redis minio rabbitmq
   ```

5. **Run database migrations**
   The database schema will be automatically created when the PostgreSQL container starts.

6. **Start the services**
   ```bash
   docker-compose up -d
   ```

### Service URLs

| Service | URL | Description |
|---------|-----|-------------|
| API Gateway | http://localhost:5000 | Main API entry point |
| Auth Service | http://localhost:5001 | Authentication API |
| Message Service | http://localhost:5002 | Messaging API |
| File Service | http://localhost:5003 | File upload/download |
| MinIO Console | http://localhost:9001 | Object storage management |
| RabbitMQ Management | http://localhost:15672 | Message queue management |

### API Documentation

Each service provides Swagger documentation:
- API Gateway: http://localhost:5000/swagger
- Auth Service: http://localhost:5001/swagger
- Message Service: http://localhost:5002/swagger
- File Service: http://localhost:5003/swagger

## Project Structure

```
LocalTelegram/
├── docker-compose.yml          # Docker Compose configuration
├── .env.example                # Environment variables template
├── infra/
│   ├── postgres/
│   │   └── init/
│   │       └── 001_init_schema.sql  # Database schema
│   └── nginx/
│       ├── nginx.conf          # Nginx main configuration
│       └── conf.d/
│           └── default.conf    # Site configuration
└── src/
    └── server/
        ├── ApiGateway/         # API Gateway service (YARP)
        ├── AuthService/        # Authentication service
        ├── MessageService/     # Messaging service
        └── FileService/        # File storage service
```

## API Endpoints

### Auth Service

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | /api/auth/register | Register new user |
| POST | /api/auth/login | Login |
| POST | /api/auth/refresh | Refresh access token |
| POST | /api/auth/logout | Logout current session |
| GET | /api/auth/me | Get current user |
| PUT | /api/auth/me | Update profile |
| POST | /api/auth/change-password | Change password |

### Message Service

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | /api/chats | Get user's chats |
| POST | /api/chats | Create new chat |
| GET | /api/chats/{id} | Get chat details |
| PUT | /api/chats/{id} | Update chat |
| DELETE | /api/chats/{id} | Delete chat |
| POST | /api/chats/{id}/members | Add member |
| DELETE | /api/chats/{id}/members/{userId} | Remove member |
| POST | /api/messages | Send message |
| GET | /api/messages/chat/{chatId} | Get chat messages |
| PUT | /api/messages/{id} | Edit message |
| DELETE | /api/messages/{id} | Delete message |

### File Service

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | /api/files | Upload file |
| GET | /api/files/{id} | Get file metadata |
| GET | /api/files/{id}/download | Download file |
| GET | /api/files/{id}/url | Get presigned URL |
| DELETE | /api/files/{id} | Delete file |

## Development

### Building Services

```bash
# Build all services
docker-compose build

# Build specific service
docker-compose build auth-service
```

### Running Tests

```bash
# TODO: Add test commands
```

### Database Migrations

The database schema is automatically applied when the PostgreSQL container starts. For manual migrations:

```bash
# Connect to PostgreSQL
docker-compose exec postgres psql -U localtelegram -d localtelegram

# Run schema manually
\i /docker-entrypoint-initdb.d/001_init_schema.sql
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| POSTGRES_USER | PostgreSQL username | localtelegram |
| POSTGRES_PASSWORD | PostgreSQL password | localtelegram123 |
| POSTGRES_DB | Database name | localtelegram |
| REDIS_PASSWORD | Redis password | redis123 |
| MINIO_ROOT_USER | MinIO access key | minioadmin |
| MINIO_ROOT_PASSWORD | MinIO secret key | minioadmin123 |
| RABBITMQ_USER | RabbitMQ username | localtelegram |
| RABBITMQ_PASSWORD | RabbitMQ password | rabbitmq123 |
| JWT_SECRET | JWT signing key | (change in production!) |

### Security Notes

1. **Change all default passwords** before deploying to production
2. Use strong JWT secret (at least 32 characters)
3. Enable SSL/TLS in production
4. Configure firewall rules appropriately
5. Review and update rate limiting settings

## Roadmap

### Phase 1: Basic Infrastructure (Current)
- [x] Project structure
- [x] Docker Compose setup
- [x] Database schema
- [x] Auth Service
- [x] Message Service
- [x] File Service
- [ ] Windows Client
- [ ] Android Client

### Phase 2: Core Features
- [ ] Group chats
- [ ] File uploads without limits
- [ ] Image gallery
- [ ] Voice messages

### Phase 3: Multimedia
- [ ] Video messages
- [ ] Streaming video
- [ ] WebRTC infrastructure

### Phase 4: Video Conferences
- [ ] Audio calls
- [ ] Video calls
- [ ] Screen sharing

### Phase 5: Enterprise Features
- [ ] LDAP/AD integration
- [ ] Two-factor authentication
- [ ] Audit logging

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [telegramdesktop/tdesktop](https://github.com/telegramdesktop/tdesktop) - Windows client base
- [Telegram-FOSS-Team/Telegram-FOSS](https://github.com/Telegram-FOSS-Team/Telegram-FOSS) - Android client base
- [loyldg/mytelegram](https://github.com/loyldg/mytelegram) - Server implementation reference
