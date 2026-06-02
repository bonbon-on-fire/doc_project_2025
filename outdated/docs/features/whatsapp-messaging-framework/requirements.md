# Feature Specification: WhatsApp Messaging Framework

## High-Level Overview

A raw and functional messaging integration service that connects a .NET Core application with WhatsApp using WPPConnect Server. This service serves as a foundational component for an AI-powered medical assistant, providing a bridge between the main .NET Core application and WhatsApp messaging capabilities. All conversations are logged chronologically for future AI integration.

## High-Level Requirements

- **Primary Purpose**: Integration service for .NET Core AI-powered medical assistant that communicates via WhatsApp
- **Messaging Scope**: Send/receive text messages, images, and documents through WPPConnect Server (not Cloud API)
- **Architecture**: Microservice architecture with WPPConnect Server + .NET Core integration service
- **Environment**: Local machine development with WPPConnect Server, future-ready for production scaling
- **Integration**: Seamless integration with existing .NET Core application and future AI system components

## Existing Solutions

- **WPPConnect Server**: Ready-to-use REST API server for WhatsApp Web interface (chosen solution)
- **WPPConnect Library**: Node.js library for direct integration (not used - using server instead)
- **WhatsApp Cloud API**: Official but not preferred for this project
- **Baileys**: Alternative WhatsApp Web API library
- **.NET Core HttpClient**: For REST API communication with WPPConnect Server

## Current Implementation

- **Status**: Starting from scratch - empty workspace
- **Previous Work**: Evidence of deleted documentation files (design.md, requirements.md, tasks.md)
- **Technology Stack**: .NET Core (main application) + WPPConnect Server (Node.js microservice)
- **Dependencies**: .NET Core project exists, WPPConnect Server needs to be set up

## Detailed Requirements

### Requirement 1: Message Sending Capabilities

- **User Story**: As a framework user, I need to send different types of messages to WhatsApp users so that the AI medical assistant can communicate effectively.

#### Acceptance Criteria 1

1. ✅ **Text Messages**: WHEN sending a text message THEN the framework SHALL deliver it successfully to the specified WhatsApp contact
2. ✅ **Images**: WHEN sending an image file THEN the framework SHALL deliver it with optional caption to the specified WhatsApp contact
3. ✅ **Documents**: WHEN sending a document file THEN the framework SHALL deliver it with filename and optional caption to the specified WhatsApp contact
4. ✅ **Error Handling**: WHEN a message fails to send THEN the framework SHALL log the error and provide meaningful feedback

### Requirement 2: Message Receiving Capabilities

- **User Story**: As a framework user, I need to receive and process incoming messages from WhatsApp users so that the AI medical assistant can respond appropriately.

#### Acceptance Criteria 2

1. ✅ **Text Messages**: WHEN a user sends a text message THEN the framework SHALL capture and log the message content
2. ✅ **Images**: WHEN a user sends an image THEN the framework SHALL receive, save, and log the image with metadata
3. ✅ **Real-time Processing**: WHEN messages arrive THEN the framework SHALL process them immediately via event listeners
4. ✅ **Message Metadata**: WHEN any message is received THEN the framework SHALL capture sender ID, timestamp, and message type

### Requirement 3: Conversation Logging and Storage

- **User Story**: As a framework user, I need all conversations stored chronologically so that the AI medical assistant can maintain conversation context and history.

#### Acceptance Criteria 3

1. ✅ **Chronological Storage**: WHEN messages are sent or received THEN they SHALL be stored in timestamp order
2. ✅ **Complete Conversation History**: WHEN storing messages THEN both user and bot messages SHALL be logged with full context
3. ✅ **Database Schema**: WHEN storing data THEN it SHALL use a structured schema that supports future AI integration
4. ✅ **Query Capability**: WHEN retrieving conversation history THEN the framework SHALL support efficient querying by chat ID and timeframe

### Requirement 4: Future AI Integration Readiness

- **User Story**: As a developer integrating AI, I need the framework to provide clean data access so that AI systems can process conversation history and respond to messages.

