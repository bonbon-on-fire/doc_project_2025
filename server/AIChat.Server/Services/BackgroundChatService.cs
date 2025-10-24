using System.Collections.Concurrent;
using System.Threading.Channels;
using AchieveAi.LmDotnetTools.LmCore.Agents;
using AIChat.Orleans.Client.Services;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Tracing;
using AIChat.Server.Models;
using AIChat.Server.Storage;
using Microsoft.Extensions.Options;
// Alias to resolve OperationStatus ambiguity between AIChat.Server.Models.OperationStatus and AIChat.Orleans.Contracts.OperationStatus
using ServerOperationStatus = AIChat.Server.Models.OperationStatus;

namespace AIChat.Server.Services;

/// <summary>
/// <para>Background service that processes chat operations asynchronously.</para>
/// <para>
/// KEY ARCHITECTURAL DECISION: This service uses the existing ChatService internally
/// to leverage all existing functionality:
/// - Agentic loop and LLM processing
/// - Tool middleware and integrations
/// - Mode-based system prompts
/// - Message persistence and validation
/// - Stream processing capabilities
/// </para>
/// <para>
/// BackgroundChatService handles:
/// - Queueing and worker pool management
/// - Operation lifecycle tracking
/// - Cancellation support
/// - Grain coordination
/// </para>
/// </summary>
public class BackgroundChatService : BackgroundService, IBackgroundChatService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackgroundChatService> _logger;
    private readonly BackgroundServiceOptions _options;

    /// <summary>
    /// Queue management
    /// </summary>
    private readonly Channel<ChatOperation> _operationQueue;
    private readonly ChannelWriter<ChatOperation> _queueWriter;
    private readonly ChannelReader<ChatOperation> _queueReader;

    /// <summary>
    /// Concurrency control - max 10 concurrent operations as per design
    /// </summary>
    private readonly SemaphoreSlim _workerSemaphore;

    /// <summary>
    /// Operation tracking
    /// </summary>
    private readonly ConcurrentDictionary<string, OperationState> _activeOperations;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _operationCancellations;

    /// <summary>
    /// Statistics tracking
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _completedOperations;
    private readonly ConcurrentDictionary<
        string,
        (DateTime timestamp, string error)
    > _failedOperations;
    private readonly ConcurrentQueue<double> _processingTimes;

    public BackgroundChatService(
        IServiceProvider serviceProvider,
        ILogger<BackgroundChatService> logger,
        IOptions<BackgroundServiceOptions> options
    )
    {
        _serviceProvider =
            serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value;

        // Create unbounded channel for operations queue
        var channelOptions = new UnboundedChannelOptions
        {
            SingleReader = false, // Multiple workers can read
            SingleWriter = false, // Multiple threads can enqueue
            AllowSynchronousContinuations = false, // Avoid blocking
        };
        _operationQueue = Channel.CreateUnbounded<ChatOperation>(channelOptions);
        _queueWriter = _operationQueue.Writer;
        _queueReader = _operationQueue.Reader;

        // Initialize concurrency control
        _workerSemaphore = new SemaphoreSlim(
            _options.MaxConcurrentOperations,
            _options.MaxConcurrentOperations
        );

        // Initialize tracking dictionaries
        _activeOperations = new ConcurrentDictionary<string, OperationState>();
        _operationCancellations = new ConcurrentDictionary<string, CancellationTokenSource>();
        _completedOperations = new ConcurrentDictionary<string, DateTime>();
        _failedOperations = new ConcurrentDictionary<string, (DateTime, string)>();
        _processingTimes = new ConcurrentQueue<double>();

        _logger.LogInformation(
            "BackgroundChatService initialized with max concurrency: {MaxConcurrency}",
            _options.MaxConcurrentOperations
        );
    }

    public async Task<string> EnqueueOperationAsync(
        ChatOperation operation,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = OrleansActivitySource.StartBackgroundActivity(
            "BackgroundChatService",
            nameof(EnqueueOperationAsync),
            operation?.Id
        );
        try
        {
            ArgumentNullException.ThrowIfNull(operation);

            // Generate operation ID if not provided
            if (string.IsNullOrEmpty(operation.Id))
            {
                operation.Id = Guid.NewGuid().ToString();
            }

            // Add tracing tags
            _ = (activity?.SetTag("operation.type", operation.Type.ToString()));
            _ = (activity?.SetTag("operation.chat_id", operation.ChatId));
            _ = (activity?.SetTag("operation.user_id", operation.UserId));

            // Set queued timestamp
            operation.QueuedAt = DateTime.UtcNow;

            // Create operation state
            

            // Track the operation
            _activeOperations[operation.Id] = new OperationState
            {
                Operation = operation,
                Status = ServerOperationStatus.Queued,
                QueuedAt = operation.QueuedAt,
            };

            try
            {
                // Enqueue the operation
                await _queueWriter.WriteAsync(operation, cancellationToken);

                _logger.LogInformation(
                    "Enqueued operation {OperationId} of type {OperationType} for chat {ChatId} and user {UserId}",
                    operation.Id,
                    operation.Type,
                    operation.ChatId,
                    operation.UserId
                );

                // Mark activity as successful
                OrleansActivitySource.SetSuccess(
                    activity,
                    new Dictionary<string, object>
                    {
                        {
                            "queue.size",
                            _operationQueue.Reader.CanCount ? _operationQueue.Reader.Count : -1
                        },
                        { "active.operations", _activeOperations.Count },
                    }
                );

                return operation.Id;
            }
            catch (Exception ex)
            {
                // Set activity error
                OrleansActivitySource.SetError(activity, ex);

                // Remove from tracking if enqueue failed
                _ = _activeOperations.TryRemove(operation.Id, out _);
                _logger.LogError(ex, "Failed to enqueue operation {OperationId}", operation.Id);
                throw;
            }
        }
        catch (Exception outerEx)
        {
            // Handle any exceptions from the activity setup
            OrleansActivitySource.SetError(activity, outerEx);
            throw;
        }
    }

    public Task<bool> CancelOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(operationId))
        {
            return Task.FromResult(false);
        }

        _logger.LogInformation("Attempting to cancel operation {OperationId}", operationId);

        // Try to cancel if operation exists
        if (_operationCancellations.TryGetValue(operationId, out var cts))
        {
            try
            {
                cts.Cancel();

                // Update operation status
                if (_activeOperations.TryGetValue(operationId, out var state))
                {
                    state.Status = ServerOperationStatus.Cancelled;
                    state.CompletedAt = DateTime.UtcNow;
                    state.Error = "Operation was cancelled by user request";
                }

                _logger.LogInformation(
                    "Successfully cancelled operation {OperationId}",
                    operationId
                );
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling operation {OperationId}", operationId);
                return Task.FromResult(false);
            }
        }

        // Operation might be in queue but not started yet
        if (
            _activeOperations.TryGetValue(operationId, out var queuedState)
            && queuedState.Status == ServerOperationStatus.Queued
        )
        {
            queuedState.Status = ServerOperationStatus.Cancelled;
            queuedState.CompletedAt = DateTime.UtcNow;
            queuedState.Error = "Operation was cancelled before processing started";

            _logger.LogInformation("Cancelled queued operation {OperationId}", operationId);
            return Task.FromResult(true);
        }

        _logger.LogWarning(
            "Could not cancel operation {OperationId} - not found or already completed",
            operationId
        );
        return Task.FromResult(false);
    }

    public Task<OperationStatusInfo?> GetOperationStatusAsync(
        string operationId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(operationId))
        {
            return Task.FromResult<OperationStatusInfo?>(null);
        }

        if (_activeOperations.TryGetValue(operationId, out var state))
        {
            var statusInfo = new OperationStatusInfo
            {
                OperationId = operationId,
                Status = state.Status,
                QueuedAt = state.QueuedAt,
                StartedAt = state.StartedAt,
                CompletedAt = state.CompletedAt,
                Error = state.Error,
                Progress = state.Progress,
                ProgressDescription = state.ProgressDescription,
            };
            return Task.FromResult<OperationStatusInfo?>(statusInfo);
        }

        return Task.FromResult<OperationStatusInfo?>(null);
    }

    public Task<QueueStatistics> GetQueueStatisticsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var pendingCount = _activeOperations.Values.Count(s =>
            s.Status == ServerOperationStatus.Queued
        );
        var activeCount = _activeOperations.Values.Count(s =>
            s.Status == ServerOperationStatus.InProgress
        );

        // Count completed/failed operations in the last hour
        var oneHourAgo = DateTime.UtcNow.AddHours(-1);
        var completedLastHour = _completedOperations.Values.Count(t => t > oneHourAgo);
        var failedLastHour = _failedOperations.Values.Count(f => f.timestamp > oneHourAgo);

        // Calculate average processing time
        var recentProcessingTimes = _processingTimes.ToArray().TakeLast(100).ToArray();
        var avgProcessingTime =
            recentProcessingTimes.Length > 0 ? recentProcessingTimes.Average() : 0.0;

        var statistics = new QueueStatistics
        {
            PendingCount = pendingCount,
            ActiveCount = activeCount,
            MaxConcurrency = _options.MaxConcurrentOperations,
            CompletedLastHour = completedLastHour,
            FailedLastHour = failedLastHour,
            AverageProcessingTimeMs = avgProcessingTime,
            CollectedAt = DateTime.UtcNow,
        };

        return Task.FromResult(statistics);
    }

    public Task<IEnumerable<OperationStatusInfo>> GetUserOperationsAsync(
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(Enumerable.Empty<OperationStatusInfo>());
        }

        var operations = _activeOperations
            .Values.Where(state => state.Operation.UserId == userId && state.Status is ServerOperationStatus.Queued or ServerOperationStatus.InProgress
)
            .Select(state => new OperationStatusInfo
            {
                OperationId = state.Operation.Id,
                Status = state.Status,
                QueuedAt = state.QueuedAt,
                StartedAt = state.StartedAt,
                CompletedAt = state.CompletedAt,
                Error = state.Error,
                Progress = state.Progress,
                ProgressDescription = state.ProgressDescription,
            });

        return Task.FromResult(operations);
    }

    public Task<IEnumerable<OperationStatusInfo>> GetChatOperationsAsync(
        string chatId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(chatId))
        {
            return Task.FromResult(Enumerable.Empty<OperationStatusInfo>());
        }

        var operations = _activeOperations
            .Values.Where(state => state.Operation.ChatId == chatId && state.Status is ServerOperationStatus.Queued or ServerOperationStatus.InProgress
)
            .Select(state => new OperationStatusInfo
            {
                OperationId = state.Operation.Id,
                Status = state.Status,
                QueuedAt = state.QueuedAt,
                StartedAt = state.StartedAt,
                CompletedAt = state.CompletedAt,
                Error = state.Error,
                Progress = state.Progress,
                ProgressDescription = state.ProgressDescription,
            });

        return Task.FromResult(operations);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BackgroundChatService started executing");

        try
        {
            // Process operations from the queue
            await foreach (var operation in _queueReader.ReadAllAsync(stoppingToken))
            {
                // Don't block the main loop - fire and forget each operation
                _ = Task.Run(
                    async () => await ProcessOperationAsync(operation, stoppingToken),
                    stoppingToken
                );
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("BackgroundChatService execution cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BackgroundChatService execution failed");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BackgroundChatService stopping...");

        // Complete the queue writer to signal no more operations
        try
        {
            _queueWriter.Complete();
        }
        catch (InvalidOperationException)
        {
            // Channel might already be closed
        }

        // Wait for all active operations to complete or timeout
        var activeOperationIds = _operationCancellations.Keys.ToList();
        if (activeOperationIds.Count > 0)
        {
            _logger.LogInformation(
                "Waiting for {Count} active operations to complete",
                activeOperationIds.Count
            );

            // Cancel all active operations
            foreach (var operationId in activeOperationIds)
            {
                if (_operationCancellations.TryGetValue(operationId, out var cts))
                {
                    try
                    {
                        cts.Cancel();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Error cancelling operation {OperationId} during shutdown",
                            operationId
                        );
                    }
                }
            }

            // Wait a reasonable time for operations to complete
            var shutdownTimeout = TimeSpan.FromSeconds(30);
            using var timeoutCts = new CancellationTokenSource(shutdownTimeout);
            try
            {
                var combinedToken = CancellationTokenSource
                    .CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
                    .Token;

                // Wait for all operations to complete
                while (
                    _activeOperations.Values.Any(s => s.Status == ServerOperationStatus.InProgress)
                    && !combinedToken.IsCancellationRequested
                )
                {
                    await Task.Delay(100, combinedToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Shutdown timeout reached, some operations may not have completed gracefully"
                );
            }
        }

        // Dispose resources
        _workerSemaphore?.Dispose();

        foreach (var cts in _operationCancellations.Values)
        {
            cts?.Dispose();
        }
        _operationCancellations.Clear();

        _logger.LogInformation("BackgroundChatService stopped");

        await base.StopAsync(cancellationToken);
    }

    private async Task ProcessOperationAsync(
        ChatOperation operation,
        CancellationToken stoppingToken
    )
    {
        // Wait for available worker slot
        await _workerSemaphore.WaitAsync(stoppingToken);

        var startTime = DateTime.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        OperationState? state = null;

        try
        {
            // Skip if operation was cancelled while waiting
            if (
                !_activeOperations.TryGetValue(operation.Id, out state)
                || state.Status == ServerOperationStatus.Cancelled
            )
            {
                _logger.LogInformation(
                    "Operation {OperationId} was cancelled before processing",
                    operation.Id
                );
                return;
            }

            // Create cancellation token for this operation
            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var operationToken = operationCts.Token;

            // Set timeout
            operationCts.CancelAfter(operation.TimeoutMs);

            // Track cancellation token for external cancellation
            _operationCancellations[operation.Id] = operationCts;

            // Update operation state to in-progress
            state.Status = ServerOperationStatus.InProgress;
            state.StartedAt = startTime;
            state.ProgressDescription = "Starting operation processing";

            _logger.LogInformation(
                "Started processing operation {OperationId} of type {OperationType} for chat {ChatId}",
                operation.Id,
                operation.Type,
                operation.ChatId
            );

            // Process the operation based on its type
            await ProcessOperationByTypeAsync(operation, state, operationToken);

            // Mark as completed
            state.Status = ServerOperationStatus.Completed;
            state.CompletedAt = DateTime.UtcNow;
            state.Progress = 1.0;
            state.ProgressDescription = "Operation completed successfully";

            // Record completion statistics
            _completedOperations[operation.Id] = DateTime.UtcNow;
            _processingTimes.Enqueue(stopwatch.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "Completed operation {OperationId} in {Duration}ms",
                operation.Id,
                stopwatch.ElapsedMilliseconds
            );
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested
                || (state?.Status == ServerOperationStatus.Cancelled)
            )
        {
            if (state != null)
            {
                state.Status = ServerOperationStatus.Cancelled;
                state.CompletedAt = DateTime.UtcNow;
                state.Error = "Operation was cancelled";
            }

            _logger.LogInformation("Operation {OperationId} was cancelled", operation.Id);
        }
        catch (Exception ex)
        {
            if (state != null)
            {
                state.Status = ServerOperationStatus.Failed;
                state.CompletedAt = DateTime.UtcNow;
                state.Error = ex.Message;
                state.ProgressDescription = $"Operation failed: {ex.Message}";
            }

            // Record failure statistics
            _failedOperations[operation.Id] = (DateTime.UtcNow, ex.Message);

            _logger.LogError(
                ex,
                "Operation {OperationId} failed after {Duration}ms",
                operation.Id,
                stopwatch.ElapsedMilliseconds
            );
        }
        finally
        {
            // Cleanup
            _ = _operationCancellations.TryRemove(operation.Id, out _);

            // Clean up old tracking data periodically
            CleanupOldOperations();

            // Release worker slot
            _ = _workerSemaphore.Release();
        }
    }

    private async Task ProcessOperationByTypeAsync(
        ChatOperation operation,
        OperationState state,
        CancellationToken cancellationToken
    )
    {
        using var scope = _serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

        // Get required services
        var chatService = services.GetRequiredService<IChatServiceStreaming>();
        var storage = services.GetRequiredService<IChatStorage>();
        var streamingAgent = services.GetRequiredService<IStreamingAgent>();
        var toolingService = services.GetRequiredService<IToolingService>();
        var modeService = services.GetRequiredService<IModeService>();
        var orleansService = services.GetService<IOrleansIntegrationService>();

        state.ProgressDescription = $"Processing {operation.Type} operation";
        state.Progress = 0.1;

        // Notify UserGrain that operation started
        await NotifyUserGrainOperationStartedAsync(operation, orleansService);

        try
        {
            switch (operation.Type)
            {
                case OperationType.SendMessage:
                    await ProcessSendMessageOperationAsync(
                        operation,
                        state,
                        chatService,
                        storage,
                        streamingAgent,
                        toolingService,
                        modeService,
                        orleansService,
                        cancellationToken
                    );
                    break;

                case OperationType.RegenerateResponse:
                    await ProcessRegenerateResponseOperationAsync(
                        operation,
                        state,
                        chatService,
                        storage,
                        streamingAgent,
                        toolingService,
                        modeService,
                        orleansService,
                        cancellationToken
                    );
                    break;

                case OperationType.EditMessage:
                    await ProcessEditMessageOperationAsync(
                        operation,
                        state,
                        chatService,
                        storage,
                        streamingAgent,
                        toolingService,
                        modeService,
                        orleansService,
                        cancellationToken
                    );
                    break;
                case OperationType.DeleteMessage:
                    break;
                default:
                    throw new ArgumentException($"Unknown operation type: {operation.Type}");
            }

            // Notify UserGrain of successful completion
            await NotifyUserGrainOperationCompletedAsync(operation, orleansService, success: true);
        }
        catch (Exception ex)
        {
            // Notify UserGrain of failure
            await NotifyUserGrainOperationCompletedAsync(
                operation,
                orleansService,
                success: false,
                ex.Message
            );
            throw; // Re-throw to maintain error handling flow
        }
    }

    private async Task ProcessSendMessageOperationAsync(
        ChatOperation operation,
        OperationState state,
        IChatServiceStreaming chatService,
        IChatStorage storage,
        IStreamingAgent streamingAgent,
        IToolingService toolingService,
        IModeService modeService,
        IOrleansIntegrationService? orleansService,
        CancellationToken cancellationToken
    )
    {
        // Extract message from payload
        var message = operation.Payload?.ToString();
        if (string.IsNullOrEmpty(message))
        {
            throw new ArgumentException(
                "SendMessage operation requires message content in payload"
            );
        }

        state.ProgressDescription = "Processing message with AI";
        state.Progress = 0.3;

        // Create callbacks for streaming updates (simplified for now)
        var messageCallback = CreateMessageCallback(operation, state);
        var chunkCallback = CreateChunkCallback(operation, state);

        // Process the message using ChatService
        await chatService.ProcessMessageWithCallbackAsync(
            chatId: operation.ChatId,
            message: message,
            userId: operation.UserId,
            storage: storage,
            streamingAgent: streamingAgent,
            toolingService: toolingService,
            modeService: modeService,
            orleansService: orleansService,
            modeId: operation.ModeId,
            systemPrompt: operation.SystemPrompt,
            messageCallback: messageCallback,
            chunkCallback: chunkCallback,
            cancellationToken: cancellationToken
        );

        state.ProgressDescription = "Message processing completed";
        state.Progress = 0.9;
    }

    private Func<MessageEvent, Task>? CreateMessageCallback(
        ChatOperation operation,
        OperationState state
    )
    {
        return async (messageEvent) =>
        {
            try
            {
                state.ProgressDescription = $"Processing {messageEvent.GetType().Name}";

                _logger.LogDebug(
                    "Message event for operation {OperationId}: {EventType}",
                    operation.Id,
                    messageEvent.GetType().Name
                );

                // Convert MessageEvent to ChatMessage and relay through UserGrain
                var chatMessage = ConvertMessageEventToChatMessage(messageEvent, operation);
                if (chatMessage != null)
                {
                    await RelayMessageThroughUserGrainAsync(operation, chatMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error in message callback for operation {OperationId}",
                    operation.Id
                );
            }
        };
    }

    private Func<StreamChunkEvent, Task>? CreateChunkCallback(
        ChatOperation operation,
        OperationState state
    )
    {
        return async (chunkEvent) =>
        {
            try
            {
                // Update progress based on chunk
                state.Progress = Math.Min(0.9, state.Progress + 0.01); // Gradually increase progress
                state.ProgressDescription = "Streaming AI response";

                _logger.LogTrace(
                    "Chunk event for operation {OperationId}: {EventType}",
                    operation.Id,
                    chunkEvent.GetType().Name
                );

                // Convert StreamChunkEvent to StreamChunk and relay through UserGrain
                var streamChunk = ConvertStreamChunkEventToStreamChunk(
                    chunkEvent,
                    operation,
                    state
                );
                if (streamChunk != null)
                {
                    await RelayStreamChunkThroughUserGrainAsync(operation, streamChunk);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error in chunk callback for operation {OperationId}",
                    operation.Id
                );
            }
        };
    }

    private void CleanupOldOperations()
    {
        // Only cleanup every 100 operations to avoid overhead
        if (_activeOperations.Count < 100)
        {
            return;
        }

        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-2); // Keep operations for 2 hours
            var toRemove = _activeOperations
                .Where(kvp => (kvp.Value.CompletedAt ?? kvp.Value.QueuedAt) < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var operationId in toRemove)
            {
                _ = _activeOperations.TryRemove(operationId, out _);
                _ = _completedOperations.TryRemove(operationId, out _);
                _ = _failedOperations.TryRemove(operationId, out _);
            }

            // Limit processing times queue size
            while (_processingTimes.Count > 1000)
            {
                _ = _processingTimes.TryDequeue(out _);
            }

            if (toRemove.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} old operations", toRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during operation cleanup");
        }
    }

    #region RegenerateResponse and EditMessage Operations

    private async Task ProcessRegenerateResponseOperationAsync(
        ChatOperation operation,
        OperationState state,
        IChatServiceStreaming chatService,
        IChatStorage storage,
        IStreamingAgent streamingAgent,
        IToolingService toolingService,
        IModeService modeService,
        IOrleansIntegrationService? orleansService,
        CancellationToken cancellationToken
    )
    {
        // Extract message ID from payload
        var messageId = ExtractMessageIdFromPayload(operation.Payload);
        if (string.IsNullOrEmpty(messageId))
        {
            throw new ArgumentException(
                "RegenerateResponse operation requires message ID in payload"
            );
        }

        state.ProgressDescription = "Looking up message to regenerate";
        state.Progress = 0.2;

        // For now, we'll implement a simplified regenerate by just re-processing the most recent user message
        // A more complete implementation would need to look up the specific message in the chat history
        // and find the preceding user message, but that requires more complex chat storage integration

        // Extract message content from payload for regeneration
        var messageContent = operation.Payload?.ToString();
        if (string.IsNullOrEmpty(messageContent))
        {
            throw new ArgumentException(
                "RegenerateResponse operation requires message content in payload"
            );
        }

        // For now, use the message content directly
        var userMessage = messageContent;

        state.ProgressDescription = "Regenerating AI response";
        state.Progress = 0.4;

        // Create callbacks for streaming updates
        var messageCallback = CreateMessageCallback(operation, state);
        var chunkCallback = CreateChunkCallback(operation, state);

        // Process the user message again to regenerate response
        await chatService.ProcessMessageWithCallbackAsync(
            chatId: operation.ChatId,
            message: userMessage,
            userId: operation.UserId,
            storage: storage,
            streamingAgent: streamingAgent,
            toolingService: toolingService,
            modeService: modeService,
            orleansService: orleansService,
            modeId: operation.ModeId,
            systemPrompt: operation.SystemPrompt,
            messageCallback: messageCallback,
            chunkCallback: chunkCallback,
            cancellationToken: cancellationToken
        );

        state.ProgressDescription = "Response regeneration completed";
        state.Progress = 0.9;
    }

    private async Task ProcessEditMessageOperationAsync(
        ChatOperation operation,
        OperationState state,
        IChatServiceStreaming chatService,
        IChatStorage storage,
        IStreamingAgent streamingAgent,
        IToolingService toolingService,
        IModeService modeService,
        IOrleansIntegrationService? orleansService,
        CancellationToken cancellationToken
    )
    {
        // Extract edit payload
        var editPayload =
            ExtractEditPayloadFromOperation(operation.Payload)
            ?? throw new ArgumentException(
                "EditMessage operation requires edit payload with messageId and newContent"
            );
        state.ProgressDescription = "Updating message content";
        state.Progress = 0.3;

        // For now, we'll treat edit as adding a new user message with the edited content
        // A more sophisticated implementation would involve message history management

        // Create callbacks for streaming updates
        var messageCallback = CreateMessageCallback(operation, state);
        var chunkCallback = CreateChunkCallback(operation, state);

        state.ProgressDescription = "Processing edited message";
        state.Progress = 0.5;

        // Process the edited message
        await chatService.ProcessMessageWithCallbackAsync(
            chatId: operation.ChatId,
            message: editPayload.NewContent,
            userId: operation.UserId,
            storage: storage,
            streamingAgent: streamingAgent,
            toolingService: toolingService,
            modeService: modeService,
            orleansService: orleansService,
            modeId: operation.ModeId,
            systemPrompt: operation.SystemPrompt,
            messageCallback: messageCallback,
            chunkCallback: chunkCallback,
            cancellationToken: cancellationToken
        );

        state.ProgressDescription = "Message edit processing completed";
        state.Progress = 0.9;
    }

    #endregion RegenerateResponse and EditMessage Operations

    #region UserGrain Integration

    private async Task NotifyUserGrainOperationStartedAsync(
        ChatOperation operation,
        IOrleansIntegrationService? orleansService
    )
    {
        if (orleansService == null)
        {
            return;
        }

        try
        {
            var userGrain = GetUserGrainFromOrleansService(orleansService, operation.UserId);
            await userGrain.NotifyOperationStarted(operation.Id, operation.ChatId);

            _logger.LogDebug(
                "Notified UserGrain of operation start: {OperationId} for user {UserId}",
                operation.Id,
                operation.UserId
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to notify UserGrain of operation start: {OperationId}",
                operation.Id
            );
            // Don't throw - grain notification failure shouldn't stop operation processing
        }
    }

    private async Task NotifyUserGrainOperationCompletedAsync(
        ChatOperation operation,
        IOrleansIntegrationService? orleansService,
        bool success,
        string? error = null
    )
    {
        if (orleansService == null)
        {
            return;
        }

        try
        {
            var userGrain = GetUserGrainFromOrleansService(orleansService, operation.UserId);
            await userGrain.NotifyOperationCompleted(operation.Id, success, error);

            _logger.LogDebug(
                "Notified UserGrain of operation completion: {OperationId} success={Success} for user {UserId}",
                operation.Id,
                success,
                operation.UserId
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to notify UserGrain of operation completion: {OperationId}",
                operation.Id
            );
            // Don't throw - grain notification failure shouldn't stop operation processing
        }
    }

    private async Task RelayMessageThroughUserGrainAsync(
        ChatOperation operation,
        ChatMessage chatMessage
    )
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var orleansService = scope.ServiceProvider.GetService<IOrleansIntegrationService>();

            if (orleansService == null)
            {
                return;
            }

            var userGrain = GetUserGrainFromOrleansService(orleansService, operation.UserId);
            await userGrain.RelayMessage(chatMessage);

            _logger.LogTrace(
                "Relayed message through UserGrain: {MessageId} for user {UserId}",
                chatMessage.Id,
                operation.UserId
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to relay message through UserGrain for operation {OperationId}",
                operation.Id
            );
            // Don't throw - grain relay failure shouldn't stop operation processing
        }
    }

    private async Task RelayStreamChunkThroughUserGrainAsync(
        ChatOperation operation,
        StreamChunk streamChunk
    )
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var orleansService = scope.ServiceProvider.GetService<IOrleansIntegrationService>();

            if (orleansService == null)
            {
                return;
            }

            var userGrain = GetUserGrainFromOrleansService(orleansService, operation.UserId);
            await userGrain.RelayStreamChunk(streamChunk);

            _logger.LogTrace(
                "Relayed stream chunk through UserGrain: operation {OperationId} chunk {ChunkIndex} for user {UserId}",
                operation.Id,
                streamChunk.ChunkIndex,
                operation.UserId
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to relay stream chunk through UserGrain for operation {OperationId}",
                operation.Id
            );
            // Don't throw - grain relay failure shouldn't stop operation processing
        }
    }

    #endregion UserGrain Integration

    #region Event Conversion Utilities

    private static ChatMessage? ConvertMessageEventToChatMessage(
        MessageEvent messageEvent,
        ChatOperation operation
    )
    {
        return messageEvent switch
        {
            TextEvent textEvent => new ChatMessage
            {
                Id = messageEvent.MessageId ?? Guid.NewGuid().ToString(),
                ChatId = operation.ChatId,
                UserId = operation.UserId,
                Content = textEvent.Text,
                Role = "assistant",
                Timestamp = DateTime.UtcNow,
                IsStreaming = false,
            },

            ReasoningEvent reasoningEvent => new ChatMessage
            {
                Id = messageEvent.MessageId ?? Guid.NewGuid().ToString(),
                ChatId = operation.ChatId,
                UserId = operation.UserId,
                Content = reasoningEvent.Reasoning,
                Role = "assistant",
                Timestamp = DateTime.UtcNow,
                IsStreaming = false,
                Metadata = System.Text.Json.JsonSerializer.Serialize(
                    new { Type = "reasoning", reasoningEvent.Visibility }
                ),
            },

            ToolCallEvent toolCallEvent => new ChatMessage
            {
                Id = messageEvent.MessageId ?? Guid.NewGuid().ToString(),
                ChatId = operation.ChatId,
                UserId = operation.UserId,
                Content = System.Text.Json.JsonSerializer.Serialize(toolCallEvent.ToolCalls),
                Role = "assistant",
                Timestamp = DateTime.UtcNow,
                IsStreaming = false,
                Metadata = System.Text.Json.JsonSerializer.Serialize(new { Type = "tool_call" }),
            },

            // Add more event type conversions as needed
            _ => null, // Skip unknown event types
        };
    }

    private static StreamChunk? ConvertStreamChunkEventToStreamChunk(
        StreamChunkEvent chunkEvent,
        ChatOperation operation,
        OperationState state
    )
    {
        var content = chunkEvent switch
        {
            TextStreamEvent textChunk => textChunk.Delta,
            ReasoningStreamEvent reasoningChunk => reasoningChunk.Delta,
            ToolCallStreamEvent toolCallChunk => toolCallChunk.Delta,
            MessageStreamCompleteEvent => "[COMPLETE]",
            _ => null,
        };

        return content == null
            ? null
            : new StreamChunk
            {
                OperationId = operation.Id,
                ChatId = operation.ChatId,
                Content = content,
                ChunkIndex = state.ChunkIndex++, // Increment chunk counter
                IsComplete = chunkEvent is MessageStreamCompleteEvent,
                Timestamp = DateTime.UtcNow,
                MessageId = chunkEvent.MessageId,
            };
    }

    #endregion Event Conversion Utilities

    #region Payload Extraction Utilities

    private string? ExtractMessageIdFromPayload(object? payload)
    {
        if (payload == null)
        {
            return null;
        }

        try
        {
            if (payload is string directString)
            {
                return directString;
            }

            var json = payload.ToString();
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
            if (jsonDoc.RootElement.TryGetProperty("messageId", out var messageIdElement))
            {
                return messageIdElement.GetString();
            }

            // Also try "id" property
            return jsonDoc.RootElement.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract message ID from payload: {Payload}", payload);
            return null;
        }
    }

    private EditMessagePayload? ExtractEditPayloadFromOperation(object? payload)
    {
        if (payload == null)
        {
            return null;
        }

        try
        {
            var json = payload.ToString();
            return string.IsNullOrEmpty(json)
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<EditMessagePayload>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to extract edit payload from operation: {Payload}",
                payload
            );
            return null;
        }
    }

    private static IUserGrain GetUserGrainFromOrleansService(
        IOrleansIntegrationService orleansService,
        string userId
    )
    {
        // Access the grain factory through reflection or implement a method in IOrleansIntegrationService
        // For now, we'll use a workaround to get the grain factory
        var serviceType = orleansService.GetType();
        var grainFactoryField = serviceType.GetField(
            "_grainFactory",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );

        return grainFactoryField?.GetValue(orleansService) is IGrainFactory grainFactory
            ? grainFactory.GetGrain<IUserGrain>(userId)
            : throw new InvalidOperationException(
                "Could not access grain factory from Orleans integration service"
            );
    }

    #endregion Payload Extraction Utilities
}

