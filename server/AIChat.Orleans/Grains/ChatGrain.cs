using System.Text.Json;
using AIChat.Orleans.Configuration;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Metrics;
using AIChat.Orleans.Models;
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
    private IGrainTimer? _sequenceGapTimer;
    private bool _disposed;
    private readonly object _stateLock = new();

    // Cached JSON serializer options for notifications
    private static readonly JsonSerializerOptions NotificationJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

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
            _sequenceGapTimer?.Dispose();

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

            // Pipeline step 3: Enhanced sequence-aware message processing
            var processedImmediately = await ProcessSequencedMessageAsync(preparedMessage);

            // Pipeline step 4: Process any queued messages that are now available
            var queuedProcessedCount = await ProcessQueuedMessagesAsync();

            // Pipeline step 5: Handle sequence gap timeouts (periodic recovery)
            await HandleSequenceGapTimeoutsAsync();

            // Pipeline step 6: Persist state changes
            await WriteStateAsync();

            // Pipeline step 7: Record metrics
            await RecordMessageMetricsAsync();

            // Pipeline step 8: Broadcast messages
            if (processedImmediately)
            {
                // Broadcast the primary message if it was processed immediately
                await BroadcastMessageAsync(preparedMessage);
                LogMessageProcessed(preparedMessage);
            }
            else
            {
                // Message was queued - log for monitoring
                _logger.LogInformation(
                    "Message {MessageId} queued for sequence processing in chat {ChatId}",
                    preparedMessage.Id, State.ChatMetadata.ChatId);
            }

            // Log queue processing if any messages were processed
            if (queuedProcessedCount > 0)
            {
                _logger.LogInformation(
                    "Processed {QueuedCount} queued messages after receiving message {MessageId} in chat {ChatId}",
                    queuedProcessedCount, preparedMessage.Id, State.ChatMetadata.ChatId);
            }

            OrleansActivitySource.SetSuccess(activity, new Dictionary<string, object>
            {
                ["ProcessedImmediately"] = processedImmediately,
                ["QueuedProcessedCount"] = queuedProcessedCount,
                ["MessageId"] = preparedMessage.Id,
                ["SequenceNumber"] = GetSequenceNumberFromMessage(preparedMessage)
            });

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

            // Comprehensive validation
            ValidateParticipantData(participant);
            ValidateChatStateConsistency("add participant");

            // Check if participant already exists (Orleans grain single-threaded, no lock needed)
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

            // Add participant (Orleans grain single-threaded, no lock needed)
            State.Participants[participant.ParticipantId] = participant;
            State.ChatMetadata.ParticipantCount = State.Participants.Count;
            State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
            State.ChatMetadata.Version++;
            State.IncrementVersion();

            await WriteStateAsync();

            // Notify other participants
            await NotifyParticipantsAsync("ParticipantJoined", participant, cancellationToken);

            _logger.LogInformation("Added participant {ParticipantId} ({DisplayName}, role: {Role}) to chat {ChatId}. Total participants: {ParticipantCount}",
                participant.ParticipantId, participant.DisplayName, participant.Role, State.ChatMetadata.ChatId, State.Participants.Count);

            OrleansActivitySource.SetSuccess(activity, new Dictionary<string, object>
            {
                ["ParticipantId"] = participant.ParticipantId,
                ["DisplayName"] = participant.DisplayName,
                ["Role"] = participant.Role.ToString(),
                ["TotalParticipants"] = State.Participants.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add participant {ParticipantId} to chat {ChatId}. Error: {ErrorType}",
                participant?.ParticipantId, State.ChatMetadata.ChatId, ex.GetType().Name);
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

            // Validate chat state and operation
            ValidateChatStateConsistency("remove participant");

            // Remove participant (Orleans grain single-threaded, no lock needed)
            if (State.Participants.TryGetValue(participantId, out var removedParticipant))
            {
                State.Participants.Remove(participantId);
                State.ChatMetadata.ParticipantCount = State.Participants.Count;
                State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
                State.ChatMetadata.Version++;
                State.IncrementVersion();
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

            _logger.LogInformation("Removed participant {ParticipantId} ({DisplayName}, role: {Role}) from chat {ChatId}. Remaining participants: {ParticipantCount}",
                participantId, removedParticipant.DisplayName, removedParticipant.Role, State.ChatMetadata.ChatId, State.Participants.Count);

            OrleansActivitySource.SetSuccess(activity, new Dictionary<string, object>
            {
                ["ParticipantId"] = participantId,
                ["DisplayName"] = removedParticipant.DisplayName,
                ["Role"] = removedParticipant.Role.ToString(),
                ["RemainingParticipants"] = State.Participants.Count
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove participant {ParticipantId} from chat {ChatId}. Error: {ErrorType}",
                participantId, State.ChatMetadata.ChatId, ex.GetType().Name);
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

            // Update participant (Orleans grain single-threaded, no lock needed)
            ValidateChatStateConsistency("update participant");

            if (!State.Participants.TryGetValue(update.ParticipantId, out var participant))
            {
                throw new ParticipantNotFoundException(State.ChatMetadata.ChatId, update.ParticipantId);
            }

            // Validate update data before applying changes
            if (!string.IsNullOrEmpty(update.DisplayName))
            {
                if (update.DisplayName.Length > 100)
                {
                    throw new ArgumentException("Display name cannot exceed 100 characters");
                }
                if (update.DisplayName.Contains('<') || update.DisplayName.Contains('>') ||
                    update.DisplayName.Contains("script", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Display name contains invalid characters");
                }
            }

            if (update.Role.HasValue && !Enum.IsDefined(update.Role.Value))
            {
                throw new ArgumentException($"Invalid participant role: {update.Role.Value}");
            }

            if (!string.IsNullOrEmpty(update.Metadata) && update.Metadata.Length > 1000)
            {
                throw new ArgumentException("Participant metadata cannot exceed 1000 characters");
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

            await WriteStateAsync();

            // Notify other participants about the update
            await NotifyParticipantsAsync("ParticipantUpdated", new
            {
                update.ParticipantId,
                update,
                UpdatedAt = DateTime.UtcNow
            }, cancellationToken);

            _logger.LogInformation("Updated participant {ParticipantId} in chat {ChatId}. Changes: DisplayName={DisplayNameChanged}, Role={RoleChanged}, Metadata={MetadataChanged}",
                update.ParticipantId, State.ChatMetadata.ChatId,
                !string.IsNullOrEmpty(update.DisplayName),
                update.Role.HasValue,
                !string.IsNullOrEmpty(update.Metadata));

            OrleansActivitySource.SetSuccess(activity, new Dictionary<string, object>
            {
                ["ParticipantId"] = update.ParticipantId,
                ["UpdatedDisplayName"] = !string.IsNullOrEmpty(update.DisplayName),
                ["UpdatedRole"] = update.Role.HasValue,
                ["UpdatedMetadata"] = !string.IsNullOrEmpty(update.Metadata),
                ["CurrentRole"] = participant.Role.ToString()
            });
            return participant; // Orleans manages immutability
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update participant {ParticipantId} in chat {ChatId}. Error: {ErrorType}",
                update?.ParticipantId, State.ChatMetadata.ChatId, ex.GetType().Name);
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

            // Get participant (Orleans grain single-threaded, no lock needed)
            if (State.Participants.TryGetValue(participantId, out var participant))
            {
                OrleansActivitySource.SetSuccess(activity);
                return participant; // Orleans manages immutability
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

            // Get participants (Orleans grain single-threaded, no lock needed)
            var participants = State.Participants.Values
                .Select(p => p) // Orleans manages immutability
                .ToList();

            OrleansActivitySource.SetSuccess(activity);
            return participants;
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

            // Update presence (Orleans grain single-threaded, no lock needed)
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

            // Serialize event data for consistent notification format
            string serializedEventData;
            try
            {
                serializedEventData = JsonSerializer.Serialize(eventData, NotificationJsonOptions);
            }
            catch (Exception serializationEx)
            {
                _logger.LogError(serializationEx, "Failed to serialize event data for notification in chat {ChatId}, eventType: {EventType}",
                    State.ChatMetadata.ChatId, eventType);
                throw new InvalidOperationException($"Failed to serialize notification data for event '{eventType}'", serializationEx);
            }

            // Broadcast via SignalR with serialized data
            await _signalRBroadcast.BroadcastToGroupAsync($"chat_{State.ChatMetadata.ChatId}", eventType, serializedEventData);

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

            // Check permissions (Orleans grain single-threaded, no lock needed)
            if (!State.Participants.TryGetValue(participantId, out var participant))
            {
                throw new ParticipantNotFoundException(State.ChatMetadata.ChatId, participantId);
            }

            // Check permissions based on participant role and action
            var hasPermission = HasPermission(participant.Role, action);

            _logger.LogDebug("Permission check for participant {ParticipantId} in chat {ChatId}: action {Action}, role {Role}, granted: {HasPermission}",
                participantId, State.ChatMetadata.ChatId, action, participant.Role, hasPermission);

            OrleansActivitySource.SetSuccess(activity);
            return hasPermission;
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

        // Note: SequenceNumber will be added in ProcessSequencedMessageAsync
        preparedMessage.Metadata = JsonSerializer.Serialize(metadata);

        return preparedMessage;
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
    /// Enhanced message processing with sequence verification and out-of-order handling.
    /// Optimized to minimize lock contention by preparing data outside the lock.
    /// </summary>
    /// <param name="message">The prepared message to process</param>
    /// <returns>True if message was processed immediately, false if queued</returns>
    private async Task<bool> ProcessSequencedMessageAsync(ChatMessage message)
    {
        await Task.CompletedTask; // For async consistency

        // Prepare sequence processing info outside the lock for better performance
        var sequenceInfo = PrepareSequenceProcessingInfo(message);

        // Critical section: minimal lock scope for state updates only
        SequenceProcessingResult result;
        lock (_stateLock)
        {
            result = ProcessSequenceWithinLock(sequenceInfo);
        }

        // Handle logging and other operations outside the lock
        await HandleSequenceProcessingResult(result, sequenceInfo);

        return result.ProcessedImmediately;
    }

    /// <summary>
    /// Prepares sequence processing information outside of the lock for optimal performance.
    /// Includes enhanced error handling and metadata recovery.
    /// </summary>
    /// <param name="message">The message to prepare for sequence processing</param>
    /// <returns>Sequence processing information</returns>
    private SequenceProcessingInfo PrepareSequenceProcessingInfo(ChatMessage message)
    {
        Dictionary<string, object> metadata;

        try
        {
            // Parse existing metadata outside the lock to reduce lock duration
            metadata = ParseMessageMetadata(message.Metadata);
        }
        catch (Exception ex)
        {
            // Attempt to recover from corrupted metadata
            var recoveredMetadata = RecoverFromCorruptedMetadata(message, ex);
            metadata = recoveredMetadata ?? [];
        }

        // Perform validation of sequence state periodically
        if (State.MessageSequenceNumber % 100 == 0) // Every 100 messages
        {
            ValidateAndRecoverSequenceState();
        }

        return new SequenceProcessingInfo
        {
            Message = message,
            ParsedMetadata = metadata,
            ChatId = State.ChatMetadata.ChatId,
            IsStrictOrderingEnabled = State.SequenceConfig.EnableStrictOrdering,
            ShouldLogGaps = State.SequenceConfig.LogSequenceGaps
        };
    }

    /// <summary>
    /// Handles the core sequence processing logic within the lock with minimal duration.
    /// Includes enhanced gap recovery and error handling.
    /// </summary>
    /// <param name="info">Pre-prepared sequence processing information</param>
    /// <returns>The result of sequence processing</returns>
    private SequenceProcessingResult ProcessSequenceWithinLock(SequenceProcessingInfo info)
    {
        // Assign sequence number and update metadata
        var sequenceNumber = State.GetNextSequenceNumber();
        info.ParsedMetadata["SequenceNumber"] = sequenceNumber;
        info.Message.Metadata = JsonSerializer.Serialize(info.ParsedMetadata);

        // Check if message can be processed immediately (in sequence)
        if (info.IsStrictOrderingEnabled && !State.CanProcessMessageImmediately(sequenceNumber))
        {
            var expectedSequence = State.LastProcessedSequenceNumber + 1;

            // Use enhanced gap recovery logic
            var recoveryAction = HandleSequenceGapWithRecovery(sequenceNumber, expectedSequence);

            if (recoveryAction == SequenceGapRecoveryAction.SkippedToSequence)
            {
                // Message can now be processed immediately after skip
                ProcessMessageInSequence(info.Message, sequenceNumber);

                return new SequenceProcessingResult
                {
                    ProcessedImmediately = true,
                    SequenceNumber = sequenceNumber,
                    ExpectedSequence = expectedSequence,
                    ShouldLogGap = info.ShouldLogGaps,
                    RecoveryAction = recoveryAction
                };
            }

            // Queue out-of-order message for later processing
            State.QueueOutOfOrderMessage(info.Message, sequenceNumber);
            State.IncrementVersion();

            return new SequenceProcessingResult
            {
                ProcessedImmediately = false,
                SequenceNumber = sequenceNumber,
                ExpectedSequence = expectedSequence,
                ShouldLogGap = info.ShouldLogGaps,
                RecoveryAction = recoveryAction
            };
        }

        // Process message immediately (in sequence or strict ordering disabled)
        ProcessMessageInSequence(info.Message, sequenceNumber);

        return new SequenceProcessingResult
        {
            ProcessedImmediately = true,
            SequenceNumber = sequenceNumber,
            RecoveryAction = SequenceGapRecoveryAction.None
        };
    }

    /// <summary>
    /// Handles post-processing operations outside the lock, such as logging.
    /// </summary>
    /// <param name="result">The result of sequence processing</param>
    /// <param name="info">The original sequence processing information</param>
    private async Task HandleSequenceProcessingResult(SequenceProcessingResult result, SequenceProcessingInfo info)
    {
        await Task.CompletedTask; // For async consistency

        // Log sequence gap warning outside the lock for better performance
        if (!result.ProcessedImmediately && result.ShouldLogGap)
        {
            _logger.LogWarning(
                "Message {MessageId} with sequence {SequenceNumber} queued (expected {ExpectedSequence}) in chat {ChatId}",
                info.Message.Id, result.SequenceNumber, result.ExpectedSequence, info.ChatId);
        }
    }

    /// <summary>
    /// Efficiently parses message metadata with error handling.
    /// </summary>
    /// <param name="metadata">The JSON metadata string</param>
    /// <returns>Parsed metadata dictionary</returns>
    private Dictionary<string, object> ParseMessageMetadata(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(metadata) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse message metadata, using empty metadata");
            return [];
        }
    }

    /// <summary>
    /// Processes a message that is in the correct sequence order.
    /// Updates state and tracks the processed sequence number.
    /// </summary>
    /// <param name="message">The message to process</param>
    /// <param name="sequenceNumber">The sequence number of the message</param>
    private void ProcessMessageInSequence(ChatMessage message, long sequenceNumber)
    {
        // Add to recent messages buffer
        State.AddRecentMessage(message);
        State.ChatMetadata.MessageCount++;
        State.ChatMetadata.LastActivityAt = DateTime.UtcNow;
        State.ChatMetadata.Version++;

        // Update last processed sequence number
        State.LastProcessedSequenceNumber = sequenceNumber;

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

    /// <summary>
    /// Processes any queued out-of-order messages that can now be handled in sequence.
    /// Should be called after processing any message to check for newly available messages.
    /// </summary>
    /// <returns>Number of queued messages that were processed</returns>
    private async Task<int> ProcessQueuedMessagesAsync()
    {
        await Task.CompletedTask; // For async consistency

        List<ChatMessage> processableMessages;

        lock (_stateLock)
        {
            // Get messages that can now be processed
            processableMessages = State.ProcessQueuedMessages();
        }

        if (processableMessages.Count > 0)
        {
            foreach (var queuedMessage in processableMessages)
            {
                // Get sequence number from metadata
                var sequenceNumber = GetSequenceNumberFromMessage(queuedMessage);

                lock (_stateLock)
                {
                    // Process queued message (already has sequence number assigned)
                    ProcessMessageInSequence(queuedMessage, sequenceNumber);
                }

                // Broadcast queued message (outside of lock for performance)
                await BroadcastMessageAsync(queuedMessage);

                _logger.LogInformation(
                    "Processed queued message {MessageId} with sequence {SequenceNumber} in chat {ChatId}",
                    queuedMessage.Id, sequenceNumber, State.ChatMetadata.ChatId);
            }

            // Record metrics for queued message processing
            await RecordQueuedMessageMetricsAsync(processableMessages.Count);
        }

        return processableMessages.Count;
    }

    /// <summary>
    /// Handles sequence gap timeouts and recovery logic.
    /// Identifies missing messages that have timed out and skips them to maintain message flow.
    /// </summary>
    /// <returns>Number of sequence gaps that were resolved by timeout</returns>
    private async Task<int> HandleSequenceGapTimeoutsAsync()
    {
        await Task.CompletedTask; // For async consistency

        List<long> timedOutSequences;

        lock (_stateLock)
        {
            timedOutSequences = State.GetTimedOutSequences();
        }

        if (timedOutSequences.Count > 0)
        {
            lock (_stateLock)
            {
                foreach (var timedOutSequence in timedOutSequences)
                {
                    // Skip to the timed-out sequence
                    State.SkipToSequence(timedOutSequence);

                    if (State.SequenceConfig.LogSequenceGaps)
                    {
                        _logger.LogWarning(
                            "Sequence gap timeout: skipped to sequence {SkippedSequence} in chat {ChatId}",
                            timedOutSequence, State.ChatMetadata.ChatId);
                    }
                }

                State.IncrementVersion();
            }

            // After skipping sequences, try to process newly available queued messages
            await ProcessQueuedMessagesAsync();

            // Record recovery metrics
            await RecordSequenceRecoveryMetricsAsync(timedOutSequences.Count);
        }

        return timedOutSequences.Count;
    }

    /// <summary>
    /// Extracts the sequence number from a message's metadata with optimized parsing.
    /// Uses caching and efficient extraction to minimize JSON overhead.
    /// </summary>
    /// <param name="message">The message to extract sequence number from</param>
    /// <returns>The sequence number, or 0 if not found</returns>
    private long GetSequenceNumberFromMessage(ChatMessage message)
    {
        if (string.IsNullOrEmpty(message.Metadata))
        {
            return 0;
        }

        // Try fast path: look for sequence number in a simple JSON pattern
        var fastSequence = TryExtractSequenceNumberFast(message.Metadata);
        if (fastSequence > 0)
        {
            return fastSequence;
        }

        // Fallback to full JSON parsing
        return ExtractSequenceNumberFromMetadata(message.Metadata, message.Id);
    }

    /// <summary>
    /// Attempts to extract sequence number using fast string parsing without full JSON deserialization.
    /// This optimization handles the common case where metadata follows a predictable pattern.
    /// </summary>
    /// <param name="metadata">The JSON metadata string</param>
    /// <returns>The sequence number if found via fast path, 0 otherwise</returns>
    private long TryExtractSequenceNumberFast(string metadata)
    {
        // Look for the pattern: "SequenceNumber":number (with various whitespace possibilities)
        const string sequenceKey = "\"SequenceNumber\"";
        var keyIndex = metadata.IndexOf(sequenceKey, StringComparison.Ordinal);
        if (keyIndex == -1)
        {
            return 0;
        }

        // Find the colon after the key
        var colonIndex = metadata.IndexOf(':', keyIndex + sequenceKey.Length);
        if (colonIndex == -1)
        {
            return 0;
        }

        // Find the start of the number (skip whitespace)
        var startIndex = colonIndex + 1;
        while (startIndex < metadata.Length && char.IsWhiteSpace(metadata[startIndex]))
        {
            startIndex++;
        }

        if (startIndex >= metadata.Length)
        {
            return 0;
        }

        // Find the end of the number
        var endIndex = startIndex;
        while (endIndex < metadata.Length && char.IsDigit(metadata[endIndex]))
        {
            endIndex++;
        }

        if (endIndex == startIndex)
        {
            return 0; // No digits found
        }

        // Try to parse the number
        var numberSpan = metadata.AsSpan(startIndex, endIndex - startIndex);
        if (long.TryParse(numberSpan, out var sequence))
        {
            return sequence;
        }

        return 0;
    }

    /// <summary>
    /// Extracts sequence number using full JSON parsing as a fallback.
    /// </summary>
    /// <param name="metadata">The JSON metadata string</param>
    /// <param name="messageId">The message ID for logging purposes</param>
    /// <returns>The sequence number, or 0 if not found</returns>
    private long ExtractSequenceNumberFromMetadata(string metadata, string messageId)
    {
        try
        {
            var parsedMetadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadata) ?? [];
            if (parsedMetadata.TryGetValue("SequenceNumber", out var sequenceNumberObj))
            {
                return Convert.ToInt64(sequenceNumberObj, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse sequence number from message {MessageId} metadata", messageId);
        }

        return 0;
    }

    /// <summary>
    /// Records metrics for queued message processing.
    /// </summary>
    private async Task RecordQueuedMessageMetricsAsync(int processedCount)
    {
        await _metricsCollector.RecordGrainOperationAsync(
            "ChatGrain",
            "ProcessQueuedMessages",
            processedCount,
            true
        );
    }

    /// <summary>
    /// Records metrics for sequence recovery operations.
    /// </summary>
    private async Task RecordSequenceRecoveryMetricsAsync(int recoveredCount)
    {
        await _metricsCollector.RecordGrainOperationAsync(
            "ChatGrain",
            "SequenceRecovery",
            recoveredCount,
            true
        );
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
    /// Validates participant data for consistency and business rules.
    /// </summary>
    /// <param name="participant">The participant to validate</param>
    /// <exception cref="ArgumentException">When participant data is invalid</exception>
    /// <exception cref="InvalidChatStateException">When participant violates chat state rules</exception>
    private void ValidateParticipantData(ChatParticipant participant)
    {
        // Basic null/empty validation
        if (string.IsNullOrWhiteSpace(participant.ParticipantId))
        {
            throw new ArgumentException("Participant ID cannot be null or empty", nameof(participant));
        }

        if (string.IsNullOrWhiteSpace(participant.DisplayName))
        {
            throw new ArgumentException("Display name cannot be null or empty", nameof(participant));
        }

        // Enhanced validation for participant ID format (basic GUID or alphanumeric validation)
        if (participant.ParticipantId.Length is < 3 or > 50)
        {
            throw new ArgumentException("Participant ID must be between 3 and 50 characters", nameof(participant));
        }

        // Validate display name format and length
        if (participant.DisplayName.Length > 100)
        {
            throw new ArgumentException("Display name cannot exceed 100 characters", nameof(participant));
        }

        // Check for potentially harmful content in display name (basic XSS protection)
        if (participant.DisplayName.Contains('<') || participant.DisplayName.Contains('>') ||
            participant.DisplayName.Contains("script", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Display name contains invalid characters", nameof(participant));
        }

        // Validate role is within acceptable range
        if (!Enum.IsDefined(participant.Role))
        {
            throw new ArgumentException($"Invalid participant role: {participant.Role}", nameof(participant));
        }

        // Validate status is within acceptable range
        if (!Enum.IsDefined(participant.Status))
        {
            throw new ArgumentException($"Invalid presence status: {participant.Status}", nameof(participant));
        }

        // Validate metadata if present
        if (!string.IsNullOrEmpty(participant.Metadata) && participant.Metadata.Length > 1000)
        {
            throw new ArgumentException("Participant metadata cannot exceed 1000 characters", nameof(participant));
        }
    }

    /// <summary>
    /// Validates chat state consistency for participant operations.
    /// </summary>
    /// <param name="operation">The operation being performed</param>
    /// <exception cref="InvalidChatStateException">When chat state is inconsistent</exception>
    private void ValidateChatStateConsistency(string operation)
    {
        // Ensure participant count matches dictionary count
        if (State.ChatMetadata.ParticipantCount != State.Participants.Count)
        {
            _logger.LogWarning("Participant count mismatch in chat {ChatId}: metadata={MetadataCount}, actual={ActualCount}",
                State.ChatMetadata.ChatId, State.ChatMetadata.ParticipantCount, State.Participants.Count);

            // Fix the inconsistency
            State.ChatMetadata.ParticipantCount = State.Participants.Count;
            State.IncrementVersion();
        }

        // Validate chat is in a valid state for participant operations
        if (State.ChatMetadata.Status == ChatStatus.Archived)
        {
            throw new InvalidChatStateException(State.ChatMetadata.ChatId,
                $"Cannot perform {operation} on archived chat");
        }

        if (State.ChatMetadata.Status == ChatStatus.Deleted)
        {
            throw new InvalidChatStateException(State.ChatMetadata.ChatId,
                $"Cannot perform {operation} on deleted chat");
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

        // Setup sequence gap processing timer
        _sequenceGapTimer = this.RegisterGrainTimer(
            async _ => await HandleSequenceGapTimeoutsAsync(),
            new GrainTimerCreationOptions
            {
                DueTime = State.SequenceConfig.GapProcessingInterval,
                Period = State.SequenceConfig.GapProcessingInterval,
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
        _sequenceGapTimer?.Dispose();

        _disposed = true;
    }

    #endregion

    #region Enhanced Error Handling for Sequence Processing

    /// <summary>
    /// Handles sequence gap recovery with enhanced error handling and diagnostics.
    /// </summary>
    /// <param name="sequenceNumber">The sequence number that caused the gap</param>
    /// <param name="expectedSequence">The expected next sequence number</param>
    /// <returns>Recovery action taken</returns>
    private SequenceGapRecoveryAction HandleSequenceGapWithRecovery(long sequenceNumber, long expectedSequence)
    {
        var gapSize = sequenceNumber - expectedSequence;

        // Check for extreme sequence gaps that might indicate system issues
        if (gapSize > State.SequenceConfig.MaxSequenceGap)
        {
            _logger.LogError(
                "Extreme sequence gap detected in chat {ChatId}: received {SequenceNumber}, expected {ExpectedSequence} (gap: {GapSize})",
                State.ChatMetadata.ChatId, sequenceNumber, expectedSequence, gapSize);

            // For extreme gaps, consider immediate recovery
            if (State.SequenceConfig.EnableStrictOrdering)
            {
                // Skip forward to the received message to prevent indefinite blocking
                State.SkipToSequence(sequenceNumber - 1);

                return SequenceGapRecoveryAction.SkippedToSequence;
            }
        }

        // Normal gap handling - queue and wait for timeout
        return SequenceGapRecoveryAction.QueuedForTimeout;
    }

    /// <summary>
    /// Validates and recovers from corrupted message metadata.
    /// </summary>
    /// <param name="message">The message with potentially corrupted metadata</param>
    /// <param name="exception">The exception that occurred during parsing</param>
    /// <returns>Recovered metadata or null if unrecoverable</returns>
    private Dictionary<string, object>? RecoverFromCorruptedMetadata(ChatMessage message, Exception exception)
    {
        _logger.LogWarning(exception,
            "Corrupted metadata detected for message {MessageId} in chat {ChatId}, attempting recovery",
            message.Id, State.ChatMetadata.ChatId);

        try
        {
            // Attempt to create minimal metadata for the message
            var recoveredMetadata = new Dictionary<string, object>
            {
                ["MessageId"] = message.Id,
                ["RecoveredFromCorruption"] = true,
                ["OriginalCorruptedMetadata"] = message.Metadata ?? string.Empty,
                ["RecoveryTimestamp"] = DateTime.UtcNow
            };

            _logger.LogInformation(
                "Successfully recovered metadata for message {MessageId} in chat {ChatId}",
                message.Id, State.ChatMetadata.ChatId);

            return recoveredMetadata;
        }
        catch (Exception recoveryException)
        {
            _logger.LogError(recoveryException,
                "Failed to recover corrupted metadata for message {MessageId} in chat {ChatId}",
                message.Id, State.ChatMetadata.ChatId);

            return null;
        }
    }

    /// <summary>
    /// Enhanced validation of sequence processing state with recovery capabilities.
    /// </summary>
    /// <returns>True if state is valid or was successfully recovered</returns>
    private bool ValidateAndRecoverSequenceState()
    {
        var issues = new List<string>();

        // Check for sequence number inconsistencies
        if (State.LastProcessedSequenceNumber > State.MessageSequenceNumber)
        {
            issues.Add($"LastProcessed ({State.LastProcessedSequenceNumber}) > MessageSequence ({State.MessageSequenceNumber})");

            // Recovery: reset last processed to the current sequence number
            State.LastProcessedSequenceNumber = State.MessageSequenceNumber;
        }

        // Check for excessive out-of-order queue size
        if (State.OutOfOrderMessageQueue.Count > State.SequenceConfig.MaxOutOfOrderQueueSize * 0.9)
        {
            issues.Add($"OutOfOrder queue near capacity: {State.OutOfOrderMessageQueue.Count}/{State.SequenceConfig.MaxOutOfOrderQueueSize}");
        }

        // Check for stale gap timeouts
        var staleTimeouts = State.SequenceGapTimeouts.Count(kvp =>
            DateTime.UtcNow - kvp.Value > State.SequenceConfig.SequenceGapTimeout.Add(TimeSpan.FromMinutes(5)));

        if (staleTimeouts > 0)
        {
            issues.Add($"Found {staleTimeouts} stale gap timeouts");

            // Recovery: clean up stale timeouts
            var staleKeys = State.SequenceGapTimeouts
                .Where(kvp => DateTime.UtcNow - kvp.Value > State.SequenceConfig.SequenceGapTimeout.Add(TimeSpan.FromMinutes(5)))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                State.SequenceGapTimeouts.Remove(key);
            }
        }

        if (issues.Count > 0)
        {
            _logger.LogWarning(
                "Sequence state issues detected and recovered in chat {ChatId}: {Issues}",
                State.ChatMetadata.ChatId, string.Join(", ", issues));

            State.IncrementVersion();
        }

        return true; // Always return true since we perform recovery
    }

    #endregion

    #region Helper Classes for Sequence Processing Optimization

    /// <summary>
    /// Information prepared for sequence processing outside of the lock.
    /// </summary>
    private sealed class SequenceProcessingInfo
    {
        public ChatMessage Message { get; init; } = null!;
        public Dictionary<string, object> ParsedMetadata { get; init; } = [];
        public string ChatId { get; init; } = string.Empty;
        public bool IsStrictOrderingEnabled { get; init; }
        public bool ShouldLogGaps { get; init; }
    }

    /// <summary>
    /// Result of sequence processing operations.
    /// </summary>
    private sealed class SequenceProcessingResult
    {
        public bool ProcessedImmediately { get; init; }
        public long SequenceNumber { get; init; }
        public long ExpectedSequence { get; init; }
        public bool ShouldLogGap { get; init; }
        public SequenceGapRecoveryAction RecoveryAction { get; init; }
    }

    /// <summary>
    /// Indicates the type of recovery action taken for sequence gaps.
    /// </summary>
    private enum SequenceGapRecoveryAction
    {
        /// <summary>No recovery action needed.</summary>
        None,
        /// <summary>Message was queued to wait for timeout recovery.</summary>
        QueuedForTimeout,
        /// <summary>Sequence was skipped forward to resume processing.</summary>
        SkippedToSequence,
        /// <summary>Metadata was recovered from corruption.</summary>
        MetadataRecovered
    }

    #endregion
}