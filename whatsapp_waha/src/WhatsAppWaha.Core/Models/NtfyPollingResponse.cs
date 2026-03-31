using System.Text.Json.Serialization;

namespace WhatsAppWaha.Core.Models;

/// <summary>
/// Represents the response from ntfy when polling for messages (batch retrieval).
/// Note: ntfy API returns an array of messages when polling, not a wrapper object.
/// This class is used for structured handling of the polling operation.
/// </summary>
public sealed class NtfyPollingResponse
{
    /// <summary>
    /// The messages retrieved from the polling request.
    /// </summary>
    public IReadOnlyList<NtfyMessage> Messages { get; init; } = Array.Empty<NtfyMessage>();

    /// <summary>
    /// The topic that was polled.
    /// </summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary>
    /// The timestamp when the polling request was made.
    /// </summary>
    public DateTime PolledAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// The number of messages that were already processed (duplicates).
    /// </summary>
    public int DuplicateCount { get; init; }

    /// <summary>
    /// The number of new messages that haven't been processed before.
    /// </summary>
    public int NewMessageCount { get; init; }

    /// <summary>
    /// Indicates whether the polling was successful.
    /// </summary>
    public bool IsSuccess { get; init; } = true;

    /// <summary>
    /// Error message if the polling failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets only the valid, new messages (excludes keepalives and duplicates).
    /// </summary>
    [JsonIgnore]
    public IEnumerable<NtfyMessage> ValidNewMessages => Messages
        .Where(m => m.IsValidMessage);

    /// <summary>
    /// Creates a successful polling response.
    /// </summary>
    /// <param name="messages">The retrieved messages.</param>
    /// <param name="topic">The topic that was polled.</param>
    /// <param name="duplicateCount">The number of duplicate messages.</param>
    /// <returns>A successful polling response.</returns>
    public static NtfyPollingResponse CreateSuccess(
        IReadOnlyList<NtfyMessage> messages, 
        string topic, 
        int duplicateCount = 0)
    {
        var validMessages = messages.Where(m => m.IsValidMessage).ToList();
        
        return new NtfyPollingResponse
        {
            Messages = messages,
            Topic = topic,
            DuplicateCount = duplicateCount,
            NewMessageCount = validMessages.Count - duplicateCount,
            IsSuccess = true,
            PolledAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a failed polling response.
    /// </summary>
    /// <param name="topic">The topic that was polled.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>A failed polling response.</returns>
    public static NtfyPollingResponse CreateFailure(string topic, string errorMessage)
    {
        return new NtfyPollingResponse
        {
            Topic = topic,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            PolledAt = DateTime.UtcNow
        };
    }
}