#### Acceptance Criteria 4

1. ✅ **Structured Data Access**: WHEN AI systems query conversation data THEN they SHALL receive properly formatted message history
2. ✅ **Extensible Architecture**: WHEN adding AI components THEN the framework SHALL support integration without major refactoring
3. ✅ **Message Queue Ready**: WHEN implementing real-time AI responses THEN the framework SHALL support event-driven architecture
4. ✅ **Data Export**: WHEN migrating or analyzing data THEN the framework SHALL provide clean export capabilities

## Database Schema Design

### Recommended Database: SQLite (Current Phase)

**Rationale**: Perfect for local development, zero configuration, easy migration to PostgreSQL later.

### Core Schema

```sql
-- Main conversations table
CREATE TABLE conversations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    chat_id TEXT NOT NULL,                    -- WhatsApp chat identifier
    message_id TEXT UNIQUE,                   -- WPPConnect message ID
    timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
    sender_type TEXT NOT NULL CHECK (sender_type IN ('user', 'bot')),
    sender_id TEXT,                           -- Phone number or contact ID
    sender_name TEXT,                         -- Contact display name
    message_type TEXT NOT NULL CHECK (message_type IN ('text', 'image', 'document')),
    content TEXT,                             -- Message text content
    media_path TEXT,                          -- Local path to saved media files
    media_filename TEXT,                      -- Original filename for documents
    caption TEXT,                             -- Image/document caption
    metadata TEXT,                            -- JSON string for additional data
    delivery_status TEXT DEFAULT 'sent',     -- For outgoing messages
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Chat sessions for grouping conversations
CREATE TABLE chat_sessions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    chat_id TEXT UNIQUE NOT NULL,
    contact_name TEXT,
    contact_phone TEXT,
    first_message_at DATETIME,
    last_message_at DATETIME,
    total_messages INTEGER DEFAULT 0,
    session_status TEXT DEFAULT 'active',    -- active, closed, archived
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Media files tracking
CREATE TABLE media_files (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    conversation_id INTEGER,
    original_filename TEXT,
    stored_filename TEXT,
    file_path TEXT NOT NULL,
    file_size INTEGER,
    mime_type TEXT,
    upload_timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (conversation_id) REFERENCES conversations(id)
);

-- Indexes for performance
CREATE INDEX idx_conversations_chat_timestamp ON conversations(chat_id, timestamp);
CREATE INDEX idx_conversations_sender_type ON conversations(sender_type);
CREATE INDEX idx_conversations_message_type ON conversations(message_type);
CREATE INDEX idx_chat_sessions_phone ON chat_sessions(contact_phone);
CREATE INDEX idx_media_files_conversation ON media_files(conversation_id);
```

### Future PostgreSQL Migration Schema

```sql
-- Enhanced schema for production use
CREATE TABLE conversations (
    id SERIAL PRIMARY KEY,
    chat_id VARCHAR(255) NOT NULL,
    message_id VARCHAR(255) UNIQUE,
    timestamp TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    sender_type VARCHAR(10) NOT NULL CHECK (sender_type IN ('user', 'bot')),
    sender_id VARCHAR(255),
    sender_name VARCHAR(255),
    message_type VARCHAR(20) NOT NULL CHECK (message_type IN ('text', 'image', 'document')),
    content TEXT,
    media_path TEXT,
    media_filename TEXT,
    caption TEXT,
    metadata JSONB,                           -- Native JSON support
    delivery_status VARCHAR(20) DEFAULT 'sent',
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

-- Full-text search capability
CREATE INDEX idx_conversations_content_fts ON conversations USING gin(to_tsvector('english', content));
```

## Architecture Overview

### System Architecture

