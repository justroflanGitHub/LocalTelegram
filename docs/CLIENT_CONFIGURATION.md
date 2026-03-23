# LocalTelegram Client Configuration Guide

This guide explains how to configure the Windows (tdesktop) and Android clients to connect to your LocalTelegram server.

## Prerequisites

- LocalTelegram server running (see README.md for server setup)
- Your server's IP address or hostname

## Windows Client (tdesktop)

### Step 1: Clone the Client Repository

```bash
cd external
git clone https://github.com/loyldg/mytelegram-tdesktop.git mytelegram-tdesktop
```

### Step 2: Configure Server Address

Edit the file: `Telegram/SourceFiles/mtproto/mtproto_dc_options.cpp`

Find these lines (around line 31-36):

```cpp
#define IpV4 "192.168.1.100"
#define IpV6 "192.168.1.100"
#define IpV4Port 20443
#define IpV4Port2 20443
#define IpV6Port 20443
#define IpV6Port2 20443
```

Change `192.168.1.100` to your server's IP address:

```cpp
#define IpV4 "YOUR_SERVER_IP"
#define IpV6 "YOUR_SERVER_IP"
#define IpV4Port 20443
#define IpV4Port2 20443
#define IpV6Port 20443
#define IpV6Port2 20443
```

### Step 3: Build the Client

#### Prerequisites for Building
- Visual Studio 2022 or later
- CMake
- vcpkg
- Qt 6

#### Build Steps

```bash
# Initialize vcpkg
git clone https://github.com/Microsoft/vcpkg.git
cd vcpkg
.\bootstrap-vcpkg.bat
.\vcpkg integrate install

# Configure and build
cd external/mytelegram-tdesktop
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release
```

For detailed build instructions, see: https://github.com/telegramdesktop/tdesktop/blob/dev/docs/building-msvc.md

### Step 4: Run the Client

After building, the executable will be in:
```
build\out\Debug\Telegram.exe
# or
build\out\Release\Telegram.exe
```

## Android Client

### Step 1: Clone the Client Repository

```bash
cd external
git clone https://github.com/loyldg/mytelegram-android.git mytelegram-android
```

### Step 2: Configure Server Address

The Android client configuration is typically in one of these locations:
- `TMessagesProj/jni/tgnet/Datacenter.cpp`
- `TMessagesProj/src/main/java/org/telegram/messenger/BuildVars.java`

Search for "192.168.1.100" or similar IP addresses and replace with your server IP.

### Step 3: Build the APK

#### Prerequisites for Building
- Android Studio
- Android SDK (API 31+)
- NDK

#### Build Steps

1. Open Android Studio
2. Open the project from `external/mytelegram-android`
3. Let Gradle sync complete
4. Build > Build Bundle(s) / APK(s) > Build APK(s)

For detailed build instructions, see: https://github.com/DrKLO/Telegram/blob/master/README.md

### Step 4: Install the APK

```bash
adb install build/outputs/apk/debug/Telegram-debug.apk
```

Or transfer the APK to your device and install manually.

## Testing the Connection

### Default Verification Code

For testing, the server is configured with a fixed verification code: **22222**

1. Open the client
2. Enter any phone number (e.g., +1234567890)
3. When prompted for the verification code, enter: **22222**
4. You should be logged in successfully

### Troubleshooting

#### Connection Failed
- Verify the server is running: `docker-compose ps`
- Check server logs: `docker-compose logs auth-server`
- Verify firewall allows ports 20443, 20543, 20643
- Verify IP address in client matches server IP

#### Authentication Failed
- Check auth-server logs: `docker-compose logs auth-server`
- Verify verification code is correct (default: 22222)
- Try with a different phone number

#### File Upload/Download Issues
- Check file-server logs: `docker-compose logs file-server`
- Verify MinIO is running: `docker-compose ps minio`
- Check MinIO console at http://localhost:9001

## Server Ports Reference

| Port | Service | Description |
|------|---------|-------------|
| 20443 | Auth Server | Authentication and authorization |
| 20543 | Gateway Server | MTProto gateway |
| 20643 | File Server | File upload/download |
| 15672 | RabbitMQ | Management console |
| 9001 | MinIO | Storage console |

## Environment Variables

Key environment variables in `.env.mytelegram`:

| Variable | Description | Default |
|----------|-------------|---------|
| SERVER_IP | Your server's IP address | 127.0.0.1 |
| VERIFY_CODE | Fixed test verification code | 22222 |
| DB_NAME | MongoDB database name | localtelegram |
| MINIO_ACCESS_KEY | MinIO access key | localtelegram |
| MINIO_SECRET_KEY | MinIO secret key | localtelegram123456 |

## Next Steps

After successful connection:
1. Create additional test accounts
2. Test messaging between accounts
3. Test file sharing
4. Test group chats
5. Report any issues to the project repository
