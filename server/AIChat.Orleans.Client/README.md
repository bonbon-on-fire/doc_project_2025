# AIChat.Orleans.Client

Orleans client library for the AIChat application. Provides a clean abstraction layer for the main application to interact with Orleans grains without direct dependencies on Orleans infrastructure.

## Overview

This library provides:

- **Integration Service**: High-level service interface for Orleans operations
- **Resilience Patterns**: Circuit breaker, retry, and timeout policies
- **Feature Flag Integration**: Controlled rollout with Microsoft.FeatureManagement
- **Health Checks**: Built-in health monitoring for Orleans connectivity
- **Configuration Extensions**: Easy setup for different environments

## Key Components

### IOrleansIntegrationService

The primary interface for Orleans operations:

```csharp
public interface IOrleansIntegrationService
{
    // Phase 1: Shadow mode operations
    Task RecordUserActivityAsync(string userId, ActivityType type, object data);
    Task<UserGrainState?> GetUserStateAsync(string userId);
    Task<bool> IsOrleansHealthyAsync();
    Task<HealthCheckResult?> CheckUserHealthAsync(string userId);
    
    // Phase 2: SignalR integration (stubbed in Phase 1)
    Task RegisterConnectionAsync(string userId, string connectionId, string clientId);
    Task UnregisterConnectionAsync(string userId, string connectionId);
    Task SubscribeToChatAsync(string userId, string connectionId, string chatId);
    
    // Phase 3: Background processing (stubbed in Phase 1)
    Task<string> ProcessMessageAsync(string userId, ChatMessage message);
    Task CancelOperationAsync(string userId, string operationId);
}
```

### OrleansIntegrationService Implementation

Key features:
- **Fire-and-forget operations**: Never throws exceptions in shadow mode
- **Feature flag controlled**: Respects `OrleansIntegration` feature flag
- **Resilient**: Built-in retry, circuit breaker, and timeout policies
- **Comprehensive logging**: Detailed logging for diagnostics
- **JSON serialization**: Automatic metadata serialization

### Configuration

#### Development Setup

```csharp
// In Program.cs or Startup.cs
services.AddOrleansClient(configuration, environment);
services.AddFeatureManagement(configuration);
```

#### appsettings.json

```json
{
  "Orleans": {
    "ClusterId": "doc-chat-cluster",
    "ServiceId": "doc-chat-service",
    "GatewayPort": 30000
  },
  "FeatureManagement": {
    "OrleansIntegration": {
      "EnabledFor": [
        {
          "Name": "Percentage",
          "Parameters": {
            "Value": 10
          }
        }
      ]
    }
  }
}
```

## Usage Examples

### Recording User Activity (Shadow Mode)

```csharp
public class ChatService
{
    private readonly IOrleansIntegrationService _orleans;
    
    public async Task<ChatResponse> SendMessageAsync(SendMessageRequest request)
    {
        // Record activity in Orleans (shadow mode - won't throw)
        await _orleans.RecordUserActivityAsync(
            request.UserId,
            ActivityType.MessageSent,
            new { ChatId = request.ChatId, MessageLength = request.Message.Length });
        
        // Continue with existing SSE logic
        var response = await ProcessMessageWithSSE(request);
        
        // Record completion
        await _orleans.RecordUserActivityAsync(
            request.UserId,
            ActivityType.MessageCompleted,
            new { ChatId = request.ChatId, ResponseLength = response.Content.Length });
        
        return response;
    }
}
```

### Health Monitoring

```csharp
public class HealthController : ControllerBase
{
    private readonly IOrleansIntegrationService _orleans;
    
    [HttpGet("orleans")]
    public async Task<IActionResult> CheckOrleansHealth()
    {
        var isHealthy = await _orleans.IsOrleansHealthyAsync();
        var status = await _orleans.GetConnectionStatusAsync();
        
        if (isHealthy)
        {
            return Ok(new { Status = "Healthy", Connection = status });
        }
        else
        {
            return StatusCode(503, new { Status = "Unhealthy", Connection = status });
        }
    }
}
```

### Getting User State

