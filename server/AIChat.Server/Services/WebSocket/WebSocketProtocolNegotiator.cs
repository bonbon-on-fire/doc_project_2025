using System.Diagnostics;
using AIChat.Orleans.Contracts;
using AIChat.Server.Models.WebSocket;
using AIChat.Server.Services.Routing;

namespace AIChat.Server.Services.WebSocket;

/// <summary>
/// Implementation of IWebSocketProtocolNegotiator for WebSocket protocol negotiation with Orleans integration.
/// Handles protocol selection, capability negotiation, and integration with Orleans session grains
/// following the dual-mode routing pattern for resilient operation.
///
/// Features:
/// - Orleans grain integration for protocol operations
/// - Dual-mode routing with fallback to direct protocol handling
/// - Comprehensive capability validation and negotiation
/// - Distributed tracing and structured logging
/// - Statistics collection for monitoring
/// </summary>
public class WebSocketProtocolNegotiator : IWebSocketProtocolNegotiator
{
    private readonly IDualModeRouter _dualModeRouter;
    private readonly ILogger<WebSocketProtocolNegotiator> _logger;
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.WebSocketProtocolNegotiator");

    // Statistics tracking
    private long _totalNegotiations;
    private long _successfulNegotiations;
    private long _failedNegotiations;
    private readonly Dictionary<string, long> _negotiationsByProtocol = new();
    private readonly Dictionary<string, long> _requestedProtocols = new();
    private readonly object _statisticsLock = new();

    /// <summary>
    /// Initializes a new instance of the WebSocketProtocolNegotiator.
    /// </summary>
    /// <param name="dualModeRouter">Router for Orleans/direct service operations</param>
    /// <param name="logger">Logger instance</param>
    public WebSocketProtocolNegotiator(
        IDualModeRouter dualModeRouter,
        ILogger<WebSocketProtocolNegotiator> logger)
    {
        _dualModeRouter = dualModeRouter ?? throw new ArgumentNullException(nameof(dualModeRouter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ProtocolNegotiationResult> NegotiateProtocolAsync(
        IEnumerable<string> requestedProtocols,
        string userId,
        Dictionary<string, object>? clientCapabilities = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("NegotiateProtocol");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            ArgumentNullException.ThrowIfNull(requestedProtocols);
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            var protocolList = requestedProtocols.ToList();
            clientCapabilities ??= new Dictionary<string, object>();

            Interlocked.Increment(ref _totalNegotiations);

            _logger.LogInformation(
                "Starting protocol negotiation for user {UserId} with requested protocols: {RequestedProtocols}",
                userId, string.Join(", ", protocolList));

            // Track requested protocols for statistics
            lock (_statisticsLock)
            {
                foreach (var protocol in protocolList)
                {
                    _requestedProtocols.TryGetValue(protocol, out var count);
                    _requestedProtocols[protocol] = count + 1;
                }
            }

            // Get available protocols from Orleans or fallback
            var availableProtocols = await GetAvailableProtocolsAsync(userId, cancellationToken);

            // Find the best match based on client preferences and server capabilities
            var selectedProtocol = FindBestProtocolMatch(protocolList, availableProtocols);

            if (selectedProtocol == null)
            {
                var result = ProtocolNegotiationResult.CreateFailure(
                    "No compatible protocol found",
                    availableProtocols);

                Interlocked.Increment(ref _failedNegotiations);
                stopwatch.Stop();

                _logger.LogWarning(
                    "Protocol negotiation failed for user {UserId}: No compatible protocol found. Requested: {RequestedProtocols}, Available: {AvailableProtocols} (Duration: {ElapsedMs}ms)",
                    userId, string.Join(", ", protocolList), string.Join(", ", availableProtocols.Select(p => p.Name)), stopwatch.ElapsedMilliseconds);

                activity?.SetTag("operation.success", false);
                activity?.SetTag("error.reason", "no_compatible_protocol");
                activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);

                return result;
            }

            // Negotiate capabilities for the selected protocol
            var negotiatedCapabilities = await NegotiateCapabilitiesAsync(
                selectedProtocol.Name,
                clientCapabilities,
                cancellationToken);

            // Use Orleans grain for protocol validation if available
            var isValid = await ValidateProtocolWithOrleansAsync(selectedProtocol.Name, userId, cancellationToken);

            if (!isValid)
            {
                var result = ProtocolNegotiationResult.CreateFailure(
                    $"Protocol {selectedProtocol.Name} validation failed",
                    availableProtocols);

                Interlocked.Increment(ref _failedNegotiations);
                stopwatch.Stop();

                _logger.LogWarning(
                    "Protocol validation failed for user {UserId} with protocol {Protocol} (Duration: {ElapsedMs}ms)",
                    userId, selectedProtocol.Name, stopwatch.ElapsedMilliseconds);

                activity?.SetTag("operation.success", false);
                activity?.SetTag("error.reason", "protocol_validation_failed");
                activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);

                return result;
            }

            // Successful negotiation
            var successResult = ProtocolNegotiationResult.CreateSuccess(selectedProtocol, negotiatedCapabilities);
            successResult.AvailableProtocols.AddRange(availableProtocols);

            // Update statistics
            Interlocked.Increment(ref _successfulNegotiations);
            lock (_statisticsLock)
            {
                _negotiationsByProtocol.TryGetValue(selectedProtocol.Name, out var count);
                _negotiationsByProtocol[selectedProtocol.Name] = count + 1;
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "Protocol negotiation successful for user {UserId}: Selected {SelectedProtocol} with {CapabilityCount} capabilities (Duration: {ElapsedMs}ms)",
                userId, selectedProtocol.Name, negotiatedCapabilities.Count, stopwatch.ElapsedMilliseconds);

            activity?.SetTag("user.id", userId);
            activity?.SetTag("selected.protocol", selectedProtocol.Name);
            activity?.SetTag("capabilities.count", negotiatedCapabilities.Count);
            activity?.SetTag("operation.success", true);
            activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);

            return successResult;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedNegotiations);
            stopwatch.Stop();

