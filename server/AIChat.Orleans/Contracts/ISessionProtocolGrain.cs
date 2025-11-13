using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for session protocol management.
/// Handles protocol-specific configuration, state management, and protocol switching.
/// This interface follows the Interface Segregation Principle by focusing on protocol-related operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.ISessionProtocolGrain")]
public interface ISessionProtocolGrain : IGrainWithStringKey
{
    /// <summary>
    /// Gets the current protocol configuration for the session.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current protocol configuration</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("GetProtocolConfigurationAsync")]
    [ReadOnly]
    Task<ProtocolConfiguration> GetProtocolConfigurationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates protocol-specific configuration.
    /// </summary>
    /// <param name="configuration">New protocol configuration</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Updated protocol information</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    /// <exception cref="SessionProtocolException">Thrown when protocol configuration is invalid</exception>
    /// <exception cref="SessionValidationException">Thrown when configuration validation fails</exception>
    [Alias("UpdateProtocolConfigurationAsync")]
    Task<SessionProtocolInfo> UpdateProtocolConfigurationAsync(ProtocolConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches the session to a different protocol.
    /// </summary>
    /// <param name="request">Protocol switch request with target protocol and options</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>New protocol information after successful switch</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    /// <exception cref="SessionProtocolException">Thrown when protocol switch fails</exception>
    /// <exception cref="SessionDisconnectedException">Thrown when attempting to switch protocol on a disconnected session</exception>
    [Alias("SwitchProtocolAsync")]
    Task<SessionProtocolInfo> SwitchProtocolAsync(ProtocolSwitchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if a protocol is supported and can be used with the current session.
    /// </summary>
    /// <param name="protocolType">Type of protocol to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Validation result with compatibility information</returns>
    [Alias("ValidateProtocolAsync")]
    [ReadOnly]
    Task<ProtocolValidationResult> ValidateProtocolAsync(string protocolType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets protocol-specific state information.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current protocol state</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("GetProtocolStateAsync")]
    [ReadOnly]
    Task<ProtocolState> GetProtocolStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates protocol-specific state.
    /// </summary>
    /// <param name="state">New protocol state</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    /// <exception cref="SessionProtocolException">Thrown when state update fails</exception>
    [Alias("UpdateProtocolStateAsync")]
    Task UpdateProtocolStateAsync(ProtocolState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Negotiates protocol capabilities with the client.
    /// </summary>
    /// <param name="clientCapabilities">Client's supported capabilities</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Negotiated protocol capabilities</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    /// <exception cref="SessionProtocolException">Thrown when negotiation fails</exception>
    [Alias("NegotiateProtocolAsync")]
    Task<ProtocolCapabilities> NegotiateProtocolAsync(ProtocolCapabilities clientCapabilities, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available protocols for the current session.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of available protocols with their configurations</returns>
    [Alias("GetAvailableProtocolsAsync")]
    [ReadOnly]
    Task<List<ProtocolInfo>> GetAvailableProtocolsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles protocol-specific messages.
    /// </summary>
    /// <param name="message">Protocol message to handle</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Protocol response</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    /// <exception cref="SessionProtocolException">Thrown when message handling fails</exception>
    /// <exception cref="SessionDisconnectedException">Thrown when the session is disconnected</exception>
    [Alias("HandleProtocolMessageAsync")]
    Task<ProtocolResponse> HandleProtocolMessageAsync(ProtocolMessage message, CancellationToken cancellationToken = default);
}
