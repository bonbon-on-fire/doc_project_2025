using AchieveAi.LmDotnetTools.LmCore.Middleware;
using AIChat.Orleans.Host.Models;
using Microsoft.Extensions.Options;
using LmCoreFunctionFilterConfig = AchieveAi.LmDotnetTools.LmCore.Configuration.FunctionFilterConfig;
using LmCoreProviderFilterConfig = AchieveAi.LmDotnetTools.LmCore.Configuration.ProviderFilterConfig;

namespace AIChat.Orleans.Host.Services;

/// <summary>
/// Service responsible for managing tool registrations and creating function call middleware
/// Simplified version for Orleans.Host focusing on MCP integration
/// </summary>
public class ToolingService(
    IServiceProvider serviceProvider,
    IOptions<McpConfiguration> mcpConfiguration,
    ILogger<ToolingService> logger
) : AIChat.Orleans.Services.IToolingService
{
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
                    ProviderConfigs = [],
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
                    ProviderConfigs = [],
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

            // TODO: Add MCP clients to the registry when MCP middleware becomes available
            // NOTE: WeatherFunction, TaskManager, and ModeService integration removed for Orleans.Host
            // Those are Server-side responsibilities. Orleans.Host focuses on MCP integration only.
            logger.LogInformation("MCP client integration is currently disabled (middleware not fully migrated)");

            // Build function contracts and handlers with conflict resolution
            _ = registry.WithConflictResolution(ConflictResolution.PreferMcp);
            var (contracts, handlers) = registry.Build();

            if (!contracts.Any())
            {
                logger.LogWarning("No functions registered for FunctionCallMiddleware");
                // Still create middleware even with no functions - it can be used for filtering validation
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
        catch (OperationCanceledException)
        {
            // Rethrow cancellation exceptions
            throw;
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
