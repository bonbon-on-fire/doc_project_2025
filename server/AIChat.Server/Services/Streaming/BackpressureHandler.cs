using System.Diagnostics;

namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Adaptive backpressure handler implementation for flow control.
/// </summary>
public sealed class BackpressureHandler : IBackpressureHandler
{
    private readonly ILogger<BackpressureHandler> _logger;
    private readonly int _baseDelayMs;
    private long _eventCount;
    private long _totalDelayMs;
    private readonly Stopwatch _stopwatch;

    /// <summary>
    /// Initializes a new instance of the BackpressureHandler class.
    /// </summary>
    /// <param name="logger">Logger for diagnostics</param>
    /// <param name="threshold">Utilization threshold for triggering backpressure (0-100)</param>
    /// <param name="baseDelayMs">Base delay in milliseconds when backpressure is applied</param>
    /// <param name="isAdaptive">Whether to use adaptive delays based on utilization</param>
    public BackpressureHandler(
        ILogger<BackpressureHandler> logger,
        float threshold = 80.0f,
        int baseDelayMs = 100,
        bool isAdaptive = true)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (threshold is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be between 0 and 100");
        }

        if (baseDelayMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDelayMs), "Base delay must be non-negative");
        }

        Threshold = threshold;
        _baseDelayMs = baseDelayMs;
        IsAdaptive = isAdaptive;
        _stopwatch = Stopwatch.StartNew();
    }

    /// <inheritdoc />
    public bool ShouldApplyBackpressure(float utilization)
    {
        return utilization is < 0 or > 100
            ? throw new ArgumentOutOfRangeException(nameof(utilization), "Utilization must be between 0 and 100")
            : utilization >= Threshold;
    }

    /// <inheritdoc />
    public async Task<int> ApplyBackpressureAsync(
        float utilization,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldApplyBackpressure(utilization))
        {
            return 0;
        }

        var delay = CalculateDelay(utilization);

        _ = Interlocked.Increment(ref _eventCount);
        _ = Interlocked.Add(ref _totalDelayMs, delay);

        _logger.LogWarning(
            "Backpressure applied. Utilization: {Utilization:F1}%, Delay: {Delay}ms, Total events: {Count}",
            utilization,
            delay,
            _eventCount);

        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return delay;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Backpressure delay cancelled");
            return delay; // Return the intended delay even if cancelled
        }
    }

    /// <inheritdoc />
    public int CalculateDelay(float utilization)
    {
        if (utilization < Threshold)
        {
            return 0;
        }

        if (!IsAdaptive)
        {
            return _baseDelayMs;
        }

        // Adaptive calculation: exponentially increase delay as utilization approaches 100%
        var utilizationAboveThreshold = utilization - Threshold;
        var maxUtilizationAboveThreshold = 100 - Threshold;
        var utilizationFactor = utilizationAboveThreshold / maxUtilizationAboveThreshold;

        // Exponential scaling: delay increases more rapidly as utilization approaches 100%
        var scaleFactor = Math.Pow(utilizationFactor, 2) * 3; // Up to 3x at 100%
        var adaptiveDelay = (int)(_baseDelayMs * (1 + scaleFactor));

        // Cap the maximum delay at 10x base delay
        return Math.Min(adaptiveDelay, _baseDelayMs * 10);
    }

    /// <inheritdoc />
    public void Reset()
    {
        _ = Interlocked.Exchange(ref _eventCount, 0);
        _ = Interlocked.Exchange(ref _totalDelayMs, 0);
        _stopwatch.Restart();

        _logger.LogInformation("Backpressure handler reset");
    }

    /// <inheritdoc />
    public float Threshold { get; }

    /// <inheritdoc />
    public bool IsAdaptive { get; }

    /// <inheritdoc />
    public long BackpressureEventCount => Interlocked.Read(ref _eventCount);

    /// <inheritdoc />
    public long TotalDelayMs => Interlocked.Read(ref _totalDelayMs);
}
