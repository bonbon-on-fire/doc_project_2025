# Phone Connection Guide for WAHA

## Overview
WAHA (WhatsApp HTTP API) works like WhatsApp Web - it connects to your existing WhatsApp account through QR code scanning. Your phone number becomes the sender for all messages sent through the API.

## ⚠️ Migration Notice
This guide has been updated from WPPConnect to WAHA for better stability and reliability.

## When to Connect Your Phone

### 🟢 **Connect Now (Recommended for Development)**
**Benefits:**
- Test the integration immediately
- Validate webhook connectivity
- Send test messages to verify everything works
- Debug any issues early

### 🟡 **Connect Later (Production Approach)**
**When:**
- After you implement the .NET Core webhook receiver
- When ready for full end-to-end testing
- For production deployment

## Step-by-Step Connection Process

### 1. Start WPPConnect Server
```powershell
cd wppconnect-server/wppconnect-server
npm start
```

### 2. Generate Authentication Token
```powershell
$response = Invoke-WebRequest -Uri "http://localhost:21465/api/Medical-Assistant/MEDICAL_ASSISTANT_SECURE_TOKEN_2025/generate-token" -Method POST -ContentType "application/json"
$json = $response.Content | ConvertFrom-Json
$token = $json.full
Write-Host "Token: $token"
```

### 3. Start Session and Get QR Code
**Option A: Browser Method (Easiest)**
1. Open browser to: `http://localhost:21465/api-docs/`
2. Find the "Auth" section
3. Use the `/api/{session}/start-session` endpoint
4. Enter session: `Medical-Assistant`
5. Execute to start session

**Option B: API Method**
```powershell
# Start session
$headers = @{"Authorization" = "Bearer $token"}
Invoke-WebRequest -Uri "http://localhost:21465/api/Medical-Assistant/start-session" -Method POST -Headers $headers

# Get QR code (will be base64 image)
$qrResponse = Invoke-WebRequest -Uri "http://localhost:21465/api/Medical-Assistant/qrcode-session" -Method GET -Headers $headers
```

### 4. Scan QR Code with Your Phone
1. Open WhatsApp on your phone
2. Go to **Settings** > **Linked Devices** 
3. Tap **"Link a Device"**
4. Scan the QR code displayed
5. Wait for connection confirmation

### 5. Verify Connection
```powershell
# Check session status
$status = Invoke-WebRequest -Uri "http://localhost:21465/api/Medical-Assistant/check-connection-session" -Method GET -Headers $headers
Write-Host $status.Content
```

## Important Notes

### 📱 **Phone Requirements**
- Must have active WhatsApp account
- Phone must have internet connection
- WhatsApp must be running on your phone (doesn't need to stay open after linking)

### 🔐 **Security Considerations**
- QR code expires after 60 seconds
- Session persists until manually disconnected
- Your phone number will be the sender for all API messages
- Messages sent via API will appear in your WhatsApp chat history

### 🔄 **Session Management**
- Session data stored in `wppconnect_tokens/` directory
- Session survives server restarts
- Can disconnect/reconnect anytime via API

### 🎯 **For Medical Assistant Use**
- Consider using a dedicated business phone number
- Ensure compliance with medical privacy regulations
- Test with non-sensitive data first
- Consider using WhatsApp Business API for production

## Troubleshooting

### QR Code Not Appearing
```powershell
# Check if session is ready
Invoke-WebRequest -Uri "http://localhost:21465/api/Medical-Assistant/status-session" -Method GET -Headers $headers
```

### Connection Failed
1. Ensure phone has internet
2. Try regenerating QR code
3. Check server logs for errors
4. Restart session if needed

### Session Lost
```powershell
# Restart session
Invoke-WebRequest -Uri "http://localhost:21465/api/Medical-Assistant/restart-session" -Method POST -Headers $headers
```

## Next Steps After Connection
1. Test sending a message to yourself
2. Verify webhook receives incoming messages
3. Implement .NET Core webhook handler
4. Test end-to-end message flow

## Current Status
- ✅ WPPConnect Server configured and running
- ✅ API endpoints tested and working  
- ⏳ **Ready for phone connection**
- ⏳ Awaiting .NET Core webhook implementation
