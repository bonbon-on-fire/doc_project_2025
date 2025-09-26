using AIChat.Server.Services.EventStore;
// Note: Implementation classes will be created - removing this for now
// using AIChat.Server.Services.Recovery.Implementations;

namespace AIChat.Server.Services.Recovery;

/// <summary>
/// Extension methods for registering automatic recovery services with dependency injection.
/// Provides convenient configuration and registration for the recovery system.
/// </summary>
public static class AutomaticRecoveryServiceExtensions
{
    /// <summary>
    /// Adds automatic recovery services to the service collection with default configuration.
    /// This method registers all necessary components for automatic state reconstruction.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <returns>The service collection for chaining</returns>
    /// <exception cref="ArgumentNullException">Thrown when services is null</exception>
    public static IServiceCollection AddAutomaticRecoveryServices(this IServiceCollection services)
    {
        return AddAutomaticRecoveryServices(services, _ => { });
    }

    /// <summary>
    /// Adds automatic recovery services to the service collection with custom configuration.
    /// This method registers all necessary components for automatic state reconstruction.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configureOptions">Action to configure recovery options</param>
    /// <returns>The service collection for chaining</returns>
    /// <exception cref="ArgumentNullException">Thrown when services or configureOptions is null</exception>
    public static IServiceCollection AddAutomaticRecoveryServices(
        this IServiceCollection services,
        Action<AutomaticRecoveryOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        // Configure options
        services.Configure(configureOptions);

        // Register core recovery components - implementations will be created
        // services.AddSingleton<IStateRecoveryDetector, StateRecoveryDetector>();
        // services.AddSingleton<IStateRecoveryOrchestrator, StateRecoveryOrchestrator>();
        // services.AddSingleton<IStateConsistencyVerifier, StateConsistencyVerifier>();

        // Register the main automatic recovery service
        // services.AddSingleton<IAutomaticRecoveryService, AutomaticRecoveryService>();

        // Register background services for periodic checks
        // services.AddHostedService<RecoveryBackgroundService>();

        // Register health checks
        // services.AddHealthChecks()
        //     .AddCheck<AutomaticRecoveryHealthCheck>("automatic-recovery")
        //     .AddCheck<RecoveryDetectorHealthCheck>("recovery-detector")
        //     .AddCheck<RecoveryOrchestratorHealthCheck>("recovery-orchestrator")
        //     .AddCheck<RecoveryVerifierHealthCheck>("recovery-verifier");

        return services;
    }

    /// <summary>
    /// Adds automatic recovery services with Orleans-specific configuration.
    /// This method ensures proper integration with Orleans grain lifecycle.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configureOptions">Action to configure recovery options</param>
    /// <returns>The service collection for chaining</returns>
    /// <exception cref="ArgumentNullException">Thrown when services or configureOptions is null</exception>
    public static IServiceCollection AddOrleansAutomaticRecovery(
        this IServiceCollection services,
        Action<AutomaticRecoveryOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Add base recovery services
        if (configureOptions != null)
        {
            services.AddAutomaticRecoveryServices(configureOptions);
        }
        else
        {
            services.AddAutomaticRecoveryServices();
        }

        // Add Orleans-specific services - implementations will be created
        // services.AddSingleton<IOrleansRecoveryHelper, OrleansRecoveryHelper>();
        // services.AddSingleton<IGrainRecoveryExtensions, GrainRecoveryExtensions>();

        return services;
    }

    /// <summary>
    /// Adds recovery services with custom detector implementation.
    /// Allows replacement of the default detection logic.
    /// </summary>
    /// <typeparam name="TDetector">The custom detector implementation type</typeparam>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configureOptions">Action to configure recovery options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddAutomaticRecoveryWithCustomDetector<TDetector>(
        this IServiceCollection services,
        Action<AutomaticRecoveryOptions>? configureOptions = null)
        where TDetector : class, IStateRecoveryDetector
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register custom detector
        services.AddSingleton<IStateRecoveryDetector, TDetector>();

        // Register other core components - implementations will be created
        // services.AddSingleton<IStateRecoveryOrchestrator, StateRecoveryOrchestrator>();
        // services.AddSingleton<IStateConsistencyVerifier, StateConsistencyVerifier>();
        // services.AddSingleton<IAutomaticRecoveryService, AutomaticRecoveryService>();

        // Register background services
        // services.AddHostedService<RecoveryBackgroundService>();

        return services;
    }

    /// <summary>
    /// Adds recovery services with custom orchestrator implementation.
    /// Allows replacement of the default orchestration logic.
    /// </summary>
    /// <typeparam name="TOrchestrator">The custom orchestrator implementation type</typeparam>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configureOptions">Action to configure recovery options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddAutomaticRecoveryWithCustomOrchestrator<TOrchestrator>(
        this IServiceCollection services,
        Action<AutomaticRecoveryOptions>? configureOptions = null)
        where TOrchestrator : class, IStateRecoveryOrchestrator
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register core components with custom orchestrator
        // services.AddSingleton<IStateRecoveryDetector, StateRecoveryDetector>();
        services.AddSingleton<IStateRecoveryOrchestrator, TOrchestrator>();
        // services.AddSingleton<IStateConsistencyVerifier, StateConsistencyVerifier>();
        // services.AddSingleton<IAutomaticRecoveryService, AutomaticRecoveryService>();

        // Register background services
        // services.AddHostedService<RecoveryBackgroundService>();

        return services;
    }

    /// <summary>
    /// Adds recovery services with custom verifier implementation.
    /// Allows replacement of the default consistency verification logic.
    /// </summary>
    /// <typeparam name="TVerifier">The custom verifier implementation type</typeparam>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configureOptions">Action to configure recovery options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddAutomaticRecoveryWithCustomVerifier<TVerifier>(
        this IServiceCollection services,
        Action<AutomaticRecoveryOptions>? configureOptions = null)
        where TVerifier : class, IStateConsistencyVerifier
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register core components with custom verifier
        // services.AddSingleton<IStateRecoveryDetector, StateRecoveryDetector>();
        // services.AddSingleton<IStateRecoveryOrchestrator, StateRecoveryOrchestrator>();
        services.AddSingleton<IStateConsistencyVerifier, TVerifier>();
        // services.AddSingleton<IAutomaticRecoveryService, AutomaticRecoveryService>();

        // Register background services
        // services.AddHostedService<RecoveryBackgroundService>();

        return services;
    }
}

