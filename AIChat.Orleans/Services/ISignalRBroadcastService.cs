namespace AIChat.Orleans.Services;

/// <summary>
/// Abstraction for SignalR broadcasting functionality.
/// This allows Orleans grains to broadcast messages without directly depending on SignalR.
/// </summary>
public interface ISignalRBroadcastService
{
    /// <summary>
    /// Broadcasts a message to a SignalR group.
    /// </summary>
    /// <param name="groupName">The group to broadcast to</param>
    /// <param name="methodName">The method name to invoke on clients</param>
    /// <param name="payload">The payload to send</param>
    /// <returns>Task representing the broadcast operation</returns>
    Task BroadcastToGroupAsync(string groupName, string methodName, object payload);

    /// <summary>
    /// Checks if SignalR broadcasting is available.
    /// </summary>
    /// <returns>True if SignalR is available for broadcasting</returns>
    bool IsAvailable { get; }
}

/// <summary>
/// No-op implementation of ISignalRBroadcastService for when SignalR is not available.
/// </summary>
public class NullSignalRBroadcastService : ISignalRBroadcastService
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task BroadcastToGroupAsync(string groupName, string methodName, object payload)
    {
        // No-op implementation
        return Task.CompletedTask;
    }
}