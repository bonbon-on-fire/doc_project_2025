namespace AIChat.Server.Models;

public class McpConfiguration
{
    public Dictionary<string, McpServerConfig> McpServers { get; set; } = [];
    public List<McpInputConfig>? Inputs { get; set; }

    /// <summary>
    /// Configuration for tool filtering and collision handling (legacy name)
    /// </summary>
    [Obsolete("Use FunctionFiltering instead")]
    public McpToolFilterConfig? ToolFiltering { get; set; }

    /// <summary>
    /// Configuration for function filtering and collision handling across all providers
    /// </summary>
    public FunctionFilterConfig? FunctionFiltering { get; set; }
}

public class McpServerConfig
{
    public string Type { get; set; } = "stdio";
    public string? Command { get; set; }
    public List<string>? Args { get; set; }
    public Dictionary<string, string>? Env { get; set; }
    public string? Url { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public string? WorkingDirectory { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// List of tool names allowed from this server (supports wildcards)
    /// </summary>
    public List<string>? AllowedTools { get; set; }

    /// <summary>
    /// List of tool names blocked from this server (supports wildcards)
    /// </summary>
    public List<string>? BlockedTools { get; set; }
}

public class McpInputConfig
{
    public string Type { get; set; } = "promptString";
    public string Id { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Password { get; set; }
    public string? DefaultValue { get; set; }
}

/// <summary>
/// Configuration for MCP tool filtering and collision handling (legacy, use FunctionFilterConfig)
/// </summary>
[Obsolete("Use FunctionFilterConfig instead")]
public class McpToolFilterConfig
{
    /// <summary>
    /// Whether to enable tool filtering based on configuration
    /// </summary>
    public bool EnableFiltering { get; set; }

    /// <summary>
    /// Global list of allowed tool names (supports wildcards)
    /// If specified, only these tools will be available across all servers
    /// </summary>
    public List<string>? GlobalAllowedTools { get; set; }

    /// <summary>
    /// Global list of blocked tool names (supports wildcards)
    /// These tools will be blocked across all servers
    /// </summary>
    public List<string>? GlobalBlockedTools { get; set; }

    /// <summary>
    /// Whether to use prefixes only for tools with name collisions
    /// When true: Only colliding tools get prefixed with server ID
    /// When false: All tools get prefixed with server ID
    /// </summary>
    public bool UsePrefixOnlyForCollisions { get; set; } = true;
}

/// <summary>
/// Configuration for function filtering and collision handling across all providers
/// </summary>
public class FunctionFilterConfig
{
    /// <summary>
    /// Whether to enable function filtering based on configuration
    /// </summary>
    public bool EnableFiltering { get; set; }

    /// <summary>
    /// Global list of allowed function names (supports wildcards)
    /// If specified, only these functions will be available across all providers
    /// </summary>
    public List<string>? GlobalAllowedFunctions { get; set; }

    /// <summary>
    /// Global list of blocked function names (supports wildcards)
    /// These functions will be blocked across all providers
    /// </summary>
    public List<string>? GlobalBlockedFunctions { get; set; }

    /// <summary>
    /// Whether to use prefixes only for functions with name collisions
    /// When true: Only colliding functions get prefixed with provider ID
    /// When false: All functions get prefixed with provider ID
    /// </summary>
    public bool UsePrefixOnlyForCollisions { get; set; } = true;

    /// <summary>
    /// Provider-specific filtering configurations
    /// Key is the provider name (e.g., "McpServers", "WeatherAPI", "TaskManager")
    /// </summary>
    public Dictionary<string, ProviderFilterConfig>? ProviderConfigs { get; set; }
}

/// <summary>
/// Configuration for filtering functions from a specific provider
/// </summary>
public class ProviderFilterConfig
{
    /// <summary>
    /// Whether this provider is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// List of function names allowed from this provider (supports wildcards)
    /// </summary>
    public List<string>? AllowedFunctions { get; set; }

    /// <summary>
    /// List of function names blocked from this provider (supports wildcards)
    /// </summary>
    public List<string>? BlockedFunctions { get; set; }

    /// <summary>
    /// Custom prefix to use for this provider's functions (optional)
    /// If not specified, the provider name will be used
    /// </summary>
    public string? CustomPrefix { get; set; }
}
