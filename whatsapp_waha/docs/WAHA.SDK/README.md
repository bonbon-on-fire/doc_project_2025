# WAHA C# SDK Documentation

This documentation covers the C# integration with WAHA (WhatsApp HTTP API) for the WhatsApp WAHA Messaging Framework project.

## 📚 Documentation Structure

- **[Getting Started Guide](README.md)** - Setup, installation, and basic usage
- **[Complete API Reference](API-Reference.md)** - Comprehensive coverage of all WAHA endpoints with C# examples

## Overview

The WhatsAppWaha.Core library provides a comprehensive C# SDK for integrating with WAHA WhatsApp API services. This implementation follows modern .NET Core 9.0 architecture patterns with dependency injection, configuration management, and robust error handling.

### Full API Coverage

This SDK provides C# implementations for all major WAHA API endpoints:

- **Session Management** - Create, start, stop, authenticate sessions
- **Message Sending** - Text, media, location, contact, poll, button messages  
- **Message Receiving** - Webhooks, polling, event handling
- **Contact Management** - Get, update, block contacts, profile pictures
- **Chat Operations** - Archive, delete, mark as read, message management
- **Group Management** - Create, manage participants, settings, invites
- **Media Handling** - Upload, download, convert media files

👉 **[See Complete API Reference](API-Reference.md)** for detailed endpoint documentation with C# examples.

## Architecture

Our WAHA integration is built around several key components:

- **WahaService**: Core service for WAHA API communication
- **Configuration Models**: Strongly-typed settings for WAHA endpoints
- **Message Models**: Data models for sending and receiving WhatsApp messages
- **Resilience Patterns**: Retry policies and circuit breakers using Polly

## Installation

### Prerequisites

- .NET Core 9.0 or later
- WAHA instance running (local or remote)
- Valid WhatsApp Business API session

### NuGet Dependencies

The following packages are included in the WhatsAppWaha.Core project:

```xml
<PackageReference Include="Microsoft.Extensions.Http" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Http.Polly" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="9.0.0" />
<PackageReference Include="Polly" Version="8.5.0" />
<PackageReference Include="Serilog" Version="4.0.2" />
```

## Quick Start

### 1. Configuration Setup

Configure WAHA settings in your `appsettings.json`:

```json
{
  "WahaSettings": {
    "BaseUrl": "http://localhost:3000",
    "Session": "default",
    "ApiKey": "",
    "TimeoutSeconds": 30,
    "MaxRetries": 3,
    "RetryDelaySeconds": 2
  }
}
```

### 2. Service Registration

Register WAHA services in your dependency injection container:

```csharp
using WhatsAppWaha.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register WAHA services with configuration
builder.Services.AddWahaServices(builder.Configuration);

var app = builder.Build();
```

### 3. Basic Usage

Inject and use the WAHA service in your application:

```csharp
using WhatsAppWaha.Core.Interfaces;
using WhatsAppWaha.Core.Models;

public class MessageController : ControllerBase
{
    private readonly IWahaService _wahaService;

    public MessageController(IWahaService wahaService)
    {
        _wahaService = wahaService;
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        var result = await _wahaService.SendTextMessageAsync(
            request.PhoneNumber, 
            request.Message
        );

        if (result.IsSuccess)
        {
            return Ok(new { success = true, messageId = result.Id });
        }

        return BadRequest(new { success = false, error = result.Error });
    }
}
```

## Core Components

### WahaService

The `WahaService` class provides the main interface for WAHA API operations:

```csharp
public interface IWahaService
{
    Task<WahaMessageResult> SendTextMessageAsync(string phoneNumber, string message);
    Task<bool> ValidateSessionAsync();
}
```

#### Key Features

- **Phone Number Validation**: Supports E.164, international, and WAHA chat ID formats
- **Retry Policies**: Exponential backoff with configurable retry attempts
- **Circuit Breaker**: Prevents cascading failures (3 failures → 1 minute break)
- **Comprehensive Logging**: Structured logging with Serilog
- **Error Handling**: Custom exceptions with detailed error information

### Configuration Models

#### WahaSettings

```csharp
public class WahaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:3000";
    public string Session { get; set; } = "default";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 2;
    public int CircuitBreakerFailureThreshold { get; set; } = 3;
    public int CircuitBreakerBreakDurationMinutes { get; set; } = 1;
}
```

### Message Models

#### WahaMessage (Request)

```csharp
public class WahaMessage
{
    [JsonPropertyName("session")]
    public string Session { get; set; }

    [JsonPropertyName("chatId")]
    public string ChatId { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }
}
```

#### WahaMessageResult (Response)

```csharp
public class WahaMessageResult
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("timestamp")]
    public long? Timestamp { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    public bool IsSuccess => !string.IsNullOrEmpty(Id) && string.IsNullOrEmpty(Error);
}
```

## Advanced Usage

### Phone Number Validation

The WAHA service includes comprehensive phone number validation:

