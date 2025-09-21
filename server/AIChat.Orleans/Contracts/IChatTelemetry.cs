using System.Diagnostics;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Interface for collecting telemetry and metrics from chat grains.
/// Provides hooks for monitoring, alerting, and performance tracking.
/// </summary>
public interface IChatTelemetry
{
    /// <summary>
    /// Records that a message was processed.
    /// </summary>
    /// <param name="chatId">ID of the chat</param>
    /// <param name="messageId">ID of the message</param>
    /// <param name="duration">Time taken to process the message</param>
    /// <param name="success">Whether the processing was successful</param>
    void RecordMessageProcessed(string chatId, string messageId, TimeSpan duration, bool success);

    /// <summary>
    /// Records that a stream was started.
    /// </summary>
    /// <param name="chatId">ID of the chat</param>
    /// <param name="streamId">ID of the stream</param>
    /// <param name="participantId">ID of the participant who started the stream</param>
    void RecordStreamStarted(string chatId, string streamId, string participantId);

    /// <summary>
    /// Records that a stream was completed.
    /// </summary>
    /// <param name="chatId">ID of the chat</param>
    /// <param name="streamId">ID of the stream</param>
    /// <param name="duration">Duration of the stream</param>
    /// <param name="chunksProcessed">Number of chunks processed</param>
    void RecordStreamCompleted(string chatId, string streamId, TimeSpan duration, int chunksProcessed);

    /// <summary>
    /// Records a participant action.
    /// </summary>
    /// <param name="chatId">ID of the chat</param>
    /// <param name="participantId">ID of the participant</param>
    /// <param name="action">Action performed</param>
    /// <param name="success">Whether the action was successful</param>
    void RecordParticipantAction(string chatId, string participantId, string action, bool success);

    /// <summary>
    /// Records an error that occurred.
    /// </summary>
    /// <param name="chatId">ID of the chat</param>
    /// <param name="exception">Exception that occurred</param>
    /// <param name="context">Additional context about where the error occurred</param>
    void RecordError(string chatId, Exception exception, string context);

    /// <summary>
    /// Records grain activation.
    /// </summary>
    /// <param name="grainType">Type of grain activated</param>
    /// <param name="grainId">ID of the grain</param>
    /// <param name="activationTime">Time taken to activate</param>
    void RecordGrainActivation(string grainType, string grainId, TimeSpan activationTime);

    /// <summary>
    /// Records grain deactivation.
    /// </summary>
    /// <param name="grainType">Type of grain deactivated</param>
    /// <param name="grainId">ID of the grain</param>
    /// <param name="lifetime">How long the grain was active</param>
    void RecordGrainDeactivation(string grainType, string grainId, TimeSpan lifetime);

    /// <summary>
    /// Records state size for monitoring memory usage.
    /// </summary>
    /// <param name="chatId">ID of the chat</param>
    /// <param name="stateSize">Size of the state in bytes</param>
    /// <param name="messageCount">Number of messages in state</param>
    /// <param name="participantCount">Number of participants</param>
    void RecordStateSize(string chatId, long stateSize, int messageCount, int participantCount);

    /// <summary>
    /// Creates an activity for distributed tracing.
    /// </summary>
    /// <param name="operationName">Name of the operation</param>
    /// <param name="tags">Optional tags for the activity</param>
    /// <returns>Activity for tracing</returns>
    Activity? StartActivity(string operationName, Dictionary<string, object?>? tags = null);

    /// <summary>
    /// Records a custom metric.
    /// </summary>
    /// <param name="metricName">Name of the metric</param>
    /// <param name="value">Value to record</param>
    /// <param name="tags">Optional tags for the metric</param>
    void RecordMetric(string metricName, double value, Dictionary<string, object?>? tags = null);
}

/// <summary>
/// Default no-op implementation of IChatTelemetry for when telemetry is disabled.
/// </summary>
public class NoOpChatTelemetry : IChatTelemetry
{
    /// <summary>
    /// Gets the singleton instance of the no-op telemetry provider.
    /// </summary>
    public static readonly NoOpChatTelemetry Instance = new();

    private NoOpChatTelemetry() { }

    /// <inheritdoc/>
    public void RecordMessageProcessed(string chatId, string messageId, TimeSpan duration, bool success) { }

    /// <inheritdoc/>
    public void RecordStreamStarted(string chatId, string streamId, string participantId) { }

    /// <inheritdoc/>
    public void RecordStreamCompleted(string chatId, string streamId, TimeSpan duration, int chunksProcessed) { }

    /// <inheritdoc/>
    public void RecordParticipantAction(string chatId, string participantId, string action, bool success) { }

    /// <inheritdoc/>
    public void RecordError(string chatId, Exception exception, string context) { }

    /// <inheritdoc/>
    public void RecordGrainActivation(string grainType, string grainId, TimeSpan activationTime) { }

    /// <inheritdoc/>
    public void RecordGrainDeactivation(string grainType, string grainId, TimeSpan lifetime) { }

    /// <inheritdoc/>
    public void RecordStateSize(string chatId, long stateSize, int messageCount, int participantCount) { }

    /// <inheritdoc/>
    public Activity? StartActivity(string operationName, Dictionary<string, object?>? tags = null) => null;

    /// <inheritdoc/>
    public void RecordMetric(string metricName, double value, Dictionary<string, object?>? tags = null) { }
}