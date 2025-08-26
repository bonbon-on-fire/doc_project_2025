using AchieveAi.LmDotnetTools.LmCore.Middleware;
using AchieveAi.LmDotnetTools.McpMiddleware;
using AchieveAi.LmDotnetTools.McpMiddleware.Extensions;
using AIChat.Server.Functions;
using AIChat.Server.Models;
using Microsoft.Extensions.Options;
using LmCoreFunctionFilterConfig = AchieveAi.LmDotnetTools.LmCore.Configuration.FunctionFilterConfig;
using LmCoreProviderFilterConfig = AchieveAi.LmDotnetTools.LmCore.Configuration.ProviderFilterConfig;

namespace AIChat.Server.Services;

/// <summary>
/// Service responsible for managing tool registrations and creating function call middleware
/// </summary>
public class ToolingService(
    IServiceProvider serviceProvider,
    IMcpClientManager mcpClientManager,
    ITaskManagerService taskManagerService,
    IModeService modeService,
    IOptions<McpConfiguration> mcpConfiguration,
    ILogger<ToolingService> logger
    ) : IToolingService
{
    private readonly IMcpClientManager _mcpClientManager = mcpClientManager;

    public async Task<FunctionCallMiddleware?> CreateChatSpecificFunctionCallMiddlewareAsync(
        string chatId,
        string? modeId = null,
        string? userId = null,
        IToolResultCallback? resultCallback = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            logger.LogInformation(
                "Creating chat-specific FunctionCallMiddleware for chat {ChatId} with mode {ModeId}",
                chatId,
                modeId ?? "default"
            );

            // Create function registry
            var registry = new FunctionRegistry();

            // Configure function filtering if available
            var mcpConfig = mcpConfiguration.Value;
            LmCoreFunctionFilterConfig? functionFilterConfig = null;

            // Use new FunctionFiltering if available, otherwise fall back to legacy ToolFiltering
            if (mcpConfig?.FunctionFiltering != null)
            {
                // Map server configuration to LmCore configuration
                functionFilterConfig = new LmCoreFunctionFilterConfig
                {
                    EnableFiltering = mcpConfig.FunctionFiltering.EnableFiltering,
                    GlobalAllowedFunctions = mcpConfig.FunctionFiltering.GlobalAllowedFunctions,
                    GlobalBlockedFunctions = mcpConfig.FunctionFiltering.GlobalBlockedFunctions,
                    UsePrefixOnlyForCollisions = mcpConfig
                        .FunctionFiltering
                        .UsePrefixOnlyForCollisions,
                    ProviderConfigs = new Dictionary<string, LmCoreProviderFilterConfig>(),
                };

                // Map provider configs
                if (mcpConfig.FunctionFiltering.ProviderConfigs != null)
                {
                    foreach (
                        var (providerId, providerConfig) in mcpConfig
                            .FunctionFiltering
                            .ProviderConfigs
                    )
                    {
                        functionFilterConfig.ProviderConfigs[providerId] =
                            new LmCoreProviderFilterConfig
                            {
                                AllowedFunctions = providerConfig.AllowedFunctions,
                                BlockedFunctions = providerConfig.BlockedFunctions,
                                Enabled = providerConfig.Enabled,
                                CustomPrefix = providerConfig.CustomPrefix,
                            };
                    }
                }
            }
            else if (mcpConfig?.ToolFiltering != null)
            {
                // Map legacy configuration to new format
#pragma warning disable CS0618 // Type or member is obsolete
                functionFilterConfig = new LmCoreFunctionFilterConfig
                {
                    EnableFiltering = mcpConfig.ToolFiltering.EnableFiltering,
                    GlobalAllowedFunctions = mcpConfig.ToolFiltering.GlobalAllowedTools,
                    GlobalBlockedFunctions = mcpConfig.ToolFiltering.GlobalBlockedTools,
                    UsePrefixOnlyForCollisions = mcpConfig.ToolFiltering.UsePrefixOnlyForCollisions,
#pragma warning restore CS0618 // Type or member is obsolete
                    ProviderConfigs = new Dictionary<string, LmCoreProviderFilterConfig>(),
                };

                // Map MCP server configs to provider configs
                if (mcpConfig.McpServers != null)
                {
                    foreach (var (serverId, serverConfig) in mcpConfig.McpServers)
                    {
                        functionFilterConfig.ProviderConfigs[serverId] =
                            new LmCoreProviderFilterConfig
                            {
                                AllowedFunctions = serverConfig.AllowedTools,
                                BlockedFunctions = serverConfig.BlockedTools,
                                Enabled = serverConfig.Enabled,
                            };
                    }
                }
            }

            // Apply filtering configuration to registry
            if (functionFilterConfig != null)
            {
                _ = registry.WithFilterConfig(functionFilterConfig).WithLogger(logger);

                if (functionFilterConfig.EnableFiltering)
                {
                    logger.LogInformation(
                        "Function filtering enabled: UsePrefixOnlyForCollisions={UsePrefixOnlyForCollisions}, GlobalAllowed={GlobalAllowedCount}, GlobalBlocked={GlobalBlockedCount}",
                        functionFilterConfig.UsePrefixOnlyForCollisions,
                        functionFilterConfig.GlobalAllowedFunctions?.Count ?? 0,
                        functionFilterConfig.GlobalBlockedFunctions?.Count ?? 0
                    );
                }
            }

            // Add weather function provider
            var weatherLogger = serviceProvider.GetRequiredService<ILogger<WeatherFunction>>();
            var weatherProvider = new WeatherFunction(weatherLogger);
            _ = registry.AddProvider(weatherProvider);
            logger.LogInformation("Added WeatherFunction provider to registry");

            // Get or create TaskManager for this specific chat
            var taskManager = await taskManagerService.GetTaskManagerAsync(chatId, cancellationToken);
            _ = registry.AddFunctionsFromObject(taskManager, "TaskManager");
            logger.LogInformation("Added TaskManager functions for chat {ChatId}", chatId);

            // Add MCP clients to the registry
            try
            {
                var mcpClients = await _mcpClientManager.GetActiveClientsAsync(cancellationToken);
                if (mcpClients.Count != 0)
                {
                    var mcpLogger = serviceProvider.GetService<
                        ILogger<McpClientFunctionProvider>
                    >();

                    // Note: Filtering is now handled at the FunctionRegistry level for ALL providers
                    // We pass null for the filter configs here since they're already configured in the registry
                    _ = await registry.AddMcpClientsAsync(mcpClients, null, null, "McpServers", mcpLogger
, cancellationToken: cancellationToken);

                    logger.LogInformation(
                        "Added {Count} MCP clients to function registry",
                        mcpClients.Count
                    );
                }
                else
                {
                    logger.LogInformation("No MCP clients configured or available");
                }
            }
            catch (Exception mcpEx)
            {
                logger.LogError(mcpEx, "Failed to add MCP clients to function registry");
            }

            // Apply mode-based filtering if modeId and userId are provided
            if (!string.IsNullOrEmpty(modeId) && !string.IsNullOrEmpty(userId))
            {
                // Get the list of all available function names before building
                var allDescriptors = new List<FunctionDescriptor>();
                foreach (var provider in registry.GetProviders())
                {
                    allDescriptors.AddRange(provider.GetFunctions());
                }

                var availableToolNames = allDescriptors.Select(d => d.Contract.Name).ToList();
                var (Success, Error, FilteredTools) = await modeService.FilterToolsByModeAsync(modeId, userId, availableToolNames
, cancellationToken);

                if (Success)
                {
                    // Update or create function filter config to include mode filtering
                    if (functionFilterConfig == null)
                    {
                        functionFilterConfig = new LmCoreFunctionFilterConfig
                        {
                            EnableFiltering = true,
                        };
                    }
                    else if (!functionFilterConfig.EnableFiltering)
                    {
                        functionFilterConfig.EnableFiltering = true;
                    }

                    // Set the global allowed functions based on mode
                    functionFilterConfig.GlobalAllowedFunctions =
                        FilteredTools.ToList();
                    _ = registry.WithFilterConfig(functionFilterConfig);

                    logger.LogInformation(
                        "Applied mode {ModeId} filtering: {FilteredCount} of {TotalCount} functions allowed",
                        modeId,
                        FilteredTools.Count,
                        availableToolNames.Count
                    );
                }
                else
                {
                    logger.LogWarning(
                        "Failed to apply filtering for mode {ModeId}: {Error}",
                        modeId,
                        Error
                    );
                }
            }

            // Build function contracts and handlers with conflict resolution
            _ = registry.WithConflictResolution(ConflictResolution.PreferMcp);
            var (contracts, handlers) = registry.Build();

            if (!contracts.Any())
            {
                logger.LogWarning("No functions registered for FunctionCallMiddleware");
                return null;
            }

            logger.LogInformation(
                "Registered {Count} functions for tool calling in chat {ChatId}",
                contracts.Count(),
                chatId
            );
            foreach (var contract in contracts)
            {
                logger.LogDebug(
                    "Registered function: {Name} - {Description}",
                    contract.Name,
                    contract.Description
                );
            }

            // Create middleware with callback
            var middlewareLogger = serviceProvider.GetRequiredService<
                ILogger<FunctionCallMiddleware>
            >();
            var middleware = new FunctionCallMiddleware(
                contracts,
                handlers,
                name: "FunctionCall",
                logger: middlewareLogger,
                resultCallback: resultCallback
            );

            logger.LogInformation(
                "FunctionCallMiddleware created successfully for chat {ChatId}",
                chatId
            );
            return middleware;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to initialize FunctionCallMiddleware for chat {ChatId}",
                chatId
            );
            return null;
        }
    }
}
