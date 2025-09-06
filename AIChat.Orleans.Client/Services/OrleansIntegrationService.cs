using System.Text.Json;
using AIChat.Orleans.Client.Configuration;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Orleans;
using Orleans.Runtime;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

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
    private readonly OrleansResilienceConfiguration _resilienceConfig;
    private readonly ResiliencePipeline _resiliencePipeline;

    /// <summary>
    /// Initializes a new instance of the OrleansIntegrationService.
    /// </summary>
    /// <param name="grainFactory">Orleans grain factory</param>
    /// <param name="featureManager">Feature flag manager</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="resilienceOptions">Resilience configuration options</param>
    public OrleansIntegrationService(
        IGrainFactory grainFactory,
        IFeatureManager featureManager,
        ILogger<OrleansIntegrationService> logger,
        IOptions<OrleansResilienceConfiguration> resilienceOptions)
    {
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _featureManager = featureManager ?? throw new ArgumentNullException(nameof(featureManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resilienceConfig = resilienceOptions?.Value ?? new OrleansResilienceConfiguration();

        // Initialize resilience pipeline with Polly v8 syntax
        _resiliencePipeline = CreateResiliencePipeline();
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

            // Execute Orleans operation with resilience
            await ExecuteWithResilienceAsync(async () =>
            {
                var grain = _grainFactory.GetGrain<IUserGrain>(userId);
                var metadata = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await grain.RecordActivity(type, metadata);
            }, $"RecordActivity-{userId}");

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

            var state = await ExecuteWithResilienceAsync(async () =>
            {
                var grain = _grainFactory.GetGrain<IUserGrain>(userId);
                return await grain.GetState();
            }, $"GetUserState-{userId}");

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
            var healthResult = await ExecuteWithResilienceAsync(async () =>
            {
                var grain = _grainFactory.GetGrain<IUserGrain>(healthCheckUserId);
                return await grain.CheckHealth();
            }, "OrleansHealthCheck");

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
            return new HealthCheckResult 
            { 
                IsHealthy = false, 
                GrainId = userId,
                CheckedAt = DateTime.UtcNow,
                Warnings = new List<string> { "Invalid user ID provided" }
            };
        }

        try
        {
            if (!await _featureManager.IsEnabledAsync("OrleansIntegration"))
            {
                return new HealthCheckResult 
                { 
                    IsHealthy = false, 
                    GrainId = userId,
                    CheckedAt = DateTime.UtcNow,
                    Warnings = new List<string> { "Orleans integration disabled" }
                };
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
            return new HealthCheckResult 
            { 
                IsHealthy = false, 
                GrainId = userId,
                CheckedAt = DateTime.UtcNow,
                Warnings = new List<string> { $"Orleans health check failed: {ex.Message}" }
            };
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

    /// <summary>
    /// Creates a resilience pipeline using Polly v8 with circuit breaker, retry, and timeout policies.
    /// </summary>
    /// <returns>Configured resilience pipeline</returns>
    private ResiliencePipeline CreateResiliencePipeline()
    {
        var pipelineBuilder = new ResiliencePipelineBuilder();

        // Add timeout strategy first (innermost)
        if (_resilienceConfig.Timeout.Enabled)
        {
            pipelineBuilder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(_resilienceConfig.Timeout.DefaultTimeoutSeconds),
                OnTimeout = args =>
                {
                    _logger.LogWarning("Orleans operation timed out after {Timeout}s. Operation: {Operation}",
                        _resilienceConfig.Timeout.DefaultTimeoutSeconds, args.Context.OperationKey);
                    return ValueTask.CompletedTask;
                }
            });
        }

        // Add retry strategy
        if (_resilienceConfig.RetryPolicy.Enabled)
        {
            pipelineBuilder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex =>
                {
                    // Check if this exception type should trigger retries
                    if (_resilienceConfig.RetryPolicy.RetryableExceptions.Count == 0)
                        return true;

                    var exceptionTypeName = ex.GetType().FullName ?? ex.GetType().Name;
                    return _resilienceConfig.RetryPolicy.RetryableExceptions.Contains(exceptionTypeName);
                }),
                MaxRetryAttempts = _resilienceConfig.RetryPolicy.MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(_resilienceConfig.RetryPolicy.BaseDelayMilliseconds),
                MaxDelay = TimeSpan.FromMilliseconds(_resilienceConfig.RetryPolicy.MaxDelayMilliseconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = _resilienceConfig.RetryPolicy.UseJitter,
                OnRetry = args =>
                {
                    _logger.LogWarning("Retrying Orleans operation. Attempt {Attempt}/{MaxAttempts}. Exception: {Exception}",
                        args.AttemptNumber + 1, _resilienceConfig.RetryPolicy.MaxRetryAttempts + 1, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            });
        }

        // Add circuit breaker strategy (outermost)
        if (_resilienceConfig.CircuitBreaker.Enabled)
        {
            pipelineBuilder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                FailureRatio = (double)_resilienceConfig.CircuitBreaker.FailureThreshold / 100.0,
                SamplingDuration = TimeSpan.FromSeconds(_resilienceConfig.CircuitBreaker.SamplingDurationSeconds),
                MinimumThroughput = _resilienceConfig.CircuitBreaker.MinimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(_resilienceConfig.CircuitBreaker.BreakDurationSeconds),
                OnOpened = args =>
                {
                    _logger.LogError("Orleans circuit breaker OPENED. Break duration: {BreakDuration}s",
                        _resilienceConfig.CircuitBreaker.BreakDurationSeconds);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("Orleans circuit breaker CLOSED. System recovered.");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogInformation("Orleans circuit breaker HALF-OPENED. Testing system recovery...");
                    return ValueTask.CompletedTask;
                }
            });
        }

        var pipeline = pipelineBuilder.Build();

        _logger.LogInformation("Orleans resilience pipeline created. Circuit Breaker: {CBEnabled}, Retry: {RetryEnabled}, Timeout: {TimeoutEnabled}",
            _resilienceConfig.CircuitBreaker.Enabled,
            _resilienceConfig.RetryPolicy.Enabled,
            _resilienceConfig.Timeout.Enabled);

        return pipeline;
    }

    /// <summary>
    /// Executes an Orleans operation with resilience patterns applied.
    /// </summary>
    /// <typeparam name="T">Return type of the operation</typeparam>
    /// <param name="operation">The operation to execute</param>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <returns>Result of the operation</returns>
    private async Task<T> ExecuteWithResilienceAsync<T>(Func<Task<T>> operation, string operationName)
    {
        try
        {
            return await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                _logger.LogTrace("Executing Orleans operation: {OperationName}", operationName);
                return await operation();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orleans operation failed after all resilience attempts: {OperationName}", operationName);
            throw;
        }
    }

    /// <summary>
    /// Executes an Orleans operation with resilience patterns applied (void return).
    /// </summary>
    /// <param name="operation">The operation to execute</param>
    /// <param name="operationName">Name of the operation for logging</param>
    private async Task ExecuteWithResilienceAsync(Func<Task> operation, string operationName)
    {
        try
        {
            await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                _logger.LogTrace("Executing Orleans operation: {OperationName}", operationName);
                await operation();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orleans operation failed after all resilience attempts: {OperationName}", operationName);
            throw;
        }
    }

    #endregion
}