```
┌─────────────────────┐    HTTP/REST API    ┌──────────────────────┐    WebSocket/HTTP    ┌─────────────────────┐
│                     │ ◄──────────────────► │                      │ ◄──────────────────► │                     │
│   .NET Core App     │                      │  WPPConnect Server   │                      │    WhatsApp Web     │
│  (Main Application) │                      │   (Node.js Service)  │                      │                     │
│                     │                      │                      │                      │                     │
└─────────────────────┘                      └──────────────────────┘                      └─────────────────────┘
          │                                             │
          │                                             │
          ▼                                             ▼
┌─────────────────────┐                      ┌──────────────────────┐
│                     │                      │                      │
│  SQL Server/SQLite  │                      │   Session Storage    │
│  (Conversation DB)  │                      │   (WPPConnect Data)  │
│                     │                      │                      │
└─────────────────────┘                      └──────────────────────┘
```

### Project Structure Recommendations

```
YourDotNetProject/                         # Main .NET Core application
├── src/
│   ├── YourApp.Api/                       # Main API project
│   ├── YourApp.Core/                      # Business logic
│   ├── YourApp.Infrastructure/            # Data access
│   └── YourApp.Services/
│       └── WhatsApp/                      # WhatsApp integration service
│           ├── IWhatsAppService.cs
│           ├── WhatsAppService.cs
│           ├── Models/
│           │   ├── WhatsAppMessage.cs
│           │   ├── SendMessageRequest.cs
│           │   └── WebhookEvent.cs
│           └── Configuration/
│               └── WhatsAppOptions.cs
├── infrastructure/
│   └── wppconnect-server/                 # WPPConnect Server instance
│       ├── Dockerfile                     # Container for WPPConnect Server
│       ├── docker-compose.yml             # Orchestration
│       └── config/
│           └── production.ts              # WPPConnect Server config
└── database/
    ├── migrations/                        # EF Core migrations
    └── seed-data/                         # Initial data
```

### Key Components and Their Purposes

#### WPPConnect Server Setup

Install and run WPPConnect Server as a separate service:

```bash
# Clone and setup WPPConnect Server
git clone https://github.com/wppconnect-team/wppconnect-server.git
cd wppconnect-server
npm install

# Configure in config.ts
export const config = {
  secretKey: 'your-secret-key',
  host: 'http://localhost',
  port: '21465',
  deviceName: 'Medical-Assistant',
  poweredBy: 'YourApp',
  startAllSession: true,
  tokenStoreType: 'file',
  webhook: {
    url: 'http://localhost:5000/api/webhook/whatsapp', // Your .NET Core webhook
    autoDownload: true,
    readMessage: false,
    allUnreadOnStart: true
  }
};

# Start server
npm run dev
```

#### .NET Core WhatsApp Service

```csharp
using System.Text.Json;

public interface IWhatsAppService
{
    Task<bool> SendTextMessageAsync(string phoneNumber, string message);
    Task<bool> SendImageAsync(string phoneNumber, string imagePath, string caption = "");
    Task<bool> SendDocumentAsync(string phoneNumber, string documentPath, string caption = "");
    Task<string> GenerateTokenAsync(string sessionName);
    Task<bool> StartSessionAsync(string sessionName);
}

public class WhatsAppService : IWhatsAppService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IConversationRepository _conversationRepository;
    private readonly ILogger<WhatsAppService> _logger;

    public WhatsAppService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IConversationRepository conversationRepository,
        ILogger<WhatsAppService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("WPPConnect");
        _configuration = configuration;
        _conversationRepository = conversationRepository;
        _logger = logger;
    }

    public async Task<bool> SendTextMessageAsync(string phoneNumber, string message)
    {
        try
        {
            var payload = new
            {
                phone = phoneNumber,
                message = message
            };

            var sessionName = _configuration["WhatsApp:SessionName"];
            var response = await _httpClient.PostAsJsonAsync($"{sessionName}/send-message", payload);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                var messageResult = JsonSerializer.Deserialize<dynamic>(result);
                
                // Log to database
                await _conversationRepository.LogMessageAsync(new ConversationMessage
                {
                    ChatId = phoneNumber,
                    MessageId = messageResult?.id?.ToString(),
                    SenderType = "bot",
                    MessageType = "text",
                    Content = message,
                    DeliveryStatus = "sent",
                    Timestamp = DateTime.UtcNow
                });

                return true;
            }
            
            _logger.LogError($"Failed to send message: {response.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending WhatsApp message");
            return false;
        }
    }

    public async Task<bool> SendImageAsync(string phoneNumber, string imagePath, string caption = "")
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(phoneNumber), "phone");
            form.Add(new StringContent(caption), "caption");
            
            var imageBytes = await File.ReadAllBytesAsync(imagePath);
            form.Add(new ByteArrayContent(imageBytes), "file", Path.GetFileName(imagePath));

            var sessionName = _configuration["WhatsApp:SessionName"];
            var response = await _httpClient.PostAsync($"{sessionName}/send-image", form);
            
            if (response.IsSuccessStatusCode)
            {
                // Log to database
                await _conversationRepository.LogMessageAsync(new ConversationMessage
                {
                    ChatId = phoneNumber,
                    SenderType = "bot",
                    MessageType = "image",
                    MediaPath = imagePath,
                    Caption = caption,
                    DeliveryStatus = "sent",
                    Timestamp = DateTime.UtcNow
                });

                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending WhatsApp image");
            return false;
        }
    }

    public async Task<string> GenerateTokenAsync(string sessionName)
    {
        try
        {
            var secretKey = _configuration["WhatsApp:SecretKey"];
            var response = await _httpClient.PostAsync($"{sessionName}/{secretKey}/generate-token", null);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                var tokenResult = JsonSerializer.Deserialize<dynamic>(result);
                return tokenResult?.token?.ToString();
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating token");
            return null;
        }
    }
}
```

