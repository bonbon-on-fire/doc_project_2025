using System.Collections.Concurrent;
using AIChat.Server.Models.WebSocket;

namespace AIChat.Server.Services.WebSocket;

/// <summary>
/// Implementation of IWebSocketProtocolNegotiator for handling WebSocket protocol negotiation.
/// Provides basic protocol selection and capability negotiation for WebSocket connections.
/// </summary>
public class WebSocketProtocolNegotiator : IWebSocketProtocolNegotiator
{
    private readonly ILogger<WebSocketProtocolNegotiator> _logger;
    private readonly ConcurrentDictionary<string, WebSocketProtocolInfo> _registeredProtocols = new();
    private readonly ConcurrentDictionary<string, long> _negotiationCounts = new();
    private long _totalNegotiations;
    private long _successfulNegotiations;
    private long _failedNegotiations;

    public WebSocketProtocolNegotiator(ILogger<WebSocketProtocolNegotiator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Register standard protocols
        foreach (var protocol in StandardWebSocketProtocols.GetAllProtocols())
        {
            _ = _registeredProtocols.TryAdd(protocol.Name, protocol);
        }
    }

    public Task<ProtocolNegotiationResult> NegotiateProtocolAsync(
        IEnumerable<string> requestedProtocols,
        string userId,
        Dictionary<string, object>? clientCapabilities = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedProtocols);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        _ = Interlocked.Increment(ref _totalNegotiations);

