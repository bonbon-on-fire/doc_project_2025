using Orleans;

namespace AIChat.Orleans.Contracts.Attributes;

/// <summary>
/// Attribute to indicate rate limiting requirements for a grain method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RateLimitAttribute : Attribute
{
    /// <summary>
    /// Gets the maximum number of calls allowed within the time window.
    /// </summary>
    public int MaxCalls { get; }

    /// <summary>
    /// Gets the time window in seconds for the rate limit.
    /// </summary>
    public int WindowSeconds { get; }

    /// <summary>
    /// Gets whether the rate limit is per-user or global.
    /// </summary>
    public bool PerUser { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitAttribute"/> class.
    /// </summary>
    /// <param name="maxCalls">Maximum calls allowed in the window.</param>
    /// <param name="windowSeconds">Time window in seconds.</param>
    /// <param name="perUser">Whether limit is per-user (true) or global (false).</param>
    public RateLimitAttribute(int maxCalls, int windowSeconds = 60, bool perUser = true)
    {
        MaxCalls = maxCalls;
        WindowSeconds = windowSeconds;
        PerUser = perUser;
    }
}

/// <summary>
/// Attribute to indicate telemetry requirements for a grain method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TelemetryAttribute : Attribute
{
    /// <summary>
    /// Gets the telemetry level for this method.
    /// </summary>
    public TelemetryLevel Level { get; }

    /// <summary>
    /// Gets whether to include parameters in telemetry.
    /// </summary>
    public bool IncludeParameters { get; }

    /// <summary>
    /// Gets whether to include result in telemetry.
    /// </summary>
    public bool IncludeResult { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TelemetryAttribute"/> class.
    /// </summary>
    /// <param name="level">Telemetry level.</param>
    /// <param name="includeParameters">Whether to include parameters.</param>
    /// <param name="includeResult">Whether to include result.</param>
    public TelemetryAttribute(
        TelemetryLevel level = TelemetryLevel.Normal,
        bool includeParameters = false,
        bool includeResult = false)
    {
        Level = level;
        IncludeParameters = includeParameters;
        IncludeResult = includeResult;
    }
}

/// <summary>
/// Telemetry levels for grain operations.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.TelemetryLevel")]
public enum TelemetryLevel
{
    /// <summary>
    /// Minimal telemetry - only errors.
    /// </summary>
    [Id(0)]
    Minimal = 0,

    /// <summary>
    /// Normal telemetry - errors and key operations.
    /// </summary>
    [Id(1)]
    Normal = 1,

    /// <summary>
    /// Detailed telemetry - all operations.
    /// </summary>
    [Id(2)]
    Detailed = 2,

    /// <summary>
    /// Verbose telemetry - includes debug information.
    /// </summary>
    [Id(3)]
    Verbose = 3
}

/// <summary>
/// Attribute to indicate caching hints for a grain method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CacheHintAttribute : Attribute
{
    /// <summary>
    /// Gets whether the result is cacheable.
    /// </summary>
    public bool IsCacheable { get; }

    /// <summary>
    /// Gets the cache duration in seconds.
    /// </summary>
    public int DurationSeconds { get; }

    /// <summary>
    /// Gets the cache key pattern.
    /// </summary>
    public string? KeyPattern { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheHintAttribute"/> class.
    /// </summary>
    /// <param name="isCacheable">Whether the result is cacheable.</param>
    /// <param name="durationSeconds">Cache duration in seconds.</param>
    /// <param name="keyPattern">Optional cache key pattern.</param>
    public CacheHintAttribute(bool isCacheable = true, int durationSeconds = 300, string? keyPattern = null)
    {
        IsCacheable = isCacheable;
        DurationSeconds = durationSeconds;
        KeyPattern = keyPattern;
    }
}

/// <summary>
/// Attribute to indicate security requirements for a grain method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SecurityAttribute : Attribute
{
    /// <summary>
    /// Gets whether authorization is required.
    /// </summary>
    public bool RequireAuthorization { get; }

    /// <summary>
    /// Gets the required roles (comma-separated).
    /// </summary>
    public string? RequiredRoles { get; }

    /// <summary>
    /// Gets whether to audit this operation.
    /// </summary>
    public bool Audit { get; }

    /// <summary>
    /// Gets the data classification level.
    /// </summary>
    public DataClassification Classification { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityAttribute"/> class.
    /// </summary>
    /// <param name="requireAuthorization">Whether authorization is required.</param>
    /// <param name="requiredRoles">Required roles.</param>
    /// <param name="audit">Whether to audit.</param>
    /// <param name="classification">Data classification.</param>
    public SecurityAttribute(
        bool requireAuthorization = true,
        string? requiredRoles = null,
        bool audit = true,
        DataClassification classification = DataClassification.Internal)
    {
        RequireAuthorization = requireAuthorization;
        RequiredRoles = requiredRoles;
        Audit = audit;
        Classification = classification;
    }
}

/// <summary>
/// Data classification levels for security.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.DataClassification")]
public enum DataClassification
{
    /// <summary>
    /// Public data.
    /// </summary>
    [Id(0)]
    Public = 0,

    /// <summary>
    /// Internal use only.
    /// </summary>
    [Id(1)]
    Internal = 1,

    /// <summary>
    /// Confidential data.
    /// </summary>
    [Id(2)]
    Confidential = 2,

    /// <summary>
    /// Restricted access data.
    /// </summary>
    [Id(3)]
    Restricted = 3
}