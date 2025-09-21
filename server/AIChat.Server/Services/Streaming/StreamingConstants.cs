namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Constants used throughout the streaming services.
/// </summary>
public static class StreamingConstants
{
    /// <summary>
    /// Buffer management constants.
    /// </summary>
    public static class Buffer
    {
        /// <summary>
        /// Minimum number of data points required for trend analysis.
        /// </summary>
        public const int MinDataPointsForAnalysis = 10;

        /// <summary>
        /// Default initial buffer size if not specified.
        /// </summary>
        public const int DefaultInitialSize = 100;

        /// <summary>
        /// Trend detection slope threshold for determining increasing/decreasing trends.
        /// </summary>
        public const double TrendSlopeThreshold = 0.5;

        /// <summary>
        /// Multiplier for aggressive scaling when trend is increasing.
        /// </summary>
        public const float AggressiveScaleMultiplier = 1.5f;

        /// <summary>
        /// Near-zero threshold for avoiding division by zero in calculations.
        /// </summary>
        public const double EpsilonThreshold = 0.0001;
    }

    /// <summary>
    /// Recovery and replay constants.
    /// </summary>
    public static class Recovery
    {
        /// <summary>
        /// Default delay between replayed messages in milliseconds.
        /// </summary>
        public const int ReplayMessageDelayMs = 10;

        /// <summary>
        /// Default maximum buffer size for message replay.
        /// </summary>
        public const int DefaultReplayBufferSize = 1000;

        /// <summary>
        /// Time to keep recovered stream states before cleanup (in hours).
        /// </summary>
        public const int RecoveredStateRetentionHours = 1;

        /// <summary>
        /// Minimum cleanup interval divisor (cleanup interval = max age / divisor).
        /// </summary>
        public const int CleanupIntervalDivisor = 10;

        /// <summary>
        /// Minimum cleanup interval in seconds.
        /// </summary>
        public const int MinCleanupIntervalSeconds = 30;
    }

    /// <summary>
    /// Timing and scheduling constants.
    /// </summary>
    public static class Timing
    {
        /// <summary>
        /// Minimum analysis interval in seconds.
        /// </summary>
        public const int MinAnalysisIntervalSeconds = 10;

        /// <summary>
        /// Analysis interval divisor (analysis interval = cooldown / divisor).
        /// </summary>
        public const int AnalysisIntervalDivisor = 3;
    }

    /// <summary>
    /// Overflow strategy constants.
    /// </summary>
    public static class Overflow
    {
        /// <summary>
        /// Default number of items to drop when using drop strategies.
        /// </summary>
        public const int DefaultDropCount = 10;

        /// <summary>
        /// Maximum backpressure delay multiplier.
        /// </summary>
        public const float MaxBackpressureMultiplier = 2.0f;

        /// <summary>
        /// Base delay for backpressure in milliseconds.
        /// </summary>
        public const int BaseBackpressureDelayMs = 100;
    }
}