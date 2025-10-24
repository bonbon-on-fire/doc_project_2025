using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orleans;

namespace AIChat.Orleans.Logging;

/// <summary>
/// Grain call filter that logs all incoming grain method invocations.
/// Provides distributed tracing and performance monitoring for grain calls.
/// </summary>
/// <remarks>
/// This filter is registered as an incoming grain call filter to provide:
/// - Entry/exit logging for all grain method calls
/// - Performance metrics (elapsed time)
/// - Error logging with full exception details
/// - Structured logging with grain type, grain ID, and method name
/// - Scope-based context for log correlation
/// </remarks>
public class LoggingGrainCallFilter : IIncomingGrainCallFilter
{
    private readonly ILogger<LoggingGrainCallFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the LoggingGrainCallFilter.
    /// </summary>
    /// <param name="logger">Logger instance for grain call diagnostics</param>
    public LoggingGrainCallFilter(ILogger<LoggingGrainCallFilter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Filters incoming grain method calls with logging and performance tracking.
    /// </summary>
    /// <param name="context">Incoming grain call context</param>
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        var grainType = context.TargetContext.GrainInstance?.GetType().Name ?? "Unknown";
        var grainId = context.TargetContext.GrainId.ToString();
        var methodName = context.InterfaceMethod?.Name ?? "Unknown";
        var siloAddress = context.TargetContext.Address?.SiloAddress?.ToString() ?? "Unknown";

        // Create structured logging scope with grain context
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["GrainType"] = grainType,
            ["GrainId"] = grainId,
            ["MethodName"] = methodName,
            ["SiloAddress"] = siloAddress
        });

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Log method entry at debug level (verbose)
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Grain method invocation started: {GrainType}.{MethodName}({GrainId})",
                    grainType,
                    methodName,
                    grainId
                );
            }

            // Invoke the actual grain method
            await context.Invoke();

            stopwatch.Stop();

            // Log method exit - only log if it took more than 100ms to avoid noise in high-frequency operations
            if (stopwatch.ElapsedMilliseconds > 100)
            {
                _logger.LogInformation(
                    "Grain method completed successfully in {ElapsedMs}ms: {GrainType}.{MethodName}",
                    stopwatch.ElapsedMilliseconds,
                    grainType,
                    methodName
                );
            }
            else if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Grain method completed successfully in {ElapsedMs}ms: {GrainType}.{MethodName}",
                    stopwatch.ElapsedMilliseconds,
                    grainType,
                    methodName
                );
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Log method failure with full exception details
            _logger.LogError(
                ex,
                "Grain method failed after {ElapsedMs}ms: {GrainType}.{MethodName} - {ExceptionType}: {ExceptionMessage}",
                stopwatch.ElapsedMilliseconds,
                grainType,
                methodName,
                ex.GetType().Name,
                ex.Message
            );

            // Re-throw to maintain Orleans error handling semantics
            throw;
        }
    }
}
