using AIChat.Server.Models.WebSocket;

namespace AIChat.Server.Services.WebSocket;

/// <summary>
/// Interface for negotiating WebSocket protocols with Orleans integration.
/// Handles protocol selection, capability negotiation, and integration with Orleans session grains.
/// </summary>
public interface IWebSocketProtocolNegotiator
{
    /// <summary>
    /// Negotiates the best protocol based on client requests and server capabilities.
    /// Integrates with Orleans ISessionProtocolGrain for protocol validation and selection.
    /// </summary>
    /// <param name="requestedProtocols">Protocols requested by the client in order of preference</param>
    /// <param name="userId">User identifier for session-specific protocol preferences</param>
    /// <param name="clientCapabilities">Capabilities reported by the client</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of protocol negotiation</returns>
    Task<ProtocolNegotiationResult> NegotiateProtocolAsync(
        IEnumerable<string> requestedProtocols,
        string userId,
        Dictionary<string, object>? clientCapabilities = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all protocols available for a specific user.
    /// Considers user permissions, feature flags, and Orleans grain capabilities.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of available protocols for the user</returns>
    Task<List<WebSocketProtocolInfo>> GetAvailableProtocolsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates whether a specific protocol is supported and available.
    /// </summary>
    /// <param name="protocolName">Name of the protocol to validate</param>
    /// <param name="userId">User identifier for permission checks</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the protocol is valid and available for the user</returns>
    Task<bool> IsProtocolSupportedAsync(
        string protocolName,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed information about a specific protocol.
    /// </summary>
    /// <param name="protocolName">Name of the protocol</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Protocol information if found, null otherwise</returns>
    Task<WebSocketProtocolInfo?> GetProtocolInfoAsync(
        string protocolName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates client capabilities against protocol requirements.
    /// </summary>
    /// <param name="protocolName">Name of the protocol</param>
    /// <param name="clientCapabilities">Capabilities reported by the client</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Validation result with any missing or incompatible capabilities</returns>
    Task<CapabilityValidationResult> ValidateCapabilitiesAsync(
        string protocolName,
        Dictionary<string, object> clientCapabilities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Negotiates specific capabilities for a protocol.
    /// Determines the intersection of client and server capabilities.
    /// </summary>
    /// <param name="protocolName">Name of the protocol</param>
    /// <param name="clientCapabilities">Capabilities requested by the client</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Negotiated capabilities that both client and server support</returns>
    Task<Dictionary<string, object>> NegotiateCapabilitiesAsync(
        string protocolName,
        Dictionary<string, object> clientCapabilities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a custom protocol dynamically.
    /// Allows runtime addition of new protocols.
    /// </summary>
    /// <param name="protocolInfo">Information about the protocol to register</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the protocol was registered successfully</returns>
    Task<bool> RegisterProtocolAsync(
        WebSocketProtocolInfo protocolInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters a protocol.
    /// </summary>
    /// <param name="protocolName">Name of the protocol to unregister</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the protocol was unregistered successfully</returns>
    Task<bool> UnregisterProtocolAsync(
        string protocolName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates protocol configuration.
    /// </summary>
    /// <param name="protocolInfo">Updated protocol information</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the protocol was updated successfully</returns>
    Task<bool> UpdateProtocolAsync(
        WebSocketProtocolInfo protocolInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets protocol negotiation statistics and metrics.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Protocol negotiation statistics</returns>
    Task<ProtocolNegotiationStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of capability validation.
/// </summary>
public record CapabilityValidationResult
{
    /// <summary>
    /// Gets whether all required capabilities are satisfied.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the capabilities that are missing or incompatible.
    /// </summary>
    public List<string> MissingCapabilities { get; init; } = [];

    /// <summary>
    /// Gets the capabilities that are incompatible (wrong type/value).
    /// </summary>
    public Dictionary<string, string> IncompatibleCapabilities { get; init; } = [];

    /// <summary>
    /// Gets any additional validation messages.
    /// </summary>
    public List<string> ValidationMessages { get; init; } = [];

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <returns>A successful capability validation result</returns>
    public static CapabilityValidationResult Success()
    {
        return new CapabilityValidationResult { IsValid = true };
    }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="missingCapabilities">List of missing capabilities</param>
    /// <param name="incompatibleCapabilities">Dictionary of incompatible capabilities</param>
    /// <param name="validationMessages">Additional validation messages</param>
    /// <returns>A failed capability validation result</returns>
    public static CapabilityValidationResult Failure(
        List<string>? missingCapabilities = null,
        Dictionary<string, string>? incompatibleCapabilities = null,
        List<string>? validationMessages = null)
    {
        return new CapabilityValidationResult
        {
            IsValid = false,
            MissingCapabilities = missingCapabilities ?? [],
            IncompatibleCapabilities = incompatibleCapabilities ?? [],
            ValidationMessages = validationMessages ?? []
        };
    }
}

/// <summary>
/// Represents statistics about protocol negotiation.
/// </summary>
public record ProtocolNegotiationStatistics
{
    /// <summary>
    /// Gets the total number of protocol negotiations performed.
    /// </summary>
    public long TotalNegotiations { get; init; }

    /// <summary>
    /// Gets the number of successful negotiations.
    /// </summary>
    public long SuccessfulNegotiations { get; init; }

    /// <summary>
    /// Gets the number of failed negotiations.
    /// </summary>
    public long FailedNegotiations { get; init; }

    /// <summary>
    /// Gets the count of negotiations by protocol.
    /// </summary>
    public Dictionary<string, long> NegotiationsByProtocol { get; init; } = [];

    /// <summary>
    /// Gets the most commonly requested protocols.
    /// </summary>
    public Dictionary<string, long> RequestedProtocols { get; init; } = [];

    /// <summary>
    /// Gets the average negotiation time in milliseconds.
    /// </summary>
    public double AverageNegotiationTimeMs { get; init; }

    /// <summary>
    /// Gets the timestamp when statistics were collected.
    /// </summary>
    public DateTime CollectedAt { get; init; } = DateTime.UtcNow;
}
