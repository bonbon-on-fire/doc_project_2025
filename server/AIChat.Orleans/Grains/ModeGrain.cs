using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AIChat.Orleans.Base;
using AIChat.Orleans.Configuration;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Metrics;
using AIChat.Orleans.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;

namespace AIChat.Orleans.Grains;

/// <summary>
/// ModeGrain implementation providing comprehensive mode management functionality.
/// Implements all mode-related operations including state management, configuration, transitions, and validation.
/// Uses Orleans native state persistence for optimal performance and consistency.
/// </summary>
public sealed class ModeGrain : TracedGrainBase<ModeGrainState>, IModeGrain, IDisposable
{
    private readonly ILogger<ModeGrain> _logger;
    private readonly IModeCacheManager _cacheManager;
    // private readonly OrleansGrainConfiguration _configuration;
    // private readonly IOrleansMetricsCollector _metricsCollector;

    private IGrainTimer? _metricsTimer;
    private IGrainTimer? _cacheEvictionTimer;
    private IGrainTimer? _scheduledTransitionTimer;
    private bool _disposed;
    // private readonly object _stateLock = new();

    // Configuration constants
    private const int MaxHistoryEntries = 1000;
    private const int MaxTransitionHistoryEntries = 500;
    private const int MaxCacheEntries = 100;
    private const int CacheCleanupIntervalMinutes = 15;
    private const int MetricsCollectionIntervalMinutes = 5;

    // JSON serializer options for caching
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // Valid model patterns for validation
    private static readonly string[] ValidModelPatterns = ["gpt-", "claude-", "gemini-", "llama-", "mistral-"];

    /// <summary>
    /// Initializes a new instance of the ModeGrain.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics</param>
    /// <param name="cacheManager">Bounded cache manager for memory-safe caching</param>
    /// <param name="configuration">Configuration for Orleans grains</param>
    /// <param name="metricsCollector">Metrics collector for performance tracking</param>
    public ModeGrain(
        ILogger<ModeGrain> logger,
        IModeCacheManager cacheManager,
        IOptionsSnapshot<OrleansGrainConfiguration> configuration,
        IOrleansMetricsCollector metricsCollector)
        : base(logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        // _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        // _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
    }

    #region Orleans Grain Lifecycle

    /// <summary>
    /// Called when the grain is activated.
    /// Sets up timers and initializes state if needed.
    /// </summary>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        using var activity = StartActivity(nameof(OnActivateAsync));

        try
        {
            await base.OnActivateAsync(cancellationToken).ConfigureAwait(false);

            // Initialize state if this is the first activation
            if (State.CreatedAtUtc == default)
            {
                State.CreatedAtUtc = DateTime.UtcNow;
                State.LastModifiedUtc = DateTime.UtcNow;
                State.Version = 0;
                State.Metadata = new ModeGrainMetadata();
                State.Performance = new ModeGrainPerformanceMetrics();
            }

            // Set up periodic timers
            SetupTimers();

            _logger.LogInformation(
                "ModeGrain {GrainId} activated. IsInitialized: {IsInitialized}, Version: {Version}",
                this.GetPrimaryKeyString(),
                State.IsInitialized,
                State.Version
            );

            CompleteActivity(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate ModeGrain {GrainId}", this.GetPrimaryKeyString());
            CompleteActivityWithError(activity, ex);
            throw;
        }
    }

    /// <summary>
    /// Called when the grain is deactivated.
    /// Cleans up timers and persists final state.
    /// </summary>
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        using var activity = StartActivity(nameof(OnDeactivateAsync));

        try
        {
            // Dispose timers
            _metricsTimer?.Dispose();
            _cacheEvictionTimer?.Dispose();
            _scheduledTransitionTimer?.Dispose();

            // Perform final state save if there are pending changes
            await WriteStateAsync().ConfigureAwait(false);

            _logger.LogInformation(
                "ModeGrain {GrainId} deactivated. Reason: {Reason}, FinalVersion: {Version}",
                this.GetPrimaryKeyString(),
                reason,
                State.Version
            );

            await base.OnDeactivateAsync(reason, cancellationToken).ConfigureAwait(false);
            CompleteActivity(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deactivate ModeGrain {GrainId}", this.GetPrimaryKeyString());
            CompleteActivityWithError(activity, ex);
            throw;
        }
    }

    #endregion

    #region IModeStateGrain Implementation

    /// <summary>
    /// Initializes a mode with the provided configuration.
    /// </summary>
    public async Task<ModeState> InitializeAsync(ModeInitRequest request, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(InitializeAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Check if already initialized
                if (State.IsInitialized)
                {
                    throw new ModeAlreadyExistsException($"Mode {this.GetPrimaryKeyString()} is already initialized");
                }

                // Validate the initialization request
                await ValidateInitializationRequestAsync(request, cancellationToken).ConfigureAwait(false);

                // Create the initial mode state
                var modeId = this.GetPrimaryKeyString();
                var now = DateTime.UtcNow;

                var modeState = new ModeState
                {
                    ModeId = modeId,
                    Name = request.Name,
                    Description = request.Description,
                    Configuration = request.Configuration,
                    Status = ModeStatus.Active,
                    IsSystem = request.IsSystem,
                    UserId = request.UserId,
                    Category = request.Category,
                    Metadata = request.Metadata ?? [],
                    CreatedAtUtc = now,
                    LastModifiedUtc = now,
                    Version = 1
                };

                // Update grain state
                State.CurrentState = modeState;
                State.IsInitialized = true;
                State.LastModifiedUtc = now;
                State.Version++;

                // Record the initialization in history
                var initEvent = new ModeChangeEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    ChangeType = ModeChangeType.Created,
                    TimestampUtc = now,
                    UserId = request.UserId,
                    Description = $"Mode '{request.Name}' initialized",
                    PreviousValue = null,
                    NewValue = JsonSerializer.Serialize(modeState, CacheJsonOptions),
                    Metadata = new()
                    {
                        ["TemplateId"] = request.TemplateId ?? "none",
                        ["IsSystem"] = request.IsSystem.ToString(),
                        ["Category"] = request.Category ?? "none"
                    }
                };

                State.ChangeHistory.Add(initEvent);
                TrimHistoryIfNeeded();

                // Persist state
                await WriteStateAsync().ConfigureAwait(false);

                // Update metrics
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "Initialize");
                State.Metadata.TotalMethodCalls++;
                State.Metadata.StatePersistenceCount++;

                _logger.LogInformation(
                    "Mode {ModeId} initialized successfully. Name: {Name}, IsSystem: {IsSystem}",
                    modeId,
                    request.Name,
                    request.IsSystem
                );

                // TODO: Collect metrics when methods are available
                // _metricsCollector.RecordModeInitialized(modeId, request.IsSystem);

