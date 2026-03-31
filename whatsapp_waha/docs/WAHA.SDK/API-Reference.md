# WAHA API Reference - C# Implementation

🚨 **UPDATED DISCOVERY**: WAHA NuGet Package v1.1.0 has **COMPREHENSIVE METHOD COVERAGE**

Based on analysis of the official WAHA .NET SDK source code, the package includes extensive functionality across multiple domains:

## ✅ **Available in WAHA SDK v1.1.0** (Full Coverage):

### **Sessions Management**
- `GetSessionsAsync()` - Get all sessions
- `CreateSessionAsync()` - Create new session  
- `GetSessionAsync()` - Get specific session details
- `StartSessionAsync()` - Start a session
- `StopSessionAsync()` - Stop a session
- `LogoutSessionAsync()` - Logout from session

### **Authentication**
- `GetAuthQrAsync()` - Get QR code for authentication
- `RequestAuthCodeAsync()` - Request authentication code

### **Messaging (Complete Coverage)**
- `SendTextAsync()` - Send text messages ✅ **AVAILABLE IN SDK**
- `SendImageAsync()` - Send image messages
- `SendFileAsync()` - Send file messages  
- `SendVoiceAsync()` - Send voice messages
- `SendVideoAsync()` - Send video messages
- `SendPollAsync()` - Send polls
- `SendLocationAsync()` - Send location
- `ForwardMessageAsync()` - Forward messages

### **Contacts Management**
- `GetAllContactsAsync()` - Get all contacts
- `CheckContactExistsAsync()` - Check if contact exists
- `BlockContactAsync()` - Block contacts
- `UnblockContactAsync()` - Unblock contacts

### **Groups Management**  
- `CreateGroupAsync()` - Create groups
- `GetGroupsAsync()` - Get all groups
- `AddGroupParticipantsAsync()` - Add participants
- `RemoveGroupParticipantsAsync()` - Remove participants

### **Chats Management**
- `GetChatsAsync()` - Get chat list
- `GetChatMessagesAsync()` - Get chat messages
- `DeleteChatsAsync()` - Delete chats

### **Channels (Advanced)**
- Channel search and management
- Channel following/unfollowing
- Channel message retrieval

**Conclusion**: The WAHA SDK provides comprehensive coverage. Use SDK-first approach with HTTP client only for edge cases.

This document provides comprehensive coverage of all WAHA (WhatsApp HTTP API) endpoints with C# implementation examples using the WAHA SDK.

## 🏗️ SDK Architecture Overview

### **Core Classes and Interfaces**

| Class/Interface | Purpose | File Location |
|----------------|---------|---------------|
| `IWahaApiClient` | Main client interface | IWahaApiClient.cs |
| `WahaApiClient` | Complete API implementation | WahaApiClient.cs |
| `WahaApiClientBuilder` | Builder pattern for client setup | WahaApiClientBuilder.cs |
| `WahaSettings` | Configuration settings | WahaSettings.cs |
| `WahaExtension` | Dependency injection extensions | WahaExtension.cs |
| Entity Models | Data transfer objects | WahaEntities.cs |

### **Project Configuration**
- **Target Framework**: .NET 9.0
- **Assembly Name**: Waha
- **NuGet Package**: Available as "WAHA (WhatsApp HTTP API)"
- **Version**: 1.1.0
- **Author**: fabio.caldas

### **Key Dependencies**
- Microsoft.AspNetCore.WebUtilities (v9.0.5)
- Microsoft.Extensions.Configuration.Binder (v9.0.5)
- Microsoft.Extensions.Hosting.Abstractions (v9.0.5)
- System.Net.Http (v4.3.4)

## 🔧 SDK Setup and Configuration

### **Basic Setup**
```csharp
// Program.cs - Minimal setup
var builder = WebApplication.CreateBuilder(args);

// Add WAHA client with default configuration
builder.AddWahaApiClient("Waha");

var app = builder.Build();
```

### **Advanced Configuration**
```csharp
// Program.cs - Advanced setup with custom settings
var builder = WebApplication.CreateBuilder(args);

// Configure WAHA client with custom settings
builder.AddWahaApiClient("Waha", configureSettings: settings =>
{
    settings.Endpoint = new Uri("https://your-waha-server.com");
});

// Register additional services
builder.Services.AddScoped<IYourService, YourService>();

var app = builder.Build();
```

### **Configuration Options**
```csharp
public class WahaSettings
{
    public Uri Endpoint { get; set; } = new Uri("localhost:3000"); // Default endpoint
}
```

### **Dependency Injection**
```csharp
public class YourService
{
    private readonly IWahaApiClient _wahaClient;
    
    public YourService(IWahaApiClient wahaClient)
    {
        _wahaClient = wahaClient;
    }
    
    public async Task SendMessageAsync(string phoneNumber, string message)
    {
        await _wahaClient.SendTextAsync(phoneNumber, message);
    }
}
```

## 📝 Implementation Strategy

### SDK-First Approach: Complete WAHA SDK Coverage

The WAHA SDK v1.1.0 provides comprehensive method coverage across all domains. Use the SDK for all operations:

```csharp
public class WahaService : IWahaService
{
    private readonly IWahaApiClient _wahaClient; // ✅ Complete API coverage
    private readonly ILogger<WahaService> _logger;
    
    public WahaService(IWahaApiClient wahaClient, ILogger<WahaService> logger)
    {
        _wahaClient = wahaClient;
        _logger = logger;
    }
}
```

### Complete Method Coverage Matrix

| Domain | Available Methods | SDK Coverage |
|--------|-------------------|--------------|
| **Sessions** | Create, Get, Start, Stop, Logout | ✅ Complete |
| **Authentication** | QR Code, Auth Codes | ✅ Complete |
| **Messaging** | Text, Image, File, Voice, Video, Poll, Location | ✅ Complete |
| **Contacts** | Get, Check, Block, Unblock | ✅ Complete |
| **Groups** | Create, Get, Add/Remove Participants | ✅ Complete |
| **Chats** | Get, Messages, Delete, Labels | ✅ Complete |
| **Channels** | Search, Create, Follow, Messages | ✅ Complete |
| **Profile** | Get, Update Name/About/Picture | ✅ Complete |
| **Observability** | Ping, Health, Version, Status | ✅ Complete |

### Recommended Registration Pattern

```csharp
// Program.cs - Recommended setup
var builder = WebApplication.CreateBuilder(args);

// Single WAHA SDK registration (comprehensive coverage)
builder.AddWahaApiClient("Waha", configureSettings: settings =>
{
    settings.Endpoint = new Uri(builder.Configuration.GetConnectionString("Waha") ?? "localhost:3000");
});

// Register your business services
builder.Services.AddScoped<IWahaService, WahaService>();

var app = builder.Build();
```

## Table of Contents

