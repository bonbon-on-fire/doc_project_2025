# Docker Setup Guide for WPPConnect Medical Assistant

## Prerequisites

### Install Docker Desktop for Windows
1. Download Docker Desktop from: https://www.docker.com/products/docker-desktop/
2. Run the installer and follow setup instructions
3. Restart your computer if required
4. Verify installation: `docker --version`

## Docker Configuration Files

### 1. Dockerfile (Already provided)
Located at: `wppconnect-server/wppconnect-server/Dockerfile`
- Uses Node.js 22.17.1 Alpine Linux
- Installs required dependencies for image processing
- Builds TypeScript code
- Exposes port 21465

### 2. Medical Assistant Docker Compose
File: `docker-compose.medical-assistant.yml`

```yaml
version: "3.8"

services:
  wppconnect-medical-assistant:
    container_name: wppconnect-medical-assistant
    restart: unless-stopped
    build:
      context: .
      dockerfile: Dockerfile
    volumes:
      - ./src/config.ts:/usr/src/wpp-server/src/config.ts
      - wppconnect_sessions:/usr/src/wpp-server/userDataDir
      - wppconnect_tokens:/usr/src/wpp-server/tokens
      - wppconnect_logs:/usr/src/wpp-server/logs
    ports:
      - "21465:21465"
    environment:
      - NODE_ENV=production
      - TZ=UTC
    networks:
      - medical-assistant-network
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:21465/api/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 60s

volumes:
  wppconnect_sessions:
    driver: local
  wppconnect_tokens:
    driver: local
  wppconnect_logs:
    driver: local

networks:
  medical-assistant-network:
    driver: bridge
```

## Build and Run Commands

### Build Docker Image
```bash
cd wppconnect-server/wppconnect-server
docker build -t wppconnect-medical-assistant .
```

### Run with Docker Compose
```bash
# Start the service
docker-compose -f docker-compose.medical-assistant.yml up -d

# View logs
docker-compose -f docker-compose.medical-assistant.yml logs -f

# Stop the service
docker-compose -f docker-compose.medical-assistant.yml down
```

### Manual Docker Run (Alternative)
```bash
docker run -d \
  --name wppconnect-medical-assistant \
  --restart unless-stopped \
  -p 21465:21465 \
  -v $(pwd)/src/config.ts:/usr/src/wpp-server/src/config.ts \
  -v wppconnect_sessions:/usr/src/wpp-server/userDataDir \
  -v wppconnect_tokens:/usr/src/wpp-server/tokens \
  wppconnect-medical-assistant
```

## Key Features of Docker Setup

### Volume Mounts
- **Config**: Medical assistant configuration persisted
- **Sessions**: WhatsApp session data preserved across restarts
- **Tokens**: Authentication tokens stored persistently
- **Logs**: Application logs accessible from host

### Health Checks
- Monitors service health every 30 seconds
- Automatically restarts if health check fails
- 60-second startup grace period

### Network Configuration
- Isolated network for medical assistant services
- Ready for .NET Core service integration
- Proper service discovery between containers

## Integration with .NET Core

### Future Docker Compose Extension
When ready to add .NET Core service:

```yaml
medical-assistant-api:
  container_name: medical-assistant-api
  build:
    context: ../../src
    dockerfile: Dockerfile
  ports:
    - "5000:80"
  environment:
    - ASPNETCORE_ENVIRONMENT=Production
    - WhatsApp__ServerUrl=http://wppconnect-medical-assistant:21465/api
  networks:
    - medical-assistant-network
  depends_on:
    - wppconnect-medical-assistant
```

## Troubleshooting

### Port Conflicts
If port 21465 is already in use:
```bash
# Check what's using the port
netstat -ano | findstr :21465

# Change port in docker-compose.yml
ports:
  - "21466:21465"  # Use different external port
```

### Container Won't Start
```bash
# Check container logs
docker logs wppconnect-medical-assistant

# Check container status
docker ps -a

# Restart container
docker restart wppconnect-medical-assistant
```

### Volume Permission Issues
On Windows with WSL2:
```bash
# Ensure proper file permissions
chmod +x src/config.ts
```

## Production Considerations

### Security
- Use environment variables for sensitive configuration
- Implement proper secrets management
- Use non-root user in container
- Regular security updates

### Performance
- Resource limits for containers
- Log rotation configuration
- Monitoring and alerting setup

### Backup
- Regular backup of session data
- Token backup strategy
- Configuration backup

## Testing Docker Setup

### 1. Build Test
```bash
docker build -t wppconnect-medical-assistant .
echo "Build result: $?"
```

### 2. Run Test
```bash
docker run --rm -p 21465:21465 wppconnect-medical-assistant &
sleep 30
curl http://localhost:21465/api/health
docker stop $(docker ps -q --filter ancestor=wppconnect-medical-assistant)
```

### 3. Health Check Test
```bash
docker inspect wppconnect-medical-assistant | grep -A 5 "Health"
```

## Next Steps After Docker Setup

1. ✅ **Install Docker Desktop**
2. ✅ **Build Docker image**
3. ✅ **Test container startup**
4. ✅ **Verify health checks**
5. ✅ **Test API endpoints**
6. ✅ **Integration with .NET Core service**

This Docker setup provides a production-ready foundation for the WhatsApp messaging framework integration with your medical assistant application.
