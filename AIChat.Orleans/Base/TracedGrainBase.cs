using System.Diagnostics;
using AIChat.Orleans.Tracing;
using Microsoft.Extensions.Logging;
using Orleans;

namespace AIChat.Orleans.Base;

/// <summary>
/// Base class for Orleans grains that provides automatic distributed tracing support.
/// All grains should inherit from this class to get consistent tracing behavior.
/// </summary>
/// <typeparam name="TGrainState">The grain state type</typeparam>
public abstract class TracedGrainBase<TGrainState> : Grain<TGrainState> where TGrainState : new()
{
    /// <summary>
    /// Logger instance for diagnostics and tracing information.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Initializes a new instance of the TracedGrainBase.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics</param>
    protected TracedGrainBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new tracing activity for a grain method call.
    /// Use this at the beginning of grain methods that should be traced.
    /// </summary>
    /// <param name="methodName">The name of the method being called</param>
    /// <returns>The created activity, or null if tracing is disabled</returns>
    protected Activity? StartActivity(string methodName)
    {
        var grainType = GetType().Name;
        var grainId = this.GetPrimaryKeyString();

        var activity = OrleansActivitySource.StartGrainActivity(grainType, methodName, grainId);

        if (activity != null)
        {
            Logger.LogDebug("Started tracing activity {ActivityId} for {GrainType}.{Method} (GrainId: {GrainId})",
                activity.Id, grainType, methodName, grainId);
        }

        return activity;
    }

    /// <summary>
    /// Completes a tracing activity with success status.
    /// </summary>
    /// <param name="activity">The activity to complete</param>
    /// <param name="additionalTags">Optional additional tags to add</param>
    protected void CompleteActivity(Activity? activity, Dictionary<string, object>? additionalTags = null)
    {
        if (activity == null)
        {
            return;
        }

        OrleansActivitySource.SetSuccess(activity, additionalTags);

        Logger.LogDebug("Completed tracing activity {ActivityId} successfully", activity.Id);

        activity.Dispose();
    }

    /// <summary>
    /// Completes a tracing activity with error status.
    /// </summary>
    /// <param name="activity">The activity to complete</param>
    /// <param name="exception">The exception that occurred</param>
    protected void CompleteActivityWithError(Activity? activity, Exception exception)
    {
        if (activity == null)
        {
            return;
        }

        OrleansActivitySource.SetError(activity, exception);

        Logger.LogError(exception, "Completed tracing activity {ActivityId} with error", activity.Id);

        activity.Dispose();
    }

    /// <summary>
    /// Executes a grain method with automatic tracing.
    /// Handles activity creation, completion, and error handling automatically.
    /// </summary>
    /// <typeparam name="TResult">The return type of the method</typeparam>
    /// <param name="methodName">The name of the method being executed</param>
    /// <param name="operation">The operation to execute</param>
    /// <returns>The result of the operation</returns>
    protected async Task<TResult> ExecuteWithTracing<TResult>(string methodName, Func<Task<TResult>> operation)
    {
        using var activity = StartActivity(methodName);

        try
        {
            var result = await operation();
            CompleteActivity(activity);
            return result;
        }
        catch (Exception ex)
        {
            CompleteActivityWithError(activity, ex);
            throw;
        }
    }

    /// <summary>
    /// Executes a void grain method with automatic tracing.
    /// Handles activity creation, completion, and error handling automatically.
    /// </summary>
    /// <param name="methodName">The name of the method being executed</param>
    /// <param name="operation">The operation to execute</param>
    protected async Task ExecuteWithTracing(string methodName, Func<Task> operation)
    {
        using var activity = StartActivity(methodName);

        try
        {
            await operation();
            CompleteActivity(activity);
        }
        catch (Exception ex)
        {
            CompleteActivityWithError(activity, ex);
            throw;
        }
    }
}
