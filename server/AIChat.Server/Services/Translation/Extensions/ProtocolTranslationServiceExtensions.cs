using AIChat.Orleans.Contracts;
using AIChat.Server.Models.WebSocket;
using AIChat.Server.Services.Translation.Extensions.HealthChecks;
using AIChat.Server.Services.Translation.Models;
using AIChat.Server.Services.Translation.Translators;

namespace AIChat.Server.Services.Translation.Extensions;

/// <summary>
/// Extension methods for registering protocol translation services in the dependency injection container.
/// </summary>
public static class ProtocolTranslationServiceExtensions
{
    /// <summary>
    /// Adds protocol translation services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional configuration delegate</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddProtocolTranslation(
        this IServiceCollection services,
        Action<TranslationOptions>? configure = null)
    {
        // Configure options
        var options = new TranslationOptions();
        configure?.Invoke(options);
        _ = services.AddSingleton(options);

        // Register core specialized services
        _ = services.AddSingleton<ITranslatorRegistry, TranslatorRegistry>();
        _ = services.AddSingleton<ITranslationMetricsCollector, TranslationMetricsCollector>();

        // Register main orchestration service
        _ = services.AddSingleton<IProtocolTranslationService, ProtocolTranslationService>();

        // Register translation cache
        if (options.EnableCaching)
        {
            _ = services.AddSingleton<ITranslationCache, MemoryTranslationCache>();
        }
        else
        {
            _ = services.AddSingleton<ITranslationCache, NullTranslationCache>();
        }

        // Register all translators
        RegisterTranslators(services);

        // Register health checks
        _ = services.AddHealthChecks()
            .AddCheck<ProtocolTranslationHealthCheck>("protocol_translation");

        return services;
    }

    /// <summary>
    /// Adds protocol translation services with production-optimized settings.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddProtocolTranslationForProduction(this IServiceCollection services)
    {
        return services.AddProtocolTranslation(options =>
        {
            options.TimeoutMs = 5000;
            options.EnableCaching = true;
            options.CacheExpirationMinutes = 15;
            options.MaxCacheSize = 5000;
            options.EnableMetrics = true;
            options.EnableBatchTranslation = true;
            options.MaxBatchSize = 10;
        });
    }

    /// <summary>
    /// Adds protocol translation services with development-optimized settings.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddProtocolTranslationForDevelopment(this IServiceCollection services)
    {
        return services.AddProtocolTranslation(options =>
        {
            options.TimeoutMs = 10000;
            options.EnableCaching = false; // Disable caching for easier debugging
            options.EnableMetrics = true;
            options.EnableBatchTranslation = true;
            options.MaxBatchSize = 5;
        });
    }

    /// <summary>
    /// Adds protocol translation services with testing-optimized settings.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddProtocolTranslationForTesting(this IServiceCollection services)
    {
        return services.AddProtocolTranslation(options =>
        {
            options.TimeoutMs = 30000; // Longer timeout for test environments
            options.EnableCaching = false; // Disable caching for predictable test results
            options.EnableMetrics = false; // Disable metrics to avoid interference
            options.EnableBatchTranslation = false; // Simplify testing
        });
    }

    /// <summary>
    /// Registers custom translators in addition to the default ones.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Configuration delegate for custom translators</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddCustomTranslators(
        this IServiceCollection services,
        Action<IServiceCollection> configure)
    {
        configure(services);
        return services;
    }

    private static void RegisterTranslators(IServiceCollection services)
    {
        // Protocol to Orleans translators (incoming)
        _ = services.AddTransient<IMessageTranslator<SignalRMessage, ChatMessage>, SignalRToOrleansTranslator>();
        _ = services.AddTransient<IMessageTranslator<WebSocketMessage, ChatMessage>, WebSocketToOrleansTranslator>();
        _ = services.AddTransient<IMessageTranslator<RestMessage, ChatMessage>, RestToOrleansTranslator>();

        // Orleans to Protocol translators (outgoing)
        _ = services.AddTransient<IMessageTranslator<MessageResult, SignalRResponse>, OrleansToSignalRTranslator>();

        // Additional bidirectional translators can be added here
        // services.AddTransient<IMessageTranslator<MessageResult, WebSocketMessage>, OrleansToWebSocketTranslator>();
        // services.AddTransient<IMessageTranslator<MessageResult, RestResponse>, OrleansToRestTranslator>();
    }
}

/// <summary>
/// Builder class for configuring protocol translation services.
/// </summary>
public class ProtocolTranslationBuilder
{
    private readonly IServiceCollection _services;
    private readonly TranslationOptions _options;

    internal ProtocolTranslationBuilder(IServiceCollection services, TranslationOptions options)
    {
        _services = services;
        _options = options;
    }

    /// <summary>
    /// Configures caching settings.
    /// </summary>
    /// <param name="enabled">Whether caching is enabled</param>
    /// <param name="expirationMinutes">Cache expiration time in minutes</param>
    /// <param name="maxSize">Maximum cache size</param>
    /// <returns>The builder for chaining</returns>
    public ProtocolTranslationBuilder WithCaching(bool enabled = true, int expirationMinutes = 30, int maxSize = 1000)
    {
        _options.EnableCaching = enabled;
        _options.CacheExpirationMinutes = expirationMinutes;
        _options.MaxCacheSize = maxSize;
        return this;
    }

    /// <summary>
    /// Configures timeout settings.
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds</param>
    /// <returns>The builder for chaining</returns>
    public ProtocolTranslationBuilder WithTimeout(int timeoutMs)
    {
        _options.TimeoutMs = timeoutMs;
        return this;
    }