```csharp
public class UserController : ControllerBase
{
    private readonly IOrleansIntegrationService _orleans;
    
    [HttpGet("users/{userId}/orleans-state")]
    public async Task<IActionResult> GetUserOrleansState(string userId)
    {
        var state = await _orleans.GetUserStateAsync(userId);
        
        if (state == null)
        {
            return NotFound("Orleans state not available");
        }
        
        return Ok(new
        {
            UserId = state.UserId,
            ConnectionCount = state.Connections.Count,
            ActiveChats = state.ActiveChats.Count,
            TotalActivities = state.Metrics.TotalActivities,
            LastActivity = state.LastActivity
        });
    }
}
```

## Resilience Features

### Circuit Breaker
- Opens when 50% of operations fail over 30 seconds
- Minimum 5 operations before triggering
- 30-second break duration

### Retry Policy
- Maximum 3 retry attempts
- Exponential backoff (1s, 2s, 4s)
- Only retries transient failures

### Timeout Policy
- 10-second timeout for all Orleans operations
- Prevents hanging operations

## Health Checks

The client includes health check integration:

```csharp
services.AddHealthChecks()
    .AddCheck<OrleansClientHealthCheck>("orleans-client");
```

Health check returns:
- **Healthy**: Orleans is connected and responsive
- **Healthy**: Orleans is disabled via feature flags
- **Unhealthy**: Orleans is enabled but not working

## Feature Flag Integration

Supports Microsoft.FeatureManagement for controlled rollout:

- `OrleansIntegration`: Master switch for all Orleans operations
- Percentage rollout support
- User-based targeting
- Real-time configuration updates

## Error Handling

### Shadow Mode Behavior
In Phase 1, all operations are fire-and-forget:
- Exceptions are caught and logged
- Main application flow continues uninterrupted
- Warnings logged for diagnostics
- Feature flags can disable Orleans entirely

### Logging Levels
- **Trace**: Successful operations
- **Debug**: Method calls and state changes  
- **Information**: Connection events and status
- **Warning**: Non-critical failures in shadow mode
- **Error**: Critical failures that indicate problems

## Performance Considerations

### Connection Management
- Single Orleans client shared across application
- Connection pooling handled by Orleans client
- Automatic reconnection on failures

### State Serialization
- JSON serialization for metadata objects
- Efficient binary serialization for grain state
- Minimal network overhead

### Memory Usage
- Client maintains minimal state
- Grain references are lightweight proxies
- No local caching of grain state

## Testing

### Unit Testing
```csharp
[Test]
public async Task RecordUserActivity_WithDisabledFeatureFlag_ShouldNotCallGrain()
{
    // Arrange
    var mockFeatureManager = new Mock<IFeatureManager>();
    mockFeatureManager.Setup(x => x.IsEnabledAsync("OrleansIntegration"))
                      .ReturnsAsync(false);
    
    var service = new OrleansIntegrationService(
        Mock.Of<IGrainFactory>(),
        mockFeatureManager.Object,
        Mock.Of<ILogger<OrleansIntegrationService>>());
    
    // Act
    await service.RecordUserActivityAsync("test-user", ActivityType.MessageSent, new { });
    
    // Assert
    // Should complete without calling grain factory
}
```

### Integration Testing
```csharp
[Test]
public async Task IntegrationTest_WithTestCluster()
{
    // Use TestCluster for integration tests
    var cluster = new TestClusterBuilder().Build();
    await cluster.DeployAsync();
    
    var service = new OrleansIntegrationService(
        cluster.GrainFactory,
        /* other dependencies */);
    
    // Test actual Orleans operations
}
```

## Migration Notes

### Phase 1 (Current)
- All operations are shadow mode
- No impact on existing functionality
- Comprehensive logging and monitoring
- Feature flag controlled rollout

### Phase 2 (Future)
- SignalR connection management through Orleans
- Real-time message routing
- Multi-tab synchronization

### Phase 3 (Future)
- Background message processing
- Operation cancellation support
- Full SSE replacement

## Dependencies

- .NET 9.0
- Microsoft Orleans 8.0.0
- Microsoft.FeatureManagement 3.0.0
- Polly 8.0.0 (resilience patterns)
- Microsoft Extensions (DI, Configuration, Logging)

## Best Practices

1. **Always use IOrleansIntegrationService**: Don't access grains directly
2. **Feature flag everything**: Use feature flags for controlled rollout
3. **Log everything**: Comprehensive logging for diagnostics
4. **Handle failures gracefully**: Never let Orleans failures break main flow
5. **Monitor health**: Use built-in health checks
6. **Test thoroughly**: Unit test with mocks, integration test with TestCluster