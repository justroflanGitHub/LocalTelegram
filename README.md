# LocalTelegram

> A fully autonomous closed messenger for corporate or private use.

## Overview

**LocalTelegram** is a self-hosted messaging system that runs on your own infrastructure, providing complete control over your data and communications. The system is independent of external cloud services and supports unlimited file sizes.

This project is based on [MyTelegram](https://github.com/loyldg/mytelegram) - a C# implementation of the Telegram server-side API.

## Features

- **Autonomy**: Complete independence from external servers and cloud services
- **Data Control**: All data stored on your organization's servers
- **No Limits**: Send files of any size
- **Conferences**: Audio and video calls, screen sharing (Pro version)
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
             │     MTProto Protocol       │
             │                            │
             ▼                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    MYTELEGRAM SERVER                             │
├─────────────────────────────────────────────────────────────────┤
│  Auth Server  │  Gateway Server  │  Messenger  │  File Server   │
│    :20443     │      :20543      │   (internal)│     :20643     │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                       DATA LAYER                                 │
├─────────────┬─────────────┬─────────────┬───────────────────────┤
│   MongoDB   │    Redis    │   MinIO     │      RabbitMQ         │
│  (Main DB)  │   (Cache)   │  (Files)    │     (Queues)          │
└─────────────┴─────────────┴─────────────┴───────────────────────┘
```

## Quick Start

### Option 1: Using Pre-built MyTelegram Images (Recommended)

1. **Copy environment configuration**
   ```bash
   cp .env.mytelegram .env
   ```

2. **Edit `.env` - Set your server IP**
   ```bash
   # Change this to your server's IP address
   SERVER_IP=192.168.1.100
   ```

3. **Create data directories**
   ```bash
   mkdir -p data/redis data/rabbitmq data/mongo/db data/mongo/configdb data/minio
   ```

4. **Start the services**
   ```bash
   docker-compose -f docker-compose.mytelegram.yml up -d
   ```

5. **Check service status**
   ```bash
   docker-compose -f docker-compose.mytelegram.yml ps
   ```

### Option 2: Custom .NET Implementation

See [docs/CLIENT_CONFIGURATION.md](docs/CLIENT_CONFIGURATION.md) for details on the custom microservices implementation.

1. **Copy environment configuration**
   ```bash
   cp .env.example .env
   ```

2. **Start infrastructure services**
   ```bash
   docker-compose up -d postgres redis minio rabbitmq
   ```

3. **Start the services**
   ```bash
   docker-compose up -d
   ```

## Service URLs

| Service | URL | Description |
|---------|-----|-------------|
| Auth Server | Port 20443 | Authentication (MTProto) |
| Gateway Server | Port 20543 | MTProto Gateway |
| File Server | Port 20643 | File upload/download |
| RabbitMQ Management | http://localhost:15672 | Message queue management |
| MinIO Console | http://localhost:9001 | Object storage management |

## Client Setup

### Windows Client (tdesktop)

1. Clone the modified client:
   ```bash
   git clone https://github.com/loyldg/mytelegram-tdesktop.git
   ```

2. Edit `Telegram/SourceFiles/mtproto/mtproto_dc_options.cpp`:
   ```cpp
   #define IpV4 "YOUR_SERVER_IP"  // Change 192.168.1.100 to your IP
   #define IpV4Port 20443
   ```

3. Build the client (requires Visual Studio, CMake, Qt 6)

See [docs/CLIENT_CONFIGURATION.md](docs/CLIENT_CONFIGURATION.md) for detailed build instructions.

### Android Client

1. Clone the modified client:
   ```bash
   git clone https://github.com/loyldg/mytelegram-android.git
   ```

2. Search for `192.168.1.100` and replace with your server IP

3. Build APK using Android Studio

## Testing

### Default Verification Code

For testing, use verification code: **22222**

1. Open the client
2. Enter any phone number (e.g., +1234567890)
3. Enter verification code: **22222**
4. You should be logged in

## Project Structure

```
LocalTelegram/
├── docker-compose.mytelegram.yml  # MyTelegram server configuration
├── docker-compose.yml             # Custom .NET services configuration
├── .env.mytelegram                # MyTelegram environment config
├── .env.example                   # Custom services environment config
├── docs/
│   └── CLIENT_CONFIGURATION.md    # Client build instructions
├── infra/
│   ├── postgres/
│   │   └── init/
│   │       └── 001_init_schema.sql  # Database schema (custom)
│   └── nginx/
│       └── conf.d/
│           └── default.conf       # Reverse proxy config
├── external/                      # Cloned repositories (gitignored)
│   ├── mytelegram/                # Server source
│   ├── mytelegram-tdesktop/       # Windows client
│   └── mytelegram-android/        # Android client
└── src/
    └── server/                    # Custom .NET services
        ├── ApiGateway/
        ├── AuthService/
        ├── MessageService/
        └── FileService/
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| SERVER_IP | Your server's IP address | 127.0.0.1 |
| VERIFY_CODE | Fixed test verification code | 22222 |
| DB_NAME | MongoDB database name | localtelegram |
| MINIO_ACCESS_KEY | MinIO access key | localtelegram |
| MINIO_SECRET_KEY | MinIO secret key | localtelegram123456 |
| RABBITMQ_USER | RabbitMQ username | localtelegram |
| RABBITMQ_PASSWORD | RabbitMQ password | localtelegram123 |

### Security Notes

1. **Change all default passwords** before deploying to production
2. **Update SERVER_IP** to your actual server address
3. Enable SSL/TLS in production
4. Configure firewall rules appropriately
5. Change `ACCESS_HASH_SECRET` to a unique value

## Supported Features

### Open Source (Free)
- API Layer: 222
- MTProto Transports: Abridged, Intermediate
- Private Chat
- Supergroup Chat
- Channel

### Pro Version (Paid)
- End-to-End Encrypted Chat
- Voice & Video Calls
- Bot Support
- Privacy Settings & 2FA
- Stickers
- Reactions
- Forum Topics
- Scheduled Messages
- And more...

## Roadmap

### Phase 1: Basic Infrastructure (Current)
- [x] Project structure
- [x] Docker Compose setup (MyTelegram)
- [x] Server configuration
- [x] Client configuration documentation
- [ ] Build Windows client
- [ ] Build Android client
- [ ] Integration testing

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

## Building MyTelegram Server from Source

If you want to build the server from source instead of using pre-built images:

```bash
cd external/mytelegram

# Build Docker images
./scripts/build-all-amd64.sh  # Linux/amd64
# or
./scripts/build-all-arm64.sh  # Linux/arm64

# Update docker-compose to use local images
# Set MYTELEGRAM_REGISTRY=local in .env
```

## Troubleshooting

### Connection Failed
- Verify server is running: `docker-compose -f docker-compose.mytelegram.yml ps`
- Check logs: `docker-compose -f docker-compose.mytelegram.yml logs auth-server`
- Verify firewall allows ports 20443, 20543, 20643
- Verify IP address in client matches server IP

### Authentication Failed
- Check auth-server logs
- Verify verification code is correct (default: 22222)
- Try with a different phone number

### File Upload Issues
- Check file-server logs
- Verify MinIO is running
- Check MinIO console at http://localhost:9001

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [loyldg/mytelegram](https://github.com/loyldg/mytelegram) - Server implementation
- [loyldg/mytelegram-tdesktop](https://github.com/loyldg/mytelegram-tdesktop) - Windows client
- [loyldg/mytelegram-android](https://github.com/loyldg/mytelegram-android) - Android client
- [telegramdesktop/tdesktop](https://github.com/telegramdesktop/tdesktop) - Original Windows client
- [Telegram-FOSS-Team/Telegram-FOSS](https://github.com/Telegram-FOSS-Team/Telegram-FOSS) - Original Android FOSS client
