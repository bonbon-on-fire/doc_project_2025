# WPPConnect Server Setup Documentation

## Overview
This document outlines the setup and configuration of WPPConnect Server for the Medical Assistant WhatsApp integration.

## Installation Steps

### 1. Clone Repository
```bash
git clone https://github.com/wppconnect-team/wppconnect-server.git
cd wppconnect-server
```

### 2. Install Dependencies
```bash
npm install
```

### 3. Fix ARM64 Windows Compatibility (if needed)
If you encounter sharp module errors on ARM64 Windows:
```bash
npm uninstall sharp
npm install sharp --platform=win32 --arch=x64
```

### 4. Build Project
```bash
npm run build
```

### 5. Start Development Server
```bash
npm run dev
```

## Configuration

### Medical Assistant Configuration (`src/config.ts`)
```typescript
export default {
  secretKey: 'MEDICAL_ASSISTANT_SECURE_TOKEN_2025',
  host: 'http://localhost',
  port: '21465',
  deviceName: 'Medical-Assistant',
  poweredBy: 'AI-Medical-Assistant',
  startAllSession: true,
  tokenStoreType: 'file',
  webhook: {
    url: 'http://localhost:5000/api/webhook/whatsapp', // .NET Core webhook
    autoDownload: true,
    readMessage: false, // Don't auto-mark as read for medical context
    allUnreadOnStart: true,
    onPresenceChanged: false, // Privacy consideration
    ignore: ['status@broadcast'],
  },
  log: {
    level: 'info',
    logger: ['console', 'file'],
  },
  // ... other configurations
};
```

## Key Configuration Changes for Medical Assistant

### Privacy & Medical Context
- `readMessage: false` - Messages aren't automatically marked as read
- `onPresenceChanged: false` - Presence updates disabled for privacy
- `allUnreadOnStart: true` - Retrieve all unread messages on startup

### Integration Settings
- **Webhook URL**: `http://localhost:5000/api/webhook/whatsapp`
- **Device Name**: `Medical-Assistant`
- **Secret Key**: `MEDICAL_ASSISTANT_SECURE_TOKEN_2025`

### Security Features
- Custom secret key for authentication
- Webhook authentication required
- File-based token storage
- Status broadcast messages ignored

## Server Information

### Endpoints
- **Server**: http://localhost:21465
- **API Documentation**: http://localhost:21465/api-docs
- **Health Check**: http://localhost:21465/api/health

### Key API Endpoints for .NET Core Integration
- `POST /{session}/send-message` - Send text messages
- `POST /{session}/send-image` - Send images
- `POST /{session}/send-file` - Send documents
- `POST /{session}/{secretKey}/generate-token` - Generate auth token
- `GET /{session}/status` - Check session status
- `GET /{session}/qrcode` - Get QR code for authentication

## Authentication Flow

### 1. Generate Token
```http
POST http://localhost:21465/api/{session}/MEDICAL_ASSISTANT_SECURE_TOKEN_2025/generate-token
```

### 2. Start Session
```http
POST http://localhost:21465/api/{session}/start-session
```

### 3. Get QR Code
```http
GET http://localhost:21465/api/{session}/qrcode
```

### 4. Check Session Status
```http
GET http://localhost:21465/api/{session}/status
```

## Troubleshooting

### Sharp Module Error (ARM64 Windows)
**Error**: Could not load the "sharp" module using the win32-arm64 runtime
**Solution**: 
```bash
npm uninstall sharp
npm install sharp --platform=win32 --arch=x64
```

### Port Already in Use
**Error**: Port 21465 is already in use
**Solution**: 
- Check for existing WPPConnect instances: `netstat -ano | findstr :21465`
- Kill existing process or change port in config.ts

### Webhook Connection Issues
**Error**: Webhook endpoint not reachable
**Solution**: 
- Ensure .NET Core app is running on port 5000
- Verify webhook URL in config.ts
- Check firewall/antivirus settings

## Next Steps

1. **Test QR Code Generation**: Scan QR code with WhatsApp to authenticate
2. **Verify Webhook**: Send test message to confirm webhook delivery
3. **Set up Docker**: Create container for production deployment
4. **Integration Testing**: Connect with .NET Core application

## Security Considerations

- Change default secret key in production
- Use HTTPS for webhook URLs in production
- Implement proper authentication middleware
- Monitor and log all API access
- Regular security updates for dependencies

## Production Deployment

For production deployment, consider:
- Docker containerization
- Environment-specific configuration
- SSL/TLS certificates
- Load balancing
- Monitoring and alerting
- Backup and recovery procedures