        try
        {
            var protocols = requestedProtocols.ToList();
            _logger.LogDebug("Negotiating protocol for user {UserId} with requested protocols: {Protocols}",
                userId, string.Join(", ", protocols));

            // Find the first supported protocol from the client's list
            foreach (var requestedProtocol in protocols)
            {
                if (_registeredProtocols.TryGetValue(requestedProtocol, out var protocolInfo) && protocolInfo.Enabled)
                {
                    var negotiatedCapabilities = clientCapabilities ?? [];

                    _ = Interlocked.Increment(ref _successfulNegotiations);
                    _ = _negotiationCounts.AddOrUpdate(requestedProtocol, 1, (_, count) => count + 1);

                    _logger.LogInformation("Protocol negotiation successful for user {UserId}: {Protocol}",
                        userId, requestedProtocol);

                    return Task.FromResult(ProtocolNegotiationResult.CreateSuccess(
                        protocolInfo, negotiatedCapabilities));
                }
            }

            // No matching protocol found - use generic-v1 as fallback
            if (_registeredProtocols.TryGetValue("generic-v1", out var genericProtocol) && genericProtocol.Enabled)
            {
                _ = Interlocked.Increment(ref _successfulNegotiations);
                _ = _negotiationCounts.AddOrUpdate("generic-v1", 1, (_, count) => count + 1);

                _logger.LogInformation("No matching protocol found for user {UserId}, using generic-v1 fallback", userId);

                return Task.FromResult(ProtocolNegotiationResult.CreateSuccess(
                    genericProtocol, clientCapabilities ?? []));
            }

            // Negotiation failed
            _ = Interlocked.Increment(ref _failedNegotiations);
            _logger.LogWarning("Protocol negotiation failed for user {UserId}: no supported protocols found", userId);

            return Task.FromResult(ProtocolNegotiationResult.CreateFailure(
                "No supported protocols found",
                [.. _registeredProtocols.Values.Where(p => p.Enabled)]));
        }
        catch (Exception ex)
        {
            _ = Interlocked.Increment(ref _failedNegotiations);
            _logger.LogError(ex, "Error during protocol negotiation for user {UserId}", userId);
            return Task.FromResult(ProtocolNegotiationResult.CreateFailure($"Protocol negotiation error: {ex.Message}"));
        }
    }

    public Task<List<WebSocketProtocolInfo>> GetAvailableProtocolsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var availableProtocols = _registeredProtocols.Values
            .Where(p => p.Enabled)
            .OrderByDescending(p => p.Priority)
            .ToList();

        return Task.FromResult(availableProtocols);
    }

    public Task<bool> IsProtocolSupportedAsync(
        string protocolName,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var isSupported = _registeredProtocols.TryGetValue(protocolName, out var protocol) && protocol.Enabled;
        return Task.FromResult(isSupported);
    }

    public Task<WebSocketProtocolInfo?> GetProtocolInfoAsync(
        string protocolName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);

        _ = _registeredProtocols.TryGetValue(protocolName, out var protocolInfo);
        return Task.FromResult(protocolInfo);
    }

    public Task<CapabilityValidationResult> ValidateCapabilitiesAsync(
        string protocolName,
        Dictionary<string, object> clientCapabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentNullException.ThrowIfNull(clientCapabilities);

        if (!_registeredProtocols.TryGetValue(protocolName, out var protocol))
        {
            return Task.FromResult(CapabilityValidationResult.Failure(
                validationMessages: [$"Protocol '{protocolName}' not found"]));
        }

        // Simple validation - in a real implementation, this would check specific capability requirements
        return Task.FromResult(CapabilityValidationResult.Success());
    }

    public Task<Dictionary<string, object>> NegotiateCapabilitiesAsync(
        string protocolName,
        Dictionary<string, object> clientCapabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentNullException.ThrowIfNull(clientCapabilities);

        if (!_registeredProtocols.TryGetValue(protocolName, out var protocol))
        {
            return Task.FromResult(new Dictionary<string, object>());
        }

        // Merge client and server capabilities - in a real implementation, this would intelligently negotiate
        var negotiated = new Dictionary<string, object>(protocol.Capabilities);
        foreach (var kvp in clientCapabilities)
        {
            if (protocol.Capabilities.ContainsKey(kvp.Key))
            {
                negotiated[kvp.Key] = kvp.Value;
            }
        }

        return Task.FromResult(negotiated);
    }

    public Task<bool> RegisterProtocolAsync(
        WebSocketProtocolInfo protocolInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(protocolInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolInfo.Name);

        var added = _registeredProtocols.TryAdd(protocolInfo.Name, protocolInfo);

        if (added)
        {
            _logger.LogInformation("Registered protocol: {ProtocolName}", protocolInfo.Name);
        }
        else
        {
            _logger.LogWarning("Protocol already registered: {ProtocolName}", protocolInfo.Name);
        }

        return Task.FromResult(added);
    }

    public Task<bool> UnregisterProtocolAsync(
        string protocolName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);

        var removed = _registeredProtocols.TryRemove(protocolName, out _);

        if (removed)
        {
            _logger.LogInformation("Unregistered protocol: {ProtocolName}", protocolName);
        }

        return Task.FromResult(removed);
    }

    public Task<bool> UpdateProtocolAsync(
        WebSocketProtocolInfo protocolInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(protocolInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolInfo.Name);

        var updated = _registeredProtocols.TryUpdate(protocolInfo.Name, protocolInfo, _registeredProtocols[protocolInfo.Name]);

        if (updated)
        {
            _logger.LogInformation("Updated protocol: {ProtocolName}", protocolInfo.Name);
        }

        return Task.FromResult(updated);
    }

    public Task<ProtocolNegotiationStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var stats = new ProtocolNegotiationStatistics
        {
            TotalNegotiations = Interlocked.Read(ref _totalNegotiations),
            SuccessfulNegotiations = Interlocked.Read(ref _successfulNegotiations),
            FailedNegotiations = Interlocked.Read(ref _failedNegotiations),
            NegotiationsByProtocol = new Dictionary<string, long>(_negotiationCounts),
            RequestedProtocols = new Dictionary<string, long>(_negotiationCounts),
            AverageNegotiationTimeMs = 0, // Would track timing in real implementation
            CollectedAt = DateTime.UtcNow
        };

        return Task.FromResult(stats);
    }
}
