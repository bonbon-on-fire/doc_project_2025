using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace WhatsAppWaha.Core.Exceptions;

/// <summary>
/// Base exception for all WhatsApp WAHA framework-related errors.
/// </summary>
[Serializable]
public abstract class WhatsAppWahaException : Exception
{
  /// <summary>
  /// Gets the error code associated with this exception.
  /// </summary>
  public string ErrorCode { get; }

  /// <summary>
  /// Gets additional context data for this exception.
  /// </summary>
  public Dictionary<string, object> Context { get; }

  /// <summary>
  /// Initializes a new instance of the <see cref="WhatsAppWahaException"/> class.
  /// </summary>
  /// <param name="message">The exception message.</param>
  /// <param name="errorCode">The error code.</param>
  protected WhatsAppWahaException(string message, string errorCode)
      : base(message)
  {
    ErrorCode = errorCode;
    Context = new Dictionary<string, object>();
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="WhatsAppWahaException"/> class.
  /// </summary>
  /// <param name="message">The exception message.</param>
  /// <param name="errorCode">The error code.</param>
  /// <param name="innerException">The inner exception.</param>
  protected WhatsAppWahaException(string message, string errorCode, Exception innerException)
      : base(message, innerException)
  {
    ErrorCode = errorCode;
    Context = new Dictionary<string, object>();
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="WhatsAppWahaException"/> class.
  /// </summary>
  /// <param name="info">The serialization info.</param>
  /// <param name="context">The streaming context.</param>
  [ExcludeFromCodeCoverage]
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete in .NET 9
  protected WhatsAppWahaException(SerializationInfo info, StreamingContext context)
      : base(info, context)
  {
    ErrorCode = info.GetString(nameof(ErrorCode)) ?? "UNKNOWN";
    Context = (Dictionary<string, object>)(info.GetValue(nameof(Context), typeof(Dictionary<string, object>))
                                           ?? new Dictionary<string, object>());
  }
#pragma warning restore SYSLIB0051 // Formatter-based serialization is obsolete in .NET 9

  /// <summary>
  /// Adds context data to the exception.
  /// </summary>
  /// <param name="key">The context key.</param>
  /// <param name="value">The context value.</param>
  /// <returns>This exception instance for chaining.</returns>
  public WhatsAppWahaException WithContext(string key, object value)
  {
    Context[key] = value;
    return this;
  }

  /// <summary>
  /// Sets the object data for serialization.
  /// </summary>
  /// <param name="info">The serialization info.</param>
  /// <param name="context">The streaming context.</param>
  [ExcludeFromCodeCoverage]
  [Obsolete("Formatter-based serialization is obsolete in .NET 9")]
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete in .NET 9
  public override void GetObjectData(SerializationInfo info, StreamingContext context)
  {
    base.GetObjectData(info, context);
    info.AddValue(nameof(ErrorCode), ErrorCode);
    info.AddValue(nameof(Context), Context);
  }
#pragma warning restore SYSLIB0051 // Formatter-based serialization is obsolete in .NET 9
}

/// <summary>
/// Exception thrown when WAHA service operations fail.
/// </summary>
[Serializable]
public class WahaServiceException : WhatsAppWahaException
{
  /// <summary>
  /// Error codes for WAHA service exceptions.
  /// </summary>
  public static class ErrorCodes
  {
    public const string ConnectionFailed = "WAHA_CONNECTION_FAILED";
    public const string AuthenticationFailed = "WAHA_AUTH_FAILED";
    public const string InvalidSession = "WAHA_INVALID_SESSION";
    public const string MessageSendFailed = "WAHA_MESSAGE_SEND_FAILED";
    public const string InvalidPhoneNumber = "WAHA_INVALID_PHONE_NUMBER";
    public const string RateLimitExceeded = "WAHA_RATE_LIMIT_EXCEEDED";
    public const string ServiceUnavailable = "WAHA_SERVICE_UNAVAILABLE";
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="WahaServiceException"/> class.
  /// </summary>
  /// <param name="message">The exception message.</param>
  /// <param name="errorCode">The error code.</param>
  public WahaServiceException(string message, string errorCode)
      : base(message, errorCode) { }

  /// <summary>
  /// Initializes a new instance of the <see cref="WahaServiceException"/> class.
  /// </summary>
  /// <param name="message">The exception message.</param>
  /// <param name="errorCode">The error code.</param>
  /// <param name="innerException">The inner exception.</param>
  public WahaServiceException(string message, string errorCode, Exception innerException)
      : base(message, errorCode, innerException) { }

  /// <summary>
  /// Initializes a new instance of the <see cref="WahaServiceException"/> class.
  /// </summary>
  /// <param name="info">The serialization info.</param>
  /// <param name="context">The streaming context.</param>
  [ExcludeFromCodeCoverage]
  protected WahaServiceException(SerializationInfo info, StreamingContext context)
      : base(info, context) { }
}

/// <summary>
/// Exception thrown when ntfy service operations fail.
/// </summary>
[Serializable]
public class NtfyServiceException : WhatsAppWahaException
{
  /// <summary>
  /// Error codes for ntfy service exceptions.
  /// </summary>
  public static class ErrorCodes
  {
    public const string ConnectionFailed = "NTFY_CONNECTION_FAILED";
    public const string AuthenticationFailed = "NTFY_AUTH_FAILED";
    public const string InvalidTopic = "NTFY_INVALID_TOPIC";
    public const string MessageRetrievalFailed = "NTFY_MESSAGE_RETRIEVAL_FAILED";
    public const string NotificationSendFailed = "NTFY_NOTIFICATION_SEND_FAILED";
    public const string ServiceUnavailable = "NTFY_SERVICE_UNAVAILABLE";
    public const string PollingFailed = "NTFY_POLLING_FAILED";
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="NtfyServiceException"/> class.
  /// </summary>
  /// <param name="message">The exception message.</param>
  /// <param name="errorCode">The error code.</param>
  public NtfyServiceException(string message, string errorCode)
      : base(message, errorCode) { }

  /// <summary>
  /// Initializes a new instance of the <see cref="NtfyServiceException"/> class.
  /// </summary>
  /// <param name="message">The exception message.</param>
  /// <param name="errorCode">The error code.</param>
  /// <param name="innerException">The inner exception.</param>
  public NtfyServiceException(string message, string errorCode, Exception innerException)
      : base(message, errorCode, innerException) { }

  /// <summary>
  /// Initializes a new instance of the <see cref="NtfyServiceException"/> class.
  /// </summary>
  /// <param name="info">The serialization info.</param>
  /// <param name="context">The streaming context.</param>
  [ExcludeFromCodeCoverage]
  protected NtfyServiceException(SerializationInfo info, StreamingContext context)
      : base(info, context) { }
}

/// <summary>
/// Exception thrown when configuration validation fails.
/// </summary>
[Serializable]
public class ConfigurationException : WhatsAppWahaException
{
  /// <summary>
  /// Error codes for configuration exceptions.
  /// </summary>
  public static class ErrorCodes
  {
    public const string ValidationFailed = "CONFIG_VALIDATION_FAILED";
    public const string MissingSection = "CONFIG_MISSING_SECTION";
    public const string InvalidValue = "CONFIG_INVALID_VALUE";
    public const string LoadFailed = "CONFIG_LOAD_FAILED";
  }

  /// <summary>
  /// Gets the configuration section name that caused the error.
  /// </summary>
  public string? SectionName { get; }

  /// <summary>
  /// Gets the validation errors if this is a validation exception.
  /// </summary>
  public IReadOnlyList<string> ValidationErrors { get; }

  /// <summary>
  /// Initializes a new instance of the <see cref="ConfigurationException"/> class.
  /// </summary>
  /// <param name="message">The exception message.</param>
  /// <param name="errorCode">The error code.</param>
  /// <param name="sectionName">The configuration section name.</param>
  public ConfigurationException(string message, string errorCode, string? sectionName = null)
      : base(message, errorCode)
  {
    SectionName = sectionName;
    ValidationErrors = Array.Empty<string>();
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="ConfigurationException"/> class.
  /// </summary>
  /// <param name="message">The exception message.</param>
  /// <param name="errorCode">The error code.</param>
  /// <param name="validationErrors">The validation errors.</param>
  /// <param name="sectionName">The configuration section name.</param>
  public ConfigurationException(string message, string errorCode, IEnumerable<string> validationErrors, string? sectionName = null)
      : base(message, errorCode)
  {
    SectionName = sectionName;
    ValidationErrors = validationErrors.ToList();
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="ConfigurationException"/> class.
  /// </summary>
  /// <param name="message">The exception message.</param>
  /// <param name="errorCode">The error code.</param>
  /// <param name="innerException">The inner exception.</param>
  /// <param name="sectionName">The configuration section name.</param>
  public ConfigurationException(string message, string errorCode, Exception innerException, string? sectionName = null)
      : base(message, errorCode, innerException)
  {
    SectionName = sectionName;
    ValidationErrors = Array.Empty<string>();
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="ConfigurationException"/> class.
  /// </summary>
  /// <param name="info">The serialization info.</param>
  /// <param name="context">The streaming context.</param>
  [ExcludeFromCodeCoverage]
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete in .NET 9
  protected ConfigurationException(SerializationInfo info, StreamingContext context)
      : base(info, context)
  {
    SectionName = info.GetString(nameof(SectionName));
    ValidationErrors = (IReadOnlyList<string>)(info.GetValue(nameof(ValidationErrors), typeof(IReadOnlyList<string>))
                                               ?? Array.Empty<string>());
  }
#pragma warning restore SYSLIB0051 // Formatter-based serialization is obsolete in .NET 9

  /// <summary>
  /// Sets the object data for serialization.
  /// </summary>
  /// <param name="info">The serialization info.</param>
  /// <param name="context">The streaming context.</param>
  [ExcludeFromCodeCoverage]
  [Obsolete("Formatter-based serialization is obsolete in .NET 9")]
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete in .NET 9
  public override void GetObjectData(SerializationInfo info, StreamingContext context)
  {
    base.GetObjectData(info, context);
    info.AddValue(nameof(SectionName), SectionName);
    info.AddValue(nameof(ValidationErrors), ValidationErrors);
  }
#pragma warning restore SYSLIB0051 // Formatter-based serialization is obsolete in .NET 9
}

/// <summary>
/// Exception thrown when message processing operations fail.
/// </summary>
[Serializable]
public class MessageProcessingException : WhatsAppWahaException
{
  /// <summary>
  /// Error codes for message processing exceptions.
  /// </summary>
  public static class ErrorCodes
  {
    public const string ProcessingFailed = "MESSAGE_PROCESSING_FAILED";
    public const string InvalidMessage = "MESSAGE_INVALID";
    public const string DeserializationFailed = "MESSAGE_DESERIALIZATION_FAILED";
    public const string ValidationFailed = "MESSAGE_VALIDATION_FAILED";
    public const string ProcessorNotFound = "MESSAGE_PROCESSOR_NOT_FOUND";
  }

  /// <summary>
  /// Gets the message ID that caused the error.
  /// </summary>
  public string? MessageId { get; }

  /// <summary>
  /// Initializes a new instance of the <see cref="MessageProcessingException"/> class.
  /// </summary>
  /// <param name="message">The exception message.</param>
  /// <param name="errorCode">The error code.</param>
  /// <param name="messageId">The message ID.</param>
  public MessageProcessingException(string message, string errorCode, string? messageId = null)
      : base(message, errorCode)
  {
    MessageId = messageId;
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="MessageProcessingException"/> class.
  /// </summary>
  /// <param name="message">The exception message.</param>
  /// <param name="errorCode">The error code.</param>
  /// <param name="innerException">The inner exception.</param>
  /// <param name="messageId">The message ID.</param>
  public MessageProcessingException(string message, string errorCode, Exception innerException, string? messageId = null)
      : base(message, errorCode, innerException)
  {
    MessageId = messageId;
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="MessageProcessingException"/> class.
  /// </summary>
  /// <param name="info">The serialization info.</param>
  /// <param name="context">The streaming context.</param>
  [ExcludeFromCodeCoverage]
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete in .NET 9
  protected MessageProcessingException(SerializationInfo info, StreamingContext context)
      : base(info, context)
  {
    MessageId = info.GetString(nameof(MessageId));
  }
#pragma warning restore SYSLIB0051 // Formatter-based serialization is obsolete in .NET 9

  /// <summary>
  /// Sets the object data for serialization.
  /// </summary>
  /// <param name="info">The serialization info.</param>
  /// <param name="context">The streaming context.</param>
  [ExcludeFromCodeCoverage]
  [Obsolete("Formatter-based serialization is obsolete in .NET 9")]
#pragma warning disable SYSLIB0051 // Formatter-based serialization is obsolete in .NET 9
  public override void GetObjectData(SerializationInfo info, StreamingContext context)
  {
    base.GetObjectData(info, context);
    info.AddValue(nameof(MessageId), MessageId);
  }
#pragma warning restore SYSLIB0051 // Formatter-based serialization is obsolete in .NET 9
}
