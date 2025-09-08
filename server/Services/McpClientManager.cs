using AIChat.Server.Exceptions;
using AIChat.Server.Models;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace AIChat.Server.Services;

public interface IMcpClientManager
{
    Task<Dictionary<string, IMcpClient>> GetActiveClientsAsync(
        CancellationToken cancellationToken = default
    );
    Task InitializeClientsAsync(CancellationToken cancellationToken = default);
    Task ShutdownClientsAsync();
    bool IsInitialized { get; }
}

public class McpClientManager(
    IOptions<McpConfiguration> configuration,
    IConfiguration appConfiguration,
    ILogger<McpClientManager> logger
) : IMcpClientManager, IAsyncDisposable
{
    private readonly McpConfiguration _configuration = configuration.Value;
    private readonly Dictionary<string, IMcpClient> _clients = [];
    private readonly Dictionary<string, IClientTransport> _transports = [];
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _disposed;

    public bool IsInitialized { get; private set; }

    private static readonly string[] separator = [":-"];

    public async Task InitializeClientsAsync(CancellationToken cancellationToken = default)
    {
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (IsInitialized)
            {
                logger.LogInformation("MCP clients already initialized");
                return;
            }

            logger.LogInformation("Initializing MCP clients from configuration");

            foreach (var (serverName, serverConfig) in _configuration.McpServers)
            {
                if (!serverConfig.Enabled)
                {
                    logger.LogInformation("Skipping disabled MCP server: {ServerName}", serverName);
                    continue;
                }

                try
                {
                    await InitializeClientAsync(serverName, serverConfig, cancellationToken);
                }
                catch (McpException)
                {
                    // Already logged with specific context, just rethrow
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Unexpected error initializing MCP client for server: {ServerName}",
                        serverName
                    );
                    throw new McpInitializationException(
                        $"Failed to initialize MCP client for server '{serverName}'",
                        serverName,
                        ex
                    );
                }
            }

            IsInitialized = true;
            logger.LogInformation(
                "MCP client initialization completed. Active clients: {ClientCount}",
                _clients.Count
            );
        }
        finally
        {
            _ = _initializationLock.Release();
        }
    }

    private async Task InitializeClientAsync(
        string serverName,
        McpServerConfig config,
        CancellationToken cancellationToken
    )
    {
        logger.LogInformation(
            "Initializing MCP client for server: {ServerName} (Type: {Type})",
            serverName,
            config.Type
        );

        IClientTransport transport;

        switch (config.Type.ToLowerInvariant())
        {
            case "stdio":
                transport = CreateStdioTransport(serverName, config);
                break;

            case "sse":
            case "http":
                transport = CreateSseTransport(serverName, config);
                break;

            default:
                var errorMsg =
                    $"Transport type '{config.Type}' is not supported. Supported types: stdio, sse, http";
                logger.LogError(errorMsg + " for server: {ServerName}", serverName);
                throw new McpTransportException(errorMsg, serverName, config.Type);
        }

        try
        {
            var client = await McpClientFactory.CreateAsync(
                transport,
                cancellationToken: cancellationToken
            );

            _clients[serverName] = client;
            _transports[serverName] = transport;

            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            logger.LogInformation(
                "Successfully connected to MCP server: {ServerName}. Available tools: {ToolCount}",
                serverName,
                tools.Count
            );

            foreach (var tool in tools)
            {
                logger.LogDebug("  - {ToolName}: {ToolDescription}", tool.Name, tool.Description);
            }
        }
        catch (McpException)
        {
            // Already a specific MCP exception, just clean up and rethrow
            if (transport is IDisposable disposableTransport)
            {
                disposableTransport.Dispose();
            }

            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create MCP client for server: {ServerName}", serverName);
            if (transport is IDisposable disposableTransport)
            {
                disposableTransport.Dispose();
            }

            throw new McpConnectionException(
                $"Failed to establish connection to MCP server '{serverName}'",
                serverName,
                ex
            );
        }
    }

    private IClientTransport CreateStdioTransport(string serverName, McpServerConfig config)
    {
        if (string.IsNullOrEmpty(config.Command))
        {
            var errorMsg = "Command is required for stdio transport";
            logger.LogError("{Error} for server: {ServerName}", errorMsg, serverName);
            throw new McpConfigurationException(
                $"{errorMsg} in server configuration for '{serverName}'",
                $"Mcp:McpServers:{serverName}:Command"
            );
        }

        var options = new StdioClientTransportOptions
        {
            Name = serverName,
            Command = config.Command,
            Arguments = config.Args?.ToArray() ?? [],
            WorkingDirectory = config.WorkingDirectory,
        };

        if (config.Env != null)
        {
            options.EnvironmentVariables = ResolveEnvironmentVariables(config.Env);
        }

        logger.LogDebug(
            "Creating stdio transport: Command={Command}, Args={Args}",
            options.Command,
            string.Join(" ", options.Arguments)
        );

        return new StdioClientTransport(options);
    }

    private IClientTransport CreateSseTransport(string serverName, McpServerConfig config)
    {
        // SSE/HTTP transport is not yet available in the current ModelContextProtocol.Client version
        // This is a placeholder for future implementation when the SDK supports it
        var errorMsg =
            "SSE/HTTP transport is not yet supported in the current ModelContextProtocol.Client version";
        logger.LogWarning("{Error}. Server {ServerName} will be skipped.", errorMsg, serverName);
        throw new McpTransportException(
            $"{errorMsg}. Please use stdio transport for server: {serverName}",
            serverName,
            config.Type
        );
    }

    private Dictionary<string, string?> ResolveEnvironmentVariables(Dictionary<string, string> env)
    {
        var resolved = new Dictionary<string, string?>();

        foreach (var (key, value) in env)
        {
            var expandedValue = value;

            if (value.StartsWith("${input:") && value.EndsWith("}"))
            {
                // Handle input references
                var inputId = value[8..^1];
                var inputConfig = _configuration.Inputs?.FirstOrDefault(i => i.Id == inputId);

                if (inputConfig != null)
                {
                    var envVarName =
                        inputConfig.DefaultValue ?? inputId.ToUpper().Replace("-", "_");

                    // Try User Secrets/IConfiguration first, then environment variable
                    expandedValue =
                        appConfiguration[envVarName]
                        ?? Environment.GetEnvironmentVariable(envVarName)
                        ?? string.Empty;

                    if (
                        string.IsNullOrEmpty(expandedValue)
                        && !string.IsNullOrEmpty(inputConfig.DefaultValue)
                    )
                    {
                        expandedValue = inputConfig.DefaultValue;
                    }
                }
            }
            else if (value.StartsWith("${") && value.EndsWith("}"))
            {
                // Handle variable references with optional defaults
                var variableExpression = value[2..^1];
                var parts = variableExpression.Split(separator, 2, StringSplitOptions.None);
                var variableName = parts[0];
                var defaultValue = parts.Length > 1 ? parts[1] : string.Empty;

                // Try User Secrets/IConfiguration first, then environment variable, then default
                expandedValue =
                    appConfiguration[variableName]
                    ?? Environment.GetEnvironmentVariable(variableName)
                    ?? defaultValue;
            }
            else
            {
                // For non-variable strings, just return as-is
                expandedValue = value;
            }

            resolved[key] = expandedValue;
        }

        return resolved;
    }

    public async Task<Dictionary<string, IMcpClient>> GetActiveClientsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!IsInitialized)
        {
            await InitializeClientsAsync(cancellationToken);
        }

        return new Dictionary<string, IMcpClient>(_clients);
    }

    public async Task ShutdownClientsAsync()
    {
        logger.LogInformation("Shutting down MCP clients");

        foreach (var (serverName, transport) in _transports)
        {
            try
            {
                if (transport is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                logger.LogDebug("Disposed transport for server: {ServerName}", serverName);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error disposing transport for server: {ServerName}",
                    serverName
                );
            }
        }

        _clients.Clear();
        _transports.Clear();
        IsInitialized = false;

        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        // Dispose async resources
        await ShutdownClientsAsync().ConfigureAwait(false);

        // Dispose synchronous resources
        _initializationLock?.Dispose();
    }
}
