# Task 1 WAHA Migration Checklist

## Migration Overview
Switching from WPPConnect to WAHA (WhatsApp HTTP API) for better stability and production readiness.

## Why We Migrated

### ❌ WPPConnect Issues Encountered
- [x] Sharp module errors on ARM64 Windows (libvips compatibility)
- [x] Complex authentication token handling (token vs full confusion)  
- [x] Session instability and dependency conflicts
- [x] Outdated dependencies and maintenance gaps

### ✅ WAHA Benefits
- [ ] More stable and production-ready
- [ ] Better Docker support and documentation
- [ ] Cleaner API design and error handling
- [ ] Active development and maintenance
- [ ] Multiple engine support (WEBJS, VENOM, NOWEB)
- [ ] Better session persistence

## WAHA Setup Tasks

### Core Setup
- [ ] Pull WAHA Docker image (`devlikeapro/waha`)
- [ ] Create environment configuration file
- [ ] Configure medical assistant specific settings
- [ ] Set up Docker container with proper volumes
- [ ] Verify WAHA server startup

### Configuration
- [ ] Configure webhook URL: `http://localhost:5000/api/webhook/whatsapp`
- [ ] Set webhook events: `message,message.any,state.change`
- [ ] Configure security token: `MEDICAL_ASSISTANT_SECURE_TOKEN_2025`
- [ ] Set default engine to WEBJS
- [ ] Configure medical privacy settings (skip presence, skip receipt)
- [ ] Set up session management for `medical-assistant` session

### Testing
- [ ] Test 1: WAHA Server starts successfully on port 3000
- [ ] Test 2: API documentation accessible at `http://localhost:3000/docs`
- [ ] Test 3: Session creation and management works
- [ ] Test 4: QR code generation works
- [ ] Test 5: Phone connection and scanning works
- [ ] Test 6: Webhook configuration is accessible
- [ ] Test 7: Send test message functionality

### Documentation
- [ ] Create WAHA setup guide
- [ ] Update design document to reference WAHA
- [ ] Update main tasks.md file
- [ ] Create Docker Compose configuration
- [ ] Document API endpoints and authentication
- [ ] Create troubleshooting guide

## Implementation Details

### Docker Configuration
```powershell
# Pull image
docker pull devlikeapro/waha

# Run with configuration
docker run -it --rm `
  -p 3000:3000 `
  --env-file .env `
  -v "${PWD}/tokens:/app/tokens" `
  -v "${PWD}/.sessions:/app/.sessions" `
  devlikeapro/waha
```

### Environment Variables
```env
WHATSAPP_HOOK_URL=http://localhost:5000/api/webhook/whatsapp
WHATSAPP_HOOK_EVENTS=message,message.any,state.change
WHATSAPP_DEFAULT_ENGINE=WEBJS
WHATSAPP_START_SESSION=medical-assistant
WAHA_SECURITY_TOKEN=MEDICAL_ASSISTANT_SECURE_TOKEN_2025
WHATSAPP_SWAGGER_CONFIG_ADVANCED=true
```

### Key API Endpoints
- `POST /api/sessions/start` - Start session
- `GET /api/sessions/` - List sessions  
- `GET /api/{session}/auth/qr` - Get QR code
- `POST /api/sendText` - Send message
- `GET /health` - Health check

### Authentication
All requests require header:
```
X-Api-Key: MEDICAL_ASSISTANT_SECURE_TOKEN_2025
```

## Code Quality Checks
- [ ] No unnecessary complexity in new setup
- [ ] Clean Docker configuration with proper volumes
- [ ] Well-documented environment variables
- [ ] Comprehensive setup guide created
- [ ] All new configuration tested locally
- [ ] No build warnings in Docker setup
- [ ] All tests pass before marking as complete

## Migration Steps
1. [ ] Stop WPPConnect server (if running)
2. [ ] Set up WAHA using Docker method
3. [ ] Test WAHA server and API access
4. [ ] Test phone connection with WAHA
5. [ ] Update any existing API integrations
6. [ ] Verify webhook functionality
7. [ ] Update documentation and guides

## Outstanding Tasks
- [ ] Complete WAHA Docker setup
- [ ] Test phone connection with QR code
- [ ] Verify webhook delivery to .NET Core app
- [ ] Create production Docker Compose setup
- [ ] Performance test WAHA vs WPPConnect

## Success Criteria
- [ ] WAHA server runs stable without dependency issues
- [ ] Phone connection works reliably
- [ ] QR code generation is smooth and fast
- [ ] Webhook delivery works consistently
- [ ] API responses are clear and well-structured
- [ ] Session persistence works across restarts
- [ ] No sharp/libvips related errors
- [ ] Documentation is comprehensive and clear

## Notes
- WAHA runs on port 3000 (vs WPPConnect on 21465)
- Uses X-Api-Key header instead of Bearer token
- Simpler session management
- Better error messages and debugging
- More production-ready out of the box

## Task 1 Status: 🔄 IN PROGRESS
Migrating from WPPConnect to WAHA for better stability and reliability.
