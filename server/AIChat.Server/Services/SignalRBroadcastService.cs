using AIChat.Orleans.Services;
using AIChat.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AIChat.Server.Services;

/// <summary>
/// Implementation of ISignalRBroadcastService that integrates Orleans grains with SignalR.
/// This service allows Orleans grains to broadcast messages to SignalR clients.
/// </summary>
public class SignalRBroadcastService : ISignalRBroadcastService
{
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<SignalRBroadcastService> _logger;

    /// <summary>
    /// Initializes a new instance of the SignalRBroadcastService.
    /// </summary>
    /// <param name="hubContext">SignalR hub context for ChatHub</param>
    /// <param name="logger">Logger instance</param>
    public SignalRBroadcastService(
        IHubContext<ChatHub> hubContext,
        ILogger<SignalRBroadcastService> logger
    )
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public async Task BroadcastToGroupAsync(string groupName, string methodName, object payload)
    {
        try
        {
            await _hubContext.Clients.Group(groupName).SendAsync(methodName, payload);

            _logger.LogTrace(
                "Successfully broadcasted {MethodName} to group {GroupName}",
                methodName,
                groupName
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to broadcast {MethodName} to group {GroupName}",
                methodName,
                groupName
            );
            throw;
        }
    }
}
