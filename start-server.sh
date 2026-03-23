#!/bin/bash
# LocalTelegram Quick Start Script for Linux/Mac
# This script sets up and starts the LocalTelegram server

set -e

SERVER_IP="${1:-}"
BUILD="${BUILD:-false}"
STOP="${STOP:-false}"
LOGS="${LOGS:-false}"
STATUS="${STATUS:-false}"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

echo -e "${CYAN}=== LocalTelegram Server Management ===${NC}"
echo ""

# Check Docker
if ! command -v docker &> /dev/null; then
    echo -e "${RED}Error: Docker is not installed${NC}"
    echo "Please install Docker from https://docs.docker.com/get-docker/"
    exit 1
fi

# Check Docker Compose
DOCKER_COMPOSE=""
if docker compose version &> /dev/null; then
    DOCKER_COMPOSE="docker compose"
elif command -v docker-compose &> /dev/null; then
    DOCKER_COMPOSE="docker-compose"
else
    echo -e "${RED}Error: Docker Compose is not installed${NC}"
    exit 1
fi

# Handle stop command
if [ "$STOP" = "true" ]; then
    echo -e "${YELLOW}Stopping LocalTelegram server...${NC}"
    $DOCKER_COMPOSE -f docker-compose.mytelegram.yml down
    echo -e "${GREEN}Server stopped.${NC}"
    exit 0
fi

# Handle logs command
if [ "$LOGS" = "true" ]; then
    echo -e "${YELLOW}Showing server logs (Ctrl+C to exit)...${NC}"
    $DOCKER_COMPOSE -f docker-compose.mytelegram.yml logs -f
    exit 0
fi

# Handle status command
if [ "$STATUS" = "true" ]; then
    echo -e "${YELLOW}Server status:${NC}"
    $DOCKER_COMPOSE -f docker-compose.mytelegram.yml ps
    exit 0
fi

# Check if .env file exists
if [ ! -f ".env" ]; then
    echo -e "${YELLOW}Creating .env file from template...${NC}"
    cp ".env.mytelegram" ".env"
    echo -e "${GREEN}Created .env file. Please edit it to set your server IP.${NC}"
fi

# Auto-detect server IP if not provided
if [ -z "$SERVER_IP" ]; then
    echo -e "${YELLOW}Auto-detecting server IP...${NC}"
    
    # Try to get the primary IP address
    if command -v ip &> /dev/null; then
        # Linux
        SERVER_IP=$(ip route get 1 | awk '{print $7; exit}')
    elif command -v ifconfig &> /dev/null; then
        # macOS/BSD
        SERVER_IP=$(ifconfig | grep "inet " | grep -v 127.0.0.1 | awk '{print $2}' | head -n 1)
    fi
    
    if [ -z "$SERVER_IP" ]; then
        echo -e "${YELLOW}Could not auto-detect IP. Using localhost.${NC}"
        SERVER_IP="127.0.0.1"
    else
        echo -e "${GREEN}Detected IP: $SERVER_IP${NC}"
    fi
fi

# Update .env file with server IP
echo -e "${YELLOW}Updating server IP in .env file...${NC}"
sed -i "s/SERVER_IP=.*/SERVER_IP=$SERVER_IP/" .env 2>/dev/null || \
sed -i '' "s/SERVER_IP=.*/SERVER_IP=$SERVER_IP/" .env
echo -e "${GREEN}Server IP set to: $SERVER_IP${NC}"

# Create data directories
echo -e "${YELLOW}Creating data directories...${NC}"
mkdir -p data/redis
mkdir -p data/rabbitmq
mkdir -p data/mongo/db
mkdir -p data/mongo/configdb
mkdir -p data/minio
echo -e "${GREEN}Data directories created.${NC}"

# Build images if requested
if [ "$BUILD" = "true" ]; then
    echo -e "${YELLOW}Building Docker images...${NC}"
    $DOCKER_COMPOSE -f docker-compose.mytelegram.yml build
fi

# Start services
echo -e "${YELLOW}Starting LocalTelegram server...${NC}"
$DOCKER_COMPOSE -f docker-compose.mytelegram.yml up -d

echo ""
echo -e "${GREEN}=== LocalTelegram Server Started ===${NC}"
echo ""
echo -e "${CYAN}Server Configuration:${NC}"
echo "  Server IP: $SERVER_IP"
echo "  Auth Port: 20443"
echo "  Gateway Port: 20543"
echo "  File Port: 20643"
echo ""
echo -e "${CYAN}Management Consoles:${NC}"
echo "  RabbitMQ: http://localhost:15672"
echo "  MinIO: http://localhost:9001"
echo ""
echo -e "${CYAN}Useful Commands:${NC}"
echo "  Check status: STATUS=true ./start-server.sh"
echo "  View logs:    LOGS=true ./start-server.sh"
echo "  Stop server:  STOP=true ./start-server.sh"
echo ""
echo -e "${YELLOW}Test verification code: 22222${NC}"
echo ""
echo -e "${CYAN}Next Steps:${NC}"
echo "1. Configure clients with server IP: $SERVER_IP"
echo "2. See docs/CLIENT_CONFIGURATION.md for build instructions"
echo "3. Login with any phone number and verification code: 22222"
