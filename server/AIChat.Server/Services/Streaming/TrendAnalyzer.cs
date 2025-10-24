namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Analyzes buffer usage trends and provides scaling recommendations.
/// </summary>
public sealed class TrendAnalyzer : ITrendAnalyzer
{
    /// <summary>
    /// Sensitivity for trend detection
    /// </summary>
    private const double TrendThreshold = 0.5;
    private readonly ILogger<TrendAnalyzer> _logger;

    public TrendAnalyzer(ILogger<TrendAnalyzer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public UsageTrend CalculateTrend(IEnumerable<UsageDataPoint> dataPoints)
    {
        var points = dataPoints.ToArray();
        if (points.Length < 2)
        {
            return UsageTrend.Stable;
        }

        // Simple linear regression for trend
        var n = points.Length;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumX2 = 0.0;

        for (var i = 0; i < n; i++)
        {
            var x = i;
            var y = points[i].Utilization;
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        var denominator = (n * sumX2) - (sumX * sumX);
        if (Math.Abs(denominator) < 0.0001) // Avoid division by zero
        {
            return UsageTrend.Stable;
        }

        var slope = ((n * sumXY) - (sumX * sumY)) / denominator;

        // Determine trend based on slope
        if (slope > TrendThreshold)
        {
            _logger.LogDebug("Detected increasing trend with slope {Slope:F3}", slope);
            return UsageTrend.Increasing;
        }

        if (slope < -TrendThreshold)
        {
            _logger.LogDebug("Detected decreasing trend with slope {Slope:F3}", slope);
            return UsageTrend.Decreasing;
        }

        return UsageTrend.Stable;
    }

    /// <inheritdoc />
    public ScalingRecommendation GetScalingRecommendation(
        float currentUtilization,
        float averageUtilization,
        UsageTrend trend,
        float scaleUpThreshold,
        float scaleDownThreshold)
    {
        // Scale up logic
        if (averageUtilization > scaleUpThreshold)
        {
            if (trend == UsageTrend.Increasing)
            {
                // More aggressive scaling for increasing trend
                _logger.LogInformation(
                    "Recommending aggressive scale up due to high utilization {Utilization:F1}% and increasing trend",
                    averageUtilization);

                return new ScalingRecommendation
                {
                    Action = ScalingAction.ScaleUp,
                    ScaleFactor = 1.5f,
                    Reason = $"High utilization ({averageUtilization:F1}%) with increasing trend",
                    AggressiveScaleFactor = 2.0f
                };
            }

            _logger.LogInformation(
                "Recommending scale up due to high utilization {Utilization:F1}%",
                averageUtilization);

            return new ScalingRecommendation
            {
                Action = ScalingAction.ScaleUp,
                ScaleFactor = 1.5f,
                Reason = $"High utilization ({averageUtilization:F1}%)"
            };
        }

        // Scale down logic
        if (averageUtilization < scaleDownThreshold && trend != UsageTrend.Increasing)
        {
            _logger.LogInformation(
                "Recommending scale down due to low utilization {Utilization:F1}% and non-increasing trend",
                averageUtilization);

            return new ScalingRecommendation
            {
                Action = ScalingAction.ScaleDown,
                ScaleFactor = 1.5f,
                Reason = $"Low utilization ({averageUtilization:F1}%)"
            };
        }

        return new ScalingRecommendation
        {
            Action = ScalingAction.None,
            ScaleFactor = 1.0f,
            Reason = "No scaling needed"
        };
    }
}