using System.IO.Compression;
using AIChat.Server.Services.EventStore.Implementations;
using AIChat.Server.Services.EventStore.Orleans;
using AIChat.Server.Storage.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Service collection extensions for registering Snapshot Store services.
/// </summary>
public static class SnapshotServiceExtensions
{
    /// <summary>
    /// Adds Snapshot Store services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSnapshotStore(this IServiceCollection services)
    {
        // Register metrics collector
        services.AddSingleton<SnapshotMetricsCollector>();

        // Register SQLite implementation
        services.AddSingleton<ISnapshotStore, SqliteSnapshotStore>();

        // Register individual interfaces for segregated access
        services.AddSingleton<ISnapshotReader>(provider => provider.GetRequiredService<ISnapshotStore>());
        services.AddSingleton<ISnapshotWriter>(provider => provider.GetRequiredService<ISnapshotStore>());
        services.AddSingleton<ISnapshotQuery>(provider => provider.GetRequiredService<ISnapshotStore>());

        // Register the snapshot manager orchestration layer
        services.AddSingleton<ISnapshotManager, SnapshotManager>();

        // Register Orleans integration service
        services.AddSingleton<IOrleansSnapshotService, OrleansSnapshotService>();

        // Register the initialization service
        services.AddSingleton<IHostedService, SnapshotStoreInitializationService>();

        return services;
    }

    /// <summary>
    /// Adds Snapshot Store services with custom configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSnapshotStore(
        this IServiceCollection services,
        Action<SnapshotStoreOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return services.AddSnapshotStore();
    }
}

/// <summary>
/// Configuration options for the Snapshot Store.
/// </summary>
public class SnapshotStoreOptions
{
    /// <summary>
    /// Gets or sets whether to automatically initialize the snapshot schema on startup.
    /// Default is true.
    /// </summary>
    public bool AutoInitializeSchema { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to validate snapshot schema integrity on startup.
    /// Default is true.
    /// </summary>
    public bool ValidateSchemaOnStartup { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to collect detailed metrics.
    /// Default is true.
    /// </summary>
    public bool CollectDetailedMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of snapshots to return in a single query.
    /// Default is 1000.
    /// </summary>
    public int MaxQueryResultSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the query timeout in milliseconds.
    /// Default is 30000 (30 seconds).
    /// </summary>
    public int QueryTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Gets or sets whether to enable content deduplication.
    /// Default is true.
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>
    /// Gets or sets the default compression type for new snapshots.
    /// Default is "gzip".
    /// </summary>
    public string DefaultCompressionType { get; set; } = "gzip";

    /// <summary>
    /// Gets or sets the compression level for snapshots.
    /// Default is CompressionLevel.Optimal.
    /// </summary>
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

    /// <summary>
    /// Gets or sets the health check interval in milliseconds.
    /// Default is 60000 (1 minute).
    /// </summary>
    public int HealthCheckIntervalMs { get; set; } = 60000;

    /// <summary>
    /// Gets or sets the optimization interval in milliseconds.
    /// Default is 3600000 (1 hour).
    /// </summary>
    public int OptimizationIntervalMs { get; set; } = 3600000;

    /// <summary>
    /// Gets or sets the default snapshot creation policy.
    /// If null, no automatic snapshot creation is performed.
    /// </summary>
    public SnapshotCreationPolicy? DefaultCreationPolicy { get; set; } = SnapshotCreationPolicy.ByEventCount(100);

    /// <summary>
    /// Gets or sets the default snapshot retention policy.
    /// If null, no automatic cleanup is performed.
    /// </summary>
    public SnapshotRetentionPolicy? DefaultRetentionPolicy { get; set; } = SnapshotRetentionPolicy.Combined(
        maxAge: TimeSpan.FromDays(30),
        maxCount: 10);

    /// <summary>
    /// Gets or sets whether to log detailed performance statistics during operations.
    /// Default is false.
    /// </summary>
    public bool LogPerformanceStatistics { get; set; }

    /// <summary>
    /// Gets or sets whether to perform optimization on startup.
    /// Default is false.
    /// </summary>
    public bool OptimizeOnStartup { get; set; }

    /// <summary>
    /// Gets or sets the minimum snapshot size threshold for compression.
    /// Snapshots smaller than this will not be compressed.
    /// Default is 1024 bytes.
    /// </summary>
    public int MinCompressionSizeBytes { get; set; } = 1024;

    /// <summary>
    /// Gets or sets the maximum snapshot size that will be stored.
    /// Snapshots larger than this will be rejected.
    /// Default is 100MB.
    /// </summary>
    public long MaxSnapshotSizeBytes { get; set; } = 100 * 1024 * 1024;
}

/// <summary>
/// Hosted service that initializes the Snapshot Store schema on application startup.
/// </summary>
public class SnapshotStoreInitializationService : IHostedService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SnapshotStoreInitializationService> _logger;
    private readonly SnapshotStoreOptions _options;
    private readonly ISnapshotStore? _snapshotStore;

    /// <summary>
    /// Initializes a new instance of the SnapshotStoreInitializationService class.
    /// </summary>
    /// <param name="connectionFactory">SQLite connection factory</param>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <param name="options">Snapshot store configuration options</param>
    /// <param name="snapshotStore">Optional snapshot store for optimization</param>
    public SnapshotStoreInitializationService(
        ISqliteConnectionFactory connectionFactory,
        ILogger<SnapshotStoreInitializationService> logger,
        Microsoft.Extensions.Options.IOptions<SnapshotStoreOptions> options,
        ISnapshotStore? snapshotStore = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new SnapshotStoreOptions();
        _snapshotStore = snapshotStore;
    }

    /// <summary>
    /// Starts the service and initializes the Snapshot Store schema.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Initializing Snapshot Store schema...");

            if (_options.AutoInitializeSchema)
            {
                await InitializeSchemaAsync(cancellationToken);
            }

            if (_options.ValidateSchemaOnStartup)
            {
                await ValidateSchemaAsync(cancellationToken);
            }

            // Log Snapshot Store statistics
            await LogSnapshotStoreStatisticsAsync(cancellationToken);

            // Perform optimization if requested
            if (_options.OptimizeOnStartup && _snapshotStore != null)
            {
                await OptimizeOnStartupAsync(cancellationToken);
            }

            _logger.LogInformation("Snapshot Store initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Snapshot Store");
            throw; // Let the application know initialization failed
        }
    }

    /// <summary>
    /// Stops the service (no cleanup needed for Snapshot Store).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A completed task</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Snapshot Store initialization service stopping");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Initializes the Snapshot Store database schema.
    /// </summary>
    private async Task InitializeSchemaAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            // Ensure the main schema exists first (this should already be done by the main application)
            await Storage.Sqlite.SchemaHelper.EnsureSchemaAsync(connection, cancellationToken);

            // Now ensure the Snapshot Store schema exists
            await SnapshotSchemaHelper.EnsureSnapshotSchemaAsync(connection, cancellationToken);

            _logger.LogDebug("Snapshot Store schema initialization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Snapshot Store schema");
            throw;
        }
    }

    /// <summary>
    /// Validates the Snapshot Store schema integrity.
    /// </summary>
    private async Task ValidateSchemaAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var isValid = await SnapshotSchemaHelper.ValidateSnapshotSchemaAsync(connection, cancellationToken);

            if (!isValid)
            {
                throw new InvalidOperationException("Snapshot Store schema validation failed");
            }

            _logger.LogDebug("Snapshot Store schema validation passed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot Store schema validation failed");
            throw;
        }
    }

    /// <summary>
    /// Logs Snapshot Store statistics for diagnostic purposes.
    /// </summary>
    private async Task LogSnapshotStoreStatisticsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var statistics = await SnapshotSchemaHelper.GetSchemaStatisticsAsync(connection, cancellationToken);

            _logger.LogInformation("Snapshot Store Statistics: {SnapshotCount} snapshots across {StreamCount} streams, {ContentCount} unique content records",
                statistics.SnapshotCount, statistics.StreamCount, statistics.ContentCount);

            if (statistics.TotalCompressedSize > 0)
            {
                _logger.LogInformation("Storage efficiency: {CompressedSize:N0} bytes compressed from {UncompressedSize:N0} bytes (ratio: {CompressionRatio:P1})",
                    statistics.TotalCompressedSize, statistics.TotalUncompressedSize, statistics.CompressionRatio);

                _logger.LogInformation("Deduplication effectiveness: {DeduplicatedCount} deduplicated content records ({DeduplicationEffectiveness:F1}%)",
                    statistics.DeduplicatedContentCount, statistics.DeduplicationEffectiveness);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect Snapshot Store statistics (non-critical)");
        }
    }

    /// <summary>
    /// Performs optimization on startup if requested.
    /// </summary>
    private async Task OptimizeOnStartupAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Performing Snapshot Store optimization on startup...");

            await _snapshotStore!.OptimizeAsync(cancellationToken);

            _logger.LogDebug("Snapshot Store startup optimization completed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to optimize Snapshot Store on startup (non-critical)");
        }
    }
}