#### Webhook Controller for Incoming Messages

```csharp
[ApiController]
[Route("api/webhook")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly IConversationRepository _conversationRepository;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        IWhatsAppService whatsAppService,
        IConversationRepository conversationRepository,
        ILogger<WhatsAppWebhookController> logger)
    {
        _whatsAppService = whatsAppService;
        _conversationRepository = conversationRepository;
        _logger = logger;
    }

    [HttpPost("whatsapp")]
    public async Task<IActionResult> ReceiveWhatsAppMessage([FromBody] WhatsAppWebhookEvent webhookEvent)
    {
        try
        {
            // Log incoming message
            await _conversationRepository.LogMessageAsync(new ConversationMessage
            {
                ChatId = webhookEvent.Data.From,
                MessageId = webhookEvent.Data.Id,
                SenderType = "user",
                SenderId = webhookEvent.Data.From,
                SenderName = webhookEvent.Data.NotifyName,
                MessageType = GetMessageType(webhookEvent.Data),
                Content = webhookEvent.Data.Body,
                MediaPath = webhookEvent.Data.MediaUrl,
                Caption = webhookEvent.Data.Caption,
                Timestamp = DateTime.UtcNow
            });

            // Process message for AI response
            await ProcessIncomingMessage(webhookEvent.Data);

            return Ok(new { status = "received" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WhatsApp webhook");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    private async Task ProcessIncomingMessage(WhatsAppMessage message)
    {
        // This is where you integrate with your AI medical assistant
        if (message.Body?.ToLower() == "hello")
        {
            await _whatsAppService.SendTextMessageAsync(
                message.From, 
                "Hello! I am your medical assistant. How can I help you today?"
            );
        }
        
        // TODO: Integrate with your AI processing logic here
    }

    private string GetMessageType(WhatsAppMessage message)
    {
        if (!string.IsNullOrEmpty(message.MediaUrl))
        {
            if (message.Type == "image") return "image";
            if (message.Type == "document") return "document";
        }
        return "text";
    }
}

// Models for webhook data
public class WhatsAppWebhookEvent
{
    public string Event { get; set; }
    public string Session { get; set; }
    public WhatsAppMessage Data { get; set; }
}

public class WhatsAppMessage
{
    public string Id { get; set; }
    public string From { get; set; }
    public string Type { get; set; }
    public string Body { get; set; }
    public string NotifyName { get; set; }
    public string MediaUrl { get; set; }
    public string Caption { get; set; }
    public long Timestamp { get; set; }
}
```

#### Entity Framework Models

