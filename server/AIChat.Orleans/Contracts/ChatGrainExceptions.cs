namespace AIChat.Orleans.Contracts;

/// <summary>
/// Base exception for all chat grain-related errors.
/// </summary>
public class ChatGrainException : Exception
{
    /// <summary>
    /// Gets the chat ID associated with this exception.
    /// </summary>
    public string? ChatId { get; }

    /// <summary>
    /// Gets the correlation ID for tracing this error.
    /// </summary>
    public string CorrelationId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatGrainException"/> class.
    /// </summary>
    public ChatGrainException(string message, string? chatId = null)
        : base(message)
    {
        ChatId = chatId;
        CorrelationId = Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatGrainException"/> class with an inner exception.
    /// </summary>
    public ChatGrainException(string message, Exception innerException, string? chatId = null)
        : base(message, innerException)
    {
        ChatId = chatId;
        CorrelationId = Guid.NewGuid().ToString();
    }
}

/// <summary>
/// Exception thrown when a requested chat is not found.
/// </summary>
public class ChatNotFoundException : ChatGrainException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatNotFoundException"/> class.
    /// </summary>
    public ChatNotFoundException(string chatId)
        : base($"Chat with ID '{chatId}' was not found.", chatId)
    {
    }
}

/// <summary>
/// Exception thrown when attempting to create a chat that already exists.
/// </summary>
public class ChatAlreadyExistsException : ChatGrainException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatAlreadyExistsException"/> class.
    /// </summary>
    public ChatAlreadyExistsException(string chatId)
        : base($"Chat with ID '{chatId}' already exists.", chatId)
    {
    }
}

/// <summary>
/// Exception thrown when a requested participant is not found in the chat.
/// </summary>
public class ParticipantNotFoundException : ChatGrainException
{
    /// <summary>
    /// Gets the participant ID that was not found.
    /// </summary>
    public string ParticipantId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ParticipantNotFoundException"/> class.
    /// </summary>
    public ParticipantNotFoundException(string chatId, string participantId)
        : base($"Participant '{participantId}' was not found in chat '{chatId}'.", chatId)
    {
        ParticipantId = participantId;
    }
}

/// <summary>
/// Exception thrown when a requested message is not found.
/// </summary>
public class MessageNotFoundException : ChatGrainException
{
    /// <summary>
    /// Gets the message ID that was not found.
    /// </summary>
    public string MessageId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageNotFoundException"/> class.
    /// </summary>
    public MessageNotFoundException(string chatId, string messageId)
        : base($"Message '{messageId}' was not found in chat '{chatId}'.", chatId)
    {
        MessageId = messageId;
    }
}

/// <summary>
/// Exception thrown when a requested stream is not found.
/// </summary>
public class StreamNotFoundException : ChatGrainException
{
    /// <summary>
    /// Gets the stream ID that was not found.
    /// </summary>
    public string StreamId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamNotFoundException"/> class.
    /// </summary>
    public StreamNotFoundException(string chatId, string streamId)
        : base($"Stream '{streamId}' was not found in chat '{chatId}'.", chatId)
    {
        StreamId = streamId;
    }
}

/// <summary>
/// Exception thrown when a participant lacks permission for an action.
/// </summary>
public class PermissionDeniedException : ChatGrainException
{
    /// <summary>
    /// Gets the participant ID that was denied permission.
    /// </summary>
    public string ParticipantId { get; }

    /// <summary>
    /// Gets the action that was denied.
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PermissionDeniedException"/> class.
    /// </summary>
    public PermissionDeniedException(string chatId, string participantId, string action)
        : base($"Participant '{participantId}' does not have permission to perform '{action}' in chat '{chatId}'.", chatId)
    {
        ParticipantId = participantId;
        Action = action;
    }
}

/// <summary>
/// Exception thrown when attempting to modify an archived chat.
/// </summary>
public class ChatArchivedException : ChatGrainException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatArchivedException"/> class.
    /// </summary>
    public ChatArchivedException(string chatId)
        : base($"Chat '{chatId}' is archived and cannot be modified.", chatId)
    {
    }
}

/// <summary>
/// Exception thrown when chat state is invalid or corrupted.
/// </summary>
public class InvalidChatStateException : ChatGrainException
{
    /// <summary>
    /// Gets the reason for the invalid state.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidChatStateException"/> class.
    /// </summary>
    public InvalidChatStateException(string chatId, string reason)
        : base($"Chat '{chatId}' is in an invalid state: {reason}", chatId)
    {
        Reason = reason;
    }
}