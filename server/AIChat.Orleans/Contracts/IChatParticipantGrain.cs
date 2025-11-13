using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for chat participant management.
/// Handles participant join/leave, roles, and presence tracking.
/// This interface follows the Interface Segregation Principle by focusing solely on participant operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IChatParticipantGrain")]
public interface IChatParticipantGrain : IGrainWithStringKey
{
    /// <summary>
    /// Adds a participant to the chat.
    /// </summary>
    /// <param name="participant">Participant information to add</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="ChatNotFoundException">Thrown when the chat does not exist</exception>
    /// <exception cref="ChatArchivedException">Thrown when attempting to add a participant to an archived chat</exception>
    /// <exception cref="InvalidChatStateException">Thrown when maximum participants limit is reached</exception>
    [Alias("AddParticipantAsync")]
    Task AddParticipantAsync(ChatParticipant participant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a participant from the chat.
    /// </summary>
    /// <param name="participantId">ID of the participant to remove</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the participant was removed, false if not found</returns>
    /// <exception cref="ChatArchivedException">Thrown when attempting to remove a participant from an archived chat</exception>
    [Alias("RemoveParticipantAsync")]
    Task<bool> RemoveParticipantAsync(string participantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a participant's information.
    /// </summary>
    /// <param name="update">Participant update information</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The updated participant information</returns>
    /// <exception cref="ParticipantNotFoundException">Thrown when the participant is not found</exception>
    /// <exception cref="ChatArchivedException">Thrown when attempting to update a participant in an archived chat</exception>
    [Alias("UpdateParticipantAsync")]
    Task<ChatParticipant> UpdateParticipantAsync(ParticipantUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a specific participant.
    /// </summary>
    /// <param name="participantId">ID of the participant to query</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Participant information or null if not found</returns>
    [Alias("GetParticipantAsync")]
    [ReadOnly]
    Task<ChatParticipant?> GetParticipantAsync(string participantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all participants in the chat.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of all chat participants</returns>
    [Alias("GetParticipantsAsync")]
    [ReadOnly]
    Task<List<ChatParticipant>> GetParticipantsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a participant's presence status.
    /// </summary>
    /// <param name="participantId">ID of the participant</param>
    /// <param name="status">New presence status</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="ParticipantNotFoundException">Thrown when the participant is not found</exception>
    [Alias("UpdatePresenceAsync")]
    Task UpdatePresenceAsync(string participantId, PresenceStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies all participants of an event.
    /// </summary>
    /// <param name="eventType">Type of event to notify</param>
    /// <param name="eventData">Event data to send</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    [Alias("NotifyParticipantsAsync")]
    Task NotifyParticipantsAsync(string eventType, object eventData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user has permission for a specific action.
    /// </summary>
    /// <param name="participantId">ID of the participant</param>
    /// <param name="action">Action to check permission for</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the participant has permission, false otherwise</returns>
    /// <exception cref="ParticipantNotFoundException">Thrown when the participant is not found</exception>
    [Alias("CheckPermissionAsync")]
    [ReadOnly]
    Task<bool> CheckPermissionAsync(string participantId, ChatAction action, CancellationToken cancellationToken = default);
}