1. [Interface Reference](#interface-reference)
2. [Session Management](#session-management)
3. [Authentication](#authentication)
4. [Message Sending](#message-sending)
5. [Contact Management](#contact-management)
6. [Chat Management](#chat-management)
7. [Group Management](#group-management)
8. [Channels Management](#channels-management)
9. [Profile Management](#profile-management)
10. [Observability](#observability)
11. [Data Models](#data-models)
12. [Error Handling](#error-handling)

## Interface Reference

### IWahaApiClient - Complete Method List

The `IWahaApiClient` interface provides comprehensive WhatsApp API functionality across multiple regions:

#### **Sessions Region**
```csharp
Task<List<Session>> GetSessionsAsync(CancellationToken cancellationToken = default);
Task<Session> CreateSessionAsync(SessionCreateRequest request, CancellationToken cancellationToken = default);
Task<Session> GetSessionAsync(string name, CancellationToken cancellationToken = default);
Task<Session> UpdateSessionAsync(string name, SessionUpdateRequest request, CancellationToken cancellationToken = default);
Task DeleteSessionAsync(string name, CancellationToken cancellationToken = default);
Task<Session> StartSessionAsync(string name, CancellationToken cancellationToken = default);
Task<Session> StopSessionAsync(string name, CancellationToken cancellationToken = default);
Task<Session> RestartSessionAsync(string name, CancellationToken cancellationToken = default);
Task<Session> LogoutSessionAsync(string name, CancellationToken cancellationToken = default);
Task<SessionUser> GetSessionUserAsync(string name, CancellationToken cancellationToken = default);
```

#### **Authentication Region**
```csharp
Task<AuthQrResponse> GetAuthQrAsync(string session, string? format = null, CancellationToken cancellationToken = default);
Task RequestAuthCodeAsync(string session, string phoneNumber, string? method = null, CancellationToken cancellationToken = default);
```

#### **Profile Region**
```csharp
Task<Profile> GetProfileAsync(string session, CancellationToken cancellationToken = default);
Task UpdateProfileNameAsync(string session, string name, CancellationToken cancellationToken = default);
Task UpdateProfileAboutAsync(string session, string about, CancellationToken cancellationToken = default);
Task UpdateProfilePictureAsync(string session, string file, CancellationToken cancellationToken = default);
Task RemoveProfilePictureAsync(string session, CancellationToken cancellationToken = default);
```

#### **Messaging Region**
```csharp
Task<Message> SendTextAsync(string session, string chatId, string text, CancellationToken cancellationToken = default);
Task<Message> SendImageAsync(string session, string chatId, SendImageRequest request, CancellationToken cancellationToken = default);
Task<Message> SendFileAsync(string session, string chatId, SendFileRequest request, CancellationToken cancellationToken = default);
Task<Message> SendVoiceAsync(string session, string chatId, SendVoiceRequest request, CancellationToken cancellationToken = default);
Task<Message> SendVideoAsync(string session, string chatId, SendVideoRequest request, CancellationToken cancellationToken = default);
Task<Message> SendLocationAsync(string session, string chatId, SendLocationRequest request, CancellationToken cancellationToken = default);
Task<Message> SendLinkPreviewAsync(string session, string chatId, SendLinkPreviewRequest request, CancellationToken cancellationToken = default);
Task<Message> SendContactVCardAsync(string session, string chatId, SendContactVCardRequest request, CancellationToken cancellationToken = default);
Task<Message> SendPollAsync(string session, string chatId, SendPollRequest request, CancellationToken cancellationToken = default);
Task<Message> ForwardMessageAsync(string session, string chatId, string messageId, CancellationToken cancellationToken = default);
Task SetReactionAsync(string session, string chatId, string messageId, string reaction, CancellationToken cancellationToken = default);
```

#### **Channels Region**
```csharp
Task<List<Channel>> SearchChannelsAsync(string session, string query, CancellationToken cancellationToken = default);
Task<Channel> CreateChannelAsync(string session, string name, string? description = null, CancellationToken cancellationToken = default);
Task DeleteChannelAsync(string session, string channelId, CancellationToken cancellationToken = default);
Task<Channel> GetChannelAsync(string session, string channelId, CancellationToken cancellationToken = default);
Task<Channel> FollowChannelAsync(string session, string channelId, CancellationToken cancellationToken = default);
Task<Channel> UnfollowChannelAsync(string session, string channelId, CancellationToken cancellationToken = default);
Task<List<Message>> GetChannelMessagesAsync(string session, string channelId, int? limit = null, CancellationToken cancellationToken = default);
```

#### **Chats Region**
```csharp
Task<List<Chat>> GetChatsAsync(string session, int limit, int offset, string sortBy, string sortOrder, CancellationToken cancellationToken = default);
Task<List<Message>> GetChatMessagesAsync(string session, string chatId, int? limit = null, bool? downloadMedia = null, CancellationToken cancellationToken = default);
Task DeleteChatAsync(string session, string chatId, CancellationToken cancellationToken = default);
Task<List<string>> GetChatLabelsAsync(string session, string chatId, CancellationToken cancellationToken = default);
Task SetChatLabelsAsync(string session, string chatId, List<string> labels, CancellationToken cancellationToken = default);
```

#### **Contacts Region**
```csharp
Task<List<Contact>> GetAllContactsAsync(string session, CancellationToken cancellationToken = default);
Task<Contact> GetContactAsync(string session, string contactId, CancellationToken cancellationToken = default);
Task<bool> CheckContactExistsAsync(string session, string phone, CancellationToken cancellationToken = default);
Task BlockContactAsync(string session, string contactId, CancellationToken cancellationToken = default);
Task UnblockContactAsync(string session, string contactId, CancellationToken cancellationToken = default);
```

#### **Groups Region**
```csharp
Task<Group> CreateGroupAsync(string session, string name, List<string> participants, CancellationToken cancellationToken = default);
Task<List<Group>> GetGroupsAsync(string session, CancellationToken cancellationToken = default);
Task<Group> GetGroupAsync(string session, string groupId, CancellationToken cancellationToken = default);
Task DeleteGroupAsync(string session, string groupId, CancellationToken cancellationToken = default);
Task LeaveGroupAsync(string session, string groupId, CancellationToken cancellationToken = default);
Task SetGroupSubjectAsync(string session, string groupId, string subject, CancellationToken cancellationToken = default);
Task SetGroupDescriptionAsync(string session, string groupId, string description, CancellationToken cancellationToken = default);
Task<List<GroupParticipant>> GetGroupParticipantsAsync(string session, string groupId, CancellationToken cancellationToken = default);
Task AddGroupParticipantsAsync(string session, string groupId, List<string> participants, CancellationToken cancellationToken = default);
Task RemoveGroupParticipantsAsync(string session, string groupId, List<string> participants, CancellationToken cancellationToken = default);
Task PromoteGroupParticipantsAsync(string session, string groupId, List<string> participants, CancellationToken cancellationToken = default);
Task DemoteGroupParticipantsAsync(string session, string groupId, List<string> participants, CancellationToken cancellationToken = default);
Task SetGroupPictureAsync(string session, string groupId, string file, CancellationToken cancellationToken = default);
Task RemoveGroupPictureAsync(string session, string groupId, CancellationToken cancellationToken = default);
Task<string> GetGroupInviteCodeAsync(string session, string groupId, CancellationToken cancellationToken = default);
Task RevokeGroupInviteCodeAsync(string session, string groupId, CancellationToken cancellationToken = default);
Task<List<GroupParticipant>> GetGroupInviteInfoAsync(string session, string inviteCode, CancellationToken cancellationToken = default);
```

#### **Observability Region**
```csharp
Task<string> PingAsync(CancellationToken cancellationToken = default);
Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken = default);
Task<VersionResponse> GetVersionAsync(CancellationToken cancellationToken = default);
Task<StatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);
```

## Session Management

### Session Operations (WAHA SDK) ✅

All session operations are fully supported by the WAHA SDK:

#### Get All Sessions
```csharp
// ✅ WAHA SDK Method
public async Task<List<Session>> GetSessionsAsync()
{
    return await _wahaClient.GetSessionsAsync();
}
```

#### Create Session
```csharp
// ✅ WAHA SDK Method
public async Task<Session> CreateSessionAsync(string sessionName, SessionConfig config = null)
{
    var request = new SessionCreateRequest
    {
        Name = sessionName,
        Config = config ?? new SessionConfig()
    };
    
    return await _wahaClient.CreateSessionAsync(request);
}
```

#### Get Session Details
```csharp
// ✅ WAHA SDK Method
public async Task<Session> GetSessionAsync(string sessionName)
{
    return await _wahaClient.GetSessionAsync(sessionName);
}
```

#### Start Session
```csharp
// ✅ WAHA SDK Method
public async Task<Session> StartSessionAsync(string sessionName)
{
    return await _wahaClient.StartSessionAsync(sessionName);
}
```

#### Stop Session
```csharp
// ✅ WAHA SDK Method
public async Task<Session> StopSessionAsync(string sessionName)
{
    return await _wahaClient.StopSessionAsync(sessionName);
}
```

#### Restart Session
```csharp
// ✅ WAHA SDK Method
public async Task<Session> RestartSessionAsync(string sessionName)
{
    return await _wahaClient.RestartSessionAsync(sessionName);
}
```

#### Logout Session
```csharp
// ✅ WAHA SDK Method
public async Task<Session> LogoutSessionAsync(string sessionName)
{
    return await _wahaClient.LogoutSessionAsync(sessionName);
}
```

#### Update Session
```csharp
// ✅ WAHA SDK Method
public async Task<Session> UpdateSessionAsync(string sessionName, SessionUpdateRequest request)
{
    return await _wahaClient.UpdateSessionAsync(sessionName, request);
}
```

#### Delete Session
```csharp
// ✅ WAHA SDK Method
public async Task DeleteSessionAsync(string sessionName)
{
    await _wahaClient.DeleteSessionAsync(sessionName);
}
```

#### Get Session User
```csharp
// ✅ WAHA SDK Method
public async Task<SessionUser> GetSessionUserAsync(string sessionName)
{
    return await _wahaClient.GetSessionUserAsync(sessionName);
}
```

## Authentication

### Authentication Operations (WAHA SDK) ✅

#### Get QR Code
```csharp
// ✅ WAHA SDK Method
public async Task<AuthQrResponse> GetQrCodeAsync(string sessionName, string format = "image")
{
    return await _wahaClient.GetAuthQrAsync(sessionName, format);
}
```

#### Request Authentication Code
```csharp
// ✅ WAHA SDK Method
public async Task RequestAuthCodeAsync(string sessionName, string phoneNumber, string method = null)
{
    await _wahaClient.RequestAuthCodeAsync(sessionName, phoneNumber, method);
}
```

## Profile Management

### Profile Operations (WAHA SDK) ✅

#### Get Profile
```csharp
// ✅ WAHA SDK Method
public async Task<Profile> GetProfileAsync(string sessionName)
{
    return await _wahaClient.GetProfileAsync(sessionName);
}
```

#### Update Profile Name
```csharp
// ✅ WAHA SDK Method
public async Task UpdateProfileNameAsync(string sessionName, string name)
{
    await _wahaClient.UpdateProfileNameAsync(sessionName, name);
}
```

#### Update Profile About
```csharp
// ✅ WAHA SDK Method
public async Task UpdateProfileAboutAsync(string sessionName, string about)
{
    await _wahaClient.UpdateProfileAboutAsync(sessionName, about);
}
```

#### Update Profile Picture
```csharp
// ✅ WAHA SDK Method
public async Task UpdateProfilePictureAsync(string sessionName, string imageFile)
{
    await _wahaClient.UpdateProfilePictureAsync(sessionName, imageFile);
}
```

#### Remove Profile Picture
```csharp
// ✅ WAHA SDK Method
public async Task RemoveProfilePictureAsync(string sessionName)
{
    await _wahaClient.RemoveProfilePictureAsync(sessionName);
}
```

#### Get Session Status (HTTP Client)
```csharp
// 🔧 NOT IN SDK: Use HTTP client - This is what we need for session validation
public async Task<SessionStatus> GetSessionStatusAsync(string sessionName)
{
    var endpoint = $"/api/sessions/{sessionName}";
    var response = await _httpClient.GetAsync(endpoint);
    
    if (!response.IsSuccessStatusCode)
        return null;
        
    return await response.Content.ReadFromJsonAsync<SessionStatus>();
}

// ✅ IMPLEMENTATION: Our ValidateSessionAsync method
public async Task<bool> ValidateSessionAsync(CancellationToken cancellationToken = default)
{
    var sessionStatus = await GetSessionStatusAsync(_settings.Session);
    return sessionStatus?.Status == "WORKING";
}
```

#### List All Sessions (WAHA SDK vs HTTP Client)
```csharp
// ✅ PREFERRED: Use WAHA SDK method
public async Task<List<SessionInfo>> GetSessionsAsync()
{
    return await _wahaClient.GetSessionsAsync(true, CancellationToken.None);
}

// 🔧 ALTERNATIVE: HTTP client (if SDK method doesn't work)
public async Task<List<SessionInfo>> GetSessionsHttpAsync()
{
    var endpoint = "/api/sessions";
    var response = await _httpClient.GetAsync(endpoint);
    return await response.Content.ReadFromJsonAsync<List<SessionInfo>>();
}
```

#### Delete Session
```csharp
public async Task<bool> DeleteSessionAsync(string sessionName)
{
    var endpoint = $"/api/sessions/{sessionName}";
    var response = await _httpClient.DeleteAsync(endpoint);
    return response.IsSuccessStatusCode;
}
```

### Authentication Operations

#### Get QR Code
```csharp
public async Task<QrCodeResult> GetQrCodeAsync(string sessionName, string format = "image")
{
    var endpoint = $"/api/{sessionName}/auth/qr?format={format}";
    var response = await _httpClient.GetAsync(endpoint);
    
    var result = new QrCodeResult
    {
        Success = response.IsSuccessStatusCode
    };
    
    if (format == "image")
    {
        result.ImageData = await response.Content.ReadAsByteArrayAsync();
    }
    else
    {
        var qrData = await response.Content.ReadFromJsonAsync<QrData>();
        result.QrCode = qrData?.Qr;
    }
    
    return result;
}
```

#### Request Pairing Code
```csharp
public async Task<PairingCodeResult> RequestPairingCodeAsync(string sessionName, string phoneNumber)
{
    var endpoint = $"/api/{sessionName}/auth/request-code";
    var payload = new { phoneNumber };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return await response.Content.ReadFromJsonAsync<PairingCodeResult>();
}
```

## Message Sending

### Messaging Operations (WAHA SDK) ✅

All messaging operations are fully supported by the WAHA SDK:

#### Send Text Message
```csharp
// ✅ WAHA SDK Method
public async Task<Message> SendTextMessageAsync(string sessionName, string chatId, string text)
{
    return await _wahaClient.SendTextAsync(sessionName, chatId, text);
}
```

#### Send Image Message
```csharp
// ✅ WAHA SDK Method
public async Task<Message> SendImageAsync(string sessionName, string chatId, SendImageRequest request)
{
    return await _wahaClient.SendImageAsync(sessionName, chatId, request);
}
```

#### Send File Message
```csharp
// ✅ WAHA SDK Method
public async Task<Message> SendFileAsync(string sessionName, string chatId, SendFileRequest request)
{
    return await _wahaClient.SendFileAsync(sessionName, chatId, request);
}
```

#### Send Voice Message
```csharp
// ✅ WAHA SDK Method
public async Task<Message> SendVoiceAsync(string sessionName, string chatId, SendVoiceRequest request)
{
    return await _wahaClient.SendVoiceAsync(sessionName, chatId, request);
}
```

#### Send Video Message
```csharp
// ✅ WAHA SDK Method
public async Task<Message> SendVideoAsync(string sessionName, string chatId, SendVideoRequest request)
{
    return await _wahaClient.SendVideoAsync(sessionName, chatId, request);
}
```

#### Send Location
```csharp
// ✅ WAHA SDK Method
public async Task<Message> SendLocationAsync(string sessionName, string chatId, SendLocationRequest request)
{
    return await _wahaClient.SendLocationAsync(sessionName, chatId, request);
}
```

#### Send Contact (vCard)
```csharp
// ✅ WAHA SDK Method
public async Task<Message> SendContactAsync(string sessionName, string chatId, SendContactVCardRequest request)
{
    return await _wahaClient.SendContactVCardAsync(sessionName, chatId, request);
}
```

#### Send Poll
```csharp
// ✅ WAHA SDK Method
public async Task<Message> SendPollAsync(string sessionName, string chatId, SendPollRequest request)
{
    return await _wahaClient.SendPollAsync(sessionName, chatId, request);
}
```

#### Send Link Preview
```csharp
// ✅ WAHA SDK Method
public async Task<Message> SendLinkPreviewAsync(string sessionName, string chatId, SendLinkPreviewRequest request)
{
    return await _wahaClient.SendLinkPreviewAsync(sessionName, chatId, request);
}
```

#### Forward Message
```csharp
// ✅ WAHA SDK Method
public async Task<Message> ForwardMessageAsync(string sessionName, string chatId, string messageId)
{
    return await _wahaClient.ForwardMessageAsync(sessionName, chatId, messageId);
}
```

#### Set Message Reaction
```csharp
// ✅ WAHA SDK Method
public async Task SetReactionAsync(string sessionName, string chatId, string messageId, string reaction)
{
    await _wahaClient.SetReactionAsync(sessionName, chatId, messageId, reaction);
}
```

## Contact Management

### Contact Operations (WAHA SDK) ✅

#### Get All Contacts
```csharp
// ✅ WAHA SDK Method
public async Task<List<Contact>> GetAllContactsAsync(string sessionName)
{
    return await _wahaClient.GetAllContactsAsync(sessionName);
}
```

#### Get Contact Details
```csharp
// ✅ WAHA SDK Method
public async Task<Contact> GetContactAsync(string sessionName, string contactId)
{
    return await _wahaClient.GetContactAsync(sessionName, contactId);
}
```

#### Check Contact Exists
```csharp
// ✅ WAHA SDK Method
public async Task<bool> CheckContactExistsAsync(string sessionName, string phoneNumber)
{
    return await _wahaClient.CheckContactExistsAsync(sessionName, phoneNumber);
}
```

#### Block Contact
```csharp
// ✅ WAHA SDK Method
public async Task BlockContactAsync(string sessionName, string contactId)
{
    await _wahaClient.BlockContactAsync(sessionName, contactId);
}
```

#### Unblock Contact
```csharp
// ✅ WAHA SDK Method
public async Task UnblockContactAsync(string sessionName, string contactId)
{
    await _wahaClient.UnblockContactAsync(sessionName, contactId);
}
```

## Chat Management

### Chat Operations (WAHA SDK) ✅

#### Get All Chats
```csharp
// ✅ WAHA SDK Method
public async Task<List<Chat>> GetChatsAsync(string sessionName, int limit = 100, int offset = 0, string sortBy = "t", string sortOrder = "desc")
{
    return await _wahaClient.GetChatsAsync(sessionName, limit, offset, sortBy, sortOrder);
}
```

#### Get Chat Messages
```csharp
// ✅ WAHA SDK Method
public async Task<List<Message>> GetChatMessagesAsync(string sessionName, string chatId, int? limit = null, bool? downloadMedia = null)
{
    return await _wahaClient.GetChatMessagesAsync(sessionName, chatId, limit, downloadMedia);
}
```

#### Delete Chat
```csharp
// ✅ WAHA SDK Method
public async Task DeleteChatAsync(string sessionName, string chatId)
{
    await _wahaClient.DeleteChatAsync(sessionName, chatId);
}
```

#### Get Chat Labels
```csharp
// ✅ WAHA SDK Method
public async Task<List<string>> GetChatLabelsAsync(string sessionName, string chatId)
{
    return await _wahaClient.GetChatLabelsAsync(sessionName, chatId);
}
```

#### Set Chat Labels
```csharp
// ✅ WAHA SDK Method
public async Task SetChatLabelsAsync(string sessionName, string chatId, List<string> labels)
{
    await _wahaClient.SetChatLabelsAsync(sessionName, chatId, labels);
}
```

## Group Management

### Group Operations (WAHA SDK) ✅

#### Create Group
```csharp
// ✅ WAHA SDK Method
public async Task<Group> CreateGroupAsync(string sessionName, string groupName, List<string> participants)
{
    return await _wahaClient.CreateGroupAsync(sessionName, groupName, participants);
}
```

#### Get All Groups
```csharp
// ✅ WAHA SDK Method
public async Task<List<Group>> GetGroupsAsync(string sessionName)
{
    return await _wahaClient.GetGroupsAsync(sessionName);
}
```

#### Get Group Details
```csharp
// ✅ WAHA SDK Method
public async Task<Group> GetGroupAsync(string sessionName, string groupId)
{
    return await _wahaClient.GetGroupAsync(sessionName, groupId);
}
```

#### Delete Group
```csharp
// ✅ WAHA SDK Method
public async Task DeleteGroupAsync(string sessionName, string groupId)
{
    await _wahaClient.DeleteGroupAsync(sessionName, groupId);
}
```

#### Leave Group
```csharp
// ✅ WAHA SDK Method
public async Task LeaveGroupAsync(string sessionName, string groupId)
{
    await _wahaClient.LeaveGroupAsync(sessionName, groupId);
}
```

#### Set Group Subject
```csharp
// ✅ WAHA SDK Method
public async Task SetGroupSubjectAsync(string sessionName, string groupId, string subject)
{
    await _wahaClient.SetGroupSubjectAsync(sessionName, groupId, subject);
}
```

#### Set Group Description
```csharp
// ✅ WAHA SDK Method
public async Task SetGroupDescriptionAsync(string sessionName, string groupId, string description)
{
    await _wahaClient.SetGroupDescriptionAsync(sessionName, groupId, description);
}
```

### Group Participant Management

#### Get Group Participants
```csharp
// ✅ WAHA SDK Method
public async Task<List<GroupParticipant>> GetGroupParticipantsAsync(string sessionName, string groupId)
{
    return await _wahaClient.GetGroupParticipantsAsync(sessionName, groupId);
}
```

#### Add Group Participants
```csharp
// ✅ WAHA SDK Method
public async Task AddGroupParticipantsAsync(string sessionName, string groupId, List<string> participants)
{
    await _wahaClient.AddGroupParticipantsAsync(sessionName, groupId, participants);
}
```

#### Remove Group Participants
```csharp
// ✅ WAHA SDK Method
public async Task RemoveGroupParticipantsAsync(string sessionName, string groupId, List<string> participants)
{
    await _wahaClient.RemoveGroupParticipantsAsync(sessionName, groupId, participants);
}
```

#### Promote Participants to Admin
```csharp
// ✅ WAHA SDK Method
public async Task PromoteGroupParticipantsAsync(string sessionName, string groupId, List<string> participants)
{
    await _wahaClient.PromoteGroupParticipantsAsync(sessionName, groupId, participants);
}
```

#### Demote Participants from Admin
```csharp
// ✅ WAHA SDK Method
public async Task DemoteGroupParticipantsAsync(string sessionName, string groupId, List<string> participants)
{
    await _wahaClient.DemoteGroupParticipantsAsync(sessionName, groupId, participants);
}
```

### Group Settings and Media

#### Set Group Picture
```csharp
// ✅ WAHA SDK Method
public async Task SetGroupPictureAsync(string sessionName, string groupId, string imageFile)
{
    await _wahaClient.SetGroupPictureAsync(sessionName, groupId, imageFile);
}
```

#### Remove Group Picture
```csharp
// ✅ WAHA SDK Method
public async Task RemoveGroupPictureAsync(string sessionName, string groupId)
{
    await _wahaClient.RemoveGroupPictureAsync(sessionName, groupId);
}
```

#### Get Group Invite Code
```csharp
// ✅ WAHA SDK Method
public async Task<string> GetGroupInviteCodeAsync(string sessionName, string groupId)
{
    return await _wahaClient.GetGroupInviteCodeAsync(sessionName, groupId);
}
```

#### Revoke Group Invite Code
```csharp
// ✅ WAHA SDK Method
public async Task RevokeGroupInviteCodeAsync(string sessionName, string groupId)
{
    await _wahaClient.RevokeGroupInviteCodeAsync(sessionName, groupId);
}
```

#### Get Group Invite Info
```csharp
// ✅ WAHA SDK Method
public async Task<List<GroupParticipant>> GetGroupInviteInfoAsync(string sessionName, string inviteCode)
{
    return await _wahaClient.GetGroupInviteInfoAsync(sessionName, inviteCode);
}
```

## Channels Management

### Channel Operations (WAHA SDK) ✅

#### Search Channels
```csharp
// ✅ WAHA SDK Method
public async Task<List<Channel>> SearchChannelsAsync(string sessionName, string query)
{
    return await _wahaClient.SearchChannelsAsync(sessionName, query);
}
```

#### Create Channel
```csharp
// ✅ WAHA SDK Method
public async Task<Channel> CreateChannelAsync(string sessionName, string name, string description = null)
{
    return await _wahaClient.CreateChannelAsync(sessionName, name, description);
}
```

#### Get Channel Details
```csharp
// ✅ WAHA SDK Method
public async Task<Channel> GetChannelAsync(string sessionName, string channelId)
{
    return await _wahaClient.GetChannelAsync(sessionName, channelId);
}
```

#### Delete Channel
```csharp
// ✅ WAHA SDK Method
public async Task DeleteChannelAsync(string sessionName, string channelId)
{
    await _wahaClient.DeleteChannelAsync(sessionName, channelId);
}
```

#### Follow Channel
```csharp
// ✅ WAHA SDK Method
public async Task<Channel> FollowChannelAsync(string sessionName, string channelId)
{
    return await _wahaClient.FollowChannelAsync(sessionName, channelId);
}
```

#### Unfollow Channel
```csharp
// ✅ WAHA SDK Method
public async Task<Channel> UnfollowChannelAsync(string sessionName, string channelId)
{
    return await _wahaClient.UnfollowChannelAsync(sessionName, channelId);
}
```

#### Get Channel Messages
```csharp
// ✅ WAHA SDK Method
public async Task<List<Message>> GetChannelMessagesAsync(string sessionName, string channelId, int? limit = null)
{
    return await _wahaClient.GetChannelMessagesAsync(sessionName, channelId, limit);
}
```

## Observability

### System Operations (WAHA SDK) ✅

#### Ping Server
```csharp
// ✅ WAHA SDK Method
public async Task<string> PingAsync()
{
    return await _wahaClient.PingAsync();
}
```

#### Get Health Status
```csharp
// ✅ WAHA SDK Method
public async Task<HealthResponse> GetHealthAsync()
{
    return await _wahaClient.GetHealthAsync();
}
```

#### Get Server Version
```csharp
// ✅ WAHA SDK Method
public async Task<VersionResponse> GetVersionAsync()
{
    return await _wahaClient.GetVersionAsync();
}
```

#### Get Server Status
```csharp
// ✅ WAHA SDK Method
public async Task<StatusResponse> GetStatusAsync()
{
    return await _wahaClient.GetStatusAsync();
}
```
- **Endpoint**: `POST /api/sendText`
- **Content-Type**: `application/json`
- **Headers**: `X-Api-Key: yoursecretkey` (if API key authentication enabled)

**Request Body**:
```json
{
  "session": "default",
  "chatId": "12132132130@c.us",
  "text": "Hi there!",
  "reply_to": "optional_message_id",
  "mentions": ["optional_contact_ids"],
  "linkPreview": true
}
```

### Media Messages

#### Send Image
```csharp
public async Task<WahaMessageResult> SendImageAsync(string phoneNumber, string imageUrl, string caption = null, bool convert = true)
{
    var endpoint = "/api/sendImage";
    var payload = new
    {
        session = _settings.Session,
        chatId = FormatPhoneNumber(phoneNumber),
        file = imageUrl,
        caption = caption,
        convert = convert
    };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return await response.Content.ReadFromJsonAsync<WahaMessageResult>();
}
```

#### Send Voice Message
```csharp
public async Task<WahaMessageResult> SendVoiceAsync(string phoneNumber, string audioUrl)
{
    var endpoint = "/api/sendVoice";
    var payload = new
    {
        session = _settings.Session,
        chatId = FormatPhoneNumber(phoneNumber),
        file = audioUrl
    };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return await response.Content.ReadFromJsonAsync<WahaMessageResult>();
}
```

#### Send Video
```csharp
public async Task<WahaMessageResult> SendVideoAsync(string phoneNumber, string videoUrl, string caption = null, bool asNote = false)
{
    var endpoint = "/api/sendVideo";
    var payload = new
    {
        session = _settings.Session,
        chatId = FormatPhoneNumber(phoneNumber),
        file = videoUrl,
        caption = caption,
        asNote = asNote
    };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return await response.Content.ReadFromJsonAsync<WahaMessageResult>();
}
```

#### Send Document
```csharp
public async Task<WahaMessageResult> SendDocumentAsync(string phoneNumber, string fileUrl, string filename)
{
    var endpoint = "/api/sendDocument";
    var payload = new
    {
        session = _settings.Session,
        chatId = FormatPhoneNumber(phoneNumber),
        file = fileUrl,
        filename = filename
    };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return await response.Content.ReadFromJsonAsync<WahaMessageResult>();
}
```

### Advanced Messages

#### Send Location
```csharp
public async Task<WahaMessageResult> SendLocationAsync(string phoneNumber, double latitude, double longitude, string title = null, string address = null)
{
    var endpoint = "/api/sendLocation";
    var payload = new
    {
        session = _settings.Session,
        chatId = FormatPhoneNumber(phoneNumber),
        latitude = latitude,
        longitude = longitude,
        title = title,
        address = address
    };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return await response.Content.ReadFromJsonAsync<WahaMessageResult>();
}
```

#### Send Contact (vCard)
```csharp
public async Task<WahaMessageResult> SendContactAsync(string phoneNumber, ContactInfo contact)
{
    var endpoint = "/api/sendContactVcard";
    var payload = new
    {
        session = _settings.Session,
        chatId = FormatPhoneNumber(phoneNumber),
        contacts = new[] { contact }
    };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return await response.Content.ReadFromJsonAsync<WahaMessageResult>();
}
```

#### Send Poll
```csharp
public async Task<WahaMessageResult> SendPollAsync(string phoneNumber, string question, List<string> options, bool allowMultipleAnswers = false)
{
    var endpoint = "/api/sendPoll";
    var payload = new
    {
        session = _settings.Session,
        chatId = FormatPhoneNumber(phoneNumber),
        poll = new
        {
            name = question,
            options = options,
            allowMultipleAnswers = allowMultipleAnswers
        }
    };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return await response.Content.ReadFromJsonAsync<WahaMessageResult>();
}
```

#### Send Buttons
```csharp
public async Task<WahaMessageResult> SendButtonsAsync(string phoneNumber, string text, List<ButtonInfo> buttons)
{
    var endpoint = "/api/sendButtons";
    var payload = new
    {
        session = _settings.Session,
        chatId = FormatPhoneNumber(phoneNumber),
        text = text,
        buttons = buttons
    };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return await response.Content.ReadFromJsonAsync<WahaMessageResult>();
}
```

## Message Receiving

### Webhook Configuration

#### Set Webhook URL
```csharp
public async Task<bool> SetWebhookAsync(string sessionName, string webhookUrl, List<string> events = null)
{
    var endpoint = $"/api/sessions/{sessionName}/webhook";
    var payload = new
    {
        url = webhookUrl,
        events = events ?? new[] { "message" }
    };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return response.IsSuccessStatusCode;
}
```

#### Get Webhook Configuration
```csharp
public async Task<WebhookConfig> GetWebhookAsync(string sessionName)
{
    var endpoint = $"/api/sessions/{sessionName}/webhook";
    var response = await _httpClient.GetAsync(endpoint);
    
    if (!response.IsSuccessStatusCode)
        return null;
        
    return await response.Content.ReadFromJsonAsync<WebhookConfig>();
}
```

### Message Polling (Alternative to Webhooks)

#### Poll Messages
```csharp
public async Task<List<IncomingMessage>> PollMessagesAsync(string sessionName, int limit = 100)
{
    var endpoint = $"/api/{sessionName}/chats/{chatId}/messages?limit={limit}";
    var response = await _httpClient.GetAsync(endpoint);
    
    if (!response.IsSuccessStatusCode)
        return new List<IncomingMessage>();
        
    return await response.Content.ReadFromJsonAsync<List<IncomingMessage>>();
}
```

## Contact Management

### Contact Operations

#### Get All Contacts
```csharp
public async Task<List<Contact>> GetContactsAsync(string sessionName, int limit = 100, int offset = 0)
{
    var endpoint = $"/api/{sessionName}/contacts/all?limit={limit}&offset={offset}";
    var response = await _httpClient.GetAsync(endpoint);
    return await response.Content.ReadFromJsonAsync<List<Contact>>();
}
```

#### Get Contact Info
```csharp
public async Task<Contact> GetContactAsync(string sessionName, string contactId)
{
    var endpoint = $"/api/{sessionName}/contacts?contactId={contactId}";
    var response = await _httpClient.GetAsync(endpoint);
    
    if (!response.IsSuccessStatusCode)
        return null;
        
    return await response.Content.ReadFromJsonAsync<Contact>();
}
```

#### Update Contact
```csharp
public async Task<bool> UpdateContactAsync(string sessionName, string chatId, string firstName, string lastName = null)
{
    var endpoint = $"/api/{sessionName}/contacts/{chatId}";
    var payload = new
    {
        firstName = firstName,
        lastName = lastName
    };
    
    var response = await _httpClient.PutAsJsonAsync(endpoint, payload);
    return response.IsSuccessStatusCode;
}
```

#### Check Phone Number Exists
```csharp
public async Task<bool> CheckPhoneExistsAsync(string sessionName, string phoneNumber)
{
    var endpoint = $"/api/{sessionName}/contacts/check-exists?phone={phoneNumber}";
    var response = await _httpClient.GetAsync(endpoint);
    
    if (!response.IsSuccessStatusCode)
        return false;
        
    var result = await response.Content.ReadFromJsonAsync<PhoneExistsResult>();
    return result?.Exists ?? false;
}
```

#### Get Profile Picture
```csharp
public async Task<string> GetProfilePictureAsync(string sessionName, string contactId)
{
    var endpoint = $"/api/{sessionName}/contacts/profile-picture?contactId={contactId}";
    var response = await _httpClient.GetAsync(endpoint);
    
    if (!response.IsSuccessStatusCode)
        return null;
        
    var result = await response.Content.ReadFromJsonAsync<ProfilePictureResult>();
    return result?.Url;
}
```

#### Block/Unblock Contact
```csharp
public async Task<bool> BlockContactAsync(string sessionName, string chatId)
{
    var endpoint = $"/api/{sessionName}/contacts/block";
    var payload = new { chatId = chatId };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return response.IsSuccessStatusCode;
}

public async Task<bool> UnblockContactAsync(string sessionName, string chatId)
{
    var endpoint = $"/api/{sessionName}/contacts/unblock";
    var payload = new { chatId = chatId };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return response.IsSuccessStatusCode;
}
```

## Chat Management

### Chat Operations

#### Get All Chats (WAHA SDK Available) ✅
```csharp
// ✅ AVAILABLE: Use WAHA SDK method
public async Task<List<ChatInfo>> GetChatsAsync(string sessionName, int limit = 100, int offset = 0)
{
    return await _wahaClient.GetChatsAsync(sessionName, limit, offset, "t", "desc", CancellationToken.None);
}

// 🔧 ALTERNATIVE: HTTP client
public async Task<List<ChatInfo>> GetChatsHttpAsync(string sessionName, int limit = 100, int offset = 0)
{
    var endpoint = $"/api/{sessionName}/chats?limit={limit}&offset={offset}";
    var response = await _httpClient.GetAsync(endpoint);
    return await response.Content.ReadFromJsonAsync<List<ChatInfo>>();
}
```

#### Get Chat Messages
```csharp
public async Task<List<Message>> GetChatMessagesAsync(string sessionName, string chatId, int limit = 100, bool downloadMedia = true)
{
    var endpoint = $"/api/{sessionName}/chats/{chatId}/messages?limit={limit}&downloadMedia={downloadMedia}";
    var response = await _httpClient.GetAsync(endpoint);
    return await response.Content.ReadFromJsonAsync<List<Message>>();
}
```

#### Mark Chat as Read
```csharp
public async Task<bool> MarkChatAsReadAsync(string sessionName, string chatId)
{
    var endpoint = $"/api/{sessionName}/chats/{chatId}/messages/read";
    var response = await _httpClient.PostAsync(endpoint, null);
    return response.IsSuccessStatusCode;
}
```

#### Archive/Unarchive Chat
```csharp
public async Task<bool> ArchiveChatAsync(string sessionName, string chatId, bool archive = true)
{
    var action = archive ? "archive" : "unarchive";
    var endpoint = $"/api/{sessionName}/chats/{chatId}/{action}";
    var response = await _httpClient.PostAsync(endpoint, null);
    return response.IsSuccessStatusCode;
}
```

#### Delete Chat
```csharp
public async Task<bool> DeleteChatAsync(string sessionName, string chatId)
{
    var endpoint = $"/api/{sessionName}/chats/{chatId}";
    var response = await _httpClient.DeleteAsync(endpoint);
    return response.IsSuccessStatusCode;
}
```

### Message Operations

#### Delete Message
```csharp
public async Task<bool> DeleteMessageAsync(string sessionName, string chatId, string messageId)
{
    var endpoint = $"/api/{sessionName}/chats/{chatId}/messages/{messageId}";
    var response = await _httpClient.DeleteAsync(endpoint);
    return response.IsSuccessStatusCode;
}
```

#### Edit Message
```csharp
public async Task<bool> EditMessageAsync(string sessionName, string chatId, string messageId, string newText)
{
    var endpoint = $"/api/{sessionName}/chats/{chatId}/messages/{messageId}";
    var payload = new { text = newText };
    
    var response = await _httpClient.PutAsJsonAsync(endpoint, payload);
    return response.IsSuccessStatusCode;
}
```

#### Pin/Unpin Message
```csharp
public async Task<bool> PinMessageAsync(string sessionName, string chatId, string messageId, bool pin = true)
{
    var action = pin ? "pin" : "unpin";
    var endpoint = $"/api/{sessionName}/chats/{chatId}/messages/{messageId}/{action}";
    var response = await _httpClient.PostAsync(endpoint, null);
    return response.IsSuccessStatusCode;
}
```

## Group Management

### Group Operations

#### Create Group
```csharp
public async Task<GroupInfo> CreateGroupAsync(string sessionName, string groupName, List<string> participants)
{
    var endpoint = $"/api/{sessionName}/groups";
    var payload = new
    {
        name = groupName,
        participants = participants
    };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return await response.Content.ReadFromJsonAsync<GroupInfo>();
}
```

#### Get All Groups
```csharp
public async Task<List<GroupInfo>> GetGroupsAsync(string sessionName)
{
    var endpoint = $"/api/{sessionName}/groups";
    var response = await _httpClient.GetAsync(endpoint);
    return await response.Content.ReadFromJsonAsync<List<GroupInfo>>();
}
```

#### Get Group Info
```csharp
public async Task<GroupInfo> GetGroupInfoAsync(string sessionName, string groupId)
{
    var endpoint = $"/api/{sessionName}/groups/{groupId}";
    var response = await _httpClient.GetAsync(endpoint);
    
    if (!response.IsSuccessStatusCode)
        return null;
        
    return await response.Content.ReadFromJsonAsync<GroupInfo>();
}
```

#### Leave Group
```csharp
public async Task<bool> LeaveGroupAsync(string sessionName, string groupId)
{
    var endpoint = $"/api/{sessionName}/groups/{groupId}/leave";
    var response = await _httpClient.PostAsync(endpoint, null);
    return response.IsSuccessStatusCode;
}
```

### Group Settings

#### Update Group Name
```csharp
public async Task<bool> UpdateGroupNameAsync(string sessionName, string groupId, string newName)
{
    var endpoint = $"/api/{sessionName}/groups/{groupId}/subject";
    var payload = new { subject = newName };
    
    var response = await _httpClient.PutAsJsonAsync(endpoint, payload);
    return response.IsSuccessStatusCode;
}
```

#### Update Group Description
```csharp
public async Task<bool> UpdateGroupDescriptionAsync(string sessionName, string groupId, string description)
{
    var endpoint = $"/api/{sessionName}/groups/{groupId}/description";
    var payload = new { description = description };
    
    var response = await _httpClient.PutAsJsonAsync(endpoint, payload);
    return response.IsSuccessStatusCode;
}
```

#### Set Group Settings
```csharp
public async Task<bool> SetGroupSettingAsync(string sessionName, string groupId, string setting, bool value)
{
    var endpoint = $"/api/{sessionName}/groups/{groupId}/settings/security/{setting}";
    var payload = new { value = value };
    
    var response = await _httpClient.PutAsJsonAsync(endpoint, payload);
    return response.IsSuccessStatusCode;
}

// Usage examples:
// Set messages admin only: SetGroupSettingAsync(session, groupId, "messages-admin-only", true)
// Set info admin only: SetGroupSettingAsync(session, groupId, "info-admin-only", true)
```

### Participant Management

#### Get Group Participants
```csharp
public async Task<List<GroupParticipant>> GetGroupParticipantsAsync(string sessionName, string groupId)
{
    var endpoint = $"/api/{sessionName}/groups/{groupId}/participants";
    var response = await _httpClient.GetAsync(endpoint);
    return await response.Content.ReadFromJsonAsync<List<GroupParticipant>>();
}
```

#### Add Participants
```csharp
public async Task<bool> AddParticipantsAsync(string sessionName, string groupId, List<string> participants)
{
    var endpoint = $"/api/{sessionName}/groups/{groupId}/participants/add";
    var payload = new { participants = participants };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return response.IsSuccessStatusCode;
}
```

#### Remove Participants
```csharp
public async Task<bool> RemoveParticipantsAsync(string sessionName, string groupId, List<string> participants)
{
    var endpoint = $"/api/{sessionName}/groups/{groupId}/participants/remove";
    var payload = new { participants = participants };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return response.IsSuccessStatusCode;
}
```

#### Promote/Demote Admins
```csharp
public async Task<bool> PromoteParticipantsAsync(string sessionName, string groupId, List<string> participants)
{
    var endpoint = $"/api/{sessionName}/groups/{groupId}/admin/promote";
    var payload = new { participants = participants };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return response.IsSuccessStatusCode;
}

public async Task<bool> DemoteParticipantsAsync(string sessionName, string groupId, List<string> participants)
{
    var endpoint = $"/api/{sessionName}/groups/{groupId}/admin/demote";
    var payload = new { participants = participants };
    
    var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
    return response.IsSuccessStatusCode;
}
```

### Group Invite Management

#### Get Invite Code
```csharp
public async Task<string> GetGroupInviteCodeAsync(string sessionName, string groupId)
{
    var endpoint = $"/api/{sessionName}/groups/{groupId}/invite-code";
    var response = await _httpClient.GetAsync(endpoint);
    
    if (!response.IsSuccessStatusCode)
        return null;
        
    var result = await response.Content.ReadFromJsonAsync<InviteCodeResult>();
    return result?.InviteCode;
}
```

#### Revoke Invite Code
```csharp
public async Task<bool> RevokeGroupInviteCodeAsync(string sessionName, string groupId)
{
    var endpoint = $"/api/{sessionName}/groups/{groupId}/invite-code/revoke";
    var response = await _httpClient.PostAsync(endpoint, null);
    return response.IsSuccessStatusCode;
}
```

## Data Models

### Session Models

```csharp
public class SessionStatus
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } // STOPPED, STARTING, SCAN_QR_CODE, WORKING, FAILED

    [JsonPropertyName("config")]
    public SessionConfig Config { get; set; }

    [JsonPropertyName("me")]
    public ContactInfo Me { get; set; }
}

public class SessionConfig
{
    [JsonPropertyName("webhooks")]
    public List<WebhookConfig> Webhooks { get; set; } = new();

    [JsonPropertyName("proxy")]
    public ProxyConfig Proxy { get; set; }
}

public class WebhookConfig
{
    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("events")]
    public List<string> Events { get; set; } = new() { "message" };

    [JsonPropertyName("hmac")]
    public HmacConfig Hmac { get; set; }
}
```

### Message Models

```csharp
public class IncomingMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("from")]
    public string From { get; set; }

    [JsonPropertyName("fromMe")]
    public bool FromMe { get; set; }

    [JsonPropertyName("to")]
    public string To { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; set; }

    [JsonPropertyName("hasMedia")]
    public bool HasMedia { get; set; }

    [JsonPropertyName("mediaUrl")]
    public string MediaUrl { get; set; }

    [JsonPropertyName("mimetype")]
    public string MimeType { get; set; }

    [JsonPropertyName("ack")]
    public int Ack { get; set; } // 1=sent, 2=delivered, 3=read

    [JsonPropertyName("vCards")]
    public List<VCard> VCards { get; set; }

    [JsonPropertyName("location")]
    public LocationData Location { get; set; }
}

public class SendTextOptions
{
    public string ReplyTo { get; set; }
    public List<string> Mentions { get; set; }
    public bool? LinkPreview { get; set; }
}
```

### Contact Models

```csharp
public class Contact
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("shortName")]
    public string ShortName { get; set; }

    [JsonPropertyName("pushname")]
    public string Pushname { get; set; }

    [JsonPropertyName("isBusiness")]
    public bool IsBusiness { get; set; }

    [JsonPropertyName("isMyContact")]
    public bool IsMyContact { get; set; }
}

public class ContactInfo
{
    [JsonPropertyName("firstName")]
    public string FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string LastName { get; set; }

    [JsonPropertyName("middleName")]
    public string MiddleName { get; set; }

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; }

    [JsonPropertyName("suffix")]
    public string Suffix { get; set; }

    [JsonPropertyName("prefix")]
    public string Prefix { get; set; }

    [JsonPropertyName("organization")]
    public string Organization { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("phoneNumbers")]
    public List<PhoneNumber> PhoneNumbers { get; set; }

    [JsonPropertyName("emails")]
    public List<Email> Emails { get; set; }

    [JsonPropertyName("urls")]
    public List<Url> Urls { get; set; }
}
```

### Group Models

```csharp
public class GroupInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("participants")]
    public List<GroupParticipant> Participants { get; set; }

    [JsonPropertyName("owner")]
    public string Owner { get; set; }

    [JsonPropertyName("creation")]
    public long Creation { get; set; }

    [JsonPropertyName("subjectOwner")]
    public string SubjectOwner { get; set; }

    [JsonPropertyName("subjectTime")]
    public long SubjectTime { get; set; }
}

public class GroupParticipant
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("isAdmin")]
    public bool IsAdmin { get; set; }

    [JsonPropertyName("isSuperAdmin")]
    public bool IsSuperAdmin { get; set; }
}
```

## Error Handling

### Custom Exceptions

```csharp
public class WahaApiException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public Dictionary<string, object> Details { get; }

    public WahaApiException(int statusCode, string message, string errorCode = null, Dictionary<string, object> details = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Details = details ?? new Dictionary<string, object>();
    }
}
```

### Error Response Handling

```csharp
public async Task<T> HandleResponseAsync<T>(HttpResponseMessage response)
{
    if (response.IsSuccessStatusCode)
    {
        return await response.Content.ReadFromJsonAsync<T>();
    }

    var errorContent = await response.Content.ReadAsStringAsync();
    var errorResponse = JsonSerializer.Deserialize<WahaErrorResponse>(errorContent);

    throw new WahaApiException(
        (int)response.StatusCode,
        errorResponse?.Message ?? "Unknown WAHA API error",
        errorResponse?.Code,
        errorResponse?.Details
    );
}
```

### Common Error Codes

| Error Code | Description | Resolution |
|------------|-------------|------------|
| `SESSION_NOT_FOUND` | Session doesn't exist | Create or start session |
| `SESSION_NOT_WORKING` | Session not in WORKING state | Wait for QR scan or restart session |
| `INVALID_CHAT_ID` | Invalid phone number format | Use E.164 format (+1234567890) |
| `MESSAGE_NOT_SENT` | Failed to send message | Check recipient and retry |
| `MEDIA_DOWNLOAD_FAILED` | Media file unavailable | Verify URL accessibility |
| `GROUP_NOT_FOUND` | Group doesn't exist | Verify group ID |
| `PARTICIPANT_NOT_FOUND` | User not in group | Add participant first |

## Rate Limiting and Best Practices

### Implementation Guidelines

```csharp
public class WahaServiceWithRateLimit : IWahaService
{
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger<WahaServiceWithRateLimit> _logger;

    public WahaServiceWithRateLimit()
    {
        // Limit concurrent requests to prevent rate limiting
        _semaphore = new SemaphoreSlim(5, 5);
    }

    public async Task<WahaMessageResult> SendTextMessageAsync(string phoneNumber, string message)
    {
        await _semaphore.WaitAsync();
        try
        {
            // Add delay between requests if needed
            await Task.Delay(100);
            return await base.SendTextMessageAsync(phoneNumber, message);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
```

### Recommended Practices

1. **Session Management**: Always check session status before operations
2. **Error Handling**: Implement retry logic with exponential backoff
3. **Rate Limiting**: Limit concurrent requests to avoid API throttling
4. **Phone Validation**: Use E.164 format for phone numbers
5. **Media Handling**: Validate media URLs before sending
6. **Logging**: Log all API interactions for debugging
7. **Configuration**: Use environment variables for sensitive settings

This comprehensive API reference covers all major WAHA endpoints with C# implementation examples suitable for integration with the WhatsAppWaha.Core framework.