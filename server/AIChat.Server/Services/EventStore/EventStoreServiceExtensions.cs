using AIChat.Server.Services.EventStore.Implementations;
using AIChat.Server.Storage.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Service collection extensions for registering Event Store services.
/// </summary>
public static class EventStoreServiceExtensions
{
    /// <summary>
    /// Adds Event Store services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddEventStore(this IServiceCollection services)
    {
        // Register core services
        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
        services.AddSingleton<EventStoreMetricsCollector>();

        // Register SQLite implementation
        services.AddSingleton<IEventStore, SqliteEventStore>();

        // Register individual interfaces for segregated access
        services.AddSingleton<IEventAppender>(provider => provider.GetRequiredService<IEventStore>());
        services.AddSingleton<IEventReader>(provider => provider.GetRequiredService<IEventStore>());
        services.AddSingleton<IEventQuery>(provider => provider.GetRequiredService<IEventStore>());
        services.AddSingleton<IEventReplay>(provider => provider.GetRequiredService<IEventStore>());

        // Register the initialization service
        services.AddSingleton<IHostedService, EventStoreInitializationService>();

        return services;
    }

    /// <summary>
    /// Adds Event Store services with custom configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Configuration action</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddEventStore(
        this IServiceCollection services,
        Action<EventStoreOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return services.AddEventStore();
    }
}

/// <summary>
/// Configuration options for the Event Store.
/// </summary>
public class EventStoreOptions
{
    /// <summary>
    /// Gets or sets whether to automatically initialize the schema on startup.
    /// Default is true.
    /// </summary>
    public bool AutoInitializeSchema { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to validate schema integrity on startup.
    /// Default is true.
    /// </summary>
    public bool ValidateSchemaOnStartup { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to collect detailed metrics.
    /// Default is true.
    /// </summary>
    public bool CollectDetailedMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of events to return in a single query.
    /// Default is 1000.
    /// </summary>
    public int MaxQueryResultSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets whether to enable snapshot optimization.
    /// Default is false (not implemented yet).
    /// </summary>
    public bool EnableSnapshots { get; set; }

    /// <summary>
    /// Gets or sets the snapshot frequency (every N events).
    /// Default is 100.
    /// </summary>
    public int SnapshotFrequency { get; set; } = 100;
}

/// <summary>
/// Hosted service that initializes the Event Store schema on application startup.
/// </summary>
public class EventStoreInitializationService : IHostedService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<EventStoreInitializationService> _logger;
    private readonly EventStoreOptions _options;

    /// <summary>
    /// Initializes a new instance of the EventStoreInitializationService class.
    /// </summary>
    /// <param name="connectionFactory">SQLite connection factory</param>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <param name="options">Event store configuration options</param>
    public EventStoreInitializationService(
        ISqliteConnectionFactory connectionFactory,
        ILogger<EventStoreInitializationService> logger,
        Microsoft.Extensions.Options.IOptions<EventStoreOptions> options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new EventStoreOptions();
    }

    /// <summary>
    /// Starts the service and initializes the Event Store schema.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Initializing Event Store schema...");

            if (_options.AutoInitializeSchema)
            {
                await InitializeSchemaAsync(cancellationToken);
            }

            if (_options.ValidateSchemaOnStartup)
            {
                await ValidateSchemaAsync(cancellationToken);
            }

            // Log Event Store statistics
            await LogEventStoreStatisticsAsync(cancellationToken);

            _logger.LogInformation("Event Store initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Event Store");
            throw; // Let the application know initialization failed
        }
    }

    /// <summary>
    /// Stops the service (no cleanup needed for Event Store).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A completed task</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Event Store initialization service stopping");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Initializes the Event Store database schema.
    /// </summary>
    private async Task InitializeSchemaAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            // Ensure the main schema exists first (this should already be done by the main application)
            await Storage.Sqlite.SchemaHelper.EnsureSchemaAsync(connection, cancellationToken);

            // Now ensure the Event Store schema exists
            await EventStoreSchemaHelper.EnsureEventStoreSchemaAsync(connection, cancellationToken);

            _logger.LogDebug("Event Store schema initialization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Event Store schema");
            throw;
        }
    }

    /// <summary>
    /// Validates the Event Store schema integrity.
    /// </summary>
    private async Task ValidateSchemaAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var isValid = await EventStoreSchemaHelper.ValidateEventStoreSchemaAsync(connection, cancellationToken);

            if (!isValid)
            {
                throw new InvalidOperationException("Event Store schema validation failed");
            }

            _logger.LogDebug("Event Store schema validation passed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event Store schema validation failed");
            throw;
        }
    }

    /// <summary>
    /// Logs Event Store statistics for diagnostic purposes.
    /// </summary>
    private async Task LogEventStoreStatisticsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var statistics = await EventStoreSchemaHelper.GetEventStoreStatisticsAsync(connection, cancellationToken);

            _logger.LogInformation("Event Store Statistics: {TotalEvents} events across {TotalStreams} streams, {TotalSnapshots} snapshots",
                statistics.TotalEvents, statistics.TotalStreams, statistics.TotalSnapshots);

            if (statistics.EarliestEventTimestamp.HasValue && statistics.LatestEventTimestamp.HasValue)
            {
                _logger.LogDebug("Event Store time range: {EarliestEvent} to {LatestEvent} (data age: {DataAge})",
                    statistics.EarliestEventTimestamp.Value,
                    statistics.LatestEventTimestamp.Value,
                    statistics.DataAge);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect Event Store statistics (non-critical)");
        }
    }
}

/// <summary>
/// Extension methods for Event Store health checks.
/// </summary>
public static class EventStoreHealthCheckExtensions
{
    /// <summary>
    /// Adds Event Store health checks to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="name">The health check name</param>
    /// <param name="tags">Optional tags for the health check</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddEventStoreHealthChecks(
        this IServiceCollection services,
        string name = "event_store",
        params string[] tags)
    {
        services.AddHealthChecks()
            .AddCheck<EventStoreHealthCheck>(name, tags: tags);

        return services;
    }
}

/// <summary>
/// Health check for the Event Store.
/// </summary>
public class EventStoreHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<EventStoreHealthCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the EventStoreHealthCheck class.
    /// </summary>
    /// <param name="eventStore">The event store instance</param>
    /// <param name="logger">Logger for diagnostic information</param>
    public EventStoreHealthCheck(IEventStore eventStore, ILogger<EventStoreHealthCheck> logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
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
            // Perform Event Store health check
            var healthStatus = await _eventStore.CheckHealthAsync(cancellationToken);

            if (healthStatus.IsHealthy)
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                    "Event Store is healthy",
                    healthStatus.Details);
            }
            else
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
                    $"Event Store is unhealthy: {healthStatus.Message}",
                    data: healthStatus.Details);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event Store health check failed");
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
                "Event Store health check failed",
                ex,
                new Dictionary<string, object> { ["exception"] = ex.GetType().Name });
        }
    }
}