/// <summary>
/// Internal state tracking for an operation in progress.
/// </summary>
internal sealed class OperationState
{
    public ChatOperation Operation { get; set; } = null!;
    public ServerOperationStatus Status { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
    public double Progress { get; set; }
    public string? ProgressDescription { get; set; }
    /// <summary>
    /// Track chunk sequence for streaming
    /// </summary>
    public int ChunkIndex { get; set; }
}

/// <summary>
/// Payload structure for edit message operations.
/// </summary>
public class EditMessagePayload
{
    public string MessageId { get; set; } = string.Empty;
    public string NewContent { get; set; } = string.Empty;
}

/// <summary>
/// Configuration options for the BackgroundChatService.
/// </summary>
public class BackgroundServiceOptions
{
    /// <summary>
    /// Maximum number of concurrent operations.
    /// Default is 10 as specified in the design document.
    /// </summary>
    public int MaxConcurrentOperations { get; set; } = 10;

    /// <summary>
    /// Default timeout for operations in milliseconds.
    /// Default is 5 minutes.
    /// </summary>
    public int DefaultTimeoutMs { get; set; } = 300000;

    /// <summary>
    /// How long to keep completed operation history in hours.
    /// Default is 2 hours.
    /// </summary>
    public int OperationHistoryHours { get; set; } = 2;
}