```csharp
// Valid formats:
await wahaService.SendTextMessageAsync("+1234567890", "Hello!");        // E.164
await wahaService.SendTextMessageAsync("1234567890@c.us", "Hello!");    // WAHA Chat ID
await wahaService.SendTextMessageAsync("12345678901", "Hello!");        // International

// Invalid formats will throw WahaServiceException
```

### Error Handling

Handle WAHA service errors with custom exceptions:

```csharp
try
{
    var result = await wahaService.SendTextMessageAsync(phoneNumber, message);
    
    if (!result.IsSuccess)
    {
        logger.LogWarning("Message send failed: {Error}", result.Error);
    }
}
catch (WahaServiceException ex)
{
    logger.LogError(ex, "WAHA service error: {Message}", ex.Message);
    // Handle specific WAHA errors
}
catch (HttpRequestException ex)
{
    logger.LogError(ex, "HTTP communication error");
    // Handle network/connectivity issues
}
```

### Session Validation

Validate WAHA session availability before sending messages:

```csharp
public async Task<bool> EnsureSessionReady()
{
    try
    {
        var isValid = await _wahaService.ValidateSessionAsync();
        
        if (!isValid)
        {
            _logger.LogWarning("WAHA session is not valid or not ready");
            return false;
        }

        return true;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to validate WAHA session");
        return false;
    }
}
```

## Resilience and Reliability

### Retry Policies

The SDK implements robust retry policies using Polly:

- **Transient HTTP Errors**: Automatic retry with exponential backoff
- **Timeout Handling**: Configurable request timeouts
- **Circuit Breaker**: Prevents overwhelming failed services

### Configuration Example

```json
{
  "WahaSettings": {
    "BaseUrl": "http://localhost:3000",
    "Session": "default",
    "MaxRetries": 5,
    "RetryDelaySeconds": 1,
    "CircuitBreakerFailureThreshold": 3,
    "CircuitBreakerBreakDurationMinutes": 2
  }
}
```

### Logging

Comprehensive structured logging is provided:

```csharp
// Example log outputs:
[INFO] Sending message to +1234567890 via WAHA session 'default'
[WARN] Retry attempt 2/3 for WAHA request after transient failure
[ERROR] Circuit breaker opened for WAHA service after 3 consecutive failures
```

## Integration Examples

### Console Application

See `WhatsAppWaha.HelloWorld` project for a complete console application example:

```bash
# Send hello world message
WhatsAppWaha.HelloWorld.exe +1234567890

# With custom message
WhatsAppWaha.HelloWorld.exe +1234567890 --message "Custom greeting!"
```

### Background Service

For continuous message processing, see the `WhatsAppWaha.MessageReceiver` project which demonstrates:

- ntfy polling for incoming messages
- Message processing and echo responses
- Background service hosting

## WAHA Server Setup

### Docker Compose

Example WAHA server setup for development:

```yaml
version: '3.8'
services:
  waha:
    image: devlikeapro/waha
    ports:
      - "3000:3000"
    environment:
      - WHATSAPP_HOOK_URL=https://ntfy.sh/your-topic-name
      - WHATSAPP_HOOK_EVENTS=message
    volumes:
      - waha_data:/app/.sessions
```

### Webhook Configuration

Configure WAHA to send webhook events to ntfy for message receiving:

```bash
# Set webhook URL (replace with your ntfy topic)
curl -X POST http://localhost:3000/api/sessions/default/webhook \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://ntfy.sh/your-whatsapp-messages",
    "events": ["message"]
  }'
```

## Testing

The SDK includes comprehensive test coverage:

- **Unit Tests**: 70+ tests covering all components
- **Integration Tests**: End-to-end WAHA communication
- **Resilience Tests**: Retry policies and circuit breaker validation

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/WhatsAppWaha.Core.Tests/

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Troubleshooting

### Common Issues

1. **Connection Refused**: Ensure WAHA service is running and accessible
2. **Session Not Found**: Verify session name matches WAHA configuration
3. **Invalid Phone Number**: Check phone number format (E.164 recommended)
4. **Timeout Errors**: Increase `TimeoutSeconds` in configuration

### Debug Logging

Enable debug logging for detailed troubleshooting:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "WhatsAppWaha.Core": "Debug"
      }
    }
  }
}
```

## Performance Considerations

- **Connection Pooling**: HTTP client is registered as singleton for connection reuse
- **Async/Await**: All operations are fully asynchronous
- **Memory Management**: Efficient JSON serialization with System.Text.Json
- **Rate Limiting**: Consider WAHA's rate limits for high-volume scenarios

## Future Enhancements

The SDK is designed for extensibility:

- **Media Message Support**: Images, documents, voice messages
- **Group Chat Operations**: Group creation and management
- **Webhook Processing**: Built-in webhook endpoint handling
- **AI Integration**: Ready for medical assistant chatbot integration

## Support

For issues and feature requests:

- **Project Repository**: WhatsApp_WAHA_Project_2025
- **WAHA Documentation**: https://waha.devlike.pro/docs/
- **Community Support**: GitHub Issues

## License

This SDK is part of the WhatsApp WAHA Messaging Framework project and follows the project's licensing terms.