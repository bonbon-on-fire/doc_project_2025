# WAHA (WhatsApp HTTP API) Setup Guide

## Overview
This document outlines the setup and configuration of WAHA for the Medical Assistant WhatsApp integration. WAHA is a more stable and production-ready alternative to WPPConnect.

## Why WAHA vs WPPConnect

### ✅ WAHA Advantages
- **More Stable**: Better maintained and more reliable
- **Better Documentation**: Clearer API docs and examples  
- **Multiple Engines**: Supports WEBJS, VENOM, NOWEB engines
- **Docker-First**: Designed for containerization from the start
- **Active Development**: More frequent updates and bug fixes
- **Professional Focus**: Built for business/production use
- **Better Error Handling**: More informative error messages
- **Session Management**: More robust session persistence

### ❌ WPPConnect Issues We Encountered
- Sharp module errors on ARM64 Windows
- Complex authentication (token vs full confusion)
- Session instability and dependency conflicts
- Outdated dependencies and maintenance gaps

## Installation Methods

### Option 1: Docker (Recommended)

#### Step 1: Pull WAHA Docker Image
```powershell
docker pull devlikeapro/waha
```

#### Step 2: Create Environment File
Create `.env` file:
```env
# Medical Assistant WAHA Configuration
WHATSAPP_HOOK_URL=http://localhost:5000/api/webhook/whatsapp
WHATSAPP_HOOK_EVENTS=message,message.any,state.change
WHATSAPP_DEFAULT_ENGINE=WEBJS
WHATSAPP_START_SESSION=medical-assistant
WHATSAPP_SWAGGER_CONFIG_ADVANCED=true
```

#### Step 3: Run WAHA Container
```powershell
docker run -it --rm `
  -p 3000:3000 `
  --env-file .env `
  -v "${PWD}/tokens:/app/tokens" `
  -v "${PWD}/.sessions:/app/.sessions" `
  devlikeapro/waha
```

#### Step 4: Verify Installation
Open browser to: `http://localhost:3000/docs`

### Option 2: Docker Compose (Production)

Create `docker-compose.waha.yml`:
```yaml
version: '3.8'

services:
  waha-medical-assistant:
    image: devlikeapro/waha
    container_name: waha-medical-assistant
    restart: unless-stopped
    ports:
      - "3000:3000"
    environment:
      # Medical Assistant Configuration
      WHATSAPP_HOOK_URL: "http://localhost:5000/api/webhook/whatsapp"
      WHATSAPP_HOOK_EVENTS: "message,message.any,state.change"
      WHATSAPP_DEFAULT_ENGINE: "WEBJS"
      WHATSAPP_START_SESSION: "medical-assistant"
      WHATSAPP_SWAGGER_CONFIG_ADVANCED: "true"
      # Security
      WAHA_SECURITY_TOKEN: "MEDICAL_ASSISTANT_SECURE_TOKEN_2025"
    volumes:
      # Persist session data
      - waha_sessions:/app/.sessions
      # Persist tokens  
      - waha_tokens:/app/tokens
      # Persist files
      - waha_files:/app/files
    networks:
      - medical-assistant-network
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:3000/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 60s

volumes:
  waha_sessions:
    driver: local
  waha_tokens:
    driver: local  
  waha_files:
    driver: local

networks:
  medical-assistant-network:
    driver: bridge
```

Run with:
```powershell
docker-compose -f docker-compose.waha.yml up -d
```

## Configuration

### Medical Assistant Specific Settings

#### Environment Variables
```env
# Core Configuration
WHATSAPP_HOOK_URL=http://localhost:5000/api/webhook/whatsapp
WHATSAPP_HOOK_EVENTS=message,message.any,state.change
WHATSAPP_DEFAULT_ENGINE=WEBJS

# Medical Privacy Settings
WHATSAPP_HOOK_SKIP_PRESENCE=true
WHATSAPP_HOOK_SKIP_RECEIPT=true

# Session Management
WHATSAPP_START_SESSION=medical-assistant
WHATSAPP_RESTART_ALL_SESSIONS=true

# Security
WAHA_SECURITY_TOKEN=MEDICAL_ASSISTANT_SECURE_TOKEN_2025

# API Documentation
WHATSAPP_SWAGGER_CONFIG_ADVANCED=true
```

