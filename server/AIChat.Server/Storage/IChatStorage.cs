namespace AIChat.Server.Storage;

public interface IChatStorage
{
    /// <summary>
    /// Chats
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="title"></param>
    /// <param name="createdAtUtc"></param>
    /// <param name="updatedAtUtc"></param>
    /// <param name="chatJson"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<(bool Success, string? Error, ChatRecord? Chat)> CreateChatAsync(
        string userId,
        string title,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        string? chatJson,
        CancellationToken ct = default
    );

    Task<(bool Success, string? Error, ChatRecord? Chat)> GetChatByIdAsync(
        string chatId,
        CancellationToken ct = default
    );

    Task<(
        bool Success,
        string? Error,
        IReadOnlyList<ChatRecord> Chats,
        int TotalCount
    )> GetChatHistoryByUserAsync(
        string userId,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<(bool Success, string? Error)> DeleteChatAsync(
        string chatId,
        CancellationToken ct = default
    );

    Task<(bool Success, string? Error)> UpdateChatUpdatedAtAsync(
        string chatId,
        DateTime updatedAtUtc,
        CancellationToken ct = default
    );

    /// <summary>
    /// Messages
    /// </summary>
    /// <param name="chatId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<(bool Success, string? Error, int NextSequence)> AllocateSequenceAsync(
        string chatId,
        CancellationToken ct = default
    );

    Task<(bool Success, string? Error, MessageRecord? Message)> InsertMessageAsync(
        MessageRecord message,
        CancellationToken ct = default
    );

    Task<(
        bool Success,
        string? Error,
        IReadOnlyList<MessageRecord> Messages
    )> ListChatMessagesOrderedAsync(string chatId, CancellationToken ct = default);

    Task<(bool Success, string? Error, MessageRecord? Message)> GetMessageByIdAsync(
        string messageId,
        CancellationToken ct = default
    );

    Task<(bool Success, string? Error, string? Content)> GetMessageContentAsync(
        string messageId,
        CancellationToken ct = default
    );

    Task<(bool Success, string? Error)> UpdateMessageJsonAsync(
        string messageId,
        string newMessageJson,
        CancellationToken ct = default
    );
}

public sealed class ChatRecord
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public required string Title { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
    public string? ChatJson { get; init; }
}

public sealed class MessageRecord
{
    public required string Id { get; init; }
    public required string ChatId { get; init; }
    /// <summary>
    /// user|assistant|tool|system
    /// </summary>
    public required string Role { get; init; }
    /// <summary>
    /// text|reasoning|toolcall|toolresult (extensible)
    /// </summary>
    public required string Kind { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public required int SequenceNumber { get; init; }
    public required string MessageJson { get; init; }
}
