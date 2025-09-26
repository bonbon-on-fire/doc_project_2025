namespace AIChat.Server.Services.Translation.Translators;

/// <summary>
/// Defines the contract for translating messages between different protocol formats.
/// Implementations should focus on a specific source-to-target protocol conversion.
/// </summary>
/// <typeparam name="TSource">Source message type</typeparam>
/// <typeparam name="TTarget">Target message type</typeparam>
public interface IMessageTranslator<TSource, TTarget>
{
    /// <summary>
    /// Translates a message from the source format to the target format.
    /// </summary>
    /// <param name="source">Source message to translate</param>
    /// <param name="context">Translation context containing additional information</param>
    /// <param name="cancellationToken">Token to cancel the translation operation</param>
    /// <returns>Translation result containing the converted message or error information</returns>
    Task<TranslationResult<TTarget>> TranslateAsync(
        TSource source,
        TranslationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether this translator can handle the specified source message.
    /// </summary>
    /// <param name="source">Source message to evaluate</param>
    /// <returns>True if the translator can handle this message type, false otherwise</returns>
    bool CanTranslate(TSource source);

    /// <summary>
    /// Gets the source type that this translator handles.
    /// </summary>
    Type SourceType { get; }

    /// <summary>
    /// Gets the target type that this translator produces.
    /// </summary>
    Type TargetType { get; }

    /// <summary>
    /// Gets the name of this translator for logging and identification purposes.
    /// </summary>
    string TranslatorName { get; }

    /// <summary>
    /// Gets the version of this translator implementation.
    /// </summary>
    string Version { get; }
}

/// <summary>
/// Abstract base class for message translators that provides common functionality.
/// </summary>
/// <typeparam name="TSource">Source message type</typeparam>
/// <typeparam name="TTarget">Target message type</typeparam>
public abstract class MessageTranslatorBase<TSource, TTarget> : IMessageTranslator<TSource, TTarget>
{
    /// <summary>
    /// Logger instance for this translator.
    /// </summary>
    protected ILogger Logger { get; private set; }

    /// <summary>
    /// Metrics collection for translation operations.
    /// </summary>
    protected TranslationMetrics Metrics { get; } = new();

    /// <summary>
    /// Initializes a new instance of the MessageTranslatorBase.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    protected MessageTranslatorBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public abstract Task<TranslationResult<TTarget>> TranslateAsync(
        TSource source,
        TranslationContext context,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public virtual bool CanTranslate(TSource source)
    {
        return source != null && source.GetType() == SourceType;
    }

    /// <inheritdoc />
    public Type SourceType => typeof(TSource);

    /// <inheritdoc />
    public Type TargetType => typeof(TTarget);

    /// <inheritdoc />
    public abstract string TranslatorName { get; }

    /// <inheritdoc />
    public virtual string Version => "1.0.0";

    /// <summary>
    /// Validates the source message before translation.
    /// </summary>
    /// <param name="source">Source message to validate</param>
    /// <param name="context">Translation context</param>
    /// <returns>Validation result with error message if validation fails</returns>
    protected virtual TranslationResult<TTarget>? ValidateSource(TSource source, TranslationContext context)
    {
        if (source == null)
        {
            return TranslationResult.Failure<TTarget>("Source message cannot be null", "NULL_SOURCE");
        }

        if (context == null)
        {
            return TranslationResult.Failure<TTarget>("Translation context cannot be null", "NULL_CONTEXT");
        }

        return null; // No validation errors
    }

    /// <summary>
    /// Validates the target message after translation.
    /// </summary>
    /// <param name="target">Target message to validate</param>
    /// <param name="context">Translation context</param>
    /// <returns>Validation result with error message if validation fails</returns>
    protected virtual TranslationResult<TTarget>? ValidateTarget(TTarget target, TranslationContext context)
    {
        if (target == null)
        {
            return TranslationResult.Failure<TTarget>("Translation produced null result", "NULL_TARGET");
        }

        return null; // No validation errors
    }

    /// <summary>
    /// Updates metrics for a successful translation.
    /// </summary>
    /// <param name="duration">Translation duration</param>
    /// <param name="context">Translation context</param>
    protected virtual void UpdateSuccessMetrics(TimeSpan duration, TranslationContext context)
    {
        Metrics.TotalTranslations++;
        Metrics.SuccessfulTranslations++;

        var durationMs = duration.TotalMilliseconds;
        if (Metrics.MinTranslationTimeMs == 0 || durationMs < Metrics.MinTranslationTimeMs)
        {
            Metrics.MinTranslationTimeMs = durationMs;
        }

        if (durationMs > Metrics.MaxTranslationTimeMs)
        {
            Metrics.MaxTranslationTimeMs = durationMs;
        }

        // Update average (simple moving average)
        Metrics.AverageTranslationTimeMs =
            (Metrics.AverageTranslationTimeMs * (Metrics.SuccessfulTranslations - 1) + durationMs) /
            Metrics.SuccessfulTranslations;

        // Update protocol metrics
        if (!string.IsNullOrEmpty(context.SourceProtocol))
        {
            Metrics.TranslationsBySourceProtocol.TryGetValue(context.SourceProtocol, out var sourceCount);
            Metrics.TranslationsBySourceProtocol[context.SourceProtocol] = sourceCount + 1;
        }

        if (!string.IsNullOrEmpty(context.TargetProtocol))
        {
            Metrics.TranslationsByTargetProtocol.TryGetValue(context.TargetProtocol, out var targetCount);
            Metrics.TranslationsByTargetProtocol[context.TargetProtocol] = targetCount + 1;
        }
    }

    /// <summary>
    /// Updates metrics for a failed translation.
    /// </summary>
    /// <param name="duration">Translation attempt duration</param>
    /// <param name="errorCode">Error code</param>
    /// <param name="context">Translation context</param>
    protected virtual void UpdateFailureMetrics(TimeSpan duration, string? errorCode, TranslationContext context)
    {
        Metrics.TotalTranslations++;
        Metrics.FailedTranslations++;

        Logger.LogWarning(
            "Translation failed in {TranslatorName}: {ErrorCode} (Duration: {Duration}ms)",
            TranslatorName,
            errorCode,
            duration.TotalMilliseconds
        );
    }

    /// <summary>
    /// Gets the current metrics for this translator.
    /// </summary>
    /// <returns>Current translation metrics</returns>
    public virtual TranslationMetrics GetMetrics()
    {
        return Metrics;
    }

    /// <summary>
    /// Resets the metrics for this translator.
    /// </summary>
    public virtual void ResetMetrics()
    {
        Metrics.Reset();
    }
}