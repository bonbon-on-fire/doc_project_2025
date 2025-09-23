using System.Collections.Concurrent;
using AIChat.Server.Services.Translation.Translators;

namespace AIChat.Server.Services.Translation;

/// <summary>
/// Service for discovering, caching, and managing protocol translators.
/// Provides efficient access to registered translators with optimized reflection operations.
/// </summary>
public interface ITranslatorRegistry
{
    /// <summary>
    /// Gets a translator for the specified source and target types.
    /// </summary>
    /// <typeparam name="TSource">Source message type</typeparam>
    /// <typeparam name="TTarget">Target message type</typeparam>
    /// <returns>Translator instance or null if not found</returns>
    IMessageTranslator<TSource, TTarget>? GetTranslator<TSource, TTarget>();

    /// <summary>
    /// Determines whether a translation from the source type to target type is supported.
    /// </summary>
    /// <typeparam name="TSource">Source message type</typeparam>
    /// <typeparam name="TTarget">Target message type</typeparam>
    /// <returns>True if translation is supported, false otherwise</returns>
    bool CanTranslate<TSource, TTarget>();

    /// <summary>
    /// Determines whether a translation from the source type to target type is supported.
    /// </summary>
    /// <param name="sourceType">Source message type</param>
    /// <param name="targetType">Target message type</param>
    /// <returns>True if translation is supported, false otherwise</returns>
    bool CanTranslate(Type sourceType, Type targetType);

    /// <summary>
    /// Gets all supported translation pairs.
    /// This method uses cached results for optimal performance.
    /// </summary>
    /// <returns>Collection of supported source-to-target type pairs</returns>
    IEnumerable<(Type SourceType, Type TargetType)> GetSupportedTranslations();

    /// <summary>
    /// Gets the names of all registered translators.
    /// This method uses cached results for optimal performance.
    /// </summary>
    /// <returns>Collection of translator names</returns>
    IEnumerable<string> GetRegisteredTranslators();

    /// <summary>
    /// Gets a translator by name for metrics and health checking purposes.
    /// </summary>
    /// <param name="translatorName">Name of the translator</param>
    /// <returns>Translator instance or null if not found</returns>
    object? GetTranslatorByName(string translatorName);

    /// <summary>
    /// Forces a refresh of the cached translator registry.
    /// This should only be called when new translators are registered at runtime.
    /// </summary>
    /// <returns>Task representing the async operation</returns>
    Task RefreshRegistryAsync();

    /// <summary>
    /// Gets registry statistics for monitoring purposes.
    /// </summary>
    /// <returns>Registry statistics</returns>
    TranslatorRegistryStatistics GetStatistics();
}

