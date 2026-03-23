#!/bin/bash
# LocalTelegram Android Client Configuration Script
# This script configures the Android client to connect to your LocalTelegram server

set -e

SERVER_IP="${1:-}"
PORT="${2:-20443}"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

echo -e "${CYAN}=== LocalTelegram Android Client Configuration ===${NC}"
echo ""

# Check if server IP is provided
if [ -z "$SERVER_IP" ]; then
    echo -e "${RED}Error: Server IP address is required${NC}"
    echo "Usage: $0 <server_ip> [port]"
    echo "Example: $0 192.168.1.100 20443"
    exit 1
fi

# Android client directory
ANDROID_DIR="external/mytelegram-android"

# Check if the directory exists
if [ ! -d "$ANDROID_DIR" ]; then
    echo -e "${RED}Error: Android client directory not found at $ANDROID_DIR${NC}"
    echo -e "${YELLOW}Make sure you have cloned the mytelegram-android repository:${NC}"
    echo "  git clone https://github.com/loyldg/mytelegram-android.git external/mytelegram-android"
    exit 1
fi

echo -e "${GREEN}Configuring client to connect to server: $SERVER_IP:$PORT${NC}"
echo ""

# Find and replace IP addresses in the Android client
# The configuration might be in different files depending on the version

# Common locations for server configuration
CONFIG_FILES=(
    "TMessagesProj/jni/tgnet/Datacenter.cpp"
    "TMessagesProj/src/main/java/org/telegram/messenger/BuildVars.java"
    "TMessagesProj/jni/secure.cpp"
)

FILES_CHANGED=0

for FILE in "${CONFIG_FILES[@]}"; do
    FULL_PATH="$ANDROID_DIR/$FILE"
    if [ -f "$FULL_PATH" ]; then
        echo -e "${CYAN}Processing: $FILE${NC}"
        
        # Replace common IP patterns
        sed -i "s/192\.168\.1\.100/$SERVER_IP/g" "$FULL_PATH"
        sed -i "s/149\.154\.167\.[0-9]*/$SERVER_IP/g" "$FULL_PATH"
        
        # Replace port if specified and different from default
        if [ "$PORT" != "20443" ]; then
            sed -i "s/:20443/:$PORT/g" "$FULL_PATH"
            sed -i "s/Port = 20443/Port = $PORT/g" "$FULL_PATH"
        fi
        
        FILES_CHANGED=$((FILES_CHANGED + 1))
    fi
done

# Search for any other files containing the old IP
echo ""
echo -e "${CYAN}Searching for additional files with IP configuration...${NC}"
GREP_FILES=$(grep -r -l "192\.168\.1\.100" "$ANDROID_DIR" 2>/dev/null || true)

if [ -n "$GREP_FILES" ]; then
    echo "$GREP_FILES" | while read -r FILE; do
        # Skip .git directory
        if [[ "$FILE" == *".git"* ]]; then
            continue
        fi
        echo -e "${YELLOW}Found in: $FILE${NC}"
        sed -i "s/192\.168\.1\.100/$SERVER_IP/g" "$FILE"
    done
fi

echo ""
if [ $FILES_CHANGED -gt 0 ]; then
    echo -e "${GREEN}Configuration updated successfully!${NC}"
    echo -e "${GREEN}Files processed: $FILES_CHANGED${NC}"
else
    echo -e "${YELLOW}Warning: No configuration files were found to update${NC}"
    echo -e "${YELLOW}You may need to manually search for server configuration in the Android client${NC}"
fi

echo ""
echo -e "${CYAN}Next steps:${NC}"
echo -e "1. Open Android Studio"
echo -e "2. Open project from: $ANDROID_DIR"
echo -e "3. Let Gradle sync complete"
echo -e "4. Build > Build Bundle(s) / APK(s) > Build APK(s)"
echo -e "5. Install APK on device"
echo ""
echo -e "${YELLOW}Test verification code: 22222${NC}"
