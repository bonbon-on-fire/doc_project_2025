using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIChat.Server.Services.ResponseCaching;

/// <summary>
/// Interface for generating cache keys for response caching operations.
/// Follows the Single Responsibility Principle by focusing solely on cache key generation logic.
/// </summary>
public interface ICacheKeyGenerator
{
    /// <summary>
    /// Generates a cache key for a router operation with the specified parameters.
    /// </summary>
    /// <param name="routerType">The type of router (e.g., "Mode", "Chat", "Monitoring")</param>
    /// <param name="operationName">The name of the operation being cached</param>
    /// <param name="parameters">The operation parameters (will be hashed for key generation)</param>
    /// <param name="userContext">Optional user context for user-specific caching</param>
    /// <param name="version">Cache schema version for evolution support</param>
    /// <returns>A unique cache key for the operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when routerType or operationName is null</exception>
    string GenerateKey(string routerType, string operationName, object? parameters = null, string? userContext = null, string version = "v1");

    /// <summary>
    /// Generates an invalidation pattern for bulk cache invalidation.
    /// </summary>
    /// <param name="routerType">The type of router to invalidate</param>
    /// <param name="operationName">Optional operation name to limit invalidation scope</param>
    /// <param name="userContext">Optional user context to limit invalidation scope</param>
    /// <param name="version">Optional version to limit invalidation scope</param>
    /// <returns>A pattern string for cache invalidation</returns>
    /// <exception cref="ArgumentNullException">Thrown when routerType is null</exception>
    string GenerateInvalidationPattern(string routerType, string? operationName = null, string? userContext = null, string? version = null);

    /// <summary>
    /// Validates whether a cache key is valid and follows the expected format.
    /// </summary>
    /// <param name="cacheKey">The cache key to validate</param>
    /// <returns>True if the cache key is valid, false otherwise</returns>
    bool IsValidCacheKey(string cacheKey);

    /// <summary>
    /// Extracts metadata from a cache key for debugging and monitoring purposes.
    /// </summary>
    /// <param name="cacheKey">The cache key to parse</param>
    /// <returns>Cache key metadata including router type, operation, and context</returns>
    /// <exception cref="ArgumentNullException">Thrown when cacheKey is null</exception>
    /// <exception cref="ArgumentException">Thrown when cacheKey format is invalid</exception>
    CacheKeyMetadata ExtractMetadata(string cacheKey);
}

/// <summary>
/// Metadata extracted from a cache key for analysis and debugging.
/// </summary>
public record CacheKeyMetadata
{
    /// <summary>
    /// Gets the router type from the cache key.
    /// </summary>
    public required string RouterType { get; init; }

    /// <summary>
    /// Gets the operation name from the cache key.
    /// </summary>
    public required string OperationName { get; init; }

    /// <summary>
    /// Gets the parameter hash from the cache key.
    /// </summary>
    public required string ParameterHash { get; init; }

    /// <summary>
    /// Gets the user context from the cache key.
    /// </summary>
    public string? UserContext { get; init; }

    /// <summary>
    /// Gets the version from the cache key.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets the original cache key that was parsed.
    /// </summary>
    public required string OriginalKey { get; init; }
}

