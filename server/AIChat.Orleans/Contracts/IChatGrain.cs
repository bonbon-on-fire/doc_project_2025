using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Comprehensive grain interface representing a chat session in the system.
/// This interface extends all segregated interfaces to provide backward compatibility
/// while supporting the new Interface Segregation Principle-based architecture.
///
/// For new code, prefer using the specific segregated interfaces:
/// - IChatStateGrain for state management and persistence
/// - IChatMessagingGrain for message processing and delivery
/// - IChatStreamingGrain for real-time streaming operations
/// - IChatParticipantGrain for participant management and permissions
/// </summary>
[Alias("AIChat.Orleans.Contracts.IChatGrain")]
public interface IChatGrain
    : IChatStateGrain,
      IChatMessagingGrain,
      IChatStreamingGrain,
      IChatParticipantGrain
{
    // This interface now inherits all methods from the four segregated interfaces.
    // No additional methods are defined here to maintain clean separation of concerns.
    //
    // Inherited from IChatStateGrain:
    // - InitializeAsync(ChatInitRequest request)
    // - GetStateAsync()
    // - UpdateMetadataAsync(string metadata)
    // - ArchiveAsync()
    // - GetHistoryAsync(int? limit, string? beforeMessageId)
    // - CheckHealthAsync()
    //
    // Inherited from IChatMessagingGrain:
    // - ProcessMessageAsync(ChatMessage message)
    // - SendSystemMessageAsync(string content, string? metadata)
    // - EditMessageAsync(string messageId, string newContent)
    // - DeleteMessageAsync(string messageId)
    // - AcknowledgeMessageAsync(string messageId, string participantId)
    // - GetMessageStatusAsync(string messageId)
    //
    // Inherited from IChatStreamingGrain:
    // - StartStreamAsync(StreamMessage message)
    // - ProcessStreamChunkAsync(StreamChunk chunk)
    // - CompleteStreamAsync(string streamId, string? finalContent)
    // - CancelStreamAsync(string streamId, string? reason)
    // - GetStreamStateAsync(string streamId)
    // - GetActiveStreamsAsync()
    // - SubscribeToStreamAsync(Guid streamId)
    //
    // Inherited from IChatParticipantGrain:
    // - AddParticipantAsync(ChatParticipant participant)
    // - RemoveParticipantAsync(string participantId)
    // - UpdateParticipantAsync(ParticipantUpdate update)
    // - GetParticipantAsync(string participantId)
    // - GetParticipantsAsync()
    // - UpdatePresenceAsync(string participantId, PresenceStatus status)
    // - NotifyParticipantsAsync(string eventType, object eventData)
    // - CheckPermissionAsync(string participantId, ChatAction action)
}