/// <summary>
/// Extension methods for Snapshot Store health checks.
/// </summary>
public static class SnapshotStoreHealthCheckExtensions
{
    /// <summary>
    /// Adds Snapshot Store health checks to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="name">The health check name</param>
    /// <param name="tags">Optional tags for the health check</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSnapshotStoreHealthChecks(
        this IServiceCollection services,
        string name = "snapshot_store",
        params string[] tags)
    {
        services.AddHealthChecks()
            .AddCheck<SnapshotStoreHealthCheck>(name, tags: tags);

        return services;
    }
}

/// <summary>
/// Health check for the Snapshot Store.
/// </summary>
public class SnapshotStoreHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    private readonly ISnapshotStore _snapshotStore;
    private readonly ILogger<SnapshotStoreHealthCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the SnapshotStoreHealthCheck class.
    /// </summary>
    /// <param name="snapshotStore">The snapshot store instance</param>
    /// <param name="logger">Logger for diagnostic information</param>
    public SnapshotStoreHealthCheck(ISnapshotStore snapshotStore, ILogger<SnapshotStoreHealthCheck> logger)
    {
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Performs the health check.
    /// </summary>
    /// <param name="context">Health check context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check result</returns>
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Perform Snapshot Store health check
            var healthStatus = await _snapshotStore.CheckHealthAsync(cancellationToken);

            if (healthStatus.IsHealthy)
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                    "Snapshot Store is healthy",
                    healthStatus.Details);
            }
            else
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
                    $"Snapshot Store is unhealthy: {healthStatus.Message}",
                    data: healthStatus.Details);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot Store health check failed");
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
                "Snapshot Store health check failed",
                ex,
                new Dictionary<string, object> { ["exception"] = ex.GetType().Name });
        }
    }
}