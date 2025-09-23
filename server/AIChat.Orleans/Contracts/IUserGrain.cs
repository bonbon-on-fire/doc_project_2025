using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Comprehensive grain interface representing a user in the chat system.
/// This interface extends all segregated interfaces to provide backward compatibility
/// while supporting the new Interface Segregation Principle-based architecture.
///
/// For new code, prefer using the specific segregated interfaces:
/// - IUserActivityGrain for activity tracking and monitoring
/// - IUserConnectionGrain for connection and subscription management
/// - IUserOperationGrain for background operations and message routing
/// - IUserMessageBufferGrain for message buffering functionality
/// - IUserSessionGrain for session state management and migration (Phase 2)
/// - IUserPreferencesGrain for user preferences storage and management (Phase 2)
/// </summary>
[Alias("AIChat.Orleans.Contracts.IUserGrain")]
public interface IUserGrain
    : IUserActivityGrain,
        IUserConnectionGrain,
        IUserOperationGrain,
        IUserMessageBufferGrain,
        IUserSessionGrain,
        IUserPreferencesGrain
{
    // This interface now inherits all methods from the six segregated interfaces.
    // No additional methods are defined here to maintain clean separation of concerns.
    //
    // Inherited from IUserActivityGrain:
    // - RecordActivity(ActivityType type, string metadata)
    // - GetState()
    // - CheckHealth()
    //
    // Inherited from IUserConnectionGrain:
    // - RegisterConnection(string connectionId, string clientId)
    // - UnregisterConnection(string connectionId)
    // - SubscribeToChat(string connectionId, string chatId)
    // - UnsubscribeFromChat(string connectionId, string chatId)
    //
    // Inherited from IUserOperationGrain:
    // - ProcessMessageWithBackground(ChatMessage message)
    // - NotifyOperationStarted(string operationId, string chatId)
    // - NotifyOperationCompleted(string operationId, bool success, string? error)
    // - CancelOperation(string operationId)
    // - GetOperationStatus(string operationId)
    // - RelayMessage(ChatMessage message)
    // - RelayStreamChunk(StreamChunk chunk)
    //
    // Inherited from IUserMessageBufferGrain:
    // - BufferMessageAsync(ChatMessage message, BufferPriority priority)
    // - BufferStreamChunkAsync(StreamChunk chunk, BufferPriority priority)
    // - GetBufferedMessagesAsync(string chatId, int? limit, bool highPriorityOnly)
    // - RemoveBufferedMessageAsync(string messageId)
    // - ClearExpiredBufferedMessagesAsync()
    // - GetChatBufferAsync(string chatId)
    // - GetBufferSummaryAsync()
    // - MarkMessageDeliveredAsync(string messageId)
    // - RecordDeliveryAttemptAsync(string messageId, string error)
    // - ProcessBufferedMessagesAsync(string connectionId, string? chatId, int maxMessages)
    //
    // Inherited from IUserSessionGrain:
    // - CreateSessionAsync(UserSessionState session, CancellationToken)
    // - UpdateSessionAsync(string sessionId, UserSessionState session, CancellationToken)
    // - UpdateSessionStateAsync(string sessionId, SessionLifecycleState newState, CancellationToken)
    // - UpdateSessionActivityAsync(string sessionId, DateTime? activityTime, CancellationToken)
    // - RecordSessionConnectionAsync(string sessionId, CancellationToken)
    // - RecordSessionDisconnectionAsync(string sessionId, string? reason, CancellationToken)
    // - RecordSessionReconnectionAttemptAsync(string sessionId, bool success, CancellationToken)
    // - ArchiveSessionAsync(string sessionId, string? reason, CancellationToken)
    // - GetSessionMetricsAsync(string sessionId, CancellationToken)
    // - UpdateSessionMetricsAsync(string sessionId, UserSessionMetrics metrics, CancellationToken)
    // - BulkImportSessionsAsync(List<UserSessionState> sessions, CancellationToken)
    //
    // Inherited from IUserPreferencesGrain:
    // - GetPreferencesAsync(CancellationToken)
    // - UpdatePreferencesAsync(UserPreferencesState preferences, CancellationToken)
    // - UpdateMessagePreferenceAsync(string messageId, bool isExpanded, string renderPhase, CancellationToken)
    // - GetMessagePreferenceAsync(string messageId, CancellationToken)
    // - BulkUpdateMessagePreferencesAsync(Dictionary<string, MessagePreference> preferences, CancellationToken)
    // - ArchiveOldMessagePreferencesAsync(DateTime olderThan, CancellationToken)
    // - UpdateSelectedModeAsync(string? modeId, CancellationToken)
    // - GetSelectedModeAsync(CancellationToken)
    // - UpdateUIPreferenceAsync(string key, object value, CancellationToken)
    // - GetUIPreferenceAsync<T>(string key, CancellationToken)
    // - RemoveUIPreferenceAsync(string key, CancellationToken)
    // - ImportClientPreferencesAsync(ClientPreferencesImport import, CancellationToken)
    // - ExportPreferencesAsync(CancellationToken)
    // - ResetPreferencesAsync(CancellationToken)
    // - InvalidatePreferencesCacheAsync(CancellationToken)
}
