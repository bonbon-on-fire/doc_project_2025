# Design Document: WhatsApp WAHA Messaging Framework

## High-Level Overview

This design document outlines the implementation approach for a .NET Core 9.0 messaging framework that integrates with WAHA (WhatsApp HTTP API) and ntfy for building an AI-powered medical assistant chatbot foundation.

## Design Philosophy

### Surgical Changes vs Complete Rewrite
**Decision**: Complete new implementation - **Starting from scratch**

**Rationale**:
- Empty workspace provides clean slate for modern .NET Core 9.0 patterns
- No legacy code constraints
- Can implement best practices from the beginning
- Allows for optimal architecture design for future AI integration

### Design Principles
1. **Modularity**: Clear separation between WAHA integration, message processing, and notifications
2. **Extensibility**: Architecture supports future AI medical assistant features
3. **Testability**: Dependency injection and interface-based design
4. **Resilience**: Robust error handling and retry mechanisms
5. **Observability**: Comprehensive logging and monitoring through ntfy

## Architecture Design

### System Architecture (Revised - ntfy as Message Relay)
```
┌─────────────────────────────────────────────────────────────────┐
│                    WhatsApp WAHA Messaging Framework            │
├─────────────────────────────────────────────────────────────────┤
│  Phase 1: Console App              │  Phase 2: Message Receiver │
│  ┌─────────────────────────────────┐│  ┌─────────────────────────│
│  │ HelloWorldSender                ││  │ MessageReceivingService │
│  │ ├─ ArgumentParser               ││  │ ├─ Console/Service Host │
│  │ ├─ WahaService                  ││  │ ├─ NtfyPollingService   │
│  │ └─ ConsoleLogger                ││  │ ├─ MessageProcessor     │
│  └─────────────────────────────────┘│  │ ├─ EchoResponseHandler  │
│                                     ││  │ └─ PollingLoop         │
│                                     ││  └─────────────────────────│
├─────────────────────────────────────────────────────────────────┤
│                        Shared Services                          │
│  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────────┐ │
│  │   WahaService   │ │  NtfyService    │ │ ConfigurationManager│ │
│  │ ├─ SendMessage  │ │ ├─ PollMessages │ │ ├─ WahaSettings     │ │
│  │ ├─ ValidateNum  │ │ ├─ SendNotify   │ │ ├─ NtfySettings     │ │
│  │ └─ HandleError  │ │ └─ Deduplication│ │ └─ PollingSettings  │ │
│  └─────────────────┘ └─────────────────┘ └─────────────────────┘ │
├─────────────────────────────────────────────────────────────────┤
│                      External Integrations                      │
│  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────────┐ │
│  │      WAHA       │ │      ntfy       │ │    Configuration    │ │
│  │   (Docker)      │ │   (Message      │ │   (appsettings)     │ │
│  │ ├─ /api/sendText│ │    Relay)       │ │ ├─ Environment Vars │ │
│  │ ├─ Webhook →    │ │ ├─ Webhook RX   │ │ └─ JSON Files       │ │
│  │ └─ Session Mgmt │ │ ├─ HTTP Polling │ │                     │ │
│  └─────────────────┘ │ └─ JSON API     │ │                     │ │
                        └─────────────────┘ └─────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘

Message Flow:
WhatsApp → WAHA → ntfy (webhook) → .NET App (polling) → WAHA (response)
```

