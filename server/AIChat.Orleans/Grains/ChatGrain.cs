using System.Text.Json;
using AIChat.Orleans.Configuration;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Metrics;
using AIChat.Orleans.Services;
using AIChat.Orleans.Tracing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;

namespace AIChat.Orleans.Grains;

/// <summary>
/// ChatGrain implementation providing comprehensive chat functionality.
/// Implements all chat-related operations including state management, messaging, streaming, and participant management.
/// Uses Orleans native state persistence for optimal performance and consistency.
/// </summary>
public sealed class ChatGrain : Grain<ChatGrainState>, IChatGrain, IDisposable
{
    private readonly ILogger<ChatGrain> _logger;
    private readonly OrleansGrainConfiguration _configuration;
    private readonly IOrleansMetricsCollector _metricsCollector;
    private readonly ISignalRBroadcastService _signalRBroadcast;

    private IGrainTimer? _cleanupTimer;
    private IGrainTimer? _metricsTimer;
    private bool _disposed;
    private readonly object _stateLock = new();

    /// <summary>
    /// Initializes a new instance of the ChatGrain.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics</param>
    /// <param name="configuration">Configuration for Orleans grains</param>
    /// <param name="metricsCollector">Metrics collector for performance tracking</param>
    /// <param name="signalRBroadcast">SignalR broadcast service for real-time messaging (optional)</param>
    /// <param name="chatServiceProxy">Chat service proxy for LLM processing (optional)</param>
    public ChatGrain(
        ILogger<ChatGrain> logger,
        IOptionsSnapshot<OrleansGrainConfiguration> configuration,
        IOrleansMetricsCollector metricsCollector,
        ISignalRBroadcastService? signalRBroadcast = null,
        IChatServiceProxy? chatServiceProxy = null
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value ?? new OrleansGrainConfiguration();
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _signalRBroadcast = signalRBroadcast ?? new NullSignalRBroadcastService();

        // Use default proxy if none provided - allows for testing and gradual rollout
        if (chatServiceProxy == null)
        {
            var proxyLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultChatServiceProxy>();
        }
        else
        {
        }
    }