    /// <summary>
    /// Configures batch translation settings.
    /// </summary>
    /// <param name="enabled">Whether batch translation is enabled</param>
    /// <param name="maxBatchSize">Maximum batch size</param>
    /// <returns>The builder for chaining</returns>
    public ProtocolTranslationBuilder WithBatchTranslation(bool enabled = true, int maxBatchSize = 10)
    {
        _options.EnableBatchTranslation = enabled;
        _options.MaxBatchSize = maxBatchSize;
        return this;
    }

    /// <summary>
    /// Configures metrics collection.
    /// </summary>
    /// <param name="enabled">Whether metrics collection is enabled</param>
    /// <returns>The builder for chaining</returns>
    public ProtocolTranslationBuilder WithMetrics(bool enabled = true)
    {
        _options.EnableMetrics = enabled;
        return this;
    }

    /// <summary>
    /// Adds a custom translator to the service collection.
    /// </summary>
    /// <typeparam name="TSource">Source message type</typeparam>
    /// <typeparam name="TTarget">Target message type</typeparam>
    /// <typeparam name="TTranslator">Translator implementation type</typeparam>
    /// <returns>The builder for chaining</returns>
    public ProtocolTranslationBuilder AddTranslator<TSource, TTarget, TTranslator>()
        where TTranslator : class, IMessageTranslator<TSource, TTarget>
    {
        _ = _services.AddTransient<IMessageTranslator<TSource, TTarget>, TTranslator>();
        return this;
    }

    /// <summary>
    /// Adds a custom cache implementation.
    /// </summary>
    /// <typeparam name="TCache">Cache implementation type</typeparam>
    /// <returns>The builder for chaining</returns>
    public ProtocolTranslationBuilder WithCache<TCache>()
        where TCache : class, ITranslationCache
    {
        _ = _services.AddSingleton<ITranslationCache, TCache>();
        return this;
    }

    /// <summary>
    /// Builds and finalizes the configuration.
    /// </summary>
    /// <returns>The service collection</returns>
    public IServiceCollection Build()
    {
        return _services;
    }
}

/// <summary>
/// Extension methods for more advanced configuration.
/// </summary>
public static class AdvancedProtocolTranslationExtensions
{
    /// <summary>
    /// Adds protocol translation with a fluent configuration builder.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Configuration delegate</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddProtocolTranslationWithBuilder(
        this IServiceCollection services,
        Action<ProtocolTranslationBuilder> configure)
    {
        var options = new TranslationOptions();
        var builder = new ProtocolTranslationBuilder(services, options);

        configure(builder);

        _ = services.AddSingleton(options);
        _ = services.AddSingleton<IProtocolTranslationService, ProtocolTranslationService>();

        return builder.Build();
    }

    /// <summary>
    /// Validates the protocol translation configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>Validation result</returns>
    public static ValidationResult ValidateProtocolTranslationConfiguration(this IServiceCollection services)
    {
        var errors = new List<string>();

        // Check if core service is registered
        if (!services.Any(s => s.ServiceType == typeof(IProtocolTranslationService)))
        {
            errors.Add("IProtocolTranslationService is not registered");
        }

        // Check if cache is registered
        if (!services.Any(s => s.ServiceType == typeof(ITranslationCache)))
        {
            errors.Add("ITranslationCache is not registered");
        }

        // Check if at least one translator is registered
        var translatorServices = services.Where(s =>
            s.ServiceType.IsGenericType &&
            s.ServiceType.GetGenericTypeDefinition() == typeof(IMessageTranslator<,>));

        if (!translatorServices.Any())
        {
            errors.Add("No translators are registered");
        }

        return new ValidationResult(errors.Count == 0, errors);
    }

    /// <summary>
    /// Gets information about registered translators.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>Information about registered translators</returns>
    public static IEnumerable<TranslatorInfo> GetRegisteredTranslators(this IServiceCollection services)
    {
        return services
            .Where(s => s.ServiceType.IsGenericType &&
                       s.ServiceType.GetGenericTypeDefinition() == typeof(IMessageTranslator<,>))
            .Select(s => new TranslatorInfo
            {
                ServiceType = s.ServiceType,
                ImplementationType = s.ImplementationType,
                SourceType = s.ServiceType.GetGenericArguments()[0],
                TargetType = s.ServiceType.GetGenericArguments()[1],
                Lifetime = s.Lifetime
            });
    }
}

/// <summary>
/// Information about a registered translator.
/// </summary>
public class TranslatorInfo
{
    /// <summary>
    /// The service interface type.
    /// </summary>
    public Type? ServiceType { get; set; }

    /// <summary>
    /// The implementation type.
    /// </summary>
    public Type? ImplementationType { get; set; }

    /// <summary>
    /// The source message type.
    /// </summary>
    public Type? SourceType { get; set; }

    /// <summary>
    /// The target message type.
    /// </summary>
    public Type? TargetType { get; set; }

    /// <summary>
    /// The service lifetime.
    /// </summary>
    public ServiceLifetime Lifetime { get; set; }
}

/// <summary>
/// Validation result for service configuration.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Whether the validation passed.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Validation error messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Initializes a new validation result.
    /// </summary>
    /// <param name="isValid">Whether validation passed</param>
    /// <param name="errors">Validation errors</param>
    public ValidationResult(bool isValid, IEnumerable<string> errors)
    {
        IsValid = isValid;
        Errors = errors.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets the first error message or empty string if valid.
    /// </summary>
    public string FirstError => Errors.Count > 0 ? Errors[0] : string.Empty;

    /// <summary>
    /// Gets all error messages joined with a separator.
    /// </summary>
    /// <param name="separator">Separator to use between errors</param>
    /// <returns>Combined error messages</returns>
    public string GetErrorsString(string separator = "; ") => string.Join(separator, Errors);
}
