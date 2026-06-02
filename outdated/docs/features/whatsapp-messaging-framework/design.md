# Design Document: WhatsApp Messaging Framework

## Overview

This design document outlines the integration of WhatsApp messaging capabilities into an existing .NET Core medical assistant application using WAHA (WhatsApp HTTP API) as a microservice architecture.

## Architecture Design

### System Components

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                           .NET Core Medical Assistant                            │
├─────────────────────────────────────────────────────────────────────────────────┤
│  ┌─────────────────────┐  ┌─────────────────────┐  ┌─────────────────────────┐  │
│  │   AI Medical Core   │  │  WhatsApp Service   │  │  Webhook Controller     │  │
│  │                     │  │                     │  │                         │  │
│  │ - Diagnosis Logic   │  │ - Send Messages     │  │ - Receive Messages      │  │
│  │ - Treatment Plans   │  │ - Send Media        │  │ - Process Webhooks      │  │
│  │ - Patient Records   │  │ - Token Management  │  │ - Route to AI Core      │  │
│  └─────────────────────┘  └─────────────────────┘  └─────────────────────────┘  │
│              │                         │                         │               │
│              └─────────────────────────┼─────────────────────────┘               │
│                                        │                                         │
└────────────────────────────────────────┼─────────────────────────────────────────┘
                                         │ HTTP/REST API
                              ┌──────────▼──────────┐
                              │    WAHA Server      │
                              │ (Docker Container)  │
                              │                     │
                              │ - Session Mgmt      │
                              │ - WhatsApp Web      │
                              │ - Media Handling    │
                              │ - Webhook Events    │
                              └──────────┬──────────┘
                                         │ WebSocket/HTTP
                              ┌──────────▼──────────┐
                              │    WhatsApp Web     │
                              │                     │
                              │ - User Interface    │
                              │ - Message Delivery  │
                              │ - Media Exchange    │
                              └─────────────────────┘
```

### Data Flow Design

#### Outgoing Messages (Medical Assistant → User)
1. **AI Core** generates response/recommendation
2. **WhatsApp Service** formats message for WAHA Server
3. **HTTP POST** to WAHA Server API endpoint
4. **WAHA Server** sends via WhatsApp Web
5. **Message logged** to conversation database

#### Incoming Messages (User → Medical Assistant)
1. **User** sends message via WhatsApp
2. **WAHA Server** receives and processes
3. **Webhook** sent to .NET Core endpoint
4. **Webhook Controller** logs and routes message
5. **AI Core** processes and generates response
6. **Response sent** back through WhatsApp Service

## Component Design

### WhatsApp Service Layer

```csharp
public interface IWhatsAppService
{
    // Core messaging
    Task<MessageResult> SendTextAsync(string phoneNumber, string message);
    Task<MessageResult> SendImageAsync(string phoneNumber, byte[] imageData, string caption);
    Task<MessageResult> SendDocumentAsync(string phoneNumber, byte[] documentData, string filename);
    
    // Session management
    Task<bool> StartSessionAsync(string sessionName);
    Task<bool> IsSessionActiveAsync(string sessionName);
    Task<string> GetQrCodeAsync(string sessionName);
    
    // Token management
    Task<string> GenerateTokenAsync(string sessionName);
    Task<bool> ValidateTokenAsync(string token);
}
```

### Conversation Repository

```csharp
public interface IConversationRepository
{
    // Message logging
    Task<int> LogMessageAsync(ConversationMessage message);
    Task UpdateDeliveryStatusAsync(string messageId, DeliveryStatus status);
    
    // Conversation history
    Task<List<ConversationMessage>> GetConversationAsync(string chatId, int limit = 50);
    Task<List<ConversationMessage>> GetRecentConversationsAsync(TimeSpan timeSpan);
    
