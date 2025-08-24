using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AchieveAi.LmDotnetTools.LmCore.Middleware;
using AchieveAi.LmDotnetTools.McpMiddleware;
using AchieveAi.LmDotnetTools.McpMiddleware.Extensions;
using AIChat.Server.Functions;
using static AchieveAi.LmDotnetTools.Misc.Utils.TaskManager;

namespace AIChat.Server.Services;

/// <summary>
/// Service responsible for managing tool registrations and creating function call middleware
/// </summary>
public class ToolingService : IToolingService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMcpClientManager _mcpClientManager;
    private readonly ITaskManagerService _taskManagerService;
    private readonly IModeService _modeService;
    private readonly ILogger<ToolingService> _logger;

    public ToolingService(
        IServiceProvider serviceProvider,
        IMcpClientManager mcpClientManager,
        ITaskManagerService taskManagerService,
        IModeService modeService,
        ILogger<ToolingService> logger)
    {
        _serviceProvider = serviceProvider;
        _mcpClientManager = mcpClientManager;
        _taskManagerService = taskManagerService;
        _modeService = modeService;
        _logger = logger;
    }

    public async Task<FunctionCallMiddleware?> CreateChatSpecificFunctionCallMiddlewareAsync(
        string chatId,
        string? modeId = null,
        string? userId = null,
        IToolResultCallback? resultCallback = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating chat-specific FunctionCallMiddleware for chat {ChatId} with mode {ModeId}", 
                chatId, modeId ?? "default");
            
            // Create function registry
            var registry = new FunctionRegistry();
            
            // Add weather function provider
            var weatherLogger = _serviceProvider.GetRequiredService<ILogger<WeatherFunction>>();
            var weatherProvider = new WeatherFunction(weatherLogger);
            registry.AddProvider(weatherProvider);
            _logger.LogInformation("Added WeatherFunction provider to registry");
            
            // Get or create TaskManager for this specific chat
            var taskManager = await _taskManagerService.GetTaskManagerAsync(chatId);
            registry.AddFunctionsFromObject(taskManager, "TaskManager");
            _logger.LogInformation("Added TaskManager functions for chat {ChatId}", chatId);
            
            // Add MCP clients to the registry
            try
            {
                var mcpClients = await _mcpClientManager.GetActiveClientsAsync(cancellationToken);
                if (mcpClients.Any())
                {
                    var mcpLogger = _serviceProvider.GetService<ILogger<McpClientFunctionProvider>>();
                    await registry.AddMcpClientsAsync(mcpClients, "McpServers", mcpLogger);
                    _logger.LogInformation("Added {Count} MCP clients to function registry", mcpClients.Count);
                }
                else
                {
                    _logger.LogInformation("No MCP clients configured or available");
                }
            }
            catch (Exception mcpEx)
            {
                _logger.LogError(mcpEx, "Failed to add MCP clients to function registry");
            }
            
            // Build function contracts and handlers with conflict resolution
            registry.WithConflictResolution(ConflictResolution.PreferMcp);
            var (contracts, handlers) = registry.Build();
            
            // Apply mode-based tool filtering if modeId and userId are provided
            if (!string.IsNullOrEmpty(modeId) && !string.IsNullOrEmpty(userId))
            {
                var availableToolNames = contracts.Select(c => c.Name).ToList();
                var filterResult = await _modeService.FilterToolsByModeAsync(modeId, userId, availableToolNames);
                
                if (filterResult.Success)
                {
                    var allowedTools = filterResult.FilteredTools.ToHashSet();
                    contracts = contracts.Where(c => allowedTools.Contains(c.Name)).ToArray();
                    
                    // Filter handlers to match filtered contracts
                    var allowedContractNames = contracts.Select(c => c.Name).ToHashSet();
                    var filteredHandlers = handlers.Where(kvp => allowedContractNames.Contains(kvp.Key))
                                                 .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    handlers = filteredHandlers;
                    
                    _logger.LogInformation("Applied mode {ModeId} tool filtering: {FilteredCount} of {TotalCount} tools allowed", 
                        modeId, contracts.Count(), availableToolNames.Count);
                }
                else
                {
                    _logger.LogWarning("Failed to apply tool filtering for mode {ModeId}: {Error}", modeId, filterResult.Error);
                }
            }
            
            if (!contracts.Any())
            {
                _logger.LogWarning("No functions registered for FunctionCallMiddleware");
                return null;
            }
            
            _logger.LogInformation("Registered {Count} functions for tool calling in chat {ChatId}", contracts.Count(), chatId);
            foreach (var contract in contracts)
            {
                _logger.LogDebug("Registered function: {Name} - {Description}", contract.Name, contract.Description);
            }
            
            // Create middleware with callback
            var middlewareLogger = _serviceProvider.GetRequiredService<ILogger<FunctionCallMiddleware>>();
            var middleware = new FunctionCallMiddleware(
                contracts,
                handlers,
                name: "FunctionCall",
                logger: middlewareLogger,
                resultCallback: resultCallback);
            
            _logger.LogInformation("FunctionCallMiddleware created successfully for chat {ChatId}", chatId);    
            return middleware;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize FunctionCallMiddleware for chat {ChatId}", chatId);
            return null;
        }
    }
}