### Project Structure
```
WhatsAppWahaFramework/
├── src/
│   ├── WhatsAppWaha.Core/              # Shared library
│   │   ├── Services/
│   │   │   ├── IWahaService.cs
│   │   │   ├── WahaService.cs
│   │   │   ├── INtfyService.cs
│   │   │   └── NtfyService.cs
│   │   ├── Models/
│   │   │   ├── WahaMessage.cs
│   │   │   ├── WebhookPayload.cs
│   │   │   └── NtfyNotification.cs
│   │   ├── Configuration/
│   │   │   ├── WahaSettings.cs
│   │   │   ├── NtfySettings.cs
│   │   │   └── AppSettings.cs
│   │   └── Extensions/
│   │       └── ServiceCollectionExtensions.cs
│   │
│   ├── WhatsAppWaha.HelloWorld/        # Phase 1 Console App
│   │   ├── Program.cs
│   │   ├── Services/
│   │   │   └── HelloWorldService.cs
│   │   ├── appsettings.json
│   │   └── WhatsAppWaha.HelloWorld.csproj
│   │
│   └── WhatsAppWaha.MessageReceiver/   # Phase 2 Message Receiving Service
│       ├── Program.cs
│       ├── Services/
│       │   ├── NtfyPollingService.cs
│       │   ├── MessageProcessorService.cs
│       │   └── EchoResponseService.cs
│       ├── BackgroundServices/
│       │   └── MessagePollingBackgroundService.cs
│       ├── appsettings.json
│       └── WhatsAppWaha.MessageReceiver.csproj
│
├── tests/
│   ├── WhatsAppWaha.Core.Tests/
│   ├── WhatsAppWaha.HelloWorld.Tests/
│   └── WhatsAppWaha.WebhookReceiver.Tests/
│
├── docs/
│   └── setup/
│       ├── waha-setup.md
│       └── deployment.md
│
├── docker/
│   ├── docker-compose.yml            # WAHA + Framework services
│   └── Dockerfile.webhook
│
└── README.md
```

## Detailed Component Design

### 1. WAHA Service (Core Integration - USING WAHA SDK) 🔧 FIXED
```csharp
public interface IWahaService
{
    Task<WahaMessageResult> SendTextMessageAsync(string phoneNumber, string message, SendTextOptions? options = null, CancellationToken cancellationToken = default);
    Task<bool> ValidateSessionAsync(CancellationToken cancellationToken = default);
    bool ValidatePhoneNumber(string phoneNumber);
}

public class WahaService : IWahaService
{
    private readonly IWahaApiClient _wahaClient; // ✅ Using WAHA SDK Client
    private readonly WahaSettings _settings;
    private readonly ILogger<WahaService> _logger;
    
    public WahaService(IWahaApiClient wahaClient, ILogger<WahaService> logger, IOptions<WahaSettings> settings)
    {
        _wahaClient = wahaClient; // ✅ Inject WAHA SDK client
        _settings = settings.Value;
        _logger = logger;
    }
    
    // Implementation using WAHA SDK methods instead of raw HTTP calls
}
```

**Design Decisions (🔧 CORRECTED)**:
- **WAHA SDK Client**: Using official WAHA NuGet package's IWahaApiClient instead of raw HttpClient
- **Proper Integration**: Leveraging SDK's built-in retry policies, authentication, and error handling
- **Type Safety**: Using SDK's strongly-typed methods and models
- **Error Handling**: Custom exceptions with meaningful messages on top of SDK's error handling

### 2. ntfy Service (Message Relay and Notifications)
```csharp
public interface INtfyService
{
    // Polling functionality for message retrieval
    Task<IEnumerable<NtfyMessage>> PollMessagesAsync(string topicName, CancellationToken cancellationToken = default);
    Task<bool> HasNewMessagesAsync(string topicName, DateTime since);
    
    // Notification functionality
    Task SendNotificationAsync(string message, string title = null, NtfyPriority priority = NtfyPriority.Default);
    Task LogMessageProcessedAsync(string contact, string message);
    Task LogErrorAsync(string error, Exception exception = null);
}

public class NtfyService : INtfyService
{
    private readonly HttpClient _httpClient;
    private readonly NtfySettings _settings;
    private readonly ILogger<NtfyService> _logger;
    private readonly HashSet<string> _processedMessageIds = new();
    
    // Polling implementation for message retrieval
    // Fire-and-forget implementation for notifications
    // Message deduplication logic
}
```

**Design Decisions**:
- **Dual Purpose**: Both message polling and notifications
- **Message Deduplication**: Track processed message IDs to avoid duplicates
- **Non-blocking Notifications**: Notifications don't affect core message processing
- **Error Resilience**: Graceful handling of ntfy unavailability

