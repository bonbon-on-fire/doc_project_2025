using System.Text.Json;
using AIChat.Server.Models;
using Xunit;

namespace AIChat.Server.Tests.Models;

/// <summary>
/// Tests for McpConfiguration and function filtering configuration models
/// </summary>
public class McpConfigurationTests
{
    #region FunctionFilterConfig Tests

    [Fact]
    public void FunctionFilterConfigDefaultValuesAreCorrect()
    {
        // Arrange & Act
        var config = new FunctionFilterConfig();

        // Assert
        Assert.False(config.EnableFiltering);
        Assert.Null(config.GlobalAllowedFunctions);
        Assert.Null(config.GlobalBlockedFunctions);
        Assert.True(config.UsePrefixOnlyForCollisions);
        Assert.Null(config.ProviderConfigs);
    }

    [Fact]
    public void FunctionFilterConfigSerializationWorksCorrectly()
    {
        // Arrange
        var config = new FunctionFilterConfig
        {
            EnableFiltering = true,
            GlobalAllowedFunctions = ["search*", "read*"],
            GlobalBlockedFunctions = ["delete*"],
            UsePrefixOnlyForCollisions = false,
            ProviderConfigs = new Dictionary<string, ProviderFilterConfig>
            {
                ["MCP_github"] = new ProviderFilterConfig
                {
                    AllowedFunctions = ["create_issue"],
                    BlockedFunctions = ["delete_repo"],
                    CustomPrefix = "gh_",
                },
            },
        };

        // Act
        var json = JsonSerializer.Serialize(config);
        var deserialized = JsonSerializer.Deserialize<FunctionFilterConfig>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(config.EnableFiltering, deserialized.EnableFiltering);
        Assert.Equal(config.GlobalAllowedFunctions, deserialized.GlobalAllowedFunctions);
        Assert.Equal(config.GlobalBlockedFunctions, deserialized.GlobalBlockedFunctions);
        Assert.Equal(config.UsePrefixOnlyForCollisions, deserialized.UsePrefixOnlyForCollisions);
        Assert.NotNull(deserialized.ProviderConfigs);
        _ = Assert.Single(deserialized.ProviderConfigs);
        Assert.True(deserialized.ProviderConfigs.ContainsKey("MCP_github"));
    }

    #endregion

    #region ProviderFilterConfig Tests

    [Fact]
    public void ProviderFilterConfigDefaultValuesAreCorrect()
    {
        // Arrange & Act
        var config = new ProviderFilterConfig();

        // Assert
        Assert.Null(config.AllowedFunctions);
        Assert.Null(config.BlockedFunctions);
        Assert.Null(config.CustomPrefix);
    }

    [Fact]
    public void ProviderFilterConfigWithWildcardsSerializesCorrectly()
    {
        // Arrange
        var config = new ProviderFilterConfig
        {
            AllowedFunctions = ["*_create", "list_*", "*search*"],
            BlockedFunctions = ["admin_*"],
            CustomPrefix = "test_",
        };

        // Act
        var json = JsonSerializer.Serialize(config);
        var deserialized = JsonSerializer.Deserialize<ProviderFilterConfig>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.AllowedFunctions?.Count);
        Assert.Contains("*_create", deserialized.AllowedFunctions);
        Assert.Contains("list_*", deserialized.AllowedFunctions);
        Assert.Contains("*search*", deserialized.AllowedFunctions);
        _ = Assert.Single(deserialized.BlockedFunctions);
        Assert.Equal("test_", deserialized.CustomPrefix);
    }

    #endregion

    #region McpConfiguration Integration Tests

    [Fact]
    public void McpConfigurationWithFunctionFilteringSerializesCorrectly()
    {
        // Arrange
        var config = new McpConfiguration
        {
            FunctionFiltering = new FunctionFilterConfig
            {
                EnableFiltering = true,
                GlobalAllowedFunctions = ["*"],
            },
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["github"] = new McpServerConfig { Type = "stdio", Command = "mcp-server-github" },
            },
        };

        // Act
        var json = JsonSerializer.Serialize(config);
        var deserialized = JsonSerializer.Deserialize<McpConfiguration>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.FunctionFiltering);
        Assert.True(deserialized.FunctionFiltering.EnableFiltering);
        Assert.NotNull(deserialized.McpServers);
        _ = Assert.Single(deserialized.McpServers);
    }

#pragma warning disable CS0618 // Type or member is obsolete
    [Fact]
    public void McpConfigurationWithLegacyToolFilteringStillWorks()
    {
        // Arrange
        var config = new McpConfiguration
        {
            ToolFiltering = new McpToolFilterConfig
            {
                EnableFiltering = true,
                GlobalAllowedTools = ["search*"],
                GlobalBlockedTools = ["delete*"],
            },
        };

        // Act
        var json = JsonSerializer.Serialize(config);
        var deserialized = JsonSerializer.Deserialize<McpConfiguration>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.ToolFiltering);
        Assert.True(deserialized.ToolFiltering.EnableFiltering);
        _ = Assert.Single(deserialized.ToolFiltering.GlobalAllowedTools);
        _ = Assert.Single(deserialized.ToolFiltering.GlobalBlockedTools);
    }
#pragma warning restore CS0618 // Type or member is obsolete

    [Fact]
    public void McpConfigurationBothFilteringConfigsNewTakesPrecedence()
    {
        // Arrange
#pragma warning disable CS0618 // Type or member is obsolete
        var config = new McpConfiguration
        {
            FunctionFiltering = new FunctionFilterConfig
            {
                EnableFiltering = true,
                GlobalAllowedFunctions = ["new_function"],
            },
            ToolFiltering = new McpToolFilterConfig
            {
                EnableFiltering = false,
                GlobalAllowedTools = ["old_tool"],
            },
        };
#pragma warning restore CS0618 // Type or member is obsolete

        // Act - In ToolingService, FunctionFiltering should take precedence
        // This is more of a documentation test to show expected behavior

        // Assert
        Assert.NotNull(config.FunctionFiltering);
        Assert.NotNull(config.ToolFiltering);
        Assert.True(config.FunctionFiltering.EnableFiltering);
        Assert.False(config.ToolFiltering.EnableFiltering);
    }

    #endregion

    #region McpServerConfig Tests

    [Fact]
    public void McpServerConfigWithAllowedToolsSerializesCorrectly()
    {
        // Arrange
        var serverConfig = new McpServerConfig
        {
            Type = "stdio",
            Command = "test-server",
            AllowedTools = ["tool1", "tool2*"],
            BlockedTools = ["dangerous_*"],
        };

        // Act
        var json = JsonSerializer.Serialize(serverConfig);
        var deserialized = JsonSerializer.Deserialize<McpServerConfig>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("stdio", deserialized.Type);
        Assert.Equal("test-server", deserialized.Command);
        Assert.Equal(2, deserialized.AllowedTools?.Count);
        _ = Assert.Single(deserialized.BlockedTools);
    }

    #endregion
}