                return modeState;
            }
            catch (Exception ex)
            {
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "Initialize");
                State.Metadata.ErrorCount++;
                State.Metadata.LastErrorMessage = ex.Message;
                State.Metadata.LastErrorUtc = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to initialize mode {ModeId}", this.GetPrimaryKeyString());
                throw;
            }
        });
    }

    /// <summary>
    /// Gets the current state of the mode.
    /// </summary>
    public async Task<ModeState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(GetStateAsync), async () =>
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Check if initialized
                if (!State.IsInitialized || State.CurrentState == null)
                {
                    throw new ModeNotFoundException($"Mode {this.GetPrimaryKeyString()} not found or not initialized");
                }

                // Update access metrics
                State.Metadata.TotalMethodCalls++;
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "GetState");

                _logger.LogDebug("Retrieved state for mode {ModeId}", this.GetPrimaryKeyString());

                await Task.CompletedTask;
                return State.CurrentState;
            }
            catch (Exception ex)
            {
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "GetState");
                State.Metadata.ErrorCount++;
                State.Metadata.LastErrorMessage = ex.Message;
                State.Metadata.LastErrorUtc = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to get state for mode {ModeId}", this.GetPrimaryKeyString());
                throw;
            }
        });
    }

    /// <summary>
    /// Updates mode metadata.
    /// </summary>
    public async Task<ModeState> UpdateMetadataAsync(Dictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(UpdateMetadataAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(metadata);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Check if initialized and not archived
                await EnsureModeExistsAndNotArchivedAsync().ConfigureAwait(false);

                var now = DateTime.UtcNow;
                var previousMetadata = State.CurrentState!.Metadata;
                var newMetadata = new Dictionary<string, string>(State.CurrentState.Metadata);

                // Update metadata
                foreach (var kvp in metadata)
                {
                    newMetadata[kvp.Key] = kvp.Value;
                }

                // Create updated state
                var updatedState = State.CurrentState with
                {
                    Metadata = newMetadata,
                    LastModifiedUtc = now,
                    Version = State.CurrentState.Version + 1
                };

                State.CurrentState = updatedState;
                State.LastModifiedUtc = now;
                State.Version++;

                // Record the change in history
                var changeEvent = new ModeChangeEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    ChangeType = ModeChangeType.MetadataUpdated,
                    TimestampUtc = now,
                    UserId = null, // Could be extracted from context if available
                    Description = "Mode metadata updated",
                    PreviousValue = JsonSerializer.Serialize(previousMetadata, CacheJsonOptions),
                    NewValue = JsonSerializer.Serialize(newMetadata, CacheJsonOptions),
                    Metadata = new Dictionary<string, string>
                    {
                        ["UpdatedKeys"] = string.Join(",", metadata.Keys)
                    }
                };

                State.ChangeHistory.Add(changeEvent);
                TrimHistoryIfNeeded();

                // Persist state
                await WriteStateAsync().ConfigureAwait(false);

                // Update metrics
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "UpdateMetadata");
                State.Metadata.TotalMethodCalls++;
                State.Metadata.StatePersistenceCount++;

                _logger.LogInformation(
                    "Updated metadata for mode {ModeId}. Keys: {Keys}",
                    this.GetPrimaryKeyString(),
                    string.Join(", ", metadata.Keys)
                );

                return updatedState;
            }
            catch (Exception ex)
            {
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "UpdateMetadata");
                State.Metadata.ErrorCount++;
                State.Metadata.LastErrorMessage = ex.Message;
                State.Metadata.LastErrorUtc = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to update metadata for mode {ModeId}", this.GetPrimaryKeyString());
                throw;
            }
        });
    }

    /// <summary>
    /// Archives the mode, making it read-only.
    /// </summary>
    public async Task ArchiveAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        await ExecuteWithTracing(nameof(ArchiveAsync), async () =>
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Check if exists and not already archived
                await EnsureModeExistsAsync().ConfigureAwait(false);

                if (State.CurrentState!.Status == ModeStatus.Archived)
                {
                    throw new ModeArchivedException($"Mode {this.GetPrimaryKeyString()} is already archived");
                }

                var now = DateTime.UtcNow;
                var previousStatus = State.CurrentState.Status;

                // Update state to archived
                var archivedState = State.CurrentState with
                {
                    Status = ModeStatus.Archived,
                    LastModifiedUtc = now,
                    Version = State.CurrentState.Version + 1
                };

                State.CurrentState = archivedState;
                State.LastModifiedUtc = now;
                State.Version++;

                // Record the change in history
                var archiveEvent = new ModeChangeEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    ChangeType = ModeChangeType.Archived,
                    TimestampUtc = now,
                    UserId = null,
                    Description = $"Mode archived. Reason: {reason ?? "Not specified"}",
                    PreviousValue = previousStatus.ToString(),
                    NewValue = ModeStatus.Archived.ToString(),
                    Metadata = new Dictionary<string, string>
                    {
                        ["Reason"] = reason ?? "Not specified",
                        ["ArchivedAt"] = now.ToString("O")
                    }
                };

                State.ChangeHistory.Add(archiveEvent);
                TrimHistoryIfNeeded();

                // Clear caches since mode is now read-only
                ClearAllCaches();

                // Persist state
                await WriteStateAsync().ConfigureAwait(false);

                // Update metrics
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "Archive");
                State.Metadata.TotalMethodCalls++;
                State.Metadata.StatePersistenceCount++;

                _logger.LogInformation(
                    "Archived mode {ModeId}. Reason: {Reason}",
                    this.GetPrimaryKeyString(),
                    reason ?? "Not specified"
                );

                // TODO: Collect metrics when methods are available
                // _metricsCollector.RecordModeArchived(this.GetPrimaryKeyString());
            }
            catch (Exception ex)
            {
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "Archive");
                State.Metadata.ErrorCount++;
                State.Metadata.LastErrorMessage = ex.Message;
                State.Metadata.LastErrorUtc = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to archive mode {ModeId}", this.GetPrimaryKeyString());
                throw;
            }
        });
    }

    /// <summary>
    /// Resets the mode to its default configuration.
    /// </summary>
    public async Task<ModeState> ResetToDefaultAsync(bool preserveHistory = true, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(ResetToDefaultAsync), async () =>
        {
            await EnsureModeExistsAndNotArchivedAsync().ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var now = DateTime.UtcNow;
                var currentState = State.CurrentState!;
                var previousConfig = currentState.Configuration;

                // Create default configuration based on the mode type
                var defaultConfig = new ModeConfiguration
                {
                    SystemPrompt = "You are a helpful AI assistant. Provide clear and accurate responses.",
                    Tools = ["*"],
                    EnabledFeatures = [],
                    ResponseFormat = ResponseFormat.Markdown,
                    Temperature = 0.7,
                    MaxTokens = 4000,
                    Parameters = [],
                    Constraints = null
                };

                // Validate the default configuration
                var validationResult = await ValidateConfigurationAsync(defaultConfig, cancellationToken).ConfigureAwait(false);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning(
                        "Default configuration validation failed for mode {ModeId}: {Errors}",
                        this.GetPrimaryKeyString(),
                        string.Join(", ", validationResult.Errors.Select(e => e.Message))
                    );

                    // If default config fails validation, use a minimal safe config
                    defaultConfig = new ModeConfiguration
                    {
                        SystemPrompt = "You are a helpful AI assistant.",
                        Tools = ["*"]
                    };
                }

                // Preserve history and important metadata if requested
                var preservedHistory = preserveHistory ? State.ChangeHistory.ToList() : [];
                var preservedCreatedAt = State.CreatedAtUtc;
                var preservedMetadata = State.Metadata;

                // Create the reset state
                var resetState = new ModeState
                {
                    ModeId = currentState.ModeId,
                    Name = currentState.Name,
                    Description = currentState.Description,
                    Configuration = defaultConfig,
                    Status = ModeStatus.Active,
                    CreatedAtUtc = preservedCreatedAt,
                    LastModifiedUtc = now,
                    Version = currentState.Version + 1
                };

                // Update grain state
                State.CurrentState = resetState;
                State.LastModifiedUtc = now;
                State.Version++;

                // Restore or clear history based on preserveHistory parameter
                if (preserveHistory)
                {
                    State.ChangeHistory = preservedHistory;
                    State.Metadata = preservedMetadata;
                }
                else
                {
                    State.ChangeHistory.Clear();
                    State.Metadata = new ModeGrainMetadata
                    {
                        TotalMethodCalls = 0,
                        StatePersistenceCount = 0,
                        TotalExecutionTimeMs = 0
                    };
                }

                // Record the reset in history
                var resetEvent = new ModeChangeEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    ChangeType = ModeChangeType.Reset,
                    TimestampUtc = now,
                    UserId = null, // Could be extracted from context if available
                    Description = preserveHistory ? "Mode reset to default configuration (history preserved)" : "Mode reset to default configuration (full reset)",
                    PreviousValue = JsonSerializer.Serialize(previousConfig, CacheJsonOptions),
                    NewValue = JsonSerializer.Serialize(defaultConfig, CacheJsonOptions),
                    Metadata = new Dictionary<string, string>
                    {
                        ["ResetType"] = preserveHistory ? "ConfigurationOnly" : "FullReset",
                        ["PreviousVersion"] = currentState.Version.ToString(CultureInfo.InvariantCulture),
                        ["NewVersion"] = resetState.Version.ToString(CultureInfo.InvariantCulture),
                        ["HistoryPreserved"] = preserveHistory.ToString()
                    }
                };

                State.ChangeHistory.Add(resetEvent);
                TrimHistoryIfNeeded();

                // Invalidate all caches since we've reset everything
                InvalidateAllCaches();

                // Persist state
                await WriteStateAsync().ConfigureAwait(false);

                // Update metrics
                State.Performance.SuccessfulOperations++;

                // Reset some performance metrics if doing a full reset
                if (!preserveHistory)
                {
                    State.Performance.SuccessfulOperations = 0;
                    State.Performance.FailedOperations = 0;
                    State.Performance.CacheHitRatePercent = 0;
                }

                _logger.LogInformation(
                    "Mode {ModeId} reset to default configuration. History preserved: {PreserveHistory}",
                    this.GetPrimaryKeyString(),
                    preserveHistory
                );

                return resetState;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset mode {ModeId} to default configuration", this.GetPrimaryKeyString());
                throw;
            }
            finally
            {
                stopwatch.Stop();
                State.Performance.AverageConfigurationOpTimeMs = CalculateMovingAverage(
                    State.Performance.AverageConfigurationOpTimeMs,
                    stopwatch.ElapsedMilliseconds
                );
            }
        });
    }

    /// <summary>
    /// Gets the mode change history.
    /// </summary>
    public async Task<List<ModeChangeEvent>> GetHistoryAsync(int? limit = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(GetHistoryAsync), async () =>
        {
            await EnsureModeExistsAsync().ConfigureAwait(false);

            var history = State.ChangeHistory.AsEnumerable();
            if (limit.HasValue && limit.Value > 0)
            {
                history = history.TakeLast(limit.Value);
            }

            return history.ToList();
        });
    }

    /// <summary>
    /// Performs a health check on the mode grain.
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(CheckHealthAsync), async () =>
        {
            // TODO: Implement comprehensive health check
            var result = new HealthCheckResult
            {
                IsHealthy = State.IsInitialized && State.CurrentState != null,
                GrainId = this.GetPrimaryKeyString(),
                LastActivity = State.LastModifiedUtc,
                CheckedAt = DateTime.UtcNow,
                AdditionalInfo = State.IsInitialized ? "Grain is operational" : "Grain not initialized",
                Warnings = State.Metadata.ErrorCount > 0 ? [$"Grain has {State.Metadata.ErrorCount} errors"] : []
            };

            await Task.CompletedTask;
            return result;
        });
    }

    #endregion

    #region IModeConfigurationGrain Implementation

    /// <summary>
    /// Gets the current mode configuration.
    /// </summary>
    public async Task<ModeConfiguration> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(GetConfigurationAsync), async () =>
        {
            await EnsureModeExistsAsync().ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Try to get from cache first
                var cacheKey = GenerateConfigurationCacheKey("get");
                var cachedConfig = await TryGetCachedConfigurationAsync<ModeConfiguration>(cacheKey).ConfigureAwait(false);

                if (cachedConfig != null)
                {
                    UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "GetConfiguration");
                    State.Metadata.TotalMethodCalls++;
                    return cachedConfig;
                }

                // Cache miss - get current configuration
                var configuration = State.CurrentState!.Configuration;

                // Cache the result for future requests
                await SetCachedConfigurationAsync(cacheKey, configuration, TimeSpan.FromMinutes(30)).ConfigureAwait(false);

                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "GetConfiguration");
                State.Metadata.TotalMethodCalls++;

                _logger.LogDebug("Retrieved and cached configuration for mode {ModeId}", this.GetPrimaryKeyString());

                return configuration;
            }
            catch (Exception ex)
            {
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "GetConfiguration");
                State.Metadata.ErrorCount++;
                State.Metadata.LastErrorMessage = ex.Message;
                State.Metadata.LastErrorUtc = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to get configuration for mode {ModeId}", this.GetPrimaryKeyString());
                throw;
            }
        });
    }

    /// <summary>
    /// Updates the mode configuration.
    /// </summary>
    public async Task<ModeConfiguration> UpdateConfigurationAsync(ModeConfiguration configuration, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(UpdateConfigurationAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(configuration);
            await EnsureModeExistsAndNotArchivedAsync().ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Validate the new configuration
                var validationResult = await ValidateConfigurationAsync(configuration, cancellationToken).ConfigureAwait(false);
                if (!validationResult.IsValid)
                {
                    throw new InvalidModeConfigurationException(
                        this.GetPrimaryKeyString(),
                        [.. validationResult.Errors.Select(e => e.Message)]
                    );
                }

                var now = DateTime.UtcNow;
                var previousConfig = State.CurrentState!.Configuration;

                // Create updated state with new configuration
                var updatedState = State.CurrentState with
                {
                    Configuration = configuration,
                    LastModifiedUtc = now,
                    Version = State.CurrentState.Version + 1
                };

                State.CurrentState = updatedState;
                State.LastModifiedUtc = now;
                State.Version++;

                // Record the change in history
                var changeEvent = new ModeChangeEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    ChangeType = ModeChangeType.ConfigurationUpdated,
                    TimestampUtc = now,
                    UserId = null, // Could be extracted from context if available
                    Description = "Mode configuration updated",
                    PreviousValue = JsonSerializer.Serialize(previousConfig, CacheJsonOptions),
                    NewValue = JsonSerializer.Serialize(configuration, CacheJsonOptions),
                    Metadata = new Dictionary<string, string>
                    {
                        ["SystemPromptChanged"] = (previousConfig.SystemPrompt != configuration.SystemPrompt).ToString(),
                        ["ToolsChanged"] = (!previousConfig.Tools.SequenceEqual(configuration.Tools)).ToString(),
                        ["ModelChanged"] = (previousConfig.DefaultModel != configuration.DefaultModel).ToString()
                    }
                };

                State.ChangeHistory.Add(changeEvent);
                TrimHistoryIfNeeded();

                // Invalidate configuration cache since configuration changed
                InvalidateConfigurationCache();

                // Persist state
                await WriteStateAsync().ConfigureAwait(false);

                // Update metrics
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "UpdateConfiguration");
                State.Metadata.TotalMethodCalls++;
                State.Metadata.StatePersistenceCount++;

                _logger.LogInformation(
                    "Updated configuration for mode {ModeId}. SystemPrompt: {SystemPromptLength} chars, Tools: {ToolCount}",
                    this.GetPrimaryKeyString(),
                    configuration.SystemPrompt.Length,
                    configuration.Tools.Count
                );

                return configuration;
            }
            catch (Exception ex)
            {
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "UpdateConfiguration");
                State.Metadata.ErrorCount++;
                State.Metadata.LastErrorMessage = ex.Message;
                State.Metadata.LastErrorUtc = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to update configuration for mode {ModeId}", this.GetPrimaryKeyString());
                throw;
            }
        });
    }

    /// <summary>
    /// Gets all available mode templates.
    /// </summary>
    public async Task<List<ModeTemplate>> GetAvailableModesAsync(string? category = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(GetAvailableModesAsync), async () =>
        {
            // TODO: Implement template repository access
            await Task.CompletedTask;
            return new List<ModeTemplate>();
        });
    }

    /// <summary>
    /// Gets a specific mode template by ID.
    /// </summary>
    public async Task<ModeTemplate> GetModeTemplateAsync(string templateId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(GetModeTemplateAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(templateId);
            // TODO: Implement template retrieval
            await Task.CompletedTask;

            // Return a placeholder template until implementation is complete
            return new ModeTemplate
            {
                TemplateId = templateId,
                Name = "Placeholder Template",
                Description = "Template will be implemented in next phase",
                DefaultConfiguration = new ModeConfiguration
                {
                    SystemPrompt = "You are a helpful AI assistant.",
                    Tools = ["*"]
                }
            };
        });
    }

    /// <summary>
    /// Validates a mode configuration without applying it.
    /// </summary>
    public async Task<ModeValidationResult> ValidateConfigurationAsync(ModeConfiguration configuration, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(ValidateConfigurationAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Try to get from cache first
                var cacheKey = GenerateValidationCacheKey("configuration", configuration);
                var cachedResult = await TryGetCachedValidationAsync<ModeValidationResult>(cacheKey).ConfigureAwait(false);

                if (cachedResult != null)
                {
                    UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "ValidateConfiguration");
                    State.Metadata.TotalMethodCalls++;
                    return cachedResult;
                }

                // Cache miss - perform validation
                var errors = new List<ValidationError>();
                var warnings = new List<ValidationWarning>();

                // Validate system prompt
                if (string.IsNullOrWhiteSpace(configuration.SystemPrompt))
                {
                    errors.Add(new ValidationError
                    {
                        Code = "SYSTEM_PROMPT_EMPTY",
                        Message = "System prompt cannot be empty",
                        Field = nameof(configuration.SystemPrompt)
                    });
                }
                else if (configuration.SystemPrompt.Length > 2000)
                {
                    errors.Add(new ValidationError
                    {
                        Code = "SYSTEM_PROMPT_TOO_LONG",
                        Message = "System prompt cannot exceed 2000 characters",
                        Field = nameof(configuration.SystemPrompt)
                    });
                }
                else if (configuration.SystemPrompt.Length < 10)
                {
                    warnings.Add(new ValidationWarning
                    {
                        Code = "SYSTEM_PROMPT_SHORT",
                        Message = "System prompt is very short, consider adding more context",
                        SuggestedAction = "Expand the system prompt to provide clearer instructions"
                    });
                }

                // Validate tools
                if (configuration.Tools == null || configuration.Tools.Count == 0)
                {
                    errors.Add(new ValidationError
                    {
                        Code = "NO_TOOLS_SPECIFIED",
                        Message = "At least one tool must be specified",
                        Field = nameof(configuration.Tools)
                    });
                }
                else
                {
                    // Check for duplicate tools
                    var duplicateTools = configuration.Tools.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key);
                    if (duplicateTools.Any())
                    {
                        warnings.Add(new ValidationWarning
                        {
                            Code = "DUPLICATE_TOOLS",
                            Message = $"Duplicate tools found: {string.Join(", ", duplicateTools)}",
                            SuggestedAction = "Remove duplicate tool entries"
                        });
                    }

                    // Validate tool names (basic validation)
                    var invalidTools = configuration.Tools.Where(t => string.IsNullOrWhiteSpace(t) || t.Length > 100);
                    if (invalidTools.Any())
                    {
                        errors.Add(new ValidationError
                        {
                            Code = "INVALID_TOOL_NAMES",
                            Message = "Tool names cannot be empty or exceed 100 characters",
                            Field = nameof(configuration.Tools)
                        });
                    }
                }

                // Validate temperature
                if (configuration.Temperature is < 0.0 or > 2.0)
                {
                    errors.Add(new ValidationError
                    {
                        Code = "INVALID_TEMPERATURE",
                        Message = "Temperature must be between 0.0 and 2.0",
                        Field = nameof(configuration.Temperature)
                    });
                }

                // Validate max tokens
                if (configuration.MaxTokens is < 1 or > 100000)
                {
                    errors.Add(new ValidationError
                    {
                        Code = "INVALID_MAX_TOKENS",
                        Message = "MaxTokens must be between 1 and 100,000",
                        Field = nameof(configuration.MaxTokens)
                    });
                }

                // Validate constraints if present
                if (configuration.Constraints != null)
                {
                    await ValidateConstraintsAsync(configuration.Constraints, errors, warnings);
                }

                // Check for potential security issues in system prompt
                if (ContainsPotentialSecurityIssues(configuration.SystemPrompt))
                {
                    warnings.Add(new ValidationWarning
                    {
                        Code = "POTENTIAL_SECURITY_ISSUE",
                        Message = "System prompt may contain potential security concerns",
                        SuggestedAction = "Review prompt for sensitive information or injection risks"
                    });
                }

                var isValid = errors.Count == 0;

                var result = new ModeValidationResult
                {
                    IsValid = isValid,
                    Errors = errors,
                    Warnings = warnings,
                    SuggestedFixes = errors.Count > 0 ? GenerateSuggestedFixes(errors) : []
                };

                // Cache the validation result
                var contextHash = ComputeObjectHash(configuration);
                await SetCachedValidationAsync(cacheKey, result, contextHash, TimeSpan.FromMinutes(10)).ConfigureAwait(false);

                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "ValidateConfiguration");
                State.Metadata.TotalMethodCalls++;

                _logger.LogDebug(
                    "Configuration validation for mode {ModeId}: Valid={IsValid}, Errors={ErrorCount}, Warnings={WarningCount}",
                    this.GetPrimaryKeyString(),
                    isValid,
                    errors.Count,
                    warnings.Count
                );

                return result;
            }
            catch (Exception ex)
            {
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "ValidateConfiguration");
                State.Metadata.ErrorCount++;
                State.Metadata.LastErrorMessage = ex.Message;
                State.Metadata.LastErrorUtc = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to validate configuration for mode {ModeId}", this.GetPrimaryKeyString());
                throw;
            }
        });
    }

    /// <summary>
    /// Updates the system prompt for the mode.
    /// </summary>
    public async Task<ModeConfiguration> UpdateSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(UpdateSystemPromptAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(systemPrompt);
            await EnsureModeExistsAndNotArchivedAsync().ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var now = DateTime.UtcNow;
                var currentConfig = State.CurrentState!.Configuration;
                var previousSystemPrompt = currentConfig.SystemPrompt;

                // Create updated configuration with new system prompt
                var updatedConfig = new ModeConfiguration
                {
                    SystemPrompt = systemPrompt,
                    Parameters = currentConfig.Parameters,
                    Tools = currentConfig.Tools,
                    EnabledFeatures = currentConfig.EnabledFeatures,
                    ResponseFormat = currentConfig.ResponseFormat,
                    Constraints = currentConfig.Constraints,
                    Temperature = currentConfig.Temperature,
                    MaxTokens = currentConfig.MaxTokens
                };

                // Validate the updated configuration
                var validationResult = await ValidateConfigurationAsync(updatedConfig, cancellationToken).ConfigureAwait(false);
                if (!validationResult.IsValid)
                {
                    throw new InvalidModeConfigurationException(
                        this.GetPrimaryKeyString(),
                        [.. validationResult.Errors.Select(e => e.Message)]
                    );
                }

                // Create updated state with new configuration
                var updatedState = State.CurrentState with
                {
                    Configuration = updatedConfig,
                    LastModifiedUtc = now,
                    Version = State.CurrentState.Version + 1
                };

                State.CurrentState = updatedState;
                State.LastModifiedUtc = now;
                State.Version++;

                // Record the change in history
                var changeEvent = new ModeChangeEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    ChangeType = ModeChangeType.ConfigurationUpdated,
                    TimestampUtc = now,
                    UserId = null, // Could be extracted from context if available
                    Description = "System prompt updated",
                    PreviousValue = previousSystemPrompt,
                    NewValue = systemPrompt,
                    Metadata = new Dictionary<string, string>
                    {
                        ["SystemPromptLength"] = systemPrompt.Length.ToString(CultureInfo.InvariantCulture),
                        ["PreviousLength"] = previousSystemPrompt.Length.ToString(CultureInfo.InvariantCulture),
                        ["EstimatedTokens"] = EstimateTokenCount(systemPrompt).ToString(CultureInfo.InvariantCulture)
                    }
                };

                State.ChangeHistory.Add(changeEvent);
                TrimHistoryIfNeeded();

                // Invalidate related caches since system prompt changed
                InvalidateConfigurationCache();
                InvalidatePromptCache();

                // Persist state
                await WriteStateAsync().ConfigureAwait(false);

                // Update metrics
                State.Performance.SuccessfulOperations++;

                _logger.LogInformation(
                    "System prompt updated for mode {ModeId}. New length: {Length} characters",
                    this.GetPrimaryKeyString(),
                    systemPrompt.Length
                );

                return updatedConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update system prompt for mode {ModeId}", this.GetPrimaryKeyString());
                throw;
            }
            finally
            {
                stopwatch.Stop();
                State.Performance.AverageConfigurationOpTimeMs = CalculateMovingAverage(
                    State.Performance.AverageConfigurationOpTimeMs,
                    stopwatch.ElapsedMilliseconds
                );
            }
        });
    }

    /// <summary>
    /// Updates the available tools for the mode.
    /// </summary>
    public async Task<ModeConfiguration> UpdateToolsAsync(List<string> tools, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(UpdateToolsAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(tools);
            await EnsureModeExistsAndNotArchivedAsync().ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var now = DateTime.UtcNow;
                var currentConfig = State.CurrentState!.Configuration;
                var previousTools = currentConfig.Tools.ToList();

                // Create updated configuration with new tools
                var updatedConfig = new ModeConfiguration
                {
                    SystemPrompt = currentConfig.SystemPrompt,
                    Parameters = currentConfig.Parameters,
                    Tools = [.. tools],
                    EnabledFeatures = currentConfig.EnabledFeatures,
                    ResponseFormat = currentConfig.ResponseFormat,
                    Constraints = currentConfig.Constraints,
                    Temperature = currentConfig.Temperature,
                    MaxTokens = currentConfig.MaxTokens
                };

                // Validate the updated configuration
                var validationResult = await ValidateConfigurationAsync(updatedConfig, cancellationToken).ConfigureAwait(false);
                if (!validationResult.IsValid)
                {
                    throw new InvalidModeConfigurationException(
                        this.GetPrimaryKeyString(),
                        [.. validationResult.Errors.Select(e => e.Message)]
                    );
                }

                // Create updated state with new configuration
                var updatedState = State.CurrentState with
                {
                    Configuration = updatedConfig,
                    LastModifiedUtc = now,
                    Version = State.CurrentState.Version + 1
                };

                State.CurrentState = updatedState;
                State.LastModifiedUtc = now;
                State.Version++;

                // Calculate tool changes for metadata
                var addedTools = tools.Except(previousTools).ToList();
                var removedTools = previousTools.Except(tools).ToList();

                // Record the change in history
                var changeEvent = new ModeChangeEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    ChangeType = ModeChangeType.ConfigurationUpdated,
                    TimestampUtc = now,
                    UserId = null, // Could be extracted from context if available
                    Description = "Tools configuration updated",
                    PreviousValue = string.Join(", ", previousTools),
                    NewValue = string.Join(", ", tools),
                    Metadata = new Dictionary<string, string>
                    {
                        ["ToolCount"] = tools.Count.ToString(CultureInfo.InvariantCulture),
                        ["PreviousToolCount"] = previousTools.Count.ToString(CultureInfo.InvariantCulture),
                        ["AddedTools"] = string.Join(", ", addedTools),
                        ["RemovedTools"] = string.Join(", ", removedTools),
                        ["ToolsAdded"] = addedTools.Count.ToString(CultureInfo.InvariantCulture),
                        ["ToolsRemoved"] = removedTools.Count.ToString(CultureInfo.InvariantCulture)
                    }
                };

                State.ChangeHistory.Add(changeEvent);
                TrimHistoryIfNeeded();

                // Invalidate related caches since tools changed
                InvalidateConfigurationCache();
                InvalidateValidationCache(); // Tools affect validation

                // Persist state
                await WriteStateAsync().ConfigureAwait(false);

                // Update metrics
                State.Performance.SuccessfulOperations++;

                _logger.LogInformation(
                    "Tools updated for mode {ModeId}. Tool count: {ToolCount} (Added: {Added}, Removed: {Removed})",
                    this.GetPrimaryKeyString(),
                    tools.Count,
                    addedTools.Count,
                    removedTools.Count
                );

                return updatedConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update tools for mode {ModeId}", this.GetPrimaryKeyString());
                throw;
            }
            finally
            {
                stopwatch.Stop();
                State.Performance.AverageConfigurationOpTimeMs = CalculateMovingAverage(
                    State.Performance.AverageConfigurationOpTimeMs,
                    stopwatch.ElapsedMilliseconds
                );
            }
        });
    }

    /// <summary>
    /// Gets the effective configuration merging defaults with overrides.
    /// </summary>
    public async Task<ModeConfiguration> GetEffectiveConfigurationAsync(Dictionary<string, string>? overrides = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(GetEffectiveConfigurationAsync), async () =>
        {
            await EnsureModeExistsAsync().ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // If no overrides, return cached configuration
                if (overrides == null || overrides.Count == 0)
                {
                    UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "GetEffectiveConfiguration");
                    State.Metadata.TotalMethodCalls++;
                    return State.CurrentState!.Configuration;
                }

                // Try to get from cache first
                var cacheKey = GenerateEffectiveConfigCacheKey(State.CurrentState!.Configuration, overrides);
                var cachedConfig = await TryGetCachedConfigurationAsync(cacheKey).ConfigureAwait(false);

                if (cachedConfig != null)
                {
                    UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "GetEffectiveConfiguration");
                    State.Metadata.TotalMethodCalls++;
                    return cachedConfig;
                }

                // Cache miss - compute effective configuration
                var baseConfig = State.CurrentState!.Configuration;
                var effectiveConfig = ApplyConfigurationOverrides(baseConfig, overrides);

                // Validate the effective configuration
                var validationResult = await ValidateConfigurationAsync(effectiveConfig, cancellationToken).ConfigureAwait(false);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning(
                        "Effective configuration validation failed for mode {ModeId}: {Errors}",
                        this.GetPrimaryKeyString(),
                        string.Join(", ", validationResult.Errors.Select(e => e.Message))
                    );

                    // Return base configuration if effective config is invalid
                    return baseConfig;
                }

                // Cache the effective configuration for 30 seconds
                await SetCachedConfigurationAsync(cacheKey, effectiveConfig, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "GetEffectiveConfiguration");
                State.Metadata.TotalMethodCalls++;

                _logger.LogDebug(
                    "Computed effective configuration for mode {ModeId} with {OverrideCount} overrides",
                    this.GetPrimaryKeyString(),
                    overrides.Count
                );

                return effectiveConfig;
            }
            catch (Exception ex)
            {
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "GetEffectiveConfiguration");
                State.Metadata.ErrorCount++;
                State.Metadata.LastErrorMessage = ex.Message;
                State.Metadata.LastErrorUtc = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to compute effective configuration for mode {ModeId}", this.GetPrimaryKeyString());

                // Return base configuration as fallback
                return State.CurrentState!.Configuration;
            }
        });
    }

    #endregion

    #region IModeTransitionGrain Implementation

    /// <summary>
    /// Transitions to a different mode.
    /// </summary>
    public async Task<ModeTransitionResult> TransitionToModeAsync(ModeTransitionRequest request, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(TransitionToModeAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            await EnsureModeExistsAndNotArchivedAsync().ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();
            var transitionId = Guid.NewGuid().ToString();

            try
            {
                var currentMode = State.CurrentState!;
                var sourceModeId = this.GetPrimaryKeyString();

                _logger.LogInformation(
                    "Starting transition {TransitionId} from mode {SourceModeId} to {TargetModeId}. Reason: {Reason}",
                    transitionId, sourceModeId, request.TargetModeId, request.Reason ?? "Not specified");

                // Step 1: Validate the transition request
                var validationResult = await ValidateTransitionInternalAsync(request, cancellationToken).ConfigureAwait(false);
                if (!validationResult.IsValid || !validationResult.IsAllowed)
                {
                    var error = validationResult.DisallowedReason ??
                               (validationResult.Errors.Count > 0 ? validationResult.Errors[0].Message : "Transition validation failed");

                    await RecordFailedTransitionAsync(transitionId, sourceModeId, request, error, stopwatch.Elapsed).ConfigureAwait(false);

                    return new ModeTransitionResult
                    {
                        Success = false,
                        TransitionId = transitionId,
                        Error = error,
                        Duration = stopwatch.Elapsed,
                        Warnings = [.. validationResult.Errors.Select(e => e.Message)]
                    };
                }

                // Step 2: Check transition compatibility
                var compatibilityResult = await ValidateCompatibilityInternalAsync(sourceModeId, request.TargetModeId, cancellationToken).ConfigureAwait(false);
                if (!compatibilityResult.IsCompatible)
                {
                    var error = $"Modes are not compatible. Compatibility score: {compatibilityResult.CompatibilityScore}%";
                    await RecordFailedTransitionAsync(transitionId, sourceModeId, request, error, stopwatch.Elapsed).ConfigureAwait(false);

                    return new ModeTransitionResult
                    {
                        Success = false,
                        TransitionId = transitionId,
                        Error = error,
                        Duration = stopwatch.Elapsed,
                        Warnings = compatibilityResult.Warnings
                    };
                }

                // Step 3: Get target mode configuration (this would typically come from a template or mode repository)
                var targetModeTemplate = await GetTargetModeTemplateAsync(request.TargetModeId, cancellationToken).ConfigureAwait(false);
                if (targetModeTemplate == null)
                {
                    var error = $"Target mode template '{request.TargetModeId}' not found";
                    await RecordFailedTransitionAsync(transitionId, sourceModeId, request, error, stopwatch.Elapsed).ConfigureAwait(false);

                    return new ModeTransitionResult
                    {
                        Success = false,
                        TransitionId = transitionId,
                        Error = error,
                        Duration = stopwatch.Elapsed
                    };
                }

                // Step 4: Preserve context and history if requested
                var preservedContext = new Dictionary<string, string>();
                var preservedMetadata = new Dictionary<string, string>(currentMode.Metadata);

                if (request.PreserveContext)
                {
                    preservedContext["PreviousSystemPrompt"] = currentMode.Configuration.SystemPrompt;
                    preservedContext["PreviousModel"] = currentMode.Configuration.DefaultModel ?? "unknown";
                    preservedContext["TransitionReason"] = request.Reason ?? "User initiated";
                    preservedContext["TransitionTimestamp"] = DateTime.UtcNow.ToString("O");

                    // Preserve key configuration parameters
                    foreach (var param in currentMode.Configuration.Parameters.Take(10)) // Limit to avoid bloat
                    {
                        preservedContext[$"Previous.{param.Key}"] = param.Value;
                    }
                }

                // Step 5: Create new mode state based on target template
                var newConfiguration = CreateConfigurationFromTemplate(targetModeTemplate, currentMode.Configuration, preservedContext);
                var now = DateTime.UtcNow;

                var newModeState = currentMode with
                {
                    Name = targetModeTemplate.Name,
                    Description = targetModeTemplate.Description,
                    Configuration = newConfiguration,
                    LastModifiedUtc = now,
                    Version = currentMode.Version + 1,
                    Metadata = preservedMetadata
                };

                // Step 6: Apply the transition
                var previousState = State.CurrentState;
                State.CurrentState = newModeState;
                State.LastModifiedUtc = now;
                State.Version++;

                // Step 7: Record transition in history
                var transition = new ModeTransition
                {
                    TransitionId = transitionId,
                    SourceModeId = sourceModeId,
                    TargetModeId = request.TargetModeId,
                    TimestampUtc = now,
                    UserId = request.UserId,
                    Reason = request.Reason,
                    Success = true,
                    Error = null,
                    Duration = stopwatch.Elapsed
                };

                State.TransitionHistory.Add(transition);
                TrimHistoryIfNeeded();

                // Step 8: Record change event
                var changeEvent = new ModeChangeEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    ChangeType = ModeChangeType.Transitioned,
                    TimestampUtc = now,
                    UserId = request.UserId,
                    Description = $"Transitioned from mode '{previousState?.Name ?? "Unknown"}' to '{newModeState.Name}'",
                    PreviousValue = JsonSerializer.Serialize(previousState, CacheJsonOptions),
                    NewValue = JsonSerializer.Serialize(newModeState, CacheJsonOptions),
                    Metadata = new Dictionary<string, string>
                    {
                        ["TransitionId"] = transitionId,
                        ["TargetModeId"] = request.TargetModeId,
                        ["PreserveContext"] = request.PreserveContext.ToString(),
                        ["PreserveHistory"] = request.PreserveHistory.ToString(),
                        ["CompatibilityScore"] = compatibilityResult.CompatibilityScore.ToString(CultureInfo.InvariantCulture)
                    }
                };

                State.ChangeHistory.Add(changeEvent);

                // Step 9: Invalidate caches since we've changed the mode
                InvalidateConfigurationRelatedCaches();

                // Step 10: Persist the new state
                await WriteStateAsync().ConfigureAwait(false);

                // Step 11: Update metrics
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "Transition");
                State.Performance.AverageTransitionOpTimeMs =
                    (State.Performance.AverageTransitionOpTimeMs + stopwatch.ElapsedMilliseconds) / 2.0;
                State.Metadata.TotalMethodCalls++;
                State.Metadata.StatePersistenceCount++;

                _logger.LogInformation(
                    "Successfully completed transition {TransitionId} from {SourceModeId} to {TargetModeId} in {Duration}ms",
                    transitionId, sourceModeId, request.TargetModeId, stopwatch.ElapsedMilliseconds);

                return new ModeTransitionResult
                {
                    Success = true,
                    TransitionId = transitionId,
                    NewState = newModeState,
                    PreviousState = previousState,
                    Duration = stopwatch.Elapsed,
                    Warnings = compatibilityResult.DataLossRisks
                };
            }
            catch (Exception ex)
            {
                await RecordFailedTransitionAsync(transitionId, this.GetPrimaryKeyString(), request, ex.Message, stopwatch.Elapsed).ConfigureAwait(false);

                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "Transition");
                State.Metadata.ErrorCount++;
                State.Metadata.LastErrorMessage = ex.Message;
                State.Metadata.LastErrorUtc = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to complete transition {TransitionId} from {SourceModeId} to {TargetModeId}",
                    transitionId, this.GetPrimaryKeyString(), request.TargetModeId);

                return new ModeTransitionResult
                {
                    Success = false,
                    TransitionId = transitionId,
                    Error = $"Transition failed: {ex.Message}",
                    Duration = stopwatch.Elapsed
                };
            }
        });
    }

    /// <summary>
    /// Gets the transition history for the mode.
    /// </summary>
    public async Task<List<ModeTransition>> GetTransitionHistoryAsync(int? limit = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(GetTransitionHistoryAsync), async () =>
        {
            await EnsureModeExistsAsync().ConfigureAwait(false);

            var history = State.TransitionHistory.AsEnumerable();
            if (limit.HasValue && limit.Value > 0)
            {
                history = history.TakeLast(limit.Value);
            }

            return history.ToList();
        });
    }

    /// <summary>
    /// Checks if a transition to a target mode is allowed.
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> CanTransitionAsync(string targetModeId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetModeId);
        await EnsureModeExistsAsync().ConfigureAwait(false);

        // Create a transition request for validation
        var transitionRequest = new ModeTransitionRequest
        {
            TargetModeId = targetModeId,
            Reason = "Transition feasibility check",
            PreserveContext = true, // Default to preserving context for validation
            UserId = null, // No specific user for feasibility check
            TransitionData = []
        };

        // Use existing comprehensive validation logic
        var validationResult = await ValidateTransitionInternalAsync(transitionRequest, cancellationToken).ConfigureAwait(false);

        return (validationResult.IsAllowed, validationResult.DisallowedReason);
    }

    /// <summary>
    /// Rolls back the last mode transition.
    /// </summary>
    public async Task<ModeState> RollbackTransitionAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(RollbackTransitionAsync), async () =>
        {
            await EnsureModeExistsAndNotArchivedAsync().ConfigureAwait(false);

            // Find and validate rollback target
            var rollbackTarget = FindRollbackTarget() ??
                throw new NoTransitionToRollbackException(
                    $"No valid transition found to rollback for mode {this.GetPrimaryKeyString()}");

            await ValidateRollbackPossibleAsync(rollbackTarget, cancellationToken).ConfigureAwait(false);

            // Execute rollback operation
            var rollbackId = Guid.NewGuid().ToString();
            return await ExecuteRollbackOperation(rollbackTarget, rollbackId, reason, cancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Gets available transitions from the current mode.
    /// </summary>
    public async Task<List<ModeTransitionOption>> GetAvailableTransitionsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(GetAvailableTransitionsAsync), async () =>
        {
            await EnsureModeExistsAsync().ConfigureAwait(false);

            // Get all potentially available modes
            var potentialModes = await GetPotentialTargetModesAsync(cancellationToken).ConfigureAwait(false);

            // Evaluate each potential mode for transition availability
            var availableTransitions = await EvaluatePotentialTransitions(potentialModes, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Found {Count} available transitions for mode {ModeId}",
                availableTransitions.Count, this.GetPrimaryKeyString());

            return availableTransitions;
        });
    }

    /// <summary>
    /// Applies a mode preset which may involve multiple transitions.
    /// </summary>
    public async Task<ModePresetResult> ApplyPresetAsync(string presetId, Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(ApplyPresetAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(presetId);
            await EnsureModeExistsAndNotArchivedAsync().ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();
            var appliedChanges = new List<string>();
            var warnings = new List<string>();

            try
            {
                var currentMode = State.CurrentState!;

                _logger.LogInformation(
                    "Applying preset {PresetId} to mode {ModeId}",
                    presetId, this.GetPrimaryKeyString());

                // Step 1: Get the preset template
                var presetTemplate = await GetTargetModeTemplateAsync(presetId, cancellationToken).ConfigureAwait(false);
                if (presetTemplate == null)
                {
                    return new ModePresetResult
                    {
                        Success = false,
                        Error = $"Preset '{presetId}' not found or not available"
                    };
                }

                // Step 2: Apply parameter overrides to the preset template
                var modifiedTemplate = presetTemplate;
                if (parameters?.Count > 0)
                {
                    var modifiedConfig = presetTemplate.DefaultConfiguration;
                    var mergedParameters = new Dictionary<string, string>(modifiedConfig.Parameters);

                    foreach (var (key, value) in parameters)
                    {
                        var oldValue = mergedParameters.TryGetValue(key, out var existing) ? existing : null;
                        mergedParameters[key] = value;

                        if (oldValue != value)
                        {
                            appliedChanges.Add($"Parameter '{key}': '{oldValue ?? "not set"}' → '{value}'");
                        }
                    }

                    // Apply special parameter overrides
                    if (parameters.TryGetValue("systemPrompt", out var systemPrompt))
                    {
                        modifiedConfig = new ModeConfiguration
                        {
                            SystemPrompt = systemPrompt,
                            Tools = modifiedConfig.Tools,
                            Parameters = mergedParameters,
                            EnabledFeatures = modifiedConfig.EnabledFeatures,
                            DisabledFeatures = modifiedConfig.DisabledFeatures,
                            DefaultModel = modifiedConfig.DefaultModel,
                            Constraints = modifiedConfig.Constraints,
                            Temperature = modifiedConfig.Temperature,
                            MaxTokens = modifiedConfig.MaxTokens,
                            ResponseFormat = modifiedConfig.ResponseFormat
                        };
                        appliedChanges.Add($"System prompt updated");
                    }

                    if (parameters.TryGetValue("defaultModel", out var model))
                    {
                        modifiedConfig = new ModeConfiguration
                        {
                            SystemPrompt = modifiedConfig.SystemPrompt,
                            Tools = modifiedConfig.Tools,
                            Parameters = mergedParameters,
                            EnabledFeatures = modifiedConfig.EnabledFeatures,
                            DisabledFeatures = modifiedConfig.DisabledFeatures,
                            DefaultModel = model,
                            Constraints = modifiedConfig.Constraints,
                            Temperature = modifiedConfig.Temperature,
                            MaxTokens = modifiedConfig.MaxTokens,
                            ResponseFormat = modifiedConfig.ResponseFormat
                        };
                        appliedChanges.Add($"Default model: '{modifiedConfig.DefaultModel ?? "not set"}' → '{model}'");
                    }

                    if (parameters.TryGetValue("temperature", out var tempStr) &&
                        double.TryParse(tempStr, out var temperature))
                    {
                        modifiedConfig = new ModeConfiguration
                        {
                            SystemPrompt = modifiedConfig.SystemPrompt,
                            Tools = modifiedConfig.Tools,
                            Parameters = mergedParameters,
                            EnabledFeatures = modifiedConfig.EnabledFeatures,
                            DisabledFeatures = modifiedConfig.DisabledFeatures,
                            DefaultModel = modifiedConfig.DefaultModel,
                            Constraints = modifiedConfig.Constraints,
                            Temperature = temperature,
                            MaxTokens = modifiedConfig.MaxTokens,
                            ResponseFormat = modifiedConfig.ResponseFormat
                        };
                        appliedChanges.Add($"Temperature: {modifiedConfig.Temperature ?? 0.0} → {temperature}");
                    }

                    modifiedTemplate = new ModeTemplate
                    {
                        TemplateId = presetTemplate.TemplateId,
                        Name = presetTemplate.Name,
                        Description = presetTemplate.Description,
                        Category = presetTemplate.Category,
                        DefaultConfiguration = new ModeConfiguration
                        {
                            SystemPrompt = modifiedConfig.SystemPrompt,
                            Tools = modifiedConfig.Tools,
                            Parameters = mergedParameters,
                            EnabledFeatures = modifiedConfig.EnabledFeatures,
                            DisabledFeatures = modifiedConfig.DisabledFeatures,
                            DefaultModel = modifiedConfig.DefaultModel,
                            Constraints = modifiedConfig.Constraints,
                            Temperature = modifiedConfig.Temperature,
                            MaxTokens = modifiedConfig.MaxTokens,
                            ResponseFormat = modifiedConfig.ResponseFormat
                        }
                    };
                }

                // Step 3: Check what would change by comparing configurations
                if (currentMode.Configuration.SystemPrompt != modifiedTemplate.DefaultConfiguration.SystemPrompt)
                {
                    appliedChanges.Add($"System prompt changed");
                }

                if (currentMode.Configuration.DefaultModel != modifiedTemplate.DefaultConfiguration.DefaultModel)
                {
                    appliedChanges.Add($"Model: '{currentMode.Configuration.DefaultModel ?? "not set"}' → '{modifiedTemplate.DefaultConfiguration.DefaultModel ?? "not set"}'");
                }

                if (Math.Abs((double)((currentMode.Configuration.Temperature ?? 0.0) - (modifiedTemplate.DefaultConfiguration.Temperature ?? 0.0))) > 0.01)
                {
                    appliedChanges.Add($"Temperature: {currentMode.Configuration.Temperature} → {modifiedTemplate.DefaultConfiguration.Temperature}");
                }

                // Step 4: Create transition request to apply the preset
                var transitionRequest = new ModeTransitionRequest
                {
                    TargetModeId = presetId,
                    Reason = $"Applied preset '{presetId}'",
                    PreserveContext = true,
                    PreserveHistory = true,
                    UserId = "system", // Preset application is considered a system operation
                    TransitionData = new Dictionary<string, string>
                    {
                        ["PresetId"] = presetId,
                        ["AppliedParameters"] = parameters?.Keys != null ? string.Join(", ", parameters.Keys) : string.Empty,
                        ["ParameterCount"] = (parameters?.Count ?? 0).ToString(CultureInfo.InvariantCulture)
                    }
                };

                // Step 5: Execute the transition using existing infrastructure
                var transitionResult = await TransitionToModeAsync(transitionRequest, cancellationToken).ConfigureAwait(false);

                if (!transitionResult.Success)
                {
                    return new ModePresetResult
                    {
                        Success = false,
                        Error = $"Failed to apply preset: {transitionResult.Error}",
                        Warnings = transitionResult.Warnings
                    };
                }

                // Step 6: Add any transition warnings to our warnings
                if (transitionResult.Warnings.Count > 0)
                {
                    warnings.AddRange(transitionResult.Warnings);
                }

                // Step 7: Record successful preset application
                var presetEvent = new ModeChangeEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    ChangeType = ModeChangeType.Transitioned,
                    TimestampUtc = DateTime.UtcNow,
                    UserId = "system",
                    Description = $"Applied preset '{presetId}' with {parameters?.Count ?? 0} parameter overrides",
                    PreviousValue = JsonSerializer.Serialize(currentMode, CacheJsonOptions),
                    NewValue = JsonSerializer.Serialize(State.CurrentState, CacheJsonOptions),
                    Metadata = new Dictionary<string, string>
                    {
                        ["PresetId"] = presetId,
                        ["TransitionId"] = transitionResult.TransitionId ?? "unknown",
                        ["AppliedChanges"] = string.Join("; ", appliedChanges),
                        ["ParameterOverrides"] = JsonSerializer.Serialize(parameters ?? [])
                    }
                };

                State.ChangeHistory.Add(presetEvent);
                await WriteStateAsync().ConfigureAwait(false);

                _logger.LogInformation(
                    "Successfully applied preset {PresetId} to mode {ModeId} in {Duration}ms. Applied {ChangeCount} changes",
                    presetId, this.GetPrimaryKeyString(), stopwatch.ElapsedMilliseconds, appliedChanges.Count);

                return new ModePresetResult
                {
                    Success = true,
                    NewState = transitionResult.NewState,
                    AppliedChanges = appliedChanges,
                    Warnings = warnings
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to apply preset {PresetId} to mode {ModeId}",
                    presetId, this.GetPrimaryKeyString());

                return new ModePresetResult
                {
                    Success = false,
                    Error = $"Preset application failed: {ex.Message}"
                };
            }
        });
    }

    /// <summary>
    /// Schedules a future mode transition.
    /// </summary>
    public async Task<string> ScheduleTransitionAsync(ScheduledTransitionRequest request, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(ScheduleTransitionAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            await EnsureModeExistsAndNotArchivedAsync().ConfigureAwait(false);

            var scheduleId = Guid.NewGuid().ToString();
            State.ScheduledTransitions[scheduleId] = request;

            await WriteStateAsync().ConfigureAwait(false);

            _logger.LogInformation(
                "Scheduled transition {ScheduleId} for mode {ModeId} to {TargetModeId} at {ScheduledTime}",
                scheduleId,
                this.GetPrimaryKeyString(),
                request.TargetModeId,
                request.ScheduledTimeUtc
            );

            return scheduleId;
        });
    }

    /// <summary>
    /// Cancels a scheduled transition.
    /// </summary>
    public async Task CancelScheduledTransitionAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        await ExecuteWithTracing(nameof(CancelScheduledTransitionAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(scheduleId);
            await EnsureModeExistsAsync().ConfigureAwait(false);

            if (State.ScheduledTransitions.Remove(scheduleId))
            {
                await WriteStateAsync().ConfigureAwait(false);
                _logger.LogInformation("Cancelled scheduled transition {ScheduleId} for mode {ModeId}", scheduleId, this.GetPrimaryKeyString());
            }
            else
            {
                throw new ScheduleNotFoundException(this.GetPrimaryKeyString(), scheduleId);
            }
        });
    }

    #endregion

    #region IModeValidationGrain Implementation

    /// <summary>
    /// Validates that a mode exists and is accessible.
    /// </summary>
    public async Task<ModeValidationResult> ValidateModeAsync(string modeId, string? userId = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(ValidateModeAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(modeId);

            // TODO: Implement mode validation
            await Task.CompletedTask;
            return new ModeValidationResult { IsValid = true };
        });
    }

    /// <summary>
    /// Validates a mode transition before execution.
    /// </summary>
    public async Task<TransitionValidationResult> ValidateTransitionAsync(ModeTransitionRequest request, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(ValidateTransitionAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            await EnsureModeExistsAsync().ConfigureAwait(false);

            // Use the existing comprehensive internal validation logic
            return await ValidateTransitionInternalAsync(request, cancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Gets the validation rules for the mode.
    /// </summary>
    public async Task<List<ModeValidationRule>> GetValidationRulesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(GetValidationRulesAsync), async () =>
        {
            await EnsureModeExistsAsync().ConfigureAwait(false);

            // TODO: Implement validation rules retrieval
            await Task.CompletedTask;
            return new List<ModeValidationRule>();
        });
    }

    /// <summary>
    /// Checks if a configuration meets all mode constraints.
    /// </summary>
    public async Task<ConstraintCheckResult> CheckConstraintsAsync(ModeConfiguration configuration, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(CheckConstraintsAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(configuration);
            await EnsureModeExistsAsync().ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var violations = new List<ConstraintViolation>();
                var checkedConstraints = new List<string>();

                // If no constraints are defined, all constraints are satisfied
                if (configuration.Constraints == null)
                {
                    UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "CheckConstraints");
                    State.Metadata.TotalMethodCalls++;
                    return new ConstraintCheckResult
                    {
                        AllConstraintsSatisfied = true,
                        Violations = violations,
                        CheckedConstraints = checkedConstraints
                    };
                }

                var constraints = configuration.Constraints;

                // Check maximum message length constraint
                if (constraints.MaxMessageLength.HasValue)
                {
                    checkedConstraints.Add("MaxMessageLength");
                    if (constraints.MaxMessageLength is { } maxLength && (maxLength < 1 || maxLength > 100000))
                    {
                        violations.Add(new ConstraintViolation
                        {
                            Constraint = "MaxMessageLength",
                            ActualValue = maxLength,
                            ExpectedValue = "1-100000",
                            Message = $"MaxMessageLength {constraints.MaxMessageLength.Value} is outside valid range 1-100000"
                        });
                    }
                }

                // Check maximum messages per minute constraint
                if (constraints.MaxMessagesPerMinute.HasValue)
                {
                    checkedConstraints.Add("MaxMessagesPerMinute");
                    if (constraints.MaxMessagesPerMinute is { } maxMessages && (maxMessages < 1 || maxMessages > 1000))
                    {
                        violations.Add(new ConstraintViolation
                        {
                            Constraint = "MaxMessagesPerMinute",
                            ActualValue = maxMessages,
                            ExpectedValue = "1-1000",
                            Message = $"MaxMessagesPerMinute {constraints.MaxMessagesPerMinute.Value} is outside valid range 1-1000"
                        });
                    }
                }

                // Check maximum file size constraint
                if (constraints.MaxFileSize.HasValue)
                {
                    checkedConstraints.Add("MaxFileSize");
                    if (constraints.MaxFileSize.Value < 1)
                    {
                        violations.Add(new ConstraintViolation
                        {
                            Constraint = "MaxFileSize",
                            ActualValue = constraints.MaxFileSize.Value,
                            ExpectedValue = "> 0",
                            Message = $"MaxFileSize {constraints.MaxFileSize.Value} must be greater than 0"
                        });
                    }
                    // Check for unreasonably large file sizes (> 100MB)
                    else if (constraints.MaxFileSize.Value > 100 * 1024 * 1024)
                    {
                        violations.Add(new ConstraintViolation
                        {
                            Constraint = "MaxFileSize",
                            ActualValue = constraints.MaxFileSize.Value,
                            ExpectedValue = "<= 100MB",
                            Message = $"MaxFileSize {constraints.MaxFileSize.Value} bytes exceeds maximum allowed size of 100MB"
                        });
                    }
                }

                // Check allowed file types constraint
                if (constraints.AllowedFileTypes != null && constraints.AllowedFileTypes.Count > 0)
                {
                    checkedConstraints.Add("AllowedFileTypes");
                    var invalidFileTypes = constraints.AllowedFileTypes
                        .Where(ft => string.IsNullOrWhiteSpace(ft) || ft.Length > 50)
                        .ToList();

                    if (invalidFileTypes.Count > 0)
                    {
                        violations.Add(new ConstraintViolation
                        {
                            Constraint = "AllowedFileTypes",
                            ActualValue = string.Join(", ", invalidFileTypes),
                            ExpectedValue = "Non-empty file types with max 50 characters each",
                            Message = $"Invalid file types found: {string.Join(", ", invalidFileTypes)}"
                        });
                    }
                }

                // Check required roles constraint
                if (constraints.RequiredRoles != null && constraints.RequiredRoles.Count > 0)
                {
                    checkedConstraints.Add("RequiredRoles");
                    var invalidRoles = constraints.RequiredRoles
                        .Where(role => string.IsNullOrWhiteSpace(role) || role.Length > 100)
                        .ToList();

                    if (invalidRoles.Count > 0)
                    {
                        violations.Add(new ConstraintViolation
                        {
                            Constraint = "RequiredRoles",
                            ActualValue = string.Join(", ", invalidRoles),
                            ExpectedValue = "Non-empty role names with max 100 characters each",
                            Message = $"Invalid role names found: {string.Join(", ", invalidRoles)}"
                        });
                    }
                }

                // Check time restrictions constraint
                if (constraints.TimeRestrictions != null)
                {
                    checkedConstraints.Add("TimeRestrictions");
                    // Basic validation for time restrictions structure
                    // Could be extended to validate actual time ranges, time zones, etc.
                }

                // Check model compatibility constraints
                if (!string.IsNullOrEmpty(configuration.DefaultModel))
                {
                    checkedConstraints.Add("ModelCompatibility");

                    // Check for known model patterns
                    var isValidModel = ValidModelPatterns.Any(pattern =>
                        configuration.DefaultModel.StartsWith(pattern, StringComparison.OrdinalIgnoreCase));

                    if (!isValidModel && !configuration.DefaultModel.Equals("*", StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add(new ConstraintViolation
                        {
                            Constraint = "ModelCompatibility",
                            ActualValue = configuration.DefaultModel,
                            ExpectedValue = "Supported model name or '*' for any model",
                            Message = $"Model '{configuration.DefaultModel}' is not recognized as a supported model"
                        });
                    }
                }

                // Check tool constraints
                if (configuration.Tools.Count > 0)
                {
                    checkedConstraints.Add("ToolLimits");

                    // Check for reasonable tool limits
                    if (configuration.Tools.Count > 50)
                    {
                        violations.Add(new ConstraintViolation
                        {
                            Constraint = "ToolLimits",
                            ActualValue = configuration.Tools.Count,
                            ExpectedValue = "<= 50 tools",
                            Message = $"Tool count {configuration.Tools.Count} exceeds maximum allowed limit of 50"
                        });
                    }

                    // Check for invalid tool names
                    var invalidTools = configuration.Tools
                        .Where(tool => string.IsNullOrWhiteSpace(tool) || tool.Length > 100)
                        .ToList();

                    if (invalidTools.Count > 0)
                    {
                        violations.Add(new ConstraintViolation
                        {
                            Constraint = "ToolNames",
                            ActualValue = string.Join(", ", invalidTools.Take(5)),
                            ExpectedValue = "Non-empty tool names with max 100 characters each",
                            Message = $"Invalid tool names found: {string.Join(", ", invalidTools.Take(5))}{(invalidTools.Count > 5 ? " and others" : "")}"
                        });
                    }
                }

                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "CheckConstraints");
                State.Metadata.TotalMethodCalls++;

                _logger.LogDebug(
                    "Constraint check completed for mode {ModeId}. Checked: {CheckedCount}, Violations: {ViolationCount}",
                    this.GetPrimaryKeyString(),
                    checkedConstraints.Count,
                    violations.Count
                );

                return new ConstraintCheckResult
                {
                    AllConstraintsSatisfied = violations.Count == 0,
                    Violations = violations,
                    CheckedConstraints = checkedConstraints
                };
            }
            catch (Exception ex)
            {
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "CheckConstraints");
                State.Metadata.ErrorCount++;
                State.Metadata.LastErrorMessage = ex.Message;
                State.Metadata.LastErrorUtc = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to check constraints for mode {ModeId}", this.GetPrimaryKeyString());
                throw;
            }
        });
    }

    /// <summary>
    /// Validates tool availability for the mode.
    /// </summary>
    public async Task<ToolValidationResult> ValidateToolsAsync(List<string> toolIds, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(ValidateToolsAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(toolIds);
            await EnsureModeExistsAsync().ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var validTools = new List<string>();
                var invalidTools = new List<string>();

                // If "*" is specified, all tools are allowed
                if (toolIds.Contains("*"))
                {
                    UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "ValidateTools");
                    State.Metadata.TotalMethodCalls++;

                    _logger.LogDebug("Wildcard tool access granted for mode {ModeId}", this.GetPrimaryKeyString());

                    return new ToolValidationResult
                    {
                        AllToolsValid = true,
                        ValidTools = ["*"],
                        InvalidTools = invalidTools
                    };
                }

                // Define list of commonly available tools (simulating a tool registry)
                var availableTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Basic tools
                    "search", "calculator", "calendar", "weather", "translator",

                    // Development tools
                    "code_interpreter", "python", "javascript", "sql", "regex_tester",

                    // Data tools
                    "json_validator", "csv_parser", "data_analyzer", "chart_generator",

                    // Communication tools
                    "email", "sms", "slack", "teams", "webhook",

                    // File tools
                    "file_reader", "pdf_generator", "image_processor", "zip_handler",

                    // AI/ML tools
                    "sentiment_analyzer", "text_summarizer", "language_detector", "topic_extractor",

                    // Integration tools
                    "api_caller", "database_connector", "cloud_storage", "version_control",

                    // Security tools
                    "password_generator", "hash_calculator", "encryption", "token_validator",

                    // Utility tools
                    "url_shortener", "qr_generator", "barcode_scanner", "time_converter"
                };

                // Validate each tool
                foreach (var toolId in toolIds.Distinct())
                {
                    // Skip empty or null tool IDs
                    if (string.IsNullOrWhiteSpace(toolId))
                    {
                        invalidTools.Add(toolId ?? "(null)");
                        continue;
                    }

                    // Check tool name validity
                    if (toolId.Length > 100)
                    {
                        invalidTools.Add(toolId);
                        continue;
                    }

                    // Check for invalid characters (basic validation)
                    if (!IsValidToolName(toolId))
                    {
                        invalidTools.Add(toolId);
                        continue;
                    }

                    // Check if tool is in the available tools list
                    if (availableTools.Contains(toolId))
                    {
                        validTools.Add(toolId);
                    }
                    else
                    {
                        // For tools not in the standard list, apply additional validation
                        if (IsCustomToolValid(toolId))
                        {
                            validTools.Add(toolId);
                        }
                        else
                        {
                            invalidTools.Add(toolId);
                        }
                    }
                }

                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "ValidateTools");
                State.Metadata.TotalMethodCalls++;

                _logger.LogDebug(
                    "Tool validation completed for mode {ModeId}. Valid: {ValidCount}, Invalid: {InvalidCount}",
                    this.GetPrimaryKeyString(),
                    validTools.Count,
                    invalidTools.Count
                );

                return new ToolValidationResult
                {
                    AllToolsValid = invalidTools.Count == 0,
                    ValidTools = validTools,
                    InvalidTools = invalidTools
                };
            }
            catch (Exception ex)
            {
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "ValidateTools");
                State.Metadata.ErrorCount++;
                State.Metadata.LastErrorMessage = ex.Message;
                State.Metadata.LastErrorUtc = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to validate tools for mode {ModeId}", this.GetPrimaryKeyString());
                throw;
            }
        });
    }

    /// <summary>
    /// Validates a system prompt for the mode.
    /// </summary>
    public async Task<PromptValidationResult> ValidatePromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(ValidatePromptAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(prompt);
            await EnsureModeExistsAsync().ConfigureAwait(false);

            // TODO: Implement prompt validation
            await Task.CompletedTask;
            return new PromptValidationResult
            {
                IsValid = true,
                CharacterCount = prompt.Length
            };
        });
    }

    /// <summary>
    /// Validates mode permissions for a user.
    /// </summary>
    public async Task<PermissionValidationResult> ValidatePermissionsAsync(string userId, ModeAction action, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(ValidatePermissionsAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(userId);
            await EnsureModeExistsAsync().ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Basic user validation
                if (string.IsNullOrWhiteSpace(userId) || userId.Length > 255)
                {
                    UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "ValidatePermissions");
                    State.Metadata.TotalMethodCalls++;

                    return new PermissionValidationResult
                    {
                        HasPermission = false,
                        Action = action,
                        DenialReason = "Invalid user ID format"
                    };
                }

                // Check if mode requires specific roles
                if (State.CurrentState!.Configuration.Constraints?.RequiredRoles is { Count: > 0 } requiredRoles)
                {
                    var userRoles = await GetUserRolesAsync(userId, cancellationToken).ConfigureAwait(false);

                    // Check if user has at least one of the required roles
                    var hasRequiredRole = requiredRoles.Any(role =>
                        userRoles.Contains(role, StringComparer.OrdinalIgnoreCase));

                    if (!hasRequiredRole)
                    {
                        UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "ValidatePermissions");
                        State.Metadata.TotalMethodCalls++;

                        _logger.LogDebug(
                            "Permission denied for user {UserId} on mode {ModeId}. Required roles: {RequiredRoles}, User roles: {UserRoles}",
                            userId, this.GetPrimaryKeyString(),
                            string.Join(", ", requiredRoles),
                            string.Join(", ", userRoles)
                        );

                        return new PermissionValidationResult
                        {
                            HasPermission = false,
                            Action = action,
                            DenialReason = $"User lacks required roles: {string.Join(", ", requiredRoles)}"
                        };
                    }
                }

                // Check action-specific permissions
                var hasActionPermission = await ValidateActionPermissionAsync(userId, action, cancellationToken).ConfigureAwait(false);
                if (!hasActionPermission.IsAllowed)
                {
                    UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "ValidatePermissions");
                    State.Metadata.TotalMethodCalls++;

                    return new PermissionValidationResult
                    {
                        HasPermission = false,
                        Action = action,
                        DenialReason = hasActionPermission.Reason
                    };
                }

                // Check time-based restrictions
                if (State.CurrentState.Configuration.Constraints?.TimeRestrictions != null)
                {
                    var timeAllowed = await ValidateTimeRestrictionsAsync(userId, cancellationToken).ConfigureAwait(false);
                    if (!timeAllowed.IsAllowed)
                    {
                        UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "ValidatePermissions");
                        State.Metadata.TotalMethodCalls++;

                        return new PermissionValidationResult
                        {
                            HasPermission = false,
                            Action = action,
                            DenialReason = timeAllowed.Reason ?? "Access restricted due to time constraints"
                        };
                    }
                }

                // Check rate limiting
                var rateLimitResult = await CheckRateLimitsAsync(userId, cancellationToken).ConfigureAwait(false);
                if (!rateLimitResult.IsAllowed)
                {
                    UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "ValidatePermissions");
                    State.Metadata.TotalMethodCalls++;

                    return new PermissionValidationResult
                    {
                        HasPermission = false,
                        Action = action,
                        DenialReason = rateLimitResult.Reason ?? "Rate limit exceeded"
                    };
                }

                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "ValidatePermissions");
                State.Metadata.TotalMethodCalls++;

                _logger.LogDebug(
                    "Permission granted for user {UserId} to perform {Action} on mode {ModeId}",
                    userId, action, this.GetPrimaryKeyString()
                );

                return new PermissionValidationResult
                {
                    HasPermission = true,
                    Action = action
                };
            }
            catch (Exception ex)
            {
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "ValidatePermissions");
                State.Metadata.ErrorCount++;
                State.Metadata.LastErrorMessage = ex.Message;
                State.Metadata.LastErrorUtc = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to validate permissions for user {UserId} on mode {ModeId}", userId, this.GetPrimaryKeyString());
                throw;
            }
        });
    }

    /// <summary>
    /// Performs comprehensive validation of the entire mode state.
    /// </summary>
    public async Task<StateValidationReport> ValidateStateAsync(bool deep = false, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(ValidateStateAsync), async () =>
        {
            await EnsureModeExistsAsync().ConfigureAwait(false);

            // TODO: Implement state validation
            await Task.CompletedTask;
            return new StateValidationReport
            {
                Status = ValidationStatus.Valid,
                ValidatedAtUtc = DateTime.UtcNow,
                HealthScore = 100
            };
        });
    }

    /// <summary>
    /// Validates compatibility between two modes for transition.
    /// </summary>
    public async Task<CompatibilityValidationResult> ValidateCompatibilityAsync(string sourceModeId, string targetModeId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(ValidateCompatibilityAsync), async () =>
        {
            ArgumentNullException.ThrowIfNull(sourceModeId);
            ArgumentNullException.ThrowIfNull(targetModeId);

            // TODO: Implement compatibility validation
            await Task.CompletedTask;
            return new CompatibilityValidationResult
            {
                IsCompatible = true,
                CompatibilityScore = 100
            };
        });
    }

    #endregion

    #region Cache Management Methods

    /// <summary>
    /// Generates a cache key for configuration-related operations.
    /// </summary>
    private string GenerateConfigurationCacheKey(string operation, Dictionary<string, string>? parameters = null)
    {
        var keyBuilder = new StringBuilder($"config_{operation}");

        if (State.CurrentState?.Configuration != null)
        {
            var configHash = ComputeConfigurationHash(State.CurrentState.Configuration);
            keyBuilder.Append(CultureInfo.InvariantCulture, $"_{configHash}");
        }

        if (parameters != null && parameters.Count > 0)
        {
            var paramHash = ComputeParametersHash(parameters);
            keyBuilder.Append(CultureInfo.InvariantCulture, $"_{paramHash}");
        }

        keyBuilder.Append(CultureInfo.InvariantCulture, $"_v{State.Version}");
        return keyBuilder.ToString();
    }

    /// <summary>
    /// Generates a cache key for prompt-related operations.
    /// </summary>
    private string GeneratePromptCacheKey(ModeConfiguration config, Dictionary<string, string>? parameters = null)
    {
        var keyBuilder = new StringBuilder("prompt");

        var configHash = ComputeConfigurationHash(config);
        _ = keyBuilder.Append(CultureInfo.InvariantCulture, $"_{configHash}");

        if (parameters != null && parameters.Count > 0)
        {
            var paramHash = ComputeParametersHash(parameters);
            keyBuilder.Append(CultureInfo.InvariantCulture, $"_{paramHash}");
        }

        return keyBuilder.ToString();
    }

    /// <summary>
    /// Generates a cache key for validation operations.
    /// </summary>
    private string GenerateValidationCacheKey(string validationType, object validationTarget)
    {
        var keyBuilder = new StringBuilder($"validation_{validationType}");

        var targetHash = ComputeObjectHash(validationTarget);
        keyBuilder.Append(CultureInfo.InvariantCulture, $"_{targetHash}");

        keyBuilder.Append(CultureInfo.InvariantCulture, $"_v{State.Version}");
        return keyBuilder.ToString();
    }

    /// <summary>
    /// Computes a consistent hash for a configuration object.
    /// </summary>
    private string ComputeConfigurationHash(ModeConfiguration configuration)
    {
        var configData = JsonSerializer.Serialize(configuration, CacheJsonOptions);
        return ComputeStringHash(configData);
    }

    /// <summary>
    /// Computes a hash for a dictionary of parameters.
    /// </summary>
    private string ComputeParametersHash(Dictionary<string, string> parameters)
    {
        var sortedParams = parameters.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var paramData = JsonSerializer.Serialize(sortedParams, CacheJsonOptions);
        return ComputeStringHash(paramData);
    }

    /// <summary>
    /// Computes a hash for any object.
    /// </summary>
    private string ComputeObjectHash(object obj)
    {
        var objData = JsonSerializer.Serialize(obj, CacheJsonOptions);
        return ComputeStringHash(objData);
    }

    /// <summary>
    /// Computes a SHA256 hash of a string.
    /// </summary>
    private static string ComputeStringHash(string input)
    {
        var hashBytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes)[..16]; // Take first 16 characters for readability
    }

    /// <summary>
    /// Attempts to get a cached configuration item using bounded cache manager.
    /// </summary>
    private async Task<T?> TryGetCachedConfigurationAsync<T>(string cacheKey) where T : class
    {
        try
        {
            // Use bounded cache manager for thread-safe, memory-bounded caching
            var result = _cacheManager.Get<T>(cacheKey);
            if (result != null)
            {
                State.Metadata.CacheHits++;
                _logger.LogDebug("Cache HIT for key: {CacheKey}", cacheKey);
                await Task.CompletedTask;
                return result;
            }

            // Cache miss
            State.Metadata.CacheMisses++;
            _logger.LogDebug("Cache MISS for key: {CacheKey}", cacheKey);
            await Task.CompletedTask;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve cached item for key: {CacheKey}", cacheKey);
            State.Metadata.CacheMisses++;
            await Task.CompletedTask;
            return null;
        }
    }


    /// <summary>
    /// Attempts to get a cached prompt.
    /// </summary>
    private async Task<CachedPrompt?> TryGetCachedPromptAsync(string cacheKey)
    {
        if (State.PromptCache.TryGetValue(cacheKey, out var cachedPrompt))
        {
            if (cachedPrompt.ExpiresAtUtc > DateTime.UtcNow)
            {
                // Update usage metrics
                cachedPrompt.UsageCount++;
                cachedPrompt.LastUsedUtc = DateTime.UtcNow;
                State.Metadata.CacheHits++;

                _logger.LogDebug("Prompt cache HIT for key: {CacheKey}, UsageCount: {UsageCount}", cacheKey, cachedPrompt.UsageCount);
                await Task.CompletedTask;
                return cachedPrompt;
            }
            else
            {
                // Remove expired entry
                _ = State.PromptCache.Remove(cacheKey);
                _logger.LogDebug("Removed expired prompt cache entry for key: {CacheKey}", cacheKey);
            }
        }

        // Cache miss
        State.Metadata.CacheMisses++;
        _logger.LogDebug("Prompt cache MISS for key: {CacheKey}", cacheKey);
        await Task.CompletedTask;
        return null;
    }

    /// <summary>
    /// Caches a generated prompt.
    /// </summary>
    private async Task SetCachedPromptAsync(string cacheKey, string promptText, string configHash, Dictionary<string, string>? parameters = null, TimeSpan? expiry = null)
    {
        var expiryTime = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : DateTime.UtcNow.AddMinutes(30);

        var cachedPrompt = new CachedPrompt
        {
            PromptText = promptText,
            ConfigurationHash = configHash,
            GeneratedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiryTime,
            GenerationParameters = parameters ?? [],
            EstimatedTokenCount = EstimateTokenCount(promptText),
            UsageCount = 0
        };

        State.PromptCache[cacheKey] = cachedPrompt;

        _logger.LogDebug("Cached prompt for key: {CacheKey}, TokenCount: {TokenCount}, Expiry: {Expiry}",
            cacheKey, cachedPrompt.EstimatedTokenCount, expiryTime);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Attempts to get a cached validation result.
    /// </summary>
    private async Task<T?> TryGetCachedValidationAsync<T>(string cacheKey) where T : class
    {
        if (State.ValidationCache.TryGetValue(cacheKey, out var cachedResult))
        {
            if (cachedResult.ExpiresAtUtc > DateTime.UtcNow)
            {
                try
                {
                    var result = JsonSerializer.Deserialize<T>(cachedResult.ResultData, CacheJsonOptions);
                    if (result != null)
                    {
                        // Update usage metrics
                        cachedResult.UsageCount++;
                        State.Metadata.CacheHits++;

                        _logger.LogDebug("Validation cache HIT for key: {CacheKey}, UsageCount: {UsageCount}",
                            cacheKey, cachedResult.UsageCount);
                        await Task.CompletedTask;
                        return result;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize cached validation result for key: {CacheKey}", cacheKey);
                    _ = State.ValidationCache.Remove(cacheKey);
                }
            }
            else
            {
                // Remove expired entry
                _ = State.ValidationCache.Remove(cacheKey);
                _logger.LogDebug("Removed expired validation cache entry for key: {CacheKey}", cacheKey);
            }
        }

        // Cache miss
        State.Metadata.CacheMisses++;
        _logger.LogDebug("Validation cache MISS for key: {CacheKey}", cacheKey);
        await Task.CompletedTask;
        return null;
    }

    /// <summary>
    /// Caches a validation result.
    /// </summary>
    private async Task SetCachedValidationAsync<T>(string cacheKey, T result, string contextHash, TimeSpan? expiry = null) where T : class
    {
        var expiryTime = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : DateTime.UtcNow.AddMinutes(15);

        var cachedResult = new CachedValidationResult
        {
            ResultData = JsonSerializer.Serialize(result, CacheJsonOptions),
            ResultType = typeof(T).FullName ?? typeof(T).Name,
            ContextHash = contextHash,
            ValidatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiryTime,
            IsValid = GetValidationStatus(result),
            UsageCount = 0
        };

        State.ValidationCache[cacheKey] = cachedResult;

        _logger.LogDebug("Cached validation result for key: {CacheKey}, IsValid: {IsValid}, Expiry: {Expiry}",
            cacheKey, cachedResult.IsValid, expiryTime);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Extracts validation status from a validation result object.
    /// </summary>
    private static bool GetValidationStatus(object validationResult)
    {
        return validationResult switch
        {
            ModeValidationResult mvr => mvr.IsValid,
            TransitionValidationResult tvr => tvr.IsValid,
            ConstraintCheckResult ccr => ccr.AllConstraintsSatisfied,
            ToolValidationResult tvr => tvr.AllToolsValid,
            PromptValidationResult pvr => pvr.IsValid,
            PermissionValidationResult pvr => pvr.HasPermission,
            CompatibilityValidationResult cvr => cvr.IsCompatible,
            _ => true // Default to valid for unknown types
        };
    }

    /// <summary>
    /// Invalidates all caches related to configuration changes.
    /// </summary>
    private void InvalidateConfigurationRelatedCaches()
    {
        var keysToRemove = new List<string>();

        // Invalidate configuration cache entries
        foreach (var key in State.ConfigurationCache.Keys)
        {
            if (key.StartsWith("config_", StringComparison.Ordinal) ||
                key.StartsWith("effective_", StringComparison.Ordinal))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _ = State.ConfigurationCache.Remove(key);
        }

        // Invalidate all prompt cache since configuration affects prompts
        State.PromptCache.Clear();

        // Invalidate validation cache entries that depend on configuration
        var validationKeysToRemove = State.ValidationCache.Keys
            .Where(key => key.Contains("configuration", StringComparison.OrdinalIgnoreCase) ||
                         key.Contains("constraint", StringComparison.OrdinalIgnoreCase) ||
                         key.Contains("tool", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in validationKeysToRemove)
        {
            _ = State.ValidationCache.Remove(key);
        }

        _logger.LogDebug("Invalidated {ConfigCount} configuration cache entries, all prompt cache entries, and {ValidationCount} validation cache entries",
            keysToRemove.Count, validationKeysToRemove.Count);
    }

    /// <summary>
    /// Estimates token count for a text string using a simple heuristic.
    /// </summary>
    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        // Simple estimation: roughly 4 characters per token for English text
        // This is a rough approximation - more sophisticated token counting would require actual tokenizer
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    #endregion

    #region Dynamic Prompt Generation Methods

    /// <summary>
    /// Generates a dynamic prompt based on the current configuration and optional parameters.
    /// </summary>
    public async Task<string> GeneratePromptAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(GeneratePromptAsync), async () =>
        {
            await EnsureModeExistsAsync().ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var configuration = State.CurrentState!.Configuration;

                // Try to get from cache first
                var cacheKey = GeneratePromptCacheKey(configuration, parameters);
                var cachedPrompt = await TryGetCachedPromptAsync(cacheKey).ConfigureAwait(false);

                if (cachedPrompt != null)
                {
                    State.Performance.PromptGenerationCount++;
                    UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, true, "GeneratePrompt");
                    State.Metadata.TotalMethodCalls++;
                    return cachedPrompt.PromptText;
                }

                // Cache miss - generate new prompt
                var promptText = await BuildPromptFromConfigurationAsync(configuration, parameters, cancellationToken).ConfigureAwait(false);

                // Cache the generated prompt
                var configHash = ComputeConfigurationHash(configuration);
                await SetCachedPromptAsync(cacheKey, promptText, configHash, parameters, TimeSpan.FromMinutes(30)).ConfigureAwait(false);

                // Update performance metrics
                State.Performance.PromptGenerationCount++;
                var elapsedMs = stopwatch.ElapsedMilliseconds;
                State.Performance.AveragePromptGenerationTimeMs =
                    (State.Performance.AveragePromptGenerationTimeMs * (State.Performance.PromptGenerationCount - 1) + elapsedMs) / State.Performance.PromptGenerationCount;

                UpdatePerformanceMetrics(elapsedMs, true, "GeneratePrompt");
                State.Metadata.TotalMethodCalls++;

                _logger.LogDebug("Generated prompt for mode {ModeId}, Length: {Length}, Tokens: ~{Tokens}",
                    this.GetPrimaryKeyString(), promptText.Length, EstimateTokenCount(promptText));

                return promptText;
            }
            catch (Exception ex)
            {
                UpdatePerformanceMetrics(stopwatch.ElapsedMilliseconds, false, "GeneratePrompt");
                State.Metadata.ErrorCount++;
                State.Metadata.LastErrorMessage = ex.Message;
                State.Metadata.LastErrorUtc = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to generate prompt for mode {ModeId}", this.GetPrimaryKeyString());
                throw;
            }
        });
    }

    /// <summary>
    /// Gets the effective prompt with configuration overrides applied.
    /// </summary>
    public async Task<string> GetEffectivePromptAsync(Dictionary<string, string>? overrides = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTracing(nameof(GetEffectivePromptAsync), async () =>
        {
            await EnsureModeExistsAsync().ConfigureAwait(false);

            var effectiveConfig = await GetEffectiveConfigurationInternalAsync(overrides, cancellationToken).ConfigureAwait(false);
            return await BuildPromptFromConfigurationAsync(effectiveConfig, overrides, cancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Builds a prompt from configuration using template-based generation.
    /// </summary>
    private async Task<string> BuildPromptFromConfigurationAsync(ModeConfiguration configuration, Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        var promptBuilder = new StringBuilder();

        // Start with the base system prompt
        _ = promptBuilder.AppendLine(configuration.SystemPrompt);

        // Add context section if mode has specific features enabled
        if (configuration.EnabledFeatures.Count > 0)
        {
            _ = promptBuilder.AppendLine();
            _ = promptBuilder.AppendLine("# Enabled Features");
            foreach (var feature in configuration.EnabledFeatures)
            {
                _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"- {feature}");
            }
        }

        // Add tools section
        if (configuration.Tools.Count > 0)
        {
            _ = promptBuilder.AppendLine();
            _ = promptBuilder.AppendLine("# Available Tools");

            if (configuration.Tools.Contains("*"))
            {
                _ = promptBuilder.AppendLine("- All tools are available for use");
            }
            else
            {
                foreach (var tool in configuration.Tools)
                {
                    _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"- {tool}");
                }
            }
        }

        // Add constraints section if present
        if (configuration.Constraints != null)
        {
            _ = promptBuilder.AppendLine();
            _ = promptBuilder.AppendLine("# Constraints and Guidelines");

            if (configuration.Constraints.MaxMessageLength.HasValue)
            {
                _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"- Maximum message length: {configuration.Constraints.MaxMessageLength} characters");
            }

            if (configuration.Constraints.MaxMessagesPerMinute.HasValue)
            {
                _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"- Rate limit: {configuration.Constraints.MaxMessagesPerMinute} messages per minute");
            }

            if (configuration.Constraints.AllowedFileTypes?.Count > 0)
            {
                _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"- Allowed file types: {string.Join(", ", configuration.Constraints.AllowedFileTypes)}");
            }

            if (configuration.Constraints.ContentFilter.HasValue && configuration.Constraints.ContentFilter != ContentFilterLevel.None)
            {
                _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"- Content filtering level: {configuration.Constraints.ContentFilter}");
            }
        }

        // Add response format preferences
        if (configuration.ResponseFormat.HasValue)
        {
            _ = promptBuilder.AppendLine();
            _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"# Response Format");
            _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"Prefer responses in {configuration.ResponseFormat} format.");
        }

        // Add model-specific instructions
        if (!string.IsNullOrEmpty(configuration.DefaultModel))
        {
            _ = promptBuilder.AppendLine();
            _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"# Model Configuration");
            _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"Optimized for model: {configuration.DefaultModel}");

            if (configuration.Temperature.HasValue)
            {
                _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"Temperature setting: {configuration.Temperature}");
            }

            if (configuration.MaxTokens.HasValue)
            {
                _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"Maximum response tokens: {configuration.MaxTokens}");
            }
        }

        // Inject custom parameters if provided
        if (parameters != null && parameters.Count > 0)
        {
            _ = promptBuilder.AppendLine();
            _ = promptBuilder.AppendLine("# Context Parameters");

            foreach (var param in parameters)
            {
                _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"- {param.Key}: {param.Value}");
            }
        }

        // Add mode metadata context
        if (State.CurrentState!.Metadata.Count > 0)
        {
            _ = promptBuilder.AppendLine();
            _ = promptBuilder.AppendLine("# Mode Context");

            foreach (var metadata in State.CurrentState.Metadata.Take(5)) // Limit to avoid prompt bloat
            {
                _ = promptBuilder.AppendLine(CultureInfo.InvariantCulture, $"- {metadata.Key}: {metadata.Value}");
            }
        }

        // Apply template parameter substitution
        var finalPrompt = await ApplyParameterSubstitutionAsync(promptBuilder.ToString(), parameters, cancellationToken).ConfigureAwait(false);

        return finalPrompt.Trim();
    }

    /// <summary>
    /// Applies parameter substitution to the prompt template.
    /// </summary>
    private async Task<string> ApplyParameterSubstitutionAsync(string template, Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        if (parameters == null || parameters.Count == 0)
        {
            await Task.CompletedTask;
            return template;
        }

        var result = template;

        // Apply parameter substitution using {{parameter}} syntax
        foreach (var param in parameters)
        {
            var placeholder = $"{{{{{param.Key}}}}}";
            result = result.Replace(placeholder, param.Value, StringComparison.OrdinalIgnoreCase);
        }

        // Apply parameter substitution using {parameter} syntax
        foreach (var param in parameters)
        {
            var placeholder = $"{{{param.Key}}}";
            result = result.Replace(placeholder, param.Value, StringComparison.OrdinalIgnoreCase);
        }

        await Task.CompletedTask;
        return result;
    }

    /// <summary>
    /// Gets the effective configuration with overrides applied (internal implementation).
    /// </summary>
    private async Task<ModeConfiguration> GetEffectiveConfigurationInternalAsync(Dictionary<string, string>? overrides = null, CancellationToken cancellationToken = default)
    {
        var baseConfig = State.CurrentState!.Configuration;

        if (overrides == null || overrides.Count == 0)
        {
            await Task.CompletedTask;
            return baseConfig;
        }

        // Create a copy of the base configuration with overrides applied
        var effectiveConfig = new ModeConfiguration
        {
            SystemPrompt = GetOverrideValue(overrides, "SystemPrompt", baseConfig.SystemPrompt),
            Parameters = new Dictionary<string, string>(baseConfig.Parameters),
            Tools = [.. baseConfig.Tools],
            EnabledFeatures = [.. baseConfig.EnabledFeatures],
            DisabledFeatures = [.. baseConfig.DisabledFeatures],
            DefaultModel = GetOverrideValue(overrides, "DefaultModel", baseConfig.DefaultModel),
            Constraints = baseConfig.Constraints, // Constraints typically shouldn't be overridden
            Temperature = GetOverrideDoubleValue(overrides, "Temperature", baseConfig.Temperature),
            MaxTokens = GetOverrideIntValue(overrides, "MaxTokens", baseConfig.MaxTokens),
            ResponseFormat = GetOverrideEnumValue(overrides, "ResponseFormat", baseConfig.ResponseFormat)
        };

        // Apply parameter overrides
        foreach (var param in overrides.Where(kvp => kvp.Key.StartsWith("Param.", StringComparison.OrdinalIgnoreCase)))
        {
            var paramName = param.Key[6..]; // Remove "Param." prefix
            effectiveConfig.Parameters[paramName] = param.Value;
        }

        await Task.CompletedTask;
        return effectiveConfig;
    }

    /// <summary>
    /// Gets an override value or returns the default.
    /// </summary>
    private static string GetOverrideValue(Dictionary<string, string> overrides, string key, string? defaultValue)
    {
        return overrides.TryGetValue(key, out var value) ? value : defaultValue ?? string.Empty;
    }

    /// <summary>
    /// Gets an override double value or returns the default.
    /// </summary>
    private static double? GetOverrideDoubleValue(Dictionary<string, string> overrides, string key, double? defaultValue)
    {
        if (overrides.TryGetValue(key, out var value) && double.TryParse(value, out var doubleValue))
        {
            return doubleValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Gets an override int value or returns the default.
    /// </summary>
    private static int? GetOverrideIntValue(Dictionary<string, string> overrides, string key, int? defaultValue)
    {
        if (overrides.TryGetValue(key, out var value) && int.TryParse(value, out var intValue))
        {
            return intValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Gets an override enum value or returns the default.
    /// </summary>
    private static T? GetOverrideEnumValue<T>(Dictionary<string, string> overrides, string key, T? defaultValue) where T : struct, Enum
    {
        if (overrides.TryGetValue(key, out var value) && Enum.TryParse<T>(value, true, out var enumValue))
        {
            return enumValue;
        }
        return defaultValue;
    }

    #endregion

    #region Transition Support Methods

    /// <summary>
    /// Validates a transition request internally with comprehensive checks.
    /// </summary>
    private async Task<TransitionValidationResult> ValidateTransitionInternalAsync(ModeTransitionRequest request, CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();
        var unmetConditions = new List<string>();

        // Basic validation
        if (string.IsNullOrWhiteSpace(request.TargetModeId))
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_TARGET_MODE",
                Message = "Target mode ID cannot be empty"
            });
        }

        // Check if transitioning to self
        if (request.TargetModeId == this.GetPrimaryKeyString())
        {
            errors.Add(new ValidationError
            {
                Code = "SELF_TRANSITION",
                Message = "Cannot transition to the same mode"
            });
        }

        // Check mode status constraints
        if (State.CurrentState!.Status == ModeStatus.Archived)
        {
            errors.Add(new ValidationError
            {
                Code = "ARCHIVED_MODE",
                Message = "Cannot transition from an archived mode"
            });
        }

        // Validate user permissions if provided
        if (!string.IsNullOrEmpty(request.UserId))
        {
            var hasPermission = await ValidateUserPermissionAsync(request.UserId, ModeAction.Transition, cancellationToken).ConfigureAwait(false);
            if (!hasPermission)
            {
                errors.Add(new ValidationError
                {
                    Code = "INSUFFICIENT_PERMISSIONS",
                    Message = "User does not have permission to perform mode transitions"
                });
            }
        }

        // Check constraints for transitions
        if (State.CurrentState.Configuration.Constraints?.RequiredRoles?.Count > 0)
        {
            unmetConditions.Add("Required roles not validated - role validation not implemented");
        }

        var isValid = errors.Count == 0;
        var isAllowed = isValid && unmetConditions.Count == 0;

        await Task.CompletedTask;
        return new TransitionValidationResult
        {
            IsValid = isValid,
            IsAllowed = isAllowed,
            DisallowedReason = !isAllowed ? (errors.FirstOrDefault()?.Message ?? unmetConditions.FirstOrDefault()) : null,
            Errors = errors,
            UnmetConditions = unmetConditions,
            Impact = new TransitionImpact
            {
                AddedFeatures = [], // Would be populated based on target mode analysis
                RemovedFeatures = [],
                ConfigurationChanges = ["Mode transition will change system prompt and available tools"],
                UserExperienceImpact = "User interface and available capabilities may change"
            }
        };
    }

    /// <summary>
    /// Validates compatibility between two modes internally.
    /// </summary>
    private async Task<CompatibilityValidationResult> ValidateCompatibilityInternalAsync(string sourceModeId, string targetModeId, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var dataLossRisks = new List<string>();
        var incompatibilities = new List<Incompatibility>();

        // Basic compatibility checks
        var compatibilityScore = 85; // Default score, would be calculated based on actual mode analysis

        // Check for potential data loss
        if (State.CurrentState!.Configuration.Parameters.Count > 0)
        {
            dataLossRisks.Add("Custom configuration parameters may be lost during transition");
        }

        if (State.CurrentState.Configuration.EnabledFeatures.Count > 0)
        {
            dataLossRisks.Add("Currently enabled features may not be available in target mode");
        }

        // Add warnings for common transition issues
        warnings.Add("Mode transition will invalidate cached prompts and configurations");

        if (State.CurrentState.Configuration.Tools.Count > 10)
        {
            warnings.Add("Large number of tools may affect transition performance");
        }

        await Task.CompletedTask;
        return new CompatibilityValidationResult
        {
            IsCompatible = compatibilityScore >= 70, // Compatibility threshold
            CompatibilityScore = compatibilityScore,
            Incompatibilities = incompatibilities,
            Warnings = warnings,
            DataLossRisks = dataLossRisks
        };
    }

    /// <summary>
    /// Gets the target mode template for transition.
    /// </summary>
    private async Task<ModeTemplate?> GetTargetModeTemplateAsync(string targetModeId, CancellationToken cancellationToken = default)
    {
        // In a full implementation, this would query a mode template repository
        // For now, return a basic template based on the target mode ID
        await Task.CompletedTask;

        // Basic templates for common mode types
        return targetModeId.ToLowerInvariant() switch
        {
            "coding" or "development" => new ModeTemplate
            {
                TemplateId = targetModeId,
                Name = "Coding Assistant",
                Description = "Optimized for software development tasks",
                DefaultConfiguration = new ModeConfiguration
                {
                    SystemPrompt = "You are an expert software developer. Help with coding tasks, debugging, and technical questions.",
                    Tools = ["*"],
                    EnabledFeatures = ["code_analysis", "debugging", "documentation"],
                    ResponseFormat = ResponseFormat.Code,
                    Temperature = 0.3
                }
            },
            "creative" or "writing" => new ModeTemplate
            {
                TemplateId = targetModeId,
                Name = "Creative Writing",
                Description = "Optimized for creative and writing tasks",
                DefaultConfiguration = new ModeConfiguration
                {
                    SystemPrompt = "You are a creative writing assistant. Help with storytelling, poetry, and creative content.",
                    Tools = ["text_analysis", "grammar_check"],
                    EnabledFeatures = ["creative_mode", "style_analysis"],
                    ResponseFormat = ResponseFormat.Markdown,
                    Temperature = 0.8
                }
            },
            "research" or "analysis" => new ModeTemplate
            {
                TemplateId = targetModeId,
                Name = "Research Assistant",
                Description = "Optimized for research and analytical tasks",
                DefaultConfiguration = new ModeConfiguration
                {
                    SystemPrompt = "You are a research assistant. Provide thorough analysis and fact-based responses.",
                    Tools = ["web_search", "data_analysis", "citation_tools"],
                    EnabledFeatures = ["fact_checking", "source_validation"],
                    ResponseFormat = ResponseFormat.Markdown,
                    Temperature = 0.4
                }
            },
            _ => new ModeTemplate
            {
                TemplateId = targetModeId,
                Name = "General Assistant",
                Description = "General purpose AI assistant mode",
                DefaultConfiguration = new ModeConfiguration
                {
                    SystemPrompt = "You are a helpful AI assistant. Provide clear and accurate responses.",
                    Tools = ["*"],
                    EnabledFeatures = [],
                    ResponseFormat = ResponseFormat.Markdown,
                    Temperature = 0.7
                }
            }
        };
    }

    /// <summary>
    /// Creates a new configuration from a template while preserving relevant context.
    /// </summary>
    private ModeConfiguration CreateConfigurationFromTemplate(ModeTemplate template, ModeConfiguration currentConfig, Dictionary<string, string> preservedContext)
    {
        var newConfig = template.DefaultConfiguration;

        // Merge preserved context into parameters
        var mergedParameters = new Dictionary<string, string>(newConfig.Parameters);
        foreach (var context in preservedContext)
        {
            mergedParameters[context.Key] = context.Value;
        }

        // Create new configuration with preserved elements
        return new ModeConfiguration
        {
            SystemPrompt = newConfig.SystemPrompt,
            Parameters = mergedParameters,
            Tools = [.. newConfig.Tools],
            EnabledFeatures = [.. newConfig.EnabledFeatures],
            DisabledFeatures = [.. newConfig.DisabledFeatures],
            DefaultModel = newConfig.DefaultModel ?? currentConfig.DefaultModel,
            Constraints = currentConfig.Constraints, // Preserve constraints
            Temperature = newConfig.Temperature ?? currentConfig.Temperature,
            MaxTokens = newConfig.MaxTokens ?? currentConfig.MaxTokens,
            ResponseFormat = newConfig.ResponseFormat ?? currentConfig.ResponseFormat
        };
    }

    /// <summary>
    /// Records a failed transition in the history.
    /// </summary>
    private async Task RecordFailedTransitionAsync(string transitionId, string sourceModeId, ModeTransitionRequest request, string error, TimeSpan duration)
    {
        var transition = new ModeTransition
        {
            TransitionId = transitionId,
            SourceModeId = sourceModeId,
            TargetModeId = request.TargetModeId,
            TimestampUtc = DateTime.UtcNow,
            UserId = request.UserId,
            Reason = request.Reason,
            Success = false,
            Error = error,
            Duration = duration
        };

        State.TransitionHistory.Add(transition);
        TrimHistoryIfNeeded();

        await WriteStateAsync().ConfigureAwait(false);

        _logger.LogWarning("Recorded failed transition {TransitionId}: {Error}", transitionId, error);
    }

    /// <summary>
    /// Validates user permissions for a specific action.
    /// </summary>
    private async Task<bool> ValidateUserPermissionAsync(string userId, ModeAction action, CancellationToken cancellationToken = default)
    {
        // In a full implementation, this would check user permissions against a permission service
        // For now, assume all users have basic permissions
        await Task.CompletedTask;
        return true; // Simplified - would implement actual permission checking
    }

    /// <summary>
    /// Gets potential target modes for transition.
    /// This is a placeholder implementation until mode template repository is available.
    /// </summary>
    private async Task<List<PotentialModeInfo>> GetPotentialTargetModesAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        // Common mode types that would typically be available in a chat system
        List<PotentialModeInfo> potentialModes =
        [
            new()
            {
                ModeId = "chat-assistant",
                Name = "Chat Assistant",
                Description = "General purpose conversational assistant",
                Category = "General",
                Features = ["conversation", "general-knowledge", "problem-solving"],
                RequiredPermissions = [],
                EstimatedTransitionTimeMs = 500
            },
            new()
            {
                ModeId = "code-assistant",
                Name = "Code Assistant",
                Description = "Programming and development focused assistant",
                Category = "Development",
                Features = ["code-generation", "debugging", "code-review", "documentation"],
                RequiredPermissions = ["code"],
                EstimatedTransitionTimeMs = 750
            },
            new()
            {
                ModeId = "creative-writer",
                Name = "Creative Writer",
                Description = "Creative writing and content generation",
                Category = "Creative",
                Features = ["creative-writing", "storytelling", "content-creation"],
                RequiredPermissions = [],
                EstimatedTransitionTimeMs = 600
            },
            new()
            {
                ModeId = "data-analyst",
                Name = "Data Analyst",
                Description = "Data analysis and visualization assistant",
                Category = "Analytics",
                Features = ["data-analysis", "visualization", "statistics"],
                RequiredPermissions = ["data"],
                EstimatedTransitionTimeMs = 800
            },
            new()
            {
                ModeId = "research-assistant",
                Name = "Research Assistant",
                Description = "Research and information gathering assistant",
                Category = "Research",
                Features = ["research", "fact-checking", "citations", "summarization"],
                RequiredPermissions = [],
                EstimatedTransitionTimeMs = 650
            }
        ];

        // Filter out current mode to avoid self-transition suggestions
        var currentModeId = this.GetPrimaryKeyString();
        return [.. potentialModes.Where(m => m.ModeId != currentModeId)];
    }

    /// <summary>
    /// Finds the most recent successful transition that can be rolled back to.
    /// </summary>
    private ModeTransition? FindRollbackTarget()
    {
        // Look for the most recent successful transition (excluding rollbacks)
        var validTransitions = State.TransitionHistory
            .Where(t => t.Success &&
                       !(t.Reason?.StartsWith("Rollback:", StringComparison.OrdinalIgnoreCase) ?? false)) // Exclude rollback operations
            .OrderByDescending(t => t.TimestampUtc)
            .ToList();

        return validTransitions.FirstOrDefault();
    }

    /// <summary>
    /// Validates that a rollback operation is possible.
    /// </summary>
    private async Task ValidateRollbackPossibleAsync(ModeTransition rollbackTarget, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        // Check if we're not in an archived state
        if (State.CurrentState?.Status == ModeStatus.Archived)
        {
            throw new ModeArchivedException($"Cannot rollback an archived mode {this.GetPrimaryKeyString()}");
        }

        // Check if the rollback target has a valid source mode to rollback to
        if (string.IsNullOrEmpty(rollbackTarget.SourceModeId))
        {
            throw new InvalidOperationException("Rollback target does not have a valid source mode");
        }

        // Check if rollback target is too old (optional safety check)
        var maxRollbackAge = TimeSpan.FromDays(30); // Configurable limit
        if (rollbackTarget.TimestampUtc < DateTime.UtcNow - maxRollbackAge)
        {
            throw new InvalidOperationException($"Cannot rollback to transition older than {maxRollbackAge.TotalDays} days");
        }

        // Ensure we're not trying to rollback to ourselves
        if (rollbackTarget.SourceModeId == this.GetPrimaryKeyString())
        {
            throw new InvalidOperationException("Cannot rollback to the same mode");
        }

        _logger.LogDebug("Rollback validation passed for transition {TransitionId}", rollbackTarget.TransitionId);
    }

    /// <summary>
    /// Restores the previous mode state by performing a transition to the source mode.
    /// </summary>
    private async Task<ModeState> RestorePreviousStateAsync(ModeTransition rollbackTarget, CancellationToken cancellationToken = default)
    {
        // Since we don't have the exact previous state stored, we'll perform a new transition
        // back to the source mode of the last transition. This achieves the rollback effect.
        var rollbackRequest = new ModeTransitionRequest
        {
            TargetModeId = rollbackTarget.SourceModeId,
            Reason = $"Rollback transition to {rollbackTarget.SourceModeId}",
            PreserveContext = true,
            PreserveHistory = true,
            UserId = "system",
            TransitionData = new Dictionary<string, string>
            {
                ["RollbackOperation"] = "true",
                ["OriginalTransitionId"] = rollbackTarget.TransitionId,
                ["RollbackTimestamp"] = DateTime.UtcNow.ToString("O")
            }
        };

        // Perform the reverse transition using existing infrastructure
        // Note: This will call TransitionToModeAsync, which will validate and execute the transition
        var transitionResult = await TransitionToModeAsync(rollbackRequest, cancellationToken).ConfigureAwait(false);

        if (!transitionResult.Success)
        {
            throw new InvalidOperationException($"Failed to execute rollback transition: {transitionResult.Error}");
        }

        // Add rollback metadata to the current state
        if (State.CurrentState != null)
        {
            State.CurrentState.Metadata["LastRollbackFromTransition"] = rollbackTarget.TransitionId;
            State.CurrentState.Metadata["RollbackTimestamp"] = DateTime.UtcNow.ToString("O");
            State.CurrentState.Metadata["RollbackReason"] = "Rollback operation completed";
        }

        _logger.LogDebug("Successfully performed rollback transition from {TransitionId} for mode {ModeId}",
            rollbackTarget.TransitionId, this.GetPrimaryKeyString());

        return State.CurrentState!;
    }

    /// <summary>
    /// Executes the rollback operation with proper error handling and state management.
    /// </summary>
    private async Task<ModeState> ExecuteRollbackOperation(ModeTransition rollbackTarget, string rollbackId, string? reason, CancellationToken cancellationToken)
    {
        try
        {
            // Restore the previous mode state
            var restoredState = await RestorePreviousStateAsync(rollbackTarget, cancellationToken).ConfigureAwait(false);

            // Record successful rollback
            await RecordRollbackResult(rollbackTarget, rollbackId, reason, true, restoredState, null).ConfigureAwait(false);

            _logger.LogInformation("Successfully rolled back mode {ModeId} to previous state from transition {TransitionId}",
                this.GetPrimaryKeyString(), rollbackTarget.TransitionId);

            return restoredState;
        }
        catch (Exception ex)
        {
            // Record failed rollback
            await RecordRollbackResult(rollbackTarget, rollbackId, reason, false, null, ex).ConfigureAwait(false);

            _logger.LogError(ex, "Failed to rollback mode {ModeId} to transition {TransitionId}",
                this.GetPrimaryKeyString(), rollbackTarget.TransitionId);

            throw;
        }
    }

    /// <summary>
    /// Records the result of a rollback operation (success or failure).
    /// </summary>
    private async Task RecordRollbackResult(ModeTransition rollbackTarget, string rollbackId, string? reason, bool success, ModeState? restoredState, Exception? error)
    {
        // Create and record transition
        var rollbackTransition = CreateRollbackTransition(rollbackTarget, rollbackId, reason, success, error);
        State.TransitionHistory.Add(rollbackTransition);

        // Create and record change event (only for successful rollbacks)
        if (success && restoredState != null)
        {
            var rollbackEvent = CreateRollbackChangeEvent(rollbackTarget, rollbackId, reason, restoredState);
            State.ChangeHistory.Add(rollbackEvent);
        }

        // Update performance metrics
        if (success)
        {
            State.Performance.SuccessfulOperations++;
        }
        else
        {
            State.Performance.FailedOperations++;
        }

        // Persist the state
        await WriteStateAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a ModeTransition record for rollback operations.
    /// </summary>
    private ModeTransition CreateRollbackTransition(ModeTransition rollbackTarget, string rollbackId, string? reason, bool success, Exception? error)
    {
        return new ModeTransition
        {
            TransitionId = rollbackId,
            SourceModeId = this.GetPrimaryKeyString(),
            TargetModeId = rollbackTarget.SourceModeId,
            TimestampUtc = DateTime.UtcNow,
            Success = success,
            Reason = success
                ? $"Rollback: {reason ?? "Manual rollback"} (from transition {rollbackTarget.TransitionId})"
                : $"Failed rollback: {reason ?? "Manual rollback"} (from transition {rollbackTarget.TransitionId})",
            Error = error?.Message,
            Duration = TimeSpan.Zero, // Rollback is typically fast
            UserId = "system" // Rollback is typically a system operation
        };
    }

    /// <summary>
    /// Creates a ModeChangeEvent record for successful rollback operations.
    /// </summary>
    private ModeChangeEvent CreateRollbackChangeEvent(ModeTransition rollbackTarget, string rollbackId, string? reason, ModeState restoredState)
    {
        return new ModeChangeEvent
        {
            EventId = Guid.NewGuid().ToString(),
            ChangeType = ModeChangeType.RolledBack,
            TimestampUtc = DateTime.UtcNow,
            UserId = "system", // Rollback is typically a system operation
            Description = $"Rolled back to previous state from transition {rollbackTarget.TransitionId}",
            PreviousValue = State.CurrentState?.Name ?? "Unknown",
            NewValue = restoredState.Name,
            Metadata = new Dictionary<string, string>
            {
                ["RollbackTransitionId"] = rollbackId,
                ["OriginalTransitionId"] = rollbackTarget.TransitionId,
                ["RollbackReason"] = reason ?? "Manual rollback"
            }
        };
    }

    /// <summary>
    /// Evaluates potential modes for transition availability and creates transition options.
    /// </summary>
    private async Task<List<ModeTransitionOption>> EvaluatePotentialTransitions(List<PotentialModeInfo> potentialModes, CancellationToken cancellationToken)
    {
        var availableTransitions = new List<ModeTransitionOption>();

        foreach (var mode in potentialModes)
        {
            var transitionOption = await EvaluateTransitionForMode(mode, cancellationToken).ConfigureAwait(false);
            if (transitionOption != null)
            {
                availableTransitions.Add(transitionOption);
            }
        }

        return availableTransitions;
    }

    /// <summary>
    /// Evaluates a single mode for transition availability.
    /// </summary>
    private async Task<ModeTransitionOption?> EvaluateTransitionForMode(PotentialModeInfo mode, CancellationToken cancellationToken)
    {
        try
        {
            var (canTransition, reason) = await CanTransitionAsync(mode.ModeId, cancellationToken).ConfigureAwait(false);

            if (canTransition)
            {
                return CreateModeTransitionOption(mode);
            }

            // Optionally, we could also return disallowed transitions with reasons
            // This would be useful for UI to show why certain transitions are not available
            return null;
        }
        catch (Exception ex)
        {
            // Log but don't fail the entire operation for one mode
            _logger.LogWarning(ex, "Failed to evaluate transition to mode {ModeId}. Mode will be excluded from available transitions.", mode.ModeId);
            return null;
        }
    }

    /// <summary>
    /// Creates a ModeTransitionOption from a PotentialModeInfo.
    /// </summary>
    private static ModeTransitionOption CreateModeTransitionOption(PotentialModeInfo mode)
    {
        return new ModeTransitionOption
        {
            TargetModeId = mode.ModeId,
            TargetModeName = mode.Name,
            Description = mode.Description,
            IsAllowed = true, // We already filtered for allowed transitions
            DisallowedReason = null,
            RequiredConditions = mode.RequiredPermissions // Map required permissions to required conditions
        };
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Sets up periodic timers for maintenance operations.
    /// </summary>
    private void SetupTimers()
    {
        // Cache cleanup timer - use grain context for thread safety
        _cacheEvictionTimer = this.RegisterGrainTimer(
            async _ => await this.AsReference<IModeGrain>().InternalCleanupCachesAsync(),
            TimeSpan.FromMinutes(CacheCleanupIntervalMinutes),
            TimeSpan.FromMinutes(CacheCleanupIntervalMinutes)
        );

        // Metrics collection timer - use grain context for thread safety
        _metricsTimer = this.RegisterGrainTimer(
            async _ => await this.AsReference<IModeGrain>().InternalCollectMetricsAsync(),
            TimeSpan.FromMinutes(MetricsCollectionIntervalMinutes),
            TimeSpan.FromMinutes(MetricsCollectionIntervalMinutes)
        );

        // Scheduled transition check timer - use grain context for thread safety
        _scheduledTransitionTimer = this.RegisterGrainTimer(
            async _ => await this.AsReference<IModeGrain>().InternalProcessScheduledTransitionsAsync(),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1)
        );
    }

    /// <summary>
    /// Validates an initialization request.
    /// </summary>
    private async Task ValidateInitializationRequestAsync(ModeInitRequest request, CancellationToken cancellationToken)
    {
        // Basic validation
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Mode name cannot be empty", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw new ArgumentException("Mode description cannot be empty", nameof(request));
        }

        if (request.Configuration == null)
        {
            throw new ArgumentException("Mode configuration cannot be null", nameof(request));
        }

        // Validate configuration
        if (string.IsNullOrWhiteSpace(request.Configuration.SystemPrompt))
        {
            throw new ArgumentException("System prompt cannot be empty", nameof(request));
        }

        if (request.Configuration.Tools == null || request.Configuration.Tools.Count == 0)
        {
            throw new ArgumentException("At least one tool must be specified", nameof(request));
        }

        // Additional validation would go here (tool availability, constraints, etc.)
        await Task.CompletedTask;
    }

    /// <summary>
    /// Ensures the mode exists and is not archived.
    /// </summary>
    private async Task EnsureModeExistsAndNotArchivedAsync()
    {
        await EnsureModeExistsAsync().ConfigureAwait(false);

        if (State.CurrentState!.Status == ModeStatus.Archived)
        {
            throw new ModeArchivedException($"Mode {this.GetPrimaryKeyString()} is archived and cannot be modified");
        }
    }

    /// <summary>
    /// Ensures the mode exists.
    /// </summary>
    private async Task EnsureModeExistsAsync()
    {
        if (!State.IsInitialized || State.CurrentState == null)
        {
            throw new ModeNotFoundException($"Mode {this.GetPrimaryKeyString()} not found or not initialized");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Trims history to maintain size limits.
    /// </summary>
    private void TrimHistoryIfNeeded()
    {
        // Trim change history
        while (State.ChangeHistory.Count > MaxHistoryEntries)
        {
            State.ChangeHistory.RemoveAt(0);
        }

        // Trim transition history
        while (State.TransitionHistory.Count > MaxTransitionHistoryEntries)
        {
            State.TransitionHistory.RemoveAt(0);
        }
    }

    /// <summary>
    /// Clears all caches.
    /// </summary>
    private void ClearAllCaches()
    {
        State.ConfigurationCache.Clear();
        State.PromptCache.Clear();
        State.ValidationCache.Clear();

        State.Metadata.CacheHits = 0;
        State.Metadata.CacheMisses = 0;
    }

    /// <summary>
    /// Updates performance metrics.
    /// </summary>
    private void UpdatePerformanceMetrics(long elapsedMs, bool success, string operation)
    {
        if (success)
        {
            State.Performance.SuccessfulOperations++;
        }
        else
        {
            State.Performance.FailedOperations++;
        }

        // Update operation-specific timing (simplified)
        State.Performance.AverageConfigurationOpTimeMs =
            (State.Performance.AverageConfigurationOpTimeMs + elapsedMs) / 2.0;
    }

    /// <summary>
    /// Periodic cache cleanup task.
    /// </summary>
    private async Task PerformCacheCleanupAsync(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var cleanupCount = 0;

            // Clean expired configuration cache entries
            var expiredConfigKeys = State.ConfigurationCache
                .Where(kvp => kvp.Value.ExpiresAtUtc.HasValue && kvp.Value.ExpiresAtUtc.Value < now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredConfigKeys)
            {
                _ = State.ConfigurationCache.Remove(key);
                cleanupCount++;
            }

            // Clean expired prompt cache entries
            var expiredPromptKeys = State.PromptCache
                .Where(kvp => kvp.Value.ExpiresAtUtc < now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredPromptKeys)
            {
                _ = State.PromptCache.Remove(key);
                cleanupCount++;
            }

            // Clean expired validation cache entries
            var expiredValidationKeys = State.ValidationCache
                .Where(kvp => kvp.Value.ExpiresAtUtc < now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredValidationKeys)
            {
                _ = State.ValidationCache.Remove(key);
                cleanupCount++;
            }

            if (cleanupCount > 0)
            {
                await WriteStateAsync().ConfigureAwait(false);
                _logger.LogDebug("Cache cleanup completed. Removed {Count} expired entries", cleanupCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache cleanup for mode {ModeId}", this.GetPrimaryKeyString());
        }
    }

    /// <summary>
    /// Periodic metrics collection task.
    /// </summary>
    private async Task CollectAndReportMetricsAsync(object? state)
    {
        try
        {
            // Calculate cache hit rate
            var totalCacheRequests = State.Metadata.CacheHits + State.Metadata.CacheMisses;
            if (totalCacheRequests > 0)
            {
                State.Performance.CacheHitRatePercent = (double)State.Metadata.CacheHits / totalCacheRequests * 100.0;
            }

            // TODO: Report metrics when methods are available
            // _metricsCollector.RecordModeGrainMetrics(
            //     this.GetPrimaryKeyString(),
            //     State.Performance.SuccessfulOperations,
            //     State.Performance.FailedOperations,
            //     State.Performance.CacheHitRatePercent
            // );

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during metrics collection for mode {ModeId}", this.GetPrimaryKeyString());
        }
    }

    /// <summary>
    /// Periodic scheduled transition processing task.
    /// </summary>
    private async Task ProcessScheduledTransitionsAsync(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var readyTransitions = State.ScheduledTransitions
                .Where(kvp => kvp.Value.ScheduledTimeUtc <= now)
                .ToList();

            foreach (var (scheduleId, scheduledTransition) in readyTransitions)
            {
                try
                {
                    // Create transition request
                    var transitionRequest = new ModeTransitionRequest
                    {
                        TargetModeId = scheduledTransition.TargetModeId,
                        Reason = scheduledTransition.Reason,
                        PreserveContext = scheduledTransition.PreserveContext,
                        PreserveHistory = true,
                        TransitionData = new Dictionary<string, string>
                        {
                            ["ScheduleId"] = scheduleId,
                            ["ScheduledTime"] = scheduledTransition.ScheduledTimeUtc.ToString("O")
                        }
                    };

                    // Execute the transition
                    var result = await TransitionToModeAsync(transitionRequest, CancellationToken.None).ConfigureAwait(false);

                    // Remove from scheduled transitions
                    _ = State.ScheduledTransitions.Remove(scheduleId);

                    _logger.LogInformation(
                        "Executed scheduled transition {ScheduleId} for mode {ModeId} to {TargetModeId}",
                        scheduleId,
                        this.GetPrimaryKeyString(),
                        scheduledTransition.TargetModeId
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to execute scheduled transition {ScheduleId} for mode {ModeId}",
                        scheduleId,
                        this.GetPrimaryKeyString()
                    );

                    // Remove failed transition to prevent retry loops
                    _ = State.ScheduledTransitions.Remove(scheduleId);
                }
            }

            if (readyTransitions.Count > 0)
            {
                await WriteStateAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduled transition processing for mode {ModeId}", this.GetPrimaryKeyString());
        }
    }

    /// <summary>
    /// Invalidates configuration cache when configuration changes.
    /// </summary>
    private void InvalidateConfigurationCache()
    {
        // Clear configuration-related cache entries
        var keysToRemove = State.ConfigurationCache.Keys
            .Where(key => key.StartsWith("config_", StringComparison.Ordinal) || key.StartsWith("effective_", StringComparison.Ordinal))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _ = State.ConfigurationCache.Remove(key);
        }

        _logger.LogDebug("Invalidated {Count} configuration cache entries for mode {ModeId}",
            keysToRemove.Count, this.GetPrimaryKeyString());
    }

    /// <summary>
    /// Validates mode constraints.
    /// </summary>
    private async Task ValidateConstraintsAsync(ModeConstraints constraints, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        // Validate message length constraint
        if (constraints.MaxMessageLength.HasValue &&
            (constraints.MaxMessageLength.Value < 1 || constraints.MaxMessageLength.Value > 100000))
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_MAX_MESSAGE_LENGTH",
                Message = "MaxMessageLength must be between 1 and 100,000",
                Field = "Constraints.MaxMessageLength"
            });
        }

        // Validate rate limiting
        if (constraints.MaxMessagesPerMinute.HasValue &&
            (constraints.MaxMessagesPerMinute.Value < 1 || constraints.MaxMessagesPerMinute.Value > 1000))
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_RATE_LIMIT",
                Message = "MaxMessagesPerMinute must be between 1 and 1,000",
                Field = "Constraints.MaxMessagesPerMinute"
            });
        }

        // Validate file size constraint
        if (constraints.MaxFileSize.HasValue && constraints.MaxFileSize.Value < 1)
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_MAX_FILE_SIZE",
                Message = "MaxFileSize must be greater than 0",
                Field = "Constraints.MaxFileSize"
            });
        }

        // Warn about very restrictive constraints
        if (constraints.MaxMessageLength.HasValue && constraints.MaxMessageLength.Value < 100)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "VERY_RESTRICTIVE_MESSAGE_LENGTH",
                Message = "MaxMessageLength is very restrictive (< 100 characters)",
                SuggestedAction = "Consider increasing the message length limit for better user experience"
            });
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Checks for potential security issues in system prompt.
    /// </summary>
    private static bool ContainsPotentialSecurityIssues(string systemPrompt)
    {
        if (string.IsNullOrEmpty(systemPrompt))
        {
            return false;
        }

        var lowerPrompt = systemPrompt.ToLowerInvariant();

        // Check for potential injection patterns
        var suspiciousPatterns = new[]
        {
            "ignore previous",
            "ignore above",
            "ignore instructions",
            "system:",
            "assistant:",
            "user:",
            "<script",
            "javascript:",
            "eval(",
            "exec(",
            "password",
            "api_key",
            "secret",
            "token"
        };

        return suspiciousPatterns.Any(lowerPrompt.Contains);
    }

    /// <summary>
    /// Generates suggested fixes for validation errors.
    /// </summary>
    private static List<string> GenerateSuggestedFixes(List<ValidationError> errors)
    {
        var fixes = new List<string>();

        foreach (var error in errors)
        {
            var fix = error.Code switch
            {
                "SYSTEM_PROMPT_EMPTY" => "Provide a system prompt that describes the AI's role and behavior",
                "SYSTEM_PROMPT_TOO_LONG" => "Reduce system prompt length to 2000 characters or less",
                "NO_TOOLS_SPECIFIED" => "Add at least one tool to the configuration (use ['*'] for all tools)",
                "INVALID_TOOL_NAMES" => "Ensure all tool names are non-empty and under 100 characters",
                "INVALID_TEMPERATURE" => "Set temperature between 0.0 and 2.0 (0.7 is a good default)",
                "INVALID_MAX_TOKENS" => "Set max tokens between 1 and 100,000 (4000 is a good default)",
                "INVALID_MAX_MESSAGE_LENGTH" => "Set max message length between 1 and 100,000",
                "INVALID_RATE_LIMIT" => "Set rate limit between 1 and 1,000 messages per minute",
                "INVALID_MAX_FILE_SIZE" => "Set max file size to a positive value",
                _ => $"Fix validation error: {error.Message}"
            };

            if (!fixes.Contains(fix))
            {
                fixes.Add(fix);
            }
        }

        return fixes;
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the grain resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _metricsTimer?.Dispose();
            _cacheEvictionTimer?.Dispose();
            _scheduledTransitionTimer?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Invalidates prompt cache entries.
    /// </summary>
    private void InvalidatePromptCache()
    {
        State.PromptCache.Clear();
        _logger.LogDebug("Invalidated all prompt cache entries for mode {ModeId}", this.GetPrimaryKeyString());
    }

    /// <summary>
    /// Invalidates validation cache entries.
    /// </summary>
    private void InvalidateValidationCache()
    {
        var keysToRemove = State.ValidationCache.Keys
            .Where(key => key.Contains("validation", StringComparison.OrdinalIgnoreCase) ||
                         key.Contains("constraint", StringComparison.OrdinalIgnoreCase) ||
                         key.Contains("tool", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _ = State.ValidationCache.Remove(key);
        }

        _logger.LogDebug("Invalidated {Count} validation cache entries for mode {ModeId}",
            keysToRemove.Count, this.GetPrimaryKeyString());
    }

    /// <summary>
    /// Invalidates all cache entries.
    /// </summary>
    private void InvalidateAllCaches()
    {
        var configCount = State.ConfigurationCache.Count;
        var promptCount = State.PromptCache.Count;
        var validationCount = State.ValidationCache.Count;

        State.ConfigurationCache.Clear();
        State.PromptCache.Clear();
        State.ValidationCache.Clear();

        _logger.LogDebug("Invalidated all caches for mode {ModeId}. Config: {ConfigCount}, Prompt: {PromptCount}, Validation: {ValidationCount}",
            this.GetPrimaryKeyString(), configCount, promptCount, validationCount);
    }

    /// <summary>
    /// Calculates a moving average for performance metrics.
    /// </summary>
    private static double CalculateMovingAverage(double currentAverage, long newValue)
    {
        // Simple moving average calculation
        // In a more sophisticated implementation, we might maintain a window of values
        const double alpha = 0.1; // Exponential smoothing factor
        return currentAverage + alpha * (newValue - currentAverage);
    }

    /// <summary>
    /// Applies configuration overrides to a base configuration.
    /// </summary>
    private static ModeConfiguration ApplyConfigurationOverrides(ModeConfiguration baseConfig, Dictionary<string, string> overrides)
    {
        // Start with copies of base values
        var systemPrompt = baseConfig.SystemPrompt;
        var tools = new List<string>(baseConfig.Tools);
        var defaultModel = baseConfig.DefaultModel;
        var temperature = baseConfig.Temperature;
        var maxTokens = baseConfig.MaxTokens;
        var enabledFeatures = new List<string>(baseConfig.EnabledFeatures);
        var parameters = new Dictionary<string, string>(baseConfig.Parameters ?? []);
        var responseFormat = baseConfig.ResponseFormat;

        foreach (var (key, value) in overrides)
        {
            switch (key.ToLowerInvariant())
            {
                case "systemprompt":
                case "system_prompt":
                    systemPrompt = value;
                    break;

                case "defaultmodel":
                case "default_model":
                case "model":
                    defaultModel = value;
                    break;

                case "temperature":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tempValue))
                    {
                        temperature = tempValue;
                    }
                    break;

                case "maxtokens":
                case "max_tokens":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxTokensValue))
                    {
                        maxTokens = maxTokensValue;
                    }
                    break;

                case "tools":
                    // Parse comma-separated tool list
                    tools = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(t => t.Trim())
                                     .Where(t => !string.IsNullOrEmpty(t))];
                    break;

                case "enabledfeatures":
                case "enabled_features":
                case "features":
                    // Parse comma-separated features list
                    enabledFeatures = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                               .Select(f => f.Trim())
                                               .Where(f => !string.IsNullOrEmpty(f))];
                    break;

                // Custom parameters are added to the Parameters dictionary
                default:
                    parameters[key] = value;
                    break;
            }
        }

        // Create new configuration with all applied overrides
        return new ModeConfiguration
        {
            SystemPrompt = systemPrompt,
            Tools = tools,
            DefaultModel = defaultModel,
            Temperature = temperature,
            MaxTokens = maxTokens,
            EnabledFeatures = enabledFeatures,
            DisabledFeatures = [.. baseConfig.DisabledFeatures],
            Parameters = parameters,
            Constraints = baseConfig.Constraints,
            ResponseFormat = responseFormat
        };
    }

    /// <summary>
    /// Computes a hash for a mode configuration (static version).
    /// </summary>
    private static string ComputeConfigurationHashStatic(ModeConfiguration configuration)
    {
        var configData = JsonSerializer.Serialize(configuration, CacheJsonOptions);
        return ComputeStringHash(configData);
    }

    /// <summary>
    /// Generates a cache key for effective configuration.
    /// </summary>
    private static string GenerateEffectiveConfigCacheKey(ModeConfiguration baseConfig, Dictionary<string, string> overrides)
    {
        var baseHash = ComputeConfigurationHashStatic(baseConfig);

        // Sort overrides for consistent cache key
        var sortedOverrides = overrides.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}={kvp.Value}");
        var overrideString = string.Join("|", sortedOverrides);
        var overrideHash = ComputeStringHash(overrideString);

        return $"effective_config_{baseHash}_{overrideHash}";
    }

    /// <summary>
    /// Tries to get a cached configuration.
    /// </summary>
    private Task<ModeConfiguration?> TryGetCachedConfigurationAsync(string cacheKey)
    {
        if (!State.ConfigurationCache.TryGetValue(cacheKey, out var cachedItem))
        {
            return Task.FromResult<ModeConfiguration?>(null);
        }

        // Check if cache entry has expired
        if (cachedItem.ExpiresAtUtc < DateTime.UtcNow)
        {
            State.ConfigurationCache.Remove(cacheKey);
            return Task.FromResult<ModeConfiguration?>(null);
        }

        // Deserialize the cached configuration
        try
        {
            var config = JsonSerializer.Deserialize<ModeConfiguration>(cachedItem.Data, CacheJsonOptions);
            return Task.FromResult(config);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize cached configuration for key {CacheKey}", cacheKey);
            State.ConfigurationCache.Remove(cacheKey);
            return Task.FromResult<ModeConfiguration?>(null);
        }
    }

    /// <summary>
    /// Validates if a tool name is valid (contains only allowed characters).
    /// </summary>
    private static bool IsValidToolName(string toolName)
    {
        // Tool names should contain only alphanumeric characters, underscores, hyphens, and dots
        return toolName.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.');
    }

    /// <summary>
    /// Gets user roles for permission validation.
    /// </summary>
    private static Task<List<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken)
    {
        // Simulate role lookup - in a real implementation, this would query an identity service
        var roles = new List<string>();

        // Simple role assignment based on user ID patterns for demonstration
        if (userId.StartsWith("admin_", StringComparison.OrdinalIgnoreCase))
        {
            roles.AddRange(["Admin", "User", "PowerUser"]);
        }
        else if (userId.StartsWith("mod_", StringComparison.OrdinalIgnoreCase))
        {
            roles.AddRange(["Moderator", "User"]);
        }
        else if (userId.StartsWith("dev_", StringComparison.OrdinalIgnoreCase))
        {
            roles.AddRange(["Developer", "User"]);
        }
        else
        {
            roles.Add("User");
        }

        return Task.FromResult(roles);
    }

    /// <summary>
    /// Validates action-specific permissions.
    /// </summary>
    private static Task<(bool IsAllowed, string? Reason)> ValidateActionPermissionAsync(string userId, ModeAction action, CancellationToken cancellationToken)
    {
        // Basic action permission logic
        return action switch
        {
            ModeAction.View => Task.FromResult((true, (string?)null)),
            ModeAction.Create or ModeAction.Update => Task.FromResult(
                userId.StartsWith("admin_", StringComparison.OrdinalIgnoreCase) || userId.StartsWith("dev_", StringComparison.OrdinalIgnoreCase)
                    ? (true, null)
                    : (false, "Insufficient privileges for modification operations")),
            ModeAction.Delete => Task.FromResult(
                userId.StartsWith("admin_", StringComparison.OrdinalIgnoreCase)
                    ? (true, null)
                    : (false, "Only administrators can delete modes")),
            ModeAction.Archive => Task.FromResult(
                userId.StartsWith("admin_", StringComparison.OrdinalIgnoreCase)
                    ? (true, null)
                    : (false, "Only administrators can archive modes")),
            ModeAction.Transition => Task.FromResult<(bool, string?)>((true, null)),
            ModeAction.Reset => Task.FromResult(
                userId.StartsWith("admin_", StringComparison.OrdinalIgnoreCase)
                    ? (true, null)
                    : (false, "Only administrators can reset modes")),
            ModeAction.Export => Task.FromResult<(bool, string?)>((true, null)),
            ModeAction.Import => Task.FromResult(
                userId.StartsWith("admin_", StringComparison.OrdinalIgnoreCase) || userId.StartsWith("dev_", StringComparison.OrdinalIgnoreCase)
                    ? (true, null)
                    : (false, "Insufficient privileges for import operations")),
            _ => Task.FromResult<(bool, string?)>((true, null))
        };
    }

    /// <summary>
    /// Validates time-based access restrictions.
    /// </summary>
    private static Task<(bool IsAllowed, string? Reason)> ValidateTimeRestrictionsAsync(string userId, CancellationToken cancellationToken)
    {
        // Simple time-based validation - in real implementation, would check against TimeRestrictions configuration
        var currentHour = DateTime.UtcNow.Hour;

        // Example: restrict access during maintenance hours (2-4 AM UTC) for non-admin users
        if (currentHour >= 2 && currentHour < 4 && !userId.StartsWith("admin_", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult((false, (string?)"System maintenance in progress. Access restricted between 2-4 AM UTC."));
        }

        return Task.FromResult((true, (string?)null));
    }

    /// <summary>
    /// Checks rate limits for user actions.
    /// </summary>
    private static Task<(bool IsAllowed, string? Reason)> CheckRateLimitsAsync(string userId, CancellationToken cancellationToken)
    {
        // Simple rate limiting simulation - in real implementation, would check against actual rate limit stores
        // For demonstration, assume admin users have no rate limits
        if (userId.StartsWith("admin_", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult((true, (string?)null));
        }

        // Simulate rate limit check - could integrate with Redis or in-memory cache
        // For now, always allow but log the check
        return Task.FromResult((true, (string?)null));
    }

    /// <summary>
    /// Validates if a custom tool (not in the standard registry) is valid.
    /// </summary>
    private static bool IsCustomToolValid(string toolName)
    {
        // Custom tools should follow naming conventions
        if (toolName is { Length: < 3 or > 50 })
        {
            return false;
        }

        // Should start with a letter
        if (!char.IsLetter(toolName[0]))
        {
            return false;
        }

        // Should not contain consecutive special characters
        var prevChar = ' ';
        foreach (var ch in toolName)
        {
            if ((ch == '_' || ch == '-' || ch == '.') && (prevChar == '_' || prevChar == '-' || prevChar == '.'))
            {
                return false;
            }
            prevChar = ch;
        }

        // Common prefixes for custom tools are allowed
        var allowedPrefixes = new[] { "custom_", "ext_", "plugin_", "user_", "org_" };
        return allowedPrefixes.Any(prefix => toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Sets a cached configuration.
    /// </summary>
    private Task SetCachedConfigurationAsync(string cacheKey, ModeConfiguration configuration, TimeSpan ttl)
    {
        try
        {
            var data = JsonSerializer.Serialize(configuration, CacheJsonOptions);
            var cachedItem = new CachedItem
            {
                Data = data,
                DataType = typeof(ModeConfiguration).FullName ?? "ModeConfiguration",
                CachedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.Add(ttl)
            };

            State.ConfigurationCache[cacheKey] = cachedItem;

            // Apply bounded caching (keep cache under MaxCacheEntries)
            if (State.ConfigurationCache.Count > MaxCacheEntries)
            {
                // Remove oldest entries to stay within bounds
                var itemsToRemove = State.ConfigurationCache
                    .OrderBy(kvp => kvp.Value.CachedAtUtc)
                    .Take(State.ConfigurationCache.Count - MaxCacheEntries + 1)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in itemsToRemove)
                {
                    State.ConfigurationCache.Remove(key);
                }
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache configuration for key {CacheKey}", cacheKey);
            return Task.CompletedTask;
        }
    }

    #endregion

    #region Internal Timer Methods (Thread-Safe)

    /// <summary>
    /// Internal method for thread-safe cache cleanup operations.
    /// Executes within grain execution context to avoid thread safety issues.
    /// </summary>
    public async Task InternalCleanupCachesAsync()
    {
        using var activity = StartActivity(nameof(InternalCleanupCachesAsync));

        try
        {
            await PerformCacheCleanupAsync(null).ConfigureAwait(false);
            CompleteActivity(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform internal cache cleanup for ModeGrain {GrainId}",
                this.GetPrimaryKeyString());
            CompleteActivityWithError(activity, ex);
        }
    }

    /// <summary>
    /// Internal method for thread-safe metrics collection operations.
    /// Executes within grain execution context to avoid thread safety issues.
    /// </summary>
    public async Task InternalCollectMetricsAsync()
    {
        using var activity = StartActivity(nameof(InternalCollectMetricsAsync));

        try
        {
            await CollectAndReportMetricsAsync(null).ConfigureAwait(false);
            CompleteActivity(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform internal metrics collection for ModeGrain {GrainId}",
                this.GetPrimaryKeyString());
            CompleteActivityWithError(activity, ex);
        }
    }

    /// <summary>
    /// Internal method for thread-safe scheduled transition processing.
    /// Executes within grain execution context to avoid thread safety issues.
    /// </summary>
    public async Task InternalProcessScheduledTransitionsAsync()
    {
        using var activity = StartActivity(nameof(InternalProcessScheduledTransitionsAsync));

        try
        {
            await ProcessScheduledTransitionsAsync(null).ConfigureAwait(false);
            CompleteActivity(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process scheduled transitions for ModeGrain {GrainId}",
                this.GetPrimaryKeyString());
            CompleteActivityWithError(activity, ex);
        }
    }

    #endregion
}

// TODO: Continue implementing remaining interface methods:
// - Complete IModeStateGrain methods (ResetToDefaultAsync, GetHistoryAsync, CheckHealthAsync)
// - Implement IModeConfigurationGrain methods
// - Implement IModeTransitionGrain methods
// - Implement IModeValidationGrain methods
// This is a foundational implementation that demonstrates the structure and patterns.