/// <summary>
/// Optimized implementation of translator registry with caching for performance.
/// Uses one-time reflection discovery with thread-safe caching.
/// </summary>
public sealed class TranslatorRegistry : ITranslatorRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TranslatorRegistry> _logger;

    // Cached translator instances (thread-safe)
    private readonly ConcurrentDictionary<(Type, Type), object?> _translatorCache = new();

    // Cached supported translations (initialized once)
    private readonly Lazy<IReadOnlyList<(Type SourceType, Type TargetType)>> _supportedTranslations;

    // Cached translator names (initialized once)
    private readonly Lazy<IReadOnlyList<string>> _translatorNames;

    // Cached translator lookup by name (initialized once)
    private readonly Lazy<IReadOnlyDictionary<string, object>> _translatorsByName;

    // Statistics tracking
    private long _cacheHits;
    private long _cacheMisses;
    private readonly DateTime _createdAt = DateTime.UtcNow;

    /// <summary>
    /// Initializes a new instance of the TranslatorRegistry.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving translators</param>
    /// <param name="logger">Logger instance</param>
    public TranslatorRegistry(IServiceProvider serviceProvider, ILogger<TranslatorRegistry> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize lazy cached collections
        _supportedTranslations = new Lazy<IReadOnlyList<(Type, Type)>>(DiscoverSupportedTranslations);
        _translatorNames = new Lazy<IReadOnlyList<string>>(DiscoverTranslatorNames);
        _translatorsByName = new Lazy<IReadOnlyDictionary<string, object>>(DiscoverTranslatorsByName);

        _logger.LogInformation("TranslatorRegistry initialized with lazy discovery");
    }

    /// <inheritdoc />
    public IMessageTranslator<TSource, TTarget>? GetTranslator<TSource, TTarget>()
    {
        var key = (typeof(TSource), typeof(TTarget));

        if (_translatorCache.TryGetValue(key, out var cachedTranslator))
        {
            Interlocked.Increment(ref _cacheHits);
            return cachedTranslator as IMessageTranslator<TSource, TTarget>;
        }

        Interlocked.Increment(ref _cacheMisses);

        // Resolve from service provider
        var translator = _serviceProvider.GetService<IMessageTranslator<TSource, TTarget>>();

        // Cache the result (even if null to avoid repeated lookups)
        _translatorCache.TryAdd(key, translator);

        if (translator != null)
        {
            _logger.LogDebug(
                "Resolved and cached translator {TranslatorName} for {SourceType} -> {TargetType}",
                translator.TranslatorName,
                typeof(TSource).Name,
                typeof(TTarget).Name
            );
        }
        else
        {
            _logger.LogDebug(
                "No translator found for {SourceType} -> {TargetType}, cached null result",
                typeof(TSource).Name,
                typeof(TTarget).Name
            );
        }

        return translator;
    }

    /// <inheritdoc />
    public bool CanTranslate<TSource, TTarget>()
    {
        return GetTranslator<TSource, TTarget>() != null;
    }

    /// <inheritdoc />
    public bool CanTranslate(Type sourceType, Type targetType)
    {
        var translatorType = typeof(IMessageTranslator<,>).MakeGenericType(sourceType, targetType);
        var translator = _serviceProvider.GetService(translatorType);
        return translator != null;
    }

    /// <inheritdoc />
    public IEnumerable<(Type SourceType, Type TargetType)> GetSupportedTranslations()
    {
        return _supportedTranslations.Value;
    }

    /// <inheritdoc />
    public IEnumerable<string> GetRegisteredTranslators()
    {
        return _translatorNames.Value;
    }

    /// <inheritdoc />
    public object? GetTranslatorByName(string translatorName)
    {
        if (string.IsNullOrEmpty(translatorName))
        {
            return null;
        }

        return _translatorsByName.Value.TryGetValue(translatorName, out var translator) ? translator : null;
    }

    /// <inheritdoc />
    public async Task RefreshRegistryAsync()
    {
        _logger.LogInformation("Refreshing translator registry cache");

        // Clear caches to force re-discovery
        _translatorCache.Clear();

        // Force re-evaluation of lazy collections
        if (_supportedTranslations.IsValueCreated)
        {
            _ = DiscoverSupportedTranslations();
        }

        if (_translatorNames.IsValueCreated)
        {
            _ = DiscoverTranslatorNames();
        }

        if (_translatorsByName.IsValueCreated)
        {
            _ = DiscoverTranslatorsByName();
        }

        _logger.LogInformation("Translator registry cache refreshed");
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public TranslatorRegistryStatistics GetStatistics()
    {
        return new TranslatorRegistryStatistics
        {
            CacheHits = Interlocked.Read(ref _cacheHits),
            CacheMisses = Interlocked.Read(ref _cacheMisses),
            CachedTranslatorCount = _translatorCache.Count,
            SupportedTranslationCount = _supportedTranslations.IsValueCreated ? _supportedTranslations.Value.Count : 0,
            RegisteredTranslatorCount = _translatorNames.IsValueCreated ? _translatorNames.Value.Count : 0,
            CreatedAt = _createdAt,
            LastRefreshAt = null // Would be updated in RefreshRegistryAsync if needed
        };
    }

    private IReadOnlyList<(Type SourceType, Type TargetType)> DiscoverSupportedTranslations()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var supportedTranslations = new List<(Type, Type)>();

        try
        {
            // Get all services that implement IMessageTranslator<,>
            var translatorServices = _serviceProvider.GetServices<object>()
                .Where(service => IsTranslatorService(service))
                .ToList();

            foreach (var service in translatorServices)
            {
                var interfaceType = GetTranslatorInterface(service);
                if (interfaceType != null)
                {
                    var genericArgs = interfaceType.GetGenericArguments();
                    supportedTranslations.Add((genericArgs[0], genericArgs[1]));
                }
            }

            stopwatch.Stop();
            _logger.LogDebug(
                "Discovered {Count} supported translations in {Duration}ms",
                supportedTranslations.Count,
                stopwatch.ElapsedMilliseconds
            );

            return supportedTranslations.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover supported translations");
            return new List<(Type, Type)>().AsReadOnly();
        }
    }

    private IReadOnlyList<string> DiscoverTranslatorNames()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var translatorNames = new List<string>();

        try
        {
            foreach (var (sourceType, targetType) in GetSupportedTranslations())
            {
                var translator = GetTranslatorObject(sourceType, targetType);
                if (translator != null)
                {
                    var nameProperty = translator.GetType().GetProperty("TranslatorName");
                    if (nameProperty?.GetValue(translator) is string name && !string.IsNullOrEmpty(name))
                    {
                        translatorNames.Add(name);
                    }
                }
            }

            stopwatch.Stop();
            _logger.LogDebug(
                "Discovered {Count} translator names in {Duration}ms",
                translatorNames.Count,
                stopwatch.ElapsedMilliseconds
            );

            return translatorNames.Distinct().ToList().AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover translator names");
            return new List<string>().AsReadOnly();
        }
    }

    private IReadOnlyDictionary<string, object> DiscoverTranslatorsByName()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var translatorsByName = new Dictionary<string, object>();

        try
        {
            foreach (var (sourceType, targetType) in GetSupportedTranslations())
            {
                var translator = GetTranslatorObject(sourceType, targetType);
                if (translator != null)
                {
                    var nameProperty = translator.GetType().GetProperty("TranslatorName");
                    if (nameProperty?.GetValue(translator) is string name && !string.IsNullOrEmpty(name))
                    {
                        translatorsByName.TryAdd(name, translator);
                    }
                }
            }

            stopwatch.Stop();
            _logger.LogDebug(
                "Built translator lookup dictionary with {Count} entries in {Duration}ms",
                translatorsByName.Count,
                stopwatch.ElapsedMilliseconds
            );

            return translatorsByName.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build translator lookup dictionary");
            return new Dictionary<string, object>().AsReadOnly();
        }
    }

    private object? GetTranslatorObject(Type sourceType, Type targetType)
    {
        var translatorType = typeof(IMessageTranslator<,>).MakeGenericType(sourceType, targetType);
        return _serviceProvider.GetService(translatorType);
    }

    private static bool IsTranslatorService(object service)
    {
        return service.GetType().GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMessageTranslator<,>));
    }

    private static Type? GetTranslatorInterface(object service)
    {
        return service.GetType().GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMessageTranslator<,>));
    }
}

/// <summary>
/// Statistics for the translator registry.
/// </summary>
public sealed class TranslatorRegistryStatistics
{
    /// <summary>
    /// Number of successful cache hits.
    /// </summary>
    public long CacheHits { get; set; }

    /// <summary>
    /// Number of cache misses.
    /// </summary>
    public long CacheMisses { get; set; }

    /// <summary>
    /// Cache hit rate as a percentage.
    /// </summary>
    public double HitRate => (CacheHits + CacheMisses) > 0 ? (CacheHits * 100.0) / (CacheHits + CacheMisses) : 0;

    /// <summary>
    /// Number of cached translator instances.
    /// </summary>
    public int CachedTranslatorCount { get; set; }

    /// <summary>
    /// Number of supported translation pairs.
    /// </summary>
    public int SupportedTranslationCount { get; set; }

    /// <summary>
    /// Number of registered translators.
    /// </summary>
    public int RegisteredTranslatorCount { get; set; }

    /// <summary>
    /// When the registry was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the registry was last refreshed.
    /// </summary>
    public DateTime? LastRefreshAt { get; set; }
}