### 3. Configuration Management
```csharp
public class WahaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:3000";
    public string SessionName { get; set; } = "default";
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryAttempts { get; set; } = 3;
}

public class NtfySettings
{
    public string TopicUrl { get; set; }
    public string MessageTopicName { get; set; } = "whatsapp-messages";
    public string NotificationTopicName { get; set; } = "whatsapp-notifications";
    public string AuthToken { get; set; }
    public bool EnableNotifications { get; set; } = true;
    public int PollingIntervalSeconds { get; set; } = 3;
    public int MaxMessagesPerPoll { get; set; } = 50;
}
```

**Design Decisions**:
- **Environment Override**: Support for environment-specific settings
- **Validation**: Built-in validation attributes
- **Defaults**: Sensible default values for local development

### 4. Phase 1: Hello World Console Application
```csharp
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Modern .NET Core 9.0 hosting pattern
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services => services.AddWhatsAppWahaServices())
            .Build();
            
        var helloWorldService = host.Services.GetRequiredService<HelloWorldService>();
        return await helloWorldService.ExecuteAsync(args);
    }
}

public class HelloWorldService
{
    // Command-line argument parsing
    // WAHA service integration
    // Success/error handling with exit codes
}
```

**Design Decisions**:
- **Command Pattern**: Clear separation of concerns
- **Exit Codes**: Proper console application exit codes
- **Argument Validation**: Robust command-line parsing
- **Dependency Injection**: Full DI container even for console app

### 5. Phase 2: Message Receiving Service
```csharp
public class MessagePollingBackgroundService : BackgroundService
{
    private readonly INtfyService _ntfyService;
    private readonly IMessageProcessorService _messageProcessor;
    private readonly NtfySettings _settings;
    private readonly ILogger<MessagePollingBackgroundService> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await _ntfyService.PollMessagesAsync(_settings.MessageTopicName, stoppingToken);
                foreach (var message in messages)
                {
                    await _messageProcessor.ProcessMessageAsync(message);
                }
                
                await Task.Delay(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during message polling");
            }
        }
    }
}

public class NtfyPollingService
{
    // Handles HTTP GET to https://ntfy.sh/{topic}/json
    // Parses ntfy response format into WAHA webhook payload
    // Implements message deduplication
}
```

**Design Decisions**:
- **Background Service**: Continuous polling using .NET hosted service
- **Configurable Polling**: Adjustable interval based on requirements
- **Message Deduplication**: Avoid processing same message multiple times
- **Error Resilience**: Continue polling even if individual messages fail

### 6. Message Processing Pipeline
```csharp
public interface IMessageProcessor
{
    Task ProcessMessageAsync(WhatsAppMessage message);
}

public class EchoMessageProcessor : IMessageProcessor
{
    public async Task ProcessMessageAsync(WhatsAppMessage message)
    {
        // 1. Extract contact information
        // 2. Format echo response: "[contact] said [message]"
        // 3. Send response via WAHA
        // 4. Send notification via ntfy
        // 5. Log success/failure
    }
}
```

**Design Decisions**:
- **Strategy Pattern**: Easy to extend with AI processors later
- **Async Pipeline**: Full async processing chain
- **Error Isolation**: Failures in one message don't affect others
- **Extensibility**: Interface-based design for future AI integration

## Data Models

### WAHA Integration Models
```csharp
public class WahaMessage
{
    public string ChatId { get; set; }      // Phone number with @c.us suffix
    public string Text { get; set; }
    public string Session { get; set; } = "default";
}

public class WebhookPayload
{
    public string Event { get; set; }
    public string Session { get; set; }
    public MessageData Data { get; set; }
}

public class MessageData
{
    public string Id { get; set; }
    public string From { get; set; }
    public string FromMe { get; set; }
    public string Body { get; set; }
    public string Type { get; set; }
    public long Timestamp { get; set; }
    public ContactInfo Contact { get; set; }
}
```