```csharp
public class ConversationMessage
{
    public int Id { get; set; }
    public string ChatId { get; set; }
    public string MessageId { get; set; }
    public DateTime Timestamp { get; set; }
    public string SenderType { get; set; } // 'user' or 'bot'
    public string SenderId { get; set; }
    public string SenderName { get; set; }
    public string MessageType { get; set; } // 'text', 'image', 'document'
    public string Content { get; set; }
    public string MediaPath { get; set; }
    public string MediaFilename { get; set; }
    public string Caption { get; set; }
    public string Metadata { get; set; } // JSON string
    public string DeliveryStatus { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MedicalAssistantDbContext : DbContext
{
    public MedicalAssistantDbContext(DbContextOptions<MedicalAssistantDbContext> options) : base(options) { }
    
    public DbSet<ConversationMessage> ConversationMessages { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationMessage>(entity =>
        {
            entity.HasIndex(e => new { e.ChatId, e.Timestamp });
            entity.HasIndex(e => e.SenderType);
            entity.HasIndex(e => e.MessageType);
        });
         }
}
```

#### Webhook Controller for Incoming Messages

```csharp
[ApiController]
[Route("api/webhook")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IConversationRepository _conversationRepository;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    [HttpPost("whatsapp")]
    public async Task<IActionResult> ReceiveWhatsAppMessage([FromBody] WhatsAppWebhookEvent webhookEvent)
    {
        try
        {
            // Log incoming message to database
            await _conversationRepository.LogMessageAsync(new ConversationMessage
            {
                ChatId = webhookEvent.Data.From,
                MessageId = webhookEvent.Data.Id,
                SenderType = "user",
                MessageType = webhookEvent.Data.Type,
                Content = webhookEvent.Data.Body,
                Timestamp = DateTime.UtcNow
            });

            // TODO: Integrate with your AI medical assistant processing
            return Ok(new { status = "received" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WhatsApp webhook");
            return StatusCode(500);
        }
    }
}
```

### .NET Core Dependencies and Configuration

#### appsettings.json
```json
{
  "WhatsApp": {
    "ServerUrl": "http://localhost:21465/api",
    "SessionName": "medical-assistant",
    "SecretKey": "your-secret-key-here",
    "WebhookToken": "your-webhook-token"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=MedicalAssistantDb;Trusted_Connection=true;"
  }
}
```

#### NuGet Packages (.csproj)
```xml
<PackageReference Include="Microsoft.Extensions.Http" Version="7.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="7.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="7.0.0" />
<PackageReference Include="System.Text.Json" Version="7.0.0" />
<PackageReference Include="Serilog.AspNetCore" Version="7.0.0" />
```

#### Program.cs Configuration
```csharp
builder.Services.AddHttpClient("WPPConnect", client =>
{
    var whatsAppConfig = builder.Configuration.GetSection("WhatsApp");
    client.BaseAddress = new Uri(whatsAppConfig["ServerUrl"]);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {whatsAppConfig["WebhookToken"]}");
});

builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
```

## Summary

This specification provides a complete integration architecture between your existing .NET Core application and WhatsApp messaging capabilities through WPPConnect Server. 

### Key Benefits of This Architecture

- **Separation of Concerns**: WPPConnect Server handles WhatsApp connectivity while your .NET Core app handles business logic
- **Scalability**: Each service can be scaled independently
- **Maintainability**: Clear boundaries between WhatsApp integration and your medical assistant logic
- **Technology Consistency**: Keeps your main application in .NET Core while leveraging the mature WPPConnect ecosystem
- **Production Ready**: Both services can be containerized and deployed separately

### Next Steps

1. **Set up WPPConnect Server** as a separate service
2. **Implement WhatsApp service** in your .NET Core application
3. **Create webhook endpoint** to receive incoming messages
4. **Integrate with your existing AI medical assistant logic**
5. **Set up Entity Framework** for conversation storage
6. **Configure dependency injection** for the WhatsApp services

This approach allows you to seamlessly integrate WhatsApp messaging into your existing .NET Core medical assistant while maintaining architectural consistency.
