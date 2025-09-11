using System.Diagnostics;

namespace AIChat.Orleans.Tracing;

/// <summary>
/// Centralized ActivitySource for Orleans grain tracing.
/// Provides consistent distributed tracing across all Orleans components.
/// </summary>
public static class OrleansActivitySource
{
    /// <summary>
    /// The name of the activity source for Orleans operations.
    /// </summary>
    public const string ActivitySourceName = "AIChat.Orleans";

    /// <summary>
    /// Version of the activity source.
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// The main activity source for Orleans grains and operations.
    /// </summary>
    public static readonly ActivitySource Source = new(ActivitySourceName, Version);

    /// <summary>
    /// Creates a new activity for a grain method call.
    /// </summary>
    /// <param name="grainType">The type of grain (e.g., "UserGrain", "HealthCheckGrain")</param>
    /// <param name="methodName">The method being called</param>
    /// <param name="grainId">The grain identifier (optional)</param>
    /// <returns>The created activity, or null if tracing is disabled</returns>
    public static Activity? StartGrainActivity(
        string grainType,
        string methodName,
        string? grainId = null
    )
    {
        var activityName = $"{grainType}.{methodName}";
        var activity = Source.StartActivity(activityName);

        if (activity != null)
        {
            _ = activity.SetTag("grain.type", grainType);
            _ = activity.SetTag("grain.method", methodName);

            if (!string.IsNullOrEmpty(grainId))
            {
                _ = activity.SetTag("grain.id", grainId);
            }

            // Standard OpenTelemetry attributes
            _ = activity.SetTag("component", "orleans");
            _ = activity.SetTag("service.name", "AIChat");
        }

        return activity;
    }

    /// <summary>
    /// Creates a new activity for a background service operation.
    /// </summary>
    /// <param name="serviceName">The name of the service</param>
    /// <param name="operationName">The operation being performed</param>
    /// <param name="operationId">The operation identifier (optional)</param>
    /// <returns>The created activity, or null if tracing is disabled</returns>
    public static Activity? StartBackgroundActivity(
        string serviceName,
        string operationName,
        string? operationId = null
    )
    {
        var activityName = $"{serviceName}.{operationName}";
        var activity = Source.StartActivity(activityName);

        if (activity != null)
        {
            _ = activity.SetTag("service.type", "background");
            _ = activity.SetTag("service.name", serviceName);
            _ = activity.SetTag("operation.name", operationName);

            if (!string.IsNullOrEmpty(operationId))
            {
                _ = activity.SetTag("operation.id", operationId);
            }

            // Standard OpenTelemetry attributes
            _ = activity.SetTag("component", "background-service");
        }

        return activity;
    }

    /// <summary>
    /// Adds error information to an activity.
    /// </summary>
    /// <param name="activity">The activity to update</param>
    /// <param name="exception">The exception that occurred</param>
    public static void SetError(Activity? activity, Exception exception)
    {
        if (activity == null)
        {
            return;
        }

        _ = activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        _ = activity.SetTag("error", true);
        _ = activity.SetTag("error.type", exception.GetType().Name);
        _ = activity.SetTag("error.message", exception.Message);
        _ = activity.SetTag("error.stack", exception.StackTrace);
    }

    /// <summary>
    /// Sets the activity status to OK and adds completion tags.
    /// </summary>
    /// <param name="activity">The activity to complete</param>
    /// <param name="additionalTags">Optional additional tags to set</param>
    public static void SetSuccess(
        Activity? activity,
        Dictionary<string, object>? additionalTags = null
    )
    {
        if (activity == null)
        {
            return;
        }

        _ = activity.SetStatus(ActivityStatusCode.Ok);

        if (additionalTags != null)
        {
            foreach (var tag in additionalTags)
            {
                _ = activity.SetTag(tag.Key, tag.Value);
            }
        }
    }
}
