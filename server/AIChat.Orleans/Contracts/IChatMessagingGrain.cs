using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for chat message operations.
/// Handles message processing, delivery, and acknowledgment.
/// This interface follows the Interface Segregation Principle by focusing solely on messaging operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IChatMessagingGrain")]
public interface IChatMessagingGrain : IGrainWithStringKey
{
    /// <summary>
    /// Processes an incoming chat message.
    /// </summary>
    /// <param name="message">The chat message to process</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of the message processing operation</returns>
    /// <exception cref="ChatNotFoundException">Thrown when the chat does not exist</exception>
    /// <exception cref="ChatArchivedException">Thrown when attempting to send a message to an archived chat</exception>
    /// <exception cref="PermissionDeniedException">Thrown when the sender lacks permission to send messages</exception>
    [Alias("ProcessMessageAsync")]
    Task<MessageResult> ProcessMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a system message to the chat.
    /// </summary>
    /// <param name="content">System message content</param>
    /// <param name="metadata">Optional metadata for the system message</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The created system message</returns>
    /// <exception cref="ChatNotFoundException">Thrown when the chat does not exist</exception>
    [Alias("SendSystemMessageAsync")]
    Task<ChatMessage> SendSystemMessageAsync(string content, string? metadata = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Edits an existing message.
    /// </summary>
    /// <param name="messageId">ID of the message to edit</param>
    /// <param name="newContent">New content for the message</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The edited message</returns>
    /// <exception cref="MessageNotFoundException">Thrown when the message is not found</exception>
    /// <exception cref="ChatArchivedException">Thrown when attempting to edit a message in an archived chat</exception>
    /// <exception cref="PermissionDeniedException">Thrown when the user lacks permission to edit the message</exception>
    [Alias("EditMessageAsync")]
    Task<ChatMessage> EditMessageAsync(string messageId, string newContent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a message from the chat.
    /// </summary>
    /// <param name="messageId">ID of the message to delete</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the message was deleted, false if not found</returns>
    /// <exception cref="ChatArchivedException">Thrown when attempting to delete a message from an archived chat</exception>
    /// <exception cref="PermissionDeniedException">Thrown when the user lacks permission to delete the message</exception>
    [Alias("DeleteMessageAsync")]
    Task<bool> DeleteMessageAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges receipt of a message by a participant.
    /// </summary>
    /// <param name="messageId">ID of the message to acknowledge</param>
    /// <param name="participantId">ID of the participant acknowledging the message</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="MessageNotFoundException">Thrown when the message is not found</exception>
    /// <exception cref="ParticipantNotFoundException">Thrown when the participant is not found in the chat</exception>
    [Alias("AcknowledgeMessageAsync")]
    Task AcknowledgeMessageAsync(string messageId, string participantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets delivery status for a specific message.
    /// </summary>
    /// <param name="messageId">ID of the message to check</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Message delivery status information</returns>
    /// <exception cref="MessageNotFoundException">Thrown when the message is not found</exception>
    [Alias("GetMessageStatusAsync")]
    [ReadOnly]
    Task<MessageStatus> GetMessageStatusAsync(string messageId, CancellationToken cancellationToken = default);
}