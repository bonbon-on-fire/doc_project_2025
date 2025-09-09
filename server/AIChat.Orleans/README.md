# AIChat.Orleans

Core Orleans grains and contracts for the AIChat application. This library provides the fundamental building blocks for the Orleans-based distributed architecture.

## Overview

This project contains:

- **Grain Interfaces**: Public contracts for Orleans grains
- **Grain Implementations**: Core business logic for user management and message processing
- **State Models**: Serializable data structures for grain persistence
- **Shared Types**: Common enums and data transfer objects

## Architecture

The Orleans integration follows a three-phase migration approach:

### Phase 1: Shadow Mode (Current Implementation)
- Orleans runs alongside existing SSE system
- User activities are tracked for monitoring
- No impact on current functionality
- Health checks and metrics collection

### Phase 2: SignalR Integration (Future)
- Active connection management
- Real-time message routing through grains
- Multi-tab synchronization support

### Phase 3: Background Processing (Future)
- Full background chat service integration
- Operation tracking and cancellation
- Complete SSE removal

## Components

### IUserGrain Interface

The primary grain interface representing a user in the chat system:

```csharp
public interface IUserGrain : IGrainWithStringKey
{
    // Phase 1: Shadow operations
    Task RecordActivity(ActivityType type, string metadata);
    Task<UserGrainState> GetState();
    Task<HealthCheckResult> CheckHealth();
    
    // Phase 2: Connection management
    Task RegisterConnection(string connectionId, string clientId);
    Task UnregisterConnection(string connectionId);
    Task SubscribeToChat(string connectionId, string chatId);
    
    // Phase 3: Message processing
    Task RelayMessage(ChatMessage message);
    Task<string> ProcessMessageWithBackground(ChatMessage message);
}
```

### UserGrain Implementation

Key features:
- **State Management**: Persistent user state with automatic cleanup
- **Activity Tracking**: Circular buffer for recent user activities (max 100)
- **Health Monitoring**: Built-in health checks with warning detection
- **Metrics Collection**: Performance counters and usage statistics
- **Timer-based Cleanup**: Automatic cleanup of stale data every 5 minutes

### State Models

#### UserGrainState
- User connections and chat subscriptions
- Recent activity history (circular buffer)
- Performance metrics and health data
- Active operations tracking (Phase 3)

#### Supporting Models
- `ConnectionInfo`: SignalR connection details
- `ChatSubscription`: Chat room subscription state
- `ActivityRecord`: User activity tracking
- `OperationContext`: Background operation state

## Usage

### Basic Grain Operations

```csharp
// Get user grain
var grain = grainFactory.GetGrain<IUserGrain>(userId);

// Record activity (Phase 1)
await grain.RecordActivity(ActivityType.MessageSent, JsonSerializer.Serialize(metadata));

// Check health
var health = await grain.CheckHealth();

// Get current state
var state = await grain.GetState();
```

### Error Handling

All grain operations include comprehensive error handling:
- Logging for diagnostics
- Non-throwing operations in shadow mode
- Graceful degradation on failures
- Health check warning system

## Configuration Requirements

This library requires Orleans packages:
- `Microsoft.Orleans.Core.Abstractions` (8.0.0)
- `Microsoft.Orleans.Serialization.Abstractions` (8.0.0)

The hosting project must configure:
- Grain storage provider
- Clustering provider
- Logging configuration

## Testing

The grain implementations are designed for testability:
- Dependency injection support
- TestCluster compatibility
- Mockable interfaces
- Isolated state management

## Future Enhancements

### Phase 2 Additions
- Active SignalR hub integration
- Connection lifecycle management
- Message routing and broadcasting
- Multi-tab synchronization

### Phase 3 Additions
- Background service coordination
- Operation queue management
- Cancellation token support
- Performance optimizations

## Best Practices

1. **State Size**: Keep grain state compact for performance
2. **Async Operations**: All methods are fully async
3. **Error Handling**: Don't throw in shadow mode operations
4. **Cleanup**: Automatic cleanup prevents memory leaks
5. **Monitoring**: Built-in health checks and metrics
6. **Serialization**: All state models use Orleans serialization attributes

## Dependencies

- .NET 9.0
- Microsoft Orleans 8.0.0
- Microsoft Extensions (Logging, Hosting)

## Contributing

When extending this library:

1. Follow SOLID principles
2. Add comprehensive XML documentation
3. Include error handling and logging
4. Add appropriate serialization attributes
5. Consider backward compatibility
6. Write appropriate tests