/// <summary>
/// Production implementation of cache key generator with deterministic hashing and validation.
/// Uses SHA256 for parameter hashing to ensure consistent cache keys across application instances.
/// Thread-safe implementation using static SHA256 methods for optimal performance and concurrency.
/// </summary>
public class DefaultCacheKeyGenerator : ICacheKeyGenerator
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<DefaultCacheKeyGenerator> _logger;

    /// <summary>
    /// Initializes a new instance of the DefaultCacheKeyGenerator class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information</param>
    public DefaultCacheKeyGenerator(ILogger<DefaultCacheKeyGenerator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            // Ensure deterministic serialization for consistent cache keys
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Generates a cache key using hierarchical structure: {RouterType}:{OperationName}:{ParameterHash}:{UserContext}:{Version}
    /// Thread-safe implementation that can be called concurrently from multiple threads.
    /// </summary>
    public string GenerateKey(string routerType, string operationName, object? parameters = null, string? userContext = null, string version = "v1")
    {
        ArgumentException.ThrowIfNullOrEmpty(routerType);
        ArgumentException.ThrowIfNullOrEmpty(operationName);
        ArgumentException.ThrowIfNullOrEmpty(version);

        try
        {
            var parameterHash = GenerateParameterHash(parameters);
            var context = string.IsNullOrEmpty(userContext) ? "system" : userContext;

            var cacheKey = $"{routerType}:{operationName}:{parameterHash}:{context}:{version}";

            _logger.LogTrace("Generated cache key {CacheKey} for {RouterType}.{OperationName}",
                cacheKey, routerType, operationName);

            return cacheKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating cache key for {RouterType}.{OperationName}",
                routerType, operationName);
            throw;
        }
    }

    /// <summary>
    /// Generates invalidation pattern with wildcards for flexible bulk invalidation.
    /// </summary>
    public string GenerateInvalidationPattern(string routerType, string? operationName = null, string? userContext = null, string? version = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(routerType);

        var pattern = $"{routerType}:";
        pattern += string.IsNullOrEmpty(operationName) ? "*:" : $"{operationName}:";
        pattern += "*:"; // Parameter hash is always wildcarded for invalidation
        pattern += string.IsNullOrEmpty(userContext) ? "*:" : $"{userContext}:";
        pattern += string.IsNullOrEmpty(version) ? "*" : version;

        _logger.LogTrace("Generated invalidation pattern {Pattern} for {RouterType}", pattern, routerType);

        return pattern;
    }

    /// <summary>
    /// Validates cache key format using regex pattern matching.
    /// </summary>
    public bool IsValidCacheKey(string cacheKey)
    {
        if (string.IsNullOrEmpty(cacheKey))
        {
            return false;
        }

        // Expected format: RouterType:OperationName:ParameterHash:UserContext:Version
        var parts = cacheKey.Split(':');
        if (parts.Length != 5)
        {
            return false;
        }

        // Validate each part is non-empty
        return parts.All(part => !string.IsNullOrEmpty(part));
    }

    /// <summary>
    /// Extracts metadata from cache key by parsing the hierarchical structure.
    /// </summary>
    public CacheKeyMetadata ExtractMetadata(string cacheKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheKey);

        if (!IsValidCacheKey(cacheKey))
        {
            throw new ArgumentException($"Invalid cache key format: {cacheKey}", nameof(cacheKey));
        }

        var parts = cacheKey.Split(':');

        return new CacheKeyMetadata
        {
            RouterType = parts[0],
            OperationName = parts[1],
            ParameterHash = parts[2],
            UserContext = parts[3] == "system" ? null : parts[3],
            Version = parts[4],
            OriginalKey = cacheKey
        };
    }

    /// <summary>
    /// Generates a deterministic hash of operation parameters using thread-safe SHA256.
    /// Uses static SHA256.HashData for optimal performance and thread safety.
    /// </summary>
    private string GenerateParameterHash(object? parameters)
    {
        if (parameters == null)
        {
            return "null";
        }

        try
        {
            var json = JsonSerializer.Serialize(parameters, _jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            var hashBytes = SHA256.HashData(bytes);
            return Convert.ToHexString(hashBytes)[..16]; // Use first 16 characters for brevity
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error hashing parameters for cache key, using fallback hash");

            // Fallback to simple string hash for non-serializable objects
            var fallback = parameters.ToString() ?? "unknown";
            var fallbackBytes = Encoding.UTF8.GetBytes(fallback);
            var hashBytes = SHA256.HashData(fallbackBytes);
            return Convert.ToHexString(hashBytes)[..16];
        }
    }
}