            _logger.LogError(ex,
                "Protocol negotiation failed for user {UserId} after {ElapsedMs}ms",
                userId, stopwatch.ElapsedMilliseconds);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);

            return ProtocolNegotiationResult.CreateFailure(
                $"Protocol negotiation error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<List<WebSocketProtocolInfo>> GetAvailableProtocolsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("GetAvailableProtocols");

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            // Try to get protocols from Orleans first, fallback to standard protocols
            var protocols = await _dualModeRouter.ExecuteAsync(
                orleansOperation: async grain => await GetProtocolsFromOrleansAsync(grain, userId, cancellationToken),
                directOperation: async service => await GetStandardProtocolsAsync(cancellationToken),
                operationName: "GetAvailableProtocols",
                cancellationToken: cancellationToken);

            _logger.LogDebug(
                "Retrieved {ProtocolCount} available protocols for user {UserId}",
                protocols.Count, userId);

            activity?.SetTag("user.id", userId);
            activity?.SetTag("protocol.count", protocols.Count);
            activity?.SetTag("operation.success", true);

            return protocols;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available protocols for user {UserId}", userId);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);

            // Return standard protocols as fallback
            return await GetStandardProtocolsAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsProtocolSupportedAsync(
        string protocolName,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        try
        {
            var availableProtocols = await GetAvailableProtocolsAsync(userId, cancellationToken);
            var isSupported = availableProtocols.Any(p =>
                string.Equals(p.Name, protocolName, StringComparison.OrdinalIgnoreCase) && p.Enabled);

            _logger.LogDebug(
                "Protocol {Protocol} support check for user {UserId}: {IsSupported}",
                protocolName, userId, isSupported);

            return isSupported;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to check protocol support for {Protocol} and user {UserId}",
                protocolName, userId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<WebSocketProtocolInfo?> GetProtocolInfoAsync(
        string protocolName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);

        try
        {
            // First check standard protocols
            var standardProtocol = StandardWebSocketProtocols.GetProtocol(protocolName);
            if (standardProtocol != null)
            {
                return standardProtocol;
            }

            // Future: Could check Orleans grain for custom protocols
            _logger.LogDebug("Protocol {Protocol} not found", protocolName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get protocol info for {Protocol}", protocolName);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<CapabilityValidationResult> ValidateCapabilitiesAsync(
        string protocolName,
        Dictionary<string, object> clientCapabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentNullException.ThrowIfNull(clientCapabilities);

        try
        {
            var protocolInfo = await GetProtocolInfoAsync(protocolName, cancellationToken);
            if (protocolInfo == null)
            {
                return CapabilityValidationResult.Failure(
                    validationMessages: new List<string> { $"Protocol {protocolName} not found" });
            }

            var missingCapabilities = new List<string>();
            var incompatibleCapabilities = new Dictionary<string, string>();

            // Check required capabilities
            foreach (var requiredCapability in protocolInfo.Capabilities)
            {
                if (!clientCapabilities.ContainsKey(requiredCapability.Key))
                {
                    missingCapabilities.Add(requiredCapability.Key);
                    continue;
                }

                // Validate capability values (simplified validation)
                var clientValue = clientCapabilities[requiredCapability.Key];
                var expectedValue = requiredCapability.Value;

                if (!ValidateCapabilityValue(clientValue, expectedValue))
                {
                    incompatibleCapabilities[requiredCapability.Key] =
                        $"Expected {expectedValue}, got {clientValue}";
                }
            }

            if (missingCapabilities.Count != 0 || incompatibleCapabilities.Count != 0)
            {
                return CapabilityValidationResult.Failure(
                    missingCapabilities,
                    incompatibleCapabilities);
            }

            return CapabilityValidationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to validate capabilities for protocol {Protocol}",
                protocolName);

            return CapabilityValidationResult.Failure(
                validationMessages: new List<string> { $"Validation error: {ex.Message}" });
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, object>> NegotiateCapabilitiesAsync(
        string protocolName,
        Dictionary<string, object> clientCapabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentNullException.ThrowIfNull(clientCapabilities);

        try
        {
            var protocolInfo = await GetProtocolInfoAsync(protocolName, cancellationToken);
            if (protocolInfo == null)
            {
                return new Dictionary<string, object>();
            }

            var negotiatedCapabilities = new Dictionary<string, object>();

            // Negotiate capabilities - take intersection of client and server capabilities
            foreach (var serverCapability in protocolInfo.Capabilities)
            {
                if (clientCapabilities.TryGetValue(serverCapability.Key, out var clientValue))
                {
                    // Use the more conservative/compatible value
                    var negotiatedValue = NegotiateCapabilityValue(serverCapability.Value, clientValue);
                    negotiatedCapabilities[serverCapability.Key] = negotiatedValue;
                }
                else
                {
                    // Use server default if client doesn't specify
                    negotiatedCapabilities[serverCapability.Key] = serverCapability.Value;
                }
            }

            _logger.LogDebug(
                "Negotiated {CapabilityCount} capabilities for protocol {Protocol}",
                negotiatedCapabilities.Count, protocolName);

            return negotiatedCapabilities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to negotiate capabilities for protocol {Protocol}",
                protocolName);
            return new Dictionary<string, object>();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RegisterProtocolAsync(
        WebSocketProtocolInfo protocolInfo,
        CancellationToken cancellationToken = default)
    {
        // Future implementation: Register custom protocols in Orleans grain or configuration
        _logger.LogWarning("Custom protocol registration not yet implemented for {Protocol}", protocolInfo.Name);
        return await Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task<bool> UnregisterProtocolAsync(
        string protocolName,
        CancellationToken cancellationToken = default)
    {
        // Future implementation: Unregister custom protocols
        _logger.LogWarning("Custom protocol unregistration not yet implemented for {Protocol}", protocolName);
        return await Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateProtocolAsync(
        WebSocketProtocolInfo protocolInfo,
        CancellationToken cancellationToken = default)
    {
        // Future implementation: Update protocol configurations
        _logger.LogWarning("Protocol update not yet implemented for {Protocol}", protocolInfo.Name);
        return await Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task<ProtocolNegotiationStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_statisticsLock)
        {
            var statistics = new ProtocolNegotiationStatistics
            {
                TotalNegotiations = Interlocked.Read(ref _totalNegotiations),
                SuccessfulNegotiations = Interlocked.Read(ref _successfulNegotiations),
                FailedNegotiations = Interlocked.Read(ref _failedNegotiations),
                NegotiationsByProtocol = new Dictionary<string, long>(_negotiationsByProtocol),
                RequestedProtocols = new Dictionary<string, long>(_requestedProtocols),
                AverageNegotiationTimeMs = 0 // TODO: Implement timing tracking
            };

            return statistics;
        }
    }

    #region Private Helper Methods

    private static async Task<List<WebSocketProtocolInfo>> GetProtocolsFromOrleansAsync(
        IChatGrain grain,
        string userId,
        CancellationToken cancellationToken)
    {
        // Future: Get protocols from Orleans session grain
        // For now, return standard protocols
        return await GetStandardProtocolsAsync(cancellationToken);
    }

    private static async Task<List<WebSocketProtocolInfo>> GetStandardProtocolsAsync(
        CancellationToken cancellationToken)
    {
        return await Task.FromResult(StandardWebSocketProtocols.GetAllProtocols());
    }

    private async Task<bool> ValidateProtocolWithOrleansAsync(
        string protocolName,
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Try Orleans validation first, fallback to direct validation
            return await _dualModeRouter.ExecuteAsync(
                orleansOperation: async grain => await ValidateProtocolViaOrleansAsync(grain, protocolName, userId, cancellationToken),
                directOperation: async service => await ValidateProtocolDirectAsync(protocolName, cancellationToken),
                operationName: "ValidateProtocol",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Protocol validation failed for {Protocol}", protocolName);
            return false;
        }
    }

    private async Task<bool> ValidateProtocolViaOrleansAsync(
        IChatGrain grain,
        string protocolName,
        string userId,
        CancellationToken cancellationToken)
    {
        // Future: Use ISessionProtocolGrain.ValidateProtocolAsync
        // For now, use direct validation
        return await ValidateProtocolDirectAsync(protocolName, cancellationToken);
    }

    private async Task<bool> ValidateProtocolDirectAsync(
        string protocolName,
        CancellationToken cancellationToken)
    {
        var protocolInfo = await GetProtocolInfoAsync(protocolName, cancellationToken);
        return protocolInfo != null && protocolInfo.Enabled;
    }

    private static WebSocketProtocolInfo? FindBestProtocolMatch(
        List<string> requestedProtocols,
        List<WebSocketProtocolInfo> availableProtocols)
    {
        // Find the highest priority protocol that matches client request
        foreach (var requestedProtocol in requestedProtocols)
        {
            var match = availableProtocols
                .Where(p => string.Equals(p.Name, requestedProtocol, StringComparison.OrdinalIgnoreCase) && p.Enabled)
                .OrderByDescending(p => p.Priority)
                .FirstOrDefault();

            if (match != null)
            {
                return match;
            }
        }

        // Fallback to highest priority available protocol
        return availableProtocols
            .Where(p => p.Enabled)
            .OrderByDescending(p => p.Priority)
            .FirstOrDefault();
    }

    private static bool ValidateCapabilityValue(object clientValue, object expectedValue)
    {
        // Simplified capability validation
        // In a real implementation, this would be more sophisticated
        if (expectedValue is bool expectedBool && clientValue is bool clientBool)
        {
            return expectedBool == clientBool;
        }

        if (expectedValue is string expectedString && clientValue is string clientString)
        {
            return string.Equals(expectedString, clientString, StringComparison.OrdinalIgnoreCase);
        }

        return true; // Allow other types for now
    }

    private static object NegotiateCapabilityValue(object serverValue, object clientValue)
    {
        // Simplified capability negotiation
        // In a real implementation, this would be more sophisticated
        if (serverValue is bool serverBool && clientValue is bool clientBool)
        {
            return serverBool && clientBool; // Both must support the capability
        }

        return serverValue; // Use server value as default
    }

    #endregion
}