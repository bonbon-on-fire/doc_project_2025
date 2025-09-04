using AIChat.Server.Models;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services;

/// <summary>
/// Validates MCP configuration at startup
/// </summary>
public interface IMcpConfigurationValidator
{
    /// <summary>
    /// Validates the MCP configuration
    /// </summary>
    /// <returns>True if configuration is valid, false otherwise</returns>
    bool Validate(out List<string> errors);
}

public class McpConfigurationValidator(
    IOptions<McpConfiguration> configuration,
    ILogger<McpConfigurationValidator> logger
) : IMcpConfigurationValidator
{
    private readonly McpConfiguration _configuration = configuration.Value;

    public bool Validate(out List<string> errors)
    {
        errors = [];

        if (_configuration == null)
        {
            errors.Add("MCP configuration section is missing");
            return false;
        }

        // Validate MCP servers if any are configured
        if (_configuration.McpServers != null && _configuration.McpServers.Count != 0)
        {
            foreach (var (serverName, serverConfig) in _configuration.McpServers)
            {
                ValidateServerConfig(serverName, serverConfig, errors);
            }
        }
        else
        {
            logger.LogInformation("No MCP servers configured");
        }

        // Validate inputs if referenced by any server
        if (
            _configuration.McpServers != null
            && _configuration.McpServers.Any(s => s.Value.Env != null)
        )
        {
            ValidateInputs(errors);
        }

        // Log all validation errors
        if (errors.Count != 0)
        {
            foreach (var error in errors)
            {
                logger.LogError("MCP Configuration validation error: {Error}", error);
            }
            return false;
        }

        logger.LogInformation("MCP configuration validation passed");
        return true;
    }

    private void ValidateServerConfig(
        string serverName,
        McpServerConfig config,
        List<string> errors
    )
    {
        if (config == null)
        {
            errors.Add($"Server '{serverName}' configuration is null");
            return;
        }

        // Skip validation for disabled servers
        if (!config.Enabled)
        {
            logger.LogDebug("Server '{ServerName}' is disabled, skipping validation", serverName);
            return;
        }

        // Validate transport type
        if (string.IsNullOrWhiteSpace(config.Type))
        {
            errors.Add($"Server '{serverName}' is missing transport type");
        }
        else
        {
            var validTypes = new[] { "stdio", "sse", "http" };
            if (!validTypes.Contains(config.Type.ToLowerInvariant()))
            {
                errors.Add(
                    $"Server '{serverName}' has invalid transport type '{config.Type}'. Valid types: {string.Join(", ", validTypes)}"
                );
            }
        }

        // Validate stdio-specific requirements
        if (config.Type?.ToLowerInvariant() == "stdio")
        {
            if (string.IsNullOrWhiteSpace(config.Command))
            {
                errors.Add($"Server '{serverName}' with stdio transport requires a command");
            }
        }

        // Validate SSE/HTTP-specific requirements (for future use)
        if (config.Type?.ToLowerInvariant() is "sse" or "http")
        {
            // Currently not supported, will be validated when implemented
            logger.LogWarning(
                "Server '{ServerName}' uses {Type} transport which is not yet implemented",
                serverName,
                config.Type
            );
        }

        // Validate environment variables if present
        if (config.Env != null)
        {
            foreach (var (key, value) in config.Env)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    errors.Add($"Server '{serverName}' has empty environment variable key");
                }

                // Check for input references
                if (value?.StartsWith("${input:") == true && value.EndsWith("}"))
                {
                    var inputId = value[8..^1];
                    if (
                        _configuration.Inputs == null
                        || !_configuration.Inputs.Any(i => i.Id == inputId)
                    )
                    {
                        errors.Add($"Server '{serverName}' references undefined input '{inputId}'");
                    }
                }
            }
        }
    }

    private void ValidateInputs(List<string> errors)
    {
        if (_configuration.Inputs == null)
        {
            return;
        }

        var seenIds = new HashSet<string>();
        foreach (var input in _configuration.Inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Id))
            {
                errors.Add("Input configuration has empty ID");
                continue;
            }

            if (!seenIds.Add(input.Id))
            {
                errors.Add($"Duplicate input ID '{input.Id}'");
            }

            if (string.IsNullOrWhiteSpace(input.Description))
            {
                logger.LogWarning("Input '{InputId}' has no description", input.Id);
            }
        }
    }
}
