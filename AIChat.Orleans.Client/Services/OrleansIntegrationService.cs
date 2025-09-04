using System.Text.Json;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Orleans;
using Orleans.Runtime;

namespace AIChat.Orleans.Client.Services;

/// <summary>
/// Implementation of Orleans integration service.
/// Provides resilient, feature-flag controlled access to Orleans grains.
/// </summary>
public sealed class OrleansIntegrationService : IOrleansIntegrationService
{
    private readonly IGrainFactory _grainFactory;
    private readonly IFeatureManager _featureManager;
    private readonly ILogger<OrleansIntegrationService> _logger;

    /// <summary>
    /// Initializes a new instance of the OrleansIntegrationService.
    /// </summary>
    /// <param name="grainFactory">Orleans grain factory</param>
    /// <param name="featureManager">Feature flag manager</param>
    /// <param name="logger">Logger instance</param>
    public OrleansIntegrationService(
        IGrainFactory grainFactory,
        IFeatureManager featureManager,
        ILogger<OrleansIntegrationService> logger)
    {
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _featureManager = featureManager ?? throw new ArgumentNullException(nameof(featureManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Phase 1: Shadow Mode Operations

    /// <inheritdoc />
    public async Task RecordUserActivityAsync(string userId, ActivityType type, object data)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("RecordUserActivityAsync called with empty userId");
            return;
        }

        try
        {
            // Check if Orleans integration is enabled
            if (!await _featureManager.IsEnabledAsync("OrleansIntegration"))
            {
                _logger.LogDebug("Orleans integration disabled, skipping activity recording for {UserId}", userId);
                return;
            }

            // Execute Orleans operation
            var grain = _grainFactory.GetGrain<IUserGrain>(userId);
            var metadata = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await grain.RecordActivity(type, metadata);

            _logger.LogTrace("Activity recorded for {UserId}: {ActivityType}", userId, type);
        }
        catch (Exception ex)
        {
            // Log but don't throw in shadow mode - this should never break the main application flow
            _logger.LogWarning(ex,
                "Failed to record Orleans activity for {UserId}. Type: {ActivityType}. Error: {Error}",
                userId, type, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<UserGrainState?> GetUserStateAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("GetUserStateAsync called with empty userId");
            return null;
        }

        try
        {
            if (!await _featureManager.IsEnabledAsync("OrleansIntegration"))
            {
                _logger.LogDebug("Orleans integration disabled");
                return null;
            }

            var grain = _grainFactory.GetGrain<IUserGrain>(userId);
            var state = await grain.GetState();

            _logger.LogDebug("Retrieved state for {UserId}: {ConnectionCount} connections, {ActivityCount} activities",
                userId, state.Connections.Count, state.RecentActivity.Count);

            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user state for {UserId}: {Error}", userId, ex.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsOrleansHealthyAsync()
    {
        try
        {
            if (!await _featureManager.IsEnabledAsync("OrleansIntegration"))
            {
                return false;
            }

            // Try to activate a health check grain and verify it responds
            var healthCheckUserId = $"health-check-{DateTime.UtcNow.Ticks}";
            var grain = _grainFactory.GetGrain<IUserGrain>(healthCheckUserId);

            var healthResult = await grain.CheckHealth();

            _logger.LogDebug("Orleans health check completed. Healthy: {IsHealthy}", healthResult.IsHealthy);
            return healthResult.IsHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans health check failed: {Error}", ex.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult?> CheckUserHealthAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("CheckUserHealthAsync called with empty userId");
            return null;
        }

        try
        {
            if (!await _featureManager.IsEnabledAsync("OrleansIntegration"))
            {
                return null;
            }

            var grain = _grainFactory.GetGrain<IUserGrain>(userId);
            var healthResult = await grain.CheckHealth();

            _logger.LogDebug("Health check for {UserId} completed. Healthy: {IsHealthy}, Warnings: {WarningCount}",
                userId, healthResult.IsHealthy, healthResult.Warnings.Count);

            return healthResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check health for {UserId}: {Error}", userId, ex.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<OrleansConnectionStatus> GetConnectionStatusAsync()
    {
        var status = new OrleansConnectionStatus();

        try
        {
            if (!await _featureManager.IsEnabledAsync("OrleansIntegration"))
            {
                status.ConnectionState = "Disabled";
                status.Warnings.Add("Orleans integration is disabled via feature flag");
                return status;
            }

            // Try to get management grain to check cluster status
            var managementGrain = _grainFactory.GetGrain<IManagementGrain>(0);
            var silos = await managementGrain.GetHosts();

            status.IsConnected = true;
            status.ConnectionState = "Connected";
            status.ActiveSilos = silos.Count;
            status.ConnectedAt = DateTime.UtcNow; // Approximate
            status.LastSuccessfulOperation = DateTime.UtcNow;

            if (silos.Count == 0)
            {
                status.Warnings.Add("No active silos found in cluster");
                status.IsConnected = false;
                status.ConnectionState = "No Silos";
            }

            _logger.LogDebug("Orleans connection status: {ConnectionState}, {SiloCount} silos",
                status.ConnectionState, status.ActiveSilos);
        }
        catch (Exception ex)
        {
            status.IsConnected = false;
            status.ConnectionState = "Failed";
            status.Warnings.Add($"Connection check failed: {ex.Message}");

            _logger.LogWarning(ex, "Failed to get Orleans connection status: {Error}", ex.Message);
        }

        return status;
    }

    #endregion

    #region Phase 2: SignalR Integration (Stubbed for Phase 1)

    /// <inheritdoc />
    public async Task RegisterConnectionAsync(string userId, string connectionId, string clientId)
    {
        _logger.LogDebug("RegisterConnectionAsync called in Phase 1 (stubbed) for {UserId}: {ConnectionId}",
            userId, connectionId);

        // Phase 2 implementation will use Orleans for connection management
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task UnregisterConnectionAsync(string userId, string connectionId)
    {
        _logger.LogDebug("UnregisterConnectionAsync called in Phase 1 (stubbed) for {UserId}: {ConnectionId}",
            userId, connectionId);

        // Phase 2 implementation will use Orleans for connection management
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SubscribeToChatAsync(string userId, string connectionId, string chatId)
    {
        _logger.LogDebug("SubscribeToChatAsync called in Phase 1 (stubbed) for {UserId}: {ConnectionId} -> {ChatId}",
            userId, connectionId, chatId);

        // Phase 2 implementation will use Orleans for subscription management
        await Task.CompletedTask;
    }

    #endregion

    #region Phase 3: Background Processing (Stubbed for Phase 1)

    /// <inheritdoc />
    public async Task<string> ProcessMessageAsync(string userId, ChatMessage message)
    {
        _logger.LogDebug("ProcessMessageAsync called in Phase 1 (stubbed) for {UserId}: {MessageId}",
            userId, message.Id);

        // Phase 3 implementation will use Orleans background service
        var operationId = Guid.NewGuid().ToString();
        return await Task.FromResult(operationId);
    }

    /// <inheritdoc />
    public async Task CancelOperationAsync(string userId, string operationId)
    {
        _logger.LogDebug("CancelOperationAsync called in Phase 1 (stubbed) for {UserId}: {OperationId}",
            userId, operationId);

        // Phase 3 implementation will use Orleans for operation cancellation
        await Task.CompletedTask;
    }

    #endregion

    #region Private Helper Methods

    // TODO: Re-implement resilience patterns using correct Polly v8 API
    // Temporarily removed due to Polly v8 API compatibility issues

    #endregion
}