    // Search and analytics
    Task<List<ConversationMessage>> SearchMessagesAsync(string query, SearchFilters filters);
    Task<ConversationStats> GetConversationStatsAsync(string chatId);
}
```

### Webhook Processing

```csharp
public interface IWhatsAppMessageProcessor
{
    Task ProcessIncomingMessageAsync(WhatsAppWebhookEvent webhookEvent);
    Task ProcessMessageStatusAsync(MessageStatusEvent statusEvent);
    Task ProcessSessionStatusAsync(SessionStatusEvent sessionEvent);
}
```

## Database Design

### Entity Models

```csharp
public class ConversationMessage
{
    public int Id { get; set; }
    public string ChatId { get; set; }
    public string MessageId { get; set; }
    public DateTime Timestamp { get; set; }
    public SenderType SenderType { get; set; }
    public string SenderId { get; set; }
    public string SenderName { get; set; }
    public MessageType MessageType { get; set; }
    public string Content { get; set; }
    public string MediaPath { get; set; }
    public string MediaFilename { get; set; }
    public string Caption { get; set; }
    public string Metadata { get; set; }
    public DeliveryStatus DeliveryStatus { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public virtual ChatSession ChatSession { get; set; }
}

public class ChatSession
{
    public int Id { get; set; }
    public string ChatId { get; set; }
    public string ContactName { get; set; }
    public string ContactPhone { get; set; }
    public DateTime FirstMessageAt { get; set; }
    public DateTime LastMessageAt { get; set; }
    public int TotalMessages { get; set; }
    public SessionStatus Status { get; set; }
    public string PatientId { get; set; } // Link to medical records
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual List<ConversationMessage> Messages { get; set; }
}
```

### Database Schema Optimizations

```sql
-- Indexes for performance
CREATE INDEX IX_ConversationMessages_ChatId_Timestamp 
ON ConversationMessages (ChatId, Timestamp);

CREATE INDEX IX_ConversationMessages_SenderType 
ON ConversationMessages (SenderType);

CREATE INDEX IX_ConversationMessages_MessageType 
ON ConversationMessages (MessageType);

CREATE INDEX IX_ConversationMessages_DeliveryStatus 
ON ConversationMessages (DeliveryStatus);

CREATE INDEX IX_ChatSessions_ContactPhone 
ON ChatSessions (ContactPhone);

CREATE INDEX IX_ChatSessions_LastMessageAt 
ON ChatSessions (LastMessageAt DESC);

-- Full-text search (SQL Server)
CREATE FULLTEXT INDEX ON ConversationMessages (Content) 
KEY INDEX PK_ConversationMessages;
```

## Integration Design

### Configuration Management

```csharp
public class WhatsAppOptions
{
    public string ServerUrl { get; set; } = "http://localhost:21465/api";
    public string SessionName { get; set; } = "medical-assistant";
    public string SecretKey { get; set; }
    public string WebhookToken { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    public string MediaStoragePath { get; set; } = "./storage/media";
}
```

### Dependency Injection Setup

```csharp
// Program.cs
builder.Services.Configure<WhatsAppOptions>(
    builder.Configuration.GetSection("WhatsApp"));

builder.Services.AddHttpClient<IWhatsAppService, WhatsAppService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<WhatsAppOptions>>().Value;
    client.BaseAddress = new Uri(options.ServerUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IWhatsAppMessageProcessor, WhatsAppMessageProcessor>();
```

## Error Handling Design

### Retry Policy

```csharp
public class WhatsAppRetryPolicy
{
    public static readonly RetryPolicy Policy = Policy
        .Handle<HttpRequestException>()
        .Or<TaskCanceledException>()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => 
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
            onRetry: (outcome, timespan, retryCount, context) =>
            {
                var logger = context.GetLogger();
                logger.LogWarning($"Retry {retryCount} for WhatsApp operation after {timespan}s");
            });
}
```

### Circuit Breaker

```csharp
public class WhatsAppCircuitBreaker
{
    public static readonly CircuitBreakerPolicy Policy = Policy
        .Handle<HttpRequestException>()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromMinutes(1),
            onBreak: (exception, timespan) =>
            {
                // Log circuit breaker opened
            },
            onReset: () =>
            {
                // Log circuit breaker closed
            });
}
```

## Security Design

### Authentication Flow

1. **Generate Token**: Use secret key to generate JWT token from WPPConnect Server
2. **Store Token**: Cache token with expiration tracking
3. **Validate Requests**: Include token in all API calls
4. **Refresh Token**: Automatically refresh before expiration

### Webhook Security

```csharp
public class WebhookAuthenticationMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Path.StartsWithSegments("/api/webhook"))
        {
            var token = context.Request.Headers["Authorization"]
                .FirstOrDefault()?.Split(" ").Last();
                
            if (!await ValidateWebhookToken(token))
            {
                context.Response.StatusCode = 401;
                return;
            }
        }
        
        await next(context);
    }
}
```

## Testing Strategy

### Unit Testing Approach

```csharp
public class WhatsAppServiceTests
{
    [Test]
    public async Task SendTextAsync_ValidInput_ReturnsSuccess()
    {
        // Arrange
        var mockHttpClient = CreateMockHttpClient();
        var service = new WhatsAppService(mockHttpClient, mockLogger, mockOptions);
        
        // Act
        var result = await service.SendTextAsync("+1234567890", "Test message");
        
        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.MessageId);
    }
}
```

### Integration Testing

```csharp
[TestFixture]
public class WhatsAppIntegrationTests
{
    private TestServer _server;
    private HttpClient _client;
    
