using System.Runtime.Serialization;

namespace AIChat.Server.Services.Recovery;

/// <summary>
/// Base exception for recovery-related errors.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryException")]
public class RecoveryException : Exception
{
    /// <summary>
    /// Gets the grain ID associated with the recovery failure.
    /// </summary>
    [Id(0)]
    public string? GrainId { get; }

    /// <summary>
    /// Gets the correlation ID for tracking the recovery operation.
    /// </summary>
    [Id(1)]
    public string? CorrelationId { get; }

    /// <summary>
    /// Initializes a new instance of the RecoveryException class.
    /// </summary>
    public RecoveryException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the RecoveryException class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public RecoveryException(string? message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the RecoveryException class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public RecoveryException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the RecoveryException class with grain and correlation information.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="grainId">The grain ID associated with the failure</param>
    /// <param name="correlationId">The correlation ID for tracking</param>
    public RecoveryException(string? message, string? grainId, string? correlationId) : base(message)
    {
        GrainId = grainId;
        CorrelationId = correlationId;
    }

    /// <summary>
    /// Initializes a new instance of the RecoveryException class with grain and correlation information and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="grainId">The grain ID associated with the failure</param>
    /// <param name="correlationId">The correlation ID for tracking</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public RecoveryException(string? message, string? grainId, string? correlationId, Exception? innerException)
        : base(message, innerException)
    {
        GrainId = grainId;
        CorrelationId = correlationId;
    }

    /// <summary>
    /// Initializes a new instance of the RecoveryException class with serialized data.
    /// </summary>
    /// <param name="info">The SerializationInfo that holds the serialized object data</param>
    /// <param name="context">The StreamingContext that contains contextual information</param>
    [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete
    protected RecoveryException(SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context)
#pragma warning restore SYSLIB0051
    {
        GrainId = info.GetString(nameof(GrainId));
        CorrelationId = info.GetString(nameof(CorrelationId));
    }

    /// <summary>
    /// Sets the SerializationInfo with information about the exception.
    /// </summary>
    /// <param name="info">The SerializationInfo that holds the serialized object data</param>
    /// <param name="context">The StreamingContext that contains contextual information</param>
    [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete
    public override void GetObjectData(SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(GrainId), GrainId);
        info.AddValue(nameof(CorrelationId), CorrelationId);
    }
#pragma warning restore SYSLIB0051
}

/// <summary>
/// Exception thrown when state recovery detection fails.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.StateRecoveryDetectionException")]
public class StateRecoveryDetectionException : RecoveryException
{
    /// <summary>
    /// Initializes a new instance of the StateRecoveryDetectionException class.
    /// </summary>
    public StateRecoveryDetectionException() : base("State recovery detection failed")
    {
    }

    /// <summary>
    /// Initializes a new instance of the StateRecoveryDetectionException class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public StateRecoveryDetectionException(string? message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the StateRecoveryDetectionException class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public StateRecoveryDetectionException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the StateRecoveryDetectionException class with grain information.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="grainId">The grain ID where detection failed</param>
    /// <param name="correlationId">The correlation ID for tracking</param>
    public StateRecoveryDetectionException(string? message, string? grainId, string? correlationId)
        : base(message, grainId, correlationId)
    {
    }

    /// <summary>
    /// Initializes a new instance of the StateRecoveryDetectionException class with grain information and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="grainId">The grain ID where detection failed</param>
    /// <param name="grainType">The grain type where detection failed</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public StateRecoveryDetectionException(string? message, string? grainId, string? grainType, Exception? innerException)
        : base(message, grainId, null, innerException)
    {
        // Store grainType in Data dictionary for additional context
        if (grainType != null)
        {
            Data["GrainType"] = grainType;
        }
    }

    /// <summary>
    /// Initializes a new instance of the StateRecoveryDetectionException class with serialized data.
    /// </summary>
    /// <param name="info">The SerializationInfo that holds the serialized object data</param>
    /// <param name="context">The StreamingContext that contains contextual information</param>
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete
    protected StateRecoveryDetectionException(SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context)
    {
    }
#pragma warning restore SYSLIB0051
}

/// <summary>
/// Exception thrown when state recovery orchestration fails.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.StateRecoveryOrchestrationException")]
public class StateRecoveryOrchestrationException : RecoveryException
{
    /// <summary>
    /// Gets the recovery strategy that failed.
    /// </summary>
    [Id(0)]
    public RecoveryStrategy? FailedStrategy { get; }

    /// <summary>
    /// Initializes a new instance of the StateRecoveryOrchestrationException class.
    /// </summary>
    public StateRecoveryOrchestrationException() : base("State recovery orchestration failed")
    {
    }

    /// <summary>
    /// Initializes a new instance of the StateRecoveryOrchestrationException class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public StateRecoveryOrchestrationException(string? message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the StateRecoveryOrchestrationException class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public StateRecoveryOrchestrationException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the StateRecoveryOrchestrationException class with strategy information.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="failedStrategy">The recovery strategy that failed</param>
    /// <param name="grainId">The grain ID where orchestration failed</param>
    /// <param name="correlationId">The correlation ID for tracking</param>
    public StateRecoveryOrchestrationException(
        string? message,
        RecoveryStrategy? failedStrategy,
        string? grainId,
        string? correlationId)
        : base(message, grainId, correlationId)
    {
        FailedStrategy = failedStrategy;
    }

    /// <summary>
    /// Initializes a new instance of the StateRecoveryOrchestrationException class with grain information.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="grainId">The grain ID where orchestration failed</param>
    /// <param name="grainType">The grain type where orchestration failed</param>
    public StateRecoveryOrchestrationException(string? message, string? grainId, string? grainType)
        : base(message, grainId, null)
    {
        // Store grainType in Data dictionary for additional context
        if (grainType != null)
        {
            Data["GrainType"] = grainType;
        }
    }

    /// <summary>
    /// Initializes a new instance of the StateRecoveryOrchestrationException class with grain information and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="grainId">The grain ID where orchestration failed</param>
    /// <param name="grainType">The grain type where orchestration failed</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public StateRecoveryOrchestrationException(string? message, string? grainId, string? grainType, Exception? innerException)
        : base(message, grainId, null, innerException)
    {
        // Store grainType in Data dictionary for additional context
        if (grainType != null)
        {
            Data["GrainType"] = grainType;
        }
    }

    /// <summary>
    /// Initializes a new instance of the StateRecoveryOrchestrationException class with serialized data.
    /// </summary>
    /// <param name="info">The SerializationInfo that holds the serialized object data</param>
    /// <param name="context">The StreamingContext that contains contextual information</param>
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete
    protected StateRecoveryOrchestrationException(SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context)
    {
        FailedStrategy = (RecoveryStrategy?)info.GetValue(nameof(FailedStrategy), typeof(RecoveryStrategy?));
    }
#pragma warning restore SYSLIB0051

    /// <summary>
    /// Sets the SerializationInfo with information about the exception.
    /// </summary>
    /// <param name="info">The SerializationInfo that holds the serialized object data</param>
    /// <param name="context">The StreamingContext that contains contextual information</param>
    [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete
    public override void GetObjectData(SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(FailedStrategy), FailedStrategy);
    }
#pragma warning restore SYSLIB0051
}

/// <summary>
/// Exception thrown when state consistency verification fails.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.StateConsistencyVerificationException")]
public class StateConsistencyVerificationException : RecoveryException
{
    /// <summary>
    /// Gets the consistency issues found during verification.
    /// </summary>
    [Id(0)]
    public IReadOnlyList<string> ConsistencyIssues { get; }

    /// <summary>
    /// Initializes a new instance of the StateConsistencyVerificationException class.
    /// </summary>
    public StateConsistencyVerificationException() : base("State consistency verification failed")
    {
        ConsistencyIssues = [];
    }

    /// <summary>
    /// Initializes a new instance of the StateConsistencyVerificationException class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public StateConsistencyVerificationException(string? message) : base(message)
    {
        ConsistencyIssues = [];
    }

    /// <summary>
    /// Initializes a new instance of the StateConsistencyVerificationException class with consistency issues.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="consistencyIssues">The consistency issues found</param>
    /// <param name="grainId">The grain ID where verification failed</param>
    /// <param name="correlationId">The correlation ID for tracking</param>
    public StateConsistencyVerificationException(
        string? message,
        IReadOnlyList<string>? consistencyIssues,
        string? grainId,
        string? correlationId)
        : base(message, grainId, correlationId)
    {
        ConsistencyIssues = consistencyIssues ?? [];
    }

    /// <summary>
    /// Initializes a new instance of the StateConsistencyVerificationException class with grain information and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="grainId">The grain ID where verification failed</param>
    /// <param name="grainType">The grain type where verification failed</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public StateConsistencyVerificationException(string? message, string? grainId, string? grainType, Exception? innerException)
        : base(message, grainId, null, innerException)
    {
        ConsistencyIssues = [];
        // Store grainType in Data dictionary for additional context
        if (grainType != null)
        {
            Data["GrainType"] = grainType;
        }
    }

    /// <summary>
    /// Initializes a new instance of the StateConsistencyVerificationException class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public StateConsistencyVerificationException(string? message, Exception? innerException) : base(message, innerException)
    {
        ConsistencyIssues = [];
    }

    /// <summary>
    /// Initializes a new instance of the StateConsistencyVerificationException class with serialized data.
    /// </summary>
    /// <param name="info">The SerializationInfo that holds the serialized object data</param>
    /// <param name="context">The StreamingContext that contains contextual information</param>
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete
    protected StateConsistencyVerificationException(SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context)
    {
        var issuesArray = info.GetValue(nameof(ConsistencyIssues), typeof(string[])) as string[];
        ConsistencyIssues = issuesArray ?? [];
    }

    public StateConsistencyVerificationException(string? message, string? grainId, string? correlationId) : base(message, grainId, correlationId)
    {
        ConsistencyIssues = [];
    }
#pragma warning restore SYSLIB0051

    /// <summary>
    /// Sets the SerializationInfo with information about the exception.
    /// </summary>
    /// <param name="info">The SerializationInfo that holds the serialized object data</param>
    /// <param name="context">The StreamingContext that contains contextual information</param>
    [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete
    public override void GetObjectData(SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(ConsistencyIssues), ConsistencyIssues.ToArray());
    }
#pragma warning restore SYSLIB0051
}

/// <summary>
/// Exception thrown when automatic recovery service operations fail.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.AutomaticRecoveryServiceException")]
public class AutomaticRecoveryServiceException : RecoveryException
{
    /// <summary>
    /// Initializes a new instance of the AutomaticRecoveryServiceException class.
    /// </summary>
    public AutomaticRecoveryServiceException() : base("Automatic recovery service operation failed")
    {
    }

    /// <summary>
    /// Initializes a new instance of the AutomaticRecoveryServiceException class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public AutomaticRecoveryServiceException(string? message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the AutomaticRecoveryServiceException class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public AutomaticRecoveryServiceException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the AutomaticRecoveryServiceException class with grain information.
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="grainId">The grain ID associated with the failure</param>
    /// <param name="correlationId">The correlation ID for tracking</param>
    public AutomaticRecoveryServiceException(string? message, string? grainId, string? correlationId)
        : base(message, grainId, correlationId)
    {
    }

    /// <summary>
    /// Initializes a new instance of the AutomaticRecoveryServiceException class with serialized data.
    /// </summary>
    /// <param name="info">The SerializationInfo that holds the serialized object data</param>
    /// <param name="context">The StreamingContext that contains contextual information</param>
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete
    protected AutomaticRecoveryServiceException(SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context)
    {
    }

    public AutomaticRecoveryServiceException(string? message, string? grainId, string? correlationId, Exception? innerException) : base(message, grainId, correlationId, innerException)
    {
    }
#pragma warning restore SYSLIB0051
}