### ntfy Integration Models
```csharp
public class NtfyMessage
{
    public string Id { get; set; }
    public long Time { get; set; }
    public string Message { get; set; }
    public string Title { get; set; }
    public int Priority { get; set; }
    public string[] Tags { get; set; }
    
    // For WAHA webhook payloads received via ntfy
    public WebhookPayload ParsedWebhookPayload { get; set; }
}

public class NtfyPollingResponse
{
    public NtfyMessage[] Messages { get; set; }
    public bool HasMore { get; set; }
    public string NextId { get; set; }
}

public class MessageProcessingContext
{
    public string MessageId { get; set; }
    public DateTime ProcessedAt { get; set; }
    public string SourceTopic { get; set; }
    public WebhookPayload OriginalPayload { get; set; }
}
```

### Configuration Models
```csharp
public class AppSettings
{
    public WahaSettings Waha { get; set; } = new();
    public NtfySettings Ntfy { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public WebhookSettings Webhook { get; set; } = new();
}
```

## Technology Implementation Details

### .NET Core 9.0 Patterns
- **Minimal APIs**: For webhook endpoints (optional alternative to controllers)
- **Top-level statements**: Modern Program.cs structure
- **Global using statements**: Clean namespace management
- **Source generators**: For configuration binding
- **Native AOT ready**: Future performance optimization

### WAHA SDK Client Configuration (🔧 CORRECTED)
```csharp
// ✅ CORRECT: Using WAHA SDK registration
services.AddWahaApiClient("Waha"); // Registers IWahaApiClient

// ✅ Configuration section for WAHA SDK
services.Configure<WahaSettings>(configuration.GetSection("Waha"));

// ✅ Register our wrapper service
services.AddScoped<IWahaService, WahaService>();

// ❌ OLD WRONG WAY (DO NOT USE):
// services.AddHttpClient<IWahaService, WahaService>(...)
```

### Logging Strategy
- **Structured Logging**: Using Serilog with JSON formatting
- **Correlation IDs**: Track requests across services
- **Log Levels**: Appropriate levels for different scenarios
- **External Sinks**: File, Console, and optional external services

### Error Handling Strategy
- **Custom Exceptions**: Domain-specific exception types
- **Global Exception Handler**: Centralized error processing
- **Circuit Breaker**: Protect against cascade failures
- **Graceful Degradation**: Continue operating when ntfy is down

## Security Considerations

### Configuration Security
- **Environment Variables**: Sensitive data not in config files
- **Secret Management**: Support for Azure Key Vault, AWS Secrets Manager
- **Validation**: Input validation at all entry points

### Communication Security
- **HTTPS**: All external communications over HTTPS
- **API Keys**: Secure storage and rotation support
- **Rate Limiting**: Protect webhook endpoints

## Deployment Strategy

### Development Environment
- **Docker Compose**: WAHA + Framework services
- **Local Configuration**: File-based settings
- **Hot Reload**: Development experience optimization

### Production Environment
- **Container Images**: Multi-stage Docker builds
- **Health Checks**: Kubernetes/Docker health endpoints
- **Configuration**: Environment-based overrides
- **Monitoring**: Integration with external monitoring systems

## Testing Strategy

### Unit Tests
- **Service Layer**: Mock external dependencies
- **Business Logic**: Comprehensive test coverage
- **Configuration**: Validation testing

### Integration Tests
- **WAHA Integration**: Against test WAHA instance
- **ntfy Integration**: Mock external service
- **End-to-End**: Full workflow testing

### Performance Tests
- **Webhook Throughput**: Concurrent message handling
- **Memory Usage**: Memory leak detection
- **Response Times**: API performance benchmarks

## Migration and Extensibility

### Future AI Integration Points
1. **Message Processor Interface**: Easy to add AI message handlers
2. **Pipeline Architecture**: Support for multi-stage processing
3. **Session Management**: Multi-user conversation tracking
4. **Context Storage**: Database integration for conversation history

### Scaling Considerations
- **Horizontal Scaling**: Stateless service design
- **Load Balancing**: Multiple webhook receiver instances
- **Message Queuing**: For high-volume scenarios
- **Database Integration**: When persistence is needed

This design provides a solid foundation that meets immediate requirements while being architected for future expansion into a comprehensive AI medical assistant platform.