#### Webhook Configuration
WAHA will send webhooks to your .NET Core application at:
- **URL**: `http://localhost:5000/api/webhook/whatsapp`
- **Events**: `message`, `message.any`, `state.change`
- **Format**: JSON POST requests

### Engine Options

WAHA supports multiple WhatsApp engines:

1. **WEBJS** (Recommended)
   - Most stable and reliable
   - Good for production use
   - Best session persistence

2. **VENOM**
   - Alternative option
   - Good performance

3. **NOWEB** (Pro version)
   - No browser required
   - Fastest performance
   - Requires WAHA Pro license

## API Endpoints

### Authentication
All requests require the security token in header:
```
X-Api-Key: MEDICAL_ASSISTANT_SECURE_TOKEN_2025
```

### Key Endpoints

#### Session Management
- `POST /api/sessions/start` - Start session
- `GET /api/sessions/` - List sessions
- `GET /api/sessions/{session}/status` - Check session status
- `POST /api/sessions/stop` - Stop session

#### QR Code
- `GET /api/{session}/auth/qr` - Get QR code for scanning

#### Messaging
- `POST /api/sendText` - Send text message
- `POST /api/sendImage` - Send image
- `POST /api/sendDocument` - Send document

#### Webhooks
- Automatic webhook delivery to configured endpoint
- Events: message received, session state changes

## Phone Connection Process

### Step 1: Start WAHA Server
```powershell
docker run -it --rm -p 3000:3000 devlikeapro/waha
```

### Step 2: Access API Documentation
Open: `http://localhost:3000/docs`

### Step 3: Start Session
```powershell
$headers = @{"X-Api-Key" = "MEDICAL_ASSISTANT_SECURE_TOKEN_2025"}
Invoke-RestMethod -Uri "http://localhost:3000/api/sessions/start" -Method POST -Headers $headers -Body '{"name": "medical-assistant"}' -ContentType "application/json"
```

### Step 4: Get QR Code
```powershell
Invoke-RestMethod -Uri "http://localhost:3000/api/medical-assistant/auth/qr" -Method GET -Headers $headers
```

### Step 5: Scan with Phone
1. Open WhatsApp on phone
2. Settings → Linked Devices → Link a Device
3. Scan QR code from API response
4. Wait for connection confirmation

### Step 6: Verify Connection
```powershell
Invoke-RestMethod -Uri "http://localhost:3000/api/sessions/" -Method GET -Headers $headers
```

## Testing

### Send Test Message
```powershell
$headers = @{"X-Api-Key" = "MEDICAL_ASSISTANT_SECURE_TOKEN_2025"}
$body = @{
    session = "medical-assistant"
    chatId = "1234567890@c.us"  # Replace with your phone number
    text = "Hello from Medical Assistant!"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:3000/api/sendText" -Method POST -Headers $headers -Body $body -ContentType "application/json"
```

## Troubleshooting

### Common Issues

#### Session Not Starting
```powershell
# Check WAHA logs
docker logs <container-id>

# Restart session
Invoke-RestMethod -Uri "http://localhost:3000/api/sessions/restart" -Method POST -Headers $headers
```

#### QR Code Not Appearing
```powershell
# Check session status
Invoke-RestMethod -Uri "http://localhost:3000/api/sessions/" -Method GET -Headers $headers

# If session is not started, start it
Invoke-RestMethod -Uri "http://localhost:3000/api/sessions/start" -Method POST -Headers $headers
```

#### Webhook Not Receiving
1. Check .NET Core app is running on port 5000
2. Verify webhook URL in environment variables
3. Check firewall settings

### Health Check
```powershell
Invoke-RestMethod -Uri "http://localhost:3000/health" -Method GET
```

## Next Steps

1. **Set up WAHA** using Docker method above
2. **Test connection** with your phone
3. **Implement .NET Core webhook** receiver (Task 2)
4. **Test end-to-end** message flow
5. **Deploy to production** using Docker Compose

## Migration from WPPConnect

If migrating from WPPConnect:
1. Stop WPPConnect server
2. Set up WAHA using this guide  
3. Update .NET Core webhook endpoint (if needed)
4. Test phone connection with WAHA
5. Update any API calls to use WAHA endpoints

## Security Considerations

- Use strong API key in production
- Configure webhook URL carefully
- Consider using HTTPS in production
- Monitor session logs for security events
- Implement rate limiting on webhook endpoint

## Status: Ready for Implementation
WAHA setup guide complete and ready for medical assistant integration.