    /// <inheritdoc />
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var activationStart = DateTime.UtcNow;
        await base.OnActivateAsync(cancellationToken);

        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(OnActivateAsync), this.GetPrimaryKeyString());

        try
        {
            // Initialize grain state and setup
            await InitializeGrainStateAsync();

            // Record activation metrics
            var activationTime = await RecordActivationMetricsAsync(activationStart);

            // Setup periodic timers if enabled
            if (_configuration.UserGrain.EnablePeriodicTimers)
            {
                await SetupPeriodicTimersAsync();
            }

            // Process any pending operations from previous activation
            await ProcessPendingOperationsAsync(cancellationToken);

            // Persist state changes
            await WriteStateAsync();

            // Log successful activation
            LogSuccessfulActivation();

            OrleansActivitySource.SetSuccess(activity, new Dictionary<string, object>
            {
                ["ChatId"] = State.ChatMetadata.ChatId,
                ["ActivationCount"] = State.Metrics.ActivationCount,
                ["ActivationTimeMs"] = activationTime
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ChatGrain {ChatId} activation", State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(OnDeactivateAsync), this.GetPrimaryKeyString());

        try
        {
            _logger.LogInformation(
                "ChatGrain {ChatId} deactivating (Reason: {Reason}, Version {StateVersion})",
                State.ChatMetadata.ChatId,
                reason,
                State.StateVersion
            );

            // Clean up active streams
            await CleanupActiveStreamsAsync(cancellationToken);

            // Dispose timers
            _cleanupTimer?.Dispose();
            _metricsTimer?.Dispose();

            // Final state persistence
            await WriteStateAsync();

            // Record deactivation metrics
            await _metricsCollector.RecordGrainDeactivationAsync(
                "ChatGrain",
                State.ChatMetadata.ChatId,
                0.0 // Placeholder for lifetime in minutes
            );

            OrleansActivitySource.SetSuccess(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ChatGrain {ChatId} deactivation", State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
        }

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    #region IChatStateGrain Implementation

    /// <inheritdoc />
    public async Task<ChatState> InitializeAsync(ChatInitRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(InitializeAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentNullException.ThrowIfNull(request);

            lock (_stateLock)
            {
                if (State.ChatMetadata.Status != ChatStatus.Initializing && !string.IsNullOrEmpty(State.ChatMetadata.Title))
                {
                    throw new ChatAlreadyExistsException(request.ChatId);
                }
            }

            // Basic validation
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                throw new ArgumentException("Chat title cannot be empty", nameof(request));
            }

            lock (_stateLock)
            {
                // Initialize chat metadata
                State.ChatMetadata = new ChatState
                {
                    ChatId = request.ChatId,
                    Title = request.Title,
                    Status = ChatStatus.Active,
                    ChatType = request.ChatType,
                    CreatedBy = request.CreatedBy,
                    CreatedAt = request.CreatedAt,
                    LastActivityAt = DateTime.UtcNow,
                    ModeId = request.ModeId,
                    SystemPrompt = request.SystemPrompt,
                    Metadata = request.Metadata,
                    Version = 1
                };

                // Apply configuration overrides
                if (request.MaxParticipants.HasValue)
                {
                    State.Configuration.MaxParticipants = request.MaxParticipants;
                }

                // Add initial participants
                foreach (var participant in request.InitialParticipants)
                {
                    State.Participants[participant.ParticipantId] = participant;
                }

                State.ChatMetadata.ParticipantCount = State.Participants.Count;
                State.IncrementVersion();
            }

            await WriteStateAsync();

            // Record initialization metrics
            State.Metrics.TotalActivities++;
            await _metricsCollector.RecordGrainOperationAsync(
                "ChatGrain",
                "InitializeChat",
                (DateTime.UtcNow - DateTime.UtcNow).TotalMilliseconds,
                true
            );

            _logger.LogInformation(
                "Initialized chat {ChatId} '{Title}' with {ParticipantCount} participants",
                State.ChatMetadata.ChatId,
                State.ChatMetadata.Title,
                State.Participants.Count
            );

            OrleansActivitySource.SetSuccess(activity);
            return new ChatState
            {
                ChatId = State.ChatMetadata.ChatId,
                Title = State.ChatMetadata.Title,
                Status = State.ChatMetadata.Status,
                ChatType = State.ChatMetadata.ChatType,
                CreatedBy = State.ChatMetadata.CreatedBy,
                CreatedAt = State.ChatMetadata.CreatedAt,
                LastActivityAt = State.ChatMetadata.LastActivityAt,
                ModeId = State.ChatMetadata.ModeId,
                SystemPrompt = State.ChatMetadata.SystemPrompt,
                Metadata = State.ChatMetadata.Metadata,
                Version = State.ChatMetadata.Version,
                MessageCount = State.ChatMetadata.MessageCount,
                ParticipantCount = State.ChatMetadata.ParticipantCount,
                ArchivedAt = State.ChatMetadata.ArchivedAt
            }; // Return a copy to prevent external modification
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize chat {ChatId}", request?.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ChatState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(GetStateAsync), this.GetPrimaryKeyString());

        try
        {
            await ValidateInitialized();

            // Update last activity
            lock (_stateLock)
            {
                State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                State.IncrementVersion();
            }

            await WriteStateAsync();

            OrleansActivitySource.SetSuccess(activity);
            return new ChatState
            {
                ChatId = State.ChatMetadata.ChatId,
                Title = State.ChatMetadata.Title,
                Status = State.ChatMetadata.Status,
                ChatType = State.ChatMetadata.ChatType,
                CreatedBy = State.ChatMetadata.CreatedBy,
                CreatedAt = State.ChatMetadata.CreatedAt,
                LastActivityAt = State.ChatMetadata.LastActivityAt,
                ModeId = State.ChatMetadata.ModeId,
                SystemPrompt = State.ChatMetadata.SystemPrompt,
                Metadata = State.ChatMetadata.Metadata,
                Version = State.ChatMetadata.Version,
                MessageCount = State.ChatMetadata.MessageCount,
                ParticipantCount = State.ChatMetadata.ParticipantCount,
                ArchivedAt = State.ChatMetadata.ArchivedAt
            }; // Return a copy to prevent external modification
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get state for chat {ChatId}", State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ChatState> UpdateMetadataAsync(string metadata, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(UpdateMetadataAsync), this.GetPrimaryKeyString());

        try
        {
            await ValidateInitialized();
            await ValidateCanModify();

            // Validate metadata if provided
            if (!string.IsNullOrEmpty(metadata))
            {
                try
                {
                    JsonDocument.Parse(metadata); // Validate JSON format
                }
                catch (JsonException ex)
                {
                    throw new InvalidChatStateException(State.ChatMetadata.ChatId, $"Invalid JSON metadata: {ex.Message}");
                }
            }

            lock (_stateLock)
            {
                State.ChatMetadata.Metadata = metadata;
                State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                State.ChatMetadata.Version++;
                State.IncrementVersion();
            }

            await WriteStateAsync();

            _logger.LogInformation("Updated metadata for chat {ChatId}", State.ChatMetadata.ChatId);

            OrleansActivitySource.SetSuccess(activity);
            return new ChatState
            {
                ChatId = State.ChatMetadata.ChatId,
                Title = State.ChatMetadata.Title,
                Status = State.ChatMetadata.Status,
                ChatType = State.ChatMetadata.ChatType,
                CreatedBy = State.ChatMetadata.CreatedBy,
                CreatedAt = State.ChatMetadata.CreatedAt,
                LastActivityAt = State.ChatMetadata.LastActivityAt,
                ModeId = State.ChatMetadata.ModeId,
                SystemPrompt = State.ChatMetadata.SystemPrompt,
                Metadata = State.ChatMetadata.Metadata,
                Version = State.ChatMetadata.Version,
                MessageCount = State.ChatMetadata.MessageCount,
                ParticipantCount = State.ChatMetadata.ParticipantCount,
                ArchivedAt = State.ChatMetadata.ArchivedAt
            }; // Return a copy
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update metadata for chat {ChatId}", State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ArchiveAsync(CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(ArchiveAsync), this.GetPrimaryKeyString());

        try
        {
            await ValidateInitialized();

            if (State.ChatMetadata.Status == ChatStatus.Archived)
            {
                throw new ChatArchivedException(State.ChatMetadata.ChatId);
            }

            lock (_stateLock)
            {
                State.ChatMetadata.Status = ChatStatus.Archived;
                State.ChatMetadata.ArchivedAt = DateTime.UtcNow;
                State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                State.ChatMetadata.Version++;
                State.IncrementVersion();
            }

            // Clean up active streams
            await CleanupActiveStreamsAsync(cancellationToken);

            await WriteStateAsync();

            _logger.LogInformation("Archived chat {ChatId}", State.ChatMetadata.ChatId);

            // Notify participants
            await NotifyParticipantsAsync("ChatArchived", new { State.ChatMetadata.ChatId }, cancellationToken);

            OrleansActivitySource.SetSuccess(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive chat {ChatId}", State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<ChatMessage>> GetHistoryAsync(int? limit = null, string? beforeMessageId = null, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(GetHistoryAsync), this.GetPrimaryKeyString());

        try
        {
            await ValidateInitialized();

            var messages = new List<ChatMessage>();

            // Get recent messages from Orleans state
            lock (_stateLock)
            {
                var recentMessages = State.RecentMessages.ToArray();

                if (!string.IsNullOrEmpty(beforeMessageId))
                {
                    // Find the position of beforeMessageId and take messages before it
                    var beforeIndex = Array.FindIndex(recentMessages, m => m.Id == beforeMessageId);
                    if (beforeIndex > 0)
                    {
                        messages.AddRange(recentMessages.Take(beforeIndex));
                    }
                }
                else
                {
                    messages.AddRange(recentMessages);
                }
            }

            // Apply limit if specified
            if (limit.HasValue && messages.Count > limit.Value)
            {
                messages = [.. messages.Take(limit.Value)];
            }

            // Update last activity
            lock (_stateLock)
            {
                State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                State.IncrementVersion();
            }

            await WriteStateAsync();

            OrleansActivitySource.SetSuccess(activity);
            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get history for chat {ChatId}", State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(CheckHealthAsync), this.GetPrimaryKeyString());

        try
        {
            var warnings = new List<string>();
            bool isHealthy = true;

            // Check grain state consistency
            if (State.StateVersion <= 0)
            {
                warnings.Add("Invalid state version");
                isHealthy = false;
            }

            if (State.Participants.Count != State.ChatMetadata.ParticipantCount)
            {
                warnings.Add($"Participant count mismatch: expected {State.ChatMetadata.ParticipantCount}, actual {State.Participants.Count}");
                isHealthy = false;
            }

            // Check for expired streams
            var expiredStreams = State.ActiveStreams.Values
                .Count(s => s.Status == StreamStatus.Active &&
                           DateTime.UtcNow - s.LastActivity > TimeSpan.FromSeconds(State.Configuration.StreamTimeoutSeconds));

            if (expiredStreams > 0)
            {
                warnings.Add($"{expiredStreams} expired streams detected");
            }

            var result = new HealthCheckResult
            {
                IsHealthy = isHealthy,
                GrainId = State.ChatMetadata.ChatId,
                LastActivity = State.ChatMetadata.LastActivityAt,
                Metrics = State.Metrics,
                CheckedAt = DateTime.UtcNow,
                AdditionalInfo = isHealthy ? "All checks passed" : "Issues detected",
                Warnings = warnings
            };

            OrleansActivitySource.SetSuccess(activity);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for chat {ChatId}", State.ChatMetadata.ChatId);

            var result = new HealthCheckResult
            {
                IsHealthy = false,
                GrainId = State.ChatMetadata.ChatId,
                LastActivity = State.ChatMetadata.LastActivityAt,
                CheckedAt = DateTime.UtcNow,
                AdditionalInfo = $"Health check exception: {ex.Message}",
                Warnings = ["Health check failed with exception"]
            };

            OrleansActivitySource.SetError(activity, ex);
            return Task.FromResult(result);
        }
    }

    #endregion

    #region IChatMessagingGrain Implementation

    /// <inheritdoc />
    public async Task<MessageResult> ProcessMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(ProcessMessageAsync), this.GetPrimaryKeyString());

        try
        {
            // Pipeline step 1: Validate input and state
            await ValidateMessageInputAsync(message);

            // Pipeline step 2: Prepare message outside of lock
            var preparedMessage = await PrepareMessageForStateAsync(message);

            // Pipeline step 3: Apply state updates with minimal lock time
            await UpdateChatStateWithMessageAsync(preparedMessage);

            // Pipeline step 4: Persist state changes
            await WriteStateAsync();

            // Pipeline step 5: Record metrics and broadcast
            await RecordMessageMetricsAsync();
            await BroadcastMessageAsync(preparedMessage);

            LogMessageProcessed(preparedMessage);

            OrleansActivitySource.SetSuccess(activity);
            return MessageResult.CreateSuccess(preparedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message {MessageId} for chat {ChatId}", message?.Id, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            return MessageResult.CreateFailure($"Message processing failed: {ex.Message}", "PROCESSING_ERROR");
        }
    }

    /// <inheritdoc />
    public async Task<ChatMessage> SendSystemMessageAsync(string content, string? metadata = null, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(SendSystemMessageAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(content);
            await ValidateInitialized();
            await ValidateCanModify();

            var systemMessage = new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = State.ChatMetadata.ChatId,
                UserId = "system",
                Content = content,
                Role = "system",
                Timestamp = DateTime.UtcNow,
                Metadata = metadata
            };

            var result = await ProcessMessageAsync(systemMessage, cancellationToken);

            if (!result.Success)
            {
                throw new InvalidOperationException($"Failed to send system message: {result.ErrorMessage}");
            }

            OrleansActivitySource.SetSuccess(activity);
            return result.Message!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send system message for chat {ChatId}", State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ChatMessage> EditMessageAsync(string messageId, string newContent, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(EditMessageAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
            ArgumentException.ThrowIfNullOrWhiteSpace(newContent);
            await ValidateInitialized();
            await ValidateCanModify();

            ChatMessage? messageToEdit = null;

            // Look in recent messages buffer
            lock (_stateLock)
            {
                messageToEdit = State.RecentMessages.FirstOrDefault(m => m.Id == messageId);
            }

            if (messageToEdit == null)
            {
                throw new MessageNotFoundException(State.ChatMetadata.ChatId, messageId);
            }

            // Create edited message
            var editedMessage = new ChatMessage
            {
                Id = messageToEdit.Id,
                ChatId = messageToEdit.ChatId,
                UserId = messageToEdit.UserId,
                Content = newContent,
                Role = messageToEdit.Role,
                Timestamp = DateTime.UtcNow,
                IsStreaming = messageToEdit.IsStreaming,
                Metadata = UpdateMessageMetadata(messageToEdit.Metadata, "edited", true)
            };

            // Update in recent messages
            lock (_stateLock)
            {
                var recentMessages = State.RecentMessages.ToArray();
                var index = Array.FindIndex(recentMessages, m => m.Id == messageId);
                if (index >= 0)
                {
                    // Rebuild queue with updated message
                    State.RecentMessages.Clear();
                    for (int i = 0; i < recentMessages.Length; i++)
                    {
                        State.RecentMessages.Enqueue(i == index ? editedMessage : recentMessages[i]);
                    }
                }

                State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                State.ChatMetadata.Version++;
                State.IncrementVersion();
            }

            await WriteStateAsync();

            // Broadcast edit notification
            await _signalRBroadcast.BroadcastToGroupAsync($"chat_{State.ChatMetadata.ChatId}", "MessageEdited", new
            {
                MessageId = messageId,
                NewContent = newContent,
                EditedAt = editedMessage.Timestamp
            });

            _logger.LogInformation("Edited message {MessageId} in chat {ChatId}", messageId, State.ChatMetadata.ChatId);

            OrleansActivitySource.SetSuccess(activity);
            return editedMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to edit message {MessageId} in chat {ChatId}", messageId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(DeleteMessageAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
            await ValidateInitialized();
            await ValidateCanModify();

            bool messageFound = false;

            // Remove from recent messages buffer
            lock (_stateLock)
            {
                var recentMessages = State.RecentMessages.Where(m => m.Id != messageId).ToArray();
                if (recentMessages.Length != State.RecentMessages.Count)
                {
                    messageFound = true;
                    State.RecentMessages.Clear();
                    foreach (var message in recentMessages)
                    {
                        State.RecentMessages.Enqueue(message);
                    }
                }

                // Remove delivery status
                if (State.MessageDeliveryStatus.Remove(messageId))
                {
                    messageFound = true;
                }

                if (messageFound)
                {
                    State.ChatMetadata.MessageCount = Math.Max(0, State.ChatMetadata.MessageCount - 1);
                    State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                    State.ChatMetadata.Version++;
                    State.IncrementVersion();
                }
            }

            if (messageFound)
            {
                await WriteStateAsync();

                // Broadcast deletion notification
                await _signalRBroadcast.BroadcastToGroupAsync($"chat_{State.ChatMetadata.ChatId}", "MessageDeleted", new
                {
                    MessageId = messageId,
                    DeletedAt = DateTime.UtcNow
                });

                _logger.LogInformation("Deleted message {MessageId} from chat {ChatId}", messageId, State.ChatMetadata.ChatId);
            }

            OrleansActivitySource.SetSuccess(activity);
            return messageFound;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete message {MessageId} from chat {ChatId}", messageId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task AcknowledgeMessageAsync(string messageId, string participantId, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(AcknowledgeMessageAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
            ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
            await ValidateInitialized();

            // Verify participant exists
            lock (_stateLock)
            {
                if (!State.Participants.ContainsKey(participantId))
                {
                    throw new ParticipantNotFoundException(State.ChatMetadata.ChatId, participantId);
                }
            }

            MessageStatus? status = null;
            bool statusChanged = false;

            lock (_stateLock)
            {
                if (State.MessageDeliveryStatus.TryGetValue(messageId, out status))
                {
                    if (!status.AcknowledgedBy.Contains(participantId))
                    {
                        status.AcknowledgedBy.Add(participantId);
                        status.PendingDelivery.Remove(participantId);
                        statusChanged = true;

                        // Check if all participants have acknowledged
                        if (status.PendingDelivery.Count == 0 && status.Status != DeliveryStatus.Read)
                        {
                            status.Status = DeliveryStatus.Read;
                            status.FullyDeliveredAt = DateTime.UtcNow;
                        }
                    }
                }
            }

            if (status == null)
            {
                throw new MessageNotFoundException(State.ChatMetadata.ChatId, messageId);
            }

            if (statusChanged)
            {
                State.IncrementVersion();
                await WriteStateAsync();

                // Update participant's last activity
                lock (_stateLock)
                {
                    if (State.Participants.TryGetValue(participantId, out var participant))
                    {
                        participant.LastActivityAt = DateTime.UtcNow;
                    }
                }

                _logger.LogDebug("Message {MessageId} acknowledged by participant {ParticipantId} in chat {ChatId}",
                    messageId, participantId, State.ChatMetadata.ChatId);

                // Broadcast acknowledgment if fully delivered
                if (status.Status == DeliveryStatus.Read)
                {
                    await _signalRBroadcast.BroadcastToGroupAsync($"chat_{State.ChatMetadata.ChatId}", "MessageFullyDelivered", new
                    {
                        MessageId = messageId,
                        DeliveredAt = status.FullyDeliveredAt
                    });
                }
            }

            OrleansActivitySource.SetSuccess(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acknowledge message {MessageId} by participant {ParticipantId} in chat {ChatId}",
                messageId, participantId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<MessageStatus> GetMessageStatusAsync(string messageId, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(GetMessageStatusAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
            await ValidateInitialized();

            lock (_stateLock)
            {
                if (State.MessageDeliveryStatus.TryGetValue(messageId, out var status))
                {
                    OrleansActivitySource.SetSuccess(activity);
                    return status; // Orleans manages immutability
                }
            }

            throw new MessageNotFoundException(State.ChatMetadata.ChatId, messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get message status for {MessageId} in chat {ChatId}", messageId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    #endregion

    #region IChatStreamingGrain Implementation

    /// <inheritdoc />
    public async Task<StreamHandle> StartStreamAsync(StreamMessage message, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(StartStreamAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentNullException.ThrowIfNull(message);
            await ValidateInitialized();
            await ValidateCanModify();

            // Check concurrent stream limits
            lock (_stateLock)
            {
                var activeStreamCount = State.ActiveStreams.Count(kvp => kvp.Value.Status == StreamStatus.Active);
                if (activeStreamCount >= State.Configuration.MaxConcurrentStreams)
                {
                    throw new InvalidChatStateException(State.ChatMetadata.ChatId,
                        $"Maximum concurrent streams ({State.Configuration.MaxConcurrentStreams}) reached");
                }
            }

            var streamId = Guid.NewGuid().ToString();
            var orleansStreamId = Guid.NewGuid();

            var streamState = new StreamState
            {
                StreamId = streamId,
                ChatId = State.ChatMetadata.ChatId,
                UserId = message.UserId,
                StartedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                Status = StreamStatus.Active,
                OperationId = Guid.NewGuid().ToString()
            };

            var streamHandle = new StreamHandle
            {
                StreamId = streamId,
                ChatId = State.ChatMetadata.ChatId,
                OrleansStreamId = orleansStreamId,
                CreatedAt = DateTime.UtcNow,
                Status = StreamStatus.Active
            };

            lock (_stateLock)
            {
                State.ActiveStreams[streamId] = streamState;
                State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                State.ChatMetadata.Version++;
                State.IncrementVersion();
            }

            await WriteStateAsync();

            _logger.LogInformation("Started stream {StreamId} for user {UserId} in chat {ChatId}",
                streamId, message.UserId, State.ChatMetadata.ChatId);

            // Notify participants about stream start
            await _signalRBroadcast.BroadcastToGroupAsync($"chat_{State.ChatMetadata.ChatId}", "StreamStarted", new
            {
                streamId,
                message.UserId,
                streamState.StartedAt
            });

            OrleansActivitySource.SetSuccess(activity);
            return streamHandle;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start stream in chat {ChatId}", State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ProcessStreamChunkAsync(StreamChunk chunk, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(ProcessStreamChunkAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentNullException.ThrowIfNull(chunk);
            await ValidateInitialized();
            await ValidateCanModify();

            StreamState? streamState = null;

            lock (_stateLock)
            {
                if (!State.ActiveStreams.TryGetValue(chunk.OperationId, out streamState))
                {
                    throw new StreamNotFoundException(State.ChatMetadata.ChatId, chunk.OperationId);
                }

                if (streamState.Status != StreamStatus.Active)
                {
                    throw new InvalidChatStateException(State.ChatMetadata.ChatId,
                        $"Stream {chunk.OperationId} is not active (status: {streamState.Status})");
                }

                // Update stream state
                streamState.LastActivity = DateTime.UtcNow;
                streamState.ChunksSent++;
                streamState.PartialMessage += chunk.Content;

                State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                State.IncrementVersion();
            }

            await WriteStateAsync();

            // Broadcast chunk to participants
            await _signalRBroadcast.BroadcastToGroupAsync($"chat_{State.ChatMetadata.ChatId}", "StreamChunk", chunk);

            _logger.LogDebug("Processed chunk {ChunkIndex} for stream {StreamId} in chat {ChatId}",
                chunk.ChunkIndex, chunk.OperationId, State.ChatMetadata.ChatId);

            // If this is the final chunk, prepare for completion
            if (chunk.IsComplete)
            {
                lock (_stateLock)
                {
                    streamState.Status = StreamStatus.Completed;
                }
                await WriteStateAsync();
            }

            OrleansActivitySource.SetSuccess(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process stream chunk for chat {ChatId}", State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<StreamState> CompleteStreamAsync(string streamId, string? finalContent = null, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(CompleteStreamAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
            await ValidateInitialized();

            StreamState? streamState = null;

            lock (_stateLock)
            {
                if (!State.ActiveStreams.TryGetValue(streamId, out streamState))
                {
                    throw new StreamNotFoundException(State.ChatMetadata.ChatId, streamId);
                }

                if (streamState.Status != StreamStatus.Active)
                {
                    throw new InvalidChatStateException(State.ChatMetadata.ChatId,
                        $"Stream {streamId} is not active (status: {streamState.Status})");
                }

                // Update stream state
                streamState.Status = StreamStatus.Completed;
                streamState.LastActivity = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(finalContent))
                {
                    streamState.PartialMessage = finalContent;
                }

                State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                State.IncrementVersion();
            }

            // Create final message from stream content
            if (!string.IsNullOrEmpty(streamState.PartialMessage))
            {
                var finalMessage = new ChatMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    ChatId = State.ChatMetadata.ChatId,
                    UserId = streamState.UserId,
                    Content = streamState.PartialMessage,
                    Role = "assistant",
                    Timestamp = DateTime.UtcNow,
                    IsStreaming = false,
                    Metadata = JsonSerializer.Serialize(new { StreamId = streamId, StreamCompleted = true })
                };

                await ProcessMessageAsync(finalMessage, cancellationToken);
            }

            await WriteStateAsync();

            // Notify participants about stream completion
            await _signalRBroadcast.BroadcastToGroupAsync($"chat_{State.ChatMetadata.ChatId}", "StreamCompleted", new
            {
                StreamId = streamId,
                CompletedAt = streamState.LastActivity,
                FinalContent = streamState.PartialMessage
            });

            _logger.LogInformation("Completed stream {StreamId} in chat {ChatId}", streamId, State.ChatMetadata.ChatId);

            OrleansActivitySource.SetSuccess(activity);
            return streamState; // Orleans manages immutability
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete stream {StreamId} in chat {ChatId}", streamId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task CancelStreamAsync(string streamId, string? reason = null, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(CancelStreamAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
            await ValidateInitialized();

            StreamState? streamState = null;

            lock (_stateLock)
            {
                if (!State.ActiveStreams.TryGetValue(streamId, out streamState))
                {
                    throw new StreamNotFoundException(State.ChatMetadata.ChatId, streamId);
                }

                if (streamState.Status is StreamStatus.Cancelled or StreamStatus.Completed)
                {
                    OrleansActivitySource.SetSuccess(activity);
                    return; // Already cancelled or completed
                }

                // Update stream state
                streamState.Status = StreamStatus.Cancelled;
                streamState.LastActivity = DateTime.UtcNow;
                streamState.ErrorMessage = reason ?? "Stream cancelled";

                State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                State.IncrementVersion();
            }

            await WriteStateAsync();

            // Notify participants about stream cancellation
            await _signalRBroadcast.BroadcastToGroupAsync($"chat_{State.ChatMetadata.ChatId}", "StreamCancelled", new
            {
                StreamId = streamId,
                CancelledAt = streamState.LastActivity,
                Reason = reason
            });

            _logger.LogInformation("Cancelled stream {StreamId} in chat {ChatId}: {Reason}",
                streamId, State.ChatMetadata.ChatId, reason ?? "No reason provided");

            OrleansActivitySource.SetSuccess(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel stream {StreamId} in chat {ChatId}", streamId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<StreamState?> GetStreamStateAsync(string streamId, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(GetStreamStateAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
            await ValidateInitialized();

            lock (_stateLock)
            {
                if (State.ActiveStreams.TryGetValue(streamId, out var streamState))
                {
                    OrleansActivitySource.SetSuccess(activity);
                    return streamState; // Orleans manages immutability
                }
            }

            OrleansActivitySource.SetSuccess(activity);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get stream state {StreamId} for chat {ChatId}", streamId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<StreamState>> GetActiveStreamsAsync(CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(GetActiveStreamsAsync), this.GetPrimaryKeyString());

        try
        {
            await ValidateInitialized();

            lock (_stateLock)
            {
                var activeStreams = State.ActiveStreams.Values
                    .Where(s => s.Status == StreamStatus.Active)
                    .Select(s => s) // Orleans manages immutability
                    .ToList();

                OrleansActivitySource.SetSuccess(activity);
                return activeStreams;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active streams for chat {ChatId}", State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<StreamSubscriptionHandle> SubscribeToStreamAsync(Guid streamId, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(SubscribeToStreamAsync), this.GetPrimaryKeyString());

        try
        {
            await ValidateInitialized();

            var subscriptionId = Guid.NewGuid().ToString();

            // Create subscription handle
            var handle = new StreamSubscriptionHandle
            {
                SubscriptionId = subscriptionId,
                StreamId = streamId,
                Namespace = "chat",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _logger.LogInformation("Created stream subscription {SubscriptionId} for stream {StreamId} in chat {ChatId}",
                subscriptionId, streamId, State.ChatMetadata.ChatId);

            OrleansActivitySource.SetSuccess(activity);
            return handle;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to stream {StreamId} for chat {ChatId}", streamId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    #endregion

    #region IChatParticipantGrain Implementation

    /// <inheritdoc />
    public async Task AddParticipantAsync(ChatParticipant participant, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(AddParticipantAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentNullException.ThrowIfNull(participant);
            await ValidateInitialized();
            await ValidateCanModify();

            // Check if participant already exists
            lock (_stateLock)
            {
                if (State.Participants.ContainsKey(participant.ParticipantId))
                {
                    throw new InvalidChatStateException(State.ChatMetadata.ChatId,
                        $"Participant '{participant.ParticipantId}' already exists in the chat");
                }

                // Check participant limits
                if (State.Configuration.MaxParticipants.HasValue &&
                    State.Participants.Count >= State.Configuration.MaxParticipants.Value)
                {
                    throw new InvalidChatStateException(State.ChatMetadata.ChatId,
                        $"Maximum participants limit ({State.Configuration.MaxParticipants.Value}) reached");
                }
            }

            // Validate participant data
            if (string.IsNullOrWhiteSpace(participant.ParticipantId))
            {
                throw new ArgumentException("Participant ID cannot be null or empty", nameof(participant));
            }

            if (string.IsNullOrWhiteSpace(participant.DisplayName))
            {
                throw new ArgumentException("Display name cannot be null or empty", nameof(participant));
            }

            lock (_stateLock)
            {
                // Add participant
                State.Participants[participant.ParticipantId] = participant;
                State.ChatMetadata.ParticipantCount = State.Participants.Count;
                State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                State.ChatMetadata.Version++;
                State.IncrementVersion();
            }

            await WriteStateAsync();

            // Notify other participants
            await NotifyParticipantsAsync("ParticipantJoined", participant, cancellationToken);

            _logger.LogInformation("Added participant {ParticipantId} ({DisplayName}) to chat {ChatId}",
                participant.ParticipantId, participant.DisplayName, State.ChatMetadata.ChatId);

            OrleansActivitySource.SetSuccess(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add participant {ParticipantId} to chat {ChatId}",
                participant?.ParticipantId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveParticipantAsync(string participantId, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(RemoveParticipantAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
            await ValidateInitialized();
            await ValidateCanModify();

            ChatParticipant? removedParticipant = null;

            lock (_stateLock)
            {
                if (State.Participants.TryGetValue(participantId, out removedParticipant))
                {
                    State.Participants.Remove(participantId);
                    State.ChatMetadata.ParticipantCount = State.Participants.Count;
                    State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                    State.ChatMetadata.Version++;
                    State.IncrementVersion();
                }
            }

            if (removedParticipant == null)
            {
                OrleansActivitySource.SetSuccess(activity);
                return false;
            }

            await WriteStateAsync();

            // Clean up participant-related data
            await CleanupParticipantDataAsync(participantId);

            // Notify other participants
            await NotifyParticipantsAsync("ParticipantLeft", new
            {
                participantId,
                removedParticipant.DisplayName,
                LeftAt = DateTime.UtcNow
            }, cancellationToken);

            _logger.LogInformation("Removed participant {ParticipantId} ({DisplayName}) from chat {ChatId}",
                participantId, removedParticipant.DisplayName, State.ChatMetadata.ChatId);

            OrleansActivitySource.SetSuccess(activity);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove participant {ParticipantId} from chat {ChatId}",
                participantId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ChatParticipant> UpdateParticipantAsync(ParticipantUpdate update, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(UpdateParticipantAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentNullException.ThrowIfNull(update);
            await ValidateInitialized();
            await ValidateCanModify();

            ChatParticipant? participant = null;

            lock (_stateLock)
            {
                if (!State.Participants.TryGetValue(update.ParticipantId, out participant))
                {
                    throw new ParticipantNotFoundException(State.ChatMetadata.ChatId, update.ParticipantId);
                }

                // Update participant properties
                if (!string.IsNullOrEmpty(update.DisplayName))
                {
                    participant.DisplayName = update.DisplayName;
                }

                if (update.Role.HasValue)
                {
                    participant.Role = update.Role.Value;
                }

                if (!string.IsNullOrEmpty(update.Metadata))
                {
                    participant.Metadata = update.Metadata;
                }

                participant.LastActivityAt = DateTime.UtcNow;

                State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                State.ChatMetadata.Version++;
                State.IncrementVersion();
            }

            await WriteStateAsync();

            // Notify other participants about the update
            await NotifyParticipantsAsync("ParticipantUpdated", new
            {
                update.ParticipantId,
                update,
                UpdatedAt = DateTime.UtcNow
            }, cancellationToken);

            _logger.LogInformation("Updated participant {ParticipantId} in chat {ChatId}",
                update.ParticipantId, State.ChatMetadata.ChatId);

            OrleansActivitySource.SetSuccess(activity);
            return participant; // Orleans manages immutability
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update participant {ParticipantId} in chat {ChatId}",
                update?.ParticipantId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ChatParticipant?> GetParticipantAsync(string participantId, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(GetParticipantAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
            await ValidateInitialized();

            lock (_stateLock)
            {
                if (State.Participants.TryGetValue(participantId, out var participant))
                {
                    OrleansActivitySource.SetSuccess(activity);
                    return participant; // Orleans manages immutability
                }
            }

            OrleansActivitySource.SetSuccess(activity);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get participant {ParticipantId} in chat {ChatId}",
                participantId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<ChatParticipant>> GetParticipantsAsync(CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(GetParticipantsAsync), this.GetPrimaryKeyString());

        try
        {
            await ValidateInitialized();

            lock (_stateLock)
            {
                var participants = State.Participants.Values
                    .Select(p => p) // Orleans manages immutability
                    .ToList();

                OrleansActivitySource.SetSuccess(activity);
                return participants;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get participants for chat {ChatId}", State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdatePresenceAsync(string participantId, PresenceStatus status, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(UpdatePresenceAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
            await ValidateInitialized();

            lock (_stateLock)
            {
                if (!State.Participants.TryGetValue(participantId, out var participant))
                {
                    throw new ParticipantNotFoundException(State.ChatMetadata.ChatId, participantId);
                }

                if (participant.Status != status)
                {
                    participant.Status = status;
                    participant.LastActivityAt = DateTime.UtcNow;

                    State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                    State.IncrementVersion();
                }
            }

            await WriteStateAsync();

            // Notify other participants about presence change
            await _signalRBroadcast.BroadcastToGroupAsync($"chat_{State.ChatMetadata.ChatId}", "PresenceUpdated", new
            {
                State.ChatMetadata.ChatId,
                participantId,
                status,
                UpdatedAt = DateTime.UtcNow
            });

            _logger.LogDebug("Updated presence for participant {ParticipantId} to {Status} in chat {ChatId}",
                participantId, status, State.ChatMetadata.ChatId);

            OrleansActivitySource.SetSuccess(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update presence for participant {ParticipantId} in chat {ChatId}",
                participantId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task NotifyParticipantsAsync(string eventType, object eventData, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(NotifyParticipantsAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
            ArgumentNullException.ThrowIfNull(eventData);
            await ValidateInitialized();

            var participantIds = State.Participants.Keys.ToList();

            if (participantIds.Count == 0)
            {
                OrleansActivitySource.SetSuccess(activity);
                return; // No participants to notify
            }

            // Broadcast via SignalR
            await _signalRBroadcast.BroadcastToGroupAsync($"chat_{State.ChatMetadata.ChatId}", eventType, eventData);

            _logger.LogDebug("Notified {ParticipantCount} participants in chat {ChatId} about {EventType}",
                participantIds.Count, State.ChatMetadata.ChatId, eventType);

            OrleansActivitySource.SetSuccess(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify participants in chat {ChatId} about {EventType}",
                State.ChatMetadata.ChatId, eventType);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CheckPermissionAsync(string participantId, ChatAction action, CancellationToken cancellationToken = default)
    {
        using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(CheckPermissionAsync), this.GetPrimaryKeyString());

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
            await ValidateInitialized();

            lock (_stateLock)
            {
                if (!State.Participants.TryGetValue(participantId, out var participant))
                {
                    throw new ParticipantNotFoundException(State.ChatMetadata.ChatId, participantId);
                }

                // Check permissions based on participant role and action
                var hasPermission = HasPermission(participant.Role, action);

                OrleansActivitySource.SetSuccess(activity);
                return hasPermission;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check permission for participant {ParticipantId} in chat {ChatId}",
                participantId, State.ChatMetadata.ChatId);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Validates message input and chat state for processing.
    /// </summary>
    /// <param name="message">The message to validate</param>
    private async Task ValidateMessageInputAsync(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        await ValidateInitialized();
        await ValidateCanModify();
    }

    /// <summary>
    /// Prepares message for state updates outside of lock for better performance.
    /// </summary>
    /// <param name="message">The message to prepare</param>
    /// <returns>The prepared message with all necessary properties set</returns>
    private async Task<ChatMessage> PrepareMessageForStateAsync(ChatMessage message)
    {
        await Task.CompletedTask; // For async consistency

        // Prepare message properties outside of lock
        var preparedMessage = new ChatMessage
        {
            Id = string.IsNullOrEmpty(message.Id) ? Guid.NewGuid().ToString() : message.Id,
            ChatId = State.ChatMetadata.ChatId,
            UserId = message.UserId,
            Content = message.Content,
            Role = message.Role,
            Timestamp = DateTime.UtcNow,
            IsStreaming = message.IsStreaming
        };

        // Prepare metadata with sequence number (will be set in lock)
        var metadata = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(message.Metadata))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<Dictionary<string, object>>(message.Metadata) ?? [];
                foreach (var kvp in existing)
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse message metadata, using empty metadata");
            }
        }

        // Note: SequenceNumber will be added in UpdateChatStateWithMessageAsync
        preparedMessage.Metadata = JsonSerializer.Serialize(metadata);

        return preparedMessage;
    }

    /// <summary>
    /// Updates chat state with the prepared message using optimized locking.
    /// </summary>
    /// <param name="message">The prepared message to add to state</param>
    private async Task UpdateChatStateWithMessageAsync(ChatMessage message)
    {
        await Task.CompletedTask; // For async consistency

        lock (_stateLock)
        {
            // Assign sequence number inside lock
            var sequenceNumber = State.GetNextSequenceNumber();
            var metadata = string.IsNullOrEmpty(message.Metadata) ?
                [] :
                JsonSerializer.Deserialize<Dictionary<string, object>>(message.Metadata) ?? [];
            metadata["SequenceNumber"] = sequenceNumber;
            message.Metadata = JsonSerializer.Serialize(metadata);

            // Update state with minimal time in lock
            State.AddRecentMessage(message);
            State.ChatMetadata.MessageCount++;
            State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
            State.ChatMetadata.Version++;

            // Initialize delivery status if tracking is enabled
            if (State.Configuration.EnableDeliveryTracking)
            {
                State.MessageDeliveryStatus[message.Id] = new MessageStatus
                {
                    MessageId = message.Id,
                    Status = DeliveryStatus.Sent,
                    PendingDelivery = [.. State.Participants.Keys],
                    SentAt = message.Timestamp
                };
            }

            State.IncrementVersion();
        }
    }

    /// <summary>
    /// Records message processing metrics.
    /// </summary>
    private async Task RecordMessageMetricsAsync()
    {
        State.Metrics.MessagesRelayed++;
        await _metricsCollector.RecordGrainOperationAsync(
            "ChatGrain",
            "ProcessMessage",
            1.0, // Placeholder duration
            true
        );
    }

    /// <summary>
    /// Broadcasts message to participants via SignalR.
    /// </summary>
    /// <param name="message">The message to broadcast</param>
    private async Task BroadcastMessageAsync(ChatMessage message)
    {
        await _signalRBroadcast.BroadcastToGroupAsync($"chat_{State.ChatMetadata.ChatId}", "MessageReceived", message);
    }

    /// <summary>
    /// Logs successful message processing.
    /// </summary>
    /// <param name="message">The processed message</param>
    private void LogMessageProcessed(ChatMessage message)
    {
        _logger.LogInformation(
            "Processed message {MessageId} from {UserId} in chat {ChatId} (Sequence: {SequenceNumber})",
            message.Id, message.UserId, State.ChatMetadata.ChatId, State.MessageSequenceNumber);
    }

    /// <summary>
    /// Validates that the chat has been properly initialized.
    /// </summary>
    private Task ValidateInitialized()
    {
        if (string.IsNullOrEmpty(State.ChatMetadata.ChatId) || State.ChatMetadata.Status == ChatStatus.Initializing)
        {
            throw new InvalidChatStateException(State.ChatMetadata.ChatId ?? "unknown", "Chat has not been initialized");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates that the chat can be modified (not archived or deleted).
    /// </summary>
    private Task ValidateCanModify()
    {
        if (!State.CanModify())
        {
            throw new ChatArchivedException(State.ChatMetadata.ChatId);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates message metadata with additional properties.
    /// </summary>
    /// <param name="currentMetadata">Current metadata JSON string</param>
    /// <param name="key">Key to update</param>
    /// <param name="value">Value to set</param>
    /// <returns>Updated metadata JSON string</returns>
    private string UpdateMessageMetadata(string? currentMetadata, string key, object value)
    {
        var metadata = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(currentMetadata))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<Dictionary<string, object>>(currentMetadata) ?? [];
                foreach (var kvp in existing)
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse existing metadata for message, starting fresh");
            }
        }

        metadata[key] = value;
        metadata["lastModified"] = DateTime.UtcNow;

        return JsonSerializer.Serialize(metadata);
    }

    /// <summary>
    /// Cleans up data related to a removed participant.
    /// </summary>
    /// <param name="participantId">ID of the participant to clean up</param>
    private async Task CleanupParticipantDataAsync(string participantId)
    {
        bool stateChanged = false;

        lock (_stateLock)
        {
            // Remove from delivery status acknowledgments
            foreach (var status in State.MessageDeliveryStatus.Values)
            {
                if (status.AcknowledgedBy.Remove(participantId))
                {
                    stateChanged = true;
                }
                status.PendingDelivery.Remove(participantId);
            }

            if (stateChanged)
            {
                State.IncrementVersion();
            }
        }

        if (stateChanged)
        {
            await WriteStateAsync();
        }
    }

    /// <summary>
    /// Checks if a participant role has permission to perform an action.
    /// </summary>
    /// <param name="role">Participant role</param>
    /// <param name="action">Action to check</param>
    /// <returns>True if permission is granted</returns>
    private bool HasPermission(ParticipantRole role, ChatAction action)
    {
        return action switch
        {
            ChatAction.SendMessage => role >= ParticipantRole.Guest,
            ChatAction.EditOwnMessage => role >= ParticipantRole.Member,
            ChatAction.EditAnyMessage => role >= ParticipantRole.Moderator,
            ChatAction.DeleteOwnMessage => role >= ParticipantRole.Member,
            ChatAction.DeleteAnyMessage => role >= ParticipantRole.Moderator,
            ChatAction.AddParticipant => role >= ParticipantRole.Moderator,
            ChatAction.RemoveParticipant => role >= ParticipantRole.Moderator,
            ChatAction.ChangeRoles => role >= ParticipantRole.Admin,
            ChatAction.ArchiveChat => role >= ParticipantRole.Admin,
            ChatAction.DeleteChat => role >= ParticipantRole.Owner,
            ChatAction.UpdateSettings => role >= ParticipantRole.Admin,
            _ => false
        };
    }

    /// <summary>
    /// Initializes the grain state for new or reactivated grains.
    /// </summary>
    private Task InitializeGrainStateAsync()
    {
        if (string.IsNullOrEmpty(State.ChatMetadata.ChatId))
        {
            // Initialize new grain
            State.ChatMetadata.ChatId = this.GetPrimaryKeyString();
            State.ActivatedAt = DateTime.UtcNow;
            State.Metrics.ActivationCount = 1;
            State.IncrementVersion();
        }
        else
        {
            // Reactivate existing grain
            State.Metrics.ActivationCount++;
            State.ActivatedAt = DateTime.UtcNow;
            State.IncrementVersion();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Records activation metrics for monitoring and performance tracking.
    /// </summary>
    /// <param name="activationStart">The timestamp when activation started</param>
    /// <returns>The activation time in milliseconds</returns>
    private async Task<double> RecordActivationMetricsAsync(DateTime activationStart)
    {
        var activationTime = (DateTime.UtcNow - activationStart).TotalMilliseconds;
        await _metricsCollector.RecordGrainActivationAsync(
            "ChatGrain",
            State.ChatMetadata.ChatId,
            activationTime
        );
        return activationTime;
    }

    /// <summary>
    /// Logs successful grain activation with relevant details.
    /// </summary>
    private void LogSuccessfulActivation()
    {
        _logger.LogInformation(
            "ChatGrain {ChatId} activated (Activation #{ActivationCount}, Version {StateVersion})",
            State.ChatMetadata.ChatId,
            State.Metrics.ActivationCount,
            State.StateVersion
        );
    }

    /// <summary>
    /// Sets up periodic timers for cleanup and metrics collection.
    /// </summary>
    private Task SetupPeriodicTimersAsync()
    {
        // Setup periodic cleanup timer
        _cleanupTimer = this.RegisterGrainTimer(
            async _ => await PeriodicCleanupAsync(),
            new GrainTimerCreationOptions
            {
                DueTime = State.Configuration.CleanupInterval,
                Period = State.Configuration.CleanupInterval,
                Interleave = true,
            }
        );

        // Setup metrics timer
        _metricsTimer = this.RegisterGrainTimer(
            async _ => await UpdateMetricsAsync(),
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromMinutes(5),
                Period = TimeSpan.FromMinutes(5),
                Interleave = true,
            }
        );
        return Task.CompletedTask;
    }

    /// <summary>
    /// Processes any pending operations from previous grain activation.
    /// </summary>
    private async Task ProcessPendingOperationsAsync(CancellationToken cancellationToken)
    {
        if (State.PendingOperations.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Processing {Count} pending operations for chat {ChatId}",
            State.PendingOperations.Count, State.ChatMetadata.ChatId);

        var completedOperations = new List<string>();

        foreach (var (operationId, operation) in State.PendingOperations)
        {
            try
            {
                if (operation.NextRetryAt.HasValue && DateTime.UtcNow < operation.NextRetryAt.Value)
                {
                    continue; // Not time to retry yet
                }

                // Process the operation based on type
                await ProcessPendingOperationAsync(operation, cancellationToken);
                completedOperations.Add(operationId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process pending operation {OperationId} for chat {ChatId}",
                    operationId, State.ChatMetadata.ChatId);

                // Update retry info
                operation.RetryCount++;
                operation.LastError = ex.Message;

                if (operation.RetryCount >= 3)
                {
                    // Give up after 3 retries
                    completedOperations.Add(operationId);
                    _logger.LogError("Abandoning pending operation {OperationId} after {RetryCount} failed attempts",
                        operationId, operation.RetryCount);
                }
                else
                {
                    // Schedule next retry with exponential backoff
                    operation.NextRetryAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, operation.RetryCount));
                }
            }
        }

        // Remove completed operations
        foreach (var operationId in completedOperations)
        {
            State.PendingOperations.Remove(operationId);
        }

        if (completedOperations.Count > 0)
        {
            State.IncrementVersion();
            await WriteStateAsync();
        }
    }

    /// <summary>
    /// Processes a specific pending operation.
    /// </summary>
    private Task ProcessPendingOperationAsync(PendingOperation operation, CancellationToken cancellationToken)
    {
        // This is a placeholder - in a full implementation, you would deserialize the operation data
        // and call the appropriate method based on the operation type
        switch (operation.OperationType)
        {
            case "ProcessMessage":
                // Deserialize and reprocess message
                break;
            case "CompleteStream":
                // Complete any incomplete streams
                break;
            default:
                _logger.LogWarning("Unknown pending operation type: {OperationType}", operation.OperationType);
                break;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs periodic cleanup of expired data.
    /// </summary>
    private async Task PeriodicCleanupAsync()
    {
        try
        {
            using var activity = OrleansActivitySource.StartGrainActivity("ChatGrain", nameof(PeriodicCleanupAsync), this.GetPrimaryKeyString());

            bool stateChanged = false;

            lock (_stateLock)
            {
                // Clean up expired streams
                var expiredStreams = State.ActiveStreams.Where(kvp =>
                    DateTime.UtcNow - kvp.Value.LastActivity > TimeSpan.FromSeconds(State.Configuration.StreamTimeoutSeconds))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var streamId in expiredStreams)
                {
                    State.ActiveStreams.Remove(streamId);
                    stateChanged = true;
                    _logger.LogInformation("Cleaned up expired stream {StreamId} in chat {ChatId}", streamId, State.ChatMetadata.ChatId);
                }

                // Clean up old delivery status
                var expiredDeliveryStatus = State.MessageDeliveryStatus.Where(kvp =>
                    DateTime.UtcNow - kvp.Value.SentAt > State.Configuration.DeliveryStatusRetention)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var messageId in expiredDeliveryStatus)
                {
                    State.MessageDeliveryStatus.Remove(messageId);
                    stateChanged = true;
                }

                // Clean up old pending operations
                var expiredOperations = State.PendingOperations.Where(kvp =>
                    DateTime.UtcNow - kvp.Value.CreatedAt > TimeSpan.FromHours(24))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var operationId in expiredOperations)
                {
                    State.PendingOperations.Remove(operationId);
                    stateChanged = true;
                    _logger.LogInformation("Cleaned up expired pending operation {OperationId} in chat {ChatId}", operationId, State.ChatMetadata.ChatId);
                }

                if (stateChanged)
                {
                    State.IncrementVersion();
                }
            }

            if (stateChanged)
            {
                await WriteStateAsync();
            }

            OrleansActivitySource.SetSuccess(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during periodic cleanup for chat {ChatId}", State.ChatMetadata.ChatId);
        }
    }

    /// <summary>
    /// Updates grain metrics.
    /// </summary>
    private async Task UpdateMetricsAsync()
    {
        try
        {
            if (!State.Configuration.EnableDetailedMetrics)
            {
                return;
            }

            // Update grain metrics
            lock (_stateLock)
            {
                State.Metrics.ActiveOperationsCount = State.PendingOperations.Count;
                State.Metrics.CurrentBufferCount = State.ActiveStreams.Count;
                State.Metrics.TotalStreamsProcessed = State.MessageSequenceNumber;
            }

            // Report to metrics collector
            await _metricsCollector.RecordGrainStateMetricsAsync(
                "ChatGrain",
                State.ChatMetadata.ChatId,
                CalculateStateSize(),
                State.Participants.Count,
                State.PendingOperations.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating metrics for chat {ChatId}", State.ChatMetadata.ChatId);
        }
    }

    /// <summary>
    /// Cleans up all active streams during deactivation or archival.
    /// </summary>
    private async Task CleanupActiveStreamsAsync(CancellationToken cancellationToken)
    {
        if (State.ActiveStreams.Count == 0)
        {
            return;
        }

        var streamIds = State.ActiveStreams.Keys.ToList();

        foreach (var streamId in streamIds)
        {
            try
            {
                await CancelStreamAsync(streamId, "Chat deactivation", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup stream {StreamId} for chat {ChatId}", streamId, State.ChatMetadata.ChatId);
            }
        }
    }

    /// <summary>
    /// Calculates the approximate size of the grain state in bytes.
    /// </summary>
    /// <returns>Estimated state size in bytes</returns>
    private long CalculateStateSize()
    {
        long size = 0;

        // ChatMetadata size (estimated)
        size += 1000; // Base metadata size

        // Recent messages (approximate)
        size += State.RecentMessages.Count * 500; // ~500 bytes per message

        // Participants
        size += State.Participants.Count * 200; // ~200 bytes per participant

        // Active streams
        size += State.ActiveStreams.Count * 300; // ~300 bytes per stream

        // Message delivery status
        size += State.MessageDeliveryStatus.Count * 100; // ~100 bytes per status

        // Pending operations
        size += State.PendingOperations.Count * 400; // ~400 bytes per operation

        return size;
    }

    #endregion

    #region IDisposable Implementation

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cleanupTimer?.Dispose();
        _metricsTimer?.Dispose();

        _disposed = true;
    }

    #endregion
}