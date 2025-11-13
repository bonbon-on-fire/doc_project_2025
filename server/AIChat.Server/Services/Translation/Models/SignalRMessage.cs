using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace AIChat.Server.Services.Translation.Models;

/// <summary>
/// Represents a SignalR operation message for protocol translation.
/// Wraps SignalR hub method calls into a structured format for translation.
/// </summary>
public sealed class SignalRMessage
{
    /// <summary>
    /// The SignalR hub method name being invoked.
    /// </summary>
    [Required]
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// Parameters passed to the SignalR hub method.
    /// Key is the parameter name, value is the parameter value.
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = [];

    /// <summary>
    /// SignalR connection identifier.
    /// </summary>
    [Required]
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// User identifier associated with the SignalR connection.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Timestamp when the SignalR message was created.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Additional metadata for the SignalR operation.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = [];

    /// <summary>
    /// Creates a SignalR message for a "SendMessage" operation.
    /// </summary>
    /// <param name="connectionId">SignalR connection ID</param>
    /// <param name="chatId">Chat ID where the message should be sent</param>
    /// <param name="userId">User ID sending the message</param>
    /// <param name="message">Message content</param>
    /// <returns>SignalR message for sending a chat message</returns>
    public static SignalRMessage CreateSendMessage(string connectionId, string chatId, string userId, string message)
    {
        return new SignalRMessage
        {
            Operation = "SendMessage",
            ConnectionId = connectionId,
            UserId = userId,
            Parameters = new Dictionary<string, object>
            {
                ["chatId"] = chatId,
                ["userId"] = userId,
                ["message"] = message
            },
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a SignalR message for a "JoinChatGroup" operation.
    /// </summary>
    /// <param name="connectionId">SignalR connection ID</param>
    /// <param name="chatId">Chat ID to join</param>
    /// <param name="userId">User ID joining the chat</param>
    /// <returns>SignalR message for joining a chat group</returns>
    public static SignalRMessage CreateJoinChatGroup(string connectionId, string chatId, string? userId = null)
    {
        return new SignalRMessage
        {
            Operation = "JoinChatGroup",
            ConnectionId = connectionId,
            UserId = userId,
            Parameters = new Dictionary<string, object>
            {
                ["chatId"] = chatId
            },
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a SignalR message for a "LeaveChatGroup" operation.
    /// </summary>
    /// <param name="connectionId">SignalR connection ID</param>
    /// <param name="chatId">Chat ID to leave</param>
    /// <param name="userId">User ID leaving the chat</param>
    /// <returns>SignalR message for leaving a chat group</returns>
    public static SignalRMessage CreateLeaveChatGroup(string connectionId, string chatId, string? userId = null)
    {
        return new SignalRMessage
        {
            Operation = "LeaveChatGroup",
            ConnectionId = connectionId,
            UserId = userId,
            Parameters = new Dictionary<string, object>
            {
                ["chatId"] = chatId
            },
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Gets a parameter value of the specified type.
    /// </summary>
    /// <typeparam name="T">Type of the parameter value</typeparam>
    /// <param name="parameterName">Name of the parameter</param>
    /// <returns>Parameter value or default if not found</returns>
    public T? GetParameter<T>(string parameterName)
    {
        if (Parameters.TryGetValue(parameterName, out var value))
        {
            if (value is T typedValue)
            {
                return typedValue;
            }

            // Try to convert if possible
            try
            {
                return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
            }
            catch
            {
                return default;
            }
        }

        return default;
    }

    /// <summary>
    /// Gets a parameter value as a string.
    /// </summary>
    /// <param name="parameterName">Name of the parameter</param>
    /// <returns>Parameter value as string or empty string if not found</returns>
    public string GetParameterAsString(string parameterName)
    {
        return GetParameter<string>(parameterName) ?? string.Empty;
    }

    /// <summary>
    /// Checks if a parameter exists.
    /// </summary>
    /// <param name="parameterName">Name of the parameter</param>
    /// <returns>True if the parameter exists, false otherwise</returns>
    public bool HasParameter(string parameterName)
    {
        return Parameters.ContainsKey(parameterName);
    }

    /// <summary>
    /// Validates that all required parameters are present for the operation.
    /// </summary>
    /// <returns>Validation result</returns>
    public ValidationResult Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(Operation))
        {
            errors.Add("Operation is required");
        }

        if (string.IsNullOrEmpty(ConnectionId))
        {
            errors.Add("ConnectionId is required");
        }

        // Validate operation-specific parameters
        switch (Operation.ToLowerInvariant())
        {
            case "sendmessage":
                ValidateSendMessageParameters(errors);
                break;
            case "joinchatgroup":
            case "leavechatgroup":
                ValidateChatGroupParameters(errors);
                break;
        }

        return new ValidationResult(errors.Count == 0, errors);
    }

    private void ValidateSendMessageParameters(List<string> errors)
    {
        if (!HasParameter("chatId") || string.IsNullOrEmpty(GetParameterAsString("chatId")))
        {
            errors.Add("chatId parameter is required for SendMessage operation");
        }

        if (!HasParameter("userId") || string.IsNullOrEmpty(GetParameterAsString("userId")))
        {
            errors.Add("userId parameter is required for SendMessage operation");
        }

        if (!HasParameter("message") || string.IsNullOrEmpty(GetParameterAsString("message")))
        {
            errors.Add("message parameter is required for SendMessage operation");
        }
    }

    private void ValidateChatGroupParameters(List<string> errors)
    {
        if (!HasParameter("chatId") || string.IsNullOrEmpty(GetParameterAsString("chatId")))
        {
            errors.Add($"chatId parameter is required for {Operation} operation");
        }
    }
}

/// <summary>
/// Represents a response to be sent back to SignalR clients.
/// </summary>
public sealed class SignalRResponse
{
    /// <summary>
    /// The SignalR client method to invoke.
    /// </summary>
    [Required]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Arguments to pass to the client method.
    /// </summary>
    public object[] Arguments { get; set; } = [];

    /// <summary>
    /// Target specification for the response (group, connection, user, etc.).
    /// </summary>
    public SignalRTarget Target { get; set; } = new();

    /// <summary>
    /// Timestamp when the response was created.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a response for broadcasting a message to a chat group.
    /// </summary>
    /// <param name="chatId">Chat ID to broadcast to</param>
    /// <param name="message">Message to broadcast</param>
    /// <returns>SignalR response for message broadcast</returns>
    public static SignalRResponse CreateMessageBroadcast(string chatId, object message)
    {
        return new SignalRResponse
        {
            Method = "MessageReceived",
            Arguments = [message],
            Target = SignalRTarget.Group($"chat_{chatId}"),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a response for sending a stream chunk to a specific connection.
    /// </summary>
    /// <param name="connectionId">Connection ID to send to</param>
    /// <param name="chunk">Stream chunk data</param>
    /// <returns>SignalR response for stream chunk</returns>
    public static SignalRResponse CreateStreamChunk(string connectionId, object chunk)
    {
        return new SignalRResponse
        {
            Method = "StreamChunkReceived",
            Arguments = [chunk],
            Target = SignalRTarget.Connection(connectionId),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates an error response for a specific connection.
    /// </summary>
    /// <param name="connectionId">Connection ID to send error to</param>
    /// <param name="error">Error information</param>
    /// <returns>SignalR response for error notification</returns>
    public static SignalRResponse CreateError(string connectionId, object error)
    {
        return new SignalRResponse
        {
            Method = "ErrorOccurred",
            Arguments = [error],
            Target = SignalRTarget.Connection(connectionId),
            Timestamp = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Specifies the target for a SignalR response.
/// </summary>
public sealed class SignalRTarget
{
    /// <summary>
    /// Type of target (All, Group, Connection, User).
    /// </summary>
    public SignalRTargetType Type { get; set; }

    /// <summary>
    /// Identifier for the target (group name, connection ID, user ID).
    /// </summary>
    public string? Identifier { get; set; }

    /// <summary>
    /// Creates a target for all connected clients.
    /// </summary>
    /// <returns>Target for all clients</returns>
    public static SignalRTarget All() => new() { Type = SignalRTargetType.All };

    /// <summary>
    /// Creates a target for a specific group.
    /// </summary>
    /// <param name="groupName">Group name</param>
    /// <returns>Target for the specified group</returns>
    public static SignalRTarget Group(string groupName) => new() { Type = SignalRTargetType.Group, Identifier = groupName };

    /// <summary>
    /// Creates a target for a specific connection.
    /// </summary>
    /// <param name="connectionId">Connection ID</param>
    /// <returns>Target for the specified connection</returns>
    public static SignalRTarget Connection(string connectionId) => new() { Type = SignalRTargetType.Connection, Identifier = connectionId };

    /// <summary>
    /// Creates a target for a specific user.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <returns>Target for the specified user</returns>
    public static SignalRTarget User(string userId) => new() { Type = SignalRTargetType.User, Identifier = userId };
}

/// <summary>
/// Enumeration of SignalR target types.
/// </summary>
public enum SignalRTargetType
{
    /// <summary>
    /// Target all connected clients.
    /// </summary>
    All = 0,

    /// <summary>
    /// Target a specific group.
    /// </summary>
    Group = 1,

    /// <summary>
    /// Target a specific connection.
    /// </summary>
    Connection = 2,

    /// <summary>
    /// Target all connections for a specific user.
    /// </summary>
    User = 3
}

/// <summary>
/// Result of validating a SignalR message.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// Whether the validation passed.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Validation error messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Initializes a new validation result.
    /// </summary>
    /// <param name="isValid">Whether validation passed</param>
    /// <param name="errors">Validation errors</param>
    public ValidationResult(bool isValid, IEnumerable<string> errors)
    {
        IsValid = isValid;
        Errors = errors.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets the first error message or empty string if valid.
    /// </summary>
    public string FirstError => Errors.Count > 0 ? Errors[0] : string.Empty;

    /// <summary>
    /// Gets all error messages joined with a separator.
    /// </summary>
    /// <param name="separator">Separator to use between errors</param>
    /// <returns>Combined error messages</returns>
    public string GetErrorsString(string separator = "; ") => string.Join(separator, Errors);
}
