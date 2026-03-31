using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using AIChat.Server.Models;

namespace AIChat.Server.Services;

public interface IMcpClientManager
{
    Task<Dictionary<string, IMcpClient>> GetActiveClientsAsync(CancellationToken cancellationToken = default);
    Task InitializeClientsAsync(CancellationToken cancellationToken = default);
    Task ShutdownClientsAsync();
    bool IsInitialized { get; }
}

public class McpClientManager : IMcpClientManager, IDisposable
{
    private readonly McpConfiguration _configuration;
    private readonly IConfiguration _appConfiguration;
    private readonly ILogger<McpClientManager> _logger;
    private readonly Dictionary<string, IMcpClient> _clients = new();
    private readonly Dictionary<string, IClientTransport> _transports = new();
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _isInitialized;
    private bool _disposed;

    public bool IsInitialized => _isInitialized;

    public McpClientManager(
        IOptions<McpConfiguration> configuration,
        IConfiguration appConfiguration,
        ILogger<McpClientManager> logger)
    {
        _configuration = configuration.Value;
        _appConfiguration = appConfiguration;
        _logger = logger;
    }

    public async Task InitializeClientsAsync(CancellationToken cancellationToken = default)
    {
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                _logger.LogInformation("MCP clients already initialized");
                return;
            }

            _logger.LogInformation("Initializing MCP clients from configuration");

            foreach (var (serverName, serverConfig) in _configuration.McpServers)
            {
                if (!serverConfig.Enabled)
                {
                    _logger.LogInformation("Skipping disabled MCP server: {ServerName}", serverName);
                    continue;
                }

                try
                {
                    await InitializeClientAsync(serverName, serverConfig, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize MCP client for server: {ServerName}", serverName);
                }
            }

            _isInitialized = true;
            _logger.LogInformation("MCP client initialization completed. Active clients: {ClientCount}", _clients.Count);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task InitializeClientAsync(string serverName, McpServerConfig config, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing MCP client for server: {ServerName} (Type: {Type})", serverName, config.Type);

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
                throw new NotSupportedException($"Unsupported MCP transport type: {config.Type}");
        }

        try
        {
            var client = await McpClientFactory.CreateAsync(transport);
            
            _clients[serverName] = client;
            _transports[serverName] = transport;

            var tools = await client.ListToolsAsync();
            _logger.LogInformation("Successfully connected to MCP server: {ServerName}. Available tools: {ToolCount}", 
                serverName, tools.Count);
            
            foreach (var tool in tools)
            {
                _logger.LogDebug("  - {ToolName}: {ToolDescription}", tool.Name, tool.Description);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MCP client for server: {ServerName}", serverName);
            if (transport is IDisposable disposableTransport)
                disposableTransport.Dispose();
            throw;
        }
    }

    private IClientTransport CreateStdioTransport(string serverName, McpServerConfig config)
    {
        if (string.IsNullOrEmpty(config.Command))
        {
            throw new InvalidOperationException($"Command is required for stdio transport in server: {serverName}");
        }

        var options = new StdioClientTransportOptions
        {
            Name = serverName,
            Command = config.Command,
            Arguments = config.Args?.ToArray() ?? Array.Empty<string>(),
            WorkingDirectory = config.WorkingDirectory
        };

        if (config.Env != null)
        {
            options.EnvironmentVariables = ResolveEnvironmentVariables(config.Env);
        }

        _logger.LogDebug("Creating stdio transport: Command={Command}, Args={Args}", 
            options.Command, string.Join(" ", options.Arguments));

        return new StdioClientTransport(options);
    }

    private IClientTransport CreateSseTransport(string serverName, McpServerConfig config)
    {
        // SSE/HTTP transport is not yet available in the current ModelContextProtocol.Client version
        // This is a placeholder for future implementation when the SDK supports it
        _logger.LogWarning("SSE/HTTP transport is not yet supported in the current ModelContextProtocol.Client version. Server {ServerName} will be skipped.", serverName);
        throw new NotSupportedException($"SSE/HTTP transport is not yet supported. Please use stdio transport for server: {serverName}");
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
                var inputId = value.Substring(8, value.Length - 9);
                var inputConfig = _configuration.Inputs?.FirstOrDefault(i => i.Id == inputId);
                
                if (inputConfig != null)
                {
                    var envVarName = inputConfig.DefaultValue ?? inputId.ToUpper().Replace("-", "_");
                    
                    // Try User Secrets/IConfiguration first, then environment variable
                    expandedValue = _appConfiguration[envVarName] 
                        ?? Environment.GetEnvironmentVariable(envVarName) 
                        ?? string.Empty;
                    
                    if (string.IsNullOrEmpty(expandedValue) && !string.IsNullOrEmpty(inputConfig.DefaultValue))
                    {
                        expandedValue = inputConfig.DefaultValue;
                    }
                }
            }
            else if (value.StartsWith("${") && value.EndsWith("}"))
            {
                // Handle variable references with optional defaults
                var variableExpression = value.Substring(2, value.Length - 3);
                var parts = variableExpression.Split(new[] { ":-" }, 2, StringSplitOptions.None);
                var variableName = parts[0];
                var defaultValue = parts.Length > 1 ? parts[1] : string.Empty;
                
                // Try User Secrets/IConfiguration first, then environment variable, then default
                expandedValue = _appConfiguration[variableName] 
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

    public async Task<Dictionary<string, IMcpClient>> GetActiveClientsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeClientsAsync(cancellationToken);
        }

        return new Dictionary<string, IMcpClient>(_clients);
    }

    public async Task ShutdownClientsAsync()
    {
        _logger.LogInformation("Shutting down MCP clients");

        foreach (var (serverName, transport) in _transports)
        {
            try
            {
                if (transport is IDisposable disposable)
                    disposable.Dispose();
                _logger.LogDebug("Disposed transport for server: {ServerName}", serverName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing transport for server: {ServerName}", serverName);
            }
        }

        _clients.Clear();
        _transports.Clear();
        _isInitialized = false;

        await Task.CompletedTask;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                ShutdownClientsAsync().GetAwaiter().GetResult();
                _initializationLock?.Dispose();
            }
            _disposed = true;
        }
    }
}