/// <summary>
/// Configuration options for the automatic recovery system.
/// </summary>
public class AutomaticRecoveryOptions
{
    /// <summary>
    /// Gets or sets whether automatic recovery is enabled globally.
    /// </summary>
    public bool EnableAutomaticRecovery { get; set; } = true;

    /// <summary>
    /// Gets or sets the default recovery strategy.
    /// </summary>
    public RecoveryStrategy DefaultRecoveryStrategy { get; set; } = RecoveryStrategy.HybridRecovery;

    /// <summary>
    /// Gets or sets the default maximum recovery time in milliseconds.
    /// </summary>
    public double DefaultMaxRecoveryTimeMs { get; set; } = 30000; // 30 seconds

    /// <summary>
    /// Gets or sets the default maximum number of events to replay.
    /// </summary>
    public long DefaultMaxEventsToReplay { get; set; } = 10000;

    /// <summary>
    /// Gets or sets whether to enable periodic integrity checks globally.
    /// </summary>
    public bool EnablePeriodicIntegrityChecks { get; set; }

    /// <summary>
    /// Gets or sets the default interval for periodic integrity checks.
    /// </summary>
    public TimeSpan DefaultIntegrityCheckInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets whether to enable recovery events.
    /// </summary>
    public bool EnableRecoveryEvents { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable detailed recovery logging.
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable recovery metrics collection.
    /// </summary>
    public bool EnableMetricsCollection { get; set; } = true;

    /// <summary>
    /// Gets or sets grain type-specific recovery policies.
    /// </summary>
    public Dictionary<string, RecoveryPolicy> GrainTypePolicies { get; set; } = [];

    /// <summary>
    /// Gets or sets custom recovery settings.
    /// </summary>
    public Dictionary<string, object> CustomSettings { get; set; } = [];

    /// <summary>
    /// Gets the recovery policy for a specific grain type, or the default policy if not configured.
    /// </summary>
    /// <param name="grainType">The grain type to get policy for</param>
    /// <returns>The recovery policy for the grain type</returns>
    public RecoveryPolicy GetPolicyForGrainType(string grainType)
    {
        if (GrainTypePolicies.TryGetValue(grainType, out var policy))
        {
            return policy;
        }

        // Return default policy based on global settings
        return new RecoveryPolicy
        {
            AutomaticRecoveryEnabled = EnableAutomaticRecovery,
            PreferredStrategy = DefaultRecoveryStrategy,
            MaxRecoveryTimeMs = DefaultMaxRecoveryTimeMs,
            MaxEventsToReplay = DefaultMaxEventsToReplay,
            EnablePeriodicIntegrityChecks = EnablePeriodicIntegrityChecks,
            DefaultIntegrityCheckInterval = DefaultIntegrityCheckInterval
        };
    }

    /// <summary>
    /// Sets a recovery policy for a specific grain type.
    /// </summary>
    /// <param name="grainType">The grain type to set policy for</param>
    /// <param name="policy">The recovery policy to apply</param>
    public void SetPolicyForGrainType(string grainType, RecoveryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(grainType);
        ArgumentNullException.ThrowIfNull(policy);

        GrainTypePolicies[grainType] = policy;
    }

    /// <summary>
    /// Creates default options suitable for development environments.
    /// </summary>
    /// <returns>Development-optimized recovery options</returns>
    public static AutomaticRecoveryOptions ForDevelopment()
    {
        return new AutomaticRecoveryOptions
        {
            EnableAutomaticRecovery = true,
            DefaultRecoveryStrategy = RecoveryStrategy.HybridRecovery,
            DefaultMaxRecoveryTimeMs = 10000, // 10 seconds for faster feedback
            DefaultMaxEventsToReplay = 5000,
            EnablePeriodicIntegrityChecks = false, // Disabled in dev for performance
            EnableDetailedLogging = true,
            EnableMetricsCollection = true
        };
    }

    /// <summary>
    /// Creates default options suitable for production environments.
    /// </summary>
    /// <returns>Production-optimized recovery options</returns>
    public static AutomaticRecoveryOptions ForProduction()
    {
        return new AutomaticRecoveryOptions
        {
            EnableAutomaticRecovery = true,
            DefaultRecoveryStrategy = RecoveryStrategy.SnapshotFirst, // Conservative for production
            DefaultMaxRecoveryTimeMs = 60000, // 1 minute for thorough recovery
            DefaultMaxEventsToReplay = 20000,
            EnablePeriodicIntegrityChecks = true,
            DefaultIntegrityCheckInterval = TimeSpan.FromMinutes(30),
            EnableDetailedLogging = false, // Reduced logging for performance
            EnableMetricsCollection = true
        };
    }

    /// <summary>
    /// Creates default options suitable for testing environments.
    /// </summary>
    /// <returns>Test-optimized recovery options</returns>
    public static AutomaticRecoveryOptions ForTesting()
    {
        return new AutomaticRecoveryOptions
        {
            EnableAutomaticRecovery = true,
            DefaultRecoveryStrategy = RecoveryStrategy.HybridRecovery,
            DefaultMaxRecoveryTimeMs = 5000, // Fast for tests
            DefaultMaxEventsToReplay = 1000,
            EnablePeriodicIntegrityChecks = false, // Not needed in tests
            EnableRecoveryEvents = false, // Avoid event noise in tests
            EnableDetailedLogging = false, // Reduce test output
            EnableMetricsCollection = false // Not needed in unit tests
        };
    }
}

/// <summary>
/// Helper interface for Orleans-specific recovery operations.
/// </summary>
public interface IOrleansRecoveryHelper
{
    /// <summary>
    /// Integrates recovery logic with Orleans grain activation.
    /// </summary>
    /// <typeparam name="T">The grain state type</typeparam>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="grainType">The grain type</param>
    /// <param name="currentState">The current grain state</param>
    /// <param name="projection">The event projection for state reconstruction</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recovery result with potentially updated state</returns>
    Task<AutomaticRecoveryResult<T>> HandleGrainActivationAsync<T>(
        string grainId,
        string grainType,
        T currentState,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles recovery during grain deactivation.
    /// </summary>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="grainType">The grain type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deactivation handling result</returns>
    Task<RecoveryDeactivationResult> HandleGrainDeactivationAsync(
        string grainId,
        string grainType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Helper interface for grain recovery extensions.
/// </summary>
public interface IGrainRecoveryExtensions
{
    /// <summary>
    /// Creates a recovery-aware grain activation wrapper.
    /// </summary>
    /// <typeparam name="TGrain">The grain type</typeparam>
    /// <typeparam name="TState">The state type</typeparam>
    /// <param name="grain">The grain instance</param>
    /// <param name="projection">The event projection</param>
    /// <returns>A recovery-aware activation wrapper</returns>
    IRecoveryAwareGrainActivation<TGrain, TState> CreateActivationWrapper<TGrain, TState>(
        TGrain grain,
        IEventProjection<TState> projection)
        where TGrain : class;
}

/// <summary>
/// Interface for recovery-aware grain activation.
/// </summary>
/// <typeparam name="TGrain">The grain type</typeparam>
/// <typeparam name="TState">The state type</typeparam>
public interface IRecoveryAwareGrainActivation<TGrain, TState>
{
    /// <summary>
    /// Performs recovery-aware activation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Activation result with recovery information</returns>
    Task<RecoveryAwareActivationResult<TState>> ActivateWithRecoveryAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of recovery-aware grain activation.
/// </summary>
/// <typeparam name="TState">The state type</typeparam>
public record RecoveryAwareActivationResult<TState>
{
    /// <summary>
    /// Whether activation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The final state after activation and potential recovery.
    /// </summary>
    public TState? State { get; init; }

    /// <summary>
    /// Whether recovery was performed during activation.
    /// </summary>
    public bool RecoveryPerformed { get; init; }

    /// <summary>
    /// The type of recovery that was performed, if any.
    /// </summary>
    public RecoveryType RecoveryType { get; init; } = RecoveryType.None;

    /// <summary>
    /// Any error that occurred during activation.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Result of handling grain deactivation.
/// </summary>
public record RecoveryDeactivationResult
{
    /// <summary>
    /// Whether deactivation handling was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Cleanup actions that were performed.
    /// </summary>
    public IReadOnlyList<string> CleanupActions { get; init; } = [];

    /// <summary>
    /// Any error that occurred during deactivation handling.
    /// </summary>
    public string? Error { get; init; }
}