    [SetUp]
    public void Setup()
    {
        _server = new TestServer(new WebHostBuilder()
            .UseStartup<TestStartup>());
        _client = _server.CreateClient();
    }
    
    [Test]
    public async Task WebhookEndpoint_ValidPayload_ProcessesSuccessfully()
    {
        // Test webhook processing end-to-end
    }
}
```

## Deployment Design

### Docker Configuration

```dockerfile
# WPPConnect Server Container
FROM node:18-alpine
WORKDIR /app
COPY wppconnect-server/ .
RUN npm install
EXPOSE 21465
CMD ["npm", "start"]
```

```dockerfile
# .NET Core Application
FROM mcr.microsoft.com/dotnet/aspnet:7.0
WORKDIR /app
COPY published/ .
EXPOSE 80
ENTRYPOINT ["dotnet", "MedicalAssistant.dll"]
```

### Docker Compose

```yaml
version: '3.8'
services:
  medical-assistant:
    build: ./src
    ports:
      - "5000:80"
    depends_on:
      - wppconnect-server
      - database
    environment:
      - WhatsApp__ServerUrl=http://wppconnect-server:21465/api
      
  wppconnect-server:
    build: ./infrastructure/wppconnect-server
    ports:
      - "21465:21465"
    volumes:
      - wpp_sessions:/app/sessions
      
  database:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourPassword123!
    volumes:
      - db_data:/var/opt/mssql

volumes:
  wpp_sessions:
  db_data:
```

## Performance Considerations

### Caching Strategy

- **Token Caching**: Cache authentication tokens with Redis
- **Session Status**: Cache active session information
- **Recent Messages**: Cache recent conversation history

### Rate Limiting

```csharp
services.AddRateLimiter(options =>
{
    options.AddPolicy("WhatsApp", context =>
        RateLimitPartition.CreateFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 60
            }));
});
```

## Monitoring and Observability

### Logging Strategy

```csharp
public static class LogEvents
{
    public static readonly EventId MessageSent = new(1001, "WhatsApp Message Sent");
    public static readonly EventId MessageReceived = new(1002, "WhatsApp Message Received");
    public static readonly EventId SessionConnected = new(1003, "WhatsApp Session Connected");
    public static readonly EventId SessionDisconnected = new(1004, "WhatsApp Session Disconnected");
    public static readonly EventId ApiError = new(2001, "WhatsApp API Error");
}
```

### Health Checks

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<WhatsAppHealthCheck>("whatsapp-server")
    .AddDbContextCheck<MedicalAssistantDbContext>("database");
```

This design provides a robust, scalable, and maintainable integration between your .NET Core medical assistant and WhatsApp messaging capabilities through WPPConnect Server.
