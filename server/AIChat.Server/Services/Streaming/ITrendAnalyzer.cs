namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Interface for analyzing buffer usage trends.
/// </summary>
public interface ITrendAnalyzer
{
    /// <summary>
    /// Calculates the usage trend from historical data points.
    /// </summary>
    /// <param name="dataPoints">Collection of usage data points</param>
    /// <returns>The calculated trend</returns>
    UsageTrend CalculateTrend(IEnumerable<UsageDataPoint> dataPoints);

    /// <summary>
    /// Determines if scaling is needed based on utilization and trend.
    /// </summary>
    /// <param name="currentUtilization">Current buffer utilization percentage</param>
    /// <param name="averageUtilization">Average utilization over time window</param>
    /// <param name="trend">Current usage trend</param>
    /// <param name="scaleUpThreshold">Threshold for scaling up</param>
    /// <param name="scaleDownThreshold">Threshold for scaling down</param>
    /// <returns>Scaling recommendation</returns>
    ScalingRecommendation GetScalingRecommendation(
        float currentUtilization,
        float averageUtilization,
        UsageTrend trend,
        float scaleUpThreshold,
        float scaleDownThreshold);
}

/// <summary>
/// Data point for buffer usage analysis.
/// </summary>
public sealed class UsageDataPoint
{
    public DateTime Timestamp { get; init; }
    public int Usage { get; init; }
    public int Capacity { get; init; }
    public float Utilization { get; init; }
}

/// <summary>
/// Recommendation for buffer scaling.
/// </summary>
public sealed class ScalingRecommendation
{
    public required ScalingAction Action { get; init; }
    public required float ScaleFactor { get; init; }
    public required string Reason { get; init; }
    public float? AggressiveScaleFactor { get; init; }
}

/// <summary>
/// Scaling action to take.
/// </summary>
public enum ScalingAction
{
    None,
    ScaleUp,
    ScaleDown
}