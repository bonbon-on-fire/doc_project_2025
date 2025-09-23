using AIChat.Orleans.Contracts.Attributes;
using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for mode configuration management.
/// Handles configuration retrieval, updates, and validation.
/// This interface follows the Interface Segregation Principle by focusing solely on configuration-related operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IModeConfigurationGrain")]
public interface IModeConfigurationGrain : IGrainWithStringKey
{
    /// <summary>
    /// Gets the current mode configuration.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current mode configuration</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    [Alias("GetConfigurationAsync")]
    [ReadOnly]
    [CacheHint(isCacheable: true, durationSeconds: 30)]
    [RateLimit(100, 60, perUser: true)]
    [Telemetry(TelemetryLevel.Minimal)]
    Task<ModeConfiguration> GetConfigurationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the mode configuration.
    /// </summary>
    /// <param name="configuration">New configuration to apply</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Updated mode configuration</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    /// <exception cref="InvalidModeConfigurationException">Thrown when configuration is invalid</exception>
    /// <exception cref="ModeArchivedException">Thrown when attempting to update an archived mode</exception>
    [Alias("UpdateConfigurationAsync")]
    [RateLimit(10, 60, perUser: true)]
    [Telemetry(TelemetryLevel.Normal, includeParameters: true)]
    [Security(requireAuthorization: true, audit: true, classification: DataClassification.Confidential)]
    Task<ModeConfiguration> UpdateConfigurationAsync(ModeConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all available mode templates.
    /// </summary>
    /// <param name="category">Optional category filter</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of available mode templates</returns>
    [Alias("GetAvailableModesAsync")]
    [ReadOnly]
    [CacheHint(isCacheable: true, durationSeconds: 300)]  // Templates change infrequently
    [RateLimit(50, 60, perUser: true)]
    [Telemetry(TelemetryLevel.Minimal)]
    Task<List<ModeTemplate>> GetAvailableModesAsync(string? category = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific mode template by ID.
    /// </summary>
    /// <param name="templateId">Template ID to retrieve</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Mode template</returns>
    /// <exception cref="ModeTemplateNotFoundException">Thrown when template does not exist</exception>
    [Alias("GetModeTemplateAsync")]
    [ReadOnly]
    Task<ModeTemplate> GetModeTemplateAsync(string templateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a mode configuration without applying it.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Validation result with any issues found</returns>
    [Alias("ValidateConfigurationAsync")]
    [ReadOnly]
    [CacheHint(isCacheable: false)]  // Validation should always be fresh
    [RateLimit(30, 60, perUser: true)]
    [Telemetry(TelemetryLevel.Normal, includeParameters: true)]
    Task<ModeValidationResult> ValidateConfigurationAsync(ModeConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the system prompt for the mode.
    /// </summary>
    /// <param name="systemPrompt">New system prompt</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Updated configuration</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    /// <exception cref="InvalidPromptException">Thrown when prompt is invalid</exception>
    /// <exception cref="ModeArchivedException">Thrown when attempting to update an archived mode</exception>
    [Alias("UpdateSystemPromptAsync")]
    [RateLimit(10, 60, perUser: true)]
    [Telemetry(TelemetryLevel.Normal, includeParameters: false)]  // Don't log prompts
    [Security(requireAuthorization: true, audit: true, classification: DataClassification.Confidential)]
    Task<ModeConfiguration> UpdateSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the available tools for the mode.
    /// </summary>
    /// <param name="tools">List of tool IDs to enable</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Updated configuration</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    /// <exception cref="InvalidToolException">Thrown when a tool ID is invalid</exception>
    /// <exception cref="ModeArchivedException">Thrown when attempting to update an archived mode</exception>
    [Alias("UpdateToolsAsync")]
    Task<ModeConfiguration> UpdateToolsAsync(List<string> tools, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the effective configuration merging defaults with overrides.
    /// </summary>
    /// <param name="overrides">Optional configuration overrides</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Effective configuration after merging</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    [Alias("GetEffectiveConfigurationAsync")]
    [ReadOnly]
    Task<ModeConfiguration> GetEffectiveConfigurationAsync(Dictionary<string, string>? overrides = null, CancellationToken cancellationToken = default);
}